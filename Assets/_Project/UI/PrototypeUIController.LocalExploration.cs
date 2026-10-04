using System.Collections.Generic;
using KingdomSurvival.BattlefieldDatabase;
using KingdomSurvival.BattleSandbox;
using KingdomSurvival.DialogueDatabase;
using UnityEngine;
using UnityEngine.UIElements;

// ПР-12К (канон v1.53 §28.3): исследуемое место на локальной карте.
// Командир ходит кликом, видимые спутники идут следом; объекты открывают
// существующие диалоги; шаг в зону угрозы начинает бой здесь же, на том же
// поле (BattleSandboxController.HostLocalBattle), после боя исследование
// продолжается. Состояние — в GameState.LocalExploration (Core); здесь
// только показ и команды. Время идёт шагами и действиями через общие часы.
//
// Места без локальной карты по-прежнему открывают Location Interaction;
// место с локальной картой не показывает текстовый вход.
public partial class PrototypeUIController
{
    private VisualElement localScreen;
    private VisualElement localField;
    private VisualElement localHud;
    private VisualElement localPartyList;
    private Label localTitleLabel;
    private Label localTimeLabel;
    private Label localArtNoteLabel;
    private Label localNoticeLabel;
    private Button localExitButton;
    private bool localUiBound;

    private GameState localBoundState;
    private LocalLocationDefinition localDefinition;
    private LocalLocationGeometry localGeometry;
    private LocalExplorationView localView;
    private VisualElement localBattleHost;
    private LocalPartyMover localMover;
    private BattleSandboxController localBattle;

    private string localPendingObjectId;
    private bool localPendingExit;
    private LocalEncounterDefinition localPendingEncounter;
    private bool localBattleRequested;
    private float localNoticeUntil;
    private string localPartySignature;
    // Номер показа места: колбэки прежнего показа (бой, загрузка) не действуют.
    private int localGeneration;

    private bool IsLocalScreenOpen => localView != null;
    private bool IsLocalBattleRunning => localBattle != null;

    // ------------------------------------------------------------------
    // Вход
    // ------------------------------------------------------------------

    // Вызывается из единой точки входа в место (TryOpenLocationInteraction).
    // True — у места есть локальная карта и отряд вошёл.
    private bool TryEnterLocalExploration(LocationData location)
    {
        if (gameState == null || location == null)
            return false;
        LocalLocationDefinition definition = LocalLocationCatalog.ForWorldLocation(gameState, location.Id);
        if (definition == null)
            return false;

        BattlefieldDatabaseAsset battlefields = Resources.Load<BattlefieldDatabaseAsset>(BattlefieldDatabaseAsset.ResourcesPath);
        DialogueDatabaseAsset dialogues = DialogueDatabaseRuntime.LoadDefaultDatabase();
        List<string> errors = LocalLocationValidator.Validate(definition, battlefields,
            id => dialogues != null && dialogues.FindDialogue(id) != null);
        if (errors.Count > 0)
        {
            Debug.LogError("Исследуемое место «" + definition.DisplayName + "» с ошибками данных:\n" + string.Join("\n", errors));
            return false;
        }

        if (!LocalExplorationService.Enter(gameState, definition, null, out string reason))
        {
            AddReport(reason);
            return false;
        }
        OpenLocalScreen();
        AddReport("Отряд вошёл: " + definition.DisplayName + ".");
        Autosave();
        return true;
    }

    // ------------------------------------------------------------------
    // Кадр: открыть/закрыть экран по состоянию, движение, время, зоны
    // ------------------------------------------------------------------

    private void TickLocalExploration(float deltaSeconds)
    {
        BindLocalExplorationUi();
        if (!localUiBound)
            return;

        bool shouldShow = gameState != null && !isGameOver && LocalExplorationService.IsActive(gameState);
        if (IsLocalScreenOpen && (localBoundState != gameState || !shouldShow))
            CloseLocalScreen();
        if (shouldShow && !IsLocalScreenOpen)
        {
            // Загрузка или возврат: место могло исчезнуть — тогда отряд у входа.
            LocalExplorationService.NormalizeAfterLoad(gameState);
            if (!string.IsNullOrEmpty(gameState.LocalExploration.PendingNotice))
            {
                AddReport(gameState.LocalExploration.PendingNotice);
                gameState.LocalExploration.PendingNotice = string.Empty;
            }
            if (!LocalExplorationService.IsActive(gameState))
                return;
            OpenLocalScreen();
        }
        if (!IsLocalScreenOpen || IsLocalBattleRunning)
            return;

        bool blocked = HasBlockingModalWork() || IsNarrativeDialogueActive;
        if (!blocked)
        {
            if (localBattleRequested && localPendingEncounter != null)
            {
                LocalEncounterDefinition encounter = localPendingEncounter;
                localBattleRequested = false;
                localPendingEncounter = null;
                StartLocalBattle(encounter);
                return;
            }

            foreach (HexCoord cell in localMover.Tick(Mathf.Min(deltaSeconds, 0.1f)))
            {
                OnLocalLeaderEnteredCell(cell);
                if (localPendingEncounter != null || IsLocalBattleRunning || !IsLocalScreenOpen)
                    return;
            }
            StoreLocalPositions();
            TryCompleteLocalPendingAction();
        }

        RenderLocalExploration();
    }

    private void OnLocalLeaderEnteredCell(HexCoord cell)
    {
        // Время места: шаг стоит своих минут (трудная клетка — дороже).
        double hours = localDefinition.HoursPerCell * localGeometry.StepCost(cell);
        ContinuousSimulationBatch batch = ContinuousSimulationSystem.AdvanceLocalHours(gameState, hours);
        if (batch.HasReportableContent)
            ProcessContinuousSimulationBatch(batch);

        LocalEncounterDefinition encounter = LocalExplorationService.EncounterAt(gameState, localDefinition, cell.Q, cell.R);
        if (encounter == null)
            return;

        // Зона угрозы: движение и следование останавливаются, позиции
        // фиксируются; бой — один раз, после вступительной реплики.
        localMover.Stop();
        localPendingObjectId = null;
        localPendingExit = false;
        localPendingEncounter = encounter;
        StoreLocalPositions();
        if (string.IsNullOrEmpty(encounter.IntroDialogueId) || !TryOpenNarrativeDialogueById(encounter.IntroDialogueId))
            localBattleRequested = true;
    }

    // Диалог места закончился (OnStoryDialogueCompleted).
    private void OnLocalDialogueCompleted(string dialogueId)
    {
        if (!IsLocalScreenOpen || localPendingEncounter == null)
            return;
        if (dialogueId == localPendingEncounter.IntroDialogueId)
            localBattleRequested = true;
    }

    // ------------------------------------------------------------------
    // Команды игрока
    // ------------------------------------------------------------------

    private bool LocalCommandsBlocked =>
        !IsLocalScreenOpen || IsLocalBattleRunning || localPendingEncounter != null ||
        HasBlockingModalWork() || IsNarrativeDialogueActive;

    private void OnLocalCellClicked(HexCoord cell)
    {
        if (LocalCommandsBlocked)
            return;
        localPendingObjectId = null;
        LocalEntranceDefinition entrance = localDefinition.FindEntrance(gameState.LocalExploration.EntranceId);
        localPendingExit = entrance != null && entrance.Cell.Is(cell.Q, cell.R);
        if (!localMover.MoveLeaderTo(cell))
        {
            ShowLocalNotice("Туда не пройти.");
            return;
        }
        localView.ShowClickMarker(cell);
    }

    private void OnLocalObjectClicked(string objectId)
    {
        if (LocalCommandsBlocked)
            return;
        LocalObjectDefinition item = localDefinition.FindObject(objectId);
        if (item == null)
            return;
        if (!LocalExplorationService.IsObjectAvailable(gameState, localDefinition, item))
        {
            ShowLocalNotice(item.Label + ": здесь уже всё осмотрено.");
            return;
        }

        localPendingExit = false;
        HexCoord leader = localMover.Leader.SettledCell;
        List<HexCoord> spots = localGeometry.InteractionCells(item);
        if (spots.Contains(leader) && !localMover.LeaderHasOrder)
        {
            PerformLocalObject(item);
            return;
        }

        HexCoord? best = null;
        int bestLength = int.MaxValue;
        foreach (HexCoord spot in spots)
        {
            List<HexCoord> path = localGeometry.FindPath(leader, spot);
            if (path.Count > 0 && path.Count < bestLength)
            {
                best = spot;
                bestLength = path.Count;
            }
        }
        if (best == null || !localMover.MoveLeaderTo(best.Value))
        {
            ShowLocalNotice("К этому не подойти.");
            return;
        }
        localPendingObjectId = item.Id;
        localView.ShowClickMarker(best.Value);
    }

    private void OnLocalExitClicked()
    {
        if (LocalCommandsBlocked)
            return;
        LocalEntranceDefinition entrance = localDefinition.FindEntrance(gameState.LocalExploration.EntranceId);
        if (entrance == null)
            return;
        localPendingObjectId = null;
        HexCoord cell = LocalLocationGeometry.Cell(entrance.Cell);
        if (localMover.Leader.Cell == cell && !localMover.LeaderHasOrder)
        {
            localPendingExit = true;
            return;
        }
        if (!localMover.MoveLeaderTo(cell))
        {
            ShowLocalNotice("К выходу не пройти.");
            return;
        }
        localPendingExit = true;
        localView.ShowClickMarker(cell);
    }

    private void TryCompleteLocalPendingAction()
    {
        if (localMover.LeaderHasOrder)
            return;

        if (!string.IsNullOrEmpty(localPendingObjectId))
        {
            LocalObjectDefinition item = localDefinition.FindObject(localPendingObjectId);
            localPendingObjectId = null;
            if (item != null && localGeometry.InteractionCells(item).Contains(localMover.Leader.Cell))
                PerformLocalObject(item);
            return;
        }

        if (localPendingExit)
        {
            LocalEntranceDefinition entrance = localDefinition.FindEntrance(gameState.LocalExploration.EntranceId);
            if (entrance == null || !entrance.Cell.Is(localMover.Leader.Cell.Q, localMover.Leader.Cell.R))
            {
                localPendingExit = false;
                return;
            }
            // Перед выходом группа собирается физически.
            if (!localMover.IsGathered)
            {
                ShowLocalNotice("Отряд собирается у выхода…");
                return;
            }
            localPendingExit = false;
            LeaveLocalExploration();
        }
    }

    private void PerformLocalObject(LocalObjectDefinition item)
    {
        if (!LocalExplorationService.IsObjectAvailable(gameState, localDefinition, item))
            return;
        if (item.Kind == LocalObjectKind.Dialogue)
        {
            if (!TryOpenNarrativeDialogueById(item.DialogueId))
            {
                ShowLocalNotice("Сейчас не получится.");
                return;
            }
        }
        else
        {
            ShowLocalNotice(item.Text);
            AddReport(item.Label + ": " + item.Text);
        }
        LocalExplorationService.MarkInteraction(gameState, localDefinition, item);
        ContinuousSimulationBatch batch = ContinuousSimulationSystem.AdvanceLocalHours(gameState, localDefinition.HoursPerInteraction);
        if (batch.HasReportableContent)
            ProcessContinuousSimulationBatch(batch);
        RefreshLocalObjects();
    }

    private void LeaveLocalExploration()
    {
        string name = localDefinition.DisplayName;
        StoreLocalPositions();
        LocalExplorationService.Exit(gameState);
        CloseLocalScreen();
        AddReport("Отряд вышел: " + name + ".");
        RefreshInterface();
        Autosave();
    }

    // ------------------------------------------------------------------
    // Бой на месте
    // ------------------------------------------------------------------

    private void StartLocalBattle(LocalEncounterDefinition encounter)
    {
        if (!LocalExplorationService.IsEncounterActive(gameState, localDefinition, encounter))
            return;

        // Позиции: каждый боец — на ближайшую свободную клетку со своей
        // стороны стены; клетки противников заняты.
        HashSet<HexCoord> reserved = new HashSet<HexCoord>();
        foreach (LocalActorStateData enemy in LocalExplorationService.AliveEnemies(gameState, localDefinition))
            reserved.Add(new HexCoord(enemy.Q, enemy.R));
        List<string> candidates = PartyPresence.BattleCandidateIds(gameState);
        List<KeyValuePair<string, HexCoord>> preferred = new List<KeyValuePair<string, HexCoord>>();
        foreach (LocalPartyMover.Member member in localMover.Members)
        {
            if (candidates.Contains(member.Id))
                preferred.Add(new KeyValuePair<string, HexCoord>(member.Id, member.SettledCell));
        }
        if (!SandboxLocalNavigation.TryAssignCells(preferred, localGeometry.IsPassable, reserved, out Dictionary<string, HexCoord> assigned))
        {
            Debug.LogError("Бой на месте: не хватило клеток для отряда — бой не начат.");
            ShowLocalNotice("Здесь не развернуться для боя.");
            return;
        }

        // ПР-04: предбоевая точка возврата — до сборки запроса.
        Autosave();

        Dictionary<string, LocalCellData> cells = new Dictionary<string, LocalCellData>();
        foreach (KeyValuePair<string, HexCoord> entry in assigned)
            cells[entry.Key] = new LocalCellData(entry.Value.Q, entry.Value.R);
        CampaignBattleRequest request = LocalExplorationService.BuildEncounterRequest(gameState, localDefinition, encounter, cells);

        // Небоевые остаются в безопасной точке — не исчезают без объяснения.
        foreach (LocalPartyMover.Member member in localMover.Members)
        {
            if (candidates.Contains(member.Id))
                continue;
            ResidentState resident = HomePeopleService.Find(gameState, member.Id);
            string who = resident != null ? resident.DisplayName : member.Id;
            request.Notes.Add(who + " держится у выхода: " +
                              PartyPresence.BattleExclusionReason(gameState, member.Id).ToLowerInvariant() + ".");
            LocalExplorationService.StorePartyPosition(gameState, member.Id, encounter.RetreatCell.Q, encounter.RetreatCell.R, member.Facing);
        }

        localBattleHost = new VisualElement { name = "local-battle-host" };
        localBattleHost.style.position = Position.Absolute;
        localBattleHost.style.left = 0f;
        localBattleHost.style.right = 0f;
        localBattleHost.style.top = 0f;
        localBattleHost.style.bottom = 0f;
        localField.Add(localBattleHost);

        int generation = localGeneration;
        localBattle = BattleSandboxController.HostLocalBattle(localBattleHost, request,
            result => OnLocalBattleFinished(result, generation), out string error);
        if (localBattle == null)
        {
            localBattleHost.RemoveFromHierarchy();
            localBattleHost = null;
            AddReport("Бой не начался: " + error);
            return;
        }

        CampaignSession.EnterBattle(request);
        localView.style.display = DisplayStyle.None;
        localHud.style.display = DisplayStyle.None;
    }

    private void OnLocalBattleFinished(CampaignBattleResult result, int generation)
    {
        localBattle = null;
        localBattleHost?.RemoveFromHierarchy();
        localBattleHost = null;
        // Прежний показ (загрузка, новая партия) — итог не применяется.
        if (generation != localGeneration || gameState == null || localBoundState != gameState)
            return;

        CampaignSession.CompleteBattle(result);
        if (!ApplyReturnedCampaignBattle())
        {
            CloseLocalScreen();
            return;
        }

        // Исследование продолжается на том же месте: выжившие там, где
        // закончили бой; при отходе — у безопасной точки; павших нет.
        localView.style.display = DisplayStyle.Flex;
        localHud.style.display = DisplayStyle.Flex;
        RebuildLocalMover();
        RefreshLocalObjects();
        RenderLocalExploration();
    }

    // ------------------------------------------------------------------
    // Экран
    // ------------------------------------------------------------------

    private void BindLocalExplorationUi()
    {
        if (localUiBound || interfaceRoot == null)
            return;
        localScreen = interfaceRoot.Q<VisualElement>("local-exploration-screen");
        if (localScreen == null)
            return;
        localField = localScreen.Q<VisualElement>("local-exploration-field");
        localHud = localScreen.Q<VisualElement>("local-exploration-hud");
        localPartyList = localScreen.Q<VisualElement>("local-exploration-party-list");
        localTitleLabel = localScreen.Q<Label>("local-exploration-title");
        localTimeLabel = localScreen.Q<Label>("local-exploration-time");
        localArtNoteLabel = localScreen.Q<Label>("local-exploration-art-note");
        localNoticeLabel = localScreen.Q<Label>("local-exploration-notice");
        localExitButton = localScreen.Q<Button>("local-exploration-exit-button");
        if (localExitButton != null)
            localExitButton.clicked += OnLocalExitClicked;
        localUiBound = localField != null && localHud != null;
    }

    private void OpenLocalScreen()
    {
        CloseLocalScreen();
        localDefinition = LocalExplorationService.ActiveDefinition(gameState);
        BattlefieldDatabaseAsset battlefields = Resources.Load<BattlefieldDatabaseAsset>(BattlefieldDatabaseAsset.ResourcesPath);
        BattlefieldDefinitionData field = battlefields != null && localDefinition != null
            ? battlefields.FindById(localDefinition.BattlefieldId)
            : null;
        if (localDefinition == null || field == null)
        {
            LocalExplorationService.Exit(gameState);
            AddReport("Место недоступно — отряд снаружи, у входа.");
            return;
        }

        localGeneration++;
        localBoundState = gameState;
        localGeometry = new LocalLocationGeometry(localDefinition, field);
        localView = new LocalExplorationView(field, battlefields.GetHexStyle(field));
        localView.CellClicked += OnLocalCellClicked;
        localView.ObjectClicked += OnLocalObjectClicked;
        localField.Clear();
        localField.Add(localView);
        localHud.style.display = DisplayStyle.Flex;
        localScreen.style.display = DisplayStyle.Flex;
        localScreen.BringToFront();
        narrativeDialogueOverlay?.BringToFront();

        localPendingObjectId = null;
        localPendingExit = false;
        localPendingEncounter = null;
        localBattleRequested = false;
        localPartySignature = null;
        if (localTitleLabel != null)
            localTitleLabel.text = localDefinition.DisplayName.ToUpperInvariant();
        if (localArtNoteLabel != null)
            localArtNoteLabel.text = localDefinition.PlaceholderArt ? "временный фон — рисунка ещё нет" : string.Empty;
        ShowLocalNotice(string.Empty);

        RebuildLocalMover();
        RefreshLocalObjects();
        ContinuousSimulationSystem.SetPaused(gameState, true);
        RenderLocalExploration();
    }

    private void CloseLocalScreen()
    {
        localGeneration++;
        if (localBattle != null)
        {
            localBattle.DisposeHosted();
            localBattle = null;
        }
        localBattleHost = null;
        localField?.Clear();
        localView = null;
        localMover = null;
        localDefinition = null;
        localGeometry = null;
        localBoundState = null;
        localPendingObjectId = null;
        localPendingExit = false;
        localPendingEncounter = null;
        localBattleRequested = false;
        if (localScreen != null)
            localScreen.style.display = DisplayStyle.None;
    }

    // Отряд места из состояния: командир первым, затем бойцы и свита;
    // каждому — своя проходимая клетка без наложения.
    private void RebuildLocalMover()
    {
        LocalExplorationStateData data = gameState.LocalExploration;
        List<string> present = PartyPresence.PresentIds(gameState);
        HashSet<HexCoord> taken = new HashSet<HexCoord>();
        foreach (LocalActorStateData enemy in LocalExplorationService.AliveEnemies(gameState, localDefinition))
            taken.Add(new HexCoord(enemy.Q, enemy.R));

        LocalEntranceDefinition entrance = localDefinition.FindEntrance(data.EntranceId);
        HexCoord fallback = entrance != null ? LocalLocationGeometry.Cell(entrance.Cell) : new HexCoord(0, 3);
        List<KeyValuePair<string, HexCoord>> members = new List<KeyValuePair<string, HexCoord>>();
        List<int> facings = new List<int>();
        foreach (string personId in present)
        {
            LocalActorStateData stored = data.Party.Find(actor => actor.ActorId == personId);
            HexCoord preferred = stored != null ? new HexCoord(stored.Q, stored.R) : fallback;
            if (!localGeometry.IsPassable(preferred))
                preferred = fallback;
            HexCoord? cell = SandboxLocalNavigation.NearestFree(preferred, localGeometry.IsPassable, taken);
            if (cell == null)
                continue;
            taken.Add(cell.Value);
            members.Add(new KeyValuePair<string, HexCoord>(personId, cell.Value));
            facings.Add(stored != null ? stored.Facing : 0);
            LocalExplorationService.StorePartyPosition(gameState, personId, cell.Value.Q, cell.Value.R, facings[facings.Count - 1]);
        }
        data.Party.RemoveAll(actor => !present.Contains(actor.ActorId));
        localMover = new LocalPartyMover(localGeometry.IsPassable, localGeometry.StepCost, members, facings);
    }

    private void StoreLocalPositions()
    {
        if (localMover == null)
            return;
        foreach (LocalPartyMover.Member member in localMover.Members)
            LocalExplorationService.StorePartyPosition(gameState, member.Id, member.SettledCell.Q, member.SettledCell.R, member.Facing);
    }

    private void RefreshLocalObjects()
    {
        if (localView == null)
            return;
        foreach (LocalObjectDefinition item in localDefinition.Objects)
        {
            bool required = string.IsNullOrEmpty(item.RequiresFlag) ||
                            (gameState.Narrative != null && gameState.Narrative.HasFlag(item.RequiresFlag));
            bool active = LocalExplorationService.IsObjectAvailable(gameState, localDefinition, item);
            localView.SetObject(item.Id, LocalLocationGeometry.Cell(item.Cell), active ? item.Label : item.Label, active, required);
        }
    }

    private void RenderLocalExploration()
    {
        if (localView == null || localMover == null)
            return;

        List<LocalExplorationView.ActorFrame> frames = new List<LocalExplorationView.ActorFrame>();
        CommanderData hero = gameState.GetSelectedCommander();
        List<string> retinue = gameState.ActiveExpedition?.RetinueIds ?? new List<string>();
        foreach (LocalPartyMover.Member member in localMover.Members)
        {
            ResidentState resident = HomePeopleService.Find(gameState, member.Id);
            string unitType = hero != null && member.Id == hero.Id
                ? (string.IsNullOrWhiteSpace(hero.UnitTypeId) ? CampaignBattleBridge.HeroFallbackUnitTypeId : hero.UnitTypeId)
                : FindFighterUnitType(member.Id) ?? resident?.UnitTypeId;
            string name = resident != null ? resident.DisplayName : member.Id;
            frames.Add(new LocalExplorationView.ActorFrame
            {
                Id = member.Id,
                UnitTypeId = unitType ?? string.Empty,
                TokenText = string.IsNullOrEmpty(name) ? "?" : name.Substring(0, 1),
                Kind = retinue.Contains(member.Id) ? LocalExplorationView.ActorKind.Retinue : LocalExplorationView.ActorKind.Fighter,
                From = member.Cell,
                To = member.Stepping ? member.Next : member.Cell,
                Progress = member.Progress,
                Facing = (KingdomSurvival.AnimationDatabase.HexFacing)member.Facing,
                Walking = member.Stepping,
                Wounded = resident != null && resident.Injury == ResidentInjury.Recovering
            });
        }
        foreach (LocalActorStateData enemy in LocalExplorationService.AliveEnemies(gameState, localDefinition))
        {
            LocalEnemyDefinition definition = localDefinition.FindEnemy(enemy.ActorId);
            HexCoord cell = new HexCoord(enemy.Q, enemy.R);
            frames.Add(new LocalExplorationView.ActorFrame
            {
                Id = enemy.ActorId,
                UnitTypeId = definition.UnitTypeId,
                TokenText = "З",
                Kind = LocalExplorationView.ActorKind.Enemy,
                From = cell,
                To = cell,
                Facing = KingdomSurvival.AnimationDatabase.HexFacing.West
            });
        }
        localView.Render(frames);

        if (localTimeLabel != null)
        {
            ContinuousClockSnapshot clock = ContinuousSimulationSystem.GetClock(gameState);
            localTimeLabel.text = "День " + clock.Day + " · " + ContinuousSimulationSystem.FormatClock(clock.HourOfDay);
        }
        if (localMover.IsWaitingForStragglers)
            ShowLocalNotice("Отряд отстаёт — командир ждёт.");
        if (localNoticeLabel != null && Time.realtimeSinceStartup > localNoticeUntil)
            localNoticeLabel.text = string.Empty;
        RefreshLocalPartyList();
    }

    private string FindFighterUnitType(string personId)
    {
        if (gameState.Fighters == null)
            return null;
        foreach (FighterData fighter in gameState.Fighters)
        {
            if (fighter != null && fighter.Id == personId && !string.IsNullOrWhiteSpace(fighter.UnitTypeId))
                return fighter.UnitTypeId;
        }
        return null;
    }

    // «С вами», «Свита · с вами», «Ранен — не сражается», «В лагере».
    private void RefreshLocalPartyList()
    {
        if (localPartyList == null)
            return;
        List<string> rows = new List<string>();
        foreach (string personId in PartyPresence.ExpeditionIds(gameState))
        {
            ResidentState resident = HomePeopleService.Find(gameState, personId);
            if (resident != null && !resident.IsAlive)
                continue;
            string name = resident != null ? resident.DisplayName : personId;
            CommanderData hero = gameState.GetSelectedCommander();
            if (hero != null && personId == hero.Id)
                name = hero.Name;
            string status = PartyStatusLabel(personId);
            rows.Add(name + "|" + status);
        }
        string signature = string.Join(";", rows);
        if (signature == localPartySignature)
            return;
        localPartySignature = signature;
        localPartyList.Clear();
        foreach (string row in rows)
        {
            string[] parts = row.Split('|');
            VisualElement line = new VisualElement();
            line.AddToClassList("local-party-row");
            Label name = new Label(parts[0]);
            name.AddToClassList("local-party-name");
            Label status = new Label(parts[1]);
            status.AddToClassList("local-party-status");
            status.EnableInClassList("local-party-status--wounded", parts[1] == PartyPresence.ReasonWounded);
            status.EnableInClassList("local-party-status--away", parts[1] == PartyPresence.ReasonInCamp);
            line.Add(name);
            line.Add(status);
            localPartyList.Add(line);
        }
    }

    private string PartyStatusLabel(string personId)
    {
        if (PartyPresence.IsWaitingInCamp(gameState, personId))
            return PartyPresence.ReasonInCamp;
        string reason = PartyPresence.BattleExclusionReason(gameState, personId);
        if (reason == PartyPresence.ReasonWounded)
            return reason;
        if (reason == PartyPresence.ReasonRetinue)
            return "Свита · с вами";
        return "С вами";
    }

    private void ShowLocalNotice(string text)
    {
        if (localNoticeLabel == null)
            return;
        localNoticeLabel.text = text ?? string.Empty;
        localNoticeUntil = Time.realtimeSinceStartup + 3f;
    }
}

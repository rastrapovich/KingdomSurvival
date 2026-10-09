using System.Collections.Generic;
using KingdomSurvival.AnimationDatabase;
using KingdomSurvival.BattlefieldDatabase;
using KingdomSurvival.BattleSandbox;
using KingdomSurvival.DialogueDatabase;
using KingdomSurvival.LocationRendering;
using UnityEngine;
using UnityEngine.UIElements;

// ПР-12К (канон v1.54 §28.3): исследуемое место на локальной карте.
// Движение — то же, что на глобальной карте: клик ведёт командира по
// разметке местности с обходом, зажатая кнопка — за курсором, спутники идут
// следом по его следу; клеток нет. Рисунок места рисует общий рендерер Базы
// локаций (LocationWorldRenderer) в текстуру экрана. Объекты открывают
// существующие диалоги; вход в зону угрозы начинает бой здесь же: кадр поля
// из Базы полей боя ложится на рисунок, сетка — ровно настройки этого поля,
// стены боя — непроходимое под клетками. После боя исследование
// продолжается. Состояние — в GameState.LocalExploration (Core); здесь
// только показ и команды. Время идёт пройденным путём и действиями.
//
// Места без локальной карты по-прежнему открывают Location Interaction;
// место с локальной картой не показывает текстовый вход.
public partial class PrototypeUIController
{
    // Командир у входа — можно выходить (пиксели рисунка).
    private const float LocalExitRadius = 56f;
    // Мелкие отрезки времени копятся до минуты, чтобы не дёргать симуляцию.
    private const double LocalMinHoursStep = 1.0 / 60.0;

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
    private LocationWorldRenderer localRenderer;
    private LocalLocationGeometry localGeometry;
    private LocalFreeMover localMover;
    private Image localImage;
    private VisualElement localOverlay;
    private VisualElement localClickMarker;
    private readonly Dictionary<string, Label> localObjectLabels = new Dictionary<string, Label>();
    private VisualElement localBattleHost;
    private BattleSandboxController localBattle;
    private Vector2 localArenaCenter;

    private string localPendingObjectId;
    private bool localPendingExit;
    private LocalEncounterDefinition localPendingEncounter;
    private bool localBattleRequested;
    private float localNoticeUntil;
    private float localClickMarkerUntil;
    private Vector2 localClickMarkerPoint;
    private string localPartySignature;
    private double localPendingHours;
    private float localZoom = 1f;
    private bool localHolding;
    private Vector2 localHeldPoint;
    // Номер показа места: колбэки прежнего показа (бой, загрузка) не действуют.
    private int localGeneration;
    // ПР-12О: камера следует за командиром; при входе, загрузке и смене
    // командира кадр ставится сразу (без пролёта от прежней карты).
    private bool localCameraSnap;
    private string localFollowedLeaderId;
    // Высота земли под ногами командира (диагностика и будущие системы).
    private HeightSample localLeaderHeight;

    private bool IsLocalScreenOpen => localRenderer != null;
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
            id => dialogues != null && dialogues.FindDialogue(id) != null, null,
            LocationVisualGeometry.BlockedAreas(FindLocalVisual(definition), definition));
        // Земля из участков: неполный экспорт и пропуски Color в игру не идут.
        errors.AddRange(LocationGroundLayout.RuntimeErrors(FindLocalVisual(definition), definition));
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

    private static LocationVisualDefinition FindLocalVisual(LocalLocationDefinition definition)
    {
        LocalLocationDatabaseAsset database = Resources.Load<LocalLocationDatabaseAsset>(LocalLocationDatabaseAsset.ResourcesPath);
        return database != null && definition != null ? database.FindVisual(definition.Id) : null;
    }

    // ------------------------------------------------------------------
    // Кадр: открыть/закрыть экран по состоянию, движение, время, зоны
    // ------------------------------------------------------------------

    private void TickLocalExploration(float deltaSeconds)
    {
        // ПР-12К: лагерь у поселения — без забытых и погибших в ожидающих;
        // ушедший от поселения отряд уходит вместе.
        if (gameState != null && !isGameOver)
        {
            foreach (string report in SettlementCampService.Normalize(gameState))
                AddReport(report);
        }

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
        if (!IsLocalScreenOpen)
            return;

        HandleLocalCameraHotkeys();
        if (IsLocalBattleRunning)
        {
            AlignLocalBattleCamera();
            SyncLocalBattleFigures();
            RenderLocalWorld();
            return;
        }

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

            localMover.Tick(Mathf.Min(deltaSeconds, 0.1f), out double hours);
            AddLocalHours(hours, false);
            if (!IsLocalScreenOpen)
                return;
            CheckLocalEncounter();
            if (localPendingEncounter != null || IsLocalBattleRunning || !IsLocalScreenOpen)
                return;
            StoreLocalPositions();
            TryCompleteLocalPendingAction();
            if (!IsLocalScreenOpen)
                return;
        }

        RenderLocalExploration();
    }

    // Время места — пройденным путём, как на глобальной карте (свои числа у
    // каждого места): мелкие отрезки копятся до минуты.
    private void AddLocalHours(double hours, bool flush)
    {
        localPendingHours += hours;
        if (localPendingHours <= 0 || (!flush && localPendingHours < LocalMinHoursStep))
            return;
        double advance = localPendingHours;
        localPendingHours = 0;
        ContinuousSimulationBatch batch = ContinuousSimulationSystem.AdvanceLocalHours(gameState, advance);
        if (batch.HasReportableContent)
            ProcessContinuousSimulationBatch(batch);
    }

    private void CheckLocalEncounter()
    {
        LocalFreeMover.Member leader = localMover.Leader;
        LocalEncounterDefinition encounter = LocalExplorationService.EncounterAt(gameState, localDefinition, leader.X, leader.Y);
        if (encounter == null)
            return;

        // Зона угрозы: движение и следование останавливаются, позиции
        // фиксируются; бой — один раз, после вступительной реплики.
        localMover.Stop();
        localHolding = false;
        AddLocalHours(0, true);
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

    private bool TryLocalPointAt(Vector2 localPosition, out Vector2 point)
    {
        point = default;
        Rect bounds = localImage.contentRect;
        if (bounds.width < 1f || bounds.height < 1f)
            return false;
        point = localRenderer.ViewportToPixel(new Vector2(localPosition.x / bounds.width, localPosition.y / bounds.height));
        return point.x >= 0 && point.y >= 0 && point.x <= localDefinition.CanvasWidth && point.y <= localDefinition.CanvasHeight;
    }

    private void OnLocalPointerDown(PointerDownEvent evt)
    {
        if (evt.button != 0 || !TryLocalPointAt(evt.localPosition, out Vector2 point))
            return;
        OnLocalGroundClicked(point);
        localHolding = !LocalCommandsBlocked;
        localHeldPoint = point;
        if (localHolding)
            localImage.CapturePointer(evt.pointerId);
    }

    // Зажатая кнопка: командир идёт за курсором; в стену — просто не меняем цель.
    private void OnLocalPointerMove(PointerMoveEvent evt)
    {
        if (!localHolding)
            return;
        if ((evt.pressedButtons & 1) == 0)
        {
            StopLocalHolding(evt.pointerId);
            return;
        }
        if (TryLocalPointAt(evt.localPosition, out Vector2 point) && (point - localHeldPoint).sqrMagnitude >= 64f)
            OnLocalHeldAt(point);
    }

    private void OnLocalHeldAt(Vector2 point)
    {
        if (LocalCommandsBlocked || !localMover.IsPassable(point.x, point.y))
            return;
        localHeldPoint = point;
        localPendingObjectId = null;
        localPendingExit = IsAtLocalEntrance(point);
        localMover.MoveLeaderTo(point.x, point.y);
    }

    private void OnLocalPointerUp(PointerUpEvent evt) => StopLocalHolding(evt.pointerId);

    private void StopLocalHolding(int pointerId)
    {
        localHolding = false;
        if (localImage != null && localImage.HasPointerCapture(pointerId))
            localImage.ReleasePointer(pointerId);
    }

    private void OnLocalGroundClicked(Vector2 point)
    {
        if (LocalCommandsBlocked)
            return;
        // Клик по объекту (рядом с его точкой) — подойти и действовать.
        LocalObjectDefinition item = LocalObjectNear(point);
        if (item != null)
        {
            OnLocalObjectClicked(item.Id);
            return;
        }
        localPendingObjectId = null;
        localPendingExit = IsAtLocalEntrance(point);
        if (!localMover.MoveLeaderTo(point.x, point.y))
        {
            ShowLocalNotice("Туда не пройти.");
            return;
        }
        ShowLocalClickMarker(point);
    }

    private LocalObjectDefinition LocalObjectNear(Vector2 point)
    {
        float radius = localRenderer.HexSizePixels * 0.6f;
        foreach (LocalObjectDefinition item in localDefinition.Objects)
        {
            if (LocalExplorationService.IsObjectVisible(gameState, item) && item.Point.DistanceTo(point.x, point.y) <= radius)
                return item;
        }
        return null;
    }

    private bool IsAtLocalEntrance(Vector2 point)
    {
        LocalEntranceDefinition entrance = localDefinition.FindEntrance(gameState.LocalExploration.EntranceId);
        return entrance != null && entrance.Point.DistanceTo(point.x, point.y) <= LocalExitRadius;
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
        LocalFreeMover.Member leader = localMover.Leader;
        if (item.Point.DistanceTo(leader.X, leader.Y) <= item.InteractRadius && !localMover.LeaderHasOrder)
        {
            PerformLocalObject(item);
            return;
        }
        // Объект может стоять на стене: путь ведёт к ближайшей доступной точке.
        if (!localMover.MoveLeaderTo(item.Point.X, item.Point.Y))
        {
            ShowLocalNotice("К этому не подойти.");
            return;
        }
        localPendingObjectId = item.Id;
        ShowLocalClickMarker(new Vector2(item.Point.X, item.Point.Y));
    }

    private void OnLocalExitClicked()
    {
        if (LocalCommandsBlocked)
            return;
        LocalEntranceDefinition entrance = localDefinition.FindEntrance(gameState.LocalExploration.EntranceId);
        if (entrance == null)
            return;
        localPendingObjectId = null;
        LocalFreeMover.Member leader = localMover.Leader;
        if (entrance.Point.DistanceTo(leader.X, leader.Y) <= LocalExitRadius && !localMover.LeaderHasOrder)
        {
            localPendingExit = true;
            return;
        }
        if (!localMover.MoveLeaderTo(entrance.Point.X, entrance.Point.Y))
        {
            ShowLocalNotice("К выходу не пройти.");
            return;
        }
        localPendingExit = true;
        ShowLocalClickMarker(new Vector2(entrance.Point.X, entrance.Point.Y));
    }

    private void TryCompleteLocalPendingAction()
    {
        if (localMover.LeaderHasOrder)
            return;
        LocalFreeMover.Member leader = localMover.Leader;

        if (!string.IsNullOrEmpty(localPendingObjectId))
        {
            LocalObjectDefinition item = localDefinition.FindObject(localPendingObjectId);
            localPendingObjectId = null;
            if (item == null)
                return;
            if (item.Point.DistanceTo(leader.X, leader.Y) <= item.InteractRadius)
                PerformLocalObject(item);
            else
                ShowLocalNotice("К этому не подойти.");
            return;
        }

        if (localPendingExit)
        {
            LocalEntranceDefinition entrance = localDefinition.FindEntrance(gameState.LocalExploration.EntranceId);
            if (entrance == null || entrance.Point.DistanceTo(leader.X, leader.Y) > LocalExitRadius)
            {
                localPendingExit = false;
                return;
            }
            // Перед выходом группа собирается физически.
            if (!localMover.IsGathered || !localMover.IsIdle)
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
        AddLocalHours(localDefinition.HoursPerInteraction, true);
        RefreshLocalObjects();
    }

    private void LeaveLocalExploration()
    {
        string name = localDefinition.DisplayName;
        AddLocalHours(0, true);
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

        // Кадр поля ложится на рисунок; каждый боец — на ближайшую свободную
        // клетку кадра со своей стороны стены, клетки противников заняты.
        List<string> candidates = PartyPresence.BattleCandidateIds(gameState);
        List<KeyValuePair<string, Vector2>> party = new List<KeyValuePair<string, Vector2>>();
        foreach (LocalFreeMover.Member member in localMover.Members)
        {
            if (candidates.Contains(member.Id))
                party.Add(new KeyValuePair<string, Vector2>(member.Id, new Vector2((float)member.X, (float)member.Y)));
        }
        if (!localGeometry.TryBuildEncounterRequest(gameState, encounter, party,
                out CampaignBattleRequest request, out Vector2 arenaCenter, out string buildError))
        {
            Debug.LogError("Бой на месте не начат: " + buildError);
            ShowLocalNotice("Здесь не развернуться для боя.");
            return;
        }

        // ПР-04: предбоевая точка возврата — до показа боя.
        AddLocalHours(0, true);
        Autosave();

        // Небоевые остаются в безопасной точке — не исчезают без объяснения.
        foreach (LocalFreeMover.Member member in localMover.Members)
        {
            if (candidates.Contains(member.Id))
                continue;
            ResidentState resident = HomePeopleService.Find(gameState, member.Id);
            string who = resident != null ? resident.DisplayName : member.Id;
            request.Notes.Add(who + " держится у выхода: " +
                              PartyPresence.BattleExclusionReason(gameState, member.Id).ToLowerInvariant() + ".");
            LocalExplorationService.StorePartyPosition(gameState, member.Id, encounter.RetreatPoint.X, encounter.RetreatPoint.Y,
                (int)LocationWorldRenderer.FacingFrom(new Vector2((float)member.DirectionX, (float)member.DirectionY)));
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

        localArenaCenter = arenaCenter;
        CampaignSession.EnterBattle(request);
        // Фигуры боя рисует поле боя; рисунок места остаётся под ним.
        localRenderer.ActorsVisible = false;
        localOverlay.style.display = DisplayStyle.None;
        localHud.style.display = DisplayStyle.None;
        BindLocalBattleCamera();
        AlignLocalBattleCamera();
    }

    // Камера места совмещает кадр арены на рисунке с кадром поля боя на
    // экране: фон и клетки при начале боя не сдвигаются.
    private void AlignLocalBattleCamera()
    {
        BattlefieldView surface = localBattle?.BattlefieldSurface;
        if (surface == null || localImage == null)
            return;
        Rect image = localImage.worldBound;
        Rect frame = surface.FrameRect;
        if (image.width < 1f || image.height < 1f || frame.width < 1f)
            return;
        Vector2 min = surface.LocalToWorld(frame.min);
        Vector2 max = surface.LocalToWorld(frame.max);
        Rect viewport = Rect.MinMaxRect(
            (min.x - image.x) / image.width, (min.y - image.y) / image.height,
            (max.x - image.x) / image.width, (max.y - image.y) / image.height);
        localRenderer.AlignFrame(localGeometry.FrameRect(localArenaCenter), viewport);
    }

    // ПР-12М: фигуры боя рисует рендерер места — под его светом и тенями.
    // Поле боя отдаёт кадр, прямоугольник и отражение каждой фигуры.
    private readonly List<LocationWorldRenderer.BattleFigureFrame> localBattleFrames = new List<LocationWorldRenderer.BattleFigureFrame>();

    private void SyncLocalBattleFigures()
    {
        HexBoardElement board = localBattle?.Board;
        if (board == null || localImage == null)
            return;
        Rect image = localImage.worldBound;
        if (image.width < 1f || image.height < 1f)
            return;
        Vector2 ToPixel(Vector2 boardPoint)
        {
            Vector2 world = board.LocalToWorld(boardPoint);
            return localRenderer.ViewportToPixel(new Vector2((world.x - image.x) / image.width, (world.y - image.y) / image.height));
        }
        localBattleFrames.Clear();
        foreach (BoardFigure figure in board.Figures)
        {
            Vector2 min = ToPixel(figure.Rect.min);
            Vector2 max = ToPixel(figure.Rect.max);
            localBattleFrames.Add(new LocationWorldRenderer.BattleFigureFrame
            {
                Id = figure.UnitId,
                Sprite = figure.Sprite,
                Rect = Rect.MinMaxRect(min.x, min.y, max.x, max.y),
                Ground = ToPixel(figure.Ground),
                Mirrored = figure.Mirrored,
                FitInside = figure.FitInside,
                Tint = figure.Tint,
                Corpse = figure.Corpse
            });
        }
        localRenderer.SetBattleFigures(localBattleFrames);
    }

    private void OnLocalBattleFinished(CampaignBattleResult result, int generation)
    {
        localBattle = null;
        localBattleSurface = null;
        localBattlePanning = false;
        RefreshLocalCameraPanel();
        localRenderer?.ClearBattleFigures();
        localBattleHost?.RemoveFromHierarchy();
        localBattleHost = null;
        // Прежний показ (загрузка, новая партия) — итог не применяется.
        if (generation != localGeneration || gameState == null || localBoundState != gameState)
            return;

        // Клетки, где закончили бой, — обратно в точки рисунка места.
        localGeometry.WritePoints(result, localArenaCenter);
        CampaignSession.CompleteBattle(result);
        if (!ApplyReturnedCampaignBattle())
        {
            CloseLocalScreen();
            return;
        }

        // Исследование продолжается на том же месте: выжившие там, где
        // закончили бой; при отходе — у безопасной точки; павших нет.
        localRenderer.ActorsVisible = true;
        localOverlay.style.display = DisplayStyle.Flex;
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
            localDefinition = null;
            LocalExplorationService.Exit(gameState);
            AddReport("Место недоступно — отряд снаружи, у входа.");
            return;
        }

        localGeneration++;
        localBoundState = gameState;
        localRenderer = new LocationWorldRenderer(localDefinition, FindLocalVisual(localDefinition), field);
        // Мир места — вдали от сцены глобальной карты: её камера его не видит.
        localRenderer.Root.transform.position = new Vector3(10000f, 10000f, 0f);
        localRenderer.SuppressOtherGlobalLights();
        localGeometry = localRenderer.Geometry;

        localField.Clear();
        localImage = new Image { name = "local-exploration-image", scaleMode = ScaleMode.StretchToFill };
        localImage.style.position = Position.Absolute;
        localImage.style.left = 0f;
        localImage.style.right = 0f;
        localImage.style.top = 0f;
        localImage.style.bottom = 0f;
        localImage.RegisterCallback<PointerDownEvent>(OnLocalPointerDown);
        localImage.RegisterCallback<PointerMoveEvent>(OnLocalPointerMove);
        localImage.RegisterCallback<PointerUpEvent>(OnLocalPointerUp);
        localImage.RegisterCallback<WheelEvent>(OnLocalWheel);
        localField.Add(localImage);

        localOverlay = new VisualElement { name = "local-exploration-overlay", pickingMode = PickingMode.Ignore };
        localOverlay.style.position = Position.Absolute;
        localOverlay.style.left = 0f;
        localOverlay.style.right = 0f;
        localOverlay.style.top = 0f;
        localOverlay.style.bottom = 0f;
        localField.Add(localOverlay);
        localClickMarker = new VisualElement { name = "local-exploration-click-marker", pickingMode = PickingMode.Ignore };
        localClickMarker.AddToClassList("local-exploration-click-marker");
        localClickMarker.style.position = Position.Absolute;
        localClickMarker.style.display = DisplayStyle.None;
        localOverlay.Add(localClickMarker);
        localObjectLabels.Clear();

        localHud.style.display = DisplayStyle.Flex;
        localScreen.style.display = DisplayStyle.Flex;
        localScreen.BringToFront();
        narrativeDialogueOverlay?.BringToFront();

        localPendingObjectId = null;
        localPendingExit = false;
        localPendingEncounter = null;
        localBattleRequested = false;
        localPartySignature = null;
        localPendingHours = 0;
        // Закреплённая камера держит масштаб и в новом месте.
        if (!localCameraLocked)
            localZoom = 1f;
        localHolding = false;
        BuildLocalCameraPanel();
        if (localTitleLabel != null)
            localTitleLabel.text = localDefinition.DisplayName.ToUpperInvariant();
        if (localArtNoteLabel != null)
            localArtNoteLabel.text = localDefinition.PlaceholderArt ? "временный фон — рисунка ещё нет" : string.Empty;
        ShowLocalNotice(string.Empty);

        RebuildLocalMover();
        RefreshLocalObjects();
        ContinuousSimulationSystem.SetPaused(gameState, true);
        localCameraSnap = true;
        localFollowedLeaderId = null;
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
        localBattleSurface = null;
        localBattlePanning = false;
        localField?.Clear();
        if (localImage != null)
            localImage.image = null;
        localRenderer?.Dispose();
        localRenderer = null;
        localImage = null;
        localOverlay = null;
        localClickMarker = null;
        localObjectLabels.Clear();
        localMover = null;
        localDefinition = null;
        localGeometry = null;
        localBoundState = null;
        localPendingObjectId = null;
        localPendingExit = false;
        localPendingEncounter = null;
        localBattleRequested = false;
        localHolding = false;
        if (localScreen != null)
            localScreen.style.display = DisplayStyle.None;
    }

    // Расстояние между участниками отряда — чуть больше клетки боя.
    private float LocalSpacing => localRenderer.HexSizePixels * 1.2f;

    // Отряд места из состояния: командир первым, затем бойцы и свита;
    // каждому — своя проходимая точка без наложения.
    private void RebuildLocalMover()
    {
        LocalExplorationStateData data = gameState.LocalExploration;
        List<string> present = PartyPresence.PresentIds(gameState);
        LocalEntranceDefinition entrance = localDefinition.FindEntrance(data.EntranceId);
        LocalPointData fallback = entrance != null ? entrance.Point : new LocalPointData(localDefinition.CanvasWidth / 2f, localDefinition.CanvasHeight / 2f);
        float spacing = LocalSpacing;

        List<KeyValuePair<string, LocalPointData>> members = new List<KeyValuePair<string, LocalPointData>>();
        List<LocalPointData> spare = null;
        foreach (string personId in present)
        {
            LocalActorStateData stored = data.Party.Find(actor => actor.ActorId == personId);
            LocalPointData point = stored != null ? new LocalPointData(stored.X, stored.Y) : fallback;
            if (!localGeometry.IsPassable(point))
                point = members.Count > 0 ? members[0].Value : fallback;
            bool crowded = members.Exists(entry => entry.Value.DistanceTo(point.X, point.Y) < spacing * 0.5);
            if (crowded || !localGeometry.IsPassable(point))
            {
                LocalPointData anchor = members.Count > 0 ? members[0].Value : point;
                if (spare == null)
                    spare = localGeometry.SpreadAround(anchor, present.Count * 3, spacing);
                LocalPointData free = spare.Find(candidate => !members.Exists(entry => entry.Value.DistanceTo(candidate.X, candidate.Y) < spacing * 0.5));
                if (free != null)
                    point = free;
            }
            members.Add(new KeyValuePair<string, LocalPointData>(personId, point));
            LocalExplorationService.StorePartyPosition(gameState, personId, point.X, point.Y, stored != null ? stored.Facing : 0);
        }
        data.Party.RemoveAll(actor => !present.Contains(actor.ActorId));
        if (members.Count == 0)
            members.Add(new KeyValuePair<string, LocalPointData>(gameState.GetSelectedCommander()?.Id ?? "hero", fallback));
        localMover = new LocalFreeMover(localGeometry.Layer, localGeometry.Rules, members, spacing);
    }

    private void StoreLocalPositions()
    {
        if (localMover == null)
            return;
        foreach (LocalFreeMover.Member member in localMover.Members)
        {
            int facing = (int)LocationWorldRenderer.FacingFrom(new Vector2((float)member.DirectionX, (float)member.DirectionY));
            LocalExplorationService.StorePartyPosition(gameState, member.Id, (float)member.X, (float)member.Y, facing);
        }
    }

    // Подписи объектов — над их точками на рисунке; клик — подойти.
    private void RefreshLocalObjects()
    {
        if (localOverlay == null)
            return;
        foreach (LocalObjectDefinition item in localDefinition.Objects)
        {
            bool visible = LocalExplorationService.IsObjectVisible(gameState, item);
            bool active = LocalExplorationService.IsObjectAvailable(gameState, localDefinition, item);
            if (!localObjectLabels.TryGetValue(item.Id, out Label label))
            {
                string objectId = item.Id;
                label = new Label(item.Label) { name = "local-object-" + item.Id };
                label.AddToClassList("local-object-label");
                label.style.position = Position.Absolute;
                label.RegisterCallback<PointerDownEvent>(evt =>
                {
                    if (evt.button != 0)
                        return;
                    evt.StopPropagation();
                    OnLocalObjectClicked(objectId);
                });
                localOverlay.Add(label);
                localObjectLabels[item.Id] = label;
            }
            label.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            label.pickingMode = visible ? PickingMode.Position : PickingMode.Ignore;
            label.EnableInClassList("local-object-label--active", active);
            label.EnableInClassList("local-object-label--done", !active);
        }
    }

    private void RenderLocalExploration()
    {
        if (localRenderer == null || localMover == null)
            return;

        LocalFreeMover.Member leader = localMover.Leader;
        float viewHeight = LocationCameraFollow.DefaultViewHeight(localRenderer.Definition, localDefinition) / localZoom;
        // Новый командир — новая цель: кадр сразу на нём (закреплённая камера стоит).
        if (leader.Id != localFollowedLeaderId)
        {
            localFollowedLeaderId = leader.Id;
            localCameraSnap |= !localCameraLocked;
        }
        // Закреплённая камера не едет за командиром; первый кадр места — на нём.
        if (!localCameraLocked || localCameraSnap)
            localRenderer.Follow(new Vector2((float)leader.X, (float)leader.Y), viewHeight, Time.unscaledDeltaTime, localCameraSnap);
        localCameraSnap = false;
        RenderLocalWorld();
        LayoutLocalOverlay();
        UpdateLocalLeaderHeight(leader.Id);

        if (localTimeLabel != null)
        {
            ContinuousClockSnapshot clock = ContinuousSimulationSystem.GetClock(gameState);
            localTimeLabel.text = "День " + clock.Day + " · " + ContinuousSimulationSystem.FormatClock(clock.HourOfDay);
        }
        if (localNoticeLabel != null && Time.realtimeSinceStartup > localNoticeUntil)
            localNoticeLabel.text = string.Empty;
        RefreshLocalPartyList();
    }

    // Рисунок места в текстуру экрана: размер текстуры — размер поля на
    // экране в пикселях; фигуры, свет и время суток — из состояния.
    private void RenderLocalWorld()
    {
        Rect bounds = localImage.contentRect;
        if (bounds.width >= 1f && bounds.height >= 1f && interfaceRoot != null)
        {
            float scale = Screen.height / Mathf.Max(1f, interfaceRoot.layout.height);
            RenderTexture target = localRenderer.EnsureTarget(Mathf.RoundToInt(bounds.width * scale), Mathf.RoundToInt(bounds.height * scale));
            if (localImage.image != target)
                localImage.image = target;
        }

        ContinuousClockSnapshot clock = ContinuousSimulationSystem.GetClock(gameState);
        localRenderer.SetTime((float)clock.HourOfDay, Time.unscaledTime);
        if (!localRenderer.ActorsVisible || localMover == null)
            return;

        List<LocationWorldRenderer.ActorFrame> frames = new List<LocationWorldRenderer.ActorFrame>();
        CommanderData hero = gameState.GetSelectedCommander();
        List<string> retinue = gameState.ActiveExpedition?.RetinueIds ?? new List<string>();
        foreach (LocalFreeMover.Member member in localMover.Members)
        {
            ResidentState resident = HomePeopleService.Find(gameState, member.Id);
            string unitType = hero != null && member.Id == hero.Id
                ? (string.IsNullOrWhiteSpace(hero.UnitTypeId) ? CampaignBattleBridge.HeroFallbackUnitTypeId : hero.UnitTypeId)
                : FindFighterUnitType(member.Id) ?? resident?.UnitTypeId;
            frames.Add(new LocationWorldRenderer.ActorFrame
            {
                Id = member.Id,
                UnitTypeId = unitType ?? string.Empty,
                Kind = retinue.Contains(member.Id) ? LocationWorldRenderer.ActorKind.Retinue : LocationWorldRenderer.ActorKind.Party,
                Pixel = new Vector2((float)member.X, (float)member.Y),
                Direction = new Vector2((float)member.DirectionX, (float)member.DirectionY),
                Walking = member.Walking,
                Wounded = resident != null && resident.Injury == ResidentInjury.Recovering
            });
        }
        foreach (LocalActorStateData enemy in LocalExplorationService.AliveEnemies(gameState, localDefinition))
        {
            LocalEnemyDefinition definition = localDefinition.FindEnemy(enemy.ActorId);
            frames.Add(new LocationWorldRenderer.ActorFrame
            {
                Id = enemy.ActorId,
                UnitTypeId = definition.UnitTypeId,
                Kind = LocationWorldRenderer.ActorKind.Enemy,
                Pixel = new Vector2(enemy.X, enemy.Y)
            });
        }
        localRenderer.SetActors(frames, Time.unscaledTime);
    }

    // Высота под ногами командира — тем же сервисом, что окно базы; в
    // сборке разработчика видна в подписи места. Положение и сортировку
    // фигуры высота не меняет: рисунок земли уже ортографическая проекция.
    private void UpdateLocalLeaderHeight(string leaderId)
    {
        if (localRenderer.Height == null)
        {
            localLeaderHeight = HeightSample.Invalid("У места нет карты высот.");
            return;
        }
        localRenderer.TrySampleActorHeight(leaderId, out localLeaderHeight);
        if (localArtNoteLabel == null || !Debug.isDebugBuild)
            return;
        string art = localDefinition.PlaceholderArt ? "временный фон — рисунка ещё нет · " : string.Empty;
        localArtNoteLabel.text = art + (localLeaderHeight.Valid
            ? "высота под ногами: " + localLeaderHeight.Meters.ToString("0.00") + " м"
            : "высота: " + localLeaderHeight.Reason);
    }

    private Vector2 LocalPixelToPanel(Vector2 pixel)
    {
        Rect bounds = localImage.contentRect;
        Vector2 viewport = localRenderer.PixelToViewport(pixel);
        return new Vector2(viewport.x * bounds.width, viewport.y * bounds.height);
    }

    private void LayoutLocalOverlay()
    {
        if (localOverlay == null)
            return;
        float lift = localRenderer.HexSizePixels * 0.9f;
        foreach (LocalObjectDefinition item in localDefinition.Objects)
        {
            if (!localObjectLabels.TryGetValue(item.Id, out Label label) || label.style.display == DisplayStyle.None)
                continue;
            Vector2 panel = LocalPixelToPanel(new Vector2(item.Point.X, item.Point.Y - lift));
            float width = label.resolvedStyle.width;
            float height = label.resolvedStyle.height;
            label.style.left = panel.x - (float.IsNaN(width) ? 0f : width / 2f);
            label.style.top = panel.y - (float.IsNaN(height) ? 0f : height);
        }

        bool marker = Time.realtimeSinceStartup < localClickMarkerUntil;
        localClickMarker.style.display = marker ? DisplayStyle.Flex : DisplayStyle.None;
        if (marker)
        {
            Rect bounds = localImage.contentRect;
            float size = Mathf.Max(10f, localRenderer.HexSizePixels / Mathf.Max(1f, localRenderer.ViewHeight) * bounds.height * 0.6f);
            Vector2 panel = LocalPixelToPanel(localClickMarkerPoint);
            localClickMarker.style.left = panel.x - size / 2f;
            localClickMarker.style.top = panel.y - size * 0.3f;
            localClickMarker.style.width = size;
            localClickMarker.style.height = size * 0.6f;
        }
    }

    private void ShowLocalClickMarker(Vector2 point)
    {
        localClickMarkerPoint = point;
        localClickMarkerUntil = Time.realtimeSinceStartup + 0.6f;
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

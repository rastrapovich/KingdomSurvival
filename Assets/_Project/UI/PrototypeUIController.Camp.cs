using System.Collections.Generic;
using KingdomSurvival.Chapter01;
using UnityEngine.UIElements;

/// <summary>
/// Лагерь (P09-T04/T05) — не survival-симулятор и не отдельный CampManager.
/// Начиная с этой переработки экран — реальный, постоянный, скрытый по
/// умолчанию UXML-узел ("camp-screen" в Prototype_Main.uxml), полностью
/// редактируемый через Kingdom Survival -> UI Конструктор (экран "camp" в
/// KingdomSurvivalUILayouts.asset) — тот же паттерн, что уже работает для
/// expeditions-screen. Этот файл только ищет узлы по имени и подставляет
/// динамический контент (текст/видимость слота/список сцен) — позицию,
/// размер, цвет и арт держат Prototype_Camp.uss и данные UI Конструктора,
/// а не C# (раздел 24 инструкции: "Что C# имеет право менять в runtime").
/// Открытие/закрытие не двигает маршрут экспедиции и не тратит время само
/// по себе — оно лишь ставит/снимает общую блокирующую паузу (см.
/// IsCampScreenOpen в ModalQueue.cs и комментарий там же).
/// </summary>
public partial class PrototypeUIController
{
    private const int CampFighterSlotCount = 4;

    private VisualElement campScreen;
    private Button campNavButton;
    private Button campCloseButton;
    private Button campContinueButton;

    private Label campTimeLabel;
    private Label campLocationLabel;
    private Label campCommanderName;
    private Label campCommanderStatus;
    private VisualElement campSceneList;
    private Label campStatusText;
    // ПР-09: действия ночи и ночлег.
    private Label campActionsHint;
    private VisualElement campActionList;
    private Button campRestButton;

    private readonly VisualElement[] campFighterSlots = new VisualElement[CampFighterSlotCount];
    private readonly Label[] campFighterNameLabels = new Label[CampFighterSlotCount];
    private readonly Label[] campFighterStatusLabels = new Label[CampFighterSlotCount];

    private bool isCampScreenOpen;

    // Читается из ModalQueue.cs (HasBlockingModalWork/HasBlockingModalWorkExceptCamp).
    private bool IsCampScreenOpen => isCampScreenOpen;

    // ------------------------------------------------------------------
    // Инициализация — только поиск по имени, никакого построения дерева
    // ------------------------------------------------------------------

    private void InitializeCampUi()
    {
        if (interfaceRoot == null || campScreen != null)
            return;

        campScreen = interfaceRoot.Q<VisualElement>("camp-screen");
        if (campScreen == null)
            return;

        campTimeLabel = campScreen.Q<Label>("camp-time-label");
        campLocationLabel = campScreen.Q<Label>("camp-location-label");
        campCloseButton = campScreen.Q<Button>("camp-close-button");
        campContinueButton = campScreen.Q<Button>("camp-continue-button");
        campCommanderName = campScreen.Q<Label>("camp-commander-name");
        campCommanderStatus = campScreen.Q<Label>("camp-commander-status");
        campSceneList = campScreen.Q<VisualElement>("camp-scene-list");
        campStatusText = campScreen.Q<Label>("camp-status-text");
        campActionsHint = campScreen.Q<Label>("camp-actions-placeholder");
        campActionList = campScreen.Q<VisualElement>("camp-action-list");
        campRestButton = campScreen.Q<Button>("camp-rest-button");
        if (campRestButton != null)
            campRestButton.clicked += OnCampRestClicked;

        for (int i = 0; i < CampFighterSlotCount; i++)
        {
            int slotNumber = i + 1;
            campFighterSlots[i] = campScreen.Q<VisualElement>("camp-fighter-slot-" + slotNumber);
            campFighterNameLabels[i] = campScreen.Q<Label>("camp-fighter-name-" + slotNumber);
            campFighterStatusLabels[i] = campScreen.Q<Label>("camp-fighter-status-" + slotNumber);
        }

        if (campCloseButton != null)
            campCloseButton.clicked += CloseCampScreen;
        if (campContinueButton != null)
            campContinueButton.clicked += OnCampContinueClicked;

        campNavButton = interfaceRoot.Q<Button>("nav-camp-button");
        if (campNavButton != null)
        {
            campNavButton.clicked += ToggleCampScreen;
            // Скрыта до CampUnlocked (раздел "Кнопка": "До встречи — кнопки
            // вообще нет") — RefreshCampNavButtonState включает видимость
            // после первого реального обновления состояния.
            campNavButton.style.display = DisplayStyle.None;
        }
    }

    private void ToggleCampScreen()
    {
        if (isCampScreenOpen)
            CloseCampScreen();
        else
            OpenCampScreen();
    }

    // Раздел "Когда кнопку можно нажать" инструкции про лагерь: разблокирован
    // + активная экспедиция. Обязательное событие/бой/RoadStop-остановка на
    // дороге (раздел 19: "не блокировать Лагерь дорожной остановкой") уже
    // отдельно занимают или не занимают HasBlockingModalWorkExceptCamp —
    // кнопка тогда просто disabled (см. RefreshCampNavButtonState), открыть
    // поверх обязательного события всё равно нельзя.
    private bool CanOpenCampScreen()
    {
        return gameState != null &&
               !isGameOver &&
               IsCampUnlocked() &&
               gameState.HasActiveExpedition;
    }

    // ПР-12А: в свободной игре лагерь доступен сразу; в главе — после встречи,
    // которая его открывает (CampUnlocked).
    private bool IsCampUnlocked()
    {
        return gameState != null &&
               (CampaignContent.IsFreePlay(gameState) ||
                gameState.Narrative != null && gameState.Narrative.HasFlag(Chapter01Ids.Flags.CampUnlocked));
    }

    private void OpenCampScreen()
    {
        if (campScreen == null || isCampScreenOpen || !CanOpenCampScreen())
            return;

        // Camp — третий fullscreen-слой наравне с Journal/Hero Screen
        // (раздел 20 инструкции P08J про Journal/Hero Screen расширяется на
        // Camp тем же принципом: одновременно открытым может быть только
        // один).
        CloseJournal();
        CloseHeroScreen();

        isCampScreenOpen = true;
        PauseForBlockingModal();

        // Раздел 14 инструкции: сначала сам экран, только потом сцена —
        // иначе после завершения D11C игрок вернётся неизвестно куда.
        campScreen.style.display = DisplayStyle.Flex;
        campScreen.BringToFront();
        RefreshCampScreen();
        RefreshCampNavButtonState();

        // Раздел "Что происходит при первом открытии": авторская camp-сцена
        // запускается автоматически поверх лагерного экрана через
        // TryOpenNarrativeDialogueById — не второй рендерер диалогов.
        // HasBlockingModalWorkExceptCamp (не HasBlockingModalWork) в самом
        // TryOpenNarrativeDialogueById позволяет ей открыться, пока
        // isCampScreenOpen уже true.
        CampSceneViewData? scene = Chapter01CampSceneProvider.GetAvailableScene(gameState);
        if (scene.HasValue)
            TryOpenNarrativeDialogueById(scene.Value.DialogueId);
    }

    private void CloseCampScreen()
    {
        if (campScreen == null)
            return;

        campScreen.style.display = DisplayStyle.None;
        isCampScreenOpen = false;

        // Раздел 21/22: не строит маршрут заново, не телепортирует, не
        // сбрасывает ActiveExpedition/координату/прогресс — просто отдаёт
        // паузу тому же механизму, который её поставил
        // (PauseForBlockingModal/ResumeAfterBlockingModalIfReady).
        ResumeAfterBlockingModalIfReady();
        RefreshCampNavButtonState();
    }

    private void OnCampContinueClicked()
    {
        CloseCampScreen();
    }

    // ------------------------------------------------------------------
    // Наполнение данными — единственное, что C# имеет право менять здесь
    // (раздел 24 инструкции): текст, видимость слота, список сцен.
    // ------------------------------------------------------------------

    private void RefreshCampScreen()
    {
        if (campScreen == null || gameState == null)
            return;

        if (campTimeLabel != null)
        {
            ContinuousClockSnapshot clock = ContinuousSimulationSystem.GetClock(gameState);
            campTimeLabel.text = "День " + gameState.Day + " · " + ContinuousSimulationSystem.FormatClock(clock.HourOfDay);
        }

        RefreshCampLocationAndStatus();
        RefreshCampRoster();
        RefreshCampSceneList();
        RefreshCampActions();
    }

    // ПР-09 (ТЗ §3, §5): до двух дел на ночь и «Встать на ночлег».
    private string campActionMessage = string.Empty;

    private void RefreshCampActions()
    {
        if (campActionList == null || campRestButton == null)
            return;

        campActionList.Clear();
        bool canRest = CampRest.CanRest(gameState, out string reason);
        foreach (CampActionOption option in CampRest.GetActions(gameState))
        {
            bool chosen = CampRest.IsChosen(gameState, option.Kind);
            CampActionKind kind = option.Kind;
            Button button = new Button(() =>
            {
                CampRest.TryToggleAction(gameState, kind, out string message);
                campActionMessage = message;
                RefreshCampScreen();
            })
            { text = (chosen ? "✓ " : "") + option.Title };
            button.AddToClassList("camp-action-button");
            button.EnableInClassList("camp-action-button--chosen", chosen);
            button.SetEnabled(canRest && (option.Available || chosen));
            campActionList.Add(button);

            Label hint = new Label(option.Available ? option.Hint : option.UnavailableReason);
            hint.AddToClassList("camp-placeholder-text");
            campActionList.Add(hint);
        }

        campRestButton.SetEnabled(canRest);
        if (campActionsHint != null)
        {
            campActionsHint.text = !canRest
                ? reason
                : !string.IsNullOrEmpty(campActionMessage)
                    ? campActionMessage
                    : "Можно выбрать до двух дел на ночь — или просто лечь спать. Ночлег: 8 часов, припасы — обычным порядком.";
        }
    }

    private void OnCampRestClicked()
    {
        if (gameState == null)
            return;
        if (!CampRest.TryStartRest(gameState, out string message))
        {
            campActionMessage = message;
            RefreshCampScreen();
            return;
        }

        campActionMessage = string.Empty;
        AddReport(message);
        CloseCampScreen();
        RefreshInterface();
    }

    // Раздел 15 инструкции — только информационно, ничего не придумывается
    // заранее (снабжение/бойцы/цель/статус уже существуют в GameState;
    // ранения/усталость/мораль/погода намеренно не показываются, пока для
    // них нет реальной системы).
    private void RefreshCampLocationAndStatus()
    {
        if (!gameState.HasActiveExpedition)
        {
            if (campLocationLabel != null)
                campLocationLabel.text = string.Empty;
            if (campStatusText != null)
                campStatusText.text = string.Empty;
            return;
        }

        ExpeditionData expedition = gameState.ActiveExpedition;
        LocationData location = gameState.FindLocation(expedition.LocationId);
        string placeText = location != null && !location.IsWaypoint ? location.Name : "в пути";

        string phaseText;
        switch (expedition.Phase)
        {
            case CommanderState.AtLocation:
                phaseText = "На месте";
                break;
            case CommanderState.ReturningToCastle:
                phaseText = "Возвращается";
                break;
            default:
                phaseText = "В пути";
                break;
        }

        if (campLocationLabel != null)
            campLocationLabel.text = placeText + " · " + Chapter01StoryDirector.DescribeCampPlace(gameState);

        if (campStatusText != null)
        {
            campStatusText.text =
                "Цель: " + placeText + "\n" +
                phaseText + " · Бойцов: " + expedition.FighterIds.Count +
                " · Припасы: " + gameState.ArmySupply;
        }
    }

    // Раздел "Панель отряда" инструкции: реальные Hero + ActiveExpedition.
    // FighterIds, не первые четыре FighterData из базы. Отсутствующий боец
    // в слоте — слот скрыт (display:none), а не фальшивая заглушка с именем.
    private void RefreshCampRoster()
    {
        CommanderData commander = gameState.GetSelectedCommander();
        if (campCommanderName != null)
            campCommanderName.text = commander != null ? commander.Name : "Командир";
        if (campCommanderStatus != null)
            campCommanderStatus.text = CampPersonStatus(commander != null ? commander.Id : null);

        List<string> fighterIds = gameState.HasActiveExpedition
            ? gameState.ActiveExpedition.FighterIds
            : new List<string>();

        for (int i = 0; i < CampFighterSlotCount; i++)
        {
            if (campFighterSlots[i] == null)
                continue;

            if (i < fighterIds.Count)
            {
                FighterData fighter = gameState.FindFighter(fighterIds[i]);
                campFighterSlots[i].style.display = DisplayStyle.Flex;
                if (campFighterNameLabels[i] != null)
                    campFighterNameLabels[i].text = fighter != null ? fighter.Name : "—";
                // Раздел 11: HP/состояние/маркер разговора — места под
                // будущее, пока отсутствующая механика — нейтральная
                // заглушка "—", не фальшивые данные.
                if (campFighterStatusLabels[i] != null)
                    campFighterStatusLabels[i].text = CampPersonStatus(fighterIds[i]);
            }
            else
            {
                campFighterSlots[i].style.display = DisplayStyle.None;
            }
        }
    }

    // ПР-09: у костра видно, кто ранен и кто вымотан.
    private string CampPersonStatus(string personId)
    {
        ResidentState resident = HomePeopleService.Find(gameState, personId);
        if (resident == null || !resident.HasCombatState)
            return "—";
        string text = "HP " + resident.CurrentHitPoints + "/" + resident.MaxHitPoints;
        if (resident.Exhausted)
            text += " · изнеможён";
        return text;
    }

    // Раздел 12/20 инструкции: единственный источник — Chapter01CampSceneProvider
    // (read-only), никакого второго рендерера диалогов — запуск идёт через
    // TryOpenNarrativeDialogueById в OpenCampScreen. Список — динамический
    // контент, поэтому строится в C#, но стилизуется классами из
    // Prototype_Camp.uss (camp-scene-row/camp-scene-row-title/
    // camp-placeholder-text), а не инлайн-стилями.
    private void RefreshCampSceneList()
    {
        if (campSceneList == null)
            return;

        campSceneList.Clear();

        CampSceneViewData? scene = Chapter01CampSceneProvider.GetAvailableScene(gameState);
        if (!scene.HasValue)
        {
            Label empty = new Label("Пока нечего рассказать.");
            empty.AddToClassList("camp-placeholder-text");
            campSceneList.Add(empty);
            return;
        }

        VisualElement row = new VisualElement();
        row.AddToClassList("camp-scene-row");
        Label title = new Label("● " + scene.Value.Title);
        title.AddToClassList("camp-scene-row-title");
        row.Add(title);
        campSceneList.Add(row);
    }

    // Раздел 2/18 инструкции: скрыта/disabled/enabled/активна должны
    // визуально различаться, а не только цветом. Скрыта — display:none;
    // disabled — .shell-nav-button:disabled (Prototype_Shell.uss, ниже
    // непрозрачность); активна — общий SetNavigationButtonActive/
    // nav-button-active (тот же класс, каким уже подсвечиваются Столица/
    // Экспедиции). Tooltip называет конкретную причину, а не общую фразу.
    private void RefreshCampNavButtonState()
    {
        if (campNavButton == null || gameState == null)
            return;

        bool unlocked = IsCampUnlocked();
        campNavButton.style.display = unlocked ? DisplayStyle.Flex : DisplayStyle.None;
        if (!unlocked)
            return;

        bool blockedByOther = HasBlockingModalWorkExceptCamp();
        bool canOpen = CanOpenCampScreen() && !blockedByOther;
        campNavButton.SetEnabled(canOpen || isCampScreenOpen);
        SetNavigationButtonActive(campNavButton, isCampScreenOpen);

        if (isCampScreenOpen)
            campNavButton.tooltip = "Лагерь открыт.";
        else if (blockedByOther)
            campNavButton.tooltip = IsNarrativeDialogueActive
                ? "Сначала завершите разговор."
                : "Сначала примите обязательное решение.";
        else if (!gameState.HasActiveExpedition)
            campNavButton.tooltip = "Лагерь доступен только во время похода.";
        else
            campNavButton.tooltip = "Остановиться лагерем.";
    }
}

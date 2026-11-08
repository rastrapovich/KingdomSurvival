using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

// ПР-01: главное меню, меню паузы (Esc) и настройки. Кампания живёт в
// CampaignSession, а не в этой сцене: интерфейс при старте подхватывает уже
// идущую кампанию (возврат в сцену) или показывает главное меню. Кампания
// создаётся только «Новой игрой» или загрузкой. Пока открыто любое меню,
// время стоит и сюжетные окна не открываются (HasBlockingModalWorkExceptCamp).
public partial class PrototypeUIController
{
    private const string GameMenusScreenName = "Меню";
    private const string GameMenuOpenClass = "game-menu-overlay--open";
    private const string GameMenuConfirmOpenClass = "game-menu-confirm--open";
    private const string VolumePrefsKey = "settings.volume";

    private VisualElement mainMenuOverlay;
    private VisualElement pauseMenuOverlay;
    private VisualElement settingsOverlay;
    private Button mainMenuContinueButton;
    private Button mainMenuNewGameButton;
    private Button mainMenuLoadButton;
    private Label mainMenuContinueHint;
    private Label mainMenuLoadHint;
    private Label mainMenuMessage;
    private VisualElement mainMenuNewGameConfirm;
    private Button pauseMenuSaveButton;
    private Button pauseMenuLoadButton;
    private Label pauseMenuMessage;
    private Toggle settingsFullscreenToggle;
    private Slider settingsVolumeSlider;
    private bool gameMenusBound;

    private bool IsMainMenuOpen => IsOverlayOpen(mainMenuOverlay);
    private bool IsPauseMenuOpen => IsOverlayOpen(pauseMenuOverlay);
    private bool IsSettingsOpen => IsOverlayOpen(settingsOverlay);
    private bool IsGameMenuOpen => IsMainMenuOpen || IsPauseMenuOpen || IsSettingsOpen || IsSavesPickerOpen || IsNewGameSummaryOpen;

    // Статическое состояние кампании не должно пережить выход из Play Mode
    // при выключенной перезагрузке домена.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetCampaignSessionOnPlayModeStart()
    {
        CampaignSession.Reset();
    }

    private static bool IsOverlayOpen(VisualElement overlay)
    {
        return overlay != null && overlay.ClassListContains(GameMenuOpenClass);
    }

    private void InitializeGameMenus()
    {
        if (gameMenusBound || interfaceRoot == null)
            return;

        mainMenuOverlay = BindRequiredElement<VisualElement>(interfaceRoot, GameMenusScreenName, "main-menu-overlay");
        pauseMenuOverlay = BindRequiredElement<VisualElement>(interfaceRoot, GameMenusScreenName, "pause-menu-overlay");
        settingsOverlay = BindRequiredElement<VisualElement>(interfaceRoot, GameMenusScreenName, "settings-overlay");
        mainMenuContinueButton = BindRequiredElement<Button>(interfaceRoot, GameMenusScreenName, "main-menu-continue-button");
        mainMenuNewGameButton = BindRequiredElement<Button>(interfaceRoot, GameMenusScreenName, "main-menu-new-game-button");
        mainMenuLoadButton = BindRequiredElement<Button>(interfaceRoot, GameMenusScreenName, "main-menu-load-button");
        mainMenuContinueHint = BindRequiredElement<Label>(interfaceRoot, GameMenusScreenName, "main-menu-continue-hint");
        mainMenuLoadHint = BindRequiredElement<Label>(interfaceRoot, GameMenusScreenName, "main-menu-load-hint");
        mainMenuMessage = BindRequiredElement<Label>(interfaceRoot, GameMenusScreenName, "main-menu-message");
        mainMenuNewGameConfirm = BindRequiredElement<VisualElement>(interfaceRoot, GameMenusScreenName, "main-menu-new-game-confirm");
        pauseMenuSaveButton = BindRequiredElement<Button>(interfaceRoot, GameMenusScreenName, "pause-menu-save-button");
        pauseMenuLoadButton = BindRequiredElement<Button>(interfaceRoot, GameMenusScreenName, "pause-menu-load-button");
        pauseMenuMessage = BindRequiredElement<Label>(interfaceRoot, GameMenusScreenName, "pause-menu-message");
        settingsFullscreenToggle = BindRequiredElement<Toggle>(interfaceRoot, GameMenusScreenName, "settings-fullscreen-toggle");
        settingsVolumeSlider = BindRequiredElement<Slider>(interfaceRoot, GameMenusScreenName, "settings-volume-slider");

        if (mainMenuOverlay == null || pauseMenuOverlay == null || settingsOverlay == null ||
            mainMenuContinueButton == null || mainMenuNewGameButton == null || mainMenuLoadButton == null ||
            mainMenuContinueHint == null || mainMenuLoadHint == null || mainMenuMessage == null ||
            mainMenuNewGameConfirm == null || pauseMenuSaveButton == null || pauseMenuLoadButton == null ||
            pauseMenuMessage == null || settingsFullscreenToggle == null || settingsVolumeSlider == null)
        {
            return;
        }

        mainMenuContinueButton.clicked += OnMainMenuContinueClicked;
        mainMenuNewGameButton.clicked += OnMainMenuNewGameClicked;
        mainMenuLoadButton.clicked += OnMainMenuLoadClicked;
        BindMenuButton("main-menu-new-game-yes-button", OnNewGameConfirmed);
        BindMenuButton("main-menu-new-game-no-button", () => SetNewGameConfirmOpen(false));
        BindMenuButton("main-menu-settings-button", OpenSettings);
        BindMenuButton("main-menu-quit-button", QuitGame);

        BindMenuButton("pause-menu-resume-button", ClosePauseMenu);
        pauseMenuSaveButton.clicked += OnPauseMenuSaveClicked;
        pauseMenuLoadButton.clicked += OnPauseMenuLoadClicked;
        BindMenuButton("pause-menu-settings-button", OpenSettings);
        BindMenuButton("pause-menu-main-menu-button", OnPauseMenuMainMenuClicked);
        BindMenuButton("pause-menu-quit-button", QuitGame);

        BindMenuButton("settings-close-button", CloseSettings);
        settingsFullscreenToggle.RegisterValueChangedCallback(evt => Screen.fullScreen = evt.newValue);
        settingsVolumeSlider.RegisterValueChangedCallback(evt => ApplyVolume(evt.newValue, true));
        ApplyVolume(LoadSavedVolume(), false);

        BindSavesPicker();
        BindNewGameSummary();
        gameMenusBound = true;
    }

    private void BindMenuButton(string name, Action onClick)
    {
        Button button = BindRequiredElement<Button>(interfaceRoot, GameMenusScreenName, name);
        if (button != null)
            button.clicked += onClick;
    }

    // ------------------------------------------------------------------
    // Старт сцены: подхватить идущую кампанию или показать главное меню.
    // ------------------------------------------------------------------

    private void StartOrResumeSession()
    {
        if (CampaignSession.HasActive)
        {
            AdoptCampaign(CampaignSession.Current);
            // ПР-03: кампания могла вернуться из боя — применить итог.
            ApplyReturnedCampaignBattle();
            return;
        }

        gameState = null;
        CloseMainScreen();
        ShowMainMenu();
    }

    // Та же кампания вернулась в сцену (например, после боя): интерфейс
    // сбрасывает только своё сеансовое состояние, кампанию не трогает.
    private void AdoptCampaign(GameState campaign)
    {
        EnsureWorldMapGeographyConfigured(campaign);

        gameState = campaign;
        isGameOver = false;
        lastNavigationClickTime = -NavigationClickCooldownSeconds;
        unreadIncidents.Clear();
        reportRequiresAcknowledgement.Clear();
        reportReadStates.Clear();
        selectedJournalGoalId = null;
        ClearQueuedModals();
        ResetWorldMapSelection();

        if (quickExpeditionPopup != null)
            BindQuickExpeditionPopup();

        HideIncidentModal();
        HideGameOver();
        CloseMainScreen();
        RefreshInterface();
    }

    // ------------------------------------------------------------------
    // Главное меню
    // ------------------------------------------------------------------

    private void ShowMainMenu()
    {
        if (!gameMenusBound)
            return;

        SetNewGameConfirmOpen(false);
        mainMenuMessage.text = string.Empty;
        ShowPreBattleLoadButton(false);
        RefreshMainMenuButtons();
        OpenMenuOverlay(mainMenuOverlay);
    }

    private void HideMainMenu()
    {
        CloseMenuOverlay(mainMenuOverlay);
    }

    private void RefreshMainMenuButtons()
    {
        SaveSlotSummary? recent = FindMostRecentLoadableSlot();
        bool anySave = false;
        foreach (string slotId in CampaignSaveStore.AllSlotIds)
            anySave |= ReadSlotSummary(slotId).Exists;
        bool hasSession = CampaignSession.HasActive && gameState != null;

        if (hasSession)
        {
            mainMenuContinueButton.SetEnabled(true);
            mainMenuContinueHint.text = "Вернуться в текущую партию · день " + gameState.Day;
        }
        else if (recent.HasValue)
        {
            mainMenuContinueButton.SetEnabled(true);
            mainMenuContinueHint.text = "Последнее сохранение · " +
                                        CampaignSaveStore.GetSlotTitle(recent.Value.SlotId) + " · " +
                                        recent.Value.Description;
        }
        else
        {
            mainMenuContinueButton.SetEnabled(false);
            mainMenuContinueHint.text = anySave
                ? "Ни одно сохранение нельзя загрузить — подробности в «Загрузить»."
                : "Нет начатой партии и сохранений.";
        }

        mainMenuLoadButton.SetEnabled(anySave);
        mainMenuLoadHint.text = anySave ? string.Empty : "Сохранений пока нет.";
    }

    private void OnMainMenuContinueClicked()
    {
        if (CampaignSession.HasActive && gameState != null)
        {
            HideMainMenu();
            return;
        }

        if (LoadMostRecentCampaign())
        {
            HideMainMenu();
        }
        else
            mainMenuMessage.text = lastCampaignIoMessage;
    }

    private void OnMainMenuNewGameClicked()
    {
        if (CampaignSession.HasActive)
        {
            SetNewGameConfirmOpen(true);
            return;
        }

        OpenNewGameSummary();
    }

    // Подтверждение «начать заново» ведёт на тот же экран итога.
    private void OnNewGameConfirmed()
    {
        SetNewGameConfirmOpen(false);
        OpenNewGameSummary();
    }

    // Старт без экрана итога — для тестов и сценариев, где выбор уже сделан.
    private void StartNewGameFromMenu()
    {
        SetNewGameConfirmOpen(false);
        ShowLoadingScreen("Создаём мир…", 0.02f);
        StartNewGame(new CampaignSetup());
        CloseMenuOverlay(newGameOverlay);
        HideMainMenu();
        loadingCampaignReady = true;
    }

    // ------------------------------------------------------------------
    // ПР-05: итог перед стартом. Кампания создаётся один раз — по «Начать».
    // «Назад» ничего не создаёт, не сохраняет и не тратит время.
    // ------------------------------------------------------------------

    private VisualElement newGameOverlay;
    private Button newGameStartButton;
    private Label newGameMessage;
    private CampaignSetup pendingSetup;

    private bool IsNewGameSummaryOpen => IsOverlayOpen(newGameOverlay);

    private void BindNewGameSummary()
    {
        newGameOverlay = BindRequiredElement<VisualElement>(interfaceRoot, GameMenusScreenName, "new-game-overlay");
        newGameStartButton = BindRequiredElement<Button>(interfaceRoot, GameMenusScreenName, "new-game-start-button");
        newGameMessage = BindRequiredElement<Label>(interfaceRoot, GameMenusScreenName, "new-game-message");
        if (newGameStartButton != null)
            newGameStartButton.clicked += OnNewGameStartClicked;
        BindMenuButton("new-game-back-button", CloseNewGameSummary);
        BuildNewGameMapSizeChoice();
    }

    private readonly System.Collections.Generic.List<(MapSizeOption Option, Button Button)> newGameMapSizeButtons =
        new System.Collections.Generic.List<(MapSizeOption, Button)>();
    private Label newGameMapSizeSummary;

    // Размер карты свободной игры — перед «Начальными условиями».
    private void BuildNewGameMapSizeChoice()
    {
        Label startTitle = newGameOverlay?.Q<Label>("new-game-start-title");
        VisualElement panel = startTitle?.parent;
        if (panel == null || newGameMapSizeButtons.Count > 0)
            return;
        int index = Mathf.Max(0, panel.IndexOf(startTitle) - 1);

        Label section = new Label("КАРТА");
        section.AddToClassList("new-game-section");
        VisualElement row = new VisualElement { name = "new-game-map-size-row" };
        row.style.flexDirection = FlexDirection.Row;
        foreach (MapSizeOption option in CampaignStartOptions.MapSizes)
        {
            MapSizeOption captured = option;
            Button button = new Button(() => SelectNewGameMapSize(captured.HexesAcross)) { text = option.Title + " · " + option.HexesAcross };
            button.AddToClassList("game-menu-button");
            button.AddToClassList("game-menu-button-small");
            row.Add(button);
            newGameMapSizeButtons.Add((option, button));
        }
        newGameMapSizeSummary = new Label();
        newGameMapSizeSummary.AddToClassList("new-game-option-summary");
        // 12У-H: остальные основные параметры карты — строками кнопок.
        VisualElement options = BuildNewGameMapOptionRows();
        panel.Insert(index, options);
        panel.Insert(index, newGameMapSizeSummary);
        panel.Insert(index, row);
        panel.Insert(index, section);
    }

    // ---------- 12У-H: параметры карты новой партии ----------

    private readonly System.Collections.Generic.List<(string Key, int Value, Button Button)> newGameMapOptionButtons =
        new System.Collections.Generic.List<(string, int, Button)>();

    private static readonly (WorldMapRegionKind Kind, string Title)[] NewGameRegionKinds =
    {
        (WorldMapRegionKind.BySeed, "Любая"),
        (WorldMapRegionKind.RiverValley, "Речная долина"),
        (WorldMapRegionKind.ForestLake, "Лес и озеро"),
        (WorldMapRegionKind.HillAndLedge, "Холмы"),
        (WorldMapRegionKind.SettledLand, "Обжитая")
    };

    private VisualElement BuildNewGameMapOptionRows()
    {
        VisualElement box = new VisualElement { name = "new-game-map-options" };
        box.style.marginTop = 4;
        box.style.marginBottom = 6;
        VisualElement Row(string title)
        {
            VisualElement row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginTop = 2;
            Label label = new Label(title);
            label.AddToClassList("new-game-option-summary");
            label.style.width = 130;
            label.style.marginRight = 6;
            row.Add(label);
            box.Add(row);
            return row;
        }
        void Choice(VisualElement row, string key, int value, string text)
        {
            Button button = new Button(() => SelectNewGameMapOption(key, value)) { text = text, name = "new-game-option-" + key + "-" + value };
            button.AddToClassList("game-menu-button");
            button.AddToClassList("game-menu-button-small");
            row.Add(button);
            newGameMapOptionButtons.Add((key, value, button));
        }
        VisualElement kind = Row("Местность");
        foreach ((WorldMapRegionKind k, string title) in NewGameRegionKinds)
            Choice(kind, "region", (int)k, title);
        (string Key, string Title, string[] Levels)[] levels =
        {
            ("relief", "Рельеф", new[] { "Равнина", "Обычный", "Горы" }),
            ("water", "Реки и озёра", CampaignMapOptions.LevelTitles),
            ("forest", "Лес", CampaignMapOptions.LevelTitles),
            ("settlements", "Деревни", CampaignMapOptions.LevelTitles),
            ("discoveries", "Тайны и места", CampaignMapOptions.LevelTitles),
            ("enemies", "Враги", CampaignMapOptions.LevelTitles),
        };
        foreach ((string key, string title, string[] names) in levels)
        {
            VisualElement row = Row(title);
            for (int v = 0; v < names.Length; v++)
                Choice(row, key, v, names[v]);
        }
        return box;
    }

    private static int GetMapOption(CampaignMapOptions o, string key)
    {
        switch (key)
        {
            case "region": return (int)o.RegionKind;
            case "relief": return o.Relief;
            case "water": return o.Water;
            case "forest": return o.Forest;
            case "settlements": return o.Villages;
            case "discoveries": return o.Discoveries;
            case "enemies": return o.Enemies;
            default: return -1;
        }
    }

    private void SelectNewGameMapOption(string key, int value)
    {
        if (pendingSetup == null)
            return;
        CampaignMapOptions o = pendingSetup.MapOptions ??= new CampaignMapOptions();
        switch (key)
        {
            case "region": o.RegionKind = (WorldMapRegionKind)value; break;
            case "relief": o.Relief = value; break;
            case "water": o.Water = value; break;
            case "forest": o.Forest = value; break;
            case "settlements": o.Villages = value; break;
            case "discoveries": o.Discoveries = value; break;
            case "enemies": o.Enemies = value; break;
        }
        FillNewGameSummary();
    }

    private static void MarkSelected(Button button, bool on)
    {
        Color border = on ? new Color(0.83f, 0.69f, 0.42f) : new Color(0f, 0f, 0f, 0f);
        button.style.borderTopColor = button.style.borderBottomColor = button.style.borderLeftColor = button.style.borderRightColor = border;
        float width = on ? 2f : 0f;
        button.style.borderTopWidth = button.style.borderBottomWidth = button.style.borderLeftWidth = button.style.borderRightWidth = width;
    }

    private void FillNewGameMapOptions()
    {
        CampaignMapOptions o = pendingSetup?.MapOptions ?? new CampaignMapOptions();
        foreach ((string key, int value, Button button) in newGameMapOptionButtons)
            MarkSelected(button, GetMapOption(o, key) == value);
    }

    private void SelectNewGameMapSize(int hexesAcross)
    {
        if (pendingSetup == null)
            return;
        pendingSetup.MapHexesAcross = hexesAcross;
        FillNewGameSummary();
    }

    private void FillNewGameMapSize()
    {
        int selected = pendingSetup != null ? pendingSetup.MapHexesAcross : CampaignStartOptions.DefaultMapHexesAcross;
        foreach ((MapSizeOption option, Button button) in newGameMapSizeButtons)
        {
            bool on = option.HexesAcross == selected;
            Color border = on ? new Color(0.83f, 0.69f, 0.42f) : new Color(0f, 0f, 0f, 0f);
            button.style.borderTopColor = border;
            button.style.borderBottomColor = border;
            button.style.borderLeftColor = border;
            button.style.borderRightColor = border;
            float width = on ? 2f : 0f;
            button.style.borderTopWidth = width;
            button.style.borderBottomWidth = width;
            button.style.borderLeftWidth = width;
            button.style.borderRightWidth = width;
        }
        if (newGameMapSizeSummary != null)
            newGameMapSizeSummary.text = CampaignStartOptions.FindMapSize(selected)?.Summary ?? string.Empty;
    }

    private void OpenNewGameSummary()
    {
        if (newGameOverlay == null)
            return;

        // Канон v1.59: режим один — свободная игра.
        pendingSetup = new CampaignSetup();
        FillNewGameSummary();
        OpenMenuOverlay(newGameOverlay);
    }

    private void FillNewGameSummary()
    {
        FillOption("new-game-commander", CampaignStartOptions.Find(CampaignStartOptions.Commanders, pendingSetup.CommanderProfileId));
        FillNewGameMapSize();
        FillNewGameMapOptions();

        bool valid = pendingSetup.Validate(out string reason);
        newGameMessage.text = valid ? string.Empty : "Нельзя начать: " + reason + ".";
        newGameStartButton.SetEnabled(valid);
    }

    private void FillOption(string prefix, CampaignOptionDefinition option)
    {
        Label title = interfaceRoot.Q<Label>(prefix + "-title");
        Label summary = interfaceRoot.Q<Label>(prefix + "-summary");
        if (title != null)
            title.text = option != null ? option.Title : "—";
        if (summary != null)
            summary.text = option != null ? option.Summary : string.Empty;
    }

    private void OnNewGameStartClicked()
    {
        // Повторный клик не создаёт вторую кампанию: выбор расходуется сразу.
        CampaignSetup setup = pendingSetup;
        pendingSetup = null;
        if (setup == null)
            return;

        newGameStartButton.SetEnabled(false);
        // Сначала экран загрузки (виден сразу), мир создаётся следующим кадром.
        ShowLoadingScreen("Создаём мир…", 0.02f);
        rootVisualElementForLoading()?.schedule.Execute(() =>
        {
            StartNewGame(setup);
            CloseMenuOverlay(newGameOverlay);
            HideMainMenu();
            loadingCampaignReady = true;
        }).ExecuteLater(30);
    }

    private void CloseNewGameSummary()
    {
        pendingSetup = null;
        CloseMenuOverlay(newGameOverlay);
    }

    private void OnMainMenuLoadClicked()
    {
        OpenSavesPicker(savingMode: false);
    }

    private void SetNewGameConfirmOpen(bool open)
    {
        mainMenuNewGameConfirm?.EnableInClassList(GameMenuConfirmOpenClass, open);
    }

    // ------------------------------------------------------------------
    // Пауза (Esc)
    // ------------------------------------------------------------------

    private void HandleGameMenuHotkeys()
    {
        Keyboard keyboard = Keyboard.current;
        if (!gameMenusBound || keyboard == null || !keyboard.escapeKey.wasPressedThisFrame)
            return;

        if (IsSettingsOpen)
        {
            CloseSettings();
            return;
        }

        if (IsNewGameSummaryOpen)
        {
            CloseNewGameSummary();
            return;
        }

        if (IsSavesPickerOpen)
        {
            CloseSavesPicker();
            return;
        }

        if (IsPauseMenuOpen)
        {
            ClosePauseMenu();
            return;
        }

        if (IsMainMenuOpen)
        {
            if (CampaignSession.HasActive && gameState != null)
                HideMainMenu();
            return;
        }

        // Бриф правок п. 11: журнал — отдельный экран, Esc возвращает туда,
        // откуда его открыли (карта или поселение).
        if (IsJournalOpen && !IsNarrativeDialogueActive && activeQueuedModal == null)
        {
            CloseJournal();
            return;
        }

        // Экран героя закрывается по Esc, если поверх него ничего нет.
        if (IsHeroScreenOpen && !IsNarrativeDialogueActive && activeQueuedModal == null)
        {
            CloseHeroScreen();
            return;
        }

        // Esc сначала закрывает то, что открыто поверх карты: карточку места,
        // экран героя, журнал, диалог — у них свои правила закрытия.
        if (gameState == null || isGameOver || HasBlockingModalWork() || IsWorldMapLocationCardVisible())
            return;

        OpenPauseMenu();
    }

    private bool IsWorldMapLocationCardVisible()
    {
        return worldMapLocationCard != null &&
               worldMapLocationCard.resolvedStyle.display == DisplayStyle.Flex;
    }

    private void OpenPauseMenu()
    {
        pauseMenuMessage.text = string.Empty;
        OpenMenuOverlay(pauseMenuOverlay);
    }

    private void ClosePauseMenu()
    {
        CloseMenuOverlay(pauseMenuOverlay);
    }

    private void OnPauseMenuSaveClicked()
    {
        OpenSavesPicker(savingMode: true);
    }

    private void OnPauseMenuLoadClicked()
    {
        OpenSavesPicker(savingMode: false);
    }

    // ------------------------------------------------------------------
    // Выбор слота: сохранение — только в ручные слоты, загрузка — из любого.
    // ------------------------------------------------------------------

    private VisualElement savesOverlay;
    private Label savesTitle;
    private VisualElement savesList;
    private Label savesMessage;
    private VisualTreeAsset saveSlotRowTemplate;
    private bool savesPickerSaving;

    private bool IsSavesPickerOpen => IsOverlayOpen(savesOverlay);

    private void BindSavesPicker()
    {
        savesOverlay = BindRequiredElement<VisualElement>(interfaceRoot, GameMenusScreenName, "saves-overlay");
        savesTitle = BindRequiredElement<Label>(interfaceRoot, GameMenusScreenName, "saves-title");
        savesList = BindRequiredElement<VisualElement>(interfaceRoot, GameMenusScreenName, "saves-list");
        savesMessage = BindRequiredElement<Label>(interfaceRoot, GameMenusScreenName, "saves-message");
        BindMenuButton("saves-close-button", CloseSavesPicker);
    }

    private void OpenSavesPicker(bool savingMode)
    {
        if (savesOverlay == null)
            return;

        savesPickerSaving = savingMode;
        savesTitle.text = savingMode ? "СОХРАНИТЬ" : "ЗАГРУЗИТЬ";
        savesMessage.text = string.Empty;
        RebuildSavesList();
        OpenMenuOverlay(savesOverlay);
    }

    private void CloseSavesPicker()
    {
        CloseMenuOverlay(savesOverlay);
        if (IsMainMenuOpen)
            RefreshMainMenuButtons();
    }

    private void RebuildSavesList()
    {
        savesList.Clear();
        if (saveSlotRowTemplate == null)
            saveSlotRowTemplate = Resources.Load<VisualTreeAsset>("Templates/SaveSlotRow");
        if (saveSlotRowTemplate == null)
            return;

        foreach (string slotId in CampaignSaveStore.AllSlotIds)
        {
            // Автосохранение пишет только игра.
            if (savesPickerSaving && slotId == CampaignSaveStore.AutosaveSlotId)
                continue;

            SaveSlotSummary summary = ReadSlotSummary(slotId);
            TemplateContainer instance = saveSlotRowTemplate.Instantiate();
            Button row = instance.Q<Button>("save-slot-row");
            row.Q<Label>("save-slot-title").text = CampaignSaveStore.GetSlotTitle(slotId);
            row.Q<Label>("save-slot-description").text = summary.Description;
            row.SetEnabled(savesPickerSaving || summary.IsLoadable);

            string captured = slotId;
            row.clicked += () => OnSaveSlotClicked(captured);
            savesList.Add(instance);
        }
    }

    private void OnSaveSlotClicked(string slotId)
    {
        if (savesPickerSaving)
        {
            SaveCampaign(slotId);
            savesMessage.text = lastCampaignIoMessage;
            RebuildSavesList();
            return;
        }

        if (!LoadCampaign(slotId))
        {
            savesMessage.text = lastCampaignIoMessage;
            return;
        }

        // Загрузка — сразу в игру, все меню закрываются.
        CloseMenuOverlay(savesOverlay);
        CloseMenuOverlay(pauseMenuOverlay);
        CloseMenuOverlay(mainMenuOverlay);
    }

    // Кампания остаётся в памяти: «Продолжить» в главном меню вернёт в неё.
    private void OnPauseMenuMainMenuClicked()
    {
        CloseMenuOverlay(pauseMenuOverlay);
        ShowMainMenu();
    }

    // ------------------------------------------------------------------
    // Настройки
    // ------------------------------------------------------------------

    private void OpenSettings()
    {
        settingsFullscreenToggle.SetValueWithoutNotify(Screen.fullScreen);
        settingsVolumeSlider.SetValueWithoutNotify(AudioListener.volume);
        OpenMenuOverlay(settingsOverlay);
    }

    private void CloseSettings()
    {
        CloseMenuOverlay(settingsOverlay);
    }

    private static float LoadSavedVolume()
    {
        return PlayerPrefs.GetFloat(VolumePrefsKey, 1f);
    }

    private static void ApplyVolume(float volume, bool persist)
    {
        AudioListener.volume = Mathf.Clamp01(volume);
        if (!persist)
            return;

        PlayerPrefs.SetFloat(VolumePrefsKey, AudioListener.volume);
        PlayerPrefs.Save();
    }

    private static void QuitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // ------------------------------------------------------------------
    // Общее
    // ------------------------------------------------------------------

    // Меню — самый верхний слой; пока оно открыто, время стоит.
    private void OpenMenuOverlay(VisualElement overlay)
    {
        if (overlay == null)
            return;

        overlay.AddToClassList(GameMenuOpenClass);
        overlay.BringToFront();
        PauseForBlockingModal();
        RefreshTimeControlAvailability();
    }

    private void CloseMenuOverlay(VisualElement overlay)
    {
        if (overlay == null)
            return;

        overlay.RemoveFromClassList(GameMenuOpenClass);
        ResumeAfterBlockingModalIfReady();
    }

    // Результат последнего сохранения/загрузки — и в донесения, и в меню.
    private string lastCampaignIoMessage = string.Empty;

    private void ReportCampaignIo(string message)
    {
        lastCampaignIoMessage = message;
        if (gameState != null)
            AddReport(message);
    }
}

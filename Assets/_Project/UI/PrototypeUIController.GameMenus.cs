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
        EnsureWorldMapGeographyConfigured();

        gameState = campaign;
        isGameOver = false;
        lastNavigationClickTime = -NavigationClickCooldownSeconds;
        unreadIncidents.Clear();
        reportRequiresAcknowledgement.Clear();
        reportReadStates.Clear();
        homePeopleSignature = null;
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
            HideMainMenu();
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
        StartNewGame(new CampaignSetup());
        CloseMenuOverlay(newGameOverlay);
        HideMainMenu();
    }

    // ПР-12А: то же для свободной игры.
    private void StartNewFreePlayFromMenu()
    {
        SetNewGameConfirmOpen(false);
        StartNewGame(new CampaignSetup { CrisisId = CampaignStartOptions.FreePlayId });
        CloseMenuOverlay(newGameOverlay);
        HideMainMenu();
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
        BindMenuButton("new-game-mode-free", () => SelectNewGameMode(CampaignStartOptions.FreePlayId));
        BindMenuButton("new-game-mode-story", () => SelectNewGameMode(CampaignStartOptions.HomeOnForeignWaterCrisisId));
    }

    // ПР-12А: выбор режима меняет только ещё не созданную кампанию.
    private void SelectNewGameMode(string modeId)
    {
        if (pendingSetup == null)
            return;
        pendingSetup.CrisisId = modeId;
        FillNewGameSummary();
    }

    private void OpenNewGameSummary()
    {
        if (newGameOverlay == null)
            return;

        // ПР-12А: по умолчанию — свободная игра, основа игры (канон v1.45).
        pendingSetup = new CampaignSetup { CrisisId = CampaignStartOptions.FreePlayId };
        FillNewGameSummary();
        OpenMenuOverlay(newGameOverlay);
    }

    private void FillNewGameSummary()
    {
        interfaceRoot.Q<Button>("new-game-mode-free")?.EnableInClassList("new-game-mode-selected",
            pendingSetup.CrisisId == CampaignStartOptions.FreePlayId);
        interfaceRoot.Q<Button>("new-game-mode-story")?.EnableInClassList("new-game-mode-selected",
            pendingSetup.CrisisId == CampaignStartOptions.HomeOnForeignWaterCrisisId);
        FillOption("new-game-crisis", CampaignStartOptions.Find(CampaignStartOptions.Crises, pendingSetup.CrisisId));
        FillOption("new-game-commander", CampaignStartOptions.Find(CampaignStartOptions.Commanders, pendingSetup.CommanderProfileId));
        FillOption("new-game-start", CampaignStartOptions.Find(CampaignStartOptions.StartingConditions, pendingSetup.StartingConditionId));

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
        StartNewGame(setup);
        CloseMenuOverlay(newGameOverlay);
        HideMainMenu();
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

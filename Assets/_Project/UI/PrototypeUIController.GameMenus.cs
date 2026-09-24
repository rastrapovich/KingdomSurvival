using System;
using System.IO;
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
    private bool IsGameMenuOpen => IsMainMenuOpen || IsPauseMenuOpen || IsSettingsOpen;

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
        BindMenuButton("main-menu-new-game-yes-button", StartNewGameFromMenu);
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
        selectedFighterIds.Clear();
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
        RefreshMainMenuButtons();
        OpenMenuOverlay(mainMenuOverlay);
    }

    private void HideMainMenu()
    {
        CloseMenuOverlay(mainMenuOverlay);
    }

    private void RefreshMainMenuButtons()
    {
        SaveSummary save = ReadSaveSummary();
        bool hasSession = CampaignSession.HasActive && gameState != null;

        if (hasSession)
        {
            mainMenuContinueButton.SetEnabled(true);
            mainMenuContinueHint.text = "Вернуться в текущую партию · день " + gameState.Day;
        }
        else if (save.IsLoadable)
        {
            mainMenuContinueButton.SetEnabled(true);
            mainMenuContinueHint.text = "Последнее сохранение · " + save.Description;
        }
        else
        {
            mainMenuContinueButton.SetEnabled(false);
            mainMenuContinueHint.text = save.Exists
                ? "Сохранение нельзя загрузить: " + save.Description
                : "Нет начатой партии и сохранений.";
        }

        mainMenuLoadButton.SetEnabled(save.IsLoadable);
        mainMenuLoadHint.text = save.Exists
            ? (save.IsLoadable ? "Сохранение · " : "Сохранение нельзя загрузить: ") + save.Description
            : "Сохранений пока нет.";
    }

    private void OnMainMenuContinueClicked()
    {
        if (CampaignSession.HasActive && gameState != null)
        {
            HideMainMenu();
            return;
        }

        if (LoadCampaign())
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

        StartNewGameFromMenu();
    }

    private void StartNewGameFromMenu()
    {
        SetNewGameConfirmOpen(false);
        StartNewGame();
        HideMainMenu();
    }

    private void OnMainMenuLoadClicked()
    {
        if (LoadCampaign())
            HideMainMenu();
        else
            mainMenuMessage.text = lastCampaignIoMessage;
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
        pauseMenuLoadButton.SetEnabled(ReadSaveSummary().IsLoadable);
        OpenMenuOverlay(pauseMenuOverlay);
    }

    private void ClosePauseMenu()
    {
        CloseMenuOverlay(pauseMenuOverlay);
    }

    private void OnPauseMenuSaveClicked()
    {
        SaveCampaign();
        pauseMenuMessage.text = lastCampaignIoMessage;
        pauseMenuLoadButton.SetEnabled(ReadSaveSummary().IsLoadable);
    }

    private void OnPauseMenuLoadClicked()
    {
        if (LoadCampaign())
            ClosePauseMenu();
        else
            pauseMenuMessage.text = lastCampaignIoMessage;
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

    private readonly struct SaveSummary
    {
        public readonly bool Exists;
        public readonly bool IsLoadable;
        public readonly string Description;

        public SaveSummary(bool exists, bool isLoadable, string description)
        {
            Exists = exists;
            IsLoadable = isLoadable;
            Description = description;
        }
    }

    // Лёгкая проверка файла для меню: повреждённое или несовместимое
    // сохранение видно сразу, а не после попытки загрузки.
    private SaveSummary ReadSaveSummary()
    {
        string path = CampaignSavePath;
        if (!File.Exists(path))
            return new SaveSummary(false, false, string.Empty);

        try
        {
            CampaignSaveData data = JsonUtility.FromJson<CampaignSaveData>(File.ReadAllText(path));
            if (data == null || data.State == null)
                return new SaveSummary(true, false, "файл повреждён");
            if (data.SaveFormatVersion != CampaignSaveService.CurrentSaveFormatVersion)
                return new SaveSummary(true, false, "старый формат сохранения");

            string when = File.GetLastWriteTime(path).ToString("dd.MM HH:mm");
            return new SaveSummary(true, true, "день " + data.State.Day + " · " + when);
        }
        catch (Exception)
        {
            return new SaveSummary(true, false, "файл повреждён");
        }
    }
}

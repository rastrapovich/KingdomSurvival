using System.Collections.Generic;
using KingdomSurvival.Chapter01;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Лагерь (P09-T05) — не survival-симулятор и не отдельный CampManager:
/// маленький fullscreen-слой, программно построенный по образцу Journal/
/// Hero Screen (та же палитра и те же хелперы), который только показывает
/// отряд и доступную авторскую camp-сцену через уже существующую Dialogue
/// System (Chapter01CampSceneProvider). Открытие/закрытие не двигает
/// маршрут экспедиции и не тратит время само по себе — оно лишь ставит/
/// снимает общую блокирующую паузу (см. IsCampScreenOpen в ModalQueue.cs и
/// комментарий там же).
/// </summary>
public partial class PrototypeUIController
{
    private VisualElement campOverlay;
    private Button campNavButton;
    private Button campCloseButton;
    private Button campContinueButton;

    private Label campDayLabel;
    private VisualElement campRosterList;
    private VisualElement campSceneList;

    private bool isCampScreenOpen;

    // Читается из ModalQueue.cs (HasBlockingModalWork/HasBlockingModalWorkExceptCamp).
    private bool IsCampScreenOpen => isCampScreenOpen;

    // ------------------------------------------------------------------
    // Инициализация
    // ------------------------------------------------------------------

    private void InitializeCampUi()
    {
        if (interfaceRoot == null || campOverlay != null)
            return;

        VisualElement screen = interfaceRoot.Q<VisualElement>("screen");
        if (screen == null)
            return;

        BuildCampScreen(screen);

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
    // + активная экспедиция + герой не дома. Обязательное событие/бой уже
    // отдельно занимают HasBlockingModalWork — кнопка тогда просто disabled
    // (см. RefreshCampNavButtonState), открыть поверх них нельзя.
    private bool CanOpenCampScreen()
    {
        return gameState != null &&
               !isGameOver &&
               gameState.Narrative != null &&
               gameState.Narrative.HasFlag(Chapter01Ids.Flags.CampUnlocked) &&
               gameState.HasActiveExpedition;
    }

    private void OpenCampScreen()
    {
        if (campOverlay == null || isCampScreenOpen || !CanOpenCampScreen())
            return;

        // Camp — третий fullscreen-слой наравне с Journal/Hero Screen
        // (раздел 20 инструкции P08J про Journal/Hero Screen расширяется на
        // Camp тем же принципом: одновременно открытым может быть только
        // один).
        CloseJournal();
        CloseHeroScreen();

        isCampScreenOpen = true;
        PauseForBlockingModal();

        campOverlay.style.display = DisplayStyle.Flex;
        campOverlay.BringToFront();
        RefreshCampScreen();

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
        if (campOverlay == null)
            return;

        campOverlay.style.display = DisplayStyle.None;
        isCampScreenOpen = false;

        // Раздел "Выход из лагеря": не строит маршрут заново и не
        // телепортирует — просто отдаёт паузу тому же механизму, который её
        // поставил (PauseForBlockingModal/ResumeAfterBlockingModalIfReady),
        // ничего не трогая в ActiveExpedition.Route/Phase.
        ResumeAfterBlockingModalIfReady();
    }

    private void OnCampContinueClicked()
    {
        CloseCampScreen();
    }

    // ------------------------------------------------------------------
    // Построение
    // ------------------------------------------------------------------

    private void BuildCampScreen(VisualElement screen)
    {
        campOverlay = new VisualElement { name = "camp-overlay" };
        campOverlay.style.position = Position.Absolute;
        campOverlay.style.left = 0f;
        campOverlay.style.right = 0f;
        campOverlay.style.top = 0f;
        campOverlay.style.bottom = 0f;
        campOverlay.style.backgroundColor = HeroScreenBackdrop;
        campOverlay.style.display = DisplayStyle.None;
        campOverlay.style.paddingLeft = 18f;
        campOverlay.style.paddingRight = 18f;
        campOverlay.style.paddingTop = 12f;
        campOverlay.style.paddingBottom = 12f;
        screen.Add(campOverlay);

        campOverlay.Add(BuildCampHeader());

        VisualElement columns = new VisualElement { name = "camp-columns" };
        columns.style.flexDirection = FlexDirection.Row;
        columns.style.flexGrow = 1f;
        columns.style.minHeight = 0f;
        campOverlay.Add(columns);

        columns.Add(BuildCampRosterColumn());
        columns.Add(BuildCampSceneColumn());

        campOverlay.Add(BuildCampFooter());
    }

    private VisualElement BuildCampHeader()
    {
        VisualElement header = new VisualElement { name = "camp-header" };
        header.style.flexDirection = FlexDirection.Row;
        header.style.alignItems = Align.Center;
        header.style.justifyContent = Justify.SpaceBetween;
        header.style.height = 44f;
        header.style.flexShrink = 0f;
        header.style.marginBottom = 10f;

        Label title = new Label("ЛАГЕРЬ") { name = "camp-title" };
        title.style.color = HeroScreenGold;
        title.style.fontSize = 20f;
        title.style.unityFontStyleAndWeight = FontStyle.Bold;
        header.Add(title);

        campDayLabel = new Label(string.Empty) { name = "camp-day-label" };
        campDayLabel.style.color = HeroScreenMuted;
        campDayLabel.style.fontSize = 12f;
        header.Add(campDayLabel);

        campCloseButton = new Button(CloseCampScreen) { name = "camp-close-button", text = "ЗАКРЫТЬ" };
        StyleHeroScreenButton(campCloseButton, 120f, 32f);
        header.Add(campCloseButton);
        return header;
    }

    private VisualElement BuildCampRosterColumn()
    {
        VisualElement panel = CreateHeroScreenPanel("camp-roster-panel", "ОТРЯД");
        panel.style.width = new Length(28f, LengthUnit.Percent);
        panel.style.marginRight = 12f;
        panel.style.minWidth = 0f;

        campRosterList = new VisualElement { name = "camp-roster-list" };
        panel.Add(campRosterList);
        return panel;
    }

    // v1: только фон-заглушка под место у костра (раздел "Арт лагеря"
    // инструкции — тёмный фон, без сложной иллюстрации сейчас).
    private VisualElement BuildCampSceneColumn()
    {
        VisualElement column = new VisualElement { name = "camp-scene-column" };
        column.style.flexGrow = 1f;
        column.style.minWidth = 0f;
        column.style.marginRight = 12f;
        column.style.backgroundColor = HeroScreenPanelDeep;
        SetHeroScreenBorder(column, 1f);
        SetHeroScreenRadius(column, 4f);

        Label placeholder = new Label("КОСТЁР") { name = "camp-scene-placeholder" };
        placeholder.style.color = HeroScreenMuted;
        placeholder.style.fontSize = 12f;
        placeholder.style.unityTextAlign = TextAnchor.MiddleCenter;
        placeholder.style.flexGrow = 1f;
        column.Add(placeholder);

        VisualElement scenesPanel = CreateHeroScreenPanel("camp-scenes-panel", "СЦЕНЫ");
        scenesPanel.style.width = new Length(30f, LengthUnit.Percent);
        scenesPanel.style.marginRight = 0f;
        campSceneList = new VisualElement { name = "camp-scene-list" };
        scenesPanel.Add(campSceneList);

        VisualElement row = new VisualElement { name = "camp-scene-row" };
        row.style.flexDirection = FlexDirection.Row;
        row.style.flexGrow = 1f;
        row.style.minHeight = 0f;
        row.Add(column);
        row.Add(scenesPanel);

        VisualElement wrapper = new VisualElement { name = "camp-scene-wrapper" };
        wrapper.style.flexGrow = 1f;
        wrapper.style.minHeight = 0f;
        wrapper.Add(row);
        return wrapper;
    }

    private VisualElement BuildCampFooter()
    {
        VisualElement footer = new VisualElement { name = "camp-footer" };
        footer.style.flexDirection = FlexDirection.Row;
        footer.style.justifyContent = Justify.FlexEnd;
        footer.style.flexShrink = 0f;
        footer.style.marginTop = 10f;

        campContinueButton = new Button(OnCampContinueClicked)
        {
            name = "camp-continue-button",
            text = "ПРОДОЛЖИТЬ ПУТЬ"
        };
        StyleHeroScreenButton(campContinueButton, 220f, 36f);
        footer.Add(campContinueButton);
        return footer;
    }

    // ------------------------------------------------------------------
    // Наполнение данными
    // ------------------------------------------------------------------

    private void RefreshCampScreen()
    {
        if (campOverlay == null || gameState == null)
            return;

        if (campDayLabel != null)
        {
            ContinuousClockSnapshot clock = ContinuousSimulationSystem.GetClock(gameState);
            campDayLabel.text = "День " + gameState.Day + " · " + ContinuousSimulationSystem.FormatClock(clock.HourOfDay);
        }

        RefreshCampRoster();
        RefreshCampSceneList();
    }

    // Раздел "Слева использовать реальные Hero + ActiveExpedition.FighterIds.
    // Не первые четыре FighterData" — тот же состав, что уже показывает
    // Hero Screen/боковая панель похода, не отдельный источник правды.
    private void RefreshCampRoster()
    {
        if (campRosterList == null)
            return;

        campRosterList.Clear();

        CommanderData commander = gameState.GetSelectedCommander();
        campRosterList.Add(CreateCampRosterRow(commander != null ? commander.Name : "Командир", true));

        if (gameState.HasActiveExpedition)
        {
            foreach (string fighterId in gameState.ActiveExpedition.FighterIds)
            {
                FighterData fighter = gameState.FindFighter(fighterId);
                if (fighter != null)
                    campRosterList.Add(CreateCampRosterRow(fighter.Name, false));
            }
        }
    }

    private VisualElement CreateCampRosterRow(string name, bool isCommander)
    {
        VisualElement row = new VisualElement { name = "camp-roster-row" };
        row.style.paddingLeft = 6f;
        row.style.paddingRight = 6f;
        row.style.paddingTop = 6f;
        row.style.paddingBottom = 6f;
        row.style.marginBottom = 4f;
        row.style.backgroundColor = HeroScreenPanelDeep;
        SetHeroScreenBorder(row, isCommander ? 2f : 1f);
        SetHeroScreenRadius(row, 3f);

        Label label = new Label(name) { name = "camp-roster-row-name" };
        label.style.color = HeroScreenText;
        label.style.fontSize = 12f;
        label.style.unityFontStyleAndWeight = isCommander ? FontStyle.Bold : FontStyle.Normal;
        row.Add(label);
        return row;
    }

    private void RefreshCampSceneList()
    {
        if (campSceneList == null)
            return;

        campSceneList.Clear();

        CampSceneViewData? scene = Chapter01CampSceneProvider.GetAvailableScene(gameState);
        if (!scene.HasValue)
        {
            Label empty = new Label("Пока нечего рассказать.") { name = "camp-scene-list-empty" };
            empty.style.color = HeroScreenMuted;
            empty.style.fontSize = 11f;
            empty.style.whiteSpace = WhiteSpace.Normal;
            campSceneList.Add(empty);
            return;
        }

        VisualElement row = new VisualElement { name = "camp-scene-list-row" };
        row.style.paddingLeft = 6f;
        row.style.paddingRight = 6f;
        row.style.paddingTop = 6f;
        row.style.paddingBottom = 6f;
        row.style.backgroundColor = HeroScreenPanelDeep;
        SetHeroScreenBorder(row, 1f);
        SetHeroScreenRadius(row, 3f);

        Label title = new Label("● " + scene.Value.Title) { name = "camp-scene-list-row-title" };
        title.style.color = HeroScreenText;
        title.style.fontSize = 12f;
        row.Add(title);
        campSceneList.Add(row);
    }

    // Раздел "Кнопка" инструкции про лагерь: до разблокировки кнопки нет
    // вообще, после — видима, но disabled вне похода/вне возможности
    // открыть (обязательное событие, бой) с подсказывающим tooltip.
    private void RefreshCampNavButtonState()
    {
        if (campNavButton == null || gameState == null)
            return;

        bool unlocked = gameState.Narrative != null && gameState.Narrative.HasFlag(Chapter01Ids.Flags.CampUnlocked);
        campNavButton.style.display = unlocked ? DisplayStyle.Flex : DisplayStyle.None;
        if (!unlocked)
            return;

        bool canOpen = CanOpenCampScreen() && !HasBlockingModalWorkExceptCamp();
        campNavButton.SetEnabled(canOpen || isCampScreenOpen);
        campNavButton.tooltip = gameState.HasActiveExpedition
            ? "Остановиться лагерем."
            : "Лагерь доступен во время похода.";
    }
}

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

// ============================================================
// СВЯЗЬ ИГРОВЫХ ДАННЫХ С ИНТЕРФЕЙСОМ
// Этот компонент должен находиться рядом с UI Document.
// ============================================================

[RequireComponent(typeof(UIDocument))]
public class PrototypeUIController : MonoBehaviour
{
    // ========================================================
    // ИГРОВОЕ СОСТОЯНИЕ
    // ========================================================

    private GameState gameState;

    // ========================================================
    // ВЕРХНЯЯ ПАНЕЛЬ
    // ========================================================

    private Label dayLabel;
    private Label goldLabel;
    private Label foodLabel;
    private Label populationLabel;
    private Label moodLabel;
    private Label foodConsumptionLabel;

    private Button endDayButton;

    // ========================================================
    // ПАНЕЛЬ АРМИИ
    // ========================================================

    private DropdownField commanderDropdown;
    private Label commanderDetailLabel;
    private Label armyStatusLabel;

    // ========================================================
    // ПАНЕЛЬ ЭКСПЕДИЦИИ
    // ========================================================

    private Label expeditionStatusLabel;
    private Label reportLabel;

    private Button sendRuinsButton;
    private Button sendMineButton;
    private Button sendForestButton;

    // ========================================================
    // КАРТОЧКА АКТИВНОЙ ЭКСПЕДИЦИИ
    // ========================================================

    private VisualElement activeExpeditionCard;
    private Label activeExpeditionTitle;
    private Label activeExpeditionDetails;
    private Label activeExpeditionLastReport;
    private Button returnExpeditionButton;

    // Показывает, подключены ли события кнопок.
    private bool callbacksRegistered;

    // ========================================================
    // ЗАПУСК КОНТРОЛЛЕРА
    // ========================================================

    private void OnEnable()
    {
        UIDocument document = GetComponent<UIDocument>();
        VisualElement root = document.rootVisualElement;

        FindInterfaceElements(root);

        if (!AllRequiredElementsExist())
        {
            Debug.LogError(
                "PrototypeUIController: в Prototype_Main.uxml " +
                "отсутствуют необходимые элементы.");

            enabled = false;
            return;
        }

        // Создаём новое временное состояние игры.
        // Сохранения добавим позже.
        gameState = new GameState();
        gameState.CreateNewGame();

        ConfigureCommanderDropdown();
        RegisterCallbacks();

        reportLabel.text =
            "Прототип запущен. Выберите командира " +
            "или отправьте экспедицию.";

        RefreshInterface();
    }

    // ========================================================
    // ПОИСК ЭЛЕМЕНТОВ В UXML
    // Имена здесь должны совпадать с атрибутами name в UXML.
    // ========================================================

    private void FindInterfaceElements(VisualElement root)
    {
        // Верхняя панель
        dayLabel = root.Q<Label>("day-label");
        goldLabel = root.Q<Label>("gold-label");
        foodLabel = root.Q<Label>("food-label");

        populationLabel =
            root.Q<Label>("population-label");

        moodLabel = root.Q<Label>("mood-label");

        foodConsumptionLabel =
            root.Q<Label>("food-consumption-label");

        endDayButton =
            root.Q<Button>("end-day-button");

        // Армия
        commanderDropdown =
            root.Q<DropdownField>("commander-dropdown");

        commanderDetailLabel =
            root.Q<Label>("commander-detail-label");

        armyStatusLabel =
            root.Q<Label>("army-status-label");

        // Экспедиции
        expeditionStatusLabel =
            root.Q<Label>("expedition-status-label");

        reportLabel =
            root.Q<Label>("report-label");

        sendRuinsButton =
            root.Q<Button>("send-ruins-button");

        sendMineButton =
            root.Q<Button>("send-mine-button");

        sendForestButton =
            root.Q<Button>("send-forest-button");

        // Карточка активной экспедиции
        activeExpeditionCard =
            root.Q<VisualElement>("active-expedition-card");

        activeExpeditionTitle =
            root.Q<Label>("active-expedition-title");

        activeExpeditionDetails =
            root.Q<Label>("active-expedition-details");

        activeExpeditionLastReport =
            root.Q<Label>("active-expedition-last-report");

        returnExpeditionButton =
            root.Q<Button>("return-expedition-button");
    }

    // ========================================================
    // ПРОВЕРКА UXML
    // Если имя хотя бы одного элемента неправильное,
    // Unity покажет нашу понятную ошибку в Console.
    // ========================================================

    private bool AllRequiredElementsExist()
    {
        return
            dayLabel != null &&
            goldLabel != null &&
            foodLabel != null &&
            populationLabel != null &&
            moodLabel != null &&
            foodConsumptionLabel != null &&
            endDayButton != null &&

            commanderDropdown != null &&
            commanderDetailLabel != null &&
            armyStatusLabel != null &&

            expeditionStatusLabel != null &&
            reportLabel != null &&
            sendRuinsButton != null &&
            sendMineButton != null &&
            sendForestButton != null &&

            activeExpeditionCard != null &&
            activeExpeditionTitle != null &&
            activeExpeditionDetails != null &&
            activeExpeditionLastReport != null &&
            returnExpeditionButton != null;
    }

    // ========================================================
    // НАСТРОЙКА СПИСКА КОМАНДИРОВ
    // ========================================================

    private void ConfigureCommanderDropdown()
    {
        List<string> commanderNames =
            gameState.GetCommanderNames();

        commanderDropdown.choices = commanderNames;

        CommanderData selectedCommander =
            gameState.GetSelectedCommander();

        commanderDropdown.SetValueWithoutNotify(
            selectedCommander.Name);
    }

    // ========================================================
    // ПОДКЛЮЧЕНИЕ СОБЫТИЙ
    // ========================================================

    private void RegisterCallbacks()
    {
        endDayButton.clicked += OnEndDayClicked;

        sendRuinsButton.clicked += OnSendRuinsClicked;
        sendMineButton.clicked += OnSendMineClicked;
        sendForestButton.clicked += OnSendForestClicked;

        returnExpeditionButton.clicked +=
            OnReturnExpeditionClicked;

        commanderDropdown.RegisterValueChangedCallback(
            OnCommanderChanged);

        callbacksRegistered = true;
    }

    // ========================================================
    // ОТКЛЮЧЕНИЕ СОБЫТИЙ
    // Это предотвращает повторное срабатывание кнопок.
    // ========================================================

    private void OnDisable()
    {
        if (!callbacksRegistered)
            return;

        endDayButton.clicked -= OnEndDayClicked;

        sendRuinsButton.clicked -= OnSendRuinsClicked;
        sendMineButton.clicked -= OnSendMineClicked;
        sendForestButton.clicked -= OnSendForestClicked;

        returnExpeditionButton.clicked -=
            OnReturnExpeditionClicked;

        commanderDropdown.UnregisterValueChangedCallback(
            OnCommanderChanged);

        callbacksRegistered = false;
    }

    // ========================================================
    // СМЕНА КОМАНДИРА
    // Новый командир возглавляет ту же единственную армию.
    // ========================================================

    private void OnCommanderChanged(
        ChangeEvent<string> changeEvent)
    {
        bool commanderChanged =
            gameState.SelectCommanderByName(
                changeEvent.newValue);

        if (commanderChanged)
        {
            reportLabel.text =
                changeEvent.newValue +
                " назначен командиром единственной армии.";
        }

        RefreshInterface();
    }

    // ========================================================
    // ЗАВЕРШЕНИЕ ДНЯ
    // ========================================================

    private void OnEndDayClicked()
    {
        DayResolutionResult result =
            DayResolver.ResolveDay(gameState);

        reportLabel.text =
            string.Join("\n", result.Messages);

        RefreshInterface();
    }

    // ========================================================
    // КНОПКИ ОТПРАВКИ
    // ========================================================

    private void OnSendRuinsClicked()
    {
        TrySendExpedition("ruins");
    }

    private void OnSendMineClicked()
    {
        TrySendExpedition("mine");
    }

    private void OnSendForestClicked()
    {
        TrySendExpedition("forest");
    }

    // ========================================================
    // СОЗДАНИЕ ЭКСПЕДИЦИИ
    // ========================================================

    private void TrySendExpedition(string locationId)
    {
        string resultMessage;

        gameState.TryStartExpedition(
            locationId,
            out resultMessage);

        reportLabel.text = resultMessage;

        RefreshInterface();
    }

    // ========================================================
    // ПРИКАЗ О ВОЗВРАЩЕНИИ
    // ========================================================

    private void OnReturnExpeditionClicked()
    {
        string resultMessage;

        gameState.TryOrderReturn(out resultMessage);

        reportLabel.text = resultMessage;

        RefreshInterface();
    }

    // ========================================================
    // ОБНОВЛЕНИЕ ВСЕГО ИНТЕРФЕЙСА
    // ========================================================

    private void RefreshInterface()
    {
        dayLabel.text =
            "День: " + gameState.Day;

        goldLabel.text =
            "Золото: " + gameState.Gold;

        foodLabel.text =
            "Пища: " + gameState.Food;

        populationLabel.text =
            "Население: " + gameState.Population;

        moodLabel.text =
            "Настроение: " +
            gameState.Mood +
            "/100";

        foodConsumptionLabel.text =
            "Расход: " +
            gameState.DailyFoodConsumption +
            " в день";

        RefreshArmyPanel();
        RefreshExpeditionPanel();
    }

    // ========================================================
    // ОБНОВЛЕНИЕ ПАНЕЛИ АРМИИ
    // ========================================================

    private void RefreshArmyPanel()
    {
        CommanderData commander =
            gameState.GetSelectedCommander();

        commanderDetailLabel.text =
            "Выбран: " +
            commander.Name +
            " · " +
            GetCommanderStateText(commander.State);

        if (gameState.HasActiveExpedition)
        {
            armyStatusLabel.text =
                commander.Name +
                " → " +
                gameState.Fighters.Count +
                " отдельных воинов. " +
                "Армия находится вне столицы. " +
                "Столица не защищена этой армией.";
        }
        else
        {
            armyStatusLabel.text =
                commander.Name +
                " → " +
                gameState.Fighters.Count +
                " отдельных воинов. " +
                "Армия находится в столице и защищает её.";
        }
    }

    // ========================================================
    // ОБНОВЛЕНИЕ ПАНЕЛИ ЭКСПЕДИЦИИ
    // ========================================================

    private void RefreshExpeditionPanel()
    {
        bool expeditionActive =
            gameState.HasActiveExpedition;

        // Пока действует одна экспедиция,
        // вторую отправить нельзя.
        commanderDropdown.SetEnabled(!expeditionActive);

        sendRuinsButton.SetEnabled(!expeditionActive);
        sendMineButton.SetEnabled(!expeditionActive);
        sendForestButton.SetEnabled(!expeditionActive);

        // Показываем карточку только во время экспедиции.
        activeExpeditionCard.style.display =
            expeditionActive
                ? DisplayStyle.Flex
                : DisplayStyle.None;

        if (!expeditionActive)
        {
            expeditionStatusLabel.text =
                "Активная экспедиция: нет";

            return;
        }

        ExpeditionData expedition =
            gameState.ActiveExpedition;

        LocationData location =
            gameState.FindLocation(
                expedition.LocationId);

        CommanderData commander =
            gameState.FindCommander(
                expedition.CommanderId);

        string stateText =
            GetCommanderStateText(
                expedition.Phase);

        // Короткая строка над карточкой.
        expeditionStatusLabel.text =
            "Активная экспедиция: " +
            commander.Name +
            " · " +
            location.Name +
            " · " +
            stateText;

        // Заголовок карточки.
        activeExpeditionTitle.text =
            "ЭКСПЕДИЦИЯ: " +
            location.Name.ToUpper();

        string currentTask;
        string daysInformation;

        // Определяем задачу и отображение расстояния.
        if (expedition.Phase ==
            CommanderState.TravellingToLocation)
        {
            currentTask =
                "Добраться до цели";

            daysInformation =
                "Осталось дней пути: " +
                expedition.DaysRemaining;
        }
        else if (expedition.Phase ==
                 CommanderState.AtLocation)
        {
            currentTask =
                "Исследовать локацию";

            daysInformation =
                "Расстояние до столицы: " +
                location.DistanceDays +
                " дн.";
        }
        else
        {
            currentTask =
                "Вернуться в столицу";

            daysInformation =
                "Осталось дней пути: " +
                expedition.DaysRemaining;
        }

        // Полная информация в карточке.
        activeExpeditionDetails.text =
            "Командир: " +
            commander.Name +
            "\n" +

            "Армия: " +
            expedition.FighterIds.Count +
            " отдельных воинов\n" +

            "Цель: " +
            location.Name +
            "\n" +

            "Состояние: " +
            stateText +
            "\n" +

            "Текущая задача: " +
            currentTask +
            "\n" +

            daysInformation +
            "\n" +

            // Числовую формулу снабжения ещё не утверждали.
            "Снабжение: пока не рассчитывается";

        // Повторяем последнее сообщение в карточке.
        activeExpeditionLastReport.text =
            reportLabel.text;

        bool alreadyReturning =
            expedition.Phase ==
            CommanderState.ReturningToCastle;

        // Повторно приказывать возвращаться нельзя.
        returnExpeditionButton.SetEnabled(
            !alreadyReturning);

        returnExpeditionButton.text =
            alreadyReturning
                ? "Возвращение уже приказано"
                : "Приказать возвращаться";
    }

    // ========================================================
    // РУССКИЕ НАЗВАНИЯ СОСТОЯНИЙ
    // ========================================================

    private string GetCommanderStateText(
        CommanderState state)
    {
        switch (state)
        {
            case CommanderState.InCastle:
                return "в замке";

            case CommanderState.TravellingToLocation:
                return "в пути к цели";

            case CommanderState.AtLocation:
                return "действует в локации";

            case CommanderState.ReturningToCastle:
                return "возвращается в замок";

            default:
                return "состояние неизвестно";
        }
    }
}
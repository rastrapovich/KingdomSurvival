using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Управляет первым тестовым экраном Kingdom Survival.
///
/// Этот скрипт пока не является полноценной игровой системой.
/// Его задача — проверить интерфейс и основные правила прототипа.
/// </summary>
[RequireComponent(typeof(UIDocument))]
public sealed class PrototypeUIController : MonoBehaviour
{
    // =========================================================
    // НАЧАЛЬНЫЕ ДАННЫЕ ПРОТОТИПА
    // Позже они будут вынесены в отдельную систему состояния игры.
    // =========================================================

    private const int FoodPerResident = 1;
    private const int ArmySize = 5;

    private int _day = 1;
    private int _gold = 120;
    private int _food = 72;
    private int _population = 24;
    private int _mood = 64;

    // Одновременно разрешена только одна экспедиция.
    private bool _expeditionActive;
    private string _activeLocation = "";
    private int _remainingTravelDays;

    // =========================================================
    // ССЫЛКИ НА ЭЛЕМЕНТЫ ИНТЕРФЕЙСА
    // Эти элементы будут найдены по их именам из UXML.
    // =========================================================

    private Label _dayLabel;
    private Label _goldLabel;
    private Label _foodLabel;
    private Label _populationLabel;
    private Label _moodLabel;
    private Label _foodConsumptionLabel;

    private DropdownField _commanderDropdown;
    private Label _commanderDetailLabel;
    private Label _armyStatusLabel;
    private Label _expeditionStatusLabel;
    private Label _reportLabel;

    private Button _endDayButton;
    private Button _ruinsButton;
    private Button _mineButton;
    private Button _forestButton;

    // Доступные командиры.
    // Выбор нового имени НЕ создаёт дополнительную армию.
    private readonly List<string> _commanderNames = new List<string>
    {
        "Сэр Альрик",
        "Леди Мирена",
        "Бран Каменная Рука"
    };

    // Небольшие описания для теста выпадающего списка.
    private readonly Dictionary<string, string> _commanderDescriptions =
        new Dictionary<string, string>
        {
            {
                "Сэр Альрик",
                "опытный защитник, усиливающий гвардейцев"
            },
            {
                "Леди Мирена",
                "разведчица, лучше оценивающая угрозу"
            },
            {
                "Бран Каменная Рука",
                "тяжёлый воин, повышающий стойкость армии"
            }
        };

    // =========================================================
    // ONENABLE
    // Unity уже загрузила UXML, поэтому здесь можно искать кнопки
    // и подключать к ним игровую логику.
    // =========================================================

    private void OnEnable()
    {
        UIDocument uiDocument = GetComponent<UIDocument>();
        VisualElement root = uiDocument.rootVisualElement;

        // Основные показатели.
        _dayLabel = root.Q<Label>("day-label");
        _goldLabel = root.Q<Label>("gold-label");
        _foodLabel = root.Q<Label>("food-label");
        _populationLabel = root.Q<Label>("population-label");
        _moodLabel = root.Q<Label>("mood-label");
        _foodConsumptionLabel = root.Q<Label>("food-consumption-label");

        // Командир и армия.
        _commanderDropdown =
            root.Q<DropdownField>("commander-dropdown");

        _commanderDetailLabel =
            root.Q<Label>("commander-detail-label");

        _armyStatusLabel =
            root.Q<Label>("army-status-label");

        // Экспедиции и донесения.
        _expeditionStatusLabel =
            root.Q<Label>("expedition-status-label");

        _reportLabel =
            root.Q<Label>("report-label");

        // Кнопки.
        _endDayButton =
            root.Q<Button>("end-day-button");

        _ruinsButton =
            root.Q<Button>("send-ruins-button");

        _mineButton =
            root.Q<Button>("send-mine-button");

        _forestButton =
            root.Q<Button>("send-forest-button");

        // Заполняем открывающийся список командиров.
        _commanderDropdown.choices = _commanderNames;
        _commanderDropdown.index = 0;

        // Подключаем реакции на действия игрока.
        _commanderDropdown.RegisterValueChangedCallback(
            OnCommanderChanged
        );

        _endDayButton.clicked += EndDay;
        _ruinsButton.clicked += SendToRuins;
        _mineButton.clicked += SendToMine;
        _forestButton.clicked += SendToForest;

        // Показываем стартовые значения.
        RefreshResourceLabels();
        RefreshCommanderPanel();

        _armyStatusLabel.text =
            $"Статус армии: в столице. " +
            $"{ArmySize} воинов защищают город.";

        _expeditionStatusLabel.text =
            "Активная экспедиция: нет.";
    }

    // =========================================================
    // ONDISABLE
    // Отсоединяем обработчики, чтобы они не дублировались
    // после повторного включения объекта или сцены.
    // =========================================================

    private void OnDisable()
    {
        if (_commanderDropdown != null)
        {
            _commanderDropdown.UnregisterValueChangedCallback(
                OnCommanderChanged
            );
        }

        if (_endDayButton != null)
            _endDayButton.clicked -= EndDay;

        if (_ruinsButton != null)
            _ruinsButton.clicked -= SendToRuins;

        if (_mineButton != null)
            _mineButton.clicked -= SendToMine;

        if (_forestButton != null)
            _forestButton.clicked -= SendToForest;
    }

    // =========================================================
    // ОБНОВЛЕНИЕ ПОКАЗАТЕЛЕЙ
    // =========================================================

    private void RefreshResourceLabels()
    {
        _dayLabel.text = $"День: {_day}";
        _goldLabel.text = $"Золото: {_gold}";
        _foodLabel.text = $"Пища: {_food}";
        _populationLabel.text = $"Население: {_population}";
        _moodLabel.text = $"Настроение: {_mood}/100";

        // Главное утверждённое правило:
        // каждый житель потребляет одну пищу за день.
        int dailyFoodConsumption =
            _population * FoodPerResident;

        _foodConsumptionLabel.text =
            $"Расход: {dailyFoodConsumption} в день";
    }

    private void RefreshCommanderPanel()
    {
        string commanderName = _commanderDropdown.value;

        if (string.IsNullOrEmpty(commanderName))
            return;

        string description =
            _commanderDescriptions[commanderName];

        _commanderDetailLabel.text =
            $"{commanderName} — {description}. " +
            $"Под командованием: {ArmySize} отдельных воинов.";
    }

    // =========================================================
    // ВЫБОР КОМАНДИРА
    // Меняется руководитель, но не создаётся новая армия.
    // =========================================================

    private void OnCommanderChanged(ChangeEvent<string> evt)
    {
        RefreshCommanderPanel();

        _reportLabel.text =
            $"{evt.newValue} назначен руководителем " +
            $"единственной армии из {ArmySize} воинов.";
    }

    // =========================================================
    // ЗАВЕРШЕНИЕ ДНЯ И СПИСАНИЕ ПИЩИ
    // =========================================================

    private void EndDay()
    {
        int requiredFood =
            _population * FoodPerResident;

        // Если пищи мало, списываем всё, что осталось.
        int consumedFood =
            Mathf.Min(_food, requiredFood);

        _food -= consumedFood;
        _day++;

        string dailyReport =
            $"Наступил день {_day}. " +
            $"Население потребило {consumedFood} пищи.";

        // Последствия голода пока не определены каноном.
        // Поэтому сейчас только сообщаем о нехватке.
        if (consumedFood < requiredFood)
        {
            int foodShortage =
                requiredFood - consumedFood;

            dailyReport +=
                $" Не хватило {foodShortage} пищи. " +
                "Последствия голода пока не реализованы.";
        }

        // Если армия находится в пути, уменьшаем расстояние.
        if (_expeditionActive && _remainingTravelDays > 0)
        {
            _remainingTravelDays--;

            if (_remainingTravelDays > 0)
            {
                _expeditionStatusLabel.text =
                    $"Экспедиция: {_activeLocation}. " +
                    $"Осталось дней пути: {_remainingTravelDays}.";
            }
            else
            {
                _expeditionStatusLabel.text =
                    $"Экспедиция достигла локации: " +
                    $"{_activeLocation}. Ожидает решения.";
            }
        }

        _reportLabel.text = dailyReport;

        RefreshResourceLabels();
    }

    // =========================================================
    // КНОПКИ ТРЁХ ЛОКАЦИЙ
    // =========================================================

    private void SendToRuins()
    {
        TrySendExpedition(
            "Затопленные руины",
            2,
            "низкая"
        );
    }

    private void SendToMine()
    {
        TrySendExpedition(
            "Старая шахта",
            3,
            "средняя"
        );
    }

    private void SendToForest()
    {
        TrySendExpedition(
            "Чёрный лес",
            5,
            "высокая"
        );
    }

    // =========================================================
    // ОТПРАВКА ЕДИНСТВЕННОЙ АРМИИ
    // =========================================================

    private void TrySendExpedition(
        string locationName,
        int distance,
        string threat)
    {
        // Защита от запуска второй экспедиции.
        if (_expeditionActive)
        {
            _reportLabel.text =
                "Нельзя отправить вторую экспедицию: " +
                "единственная армия уже покинула столицу.";

            return;
        }

        _expeditionActive = true;
        _activeLocation = locationName;
        _remainingTravelDays = distance;

        string commanderName =
            _commanderDropdown.value;

        _expeditionStatusLabel.text =
            $"Экспедиция: {locationName}. " +
            $"До цели: {distance} дн. " +
            $"Угроза: {threat}.";

        _armyStatusLabel.text =
            $"Статус армии: в экспедиции. " +
            $"{commanderName} и {ArmySize} воинов " +
            "не защищают столицу.";

        _reportLabel.text =
            $"{commanderName} отправлен в локацию " +
            $"«{locationName}» вместе с " +
            $"{ArmySize} отдельными воинами.";

        // Пока армия отсутствует, нельзя сменить командира.
        _commanderDropdown.SetEnabled(false);

        // И нельзя отправить эту же армию во второе место.
        SetExpeditionButtonsEnabled(false);
    }

    private void SetExpeditionButtonsEnabled(bool enabled)
    {
        _ruinsButton.SetEnabled(enabled);
        _mineButton.SetEnabled(enabled);
        _forestButton.SetEnabled(enabled);
    }
}
using System.Collections.Generic;
using KingdomSurvival.UnitDatabase;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

// Вкладки экрана героя: «Герой», «Отряд», «Снаряжение», «Развитие»,
// «Поход». «Снаряжение» и «Развитие» — две страницы одного рабочего места
// человека (hero-screen-unit-card): слева полоса людей отряда, выбранный
// человек общий для обеих вкладок. Значки на вкладках — сколько ждёт
// внимания. Клавиши: 1–5 и Q/E — вкладки, ←/→ — другой человек, Esc —
// закрыть (GameMenus.cs). Других переходов между вкладками на экране нет —
// ни кнопок, ни кликов по строкам. Переключатели фильтров и порядка всех
// списков — HeroToggleGroup ниже.
public partial class PrototypeUIController
{
    private enum HeroScreenTab
    {
        Hero,
        Squad,
        Gear,
        Growth,
        Journey
    }

    private const string HeroScreenTabActiveClass = "hero-screen-tab--active";
    private const string HeroScreenPageActiveClass = "hero-screen-page--active";
    private const string HeroScreenPersonPageActiveClass = "hero-screen-person-page--active";
    private const string HeroScreenToggleActiveClass = "hero-screen-toggle--active";
    private const float HeroScreenWheelStep = 90f;

    private static readonly string[] HeroScreenTabSuffixes = { "hero", "squad", "gear", "growth", "journey" };

    // Группа взаимоисключающих переключателей (фильтр или порядок списка).
    // Подписи с числом («Нужен уход · 2») обновляет SetCount.
    private sealed class HeroToggleGroup<T>
    {
        private readonly List<Button> buttons = new List<Button>();
        private readonly List<T> values = new List<T>();
        private readonly List<string> captions = new List<string>();
        private readonly System.Action changed;

        public T Value { get; private set; }

        public HeroToggleGroup(T initial, System.Action changed)
        {
            Value = initial;
            this.changed = changed;
        }

        public bool Add(Button button, T value)
        {
            if (button == null)
                return false;
            buttons.Add(button);
            values.Add(value);
            captions.Add(button.text);
            button.clicked += () => Set(value, true);
            Apply();
            return true;
        }

        public void Set(T value, bool notify)
        {
            Value = value;
            Apply();
            if (notify)
                changed?.Invoke();
        }

        public void SetCount(T value, int count)
        {
            for (int i = 0; i < values.Count; i++)
            {
                if (EqualityComparer<T>.Default.Equals(values[i], value))
                    buttons[i].text = count > 0 ? captions[i] + " · " + count : captions[i];
            }
        }

        private void Apply()
        {
            for (int i = 0; i < buttons.Count; i++)
                buttons[i].EnableInClassList(HeroScreenToggleActiveClass, EqualityComparer<T>.Default.Equals(values[i], Value));
        }
    }

    private HeroScreenTab heroScreenTab = HeroScreenTab.Hero;
    private Button[] heroScreenTabButtons;
    private Label[] heroScreenTabBadges;
    private VisualElement heroScreenPageHero;
    private VisualElement heroScreenPageSquad;
    private VisualElement heroScreenPageJourney;
    private VisualElement heroScreenGearPage;
    private VisualElement heroScreenGrowthPage;
    private VisualElement heroPeopleRailList;

    private bool IsHeroPersonTab => heroScreenTab == HeroScreenTab.Gear || heroScreenTab == HeroScreenTab.Growth;

    private bool BindHeroScreenTabs()
    {
        heroScreenTabButtons = new Button[HeroScreenTabSuffixes.Length];
        heroScreenTabBadges = new Label[HeroScreenTabSuffixes.Length];
        bool ok = true;
        for (int i = 0; i < HeroScreenTabSuffixes.Length; i++)
        {
            heroScreenTabButtons[i] = BindRequiredElement<Button>(interfaceRoot, HeroScreenName, "hero-screen-tab-" + HeroScreenTabSuffixes[i]);
            heroScreenTabBadges[i] = BindRequiredElement<Label>(interfaceRoot, HeroScreenName, "hero-screen-tab-" + HeroScreenTabSuffixes[i] + "-badge");
            ok &= heroScreenTabButtons[i] != null && heroScreenTabBadges[i] != null;
        }

        heroScreenPageHero = BindRequiredElement<VisualElement>(interfaceRoot, HeroScreenName, "hero-screen-page-hero");
        heroScreenPageSquad = BindRequiredElement<VisualElement>(interfaceRoot, HeroScreenName, "hero-screen-page-squad");
        heroScreenPageJourney = BindRequiredElement<VisualElement>(interfaceRoot, HeroScreenName, "hero-screen-page-journey");
        heroScreenGearPage = BindRequiredElement<VisualElement>(interfaceRoot, HeroScreenName, "hero-screen-unit-card-gear");
        heroScreenGrowthPage = BindRequiredElement<VisualElement>(interfaceRoot, HeroScreenName, "hero-screen-unit-card-development");
        heroPeopleRailList = BindRequiredElement<VisualElement>(interfaceRoot, HeroScreenName, "hero-screen-people-rail-list");

        ok &= heroScreenPageHero != null && heroScreenPageSquad != null && heroScreenPageJourney != null &&
              heroScreenGearPage != null && heroScreenGrowthPage != null && heroPeopleRailList != null;
        if (!ok)
            return false;

        for (int i = 0; i < heroScreenTabButtons.Length; i++)
        {
            HeroScreenTab tab = (HeroScreenTab)i;
            heroScreenTabButtons[i].clicked += () => SwitchHeroScreenTab(tab);
        }

        // Колесо мыши в списках — крупным шагом: длинный список листается
        // за несколько движений, а не за десятки.
        heroScreenOverlay.Query<ScrollView>().ForEach(scroll => scroll.mouseWheelScrollSize = HeroScreenWheelStep);
        return true;
    }

    private void SwitchHeroScreenTab(HeroScreenTab tab)
    {
        heroScreenTab = tab;
        HideHeroScreenStatTooltip();
        HideHeroScreenTagTooltip();

        for (int i = 0; i < heroScreenTabButtons.Length; i++)
            heroScreenTabButtons[i].EnableInClassList(HeroScreenTabActiveClass, i == (int)tab);

        heroScreenPageHero.EnableInClassList(HeroScreenPageActiveClass, tab == HeroScreenTab.Hero);
        heroScreenPageSquad.EnableInClassList(HeroScreenPageActiveClass, tab == HeroScreenTab.Squad);
        heroScreenPageJourney.EnableInClassList(HeroScreenPageActiveClass, tab == HeroScreenTab.Journey);
        heroScreenGearPage.EnableInClassList(HeroScreenPersonPageActiveClass, tab == HeroScreenTab.Gear);
        heroScreenGrowthPage.EnableInClassList(HeroScreenPersonPageActiveClass, tab == HeroScreenTab.Growth);

        // Рабочее место человека — одна страница на две вкладки; display
        // выставляется явно, по нему открытость читают IsHeroCardOpen и тесты.
        heroScreenUnitCard.style.display = IsHeroPersonTab ? DisplayStyle.Flex : DisplayStyle.None;
        // Бриф исправлений п. 8: «Снаряжение» — временно только Командир,
        // полоса людей скрыта (отряд и вещи бойцов в игре остаются).
        heroScreenUnitCard.EnableInClassList("hero-screen-unit-card--gear", tab == HeroScreenTab.Gear);
        if (tab == HeroScreenTab.Gear && gameState?.GetSelectedCommander() != null)
            SelectHeroPerson(gameState.GetSelectedCommander().Id);
        else if (IsHeroPersonTab)
            EnsureHeroPersonSelected();
        HideHeroItemPopup();

        RefreshHeroScreen();
    }

    private void CycleHeroScreenTab(int step)
    {
        int count = HeroScreenTabSuffixes.Length;
        SwitchHeroScreenTab((HeroScreenTab)((((int)heroScreenTab + step) % count + count) % count));
    }

    // Показать человека на вкладке «Снаряжение» — программный вход (тесты,
    // будущие вызовы извне). В самом экране переходы между вкладками — только
    // вкладками; человека выбирают в полосе слева. Если открыто «Развитие» —
    // остаётся на нём.
    private void ShowHeroPersonCard(string personId)
    {
        ShowHeroPersonOnTab(personId, IsHeroPersonTab ? heroScreenTab : HeroScreenTab.Gear);
    }

    private void ShowHeroPersonOnTab(string personId, HeroScreenTab tab)
    {
        if (!heroScreenUiBound || gameState == null || string.IsNullOrEmpty(personId))
            return;

        SelectHeroPerson(personId);
        if (!IsHeroScreenOpen)
            OpenHeroScreenOnTab(tab);
        else
            SwitchHeroScreenTab(tab);
    }

    private void SelectHeroPerson(string personId)
    {
        if (heroCardPersonId != personId)
        {
            heroCardMessage = string.Empty;
            heroItemsMessage = string.Empty;
        }
        heroCardPersonId = personId;
    }

    private void EnsureHeroPersonSelected()
    {
        List<string> people = GetHeroScreenPeople();
        if (!string.IsNullOrEmpty(heroCardPersonId) && people.Contains(heroCardPersonId))
            return;
        SelectHeroPerson(people.Count > 0 ? people[0] : null);
    }

    private void CycleHeroPerson(int step)
    {
        List<string> people = GetHeroScreenPeople();
        if (people.Count == 0)
            return;
        int index = Mathf.Max(0, people.IndexOf(heroCardPersonId));
        SelectHeroPerson(people[((index + step) % people.Count + people.Count) % people.Count]);
        RefreshHeroScreen();
    }

    // Люди рабочего места — в порядке состава: Командир, бойцы похода по
    // местам, затем остальные в порядке Дома.
    private List<string> GetHeroScreenPeople()
    {
        List<string> result = new List<string>();
        if (gameState == null)
            return result;

        CommanderData commander = gameState.GetSelectedCommander();
        List<string> roster = new List<string>(ExpeditionPreparation.GetFighterIds(gameState));
        List<string> others = new List<string>();
        foreach (ResidentState resident in HomePeopleService.All(gameState))
        {
            if (!resident.IsAlive || !IsHeroItemsCandidate(resident))
                continue;
            if (commander != null && resident.PersonId == commander.Id)
                result.Insert(0, resident.PersonId);
            else if (!roster.Contains(resident.PersonId))
                others.Add(resident.PersonId);
        }

        foreach (string id in roster)
        {
            ResidentState resident = HomePeopleService.Find(gameState, id);
            if (resident != null && resident.IsAlive && IsHeroItemsCandidate(resident))
                result.Add(id);
        }
        result.AddRange(others);
        return result;
    }

    // Портрет человека: у Командира — свой облик, у бойца — его тип из Базы
    // существ, затем по роли.
    private UnitDefinitionData ResolveHeroPersonPortrait(string personId)
    {
        CommanderData commander = gameState.GetSelectedCommander();
        if (commander != null && commander.Id == personId)
            return ResolveHeroScreenUnit(commander);

        ResidentState resident = HomePeopleService.Find(gameState, personId);
        if (heroScreenUnits != null && resident != null && !string.IsNullOrEmpty(resident.UnitTypeId))
        {
            UnitDefinitionData byType = heroScreenUnits.FindById(resident.UnitTypeId);
            if (byType != null)
                return byType;
        }
        return ResolveHeroScreenUnit(gameState.FindFighter(personId));
    }

    private VisualElement CreateHeroPortraitThumb(string personId, string slotClass)
    {
        VisualElement slot = new VisualElement();
        slot.AddToClassList(slotClass);
        slot.pickingMode = PickingMode.Ignore;
        UnitPortraitElement portrait = CreateHeroScreenPortrait(slot);
        portrait.pickingMode = PickingMode.Ignore;
        ApplyHeroScreenPortrait(portrait, ResolveHeroPersonPortrait(personId));
        return slot;
    }

    // Полоса людей слева от рабочего места: портрет, уровень, здоровье и
    // отметка «ждёт выбор развития». Клик — выбрать.
    private void RefreshHeroPeopleRail()
    {
        heroPeopleRailList.Clear();
        if (!IsHeroPersonTab)
            return;

        foreach (string personId in GetHeroScreenPeople())
        {
            ResidentState resident = HomePeopleService.Find(gameState, personId);
            PersonProgressionData record = CharacterProgressionService.Get(gameState, personId);
            bool choice = CharacterProgressionService.PendingChoices(gameState, personId) > 0;

            VisualElement chip = new VisualElement();
            chip.AddToClassList("hero-people-chip");
            chip.EnableInClassList("hero-people-chip--selected", personId == heroCardPersonId);
            chip.Add(CreateHeroPortraitThumb(personId, "hero-people-chip-portrait"));

            VisualElement text = new VisualElement();
            text.AddToClassList("hero-people-chip-text");
            text.pickingMode = PickingMode.Ignore;
            Label name = new Label(resident.DisplayName) { pickingMode = PickingMode.Ignore };
            name.AddToClassList("hero-people-chip-name");
            string meta = (record != null ? "ур. " + record.Level : resident.RoleLabel) +
                          (resident.HasCombatState ? "  ·  " + resident.CurrentHitPoints + "/" + resident.MaxHitPoints : string.Empty);
            Label metaLabel = new Label(meta) { pickingMode = PickingMode.Ignore };
            metaLabel.AddToClassList("hero-people-chip-meta");
            text.Add(name);
            text.Add(metaLabel);
            chip.Add(text);

            if (choice)
            {
                Label mark = new Label("!") { pickingMode = PickingMode.Ignore };
                mark.AddToClassList("hero-people-chip-mark");
                chip.Add(mark);
            }

            string id = personId;
            chip.RegisterCallback<ClickEvent>(_ =>
            {
                if (isGameOver)
                    return;
                SelectHeroPerson(id);
                RefreshHeroScreen();
            });
            heroPeopleRailList.Add(chip);
        }
    }

    // Значки вкладок: «Отряд» — кому нужен уход, «Развитие» — ждущие выборы,
    // «Поход» — записей в сводке.
    private void RefreshHeroScreenBadges()
    {
        int hurt = 0;
        int choices = 0;
        foreach (string personId in GetHeroScreenPeople())
        {
            ResidentState resident = HomePeopleService.Find(gameState, personId);
            if (IsHeroPersonHurt(resident))
                hurt++;
            choices += CharacterProgressionService.PendingChoices(gameState, personId);
        }

        SetHeroScreenBadge(HeroScreenTab.Squad, hurt, "hero-screen-tab-badge--alert");
        SetHeroScreenBadge(HeroScreenTab.Growth, choices, null);
        SetHeroScreenBadge(HeroScreenTab.Journey, journeySummaryEntries.Count, "hero-screen-tab-badge--quiet");
    }

    private void SetHeroScreenBadge(HeroScreenTab tab, int count, string toneClass)
    {
        Label badge = heroScreenTabBadges[(int)tab];
        badge.text = count > 99 ? "99+" : count.ToString();
        badge.EnableInClassList("hero-screen-tab-badge--shown", count > 0);
        if (!string.IsNullOrEmpty(toneClass))
            badge.AddToClassList(toneClass);
    }

    private static bool IsHeroPersonHurt(ResidentState resident)
    {
        return resident != null &&
               (resident.Injury == ResidentInjury.Recovering || resident.Exhausted ||
                (resident.HasCombatState && resident.CurrentHitPoints < resident.MaxHitPoints));
    }

    // Клавиши экрана героя (LateUpdate, после Esc-обработки GameMenus.cs).
    // Не работают, пока поверх экрана меню, диалог или модальное окно.
    private void HandleHeroScreenHotkeys()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null || !heroScreenUiBound || !IsHeroScreenOpen || isGameOver ||
            IsGameMenuOpen || IsNarrativeDialogueActive || activeQueuedModal != null)
            return;

        if (keyboard.digit1Key.wasPressedThisFrame || keyboard.numpad1Key.wasPressedThisFrame)
            SwitchHeroScreenTab(HeroScreenTab.Hero);
        else if (keyboard.digit2Key.wasPressedThisFrame || keyboard.numpad2Key.wasPressedThisFrame)
            SwitchHeroScreenTab(HeroScreenTab.Squad);
        else if (keyboard.digit3Key.wasPressedThisFrame || keyboard.numpad3Key.wasPressedThisFrame)
            SwitchHeroScreenTab(HeroScreenTab.Gear);
        else if (keyboard.digit4Key.wasPressedThisFrame || keyboard.numpad4Key.wasPressedThisFrame)
            SwitchHeroScreenTab(HeroScreenTab.Growth);
        else if (keyboard.digit5Key.wasPressedThisFrame || keyboard.numpad5Key.wasPressedThisFrame)
            SwitchHeroScreenTab(HeroScreenTab.Journey);
        else if (keyboard.qKey.wasPressedThisFrame)
            CycleHeroScreenTab(-1);
        else if (keyboard.eKey.wasPressedThisFrame)
            CycleHeroScreenTab(1);
        else if (heroScreenTab == HeroScreenTab.Growth && keyboard.leftArrowKey.wasPressedThisFrame)
            CycleHeroPerson(-1);
        else if (heroScreenTab == HeroScreenTab.Growth && keyboard.rightArrowKey.wasPressedThisFrame)
            CycleHeroPerson(1);
    }
}

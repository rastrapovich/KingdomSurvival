using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

// Вкладка «Отряд» экрана героя: таблица людей (Командир и бойцы, которые
// сейчас рядом) — портрет, уровень и опыт, здоровье, состояние, место в
// походе и одно действие: взять в состав / оставить. Вещи и развитие —
// на своих вкладках, отсюда на них не ведут ни кнопки, ни клик. Фильтр
// («Нужен уход», «Ждут выбора»…) и порядок (уровень, здоровье, имя) —
// переключатели над таблицей; на фильтрах — сколько людей под ним.
// Состав меняется только командами ExpeditionPreparation (через
// selectedFighterIds), как и на экране Дома и на карте.
public partial class PrototypeUIController
{
    private enum HeroSquadFilter
    {
        All,
        Roster,
        Free,
        Hurt,
        Choice
    }

    private enum HeroSquadSort
    {
        Order,
        Level,
        Health,
        Name
    }

    private sealed class HeroSquadEntry
    {
        public string PersonId;
        public ResidentState Resident;
        public PersonProgressionData Record;
        public int Order;
        public bool IsCommander;
        public bool InRoster;
        public bool Hurt;
        public bool Choice;
        public float Health;
    }

    private HeroToggleGroup<HeroSquadFilter> heroSquadFilter;
    private HeroToggleGroup<HeroSquadSort> heroSquadSort;
    private ScrollView heroSquadScroll;
    private Label heroScreenPartyGlanceLabel;
    private string heroSquadMessage = string.Empty;

    private bool BindHeroSquad()
    {
        heroSquadScroll = BindRequiredElement<ScrollView>(interfaceRoot, HeroScreenName, "hero-screen-squad-scroll");
        heroScreenPartyGlanceLabel = BindRequiredElement<Label>(interfaceRoot, HeroScreenName, "hero-screen-party-glance-label");

        heroSquadFilter = new HeroToggleGroup<HeroSquadFilter>(HeroSquadFilter.All, OnHeroSquadViewChanged);
        heroSquadSort = new HeroToggleGroup<HeroSquadSort>(HeroSquadSort.Order, OnHeroSquadViewChanged);
        bool ok = heroSquadScroll != null && heroScreenPartyGlanceLabel != null;
        ok &= heroSquadFilter.Add(BindHeroToggle("hero-screen-squad-filter-all"), HeroSquadFilter.All);
        ok &= heroSquadFilter.Add(BindHeroToggle("hero-screen-squad-filter-roster"), HeroSquadFilter.Roster);
        ok &= heroSquadFilter.Add(BindHeroToggle("hero-screen-squad-filter-free"), HeroSquadFilter.Free);
        ok &= heroSquadFilter.Add(BindHeroToggle("hero-screen-squad-filter-hurt"), HeroSquadFilter.Hurt);
        ok &= heroSquadFilter.Add(BindHeroToggle("hero-screen-squad-filter-choice"), HeroSquadFilter.Choice);
        ok &= heroSquadSort.Add(BindHeroToggle("hero-screen-squad-sort-order"), HeroSquadSort.Order);
        ok &= heroSquadSort.Add(BindHeroToggle("hero-screen-squad-sort-level"), HeroSquadSort.Level);
        ok &= heroSquadSort.Add(BindHeroToggle("hero-screen-squad-sort-health"), HeroSquadSort.Health);
        ok &= heroSquadSort.Add(BindHeroToggle("hero-screen-squad-sort-name"), HeroSquadSort.Name);
        return ok;
    }

    private Button BindHeroToggle(string name)
    {
        return BindRequiredElement<Button>(interfaceRoot, HeroScreenName, name);
    }

    private void OnHeroSquadViewChanged()
    {
        heroSquadScroll.scrollOffset = Vector2.zero;
        RefreshHeroScreen();
    }

    private void SetHeroSquadFilter(HeroSquadFilter filter)
    {
        heroSquadFilter.Set(filter, true);
    }

    private List<HeroSquadEntry> CollectHeroSquad()
    {
        List<HeroSquadEntry> entries = new List<HeroSquadEntry>();
        CommanderData commander = gameState.GetSelectedCommander();
        List<string> people = GetHeroScreenPeople();
        for (int i = 0; i < people.Count; i++)
        {
            string personId = people[i];
            ResidentState resident = PeopleService.Find(gameState, personId);
            bool isCommander = commander != null && commander.Id == personId;
            entries.Add(new HeroSquadEntry
            {
                PersonId = personId,
                Resident = resident,
                Record = CharacterProgressionService.Get(gameState, personId),
                Order = i,
                IsCommander = isCommander,
                InRoster = isCommander || selectedFighterIds.Contains(personId),
                Hurt = IsHeroPersonHurt(resident),
                Choice = CharacterProgressionService.PendingChoices(gameState, personId) > 0,
                Health = resident.HasCombatState && resident.MaxHitPoints > 0
                    ? (float)resident.CurrentHitPoints / resident.MaxHitPoints
                    : 1f
            });
        }
        return entries;
    }

    private static bool PassesHeroSquadFilter(HeroSquadEntry entry, HeroSquadFilter filter)
    {
        switch (filter)
        {
            case HeroSquadFilter.Roster: return entry.InRoster;
            case HeroSquadFilter.Free: return !entry.InRoster;
            case HeroSquadFilter.Hurt: return entry.Hurt;
            case HeroSquadFilter.Choice: return entry.Choice;
            default: return true;
        }
    }

    private static int CompareHeroSquad(HeroSquadEntry a, HeroSquadEntry b, HeroSquadSort sort)
    {
        int result = 0;
        switch (sort)
        {
            case HeroSquadSort.Level:
                result = (b.Record != null ? b.Record.Level : 0).CompareTo(a.Record != null ? a.Record.Level : 0);
                break;
            case HeroSquadSort.Health:
                result = a.Health.CompareTo(b.Health);
                break;
            case HeroSquadSort.Name:
                result = string.Compare(a.Resident.DisplayName, b.Resident.DisplayName, System.StringComparison.CurrentCulture);
                break;
        }
        return result != 0 ? result : a.Order.CompareTo(b.Order);
    }

    private void RefreshHeroScreenSquad()
    {
        heroScreenSquadList.Clear();
        List<HeroSquadEntry> entries = CollectHeroSquad();

        int roster = 0, free = 0, hurt = 0, waiting = 0;
        foreach (HeroSquadEntry entry in entries)
        {
            if (entry.InRoster) roster++; else free++;
            if (entry.Hurt) hurt++;
            if (entry.Choice) waiting++;
        }
        heroSquadFilter.SetCount(HeroSquadFilter.All, entries.Count);
        heroSquadFilter.SetCount(HeroSquadFilter.Roster, roster);
        heroSquadFilter.SetCount(HeroSquadFilter.Free, free);
        heroSquadFilter.SetCount(HeroSquadFilter.Hurt, hurt);
        heroSquadFilter.SetCount(HeroSquadFilter.Choice, waiting);

        List<HeroSquadEntry> shown = entries.FindAll(entry => PassesHeroSquadFilter(entry, heroSquadFilter.Value));
        HeroSquadSort sort = heroSquadSort.Value;
        shown.Sort((a, b) => CompareHeroSquad(a, b, sort));

        bool canEdit = ExpeditionPreparation.CanEdit(gameState) && !isGameOver;
        foreach (HeroSquadEntry entry in shown)
            heroScreenSquadList.Add(CreateHeroSquadRow(entry, canEdit));

        if (shown.Count == 0)
            AddHeroScreenHint(heroScreenSquadList, entries.Count == 0 ? "В отряде никого нет." : "Под этот фильтр никто не подходит.");

        List<string> summary = new List<string>();
        summary.Add(hurt == 0 ? "Все целы." : "Нужен уход: " + hurt + ".");
        if (waiting > 0)
            summary.Add("Выбор развития ждёт: " + waiting + ".");
        if (!string.IsNullOrEmpty(heroSquadMessage))
            summary.Add(heroSquadMessage);
        heroStatesHint.text = string.Join(" ", summary);

        // Сводка на вкладке «Герой».
        heroScreenPartyGlanceLabel.text =
            "В походе: командир и " + (roster - 1) + " из " + HeroScreenFighterSlots + " бойцов.\n" +
            (hurt == 0 ? "Все целы." : "Нужен уход: " + hurt + ".") +
            (waiting > 0 ? "  Ждут выбора развития: " + waiting + "." : string.Empty);
    }

    private VisualElement CreateHeroSquadRow(HeroSquadEntry entry, bool canEdit)
    {
        ResidentState resident = entry.Resident;
        string personId = entry.PersonId;
        FighterData fighter = gameState.FindFighter(personId);

        VisualElement row = new VisualElement();
        row.AddToClassList("hero-squad-row");
        row.EnableInClassList("hero-squad-row--commander", entry.IsCommander);
        row.EnableInClassList("hero-squad-row--roster", entry.InRoster && !entry.IsCommander);
        row.EnableInClassList("hero-squad-row--attention", entry.Choice);

        // Человек
        VisualElement person = HeroSquadCell(row, "hero-squad-cell-person");
        person.Add(CreateHeroPortraitThumb(personId, "hero-squad-portrait"));
        VisualElement names = new VisualElement { pickingMode = PickingMode.Ignore };
        names.Add(HeroSquadLabel(resident.DisplayName, "hero-squad-name"));
        string role = entry.IsCommander ? "Командир" : fighter != null ? fighter.Role : resident.RoleLabel;
        names.Add(HeroSquadLabel(role, "hero-squad-role"));
        person.Add(names);

        // Уровень и опыт
        VisualElement level = HeroSquadCell(row, "hero-squad-cell-level");
        if (entry.Record != null)
        {
            CharacterProgressionService.GetLevelProgress(gameState, entry.Record, out int current, out int required);
            level.Add(HeroSquadLabel("Ур. " + entry.Record.Level, "hero-squad-value", "hero-squad-value--gold"));
            level.Add(HeroMiniBar(required > 0 ? (float)current / required : 1f, null));
            level.Add(HeroSquadLabel(required > 0 ? current + " / " + required + " опыта" : "предел пути", "hero-squad-value--muted"));
        }
        else
        {
            level.Add(HeroSquadLabel("не развивается", "hero-squad-value--muted"));
        }

        // Здоровье
        VisualElement health = HeroSquadCell(row, "hero-squad-cell-health");
        if (resident.HasCombatState)
        {
            health.Add(HeroSquadLabel(resident.CurrentHitPoints + " / " + resident.MaxHitPoints + " HP", "hero-squad-value"));
            string tone = entry.Health >= 0.999f ? "hero-mini-bar-fill--health"
                : entry.Health > 0.4f ? "hero-mini-bar-fill--hurt"
                : "hero-mini-bar-fill--critical";
            health.Add(HeroMiniBar(entry.Health, tone));
        }
        else
        {
            health.Add(HeroSquadLabel("—", "hero-squad-value--muted"));
        }

        // Состояние
        VisualElement state = HeroSquadCell(row, "hero-squad-cell-state");
        string condition = resident.Injury == ResidentInjury.Recovering ? "Тяжёлая рана — в поход не пойдёт"
            : resident.Exhausted ? "Изнеможён — атака и инициатива ниже"
            : resident.HasCombatState && resident.CurrentHitPoints < resident.MaxHitPoints ? "Ранен — лечится уходом дома"
            : "Цел";
        state.Add(HeroSquadLabel(condition, "hero-squad-value", entry.Hurt ? "hero-squad-value--bad" : "hero-squad-value--good"));
        if (entry.Choice)
            state.Add(HeroSquadLabel("Ждёт выбор развития", "hero-squad-value", "hero-squad-value--gold"));

        // Поход
        VisualElement status = HeroSquadCell(row, "hero-squad-cell-status");
        string place = entry.IsCommander ? "ведёт отряд"
            : entry.InRoster ? (gameState.HasActiveExpedition ? "в походе" : "в составе")
            : "свободен";
        status.Add(HeroSquadLabel(place, "hero-squad-value", entry.InRoster ? "hero-squad-value--gold" : "hero-squad-value--muted"));

        // Действия
        VisualElement actions = HeroSquadCell(row, "hero-squad-cell-actions");
        actions.pickingMode = PickingMode.Position;
        if (canEdit && !entry.IsCommander)
        {
            if (entry.InRoster)
                AddHeroSquadAction(actions, "Оставить", null, () => ChangeHeroSquadRoster(personId, false));
            else
                AddHeroSquadAction(actions, "В состав", "hero-squad-action--primary", () => ChangeHeroSquadRoster(personId, true));
        }
        return row;
    }

    private void ChangeHeroSquadRoster(string personId, bool add)
    {
        string message;
        bool ok = add
            ? ExpeditionPreparation.TryAddFighter(gameState, personId, out message)
            : ExpeditionPreparation.TryRemove(gameState, personId, out message);
        heroSquadMessage = ok ? string.Empty : message ?? string.Empty;
        RefreshHeroScreen();
        RefreshStableUiAfterStateChange();
    }

    private static VisualElement HeroSquadCell(VisualElement row, string cellClass)
    {
        VisualElement cell = new VisualElement { pickingMode = PickingMode.Ignore };
        cell.AddToClassList("hero-squad-cell");
        cell.AddToClassList(cellClass);
        row.Add(cell);
        return cell;
    }

    private static Label HeroSquadLabel(string text, string firstClass, string secondClass = null)
    {
        Label label = new Label(text) { pickingMode = PickingMode.Ignore };
        label.AddToClassList(firstClass);
        if (!string.IsNullOrEmpty(secondClass))
            label.AddToClassList(secondClass);
        return label;
    }

    private static VisualElement HeroMiniBar(float fraction, string fillClass)
    {
        VisualElement bar = new VisualElement { pickingMode = PickingMode.Ignore };
        bar.AddToClassList("hero-mini-bar");
        VisualElement fill = new VisualElement { pickingMode = PickingMode.Ignore };
        fill.AddToClassList("hero-mini-bar-fill");
        if (!string.IsNullOrEmpty(fillClass))
            fill.AddToClassList(fillClass);
        fill.style.width = Length.Percent(Mathf.Clamp01(fraction) * 100f);
        bar.Add(fill);
        return bar;
    }

    private void AddHeroSquadAction(VisualElement parent, string text, string extraClass, System.Action action)
    {
        Button button = new Button(() =>
        {
            if (!isGameOver)
                action();
        }) { text = text };
        button.AddToClassList("ks-button");
        button.AddToClassList("hero-squad-action");
        if (!string.IsNullOrEmpty(extraClass))
            button.AddToClassList(extraClass);
        parent.Add(button);
    }
}

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

// Бриф правок (22 пункта) пп. 6–10: согласованный экран героя.
// Главная страница — герой без служебных повторов: портрет и уровень, под
// ними приобретённые теги и отдельной группой иконки состояний; качества —
// столбцом одним числом; плашек «Снаряжение» и «Отряд» нет. Состояния
// показываются только здесь (их механика и данные не меняются).
// «Отряд» — внизу пять боевых позиций (командир в центре), над ними свита
// квадратными портретами только реальных участников; блока «Люди отряда»
// нет, пустая позиция открывает выбор свободного бойца.
// «Снаряжение» — без подписей роли, уровня, тегов, состояния и сюжетных
// вещей; характеристики под куклой показывают разницу до/после.
// Разметку не переставляем — реестр макетов UI сверяет её с UXML; лишнее
// скрывается, новое добавляется здесь.
public partial class PrototypeUIController
{
    private const string ServiceCommanderName = "Командир";
    private const float StatDeltaSeconds = 6f;

    private bool heroRevisionApplied;
    private VisualElement heroRevisionStates;
    private VisualElement heroRevisionRetinue;
    private VisualElement heroRevisionPicker;
    private readonly Label[] heroStatDeltaLabels = new Label[7];
    private string heroStatSnapshotPerson;
    private int[] heroStatSnapshot;
    private int[] heroStatDeltas;
    private float heroStatDeltaUntil;

    private void ApplyHeroScreenRevision()
    {
        if (interfaceRoot == null)
            return;
        if (!heroRevisionApplied)
            ApplyHeroScreenRevisionLayout();
        if (!heroRevisionApplied || gameState == null)
            return;
        RefreshHeroRevisionHeroPage();
        RefreshHeroRevisionRetinue();
        RefreshHeroStatDeltas();
    }

    private void ApplyHeroScreenRevisionLayout()
    {
        VisualElement root = interfaceRoot.Q("hero-screen-overlay") ?? interfaceRoot;
        VisualElement leftColumn = root.Q("hero-screen-left-column");
        VisualElement experience = root.Q("hero-screen-experience");
        VisualElement statesRow = root.Q("hero-screen-states-row");
        VisualElement rosterBar = root.Q("hero-screen-roster-bar");
        VisualElement rosterRow = root.Q("hero-screen-roster-row");
        VisualElement retinuePanel = root.Q("hero-screen-retinue");
        if (leftColumn == null || experience == null || statesRow == null || rosterBar == null || rosterRow == null ||
            retinuePanel == null || heroScreenTraitsRow == null)
            return;

        // П. 10: служебные «Командир» и статус «В пути» — не показываются.
        Hide(root.Q("hero-screen-identity-title"));
        Hide(heroScreenRoleLabel);
        Hide(heroScreenStatePhaseChip);
        // Плашки «Снаряжение» и «Отряд» — вне главной страницы.
        Hide(root.Q("hero-screen-hero-gear"));
        Hide(root.Q("hero-screen-party-glance"));
        // Теги, полученные в игре, — под портретом и уровнем.
        Hide(root.Q("hero-screen-traits"));
        heroScreenTraitsRow.AddToClassList("hero-revision-tags");
        VisualElement tagsHost = experience.parent ?? leftColumn;
        tagsHost.Insert(tagsHost.IndexOf(experience) + 1, heroScreenTraitsRow);
        // П. 6: иконки состояний — отдельной группой там же.
        heroRevisionStates = new VisualElement { name = "hero-screen-hero-states" };
        heroRevisionStates.AddToClassList("hero-revision-states");
        statesRow.Add(heroRevisionStates);
        // Качества — столбцом, одно число.
        root.Q("hero-screen-qualities-grid")?.AddToClassList("hero-revision-qualities");
        if (heroScreenQualityFills != null)
        {
            foreach (VisualElement fill in heroScreenQualityFills)
                Hide(fill?.parent);
        }

        // П. 8: пять боевых позиций внизу, командир в центре; без «Людей отряда».
        VisualElement commanderCard = root.Q("hero-screen-roster-commander");
        if (commanderCard != null && commanderCard.parent == rosterRow)
            rosterRow.Insert(Mathf.Min(2, rosterRow.childCount - 1), commanderCard);
        VisualElement squadPage = rosterBar.parent;
        squadPage?.Add(rosterBar);
        squadPage?.AddToClassList("hero-revision-squad");
        Hide(root.Q("hero-screen-states"));
        foreach (HeroRosterCardRefs refs in heroScreenRosterCards)
            Hide(refs?.Condition);

        // П. 7: свита — квадратные портреты только реальных участников.
        heroRevisionRetinue = new VisualElement { name = "hero-screen-retinue-portraits" };
        heroRevisionRetinue.AddToClassList("hero-revision-retinue");
        retinuePanel.Insert(Mathf.Min(1, retinuePanel.childCount), heroRevisionRetinue);
        Hide(root.Q("hero-screen-retinue-description"));

        // Пустая боевая позиция — выбор свободного бойца.
        heroRevisionPicker = new VisualElement { name = "hero-screen-fighter-picker" };
        heroRevisionPicker.AddToClassList("hero-revision-picker");
        heroRevisionPicker.style.display = DisplayStyle.None;
        heroRevisionPicker.RegisterCallback<PointerDownEvent>(evt =>
        {
            if (evt.target == heroRevisionPicker)
            {
                heroRevisionPicker.style.display = DisplayStyle.None;
                evt.StopPropagation();
            }
        });
        squadPage?.Add(heroRevisionPicker);

        // П. 9: разница характеристик до/после — маленькой меткой у числа.
        for (int i = 0; i < heroStatDeltaLabels.Length && i < heroScreenUnitCardStatValues.Length; i++)
        {
            Label value = heroScreenUnitCardStatValues[i];
            if (value?.parent == null)
                continue;
            Label delta = new Label { name = value.name + "-delta", pickingMode = PickingMode.Ignore };
            delta.AddToClassList("hero-revision-stat-delta");
            value.parent.Insert(value.parent.IndexOf(value), delta);
            heroStatDeltaLabels[i] = delta;
        }
        heroRevisionApplied = true;
    }

    private static void Hide(VisualElement element)
    {
        if (element != null)
            element.style.display = DisplayStyle.None;
    }

    // ------------------------------------------------------------------
    // Главная страница
    // ------------------------------------------------------------------

    private void RefreshHeroRevisionHeroPage()
    {
        CommanderData commander = gameState.GetSelectedCommander();
        bool named = commander != null && !string.IsNullOrWhiteSpace(commander.Name) && commander.Name != ServiceCommanderName;
        heroScreenNameLabel.style.display = named ? DisplayStyle.Flex : DisplayStyle.None;

        heroRevisionStates.Clear();
        ResidentState resident = commander != null ? HomePeopleService.Find(gameState, commander.Id) : null;
        foreach ((string glyph, string title, string text, string tone) in CommanderStates(resident))
        {
            Label icon = new Label(glyph) { name = "hero-state-" + tone };
            icon.AddToClassList("hero-revision-state");
            icon.AddToClassList("hero-revision-state--" + tone);
            icon.tooltip = title + ". " + text;
            icon.RegisterCallback<PointerEnterEvent>(_ => ShowHeroScreenTagTooltip(icon, title, text));
            icon.RegisterCallback<PointerLeaveEvent>(_ => HideHeroScreenTagTooltip());
            heroRevisionStates.Add(icon);
        }
    }

    // Действующие состояния героя из его данных: тяжёлая рана, изнеможение,
    // ранен (меньше жизней). Новых состояний здесь нет.
    private static List<(string Glyph, string Title, string Text, string Tone)> CommanderStates(ResidentState resident)
    {
        List<(string, string, string, string)> result = new List<(string, string, string, string)>();
        if (resident == null)
            return result;
        if (resident.Injury == ResidentInjury.Recovering)
            result.Add(("✚", "Тяжёлая рана", "В поход не пойдёт, пока не оправится.", "injury"));
        if (resident.Exhausted)
            result.Add(("◐", "Изнеможение", "Атака и инициатива ниже на 1, пока не отдохнёт.", "exhausted"));
        if (resident.HasCombatState && resident.CurrentHitPoints < resident.MaxHitPoints && resident.Injury != ResidentInjury.Recovering)
            result.Add(("♥", "Ранен", "Жизни: " + resident.CurrentHitPoints + " из " + resident.MaxHitPoints + ". Лечит отдых и уход.", "wounded"));
        return result;
    }

    // ------------------------------------------------------------------
    // Отряд: свита и выбор бойца
    // ------------------------------------------------------------------

    private void RefreshHeroRevisionRetinue()
    {
        heroRevisionRetinue.Clear();
        List<string> retinue = new List<string>();
        if (gameState.HasActiveExpedition)
            retinue.AddRange(gameState.ActiveExpedition.RetinueIds);
        else if (!string.IsNullOrEmpty(SelectedRetinueId))
            retinue.Add(SelectedRetinueId);
        foreach (string personId in retinue)
        {
            ResidentState resident = HomePeopleService.Find(gameState, personId);
            if (resident == null)
                continue;
            heroRevisionRetinue.Add(CreateRetinuePortrait(resident));
        }
        heroRevisionRetinue.style.display = heroRevisionRetinue.childCount > 0 ? DisplayStyle.Flex : DisplayStyle.None;

        // Выбор специалиста — только пока состав можно менять; в походе
        // свита уже ушла и видна портретами.
        bool editable = ExpeditionPreparation.CanEdit(gameState);
        foreach (string name in new[] { "hero-screen-retinue-row", "hero-screen-retinue-forecast" })
        {
            VisualElement element = interfaceRoot.Q(name);
            if (element != null)
                element.style.display = editable ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }

    private VisualElement CreateRetinuePortrait(ResidentState resident)
    {
        VisualElement card = new VisualElement { name = "hero-retinue-" + resident.PersonId };
        card.AddToClassList("hero-revision-retinue-card");
        VisualElement portrait = new VisualElement();
        portrait.AddToClassList("hero-revision-retinue-portrait");
        if (narrativeDialogueDatabase == null)
            narrativeDialogueDatabase = KingdomSurvival.DialogueDatabase.DialogueDatabaseRuntime.LoadDefaultDatabase();
        Sprite sprite = !string.IsNullOrEmpty(resident.DialogueSpeakerId)
            ? narrativeDialogueDatabase?.FindSpeaker(resident.DialogueSpeakerId)?.Portrait
            : null;
        if (sprite != null)
            portrait.style.backgroundImage = new StyleBackground(sprite);
        else
            portrait.Add(new Label(string.IsNullOrEmpty(resident.DisplayName) ? "?" : resident.DisplayName.Substring(0, 1)));
        card.Add(portrait);
        Label name = new Label(resident.DisplayName);
        name.AddToClassList("hero-revision-retinue-name");
        card.Add(name);
        card.tooltip = resident.DisplayName + " — " + resident.RoleLabel + ". ПКМ — подробнее.";
        string personId = resident.PersonId;
        card.RegisterCallback<PointerDownEvent>(evt =>
        {
            if (evt.button != 1)
                return;
            evt.StopPropagation();
            ResidentState current = HomePeopleService.Find(gameState, personId);
            if (current != null)
                ShowHeroInfoPopup(current.DisplayName, current.RoleLabel, current.ShortDescription +
                                  "\n\nВ бой не идёт, ест из припасов похода.");
        });
        return card;
    }

    // Пустая боевая позиция: свободные бойцы списком (то же действие, что
    // «В состав» прежнего блока «Люди отряда»).
    private bool TryOpenHeroFighterPicker()
    {
        if (heroRevisionPicker == null || !ExpeditionPreparation.CanEdit(gameState))
            return false;
        heroRevisionPicker.Clear();
        VisualElement box = new VisualElement();
        box.AddToClassList("hero-revision-picker-box");
        Label title = new Label("Кого взять в поход?");
        title.AddToClassList("hero-revision-picker-title");
        box.Add(title);
        int count = 0;
        foreach (HeroSquadEntry entry in CollectHeroSquad())
        {
            if (entry.IsCommander || entry.InRoster || entry.Resident == null)
                continue;
            string personId = entry.PersonId;
            string state = entry.Resident.Injury == ResidentInjury.Recovering ? " · тяжёлая рана" : entry.Hurt ? " · ранен" : string.Empty;
            Button pick = new Button(() =>
            {
                heroRevisionPicker.style.display = DisplayStyle.None;
                ChangeHeroSquadRoster(personId, true);
            })
            { text = entry.Resident.DisplayName + " — " + entry.Resident.RoleLabel + state };
            pick.AddToClassList("hero-revision-picker-option");
            box.Add(pick);
            count++;
        }
        if (count == 0)
            box.Add(new Label("Свободных бойцов нет."));
        Button close = new Button(() => heroRevisionPicker.style.display = DisplayStyle.None) { text = "Закрыть" };
        close.AddToClassList("hero-revision-picker-close");
        box.Add(close);
        heroRevisionPicker.Add(box);
        heroRevisionPicker.style.display = DisplayStyle.Flex;
        heroRevisionPicker.BringToFront();
        return true;
    }

    private void ShowHeroInfoPopup(string name, string kind, string text)
    {
        VisualElement popup = interfaceRoot.Q("hero-screen-item-popup");
        if (popup == null)
            return;
        Label nameLabel = popup.Q<Label>("hero-screen-item-popup-name");
        Label kindLabel = popup.Q<Label>("hero-screen-item-popup-kind");
        Label textLabel = popup.Q<Label>("hero-screen-item-popup-text");
        VisualElement icon = popup.Q("hero-screen-item-popup-icon");
        if (nameLabel != null)
            nameLabel.text = name;
        if (kindLabel != null)
            kindLabel.text = kind;
        if (textLabel != null)
            textLabel.text = text;
        if (icon != null)
            icon.style.display = DisplayStyle.None;
        popup.style.display = DisplayStyle.Flex;
        popup.BringToFront();
    }

    // ------------------------------------------------------------------
    // Снаряжение: разница характеристик до/после
    // ------------------------------------------------------------------

    private void RefreshHeroStatDeltas()
    {
        if (heroScreenUnitCardStatValues == null || string.IsNullOrEmpty(heroCardPersonId))
            return;
        AssembledCombatStats stats = CombatStatsAssembler.Compute(gameState, heroCardPersonId);
        if (!stats.HasTemplate)
            return;
        UnitCombatStats f = stats.Final;
        int[] now = { f.MaxHitPoints, f.Attack, f.Defense, f.Damage, f.Movement, f.Initiative, f.AttackRange };
        if (heroStatSnapshotPerson == heroCardPersonId && heroStatSnapshot != null)
        {
            bool changed = false;
            int[] deltas = new int[now.Length];
            for (int i = 0; i < now.Length; i++)
            {
                deltas[i] = now[i] - heroStatSnapshot[i];
                changed |= deltas[i] != 0;
            }
            if (changed)
            {
                heroStatDeltas = deltas;
                heroStatDeltaUntil = Time.unscaledTime + StatDeltaSeconds;
            }
        }
        else
        {
            heroStatDeltas = null;
        }
        heroStatSnapshotPerson = heroCardPersonId;
        heroStatSnapshot = now;

        bool show = heroStatDeltas != null && Time.unscaledTime < heroStatDeltaUntil;
        for (int i = 0; i < heroStatDeltaLabels.Length; i++)
        {
            Label label = heroStatDeltaLabels[i];
            if (label == null)
                continue;
            int delta = show ? heroStatDeltas[i] : 0;
            label.text = delta > 0 ? "+" + delta : delta < 0 ? "−" + (-delta) : string.Empty;
            label.EnableInClassList("hero-revision-stat-delta--up", delta > 0);
            label.EnableInClassList("hero-revision-stat-delta--down", delta < 0);
        }
    }
}

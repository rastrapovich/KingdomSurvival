using System.Collections.Generic;
using KingdomSurvival.UnitDatabase;
using UnityEngine;
using UnityEngine.UIElements;

// Рабочее место человека на экране героя (hero-screen-unit-card) — общее
// для вкладок «Снаряжение» и «Развитие»: полоса людей отряда слева
// (HeroScreenTabs.cs), портрет, уровень, опыт и состояние выбранного;
// на «Снаряжении» — собранные боевые характеристики и теги, три слота,
// сумка и кладовая (HeroItems.cs); на «Развитии» — выбор развития карточками
// с фильтром по группе, особенности и следы, компетенции с порядком
// (канон v1.48 §27). Открывается только вкладкой; человек выбирается в
// полосе слева. Качества — только у Командира, на вкладке «Герой».
// Структура — Prototype_Main.uxml, стиль — HeroScreen.uss.
public partial class PrototypeUIController
{
    private enum HeroGrowthFilter
    {
        All,
        Personal,
        Learn,
        Neutral
    }

    private enum HeroCompetencySort
    {
        Rank,
        Practice,
        Name
    }

    private sealed class HeroCompetencyEntry
    {
        public string Id;
        public string Name;
        public int Rank;
        public int Practice;
        public int Needed;
        public bool Capped;
    }

    private string heroCardPersonId;
    private string heroCardMessage = string.Empty;

    private Label heroCardSubtitle;
    private Label heroCardLevel;
    private Label heroCardExperienceLabel;
    private VisualElement heroCardExperienceFill;
    private Label heroCardCondition;
    private VisualElement heroCardHealthFill;
    private Label heroCardMessageLabel;
    private VisualElement heroCardChoice;
    private VisualElement heroCardCompetencies;
    private VisualElement heroCardFeatures;
    private VisualElement heroGrowthChoicePanel;
    private VisualElement heroGrowthFeaturesPanel;
    private Label heroGrowthChoiceTitle;
    private ScrollView heroGrowthChoiceScroll;
    private Label heroHeroGrowthLabel;
    private VisualElement heroHeroGrowthList;
    private HeroToggleGroup<HeroGrowthFilter> heroGrowthFilter;
    private HeroToggleGroup<HeroCompetencySort> heroCompetencySort;
    private readonly string[] heroCardStatExplanations = new string[7];

    private bool IsHeroCardOpen =>
        heroScreenUnitCard != null && heroScreenUnitCard.style.display == DisplayStyle.Flex;

    private bool BindHeroCard()
    {
        heroCardSubtitle = BindRequiredElement<Label>(interfaceRoot, HeroScreenName, "hero-screen-unit-card-subtitle");
        heroCardLevel = BindRequiredElement<Label>(interfaceRoot, HeroScreenName, "hero-screen-unit-card-level");
        heroCardExperienceLabel = BindRequiredElement<Label>(interfaceRoot, HeroScreenName, "hero-screen-unit-card-experience-label");
        heroCardExperienceFill = BindRequiredElement<VisualElement>(interfaceRoot, HeroScreenName, "hero-screen-unit-card-experience-fill");
        heroCardCondition = BindRequiredElement<Label>(interfaceRoot, HeroScreenName, "hero-screen-unit-card-condition");
        heroCardHealthFill = BindRequiredElement<VisualElement>(interfaceRoot, HeroScreenName, "hero-screen-unit-card-portrait-healthbar-fill");
        heroCardMessageLabel = BindRequiredElement<Label>(interfaceRoot, HeroScreenName, "hero-screen-unit-card-message");
        heroCardChoice = BindRequiredElement<VisualElement>(interfaceRoot, HeroScreenName, "hero-screen-unit-card-choice");
        heroCardCompetencies = BindRequiredElement<VisualElement>(interfaceRoot, HeroScreenName, "hero-screen-unit-card-competencies");
        heroCardFeatures = BindRequiredElement<VisualElement>(interfaceRoot, HeroScreenName, "hero-screen-unit-card-features");
        heroGrowthChoicePanel = BindRequiredElement<VisualElement>(interfaceRoot, HeroScreenName, "hero-screen-growth-choice-panel");
        heroGrowthFeaturesPanel = BindRequiredElement<VisualElement>(interfaceRoot, HeroScreenName, "hero-screen-growth-features-panel");
        heroGrowthChoiceTitle = BindRequiredElement<Label>(interfaceRoot, HeroScreenName, "hero-screen-growth-choice-title");
        heroGrowthChoiceScroll = BindRequiredElement<ScrollView>(interfaceRoot, HeroScreenName, "hero-screen-growth-choice-scroll");
        heroHeroGrowthLabel = BindRequiredElement<Label>(interfaceRoot, HeroScreenName, "hero-screen-hero-growth-label");
        heroHeroGrowthList = BindRequiredElement<VisualElement>(interfaceRoot, HeroScreenName, "hero-screen-hero-growth-list");

        bool ok = heroHeroGrowthLabel != null && heroHeroGrowthList != null &&
heroCardSubtitle != null && heroCardLevel != null && heroCardExperienceLabel != null &&
                  heroCardExperienceFill != null && heroCardCondition != null && heroCardHealthFill != null &&
                  heroCardMessageLabel != null && heroCardChoice != null && heroCardCompetencies != null &&
                  heroCardFeatures != null && heroGrowthChoicePanel != null && heroGrowthFeaturesPanel != null &&
                  heroGrowthChoiceTitle != null && heroGrowthChoiceScroll != null;

        heroGrowthFilter = new HeroToggleGroup<HeroGrowthFilter>(HeroGrowthFilter.All, () =>
        {
            heroGrowthChoiceScroll.scrollOffset = Vector2.zero;
            RefreshHeroScreen();
        });
        heroCompetencySort = new HeroToggleGroup<HeroCompetencySort>(HeroCompetencySort.Rank, RefreshHeroScreen);
        ok &= heroGrowthFilter.Add(BindHeroToggle("hero-screen-growth-filter-all"), HeroGrowthFilter.All);
        ok &= heroGrowthFilter.Add(BindHeroToggle("hero-screen-growth-filter-personal"), HeroGrowthFilter.Personal);
        ok &= heroGrowthFilter.Add(BindHeroToggle("hero-screen-growth-filter-learn"), HeroGrowthFilter.Learn);
        ok &= heroGrowthFilter.Add(BindHeroToggle("hero-screen-growth-filter-neutral"), HeroGrowthFilter.Neutral);
        ok &= heroCompetencySort.Add(BindHeroToggle("hero-screen-growth-sort-rank"), HeroCompetencySort.Rank);
        ok &= heroCompetencySort.Add(BindHeroToggle("hero-screen-growth-sort-practice"), HeroCompetencySort.Practice);
        ok &= heroCompetencySort.Add(BindHeroToggle("hero-screen-growth-sort-name"), HeroCompetencySort.Name);

        // Портрет добавлен в слот после полосы здоровья — полоса поверх него.
        if (ok && heroScreenUnitCardPortraitHealthBar != null)
            heroScreenUnitCardPortraitHealthBar.BringToFront();
        return ok;
    }

    private void RefreshHeroPersonCard()
    {
        if (!heroScreenUiBound || !IsHeroCardOpen || gameState == null)
            return;

        string personId = heroCardPersonId;
        ResidentState resident = HomePeopleService.Find(gameState, personId);
        if (resident == null || !resident.IsAlive || !IsHeroItemsCandidate(resident))
        {
            // Человек выбыл из отряда (погиб, остался дома) — к Командиру.
            heroCardPersonId = null;
            EnsureHeroPersonSelected();
            personId = heroCardPersonId;
            resident = HomePeopleService.Find(gameState, personId);
            if (resident == null)
                return;
        }

        CommanderData commander = gameState.GetSelectedCommander();
        bool isCommander = commander != null && commander.Id == personId;
        FighterData fighter = isCommander ? commander : gameState.FindFighter(personId);
        // Боевая основа — та же, что уходит в бой (у героя пока ополчение);
        // облик героя не подменяется портретом ополченца.
        string unitTypeId = isCommander
            ? (string.IsNullOrWhiteSpace(commander.UnitTypeId) ? CampaignBattleBridge.HeroFallbackUnitTypeId : commander.UnitTypeId)
            : resident.UnitTypeId;
        UnitDefinitionData unit = heroScreenUnits != null && !string.IsNullOrEmpty(unitTypeId)
            ? heroScreenUnits.FindById(unitTypeId)
            : null;
        if (unit == null)
            unit = ResolveHeroScreenUnit(fighter);
        UnitDefinitionData portraitUnit = isCommander ? ResolveHeroScreenUnit(commander) : unit;

        heroScreenUnitCardTitle.text = resident.DisplayName.ToUpperInvariant();
        string role = isCommander ? "Командир" : fighter != null ? fighter.Role : resident.RoleLabel;
        heroCardSubtitle.text = role + (unit != null ? " · " + HeroScreenRoleLabel(unit) : string.Empty);
        ApplyHeroScreenPortrait(heroScreenUnitCardPortrait, portraitUnit);

        FillHeroCardProfile(resident);
        FillHeroCardStats(personId, unit, resident);
        FillHeroCardTags(unit);

        ItemService.EnsureInventory(gameState);
        for (int i = 0; i < HeroItemSlots.Length; i++)
            FillHeroItemSlot(i, personId, HeroItemSlots[i]);
        heroCardMessageLabel.text = heroCardMessage;

        RefreshHeroCardDevelopment(personId);
    }

    // Уровень, опыт и состояние словами — под портретом.
    private void FillHeroCardProfile(ResidentState resident)
    {
        PersonProgressionData record = CharacterProgressionService.Get(gameState, resident.PersonId);
        if (record != null)
        {
            CharacterProgressionService.GetLevelProgress(gameState, record, out int current, out int required);
            heroCardLevel.text = "УР. " + record.Level;
            heroCardExperienceLabel.text = required > 0 ? current + " / " + required + " опыта" : "предел пути";
            float percent = required > 0 ? Mathf.Clamp01((float)current / required) * 100f : 100f;
            heroCardExperienceFill.style.width = Length.Percent(percent);
        }
        else
        {
            heroCardLevel.text = string.Empty;
            heroCardExperienceLabel.text = string.Empty;
            heroCardExperienceFill.style.width = Length.Percent(0f);
        }

        float health = resident.HasCombatState && resident.MaxHitPoints > 0
            ? Mathf.Clamp01((float)resident.CurrentHitPoints / resident.MaxHitPoints) * 100f
            : 100f;
        heroCardHealthFill.style.width = Length.Percent(health);
        heroCardCondition.text = DescribeCondition(resident);
    }

    // Те же собранные числа, что уходят в бой; по наведению — из чего сложилось.
    private void FillHeroCardStats(string personId, UnitDefinitionData unit, ResidentState resident)
    {
        AssembledCombatStats stats = CombatStatsAssembler.Compute(gameState, personId);
        UnitCombatStats final;
        UnitCombatStats template;
        List<string> sources = new List<string>();
        if (stats.HasTemplate)
        {
            final = stats.Final;
            template = stats.Template;
            sources.AddRange(stats.Sources);
        }
        else if (unit != null)
        {
            final = new UnitCombatStats
            {
                MaxHitPoints = unit.MaxHitPoints, Attack = unit.Attack, Defense = unit.Defense, Damage = unit.Damage,
                Movement = unit.Movement, Initiative = unit.Initiative, AttackRange = unit.AttackRange
            };
            template = final;
        }
        else
        {
            for (int i = 0; i < heroScreenUnitCardStatValues.Length; i++)
            {
                heroScreenUnitCardStatValues[i].text = "—";
                heroCardStatExplanations[i] = HeroScreenUnitCardStatExplanations[i];
            }
            return;
        }

        heroScreenUnitCardStatValues[0].text = resident != null && resident.HasCombatState
            ? resident.CurrentHitPoints + " / " + final.MaxHitPoints
            : final.MaxHitPoints.ToString();
        heroScreenUnitCardStatValues[1].text = final.Attack.ToString();
        heroScreenUnitCardStatValues[2].text = final.Defense.ToString();
        heroScreenUnitCardStatValues[3].text = final.Damage.ToString();
        heroScreenUnitCardStatValues[4].text = final.Movement.ToString();
        heroScreenUnitCardStatValues[5].text = final.Initiative.ToString();
        heroScreenUnitCardStatValues[6].text = final.AttackRange.ToString();

        int[] baseValues = { template.MaxHitPoints, template.Attack, template.Defense, template.Damage, template.Movement, template.Initiative, template.AttackRange };
        string sourceText = sources.Count > 0 ? "\n" + string.Join("\n", sources) : string.Empty;
        for (int i = 0; i < heroCardStatExplanations.Length; i++)
            heroCardStatExplanations[i] = HeroScreenUnitCardStatExplanations[i] + "\nОснова: " + baseValues[i] + sourceText;
    }

    private void FillHeroCardTags(UnitDefinitionData unit)
    {
        heroScreenUnitCardTagsRow.Clear();
        if (heroScreenUnits == null || unit == null)
            return;
        for (int i = 0; i < unit.TagIds.Count; i++)
        {
            UnitTagDefinition tag = heroScreenUnits.FindTag(unit.TagIds[i]);
            if (tag != null)
                AddHeroScreenChip(heroScreenUnitCardTagsRow, tag.DisplayLabel, tag.Color, tag.Description);
        }
    }

    // Развитие: выбор каждые 3 уровня (§27.1.1) — карточками в сетке с
    // фильтром по группе (показ растёт с 3 до 35 карточек, 12Е-3);
    // особенности и следы; компетенции со ступенью и практикой (§27.6,
    // каталог v1.49 §27.11) в выбранном порядке.
    private void RefreshHeroCardDevelopment(string personId)
    {
        heroCardChoice.Clear();
        heroCardCompetencies.Clear();
        heroCardFeatures.Clear();
        PersonProgressionData record = CharacterProgressionService.Get(gameState, personId);
        int pending = record != null ? CharacterProgressionService.PendingChoices(gameState, personId) : 0;
        SetHeroGrowthChoiceOpen(pending > 0);

        if (record == null)
        {
            heroGrowthChoiceTitle.text = "ВЫБОР РАЗВИТИЯ";
            heroCardChoice.Add(CreateHeroItemRow("Не развивается", "Уровень и компетенции есть у Командира и постоянных бойцов."));
            AddHeroScreenHint(heroCardCompetencies, "Компетенций нет.");
            return;
        }

        if (pending > 0)
        {
            heroGrowthChoiceTitle.text = "ВЫБОР РАЗВИТИЯ" + (pending > 1 ? " — ждёт " + pending : string.Empty) +
                                         "  ·  кем становится человек: из пережитого или новое направление";
            List<DevelopmentOption> options = CharacterProgressionService.GetChoiceOptions(gameState, personId);
            heroGrowthFilter.SetCount(HeroGrowthFilter.All, options.Count);
            heroGrowthFilter.SetCount(HeroGrowthFilter.Personal, options.FindAll(o => GetHeroGrowthGroup(o) == HeroGrowthFilter.Personal).Count);
            heroGrowthFilter.SetCount(HeroGrowthFilter.Learn, options.FindAll(o => GetHeroGrowthGroup(o) == HeroGrowthFilter.Learn).Count);
            heroGrowthFilter.SetCount(HeroGrowthFilter.Neutral, options.FindAll(o => GetHeroGrowthGroup(o) == HeroGrowthFilter.Neutral).Count);

            // Группы по смыслу, внутри — как предложил сервис.
            options.Sort((a, b) => GetHeroGrowthGroup(a).CompareTo(GetHeroGrowthGroup(b)));
            foreach (DevelopmentOption option in options)
            {
                HeroGrowthFilter group = GetHeroGrowthGroup(option);
                if (heroGrowthFilter.Value != HeroGrowthFilter.All && heroGrowthFilter.Value != group)
                    continue;
                heroCardChoice.Add(CreateHeroGrowthCard(personId, option, group));
            }

            if (heroCardChoice.childCount == 0)
                AddHeroScreenHint(heroCardChoice, "В этой группе вариантов нет — выберите «Все».");
        }
        else
        {
            heroGrowthChoiceTitle.text = "ВЫБОР РАЗВИТИЯ";
            int nextChoiceLevel = CharacterProgressionService.ProfileFor(gameState, personId).NextChoiceLevel(record.Level);
            heroCardChoice.Add(CreateHeroItemRow(
                nextChoiceLevel > 0 ? "Следующий выбор — на уровне " + nextChoiceLevel : "Выборов развития больше нет",
                "Опыт — за новое и значимое: бои, места, встречи. Повтор одного и того же почти ничему не учит; уровень сам сил не прибавляет."));
        }

        RefreshHeroCardFeatures(personId);
        RefreshHeroCardCompetencies(personId, record);
    }

    // Выбора нет — панель выбора короткая, особенности забирают высоту
    // (USS не знает соседнего селектора, поэтому два класса).
    private void SetHeroGrowthChoiceOpen(bool open)
    {
        heroGrowthChoicePanel.EnableInClassList("hero-screen-growth-choice-panel--idle", !open);
        heroGrowthFeaturesPanel.EnableInClassList("hero-screen-growth-features-panel--tall", !open);
    }

    private static HeroGrowthFilter GetHeroGrowthGroup(DevelopmentOption option)
    {
        if (option.IsPersonal)
            return HeroGrowthFilter.Personal;
        return option.Kind == DevelopmentOptionKind.Learn ? HeroGrowthFilter.Learn : HeroGrowthFilter.Neutral;
    }

    private VisualElement CreateHeroGrowthCard(string personId, DevelopmentOption option, HeroGrowthFilter group)
    {
        VisualElement card = CreateHeroItemRow(option.Title, option.Description);
        card.AddToClassList("hero-growth-card");
        card.AddToClassList(group == HeroGrowthFilter.Personal ? "hero-growth-card--personal"
            : group == HeroGrowthFilter.Learn ? "hero-growth-card--learn"
            : "hero-growth-card--neutral");
        Label groupLabel = new Label(group == HeroGrowthFilter.Personal ? "ИЗ ПЕРЕЖИТОГО"
            : group == HeroGrowthFilter.Learn ? "НОВОЕ ДЕЛО"
            : "НЕЙТРАЛЬНОЕ");
        groupLabel.AddToClassList("hero-growth-card-group");
        card.Insert(0, groupLabel);

        string optionId = option.Id;
        VisualElement actions = new VisualElement();
        actions.AddToClassList("hero-item-actions");
        AddHeroItemButton(actions, "Выбрать", () =>
            RunHeroCardCommand(CharacterProgressionService.TryApplyChoice(gameState, personId, optionId, out string message), message))
            .AddToClassList("hero-item-button--primary");
        card.Add(actions);
        return card;
    }

    // Особенности человека (12Е-2): название с рангом, что даёт и откуда взялась.
    private void RefreshHeroCardFeatures(string personId)
    {
        List<OwnedFeature> features = CharacterFeatureService.GetFeatures(gameState, personId);
        foreach (OwnedFeature owned in features)
        {
            string effect = string.Empty;
            if (owned.Entry != null)
            {
                List<string> ranks = new List<string>();
                for (int rank = 1; rank <= Mathf.Min(owned.Rank, owned.Entry.Ranks.Count); rank++)
                    ranks.Add(owned.Entry.Ranks[rank - 1].Effect);
                effect = ranks.Count > 0 ? string.Join(" ", ranks) : owned.Entry.Description;
            }
            AddHeroGrowthFeature(owned.Title, Join(effect, "откуда: " + FeatureLabels.Sources(owned.Source)));
        }

        if (features.Count == 0)
            AddHeroGrowthFeature("Пока нет", "Особенности приходят из выбора развития, биографии, историй и от учителей.");

        // 12Е-8: следы развития — только след и ступень; что он откроет,
        // карточка не говорит (решение автора 28.09.2026).
        foreach (KeyValuePair<TraceDefinition, TraceStage> trace in ProgressionTraces.Visible(gameState, personId))
            AddHeroGrowthFeature("След: " + trace.Key.Name, ProgressionTraces.StageLabel(trace.Value));
    }

    private void AddHeroGrowthFeature(string title, string description)
    {
        VisualElement row = CreateHeroItemRow(title, description);
        row.AddToClassList("hero-growth-feature");
        heroCardFeatures.Add(row);
    }

    // Компетенции: ступень точками, практика до следующей ступени; порядок —
    // по ступени, по близости роста или по названию.
    private void RefreshHeroCardCompetencies(string personId, PersonProgressionData record)
    {
        List<HeroCompetencyEntry> entries = new List<HeroCompetencyEntry>();
        foreach (string competencyId in CharacterProgressionService.KnownCompetencies(gameState, personId))
        {
            int rank = CharacterProgressionService.GetCompetencyRank(gameState, personId, competencyId);
            CompetencyProgressData progress = record.FindCompetency(competencyId);
            int ceiling = progress != null ? Mathf.Max(CharacterProgression.PracticeCeiling, progress.Ceiling) : CharacterProgression.PracticeCeiling;
            entries.Add(new HeroCompetencyEntry
            {
                Id = competencyId,
                Name = NarrativeCompetencyLabels.GetLabel(competencyId),
                Rank = rank,
                Practice = progress != null ? progress.Practice : 0,
                Needed = CharacterProgression.PracticeToNextRank(rank),
                Capped = rank >= CharacterProgression.MaxCompetencyRank || rank >= ceiling
            });
        }

        HeroCompetencySort sort = heroCompetencySort.Value;
        entries.Sort((a, b) =>
        {
            int result = 0;
            if (sort == HeroCompetencySort.Rank)
                result = b.Rank.CompareTo(a.Rank);
            else if (sort == HeroCompetencySort.Practice)
                result = a.Capped != b.Capped ? a.Capped.CompareTo(b.Capped) : HeroPracticeShare(b).CompareTo(HeroPracticeShare(a));
            return result != 0 ? result : string.Compare(a.Name, b.Name, System.StringComparison.CurrentCulture);
        });

        foreach (HeroCompetencyEntry entry in entries)
            heroCardCompetencies.Add(CreateHeroCompetencyRow(entry));

        if (entries.Count == 0)
            AddHeroScreenHint(heroCardCompetencies, "Пока ничего не освоено: компетенции растут от применения.");
    }

    // Вкладка «Герой»: ждёт ли Командир выбора и чем он уже владеет —
    // компетенции по ступени; подробности и выбор — на «Развитии».
    private void RefreshHeroGrowthSummary()
    {
        heroHeroGrowthList.Clear();
        CommanderData commander = gameState.GetSelectedCommander();
        PersonProgressionData record = commander != null ? CharacterProgressionService.Get(gameState, commander.Id) : null;
        if (record == null)
        {
            heroHeroGrowthLabel.text = "Командир пока не развивается.";
            return;
        }

        int pending = CharacterProgressionService.PendingChoices(gameState, commander.Id);
        int next = CharacterProgressionService.ProfileFor(gameState, commander.Id).NextChoiceLevel(record.Level);
        heroHeroGrowthLabel.text = pending > 0
            ? "Ждёт выбор развития" + (pending > 1 ? ": " + pending : string.Empty) + " — вкладка «Развитие»."
            : next > 0 ? "Следующий выбор развития — на уровне " + next + "." : "Выборов развития больше нет.";

        List<HeroCompetencyEntry> entries = new List<HeroCompetencyEntry>();
        foreach (string competencyId in CharacterProgressionService.KnownCompetencies(gameState, commander.Id))
        {
            int rank = CharacterProgressionService.GetCompetencyRank(gameState, commander.Id, competencyId);
            CompetencyProgressData progress = record.FindCompetency(competencyId);
            int ceiling = progress != null ? Mathf.Max(CharacterProgression.PracticeCeiling, progress.Ceiling) : CharacterProgression.PracticeCeiling;
            entries.Add(new HeroCompetencyEntry
            {
                Id = competencyId,
                Name = NarrativeCompetencyLabels.GetLabel(competencyId),
                Rank = rank,
                Practice = progress != null ? progress.Practice : 0,
                Needed = CharacterProgression.PracticeToNextRank(rank),
                Capped = rank >= CharacterProgression.MaxCompetencyRank || rank >= ceiling
            });
        }
        entries.Sort((a, b) => a.Rank != b.Rank ? b.Rank.CompareTo(a.Rank) : string.Compare(a.Name, b.Name, System.StringComparison.CurrentCulture));
        foreach (HeroCompetencyEntry entry in entries)
            heroHeroGrowthList.Add(CreateHeroCompetencyRow(entry));
        if (entries.Count == 0)
            AddHeroScreenHint(heroHeroGrowthList, "Компетенций пока нет: они растут от применения.");
    }

    private static float HeroPracticeShare(HeroCompetencyEntry entry)
    {
        return entry.Needed > 0 ? (float)entry.Practice / entry.Needed : 0f;
    }

    private VisualElement CreateHeroCompetencyRow(HeroCompetencyEntry entry)
    {
        VisualElement row = new VisualElement();
        row.AddToClassList("hero-competency-row");
        row.EnableInClassList("hero-competency-row--capped", entry.Capped && entry.Rank < CharacterProgression.MaxCompetencyRank);

        VisualElement head = new VisualElement();
        head.AddToClassList("hero-competency-head");
        Label name = new Label(entry.Name.ToUpperInvariant());
        name.AddToClassList("hero-competency-name");
        head.Add(name);
        VisualElement pips = new VisualElement();
        pips.AddToClassList("hero-competency-pips");
        for (int i = 1; i <= CharacterProgression.MaxCompetencyRank; i++)
        {
            VisualElement pip = new VisualElement();
            pip.AddToClassList("hero-competency-pip");
            pip.EnableInClassList("hero-competency-pip--filled", i <= entry.Rank);
            pips.Add(pip);
        }
        head.Add(pips);
        row.Add(head);

        string practice = entry.Rank >= CharacterProgression.MaxCompetencyRank
            ? "Высшая ступень."
            : entry.Capped
                ? "Собственной практикой дальше не вырасти: нужен наставник или новое знание."
                : "Практика: " + entry.Practice + " / " + entry.Needed + " до ступени " + (entry.Rank + 1) + ".";
        if (!entry.Capped)
            row.Add(HeroMiniBar(HeroPracticeShare(entry), null));
        Label note = new Label("Ступень " + entry.Rank + " из " + CharacterProgression.MaxCompetencyRank + ". " + practice);
        note.AddToClassList("hero-competency-note");
        row.Add(note);

        string title = entry.Name.ToUpperInvariant();
        string explanation = NarrativeCompetencyLabels.GetDescription(entry.Id) + "\nРастёт от реального применения и учителей. " + practice;
        row.RegisterCallback<PointerEnterEvent>(_ => ShowHeroScreenStatTooltip(row, title, explanation));
        row.RegisterCallback<PointerLeaveEvent>(_ => HideHeroScreenStatTooltip());
        return row;
    }

    private void RunHeroCardCommand(bool ok, string message)
    {
        heroCardMessage = message ?? string.Empty;
        RunHeroItemCommand(ok, message);
    }

    private static string Join(string first, string second)
    {
        if (string.IsNullOrEmpty(first))
            return second ?? string.Empty;
        return string.IsNullOrEmpty(second) ? first : first + " · " + second;
    }
}

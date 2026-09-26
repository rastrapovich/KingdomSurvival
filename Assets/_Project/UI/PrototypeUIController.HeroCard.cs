using System.Collections.Generic;
using KingdomSurvival.UnitDatabase;
using UnityEngine;
using UnityEngine.UIElements;

// Карточка человека на экране героя (hero-screen-unit-card): всё, что
// относится к одному бойцу или Командиру, — портрет, уровень и опыт,
// состояние, собранные боевые характеристики и теги, снаряжение с тем, что
// можно надеть, и развитие (канон v1.48 §27). Открывается ПКМ по карточке
// состава, по доступному в Доме бойцу или кликом по строке отряда. Сам экран
// героя относится ко всему отряду; качества — только у Командира на нём.
// Структура — Prototype_Main.uxml, стиль — HeroScreen.uss.
public partial class PrototypeUIController
{
    private string heroCardPersonId;
    private string heroCardMessage = string.Empty;

    private Label heroCardSubtitle;
    private Label heroCardLevel;
    private Label heroCardExperienceLabel;
    private VisualElement heroCardExperienceFill;
    private Label heroCardCondition;
    private VisualElement heroCardHealthFill;
    private VisualElement heroCardAvailable;
    private Label heroCardMessageLabel;
    private VisualElement heroCardChoice;
    private VisualElement heroCardCompetencies;
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
        heroCardAvailable = BindRequiredElement<VisualElement>(interfaceRoot, HeroScreenName, "hero-screen-unit-card-available");
        heroCardMessageLabel = BindRequiredElement<Label>(interfaceRoot, HeroScreenName, "hero-screen-unit-card-message");
        heroCardChoice = BindRequiredElement<VisualElement>(interfaceRoot, HeroScreenName, "hero-screen-unit-card-choice");
        heroCardCompetencies = BindRequiredElement<VisualElement>(interfaceRoot, HeroScreenName, "hero-screen-unit-card-competencies");

        bool ok = heroCardSubtitle != null && heroCardLevel != null && heroCardExperienceLabel != null &&
                  heroCardExperienceFill != null && heroCardCondition != null && heroCardHealthFill != null &&
                  heroCardAvailable != null && heroCardMessageLabel != null && heroCardChoice != null &&
                  heroCardCompetencies != null;

        // Портрет добавлен в слот после полосы здоровья — полоса поверх него.
        if (ok && heroScreenUnitCardPortraitHealthBar != null)
            heroScreenUnitCardPortraitHealthBar.BringToFront();
        return ok;
    }

    private void ShowHeroPersonCard(string personId)
    {
        if (!heroScreenUiBound || gameState == null || string.IsNullOrEmpty(personId))
            return;

        if (heroCardPersonId != personId)
            heroCardMessage = string.Empty;
        heroCardPersonId = personId;

        heroScreenUnitCardDimmer.style.display = DisplayStyle.Flex;
        heroScreenUnitCard.style.display = DisplayStyle.Flex;
        heroScreenUnitCardDimmer.BringToFront();
        heroScreenUnitCard.BringToFront();
        RefreshHeroPersonCard();
        heroScreenUnitCardDimmer.Focus();
    }

    private void RefreshHeroPersonCard()
    {
        if (!heroScreenUiBound || !IsHeroCardOpen || gameState == null)
            return;

        string personId = heroCardPersonId;
        ResidentState resident = HomePeopleService.Find(gameState, personId);
        if (resident == null || !resident.IsAlive)
        {
            HideHeroScreenUnitCard();
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
        RebuildHeroCardAvailable(personId, resident);
        heroCardMessageLabel.text = heroCardMessage;

        RefreshHeroCardDevelopment(personId);
    }

    // Уровень, опыт и состояние словами — под портретом.
    private void FillHeroCardProfile(ResidentState resident)
    {
        PersonProgressionData record = CharacterProgressionService.Get(gameState, resident.PersonId);
        if (record != null)
        {
            CharacterProgressionService.GetLevelProgress(record, out int current, out int required);
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

    // Что этот человек может надеть прямо сейчас: из сумки героя и (дома) из
    // кладовой; изнеможённому — отвар, если он рядом.
    private void RebuildHeroCardAvailable(string personId, ResidentState resident)
    {
        heroCardAvailable.Clear();
        int shown = 0;
        List<ItemInstanceData> candidates = new List<ItemInstanceData>();
        CommanderData commander = gameState.GetSelectedCommander();
        if (commander != null)
            candidates.AddRange(ItemService.OwnedBy(gameState, commander.Id));
        bool home = !HomePeopleService.HasDeparted(gameState);
        if (home)
            candidates.AddRange(ItemService.Storage(gameState));

        foreach (ItemInstanceData item in candidates)
        {
            ItemDefinition definition = ItemCatalog.Find(item.ItemId);
            if (definition == null || item.Slot != ItemSlot.None || definition.IsStory)
                continue;

            string source = string.IsNullOrEmpty(item.OwnerPersonId) ? "в кладовой" : "в сумке героя";
            string instanceId = item.InstanceId;
            if (definition.Slot != ItemSlot.None)
            {
                if (!ItemService.CanUseSlotItem(gameState, personId, definition, out _))
                    continue;
                ItemInstanceData current = ItemService.Equipped(gameState, personId, definition.Slot);
                ItemDefinition currentDefinition = current != null ? ItemCatalog.Find(current.ItemId) : null;
                VisualElement row = CreateHeroItemRow(
                    definition.Name,
                    Join(EffectText(definition), source + (currentDefinition != null ? " · заменит «" + currentDefinition.Name + "»" : string.Empty)));
                AddHeroCardAction(row, "Надеть", () =>
                    RunHeroCardCommand(ItemService.TryEquip(gameState, instanceId, personId, out string message), message));
                heroCardAvailable.Add(row);
                shown++;
            }
            else if (item.ItemId == ItemCatalog.UlyanaHerbs && resident.Exhausted)
            {
                VisualElement row = CreateHeroItemRow(definition.Name + " ×" + item.UsesLeft, Join(EffectText(definition), source));
                AddHeroCardAction(row, "Дать отвар", () =>
                    RunHeroCardCommand(ItemService.TryUse(gameState, instanceId, personId, out string message), message));
                heroCardAvailable.Add(row);
                shown++;
            }
        }

        if (shown == 0)
        {
            heroCardAvailable.Add(CreateHeroItemRow(
                "Нечего надеть",
                home ? "В сумке героя и кладовой нет подходящих вещей." : "Кладовая Дома — только дома; в пути — сумка героя."));
        }
    }

    // Развитие: выбор каждые 3 уровня (§27.1.1) и компетенции со ступенью и
    // практикой (§27.6, каталог v1.49 §27.11).
    private void RefreshHeroCardDevelopment(string personId)
    {
        heroCardChoice.Clear();
        heroCardCompetencies.Clear();
        PersonProgressionData record = CharacterProgressionService.Get(gameState, personId);
        if (record == null)
        {
            heroCardChoice.Add(CreateHeroItemRow("Не развивается", "Уровень и компетенции есть у Командира и постоянных бойцов."));
            return;
        }

        int pending = CharacterProgressionService.PendingChoices(gameState, personId);
        if (pending > 0)
        {
            heroCardChoice.Add(CreateHeroItemRow(
                "Выбор развития" + (pending > 1 ? " (" + pending + ")" : string.Empty),
                "Кем становится человек: из пережитого или новое направление."));
            foreach (DevelopmentOption option in CharacterProgressionService.GetChoiceOptions(gameState, personId))
            {
                string optionId = option.Id;
                VisualElement row = CreateHeroItemRow(
                    option.Title,
                    (option.IsPersonal ? "Из пережитого. " : "Нейтральный вариант. ") + option.Description);
                AddHeroCardAction(row, "Выбрать", () =>
                    RunHeroCardCommand(CharacterProgressionService.TryApplyChoice(gameState, personId, optionId, out string message), message));
                heroCardChoice.Add(row);
            }
        }
        else
        {
            int nextChoiceLevel = (record.Level / CharacterProgression.ChoiceEveryLevels + 1) * CharacterProgression.ChoiceEveryLevels;
            heroCardChoice.Add(CreateHeroItemRow(
                "Следующий выбор — на уровне " + Mathf.Min(nextChoiceLevel, CharacterProgression.MaxLevel),
                "Опыт — за новое и значимое: бои, места, встречи. Повтор одного и того же почти ничему не учит; уровень сам сил не прибавляет."));
        }

        List<string> known = CharacterProgressionService.KnownCompetencies(gameState, personId);
        foreach (string competencyId in known)
        {
            int rank = CharacterProgressionService.GetCompetencyRank(gameState, personId, competencyId);
            CompetencyProgressData entry = record.FindCompetency(competencyId);
            int ceiling = entry != null ? Mathf.Max(CharacterProgression.PracticeCeiling, entry.Ceiling) : CharacterProgression.PracticeCeiling;
            string practice = rank >= CharacterProgression.MaxCompetencyRank
                ? "Высшая ступень."
                : rank >= ceiling
                    ? "Собственной практикой дальше не вырасти: нужен наставник или новое знание."
                    : "Практика: " + (entry != null ? entry.Practice : 0) + " / " +
                      CharacterProgression.PracticeToNextRank(rank) + " до ступени " + (rank + 1) + ".";
            AddHeroScreenStatRow(
                heroCardCompetencies,
                NarrativeCompetencyLabels.GetLabel(competencyId).ToUpperInvariant(),
                rank + " / " + CharacterProgression.MaxCompetencyRank,
                NarrativeCompetencyLabels.GetDescription(competencyId) + "\nРастёт от реального применения и учителей. " + practice);
        }

        if (known.Count == 0)
            AddHeroScreenHint(heroCardCompetencies, "Пока ничего не освоено: компетенции растут от применения.");
    }

    private void AddHeroCardAction(VisualElement row, string text, System.Action action)
    {
        VisualElement actions = new VisualElement();
        actions.AddToClassList("hero-item-actions");
        AddHeroItemButton(actions, text, action);
        row.Add(actions);
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

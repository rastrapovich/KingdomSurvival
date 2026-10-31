using System;
using System.Collections.Generic;

public sealed class AssembledCombatStats
{
    public bool HasTemplate;
    public UnitCombatStats Template;
    public UnitCombatStats Final;
    // «Кольчуга: +2 защита, −1 инициатива» — из чего сложилось.
    public readonly List<string> Sources = new List<string>();
    // Бриф правок (19 пунктов) п. 7: те же вклады по показателям — подсказка
    // характеристики показывает только свои источники, а надетая вещь,
    // усиливающая показатель, подсвечивает его.
    public readonly List<StatContribution> Contributions = new List<StatContribution>();

    public IEnumerable<StatContribution> For(CombatStat stat)
    {
        foreach (StatContribution contribution in Contributions)
        {
            if (contribution.Stat == stat)
                yield return contribution;
        }
    }
}

public enum CombatStat
{
    MaxHitPoints,
    Attack,
    Defense,
    Damage,
    Movement,
    Initiative,
    AttackRange
}

public struct StatContribution
{
    public CombatStat Stat;
    public string Source;
    public int Amount;
    // Вклад надетой вещи.
    public bool FromItem;
}

// ПР-08 (ТЗ §4 с правками §0.2–§0.4): один источник боевых чисел для экрана
// героя, карточек и запроса боя: шаблон UnitDatabase → качества героя →
// надетые вещи → состояния. Экран и BattleSandbox показывают один итог.
public static class CombatStatsAssembler
{
    // Рабочие числа [РАБОЧЕЕ][KINGDOM SURVIVAL], утверждены 25.09.2026.
    public const int StrengthDamageThreshold = 7;
    public const int DexterityInitiativeThreshold = 7;
    public const int FortitudeBaseline = 5;
    public const int HitPointsPerFortitude = 2;

    public static AssembledCombatStats Compute(GameState state, string personId)
    {
        AssembledCombatStats result = new AssembledCombatStats();
        ResidentState resident = PeopleService.Find(state, personId);
        string unitTypeId = resident != null ? resident.UnitTypeId : null;
        CommanderData hero = state.GetSelectedCommander();
        if (hero != null && hero.Id == personId)
            unitTypeId = string.IsNullOrWhiteSpace(hero.UnitTypeId) ? CampaignBattleBridge.HeroFallbackUnitTypeId : hero.UnitTypeId;

        IUnitStatsProvider provider = GameState.UnitStatsProvider;
        if (provider == null || string.IsNullOrEmpty(unitTypeId) ||
            !provider.TryGetCombatStats(unitTypeId, out UnitCombatStats template))
        {
            return result;
        }

        result.HasTemplate = true;
        result.Template = template;
        UnitCombatStats stats = template;

        CommanderData commander = state.GetSelectedCommander();
        if (commander != null && commander.Id == personId && commander.HeroProfile != null)
            ApplyQualities(commander.HeroProfile, ref stats, result);

        ApplyProgression(state, personId, ref stats, result);

        foreach (ItemInstanceData item in ItemService.OwnedBy(state, personId))
        {
            if (item.Slot == ItemSlot.None)
                continue;
            ItemDefinition definition = ItemCatalog.Find(item.ItemId);
            if (definition == null || definition.Modifier.IsZero)
                continue;
            Apply(ref stats, definition.Modifier);
            result.Sources.Add(definition.Name + ": " + definition.Modifier.Describe());
            AddContributions(result, definition.Name, definition.Modifier, true);
        }

        // 12Е-6: боевые особенности — условные правила, чисел не меняют.
        foreach (string line in FeatureCombatBatch.Describe(state, personId))
            result.Sources.Add(line);

        if (resident != null && resident.Exhausted)
        {
            StatModifier exhaustion = ExhaustionModifier;
            Apply(ref stats, exhaustion);
            result.Sources.Add("Изнеможение: " + exhaustion.Describe());
            AddContributions(result, "Изнеможение", exhaustion, false);
        }

        stats.MaxHitPoints = Math.Max(1, stats.MaxHitPoints);
        stats.Attack = Math.Max(0, stats.Attack);
        stats.Defense = Math.Max(0, stats.Defense);
        stats.Damage = Math.Max(1, stats.Damage);
        stats.Movement = Math.Max(1, stats.Movement);
        stats.Initiative = Math.Max(0, stats.Initiative);
        stats.AttackRange = Math.Max(1, stats.AttackRange);
        result.Final = stats;
        return result;
    }

    // Изнеможение (§5, правка §0.3).
    public static readonly StatModifier ExhaustionModifier = new StatModifier { Attack = -1, Initiative = -1 };

    private static void AddContributions(AssembledCombatStats result, string source, StatModifier modifier, bool fromItem)
    {
        void Add(CombatStat stat, int amount)
        {
            if (amount != 0)
                result.Contributions.Add(new StatContribution { Stat = stat, Source = source, Amount = amount, FromItem = fromItem });
        }
        Add(CombatStat.MaxHitPoints, modifier.MaxHitPoints);
        Add(CombatStat.Attack, modifier.Attack);
        Add(CombatStat.Defense, modifier.Defense);
        Add(CombatStat.Damage, modifier.Damage);
        Add(CombatStat.Movement, modifier.Movement);
        Add(CombatStat.Initiative, modifier.Initiative);
        Add(CombatStat.AttackRange, modifier.AttackRange);
    }

    private static void AddContribution(AssembledCombatStats result, CombatStat stat, string source, int amount)
    {
        result.Contributions.Add(new StatContribution { Stat = stat, Source = source, Amount = amount });
    }

    private static void ApplyQualities(HeroProfileData profile, ref UnitCombatStats stats, AssembledCombatStats result)
    {
        List<string> sources = result.Sources;
        int strength = profile.GetQuality(HeroQuality.Strength);
        if (strength >= StrengthDamageThreshold)
        {
            stats.Damage += 1;
            sources.Add("Сила " + strength + ": +1 урон");
            AddContribution(result, CombatStat.Damage, "Сила " + strength, 1);
        }

        int fortitude = profile.GetQuality(HeroQuality.Fortitude);
        if (fortitude > FortitudeBaseline)
        {
            int hp = (fortitude - FortitudeBaseline) * HitPointsPerFortitude;
            stats.MaxHitPoints += hp;
            sources.Add("Стойкость " + fortitude + ": +" + hp + " HP");
            AddContribution(result, CombatStat.MaxHitPoints, "Стойкость " + fortitude, hp);
        }

        int dexterity = profile.GetQuality(HeroQuality.Dexterity);
        if (dexterity >= DexterityInitiativeThreshold)
        {
            stats.Initiative += 1;
            sources.Add("Сноровка " + dexterity + ": +1 инициатива");
            AddContribution(result, CombatStat.Initiative, "Сноровка " + dexterity, 1);
        }
    }

    // Канон v1.48 §27.5: точность растёт от владения своим оружием, защита —
    // от защитной практики, здоровье — очень ограниченно (крепость тела).
    // Уровень сам по себе боевые числа не меняет.
    private static void ApplyProgression(GameState state, string personId,
        ref UnitCombatStats stats, AssembledCombatStats result)
    {
        List<string> sources = result.Sources;
        if (!CharacterProgressionService.IsProgressing(state, personId))
            return;

        // Прибавки, заданные в карте развития типа на пройденных уровнях
        // (по умолчанию их нет: канон §27.5 — уровень сам числа не раздувает).
        PersonProgressionData record = CharacterProgressionService.Get(state, personId);
        if (record != null)
        {
            StatModifier levelBonus = CharacterProgressionService.ProfileFor(state, personId).CumulativeBonus(record.Level);
            if (!levelBonus.IsZero)
            {
                Apply(ref stats, levelBonus);
                sources.Add("Уровень " + record.Level + ": " + levelBonus.Describe());
                AddContributions(result, "Уровень " + record.Level, levelBonus, false);
            }
        }

        string weapon = CharacterProgressionService.WeaponCompetencyFor(state, personId, stats.AttackRange > 1);
        int attackBonus = CompetencyBonus(CharacterProgressionService.GetCompetencyRank(state, personId, weapon));
        if (attackBonus > 0)
        {
            stats.Attack += attackBonus;
            sources.Add(NarrativeCompetencyLabels.GetLabel(weapon) + ": +" + attackBonus + " атака");
            AddContribution(result, CombatStat.Attack, NarrativeCompetencyLabels.GetLabel(weapon), attackBonus);
        }

        int defenseBonus = CompetencyBonus(CharacterProgressionService.GetCompetencyRank(state, personId, NarrativeCompetencyIds.ShieldAndLine));
        if (defenseBonus > 0)
        {
            stats.Defense += defenseBonus;
            sources.Add(NarrativeCompetencyLabels.GetLabel(NarrativeCompetencyIds.ShieldAndLine) + ": +" + defenseBonus + " защита");
            AddContribution(result, CombatStat.Defense, NarrativeCompetencyLabels.GetLabel(NarrativeCompetencyIds.ShieldAndLine), defenseBonus);
        }

        int hitPoints = CharacterProgressionService.BonusMaxHitPoints(state, personId);
        if (hitPoints > 0)
        {
            stats.MaxHitPoints += hitPoints;
            sources.Add("Крепость тела: +" + hitPoints + " HP");
            AddContribution(result, CombatStat.MaxHitPoints, "Крепость тела", hitPoints);
        }
    }

    private static int CompetencyBonus(int rank)
    {
        if (rank >= CharacterProgression.CombatBonusSecondRank)
            return 2;
        return rank >= CharacterProgression.CombatBonusFirstRank ? 1 : 0;
    }

    private static void Apply(ref UnitCombatStats stats, StatModifier modifier)
    {
        stats.MaxHitPoints += modifier.MaxHitPoints;
        stats.Attack += modifier.Attack;
        stats.Defense += modifier.Defense;
        stats.Damage += modifier.Damage;
        stats.Movement += modifier.Movement;
        stats.Initiative += modifier.Initiative;
        stats.AttackRange += modifier.AttackRange;
    }
}

using System;
using System.Collections.Generic;
using System.Linq;

public sealed class BattleExperienceShare
{
    public string PersonId;
    public int Participation;
    public int Contribution;
    public int Total => Participation + Contribution;
}

// Канон v1.48 §27.2–27.4: у значимого боя один банк опыта — сумма «цены»
// противников. Размер отряда его не увеличивает: 60% делятся поровну между
// участниками, 40% — пропорционально реальному вкладу. Гарантированной доли
// Командира и награды за последний удар нет. Отдельно от общего опыта
// действия в бою дают практику владения оружием и щитом.
public static class BattleExperience
{
    public const string SourcePrefix = "battle.";

    public static int ComputeBank(IEnumerable<CampaignBattleEnemyRecord> enemies)
    {
        int bank = 0;
        foreach (CampaignBattleEnemyRecord enemy in enemies ?? Enumerable.Empty<CampaignBattleEnemyRecord>())
        {
            if (enemy != null)
                bank += CharacterProgression.EnemyExperience(enemy.MaxHitPoints, enemy.Attack, enemy.Defense, enemy.Damage);
        }
        return bank;
    }

    // Один и тот же состав противников — «тот же бой» для антифарма.
    public static string RepeatKey(IEnumerable<CampaignBattleEnemyRecord> enemies)
    {
        IEnumerable<string> ids = (enemies ?? Enumerable.Empty<CampaignBattleEnemyRecord>())
            .Where(enemy => enemy != null)
            .Select(enemy => enemy.UnitTypeId ?? string.Empty)
            .OrderBy(id => id, StringComparer.Ordinal);
        return "battle:" + string.Join("+", ids);
    }

    // Делит банк ровно без потерь: остаток от округления уходит тем, у кого
    // больше дробная часть (при равенстве — по порядку участников).
    public static List<BattleExperienceShare> Distribute(int bank, IList<string> participants, IList<int> contributionScores)
    {
        List<BattleExperienceShare> shares = new List<BattleExperienceShare>();
        if (participants == null || participants.Count == 0)
            return shares;
        foreach (string personId in participants)
            shares.Add(new BattleExperienceShare { PersonId = personId });
        if (bank <= 0)
            return shares;

        int participationPool = bank * CharacterProgression.ParticipationPercent / 100;
        int contributionPool = bank - participationPool;

        int[] equal = SplitProportionally(participationPool, Enumerable.Repeat(1, shares.Count).ToList());
        for (int i = 0; i < shares.Count; i++)
            shares[i].Participation = equal[i];

        List<int> scores = new List<int>();
        for (int i = 0; i < shares.Count; i++)
            scores.Add(contributionScores != null && i < contributionScores.Count ? Math.Max(0, contributionScores[i]) : 0);
        // Никто ничего не сделал — вклад делится поровну, банк не пропадает.
        if (scores.Sum() == 0)
            scores = Enumerable.Repeat(1, shares.Count).ToList();

        int[] contribution = SplitProportionally(contributionPool, scores);
        for (int i = 0; i < shares.Count; i++)
            shares[i].Contribution = contribution[i];
        return shares;
    }

    private static int[] SplitProportionally(int pool, IList<int> weights)
    {
        int[] result = new int[weights.Count];
        long total = weights.Sum(weight => (long)weight);
        if (pool <= 0 || total <= 0)
            return result;

        long[] remainders = new long[weights.Count];
        int given = 0;
        for (int i = 0; i < weights.Count; i++)
        {
            long exact = (long)pool * weights[i];
            result[i] = (int)(exact / total);
            remainders[i] = exact % total;
            given += result[i];
        }

        List<int> order = Enumerable.Range(0, weights.Count)
            .OrderByDescending(i => remainders[i])
            .ThenBy(i => i)
            .ToList();
        for (int k = 0; given < pool; k++, given++)
            result[order[k % order.Count]]++;
        return result;
    }

    // Применяется из CampaignBattleBridge.ApplyResult (уже один раз на бой);
    // источник опыта «battle.<id>» дополнительно защищает от повторов.
    public static List<string> Apply(GameState state, CampaignBattleResult result)
    {
        List<string> lines = new List<string>();
        if (state == null || result == null || string.IsNullOrWhiteSpace(result.BattleId))
            return lines;
        CharacterProgressionService.EnsureState(state);

        List<CampaignBattleContribution> contributions = BuildContributions(result);
        if (contributions.Count == 0)
            return lines;

        List<CampaignBattleEnemyRecord> enemies = result.Enemies ?? new List<CampaignBattleEnemyRecord>();
        string repeatKey = RepeatKey(enemies);
        int rawBank = ComputeBank(enemies);
        int percent = 0;
        if (rawBank > 0)
        {
            percent = CharacterProgression.RepeatBattlePercent(CharacterProgressionService.RepeatCount(state, repeatKey));
            CharacterProgressionService.IncrementRepeat(state, repeatKey);
            if (result.Outcome == CampaignBattleOutcome.Retreat)
                percent = percent * CharacterProgression.RetreatBankPercent / 100;
        }
        int bank = rawBank * percent / 100;

        List<BattleExperienceShare> shares = Distribute(
            bank,
            contributions.Select(contribution => contribution.PersonId).ToList(),
            contributions.Select(contribution => contribution.DamageDealt + contribution.DamagePrevented).ToList());

        string sourceId = SourcePrefix + result.BattleId;
        List<string> parts = new List<string>();
        List<ExperienceGain> gains = new List<ExperienceGain>();
        foreach (BattleExperienceShare share in shares)
        {
            ExperienceGain gain = CharacterProgressionService.AwardExperience(state, share.PersonId, sourceId, share.Total);
            if (gain == null)
                continue;
            gains.Add(gain);
            parts.Add(gain.DisplayName + " +" + share.Total + " (участие +" + share.Participation + ", вклад +" + share.Contribution + ")");
        }

        if (parts.Count > 0)
        {
            string reason = result.Outcome == CampaignBattleOutcome.Retreat ? " — отход, опыта вдвое меньше"
                : percent < 100 ? " — противник уже знаком, опыта меньше" : string.Empty;
            lines.Add("Опыт боя" + reason + ": " + string.Join(", ", parts) + ".");
        }
        lines.AddRange(CharacterProgressionService.DescribeLevelUps(gains));

        List<PracticeGain> practice = new List<PracticeGain>();
        foreach (CampaignBattleContribution contribution in contributions)
        {
            ResidentState resident = HomePeopleService.Find(state, contribution.PersonId);
            if (resident != null && !resident.IsAlive)
                continue;
            string unitTypeId = UnitTypeOf(state, contribution.PersonId);
            if (contribution.DamageDealt > 0 && contribution.UsedRangedAttack)
                practice.Add(CharacterProgressionService.AddPractice(state, contribution.PersonId,
                    CharacterProgressionService.WeaponCompetencyFor(unitTypeId, true), CharacterProgression.PracticePerUse, repeatKey));
            if (contribution.DamageDealt > 0 && (contribution.UsedMeleeAttack || !contribution.UsedRangedAttack))
                practice.Add(CharacterProgressionService.AddPractice(state, contribution.PersonId,
                    CharacterProgressionService.WeaponCompetencyFor(unitTypeId, false), CharacterProgression.PracticePerUse, repeatKey));
            if (contribution.DamagePrevented > 0)
                practice.Add(CharacterProgressionService.AddPractice(state, contribution.PersonId,
                    NarrativeCompetencyIds.ShieldAndLine, CharacterProgression.PracticePerUse, repeatKey));
        }
        lines.AddRange(CharacterProgressionService.DescribePractice(practice));
        return lines;
    }

    // Итог без подробного вклада (старые/тестовые бои) — все выжившие и
    // павшие участвовали, вклад неизвестен.
    private static List<CampaignBattleContribution> BuildContributions(CampaignBattleResult result)
    {
        List<CampaignBattleContribution> contributions = new List<CampaignBattleContribution>();
        HashSet<string> seen = new HashSet<string>();
        if (result.Contributions != null && result.Contributions.Count > 0)
        {
            foreach (CampaignBattleContribution contribution in result.Contributions)
            {
                if (contribution != null && !string.IsNullOrEmpty(contribution.PersonId) && seen.Add(contribution.PersonId))
                    contributions.Add(contribution);
            }
            return contributions;
        }

        foreach (CampaignBattleSurvivor survivor in result.Survivors ?? new List<CampaignBattleSurvivor>())
        {
            if (survivor != null && !string.IsNullOrEmpty(survivor.PersonId) && seen.Add(survivor.PersonId))
                contributions.Add(new CampaignBattleContribution { PersonId = survivor.PersonId });
        }
        foreach (string personId in result.FallenPersonIds ?? new List<string>())
        {
            if (!string.IsNullOrEmpty(personId) && seen.Add(personId))
                contributions.Add(new CampaignBattleContribution { PersonId = personId });
        }
        return contributions;
    }

    private static string UnitTypeOf(GameState state, string personId)
    {
        CommanderData hero = state.GetSelectedCommander();
        if (hero != null && hero.Id == personId)
            return string.IsNullOrWhiteSpace(hero.UnitTypeId) ? CampaignBattleBridge.HeroFallbackUnitTypeId : hero.UnitTypeId;
        ResidentState resident = HomePeopleService.Find(state, personId);
        return resident != null ? resident.UnitTypeId : null;
    }
}

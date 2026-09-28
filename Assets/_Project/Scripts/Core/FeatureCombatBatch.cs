using System;
using System.Collections.Generic;

// 12Е-6: первая партия боевых особенностей (каталог §6.1). Правила внутри
// боя исполняет BattleSandbox (SandboxPerks) — кампания передаёт ему ID
// правил участника; то, что решается итогом боя (тяжёлая рана, отход,
// подготовленное начало), решается здесь. Всё [РАБОЧЕЕ].

// Те же строки, что SandboxPerks в BattleSandbox (ядро от боя не зависит).
public static class CombatPerkIds
{
    public const string BasicDefense = "perk.basic_defense";
    public const string Counterstrike = "perk.counterstrike";
    public const string CounterstrikeDamage = "perk.counterstrike.damage";
    public const string FirstStrike = "perk.first_strike";
    public const string ColdEye = "perk.cold_eye";
    public const string StillStanding = "perk.still_standing";
}

public static class CombatFeatureIds
{
    public const string BasicDefense = "osnovnaya_zashchita";
    public const string Stubborn = "upryamets";
    public const string StillStanding = "eshchyo_na_nogakh";
    public const string Counterstrike = "kontrudar";
    public const string FirstStrike = "uprezhdayushchiy_udar";
    public const string ColdEye = "kholodnyy_glaz";
    public const string Ambush = "zasada";
    public const string LeavesNoOne = "ne_brosaet_svoikh";
}

public static class FeatureCombatBatch
{
    // «Упрямец»: тяжёлая рана по итогам боя — при HP ≤ 1/8 вместо 1/4.
    public const int StubbornHeavyWoundDivisor = 8;
    // «Засада»: +1 Инициатива всему отряду в первом раунде.
    public const int AmbushInitiativeBonus = 1;

    public static void RegisterAll()
    {
        foreach (string id in new[]
                 {
                     CombatFeatureIds.BasicDefense, CombatFeatureIds.Stubborn, CombatFeatureIds.StillStanding,
                     CombatFeatureIds.Counterstrike, CombatFeatureIds.FirstStrike, CombatFeatureIds.ColdEye,
                     CombatFeatureIds.Ambush, CombatFeatureIds.LeavesNoOne
                 })
        {
            ProgressionFeatureImplementations.Register(id);
        }
        // Реализованные ранги: «Основная защита» и «Холодный глаз» — I
        // («Ищет щель» ждёт действия «Прицелиться»); «Контрудар» — I–II.
        ProgressionFeatureImplementations.SetImplementedRanks(CombatFeatureIds.BasicDefense, 1);
        ProgressionFeatureImplementations.SetImplementedRanks(CombatFeatureIds.ColdEye, 1);
        ProgressionFeatureImplementations.SetImplementedRanks(CombatFeatureIds.Counterstrike, 2);
    }

    // Боевые правила участника для BattleSandbox.
    public static List<string> PerksFor(GameState state, string personId)
    {
        List<string> perks = new List<string>();
        if (CharacterFeatureService.Has(state, personId, CombatFeatureIds.BasicDefense))
            perks.Add(CombatPerkIds.BasicDefense);
        int counterstrike = CharacterFeatureService.GetRank(state, personId, CombatFeatureIds.Counterstrike);
        if (counterstrike >= 1)
            perks.Add(CombatPerkIds.Counterstrike);
        if (counterstrike >= 2)
            perks.Add(CombatPerkIds.CounterstrikeDamage);
        if (CharacterFeatureService.Has(state, personId, CombatFeatureIds.FirstStrike))
            perks.Add(CombatPerkIds.FirstStrike);
        if (CharacterFeatureService.Has(state, personId, CombatFeatureIds.ColdEye))
            perks.Add(CombatPerkIds.ColdEye);
        if (CharacterFeatureService.Has(state, personId, CombatFeatureIds.StillStanding))
            perks.Add(CombatPerkIds.StillStanding);
        return perks;
    }

    // Строки «из чего сложилось» для карточки человека: условные правила,
    // которые не меняют постоянные числа.
    public static List<string> Describe(GameState state, string personId)
    {
        List<string> lines = new List<string>();
        if (CharacterFeatureService.Has(state, personId, CombatFeatureIds.BasicDefense))
            lines.Add("Основная защита: первая атака по нему за раунд −1 Атака");
        int counterstrike = CharacterFeatureService.GetRank(state, personId, CombatFeatureIds.Counterstrike);
        if (counterstrike >= 1)
            lines.Add("Контрудар: два ответных удара за раунд" + (counterstrike >= 2 ? ", первый +1 Урон" : string.Empty));
        if (CharacterFeatureService.Has(state, personId, CombatFeatureIds.FirstStrike))
            lines.Add("Упреждающий удар: в защитной стойке отвечает на ближнюю атаку первым");
        if (CharacterFeatureService.Has(state, personId, CombatFeatureIds.ColdEye))
            lines.Add("Холодный глаз: не двигаясь в этот ход, стреляет с −1 к Защите цели");
        if (CharacterFeatureService.Has(state, personId, CombatFeatureIds.StillStanding))
            lines.Add("Ещё на ногах: раз за бой остаётся с 1 здоровья вместо падения (после боя — тяжёлая рана)");
        if (CharacterFeatureService.Has(state, personId, CombatFeatureIds.Stubborn))
            lines.Add("Упрямец: тяжёлая рана после боя — только при здоровье ≤ 1/8");
        if (CharacterFeatureService.Has(state, personId, CombatFeatureIds.Ambush))
            lines.Add("Засада: бой, начатый подготовленно, — отряд +1 Инициатива в первом раунде");
        if (CharacterFeatureService.Has(state, personId, CombatFeatureIds.LeavesNoOne))
            lines.Add("Не бросает своих: раз за поход при отходе выносит павшего бойца живым");
        return lines;
    }

    // Делитель порога тяжёлой раны: 4, у «Упрямца» — 8.
    public static int HeavyWoundDivisor(GameState state, string personId)
    {
        return CharacterFeatureService.Has(state, personId, CombatFeatureIds.Stubborn)
            ? StubbornHeavyWoundDivisor
            : CampaignBattleBridge.HeavyWoundHitPointsDivisor;
    }

    // «Засада»: подготовленное начало боя и особенность у Командира — отряд
    // получает прибавку к инициативе в первом раунде.
    public static void ApplyPreparedStart(GameState state, CampaignBattleRequest request)
    {
        if (state == null || request == null || !request.PreparedStart)
            return;
        CommanderData commander = state.GetSelectedCommander();
        if (commander == null || !CharacterFeatureService.Has(state, commander.Id, CombatFeatureIds.Ambush))
            return;
        request.PlayerFirstRoundInitiativeBonus = AmbushInitiativeBonus;
        request.Notes.Add(commander.Name + " («Засада»): отряд готов — +" + AmbushInitiativeBonus + " к инициативе в первом раунде.");
    }

    // «Ещё на ногах»: бой отметил, кто устоял на 1 здоровья, — строка итога
    // и запись срабатывания. Вызывается до «Не бросает своих», пока в
    // ForcedHeavyWoundIds только устоявшие.
    public static void RecordStillStanding(GameState state, CampaignBattleResult result, List<string> notes)
    {
        if (state == null || result?.ForcedHeavyWoundIds == null)
            return;
        foreach (string personId in result.ForcedHeavyWoundIds)
        {
            string line = CharacterProgressionService.DisplayName(state, personId) +
                          " («Ещё на ногах») устоял под смертельным ударом — после боя тяжело ранен.";
            notes?.Add(line);
            FeatureDispatcher.Record(state, new FeatureActivation
            {
                PersonId = personId,
                FeatureId = CombatFeatureIds.StillStanding,
                Trigger = FeatureTrigger.BattleEnded,
                Text = line
            });
        }
    }

    // «Не бросает своих»: при отходе 1/поход один выведенный из строя боец не
    // погибает, а выносится живым с тяжёлой раной.
    public static void CarryFallenOnRetreat(GameState state, CampaignBattleResult result, List<string> notes)
    {
        if (state == null || result == null || result.Outcome != CampaignBattleOutcome.Retreat ||
            result.FallenPersonIds == null || result.FallenPersonIds.Count == 0)
            return;
        CommanderData commander = state.GetSelectedCommander();
        if (commander == null || !CharacterFeatureService.Has(state, commander.Id, CombatFeatureIds.LeavesNoOne))
            return;
        string saved = result.FallenPersonIds.Find(personId => personId != commander.Id);
        if (saved == null || !FeatureLimits.TryConsume(state, commander.Id, CombatFeatureIds.LeavesNoOne))
            return;

        result.FallenPersonIds.Remove(saved);
        result.Survivors.Add(new CampaignBattleSurvivor { PersonId = saved, HitPoints = 1 });
        if (!result.ForcedHeavyWoundIds.Contains(saved))
            result.ForcedHeavyWoundIds.Add(saved);
        string line = commander.Name + " («Не бросает своих») вынес " + CharacterProgressionService.DisplayName(state, saved) +
                      " из боя живым — тяжело ранен.";
        notes?.Add(line);
        FeatureDispatcher.Record(state, new FeatureActivation
        {
            PersonId = commander.Id,
            FeatureId = CombatFeatureIds.LeavesNoOne,
            Trigger = FeatureTrigger.BattleEnded,
            Text = line
        });
    }
}

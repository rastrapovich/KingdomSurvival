using System;
using System.Collections.Generic;

public static class BuildingBattleIntegration
{
    public static string ApplyCapitalDefenseToPendingBattle(GameState state)
    {
        if (state == null)
            return string.Empty;

        PendingBattleData pending = BattleSystem.GetPendingBattle(state);
        if (pending == null || pending.Context == null ||
            pending.Context.Kind != BattleKind.CapitalDefense)
        {
            return string.Empty;
        }

        if (!string.IsNullOrEmpty(pending.Context.Description) &&
            pending.Context.Description.Contains("Оборона построек:"))
        {
            return string.Empty;
        }

        int rating = BuildingSystem.GetCapitalDefenseRating(state);
        if (rating <= 0)
            return string.Empty;

        int requestedAttackReduction = Math.Max(
            1,
            (int)Math.Round(
                rating / 3.0,
                MidpointRounding.AwayFromZero));

        int appliedAttackReduction = ReduceEnemyAttack(
            pending.Context.Enemies,
            requestedAttackReduction);

        if (appliedAttackReduction <= 0)
            return string.Empty;

        // EnemyPower остался в текущем BattleContext только как совместимый
        // fallback. Уменьшаем и его, но реальный resolver использует Enemies.
        pending.Context.EnemyPower = Math.Max(
            0,
            pending.Context.EnemyPower - appliedAttackReduction);

        pending.Context.Description +=
            " Оборона построек: рейтинг " + rating +
            ", эффективная атака нападающих снижена на " +
            appliedAttackReduction + ".";

        pending.Result = BattleSystem.SelectPendingDoctrine(
            state,
            pending.Context.Doctrine);

        return
            "Постройки столицы усилили оборону: рейтинг " + rating +
            ", эффективная атака нападающих снижена на " +
            appliedAttackReduction + ".";
    }

    private static int ReduceEnemyAttack(
        List<BattleEnemyUnit> enemies,
        int requestedReduction)
    {
        if (enemies == null || requestedReduction <= 0)
            return 0;

        int applied = 0;

        while (applied < requestedReduction)
        {
            BattleEnemyUnit strongest = null;

            foreach (BattleEnemyUnit enemy in enemies)
            {
                if (enemy == null || enemy.AttackPower <= 1)
                    continue;

                if (strongest == null ||
                    enemy.AttackPower > strongest.AttackPower)
                {
                    strongest = enemy;
                }
            }

            if (strongest == null)
                break;

            strongest.AttackPower--;
            applied++;
        }

        return applied;
    }
}

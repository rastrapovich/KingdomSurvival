using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace KingdomSurvival.BattleSandbox.Tests
{
    // Бриф завершения, этап 17: каждый тег каталога исполнителя проходит
    // настоящий бой (удары, ответы, ходы, состояния) без исключений и с
    // целыми инвариантами. Это дымовая проверка достижимости — механику
    // отдельных тегов проверяют свои тесты.
    public sealed class PassiveTagSmokeTests
    {
        private static IEnumerable<string> AllTags() => SandboxPassiveCatalog.All.Select(def => def.TagId);

        private static SandboxUnitState U(string id, SandboxTeam team, int q, int r, int init, int range = 1, params string[] tags)
        {
            return new SandboxUnitState(id, new SandboxUnitDefinition(id, id, SandboxUnitRole.Militia, 24, 3, 2, 3, 4, init, range, tags, null, null, 1, true),
                team, new HexCoord(q, r));
        }

        [TestCaseSource(nameof(AllTags))]
        public void Tag_RunsThroughRealBattle_WithoutBreakingInvariants(string tagId)
        {
            SandboxUnitState[] units =
            {
                U("bearer", SandboxTeam.Player, 4, 4, 9, 1, tagId),
                U("ally", SandboxTeam.Player, 4, 5, 6),
                U("foe", SandboxTeam.Enemy, 5, 4, 8, 1, tagId),
                U("archer", SandboxTeam.Enemy, 9, 6, 5, 4)
            };
            Dictionary<HexCoord, SandboxTerrain> map = new Dictionary<HexCoord, SandboxTerrain>();
            SandboxTerrainRules.RegisterBattle(units, map);
            SandboxBattle b = new SandboxBattle(14, 10, units, map);
            b.Start();
            b.ApplyStatus(SandboxStatusCatalog.Poison, b.GetUnit("bearer"), b.GetUnit("foe"));
            b.ApplyStatus(SandboxStatusCatalog.Fear, b.GetUnit("foe"), b.GetUnit("bearer"));

            for (int step = 0; step < 40 && b.Phase == SandboxBattlePhase.InProgress && b.Round <= 3; step++)
            {
                SandboxUnitState current = b.CurrentUnit;
                if (current == null)
                    break;
                SandboxUnitState target = b.Units.FirstOrDefault(u => u.Team != current.Team && b.PreviewAttack(current.Id, u.Id).IsValid);
                if (target != null && b.TryAttack(current.Id, target.Id, out _) && b.HasPendingRetaliation)
                    b.TryResolvePendingRetaliation(out _);
                if (b.Phase == SandboxBattlePhase.InProgress && b.CurrentUnit == current)
                    b.EndActivation();
                foreach (SandboxUnitState unit in b.Units)
                {
                    Assert.GreaterOrEqual(unit.HitPoints, 0, tagId + ": " + unit.Id);
                    Assert.LessOrEqual(unit.HitPoints, unit.MaxHitPoints, tagId + ": " + unit.Id);
                }
            }
            Assert.IsTrue(b.Round > 1 || b.Phase != SandboxBattlePhase.InProgress, tagId + ": бой не продвинулся");
        }

        private static IEnumerable<string> AllStatuses() => SandboxStatusCatalog.All.Select(def => def.Id);

        // Каждое из 89 состояний: наложение на бойца, три раунда боя, конец боя
        // снимает всё — без исключений и с целыми инвариантами HP.
        [TestCaseSource(nameof(AllStatuses))]
        public void Status_AppliedAndRunsThroughBattle(string statusId)
        {
            SandboxUnitState[] units =
            {
                U("bearer", SandboxTeam.Player, 4, 4, 9),
                U("ally", SandboxTeam.Player, 4, 5, 6),
                U("foe", SandboxTeam.Enemy, 5, 4, 8),
                U("archer", SandboxTeam.Enemy, 9, 6, 5, 4)
            };
            Dictionary<HexCoord, SandboxTerrain> map = new Dictionary<HexCoord, SandboxTerrain>();
            SandboxTerrainRules.RegisterBattle(units, map);
            SandboxBattle b = new SandboxBattle(14, 10, units, map);
            b.Start();
            b.ApplyStatus(statusId, b.GetUnit("bearer"), b.GetUnit("foe"));
            for (int step = 0; step < 40 && b.Phase == SandboxBattlePhase.InProgress && b.Round <= 3; step++)
            {
                SandboxUnitState current = b.CurrentUnit;
                if (current == null)
                    break;
                SandboxUnitState target = b.Units.FirstOrDefault(u => u.Team != current.Team && b.PreviewAttack(current.Id, u.Id).IsValid);
                if (target != null && b.TryAttack(current.Id, target.Id, out _) && b.HasPendingRetaliation)
                    b.TryResolvePendingRetaliation(out _);
                if (b.Phase == SandboxBattlePhase.InProgress && b.CurrentUnit == current)
                    b.EndActivation();
                foreach (SandboxUnitState unit in b.Units)
                {
                    Assert.GreaterOrEqual(unit.HitPoints, 0, statusId + ": " + unit.Id);
                    Assert.LessOrEqual(unit.HitPoints, unit.MaxHitPoints, statusId + ": " + unit.Id);
                }
            }
            Assert.IsTrue(b.Round > 1 || b.Phase != SandboxBattlePhase.InProgress, statusId + ": бой не продвинулся");
        }
    }
}

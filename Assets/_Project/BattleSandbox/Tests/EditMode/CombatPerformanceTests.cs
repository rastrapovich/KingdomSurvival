using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using NUnit.Framework;

namespace KingdomSurvival.BattleSandbox.Tests
{
    // Бриф завершения, этап 18: замер логики боя на большом составе — 13
    // участников по 12 состояний, все теги-носители. Пороги щедрые (защита от
    // явного регресса), фактические числа — в выводе теста.
    public sealed class CombatPerformanceTests
    {
        private static SandboxUnitState U(string id, SandboxTeam team, int q, int r, int init, params string[] tags)
        {
            return new SandboxUnitState(id, new SandboxUnitDefinition(id, id, SandboxUnitRole.Militia, 30, 3, 2, 3, 4, init, 1, tags, null, null, 2, true),
                team, new HexCoord(q, r));
        }

        [Test]
        public void ThirteenFighters_TwelveStatuses_PreviewAiAndRoundTimings()
        {
            string[] tagPool = SandboxPassiveCatalog.All.Select(def => def.TagId).ToArray();
            List<SandboxUnitState> units = new List<SandboxUnitState>();
            for (int i = 0; i < 13; i++)
            {
                SandboxTeam team = i < 6 ? SandboxTeam.Player : SandboxTeam.Enemy;
                string[] tags = Enumerable.Range(0, 4).Select(k => tagPool[(i * 7 + k * 31) % tagPool.Length]).Distinct().ToArray();
                units.Add(U("u" + i, team, team == SandboxTeam.Player ? 3 + i % 3 : 8 + i % 3, 1 + i % 7, 20 - i, tags));
            }
            Dictionary<HexCoord, SandboxTerrain> map = new Dictionary<HexCoord, SandboxTerrain>();
            SandboxTerrainRules.RegisterBattle(units, map);
            SandboxBattle b = new SandboxBattle(14, 10, units, map);
            b.Start();
            List<string> pool = SandboxStatusCatalog.All.Where(def => !def.StrongControl).Select(def => def.Id).ToList();
            int applied = 0;
            foreach (SandboxUnitState unit in b.Units)
            {
                SandboxUnitState source = b.Units.First(other => other.Team != unit.Team);
                applied += pool.Where(id => b.ApplyStatus(id, unit, source).Applied).Take(12).Count();
            }

            SandboxUnitState current = b.CurrentUnit;
            SandboxUnitState target = b.Units.First(u => u.Team != current.Team);
            Stopwatch watch = Stopwatch.StartNew();
            for (int i = 0; i < 500; i++)
                b.PreviewReachableAttack(current.Id, target.Id);
            double previewMs = watch.Elapsed.TotalMilliseconds / 500;

            watch.Restart();
            for (int i = 0; i < 20; i++)
                SandboxAiPlanner.Plan(b, current, true);
            double aiMs = watch.Elapsed.TotalMilliseconds / 20;

            watch.Restart();
            int activations = 0;
            for (int step = 0; step < 60 && b.Phase == SandboxBattlePhase.InProgress && b.Round <= 3; step++)
            {
                SandboxUnitState unit = b.CurrentUnit;
                if (unit == null)
                    break;
                SandboxUnitState foe = b.Units.FirstOrDefault(u => u.Team != unit.Team && b.PreviewAttack(unit.Id, u.Id).IsValid);
                if (foe != null && b.TryAttack(unit.Id, foe.Id, out _) && b.HasPendingRetaliation)
                    b.TryResolvePendingRetaliation(out _);
                if (b.Phase == SandboxBattlePhase.InProgress && b.CurrentUnit == unit)
                    b.EndActivation();
                activations++;
            }
            double activationMs = watch.Elapsed.TotalMilliseconds / System.Math.Max(1, activations);

            TestContext.WriteLine("[PERF][Combat] участников 13, состояний " + applied + ", предпросмотр " + previewMs.ToString("0.000") +
                                  " мс, решение ИИ " + aiMs.ToString("0.00") + " мс, активация " + activationMs.ToString("0.00") + " мс (" + activations + " шт.)");
            Assert.Greater(applied, 13 * 6);
            Assert.Less(previewMs, 20.0, "предпросмотр удара");
            Assert.Less(aiMs, 500.0, "решение ИИ");
            Assert.Less(activationMs, 200.0, "активация с состояниями и тегами");
        }
    }
}

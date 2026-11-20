using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace KingdomSurvival.BattleSandbox.Tests
{
    // Пакет состояний, этап 9: предпросмотр совпадает с фактом и ничего не
    // расходует; ИИ использует тот же запрос и умеет очищать и завершать ход.
    public sealed class StatusPreviewAiTests
    {
        private static SandboxBattle Build(params (string Id, SandboxTeam Team, int Q, int R, int Init, string[] Tags)[] specs)
        {
            List<SandboxUnitState> units = specs.Select(spec => new SandboxUnitState(
                spec.Id,
                new SandboxUnitDefinition(spec.Id, spec.Id, SandboxUnitRole.Militia, 20, 2, 2, 3, 4, spec.Init, 1, spec.Tags),
                spec.Team, new HexCoord(spec.Q, spec.R))).ToList();
            Dictionary<HexCoord, SandboxTerrain> map = new Dictionary<HexCoord, SandboxTerrain>();
            SandboxTerrainRules.RegisterBattle(units, map);
            SandboxBattle battle = new SandboxBattle(14, 10, units, map);
            battle.Start();
            return battle;
        }

        private static (string, SandboxTeam, int, int, int, string[]) U(string id, SandboxTeam team, int q, int r, int init, params string[] tags) =>
            (id, team, q, r, init, tags);

        [Test]
        public void Shield_PreviewShowsZeroHpDamage_DoesNotConsume_MatchesFact()
        {
            SandboxBattle battle = Build(U("foe", SandboxTeam.Enemy, 3, 2, 9), U("hero", SandboxTeam.Player, 2, 2, 5));
            SandboxUnitState hero = battle.GetUnit("hero");
            battle.ApplyStatus("status.132", hero, hero);
            SandboxAttackPreview preview = battle.PreviewAttack("foe", "hero");
            Assert.AreEqual(3, preview.Damage, "прямой урон показан отдельно");
            Assert.AreEqual(0, preview.HpDamage);
            Assert.AreEqual(20, preview.TargetHitPointsAfter);
            StringAssert.Contains("поглотит", preview.Details);
            Assert.IsTrue(hero.HasStatus("status.132"), "предпросмотр не расходует щит");
            battle.TryAttack("foe", "hero", out _);
            Assert.AreEqual(preview.TargetHitPointsAfter, hero.HitPoints, "прогноз совпал с фактом");
        }

        [Test]
        public void TemporaryHp_AndSharedWounds_PredictedLikeFact()
        {
            SandboxBattle battle = Build(U("foe", SandboxTeam.Enemy, 3, 2, 9), U("hero", SandboxTeam.Player, 2, 2, 5), U("ally", SandboxTeam.Player, 1, 2, 4));
            SandboxUnitState hero = battle.GetUnit("hero");
            battle.ApplyStatus(SandboxStatusCatalog.SharedWounds, hero, battle.GetUnit("ally"));
            SandboxAttackPreview preview = battle.PreviewAttack("foe", "hero");
            Assert.AreEqual(2, preview.HpDamage, "3 → 2 цели + 1 партнёру");
            battle.TryAttack("foe", "hero", out _);
            Assert.AreEqual(preview.TargetHitPointsAfter, hero.HitPoints);
        }

        [Test]
        public void PassiveStatus_ShownInPreview_WithImmunityReason()
        {
            SandboxBattle battle = Build(U("shipovik", SandboxTeam.Enemy, 3, 2, 9, "passive.20261119.052"),
                U("hero", SandboxTeam.Player, 2, 2, 5), U("golem", SandboxTeam.Player, 4, 2, 4, "resist.biological_poison"));
            SandboxAttackPreview normal = battle.PreviewAttack("shipovik", "hero");
            Assert.IsTrue(normal.StatusNotes.Any(note => note.Contains("Отравление") && note.Contains("1×2") && !note.Contains("не пройдёт")), normal.Details);
            SandboxAttackPreview immune = battle.PreviewAttack("shipovik", "golem");
            Assert.IsTrue(immune.StatusNotes.Any(note => note.Contains("не пройдёт") && note.Contains("Иммунитет к яду")), immune.Details);
        }

        [Test]
        public void UpcomingTicks_ShownSeparately_NotAddedToDirectDamage()
        {
            SandboxBattle battle = Build(U("foe", SandboxTeam.Enemy, 3, 2, 9), U("hero", SandboxTeam.Player, 2, 2, 5));
            SandboxUnitState hero = battle.GetUnit("hero");
            battle.ApplyStatus(SandboxStatusCatalog.Poison, hero, battle.GetUnit("foe"));
            SandboxAttackPreview preview = battle.PreviewAttack("foe", "hero");
            Assert.AreEqual(3, preview.Damage);
            Assert.AreEqual(1, preview.UpcomingTicks);
            Assert.AreEqual(17, preview.TargetHitPointsAfter, "тик не входит в прогноз удара");
        }

        [Test]
        public void Ai_CleansesStunnedAlly_InsteadOfIdling()
        {
            SandboxBattle battle = Build(U("medic", SandboxTeam.Enemy, 3, 2, 9), U("brute", SandboxTeam.Enemy, 4, 2, 1), U("hero", SandboxTeam.Player, 12, 8, 5));
            battle.ApplyStatus(SandboxStatusCatalog.Stunned, battle.GetUnit("brute"), battle.GetUnit("hero"));
            SandboxAiDecision decision = SandboxAiPlanner.Plan(battle, battle.GetUnit("medic"), true);
            Assert.AreEqual(SandboxAiActionKind.Context, decision.Action, decision.Reason);
            Assert.AreEqual(SandboxContextActionKind.CleanseStatus, decision.Context.Kind);
            SandboxAiExecutor.TakeTurn(battle);
            Assert.IsFalse(battle.GetUnit("brute").HasStatus(SandboxStatusCatalog.Stunned));
        }

        [Test]
        public void Ai_UnderNausea_CannotAttack_StillEndsTurn()
        {
            SandboxBattle battle = Build(U("foe", SandboxTeam.Enemy, 3, 2, 9), U("hero", SandboxTeam.Player, 2, 2, 5));
            SandboxUnitState foe = battle.GetUnit("foe");
            battle.ApplyStatus("status.079", foe, battle.GetUnit("hero"));
            Assert.IsFalse(battle.PreviewAttack("foe", "hero").IsValid);
            List<string> events = SandboxAiExecutor.TakeTurn(battle);
            Assert.AreNotEqual("foe", battle.CurrentUnit?.Id, "ход завершён: " + string.Join(" | ", events));
            Assert.AreEqual(20, battle.GetUnit("hero").HitPoints, "запрещённая атака не выполнена");
        }

        [Test]
        public void Ai_Provoked_AttacksProvoker()
        {
            SandboxBattle battle = Build(U("foe", SandboxTeam.Enemy, 3, 2, 9), U("weak", SandboxTeam.Player, 2, 2, 5), U("taunt", SandboxTeam.Player, 4, 2, 4));
            battle.GetUnit("weak").HitPoints = 3;
            battle.ApplyStatus("status.065", battle.GetUnit("foe"), battle.GetUnit("taunt"));
            SandboxAiExecutor.TakeTurn(battle);
            Assert.AreEqual(3, battle.GetUnit("weak").HitPoints, "слабую цель бить нельзя");
            Assert.Less(battle.GetUnit("taunt").HitPoints, 20, "удар по провокатору");
        }
    }
}

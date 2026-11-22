using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace KingdomSurvival.BattleSandbox.Tests
{
    // Бриф завершения, этап 16: ИИ и предпросмотр новых систем — перехват
    // защитником, «При смерти», удар за выход из соседства, кокон.
    public sealed class NewSystemsAiPreviewTests
    {
        private static SandboxUnitState U(string id, SandboxTeam team, int q, int r, int init, bool rescuable = false, params string[] tags)
        {
            return new SandboxUnitState(id, new SandboxUnitDefinition(id, id, SandboxUnitRole.Militia, 20, 2, 2, 3, 4, init, 1, tags, null, null, 0, rescuable),
                team, new HexCoord(q, r));
        }

        private static SandboxBattle Build(params SandboxUnitState[] units)
        {
            Dictionary<HexCoord, SandboxTerrain> map = new Dictionary<HexCoord, SandboxTerrain>();
            SandboxTerrainRules.RegisterBattle(units, map);
            SandboxBattle battle = new SandboxBattle(14, 10, units, map);
            battle.Start();
            return battle;
        }

        [Test]
        public void Preview_ShowsInterceptor_WithoutConsumingWard()
        {
            SandboxBattle b = Build(U("foe", SandboxTeam.Enemy, 2, 2, 9), U("ward", SandboxTeam.Player, 3, 2, 5), U("guard", SandboxTeam.Player, 4, 2, 4));
            Assert.IsTrue(b.ApplyStatus(SandboxBattle.GuardedId, b.GetUnit("ward"), b.GetUnit("guard")).Applied);
            SandboxAttackPreview preview = b.PreviewAttack("foe", "ward");
            Assert.IsTrue(preview.Notes.Any(note => note.Contains("guard")), string.Join(" | ", preview.Notes));
            Assert.AreSame(b.GetUnit("guard"), b.PreviewInterceptor(b.GetUnit("foe"), b.GetUnit("ward")));
            Assert.IsTrue(b.GetUnit("ward").HasStatus(SandboxBattle.GuardedId), "предпросмотр не тратит защиту");
        }

        [Test]
        public void PathRisk_ListsOpportunist_OnlyWhenLeavingAdjacency()
        {
            SandboxBattle b = Build(U("hero", SandboxTeam.Player, 5, 4, 9), U("opp", SandboxTeam.Enemy, 6, 4, 5, false, "passive.20261119.249"));
            HexCoord away = b.GetReachable("hero").Keys.First(c => c.DistanceTo(new HexCoord(6, 4)) > 1);
            HexCoord stay = b.GetReachable("hero").Keys.First(c => c.DistanceTo(new HexCoord(6, 4)) == 1);
            Assert.AreEqual(1, b.LeaveReactorsOnPath("hero", away).Count);
            Assert.AreEqual(0, b.LeaveReactorsOnPath("hero", stay).Count);
        }

        [Test]
        public void Ai_RescuesDyingAlly_InsteadOfAttacking()
        {
            SandboxBattle b = Build(U("medic", SandboxTeam.Enemy, 3, 2, 9), U("downed", SandboxTeam.Enemy, 4, 2, 2, true),
                U("hero", SandboxTeam.Player, 2, 2, 5));
            SandboxUnitState downed = b.GetUnit("downed");
            b.ApplyStatus(SandboxStatusCatalog.Poison, downed, b.GetUnit("hero"));
            downed.HitPoints = 1;
            // Падение в 0 HP с флагом спасения — «При смерти», не смерть.
            System.Reflection.MethodInfo enter = typeof(SandboxBattle).GetMethod("TryEnterDying",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            downed.HitPoints = 0;
            Assert.IsTrue((bool)enter.Invoke(b, new object[] { downed }));
            Assert.AreEqual("medic", b.CurrentUnit.Id);
            SandboxAiDecision decision = SandboxAiPlanner.Plan(b, b.GetUnit("medic"), true);
            Assert.AreEqual(SandboxAiActionKind.Context, decision.Action, decision.Reason);
            Assert.AreEqual(SandboxContextActionKind.Rescue, decision.Context.Kind);
        }

        [Test]
        public void Preview_DyingTarget_NotesFinalDeath()
        {
            SandboxBattle b = Build(U("hero", SandboxTeam.Player, 2, 2, 9), U("downed", SandboxTeam.Enemy, 3, 2, 2, true), U("other", SandboxTeam.Enemy, 12, 8, 1));
            SandboxUnitState downed = b.GetUnit("downed");
            System.Reflection.MethodInfo enter = typeof(SandboxBattle).GetMethod("TryEnterDying",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            downed.HitPoints = 0;
            Assert.IsTrue((bool)enter.Invoke(b, new object[] { downed }));
            SandboxAttackPreview preview = b.PreviewAttack("hero", "downed");
            Assert.IsTrue(preview.IsValid, preview.Reason);
            Assert.IsTrue(preview.Notes.Any(note => note.Contains("При смерти")), string.Join(" | ", preview.Notes));
            Assert.IsTrue(b.TryAttack("hero", "downed", out string m), m);
            Assert.IsFalse(downed.IsDying, "удар по лежащему — окончательная гибель");
            Assert.IsTrue(downed.IsDefeated);
            Assert.AreEqual(0, downed.HitPoints, "без фиктивного урона ниже нуля");
        }

        [Test]
        public void Hud_DyingUnitKeepsStatusIcon_FinalDeathClearsIt()
        {
            SandboxBattle b = Build(U("hero", SandboxTeam.Player, 2, 2, 9), U("downed", SandboxTeam.Enemy, 3, 2, 2, true), U("other", SandboxTeam.Enemy, 12, 8, 1));
            SandboxUnitState downed = b.GetUnit("downed");
            System.Reflection.MethodInfo enter = typeof(SandboxBattle).GetMethod("TryEnterDying",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            downed.HitPoints = 0;
            Assert.IsTrue((bool)enter.Invoke(b, new object[] { downed }));
            Assert.IsTrue(b.GetStatusViews("downed", SandboxTeam.Player).Any(view => view.StatusId == SandboxBattle.DyingId), "значок «При смерти» виден");
            Assert.IsTrue(b.TryAttack("hero", "downed", out string m), m);
            Assert.AreEqual(0, b.GetStatusViews("downed", SandboxTeam.Player).Count, "после гибели значков нет");
        }
    }
}

using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace KingdomSurvival.BattleSandbox.Tests
{
    // Бриф завершения, этап 2: реакции тегов на события состояний —
    // срабатывание, неподходящий переход и лимит.
    public sealed class StatusReactionTagTests
    {
        private const string P = "passive.status.20261120.";

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

        private static (string, SandboxTeam, int, int, int, string[]) A(string id, int q, int r, int init, params string[] tags) => (id, SandboxTeam.Player, q, r, init, tags);
        private static (string, SandboxTeam, int, int, int, string[]) F(string id, int q, int r, int init, params string[] tags) => (id, SandboxTeam.Enemy, q, r, init, tags);

        private static SandboxStatusApplyResult Apply(SandboxBattle b, string status, string target, string source) =>
            b.ApplyStatus(status, b.GetUnit(target), b.GetUnit(source));

        private static void PassUntil(SandboxBattle battle, string unitId)
        {
            battle.EndActivation();
            for (int i = 0; i < 40 && battle.CurrentUnit != null && battle.CurrentUnit.Id != unitId; i++)
                battle.EndActivation();
        }

        [Test]
        public void Antidote_CancelsFirstPoisonOnAdjacentAlly_OncePerBattle_NotOwner()
        {
            SandboxBattle b = Build(A("medic", 2, 2, 9, P + "003"), A("ally", 3, 2, 8), F("foe", 9, 6, 1));
            Assert.IsFalse(Apply(b, SandboxStatusCatalog.Poison, "medic", "foe").Applied == false, "носителя не защищает");
            Assert.IsFalse(Apply(b, SandboxStatusCatalog.Poison, "ally", "foe").Applied, "первое на соседа отменено");
            Assert.IsTrue(Apply(b, "status.002", "ally", "foe").Applied, "другое состояние не трогает");
            b.RemoveStatus(b.GetUnit("ally"), "status.002", "тест");
            Assert.IsTrue(Apply(b, SandboxStatusCatalog.Poison, "ally", "foe").Applied, "заряд один на бой");
        }

        [Test]
        public void BloodTrail_FirstBleedByOwner_AlsoMarksPrey_OncePerBattle()
        {
            SandboxBattle b = Build(F("hunter", 2, 2, 9, P + "006"), A("prey", 3, 2, 5), A("other", 5, 5, 4));
            Apply(b, SandboxStatusCatalog.Bleeding, "prey", "hunter");
            Assert.IsTrue(b.GetUnit("prey").HasStatus("status.101"));
            Apply(b, SandboxStatusCatalog.Bleeding, "other", "hunter");
            Assert.IsFalse(b.GetUnit("other").HasStatus("status.101"), "раз за бой");
        }

        [Test]
        public void FireWakesFury_OnBurningTick_NotOnApplication()
        {
            SandboxBattle b = Build(A("hero", 2, 2, 9, P + "011"), F("foe", 9, 6, 5));
            b.EndActivation();
            Apply(b, SandboxStatusCatalog.Burning, "hero", "foe");
            Assert.IsFalse(b.GetUnit("hero").HasStatus("status.113"), "наложение — не тик");
            PassUntil(b, "hero");
            Assert.IsTrue(b.GetUnit("hero").HasStatus("status.113"), "после фактического тика");
        }

        [Test]
        public void HardToFrighten_ImmuneAfterNaturalEnd_NotAfterCleanse()
        {
            SandboxBattle b = Build(A("hero", 2, 2, 9, P + "024"), A("medic", 3, 2, 8), F("foe", 9, 6, 1), F("foe2", 9, 7, 0));
            b.EndActivation();
            Apply(b, "status.057", "hero", "foe");
            PassUntil(b, "hero");
            b.EndActivation();
            Assert.IsFalse(b.GetUnit("hero").HasStatus("status.057"), "закончился естественно");
            Assert.IsFalse(Apply(b, "status.057", "hero", "foe").Applied, "от того же источника — невосприимчив");
            Assert.IsTrue(Apply(b, "status.057", "hero", "foe2").Applied, "другой источник действует");
        }

        [Test]
        public void BlindCannotSeeTerror_WhileBlind_Only()
        {
            SandboxBattle b = Build(A("hero", 2, 2, 9, P + "027"), F("foe", 9, 6, 5));
            Assert.IsTrue(Apply(b, "status.057", "hero", "foe").Applied, "зрячий получает Ужас");
            b.RemoveStatus(b.GetUnit("hero"), "status.057", "тест");
            Apply(b, "status.168", "hero", "foe");
            Assert.IsFalse(Apply(b, "status.057", "hero", "foe").Applied, "ослеплённый — нет");
        }

        [Test]
        public void ChallengeHoldsLine_NearestAllyInspired()
        {
            SandboxBattle b = Build(A("hero", 2, 2, 9, P + "030"), A("ally", 3, 3, 8), F("foe", 9, 6, 5));
            Apply(b, "status.106", "hero", "foe");
            Assert.IsTrue(b.GetUnit("ally").HasStatus(SandboxStatusCatalog.Inspired));
        }

        [Test]
        public void Bloodlust_RevealsAndBlocksOwnInvisibility()
        {
            SandboxBattle b = Build(A("hero", 2, 2, 9, P + "053"), F("foe", 9, 6, 5));
            Apply(b, SandboxStatusCatalog.Invisible, "hero", "hero");
            Apply(b, "status.117", "hero", "hero");
            Assert.IsFalse(b.GetUnit("hero").HasStatus(SandboxStatusCatalog.Invisible));
            Assert.IsFalse(Apply(b, SandboxStatusCatalog.Invisible, "hero", "hero").Applied);
        }

        [Test]
        public void HoldBackLuck_CancelsBadLuck_OnlyWithoutGoodLuck()
        {
            SandboxBattle b = Build(A("hero", 2, 2, 9, P + "055"), F("foe", 9, 6, 5));
            Assert.IsFalse(Apply(b, SandboxStatusCatalog.BadLuck, "hero", "foe").Applied);
            Assert.IsTrue(Apply(b, SandboxStatusCatalog.BadLuck, "hero", "foe").Applied, "один заряд за бой");
        }

        [Test]
        public void StepAgainstWeakness_HasteAbsorbsFirstSlow()
        {
            SandboxBattle b = Build(A("hero", 2, 2, 9, P + "057"), F("foe", 9, 6, 5));
            Assert.IsTrue(Apply(b, "status.041", "hero", "foe").Applied, "без ускорения — обычно");
            b.RemoveStatus(b.GetUnit("hero"), "status.041", "тест");
            Apply(b, "status.156", "hero", "hero");
            Assert.IsFalse(Apply(b, "status.041", "hero", "foe").Applied);
            Assert.IsFalse(b.GetUnit("hero").HasStatus("status.156"), "ускорение потрачено");
        }

        [Test]
        public void HandsRememberShot_ReplacesForgetfulnessWithWeakenedBlow()
        {
            SandboxBattle b = Build(A("hero", 2, 2, 9, P + "079"), F("foe", 9, 6, 5));
            Apply(b, "status.084", "hero", "foe");
            Assert.IsFalse(b.GetUnit("hero").HasStatus("status.084"));
            Assert.IsTrue(b.GetUnit("hero").HasStatus("status.088"));
            Assert.AreEqual(1, b.GetUnit("hero").Statuses.Single().EffectiveDuration);
        }

        [Test]
        public void ShroudLeavesBreach_WeaknessAfterInvulnerabilityEnds()
        {
            SandboxBattle b = Build(A("hero", 2, 2, 9, P + "080"), F("foe", 9, 6, 5));
            Apply(b, SandboxStatusCatalog.Invulnerable, "hero", "hero");
            PassUntil(b, "hero");
            Assert.IsFalse(b.GetUnit("hero").HasStatus(SandboxStatusCatalog.Invulnerable));
            Assert.IsTrue(b.GetUnit("hero").HasStatus("status.071"));
        }

        [Test]
        public void FrostOnWeak_AddsExtraCharge_PerRound()
        {
            SandboxBattle b = Build(F("cold", 2, 2, 9, P + "013"), A("hero", 9, 6, 5));
            Apply(b, "status.071", "hero", "cold");
            Apply(b, SandboxStatusCatalog.Frost, "hero", "cold");
            Assert.AreEqual(2, b.GetUnit("hero").Statuses.Single(s => s.StatusId == SandboxStatusCatalog.Frost).Charges.Count, "1 + 1 за слабость");
        }

        [Test]
        public void ShedsCurse_RemovesShortestMagicalAtStart()
        {
            SandboxBattle b = Build(A("hero", 2, 2, 9, "passive.20261119.023"), F("foe", 9, 6, 5));
            b.EndActivation();
            Apply(b, "status.006", "hero", "foe");
            Apply(b, "status.099", "hero", "foe");
            PassUntil(b, "hero");
            Assert.AreEqual(1, b.GetUnit("hero").Statuses.Count(s => s.StatusId == "status.006" || s.StatusId == "status.099"), "снята одна");
        }

        [Test]
        public void DrainLinkShortRange_BreaksBeyondTwo()
        {
            SandboxBattle b = Build(F("leech", 2, 2, 1), A("hero", 5, 2, 9, P + "041"));
            Apply(b, "status.007", "hero", "leech");
            PassUntil(b, "leech");
            Assert.IsFalse(b.GetUnit("hero").HasStatus("status.007"), "3 гекса — связь порвалась");
        }

        [Test]
        public void CinderShield_FirstRetortPerRoundReduced()
        {
            SandboxBattle b = Build(A("hero", 2, 2, 9, P + "047"), F("foe", 3, 2, 5));
            Apply(b, "status.138", "foe", "foe");
            Assert.IsTrue(b.TryAttack("hero", "foe", out _));
            Assert.AreEqual(20, b.GetUnit("hero").HitPoints - (b.HasPendingRetaliation ? 0 : 0), "ответ покрова 1 → 0");
        }

        [Test]
        public void SentenceWard_FirstDelayedBlastMinusOne()
        {
            SandboxBattle b = Build(A("hero", 2, 2, 9, P + "038"), F("foe", 9, 6, 5));
            Apply(b, "status.010", "hero", "foe");
            for (int i = 0; i < 4; i++)
                PassUntil(b, "hero");
            Assert.AreEqual(18, b.GetUnit("hero").HitPoints, "3 − 1");
        }

        [Test]
        public void ParasiteSqueeze_SelfRemovalAlsoRemovesBleedCharge()
        {
            SandboxAbilityDefinition attach = SandboxAbilityDefaults.Create(SandboxAbilityKind.Attach, "Attach");
            List<SandboxUnitState> units = new List<SandboxUnitState>
            {
                new SandboxUnitState("tick", new SandboxUnitDefinition("tick", "tick", SandboxUnitRole.Beast, 8, 2, 2, 2, 4, 9, 1, null, null, new[] { attach }), SandboxTeam.Enemy, new HexCoord(3, 2)),
                new SandboxUnitState("hero", new SandboxUnitDefinition("hero", "hero", SandboxUnitRole.Militia, 20, 2, 2, 3, 4, 5, 1, new[] { P + "065" }), SandboxTeam.Player, new HexCoord(2, 2))
            };
            Dictionary<HexCoord, SandboxTerrain> map = new Dictionary<HexCoord, SandboxTerrain>();
            SandboxTerrainRules.RegisterBattle(units, map);
            SandboxBattle b = new SandboxBattle(14, 10, units, map);
            b.Start();
            SandboxBattle.AbilityCandidate c = b.GetAbilityCandidates("tick", "Attach").First();
            Assert.IsTrue(b.TryUseAbility("tick", c.Command, out string m), m);
            if (b.HasPendingRetaliation)
                b.TryResolvePendingRetaliation(out _);
            Assert.IsTrue(b.GetUnit("tick").IsAttached, m);
            Apply(b, SandboxStatusCatalog.Bleeding, "hero", "tick");
            PassUntil(b, "hero");
            SandboxContextAction remove = b.GetContextActions("hero").Single(a => a.Kind == SandboxContextActionKind.RemoveParasite);
            Assert.IsTrue(b.TryContextAction("hero", remove, out _));
            Assert.IsFalse(b.GetUnit("hero").HasStatus(SandboxStatusCatalog.Bleeding), "заряд Кровотечения снят");
        }

        [Test]
        public void ReactionChain_IsFinite_AndLogged()
        {
            SandboxBattle b = Build(F("cold", 2, 2, 9, P + "013", "passive.20261119b.036"), A("hero", 9, 6, 5));
            Apply(b, "status.071", "hero", "cold");
            Assert.IsTrue(b.GetUnit("hero").HasStatus(SandboxStatusCatalog.Frost), "иней поверх слабости");
            Assert.IsTrue(b.CombatEvents.Any(e => e.Kind == SandboxCombatEventKind.Reaction));
        }
    }
}

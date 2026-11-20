using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace KingdomSurvival.BattleSandbox.Tests
{
    // Пакет «Пассивные теги»: фактическое срабатывание, граничное условие и
    // лимит пилотных и ключевых семейств. Атака = Защита = 2, Урон 3.
    public sealed class PassiveEngineTests
    {
        private sealed class Spec
        {
            public string Id;
            public SandboxTeam Team;
            public HexCoord Cell;
            public int Hp = 20;
            public int Damage = 3;
            public int Init = 5;
            public int Range = 1;
            public int Move = 4;
            public string[] Tags = new string[0];
            public SandboxAbilityDefinition[] Abilities = new SandboxAbilityDefinition[0];
        }

        private static Spec P(string id, int q, int r, int init, params string[] tags) => new Spec { Id = id, Team = SandboxTeam.Player, Cell = new HexCoord(q, r), Init = init, Tags = tags };
        private static Spec E(string id, int q, int r, int init, params string[] tags) => new Spec { Id = id, Team = SandboxTeam.Enemy, Cell = new HexCoord(q, r), Init = init, Tags = tags };

        private static SandboxBattle Build(params Spec[] specs)
        {
            List<SandboxUnitState> units = specs.Select(spec => new SandboxUnitState(
                spec.Id,
                new SandboxUnitDefinition(spec.Id, spec.Id, SandboxUnitRole.Beast, spec.Hp, 2, 2, spec.Damage, spec.Move, spec.Init, spec.Range, spec.Tags, null, spec.Abilities),
                spec.Team, spec.Cell)).ToList();
            Dictionary<HexCoord, SandboxTerrain> map = new Dictionary<HexCoord, SandboxTerrain>();
            SandboxTerrainRules.RegisterBattle(units, map);
            SandboxBattle battle = new SandboxBattle(14, 10, units, map);
            battle.Start();
            return battle;
        }

        private static void PassUntil(SandboxBattle battle, string unitId)
        {
            for (int i = 0; i < 40 && battle.CurrentUnit != null && battle.CurrentUnit.Id != unitId; i++)
                battle.EndActivation();
            Assert.AreEqual(unitId, battle.CurrentUnit?.Id);
        }

        private static void Attack(SandboxBattle battle, string attacker, string target)
        {
            Assert.IsTrue(battle.TryAttack(attacker, target, out string message), message);
            if (battle.HasPendingRetaliation)
                battle.TryResolvePendingRetaliation(out _);
        }

        [Test]
        public void Catalog_AllTagsUnique_AndNoEmptyRules()
        {
            Assert.Greater(SandboxPassiveCatalog.All.Count, 100);
            Assert.AreEqual(SandboxPassiveCatalog.All.Count, SandboxPassiveCatalog.All.Select(def => def.TagId).Distinct().Count());
            Assert.IsTrue(SandboxPassiveCatalog.All.All(def => def.Rules.Count > 0));
        }

        [Test]
        public void PackAlias_AndLegacyTag_GiveOneBonusOnly()
        {
            SandboxBattle both = Build(E("w1", 3, 2, 9, SandboxCombatTagRules.Pack, "tactic.pack_hunter"), E("w2", 2, 3, 1, SandboxCombatTagRules.Pack), P("hero", 2, 2, 5));
            SandboxBattle legacy = Build(E("w1", 3, 2, 9, SandboxCombatTagRules.Pack), E("w2", 2, 3, 1, SandboxCombatTagRules.Pack), P("hero", 2, 2, 5));
            SandboxBattle aliasOnly = Build(E("w1", 3, 2, 9, "tactic.pack_hunter"), E("w2", 2, 3, 1, "tactic.pack_hunter"), P("hero", 2, 2, 5));
            int expected = legacy.PreviewAttack("w1", "hero").Damage;
            Assert.AreEqual(expected, both.PreviewAttack("w1", "hero").Damage, "alias + старый ID — один бонус");
            Assert.AreEqual(expected, aliasOnly.PreviewAttack("w1", "hero").Damage, "новый ID исполняет то же правило");
        }

        [Test]
        public void VenomAttack_FirstHitPerOwnTurn_Poison1x2_RefreshFromSameSource()
        {
            SandboxBattle battle = Build(E("shipovik", 3, 2, 9, "passive.20261119.052"), P("hero", 2, 2, 5));
            SandboxUnitState hero = battle.GetUnit("hero");
            Attack(battle, "shipovik", "hero");
            SandboxStatusInstance poison = hero.Statuses.Single(item => item.StatusId == SandboxStatusCatalog.Poison);
            Assert.AreEqual(2, poison.EffectiveDuration, "профиль 1×2 пользовательского тега");
            battle.EndActivation();
            PassUntil(battle, "hero");
            int afterTick = hero.HitPoints;
            battle.EndActivation();
            Assert.AreEqual(1, hero.Statuses.Single().Elapsed);
            PassUntil(battle, "shipovik");
            Attack(battle, "shipovik", "hero");
            Assert.AreEqual(0, hero.Statuses.Single(item => item.StatusId == SandboxStatusCatalog.Poison).Elapsed, "повтор того же источника обновляет срок");
            Assert.Less(hero.HitPoints, afterTick);
        }

        [Test]
        public void VenomousFlesh_PoisonsMeleeAttacker_OncePerRound()
        {
            SandboxBattle battle = Build(P("hero", 2, 2, 9), E("toad", 3, 2, 5, "trait.venomous_flesh"));
            SandboxUnitState hero = battle.GetUnit("hero");
            Attack(battle, "hero", "toad");
            Assert.IsTrue(hero.HasStatus(SandboxStatusCatalog.Poison));
            Assert.AreEqual(2, hero.Statuses.Single().EffectiveDuration);
        }

        [Test]
        public void Bloodless_IsImmuneToBleeding_WithReason()
        {
            SandboxBattle battle = Build(P("hero", 2, 2, 9), E("husk", 6, 2, 5, "trait.bloodless"));
            SandboxStatusApplyResult result = battle.ApplyStatus(SandboxStatusCatalog.Bleeding, battle.GetUnit("husk"), battle.GetUnit("hero"));
            Assert.IsFalse(result.Applied);
            StringAssert.Contains("Без кровотока", result.RejectReason);
        }

        [Test]
        public void ExposedFlanks_MinusTwoDefense_OnlyWithTwoAdjacentEnemies()
        {
            SandboxBattle one = Build(P("a", 2, 2, 9), E("stone", 3, 2, 5, "trait.exposed_flanks"));
            SandboxBattle two = Build(P("a", 2, 2, 9), P("b", 4, 2, 8), E("stone", 3, 2, 5, "trait.exposed_flanks"));
            Assert.AreEqual(3, one.PreviewAttack("a", "stone").Damage, "один враг — штрафа нет");
            Assert.AreEqual(4, two.PreviewAttack("a", "stone").Damage, "два соседних врага: Атака 2 против Защиты 0 → ×1,5");
        }

        [Test]
        public void StickySurface_NextExitCostsOneMore_ThenConsumed()
        {
            SandboxBattle battle = Build(P("hero", 2, 2, 9), E("slime", 3, 2, 5, "body.sticky"));
            SandboxUnitState hero = battle.GetUnit("hero");
            Attack(battle, "hero", "slime");
            Assert.IsTrue(hero.Statuses.Any(item => item.Mode == "sticky_exit"));
            battle.EndActivation();
            PassUntil(battle, "hero");
            IReadOnlyDictionary<HexCoord, int> reachable = battle.GetReachable("hero");
            Assert.AreEqual(2, reachable[new HexCoord(1, 2)], "первый шаг из соседства: 1 + 1");
            Assert.IsTrue(battle.TryMove("hero", new HexCoord(1, 2), out string message), message);
            Assert.IsFalse(hero.Statuses.Any(item => item.Mode == "sticky_exit"), "расходуется при реальном выходе");
        }

        [Test]
        public void GuardAura_FirstHitOnStunnedNeighbour_MinusOne_OncePerRound()
        {
            SandboxBattle battle = Build(E("foe", 3, 2, 9), P("ally", 2, 2, 5), P("guard", 1, 2, 4, "passive.status.20261120.019"));
            battle.ApplyStatus(SandboxStatusCatalog.Stunned, battle.GetUnit("ally"), battle.GetUnit("foe"));
            Assert.AreEqual(2, battle.PreviewAttack("foe", "ally").Damage, "3 − 1");
            Attack(battle, "foe", "ally");
            Assert.AreEqual(18, battle.GetUnit("ally").HitPoints);
        }

        [Test]
        public void LynxPounce_FirstSuccessfulPounceBleeds_OncePerBattle()
        {
            SandboxAbilityDefinition pounce = new SandboxAbilityDefinition("lynx.pounce", "Прыжок", SandboxAbilityKind.Pounce, cooldown: 0, rangeMax: 3, pathMin: 2, pathMax: 3,
                damageMultiplier: 1.25m, statusPayloadId: SandboxStatusCatalog.Bleeding, statusPayloadOncePerBattle: true, statusPayloadDuration: 2, statusPayloadTick: 1);
            Spec lynx = E("lynx", 5, 2, 9);
            lynx.Abilities = new[] { pounce };
            lynx.Move = 5;
            SandboxBattle battle = Build(lynx, P("hero", 2, 2, 5));
            SandboxUnitState hero = battle.GetUnit("hero");
            SandboxBattle.AbilityCandidate candidate = battle.GetAbilityCandidates("lynx", "lynx.pounce").First();
            Assert.IsTrue(battle.TryUseAbility("lynx", candidate.Command, out string message), message);
            Assert.IsTrue(hero.HasStatus(SandboxStatusCatalog.Bleeding), message);
            Assert.IsTrue(battle.GetUnit("lynx").Passive.UsedInBattle.Contains("payload:lynx.pounce"));
        }

        [Test]
        public void CheatDeath_LeavesOneHp_OncePerBattle()
        {
            Spec tough = E("tough", 3, 2, 5, "passive.20261119.256");
            tough.Hp = 3;
            SandboxBattle battle = Build(P("hero", 2, 2, 9), tough, E("other", 9, 6, 1));
            Attack(battle, "hero", "tough");
            Assert.AreEqual(1, battle.GetUnit("tough").HitPoints);
            Assert.IsTrue(battle.GetUnit("tough").Passive.UsedInBattle.Any());
        }

        [Test]
        public void DeathBlast_HitsAllNeighbours_ChainIsFinite()
        {
            Spec a = E("a", 3, 2, 5, "passive.20261119.164");
            a.Hp = 2;
            Spec b = E("b", 4, 2, 4, "passive.20261119.164");
            b.Hp = 2;
            SandboxBattle battle = Build(P("hero", 2, 2, 9), a, b, E("far", 9, 7, 1));
            Attack(battle, "hero", "a");
            Assert.IsTrue(battle.GetUnit("a").IsDefeated);
            Assert.IsTrue(battle.GetUnit("b").IsDefeated, "взрыв a задел b, взрыв b — отдельное событие");
            Assert.AreEqual(18, battle.GetUnit("hero").HitPoints, "герой рядом с a: −2 один раз (b не сосед героя)");
        }

        [Test]
        public void EveryThirdHit_AppliesSlow_CounterIsPerBearer()
        {
            SandboxBattle battle = Build(E("s1", 3, 2, 9, "passive.20261119.009"), P("hero", 2, 2, 5));
            SandboxUnitState s1 = battle.GetUnit("s1");
            SandboxUnitState hero = battle.GetUnit("hero");
            for (int i = 0; i < 2; i++)
            {
                Attack(battle, "s1", "hero");
                Assert.IsFalse(hero.HasStatus("status.041"));
                battle.EndActivation();
                PassUntil(battle, "s1");
            }
            Attack(battle, "s1", "hero");
            Assert.IsTrue(hero.HasStatus("status.041"), "каждое третье попадание");
            Assert.AreEqual(0, s1.Passive.Counter("passive.20261119.009#0"), "счётчик сброшен");
        }

        [Test]
        public void TwoBearersOfSameType_HaveSeparateCounters()
        {
            SandboxBattle battle = Build(E("s1", 3, 2, 9, "passive.20261119.009"), E("s2", 2, 3, 8, "passive.20261119.009"), P("hero", 2, 2, 5));
            Attack(battle, "s1", "hero");
            Assert.AreEqual(1, battle.GetUnit("s1").Passive.Counter("passive.20261119.009#0"));
            Assert.AreEqual(0, battle.GetUnit("s2").Passive.Counter("passive.20261119.009#0"));
        }

        [Test]
        public void ShotExhausted_NoMeleeRetaliationUntilNextOwnTurn()
        {
            Spec archer = P("archer", 2, 2, 9, "tactic.shot_exhausted");
            archer.Range = 3;
            SandboxBattle battle = Build(archer, E("foe", 4, 2, 5));
            SandboxUnitState unit = battle.GetUnit("archer");
            Assert.IsTrue(unit.CanRetaliate);
            Attack(battle, "archer", "foe");
            Assert.IsFalse(unit.CanRetaliate, "после выстрела не отвечает");
        }
    }
}

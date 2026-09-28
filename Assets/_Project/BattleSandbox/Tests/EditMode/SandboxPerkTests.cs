using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace KingdomSurvival.BattleSandbox.Tests
{
    // 12Е-6: боевые правила первой партии особенностей (каталог §6.1,
    // варианты утверждены автором 28.09.2026). Атака 3 против Защиты 3 —
    // урон без поправок, поэтому каждая поправка видна в числе.
    public sealed class SandboxPerkTests
    {
        private sealed class Spec
        {
            public string Id;
            public SandboxTeam Team;
            public HexCoord Position;
            public int HitPoints = 100;
            public int Damage = 10;
            public int Range = 1;
            public int Initiative = 5;
            public string[] Perks = new string[0];
        }

        private static SandboxBattle Battle(int firstRoundBonus, params Spec[] specs)
        {
            List<SandboxUnitState> units = specs.Select(spec => new SandboxUnitState(
                spec.Id,
                new SandboxUnitDefinition(spec.Id, spec.Id, SandboxUnitRole.Militia, spec.HitPoints, 3, 3, spec.Damage, 3,
                    spec.Initiative, spec.Range, null, spec.Perks),
                spec.Team,
                spec.Position)).ToList();
            SandboxBattle battle = new SandboxBattle(6, 6, units);
            battle.PlayerFirstRoundInitiativeBonus = firstRoundBonus;
            battle.Start();
            return battle;
        }

        private static SandboxBattle Battle(params Spec[] specs)
        {
            return Battle(0, specs);
        }

        private static readonly HexCoord Center = new HexCoord(2, 2);

        // Две свободные клетки рядом с центром.
        private static HexCoord[] AroundCenter()
        {
            List<HexCoord> cells = new List<HexCoord>();
            for (int q = 0; q < 6; q++)
            for (int r = 0; r < 6; r++)
            {
                HexCoord cell = new HexCoord(q, r);
                if (cell.DistanceTo(Center) == 1)
                    cells.Add(cell);
            }
            Assert.GreaterOrEqual(cells.Count, 2);
            return cells.ToArray();
        }

        private static void Attack(SandboxBattle battle, string attackerId, string targetId)
        {
            Assert.AreEqual(attackerId, battle.CurrentUnit.Id, "Сейчас ход " + attackerId + ".");
            Assert.IsTrue(battle.TryAttack(attackerId, targetId, out string message), message);
        }

        private static void ResolveRetaliationAndEnd(SandboxBattle battle)
        {
            if (battle.HasPendingRetaliation)
                Assert.IsTrue(battle.TryResolvePendingRetaliation(out string message), message);
            battle.EndActivation();
        }

        private static SandboxBattle TwoAttackersOnDefender(params string[] defenderPerks)
        {
            HexCoord[] around = AroundCenter();
            return Battle(
                new Spec { Id = "p1", Team = SandboxTeam.Player, Position = around[0], Initiative = 10 },
                new Spec { Id = "p2", Team = SandboxTeam.Player, Position = around[1], Initiative = 9 },
                new Spec { Id = "e", Team = SandboxTeam.Enemy, Position = Center, Damage = 8, Perks = defenderPerks });
        }

        [Test]
        public void BasicDefense_FirstAttackPerRound_HasMinusOneAttack()
        {
            SandboxBattle battle = TwoAttackersOnDefender(SandboxPerks.BasicDefense);

            Attack(battle, "p1", "e");
            Assert.AreEqual(100 - 8, battle.GetUnit("e").HitPoints, "Первая атака за раунд: Атака 2 против Защиты 3 — 8 вместо 10.");
            ResolveRetaliationAndEnd(battle);

            Attack(battle, "p2", "e");
            Assert.AreEqual(100 - 8 - 10, battle.GetUnit("e").HitPoints, "Вторая атака за раунд — без поправки.");
        }

        [Test]
        public void Counterstrike_TwoRetaliationsPerRound_RankTwoAddsDamageToFirst()
        {
            SandboxBattle battle = TwoAttackersOnDefender(SandboxPerks.Counterstrike, SandboxPerks.CounterstrikeDamage);

            Attack(battle, "p1", "e");
            Assert.IsTrue(battle.HasPendingRetaliation);
            Assert.AreEqual(9, battle.PreviewPendingRetaliation().Damage, "Первый ответ за раунд +1 Урон.");
            ResolveRetaliationAndEnd(battle);
            Assert.AreEqual(100 - 9, battle.GetUnit("p1").HitPoints);

            Attack(battle, "p2", "e");
            Assert.IsTrue(battle.HasPendingRetaliation, "Второй ответ в том же раунде.");
            Assert.AreEqual(8, battle.PreviewPendingRetaliation().Damage, "Второй ответ — без прибавки.");
        }

        [Test]
        public void WithoutCounterstrike_OneRetaliationPerRound()
        {
            SandboxBattle battle = TwoAttackersOnDefender();
            Attack(battle, "p1", "e");
            ResolveRetaliationAndEnd(battle);
            Attack(battle, "p2", "e");
            Assert.IsFalse(battle.HasPendingRetaliation);
        }

        private static SandboxBattle GuardedDefender(params string[] perks)
        {
            SandboxBattle battle = Battle(
                new Spec { Id = "e", Team = SandboxTeam.Enemy, Position = Center, Damage = 50, Initiative = 10, Perks = perks },
                new Spec { Id = "p", Team = SandboxTeam.Player, Position = AroundCenter()[0], HitPoints = 20, Initiative = 5 });
            Assert.IsTrue(battle.TryGuard("e", out string message), message);
            battle.EndActivation();
            return battle;
        }

        [Test]
        public void FirstStrike_GuardingBearer_AnswersBeforeTheBlow()
        {
            SandboxBattle battle = GuardedDefender(SandboxPerks.FirstStrike);

            Attack(battle, "p", "e");
            Assert.IsTrue(battle.GetUnit("p").IsDefeated, "Ответ в стойке пришёл первым.");
            Assert.AreEqual(100, battle.GetUnit("e").HitPoints, "Удар выведенного из строя не состоялся.");
            Assert.IsFalse(battle.HasPendingRetaliation, "Ответ за раунд уже потрачен.");
        }

        [Test]
        public void WithoutFirstStrike_GuardingDefender_AnswersAfter()
        {
            SandboxBattle battle = GuardedDefender();

            Attack(battle, "p", "e");
            Assert.Less(battle.GetUnit("e").HitPoints, 100);
            Assert.IsTrue(battle.HasPendingRetaliation);
        }

        [Test]
        public void ColdEye_ShooterWhoDidNotMove_IgnoresOneDefense()
        {
            HexCoord shooterCell = new HexCoord(0, 2);
            HexCoord targetCell = new HexCoord(2, 2);
            Assert.AreEqual(2, shooterCell.DistanceTo(targetCell));
            Spec Shooter() => new Spec
            {
                Id = "p", Team = SandboxTeam.Player, Position = shooterCell, Range = 3, Initiative = 10,
                Perks = new[] { SandboxPerks.ColdEye }
            };
            Spec Target() => new Spec { Id = "e", Team = SandboxTeam.Enemy, Position = targetCell };

            SandboxBattle still = Battle(Shooter(), Target());
            Attack(still, "p", "e");
            Assert.AreEqual(100 - 12, still.GetUnit("e").HitPoints, "Защита 2 вместо 3: урон ×1,25.");

            SandboxBattle moved = Battle(Shooter(), Target());
            HexCoord step = Enumerable.Range(0, 6)
                .SelectMany(q => Enumerable.Range(0, 6).Select(r => new HexCoord(q, r)))
                .First(cell => cell.DistanceTo(shooterCell) == 1 && cell.DistanceTo(targetCell) == 2);
            Assert.IsTrue(moved.TryMove("p", step, out string moveMessage), moveMessage);
            Attack(moved, "p", "e");
            Assert.AreEqual(100 - 10, moved.GetUnit("e").HitPoints, "После движения — без поправки.");
        }

        [Test]
        public void StillStanding_SurvivesOneBlowPerBattle_NextBlowDowns()
        {
            HexCoord[] around = AroundCenter();
            SandboxBattle battle = Battle(
                new Spec { Id = "e1", Team = SandboxTeam.Enemy, Position = around[0], Damage = 50, Initiative = 10 },
                new Spec { Id = "e2", Team = SandboxTeam.Enemy, Position = around[1], Damage = 50, Initiative = 9 },
                new Spec { Id = "p", Team = SandboxTeam.Player, Position = Center, HitPoints = 20, Damage = 1, Perks = new[] { SandboxPerks.StillStanding } });

            Attack(battle, "e1", "p");
            SandboxUnitState bearer = battle.GetUnit("p");
            Assert.IsFalse(bearer.IsDefeated);
            Assert.AreEqual(1, bearer.HitPoints, "Смертельный удар оставил 1 здоровья.");
            Assert.IsTrue(bearer.StillStandingUsed);
            ResolveRetaliationAndEnd(battle);

            Attack(battle, "e2", "p");
            Assert.IsTrue(bearer.IsDefeated, "Следующий удар бьёт как обычно.");
        }

        [Test]
        public void FirstRoundInitiativeBonus_PlayerActsFirst_OnlyInFirstRound()
        {
            SandboxBattle battle = Battle(2,
                new Spec { Id = "p", Team = SandboxTeam.Player, Position = new HexCoord(0, 0), Initiative = 4 },
                new Spec { Id = "e", Team = SandboxTeam.Enemy, Position = new HexCoord(5, 5), Initiative = 5 });

            Assert.AreEqual("p", battle.TurnOrderIds[0], "Первый раунд: 4 + 2 против 5.");
            battle.EndActivation();
            battle.EndActivation();
            Assert.AreEqual(2, battle.Round);
            Assert.AreEqual("e", battle.TurnOrderIds[0], "Со второго раунда — обычная инициатива.");
        }
    }
}

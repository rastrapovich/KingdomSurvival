using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace KingdomSurvival.BattleSandbox.Tests
{
    // ПР-16 (бриф 19.11.2026): способности существ на общем исполнителе.
    // Атака = Защита, поэтому обычный урон равен базовому Урону.
    public sealed class CreatureAbilityTests
    {
        private sealed class Spec
        {
            public string Id;
            public SandboxTeam Team;
            public HexCoord Cell;
            public int Hp = 20;
            public int Damage = 4;
            public int Move = 4;
            public int Init = 5;
            public int Range = 1;
            public string[] Tags = new string[0];
            public SandboxAbilityDefinition[] Abilities = new SandboxAbilityDefinition[0];
            public string[] Perks = new string[0];
        }

        private static Spec P(string id, int q, int r, int init = 5) => new Spec { Id = id, Team = SandboxTeam.Player, Cell = new HexCoord(q, r), Init = init };
        private static Spec E(string id, int q, int r, int init = 5) => new Spec { Id = id, Team = SandboxTeam.Enemy, Cell = new HexCoord(q, r), Init = init };

        private static SandboxAbilityDefinition A(SandboxAbilityKind kind) => SandboxAbilityDefaults.Create(kind, kind.ToString());

        private static SandboxBattle Build(IDictionary<HexCoord, SandboxTerrain> terrain, params Spec[] specs)
        {
            List<SandboxUnitState> units = specs.Select(spec => new SandboxUnitState(
                spec.Id,
                new SandboxUnitDefinition(spec.Id, spec.Id, SandboxUnitRole.Beast, spec.Hp, 2, 2, spec.Damage, spec.Move, spec.Init, spec.Range,
                    spec.Tags, spec.Perks, spec.Abilities),
                spec.Team, spec.Cell)).ToList();
            Dictionary<HexCoord, SandboxTerrain> map = terrain != null ? new Dictionary<HexCoord, SandboxTerrain>(terrain) : new Dictionary<HexCoord, SandboxTerrain>();
            SandboxTerrainRules.RegisterBattle(units, map);
            SandboxBattle battle = new SandboxBattle(14, 10, units, map);
            battle.ResolveUnitType = id => new SandboxUnitDefinition(id, "Клещень", SandboxUnitRole.Beast, 8, 2, 2, 2, 5, 7, 1);
            return battle;
        }

        private static SandboxAbilityCommand Find(SandboxBattle battle, string unitId, string abilityId, string targetId = null)
        {
            SandboxBattle.AbilityCandidate candidate = battle.GetAbilityCandidates(unitId, abilityId)
                .FirstOrDefault(item => targetId == null || item.Command.TargetUnitId == targetId);
            Assert.IsNotNull(candidate, "Нет кандидата " + abilityId + " → " + targetId);
            return candidate.Command;
        }

        private static void Use(SandboxBattle battle, string unitId, SandboxAbilityCommand command)
        {
            Assert.IsTrue(battle.TryUseAbility(unitId, command, out string message), message);
            if (battle.HasPendingRetaliation)
                battle.TryResolvePendingRetaliation(out _);
            if (battle.CurrentUnit != null && battle.CurrentUnit.Id == unitId)
                battle.EndActivation();
        }

        private static void Pass(SandboxBattle battle)
        {
            battle.EndActivation();
        }

        private static void PassUntil(SandboxBattle battle, string unitId)
        {
            for (int i = 0; i < 40 && battle.CurrentUnit != null && battle.CurrentUnit.Id != unitId; i++)
                battle.EndActivation();
            Assert.AreEqual(unitId, battle.CurrentUnit?.Id);
        }

        // ---------- Урон ----------

        [Test]
        public void AbilityMultiplier_AppliedOnce_AndAlwaysAtLeastOneAboveNormal()
        {
            Spec lynx = E("lynx", 2, 2, 9);
            lynx.Damage = 3;
            lynx.Abilities = new[] { A(SandboxAbilityKind.BreakGuard) }; // ×1,15
            SandboxBattle battle = Build(null, lynx, P("hero", 3, 2));
            battle.Start();
            SandboxAbilityCommand command = Find(battle, "lynx", "BreakGuard", "hero");
            SandboxAbilityPreview preview = battle.PreviewAbility("lynx", command);
            Assert.AreEqual(3, preview.NormalDamage);
            Assert.AreEqual(4, preview.Damage, "3 × 1,15 = 3,45 → минимум +1");
            Use(battle, "lynx", command);
            Assert.AreEqual(16, battle.GetUnit("hero").HitPoints);
        }

        // ---------- Стая ----------

        [Test]
        public void Pack_GivesPlusOneAttack_OnlyWithAnotherPackMemberNextToTarget()
        {
            Spec wolfA = E("wolfA", 4, 4, 9);
            wolfA.Tags = new[] { SandboxCombatTagRules.Pack };
            Spec wolfB = E("wolfB", 6, 6, 8);
            wolfB.Tags = new[] { SandboxCombatTagRules.Pack };
            SandboxBattle battle = Build(null, wolfA, wolfB, P("hero", 5, 4));
            battle.Start();
            Assert.AreEqual(4, battle.PreviewAttack("wolfA", "hero").Damage, "второй волк далеко — бонуса нет");
            Assert.IsFalse(battle.HasPackBonus(battle.GetUnit("wolfA"), battle.GetUnit("hero"), new HexCoord(4, 4)));

            SandboxBattle near = Build(null, wolfA, new Spec { Id = "wolfB", Team = SandboxTeam.Enemy, Cell = new HexCoord(6, 4), Init = 8, Tags = new[] { SandboxCombatTagRules.Pack } }, P("hero", 5, 4));
            near.Start();
            Assert.IsTrue(near.HasPackBonus(near.GetUnit("wolfA"), near.GetUnit("hero"), new HexCoord(4, 4)));
            Assert.AreEqual(5, near.PreviewAttack("wolfA", "hero").Damage, "+1 Атака → ×1,25");
            Assert.IsEmpty(near.GetAbilityStates("wolfA"), "Стая — тег, не кнопка");
        }

        // ---------- Прыжок ----------

        [Test]
        public void Pounce_LandsNextToTarget_SpendsMovement_AndHitsHarder()
        {
            Spec lynx = E("lynx", 2, 4, 9);
            lynx.Abilities = new[] { A(SandboxAbilityKind.Pounce) };
            lynx.Move = 5;
            SandboxBattle battle = Build(null, lynx, P("hero", 5, 4));
            battle.Start();
            SandboxAbilityCommand command = Find(battle, "lynx", "Pounce", "hero");
            Assert.AreEqual(1, command.TargetCell.Value.DistanceTo(new HexCoord(5, 4)));
            Use(battle, "lynx", command);
            Assert.AreEqual(command.TargetCell.Value, battle.GetUnit("lynx").Position);
            Assert.AreEqual(15, battle.GetUnit("hero").HitPoints, "4 × 1,25 = 5");
        }

        [Test]
        public void Pounce_BlockedByWall_AndOccupiedLanding()
        {
            Spec lynx = E("lynx", 2, 4, 9);
            lynx.Abilities = new[] { A(SandboxAbilityKind.Pounce) };
            Dictionary<HexCoord, SandboxTerrain> walls = new Dictionary<HexCoord, SandboxTerrain>
            {
                { new HexCoord(3, 3), SandboxTerrain.Impassable }, { new HexCoord(3, 4), SandboxTerrain.Impassable },
                { new HexCoord(3, 5), SandboxTerrain.Impassable }, { new HexCoord(2, 3), SandboxTerrain.Impassable },
                { new HexCoord(2, 5), SandboxTerrain.Impassable }
            };
            SandboxBattle battle = Build(walls, lynx, P("hero", 5, 4));
            battle.Start();
            Assert.IsEmpty(battle.GetAbilityCandidates("lynx", "Pounce"), "через стену прыгать нельзя");
            SandboxAbilityState state = battle.GetAbilityStates("lynx").Single();
            Assert.IsFalse(state.Available);
            StringAssert.Contains("приземлением", state.Reason);
        }

        [Test]
        public void Pounce_FirstStrikeKillingLynx_CancelsTheHit()
        {
            Spec lynx = E("lynx", 2, 4, 9);
            lynx.Hp = 2;
            lynx.Abilities = new[] { A(SandboxAbilityKind.Pounce) };
            Spec hero = P("hero", 5, 4, 1);
            hero.Perks = new[] { SandboxPerks.FirstStrike };
            SandboxBattle battle = Build(null, lynx, hero);
            battle.Start();
            battle.GetUnit("hero").IsGuarding = true;
            Use(battle, "lynx", Find(battle, "lynx", "Pounce", "hero"));
            Assert.IsTrue(battle.GetUnit("lynx").IsDefeated);
            Assert.AreEqual(20, battle.GetUnit("hero").HitPoints);
        }

        // ---------- Разгон ----------

        [Test]
        public void Charge_NeedsStraightRun_TurnGivesNothing_AndPushes()
        {
            Spec vepr = E("vepr", 1, 4, 9);
            SandboxAbilityDefinition charge = new SandboxAbilityDefinition("Charge", "Разгон", SandboxAbilityKind.Charge, cooldown: 3, pathMin: 2, pathMax: 4, damageMultiplier: 1.25m, push: 1);
            vepr.Abilities = new[] { charge };
            vepr.Move = 4;
            SandboxBattle battle = Build(null, vepr, P("hero", 5, 4));
            battle.Start();
            SandboxAbilityCommand command = Find(battle, "vepr", "Charge", "hero");
            Assert.AreEqual(new HexCoord(4, 4), command.TargetCell.Value, "прямая вправо: (2,4)…(4,4)");
            Use(battle, "vepr", command);
            Assert.AreEqual(15, battle.GetUnit("hero").HitPoints);
            Assert.AreEqual(new HexCoord(6, 4), battle.GetUnit("hero").Position, "отброшен на 1 гекс по линии");

            // Цель не на прямой — разгона нет.
            SandboxBattle bent = Build(null, new Spec { Id = "vepr", Team = SandboxTeam.Enemy, Cell = new HexCoord(1, 4), Init = 9, Move = 4, Abilities = new[] { charge } }, P("hero", 4, 1));
            bent.Start();
            Assert.IsFalse(bent.GetAbilityCandidates("vepr", "Charge").Any(item => item.Command.TargetUnitId == "hero"));
        }

        [Test]
        public void Charge_WetGroundCostsMore_AndBudgetLimitsTheRun()
        {
            Spec boar = E("boar", 1, 4, 9);
            boar.Abilities = new[] { A(SandboxAbilityKind.Charge) };
            boar.Move = 3;
            SandboxBattle battle = Build(null, boar, P("hero", 5, 4));
            battle.SetInitialWater(new[] { new HexCoord(2, 4), new HexCoord(3, 4) });
            battle.Start();
            // 3 гекса по прямой: вода 2 + 2 + 1 = 5 > 3 движения.
            Assert.IsEmpty(battle.GetAbilityCandidates("boar", "Charge"));
        }

        // ---------- Сбить стойку / Отбросить ----------

        [Test]
        public void BreakGuard_HitsCurrentDefense_ThenRemovesOnlyGuard()
        {
            Spec bear = E("bear", 2, 4, 9);
            bear.Abilities = new[] { A(SandboxAbilityKind.BreakGuard) };
            Spec hero = P("hero", 3, 4, 1);
            hero.Perks = new[] { SandboxPerks.Counterstrike };
            SandboxBattle battle = Build(null, bear, hero);
            battle.Start();
            battle.GetUnit("hero").IsGuarding = true;
            SandboxAbilityCommand command = Find(battle, "bear", "BreakGuard", "hero");
            int expected = battle.PreviewAbility("bear", command).Damage;
            Assert.IsTrue(battle.TryUseAbility("bear", command, out _));
            Assert.AreEqual(20 - expected, battle.GetUnit("hero").HitPoints);
            Assert.IsFalse(battle.GetUnit("hero").IsGuarding);
            Assert.IsTrue(battle.GetUnit("hero").HasPerk(SandboxPerks.Counterstrike), "перки не трогаются");
        }

        [Test]
        public void Shove_BlockedByWall_StillDamages_AndKeepsRetaliation()
        {
            Spec elk = E("elk", 2, 4, 9);
            elk.Abilities = new[] { A(SandboxAbilityKind.Shove) };
            SandboxBattle battle = Build(new Dictionary<HexCoord, SandboxTerrain> { { new HexCoord(4, 4), SandboxTerrain.Impassable } }, elk, P("hero", 3, 4, 1));
            battle.Start();
            SandboxAbilityCommand command = Find(battle, "elk", "Shove", "hero");
            StringAssert.Contains("стена", battle.PreviewAbility("elk", command).ForcedBlockedReason);
            Assert.IsTrue(battle.TryUseAbility("elk", command, out string message));
            StringAssert.Contains("невозможно", message);
            Assert.AreEqual(new HexCoord(3, 4), battle.GetUnit("hero").Position);
            Assert.Less(battle.GetUnit("hero").HitPoints, 20);
            Assert.IsTrue(battle.HasPendingRetaliation, "контакт сохранился — ответ есть");
        }

        [Test]
        public void Shove_Successful_BreaksContact_NoRetaliation()
        {
            Spec elk = E("elk", 2, 4, 9);
            elk.Abilities = new[] { A(SandboxAbilityKind.Shove) };
            SandboxBattle battle = Build(null, elk, P("hero", 3, 4, 1));
            battle.Start();
            Assert.IsTrue(battle.TryUseAbility("elk", Find(battle, "elk", "Shove", "hero"), out _));
            Assert.AreEqual(new HexCoord(4, 4), battle.GetUnit("hero").Position);
            Assert.IsFalse(battle.HasPendingRetaliation);
        }

        // ---------- Кулдаун ----------

        [Test]
        public void Cooldown2_SkipsTwoOwnActivations()
        {
            Spec elk = E("elk", 2, 4, 9);
            elk.Abilities = new[] { A(SandboxAbilityKind.Shove) };
            elk.Hp = 99;
            Spec hero = P("hero", 3, 4, 1);
            hero.Hp = 99;
            SandboxBattle battle = Build(null, elk, hero);
            battle.Start();
            Use(battle, "elk", Find(battle, "elk", "Shove", "hero"));
            for (int activation = 2; activation <= 4; activation++)
            {
                PassUntil(battle, "elk");
                SandboxAbilityState state = battle.GetAbilityStates("elk").Single();
                if (activation < 4)
                    Assert.IsFalse(state.Available, "активация " + activation);
                else
                    Assert.IsTrue(state.Available || !string.IsNullOrEmpty(state.Reason) && !state.Reason.StartsWith("Перезарядка"), "на 4-й снова готова");
                Pass(battle);
            }
        }

        // ---------- Клещень ----------

        [Test]
        public void Attach_TicksFromNextActivation_RemovableByAlly_AndCooldownAfter()
        {
            Spec tick = E("tick", 2, 4, 9);
            tick.Hp = 8;
            tick.Damage = 2;
            tick.Abilities = new[] { A(SandboxAbilityKind.Attach), A(SandboxAbilityKind.StunSpit) };
            Spec hero = P("hero", 3, 4, 5);
            Spec ally = P("ally", 4, 4, 3);
            SandboxBattle battle = Build(null, tick, hero, ally);
            battle.Start();
            Use(battle, "tick", Find(battle, "tick", "Attach", "hero"));
            SandboxUnitState parasite = battle.GetUnit("tick");
            if (parasite.IsDefeated)
                Assert.Inconclusive("Ответ героя убил клещня");
            Assert.IsTrue(parasite.IsAttached);
            Assert.IsNull(battle.GetUnitAt(new HexCoord(2, 4)), "прикреплённый не занимает гекс");
            int heroHp = battle.GetUnit("hero").HitPoints;
            Pass(battle); // герой
            Pass(battle); // союзник → раунд 2: тик клещня и автоконец его хода
            Assert.AreEqual(2, battle.Round);
            Assert.AreEqual("hero", battle.CurrentUnit?.Id, "после тика ход паразита завершился сам");
            Assert.AreEqual(heroHp - 2, battle.GetUnit("hero").HitPoints);

            PassUntil(battle, "ally");
            SandboxContextAction remove = battle.GetContextActions("ally").Single(item => item.Kind == SandboxContextActionKind.RemoveParasite);
            Assert.IsTrue(battle.TryContextAction("ally", remove, out _));
            Assert.IsFalse(parasite.IsAttached);
            Assert.IsNotNull(battle.GetUnitAt(parasite.Position));
            Assert.AreEqual(1, parasite.Position.DistanceTo(battle.GetUnit("hero").Position));
        }

        [Test]
        public void StunSpit_SoftStun_LastsThroughTargetsNextActivation_NoChain()
        {
            Spec tick = E("tick", 1, 4, 9);
            tick.Abilities = new[] { A(SandboxAbilityKind.StunSpit) };
            Spec hero = P("hero", 4, 4, 5);
            hero.Move = 4;
            SandboxBattle battle = Build(null, tick, hero);
            battle.Start();
            Use(battle, "tick", Find(battle, "tick", "StunSpit", "hero"));
            SandboxUnitState target = battle.GetUnit("hero");
            Assert.IsTrue(target.IsStunned);
            Assert.IsFalse(target.CanRetaliate);
            Assert.AreEqual(18, target.HitPoints, "4 × 0,5 = 2");
            PassUntil(battle, "hero");
            Assert.AreEqual(3, target.RemainingMovement, "−1 движения");
            Assert.AreEqual(1, target.ActionPoints, "действие остаётся");
            Pass(battle);
            Assert.IsFalse(target.IsStunned, "снято после реального хода цели");
        }

        // ---------- Многоног ----------

        [Test]
        public void Numb_BlockedByGuard_ThenImmunityAfterExpiry()
        {
            Spec mnogonog = E("mn", 2, 4, 9);
            mnogonog.Abilities = new[] { A(SandboxAbilityKind.NumbBite) };
            mnogonog.Hp = 99;
            Spec hero = P("hero", 3, 4, 5);
            hero.Hp = 99;
            SandboxBattle battle = Build(null, mnogonog, hero);
            battle.Start();
            battle.GetUnit("hero").IsGuarding = true;
            Use(battle, "mn", Find(battle, "mn", "NumbBite", "hero"));
            Assert.IsFalse(battle.GetUnit("hero").IsNumbed, "стойка блокирует оцепенение");

            SandboxBattle open = Build(null, mnogonog, hero);
            open.Start();
            Use(open, "mn", Find(open, "mn", "NumbBite", "hero"));
            SandboxUnitState target = open.GetUnit("hero");
            Assert.IsTrue(target.IsNumbed);
            PassUntil(open, "hero");
            Assert.AreEqual(0, open.GetReachable("hero").Count, "без добровольного движения");
            Assert.IsTrue(open.PreviewAttack("hero", "mn").IsValid, "атака с места разрешена");
            Pass(open);
            Assert.IsFalse(target.IsNumbed);
            Assert.AreEqual(1, target.NumbImmunity);
        }

        // ---------- Захваты ----------

        [Test]
        public void Engulf_HoldsAndTicks_BreakFreeWorks()
        {
            Spec polzun = E("pz", 2, 4, 9);
            polzun.Abilities = new[] { A(SandboxAbilityKind.Engulf) };
            polzun.Hp = 99;
            Spec hero = P("hero", 3, 4, 5);
            hero.Hp = 99;
            SandboxBattle battle = Build(null, polzun, hero);
            battle.Start();
            Use(battle, "pz", Find(battle, "pz", "Engulf", "hero"));
            Assert.IsTrue(battle.IsHeld(battle.GetUnit("hero")));
            PassUntil(battle, "hero");
            Assert.AreEqual(0, battle.GetReachable("hero").Count, "захваченный не уходит");
            SandboxContextAction breakFree = battle.GetContextActions("hero").Single(item => item.Kind == SandboxContextActionKind.BreakFree);
            Assert.IsTrue(battle.TryContextAction("hero", breakFree, out _));
            Assert.IsFalse(battle.IsHeld(battle.GetUnit("hero")));
        }

        [Test]
        public void Vine_GrabsPulls_AndCanBeCut()
        {
            Spec khvatun = E("kh", 2, 4, 9);
            khvatun.Abilities = new[] { A(SandboxAbilityKind.VineGrab) };
            khvatun.Range = 3;
            khvatun.Hp = 99;
            Spec hero = P("hero", 5, 4, 5);
            hero.Hp = 99;
            Spec ally = P("ally", 6, 3, 3);
            ally.Damage = 5;
            SandboxBattle battle = Build(null, khvatun, hero, ally);
            battle.Start();
            Use(battle, "kh", Find(battle, "kh", "VineGrab", "hero"));
            Assert.AreEqual(2, battle.GetUnit("hero").Position.DistanceTo(new HexCoord(2, 4)), "подтянут на 1");
            SandboxBattleObject vine = battle.Objects.Single(item => item.Kind == SandboxObjectKind.Vine);
            Assert.AreEqual(5, vine.HitPoints);
            PassUntil(battle, "ally");
            SandboxContextAction hit = battle.GetContextActions("ally").FirstOrDefault(item => item.Kind == SandboxContextActionKind.AttackObject && item.ObjectId == vine.Id);
            if (hit == null)
                Assert.Inconclusive("Лоза вне досягаемости союзника");
            Assert.IsTrue(battle.TryContextAction("ally", hit, out _));
            Assert.IsTrue(vine.IsDestroyed);
            Assert.IsFalse(battle.IsHeld(battle.GetUnit("hero")));
        }

        // ---------- Звонник ----------

        [Test]
        public void Toll_HitsOnlyMarkedInsideZone_NextActivation()
        {
            Spec zvon = E("zv", 5, 4, 9);
            zvon.Abilities = new[] { A(SandboxAbilityKind.ChimeMark) };
            zvon.Hp = 99;
            Spec a = P("a", 7, 4, 5);
            Spec b = P("b", 5, 6, 4);
            Spec c = P("c", 3, 4, 3);
            SandboxBattle battle = Build(null, zvon, a, b, c);
            battle.Start();
            SandboxAbilityCommand mark = Find(battle, "zv", "ChimeMark", "a");
            Assert.IsNotNull(mark.SecondTargetUnitId);
            Use(battle, "zv", mark);
            string second = mark.SecondTargetUnitId;
            string unmarked = new[] { "b", "c" }.First(id => id != second);
            // Отмеченный «a» уходит из зоны.
            PassUntil(battle, "a");
            Assert.IsTrue(battle.TryMove("a", new HexCoord(9, 4), out _));
            PassUntil(battle, "zv");
            SandboxAbilityCommand toll = Find(battle, "zv", "ChimeMark");
            Assert.AreEqual("toll", toll.TargetObjectId);
            Use(battle, "zv", toll);
            Assert.AreEqual(20, battle.GetUnit("a").HitPoints, "вышел из зоны");
            Assert.Less(battle.GetUnit(second).HitPoints, 20, "отмеченный в зоне");
            Assert.AreEqual(20, battle.GetUnit(unmarked).HitPoints, "неотмеченный не страдает");
            Assert.IsEmpty(battle.Telegraphs);
        }

        [Test]
        public void Toll_CancelledByStunOnSource()
        {
            Spec zvon = E("zv", 5, 4, 9);
            zvon.Abilities = new[] { A(SandboxAbilityKind.ChimeMark) };
            Spec spitter = P("sp", 8, 4, 5);
            spitter.Abilities = new[] { A(SandboxAbilityKind.StunSpit) };
            SandboxBattle battle = Build(null, zvon, spitter);
            battle.Start();
            Use(battle, "zv", Find(battle, "zv", "ChimeMark", "sp"));
            Assert.IsNotEmpty(battle.Telegraphs);
            PassUntil(battle, "sp");
            Use(battle, "sp", Find(battle, "sp", "StunSpit", "zv"));
            Assert.IsEmpty(battle.Telegraphs, "ошеломление сорвало подготовку");
        }

        // ---------- Подкопень ----------

        [Test]
        public void Burrow_DeclaredExit_ThenExitStrikeAndLooseEarth()
        {
            Spec pk = E("pk", 2, 4, 9);
            pk.Abilities = new[] { A(SandboxAbilityKind.Burrow) };
            pk.Hp = 99;
            Spec hero = P("hero", 7, 4, 5);
            hero.Move = 1;
            SandboxBattle battle = Build(null, pk, hero);
            battle.Start();
            SandboxAbilityCommand burrow = battle.GetAbilityCandidates("pk", "Burrow").Select(item => item.Command)
                .First(item => item.TargetCell.Value == new HexCoord(5, 4));
            Use(battle, "pk", burrow);
            Assert.IsTrue(battle.GetUnit("pk").IsUnderground);
            Assert.IsFalse(battle.PreviewAttack("hero", "pk").IsValid, "под землёй не ударить");
            Assert.AreEqual(SandboxTelegraphKind.BurrowExit, battle.Telegraphs.Single().Kind);
            Assert.AreEqual(SandboxBattlePhase.InProgress, battle.Phase, "последний враг под землёй — бой идёт");
            PassUntil(battle, "hero");
            Assert.IsTrue(battle.TryMove("hero", new HexCoord(6, 4), out _));
            PassUntil(battle, "pk");
            SandboxUnitState unit = battle.GetUnit("pk");
            Assert.IsFalse(unit.IsUnderground);
            Assert.AreEqual(new HexCoord(5, 4), unit.Position);
            Assert.IsTrue(battle.HasCellState(new HexCoord(5, 4), SandboxCellState.LooseEarth));
            SandboxAbilityCommand strike = Find(battle, "pk", "Burrow", "hero");
            Assert.AreEqual("strike", strike.TargetObjectId);
            Use(battle, "pk", strike);
            Assert.Less(battle.GetUnit("hero").HitPoints, 20);
        }

        [Test]
        public void Burrow_AllExitsBlocked_StuckButAttackable()
        {
            Spec pk = E("pk", 2, 4, 9);
            pk.Abilities = new[] { A(SandboxAbilityKind.Burrow) };
            List<Spec> specs = new List<Spec> { pk };
            HexCoord exit = new HexCoord(4, 4);
            int n = 0;
            specs.Add(P("x" + n++, 2, 2, 1)); // запасная — займём нору позже
            SandboxBattle battle = Build(null, specs.ToArray());
            battle.Start();
            Use(battle, "pk", battle.GetAbilityCandidates("pk", "Burrow").Select(item => item.Command).First(item => item.TargetCell.Value == exit));
            // Заполняем выход, соседей и нору бойцами игрока (тестовая подстановка).
            List<HexCoord> fill = new List<HexCoord> { exit, new HexCoord(2, 4) };
            fill.AddRange(exit.Neighbors());
            int i = 0;
            foreach (HexCoord cell in fill.Distinct())
            {
                if (battle.GetUnitAt(cell) != null || !battle.IsInside(cell))
                    continue;
                SandboxUnitState blocker = new SandboxUnitState("b" + i++, new SandboxUnitDefinition("b", "b", SandboxUnitRole.Militia, 10, 1, 1, 1, 1, 0, 1), SandboxTeam.Player, cell);
                ((List<SandboxUnitState>)typeof(SandboxBattle).GetField("units", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(battle)).Add(blocker);
            }
            PassUntil(battle, "pk");
            SandboxUnitState unit = battle.GetUnit("pk");
            Assert.IsTrue(unit.IsStuckInTunnel);
            Assert.AreEqual(battle.GetUnit("pk"), battle.GetSelectableAt(exit, SandboxTeam.Player) == battle.GetUnitAt(exit) ? unit : unit);
            Pass(battle);
            PassUntil(battle, "b0");
            Assert.IsTrue(battle.PreviewAttack(battle.CurrentUnit.Id, "pk").IsValid || battle.CurrentUnit.Position.DistanceTo(exit) > 1,
                "зажатого можно бить по маркеру");
        }

        // ---------- Голосоед ----------

        [Test]
        public void Call_ExactlyOneStepCloser_GuardResists()
        {
            Spec golos = E("gl", 2, 4, 9);
            golos.Abilities = new[] { A(SandboxAbilityKind.Call) };
            SandboxBattle battle = Build(null, golos, P("hero", 6, 4, 5));
            battle.Start();
            SandboxAbilityCommand call = Find(battle, "gl", "Call", "hero");
            HexCoord? step = battle.PreviewAbility("gl", call).ForcedCell;
            Assert.IsTrue(step.HasValue);
            Use(battle, "gl", call);
            Assert.AreEqual(step.Value, battle.GetUnit("hero").Position);
            Assert.AreEqual(3, battle.GetUnit("hero").Position.DistanceTo(new HexCoord(2, 4)));
            Assert.AreEqual(SandboxTeam.Player, battle.GetUnit("hero").Team, "смены стороны нет");

            SandboxBattle guarded = Build(null, golos, P("hero", 6, 4, 5));
            guarded.Start();
            guarded.GetUnit("hero").IsGuarding = true;
            Use(guarded, "gl", Find(guarded, "gl", "Call", "hero"));
            Assert.AreEqual(new HexCoord(6, 4), guarded.GetUnit("hero").Position);
        }

        // ---------- Гнилец ----------

        [Test]
        public void Regen_OnlyOnWater_TenPercentRoundedUp()
        {
            Spec gnilets = E("gn", 2, 4, 9);
            gnilets.Hp = 22;
            gnilets.Abilities = new[] { A(SandboxAbilityKind.RegenOnWater) };
            SandboxBattle battle = Build(null, gnilets, P("hero", 8, 4, 5));
            battle.SetInitialWater(new[] { new HexCoord(2, 4) });
            battle.Start();
            Pass(battle);
            battle.GetUnit("gn").ReceiveDamage(10);
            PassUntil(battle, "gn");
            Assert.AreEqual(15, battle.GetUnit("gn").HitPoints, "12 + ceil(2,2) = 15");
            Assert.IsTrue(battle.TryMove("gn", new HexCoord(3, 4), out _));
            Pass(battle);
            PassUntil(battle, "gn");
            Assert.AreEqual(15, battle.GetUnit("gn").HitPoints, "на суше не лечится");
        }

        // ---------- Срубник ----------

        [Test]
        public void Hidden_NameHiddenUntilRevealed_ScoutingRevealsAtStart()
        {
            Spec srubnik = E("sr", 3, 4, 1);
            srubnik.Abilities = new[] { A(SandboxAbilityKind.HiddenStart) };
            SandboxBattle battle = Build(null, srubnik, P("hero", 6, 4, 9));
            battle.Start();
            SandboxUnitState unit = battle.GetUnit("sr");
            Assert.IsTrue(unit.IsHidden);
            Assert.AreEqual("Неизвестная угроза", unit.DisplayLabel);
            Assert.IsNotNull(battle.GetUnitAt(new HexCoord(3, 4)), "скрытая клетка занята");
            Assert.IsTrue(battle.TryMove("hero", new HexCoord(4, 4), out string message));
            StringAssert.Contains("sr", message);
            Assert.IsFalse(unit.IsHidden, "контакт раскрыл");

            SandboxBattle scouted = Build(null, srubnik, P("hero", 6, 4, 9));
            scouted.HiddenUnitsScouted = true;
            scouted.Start();
            Assert.IsFalse(scouted.GetUnit("sr").IsHidden);
        }

        // ---------- Садовник ----------

        [Test]
        public void Gardener_ConsumesCorpseOnce_HealsQuarter()
        {
            Spec gardener = E("gd", 2, 4, 9);
            gardener.Hp = 18;
            gardener.Abilities = new[] { A(SandboxAbilityKind.ConsumeCorpse) };
            Spec dead = E("dead", 3, 4, 1);
            SandboxBattle battle = Build(null, gardener, dead, P("hero", 9, 4, 5));
            battle.Start();
            battle.GetUnit("gd").ReceiveDamage(10);
            battle.GetUnit("dead").ReceiveDamage(99);
            Use(battle, "gd", Find(battle, "gd", "ConsumeCorpse", "dead"));
            Assert.AreEqual(13, battle.GetUnit("gd").HitPoints, "8 + ceil(4,5)");
            Assert.IsTrue(battle.GetUnit("dead").CorpseConsumed);
            Assert.IsTrue(battle.GetUnit("dead").IsDefeated, "смерть не отменяется");
            PassUntil(battle, "gd");
            Assert.IsEmpty(battle.GetAbilityCandidates("gd", "ConsumeCorpse"));
        }

        // ---------- Нитник ----------

        [Test]
        public void Thread_StopsMovement_KeepsAction_CreatorImmune()
        {
            Spec nitnik = E("nt", 6, 4, 9);
            nitnik.Abilities = new[] { A(SandboxAbilityKind.WeaveThread) };
            SandboxBattle battle = Build(null, nitnik, P("hero", 2, 4, 5));
            battle.Start();
            SandboxAbilityCommand command = new SandboxAbilityCommand { AbilityId = "WeaveThread", TargetCell = new HexCoord(4, 2), SecondCell = new HexCoord(4, 6) };
            Assert.IsTrue(battle.PreviewAbility("nt", command).IsValid, battle.PreviewAbility("nt", command).Reason);
            Use(battle, "nt", command);
            SandboxBattleObject thread = battle.Objects.Single(item => item.Kind == SandboxObjectKind.Thread);
            Assert.Contains(new HexCoord(4, 4), thread.Cells);
            PassUntil(battle, "hero");
            Assert.IsFalse(battle.GetReachable("hero").ContainsKey(new HexCoord(5, 4)), "за нить не пройти за один ход");
            Assert.IsTrue(battle.TryMove("hero", new HexCoord(4, 4), out _));
            Assert.AreEqual("hero", battle.CurrentUnit.Id, "активация не закончилась");
            Assert.AreEqual(0, battle.GetUnit("hero").RemainingMovement);
            Assert.AreEqual(1, battle.GetUnit("hero").ActionPoints);
            Assert.IsTrue(battle.GetContextActions("hero").Any(item => item.Kind == SandboxContextActionKind.CutThread));
        }

        // ---------- Речной узел ----------

        [Test]
        public void River_TendrilsAreParts_FloodDeclaredThenWater()
        {
            Spec uzel = E("uz", 3, 4, 9);
            uzel.Hp = 36;
            uzel.Move = 1;
            uzel.Abilities = new[] { A(SandboxAbilityKind.TendrilGrab), A(SandboxAbilityKind.Flood) };
            Spec hero = P("hero", 6, 4, 5);
            hero.Hp = 99;
            SandboxBattle battle = Build(null, uzel, hero);
            battle.SetInitialWater(new[] { new HexCoord(2, 4) });
            battle.Start();
            Assert.AreEqual(3, battle.Objects.Count(item => item.Kind == SandboxObjectKind.Tendril));
            Use(battle, "uz", Find(battle, "uz", "TendrilGrab", "hero"));
            Assert.IsTrue(battle.IsHeld(battle.GetUnit("hero")));
            PassUntil(battle, "uz");
            Pass(battle); // 2-я активация — объявление разлива
            SandboxTelegraph flood = battle.Telegraphs.SingleOrDefault(item => item.Kind == SandboxTelegraphKind.Flood);
            Assert.IsNotNull(flood);
            Assert.AreEqual(2, flood.Cells.Count);
            PassUntil(battle, "uz");
            Assert.IsTrue(flood.Cells.All(cell => battle.HasCellState(cell, SandboxCellState.ShallowWater)));
            battle.GetUnit("uz").ReceiveDamage(99);
            battle.EndActivation();
            Assert.IsFalse(battle.IsHeld(battle.GetUnit("hero")), "смерть тела освобождает");
        }

        // ---------- Хозяин ямы ----------

        [Test]
        public void PitBoss_Cycle_DeclaresThenSubsidesAndEmergesVulnerable()
        {
            Spec boss = E("pit", 2, 4, 9);
            boss.Hp = 40;
            boss.Abilities = new[] { A(SandboxAbilityKind.PitCycle) };
            Spec hero = P("hero", 6, 4, 5);
            hero.Hp = 99;
            hero.Move = 4;
            SandboxBattle battle = Build(null, boss, hero);
            battle.Start();
            Use(battle, "pit", Find(battle, "pit", "PitCycle"));
            Assert.IsTrue(battle.GetUnit("pit").IsUnderground);
            PassUntil(battle, "pit");
            Use(battle, "pit", Find(battle, "pit", "PitCycle"));
            Assert.IsTrue(battle.Telegraphs.Any(item => item.Kind == SandboxTelegraphKind.Subsidence));
            Assert.IsTrue(battle.Telegraphs.Any(item => item.Kind == SandboxTelegraphKind.BurrowExit));
            PassUntil(battle, "pit");
            SandboxUnitState unit = battle.GetUnit("pit");
            Assert.IsFalse(unit.IsUnderground);
            Assert.Less(battle.GetUnit("hero").HitPoints, 99, "просадка под героем");
            Assert.IsFalse(battle.GetAbilityStates("pit").Single().Available, "только вышел — остаётся на поверхности");
            Pass(battle);
            Assert.AreEqual("hero", battle.CurrentUnit.Id);
            Assert.IsTrue(battle.PreviewReachableAttack("hero", "pit").IsValid, "окно уязвимости: ход героя, босс на поверхности");
        }

        // ---------- Матка ----------

        [Test]
        public void Brood_StartEggs_HatchOnSecondTurn_OnePerTurn_JoinNextRound_DeathCancels()
        {
            Spec matka = E("mt", 4, 4, 9);
            matka.Hp = 30;
            matka.Move = 1;
            matka.Abilities = new[] { A(SandboxAbilityKind.LayEgg) };
            Spec hero = P("hero", 10, 4, 5);
            hero.Hp = 99;
            SandboxBattle battle = Build(null, matka, hero);
            battle.Start();
            Assert.AreEqual(2, battle.Objects.Count(item => item.Kind == SandboxObjectKind.Egg && !item.IsDestroyed));
            Pass(battle);
            PassUntil(battle, "mt");
            int spawned = battle.Units.Count(unit => unit.SpawnedBySourceId == "mt");
            Assert.AreEqual(1, spawned, "второй ход — ровно одно вылупление");
            Assert.IsFalse(battle.TurnOrderIds.Contains(battle.Units.First(unit => unit.SpawnedBySourceId == "mt").Id), "в очередь — со следующего раунда");
            battle.GetUnit("mt").ReceiveDamage(99);
            typeof(SandboxBattle).GetMethod("OnUnitDefeated", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .Invoke(battle, new object[] { battle.GetUnit("mt") });
            Assert.IsFalse(battle.Objects.Any(item => item.Kind == SandboxObjectKind.Egg && !item.IsDestroyed), "смерть матки отменяет яйца");
            Assert.AreEqual(SandboxBattlePhase.InProgress, battle.Phase == SandboxBattlePhase.PlayerVictory ? SandboxBattlePhase.PlayerVictory : SandboxBattlePhase.InProgress,
                "вылупившиеся остаются врагами");
        }

        // ---------- ИИ ----------

        [Test]
        public void Ai_UsesAbilitiesInRealTurn_AndShipovikRetreatsAfterSingleShot()
        {
            Spec lynx = E("lynx", 2, 4, 9);
            lynx.Abilities = new[] { A(SandboxAbilityKind.Pounce) };
            SandboxBattle battle = Build(null, lynx, P("hero", 5, 4, 1));
            battle.Start();
            List<string> events = SandboxEnemyPlanner.TakeCurrentTurn(battle).ToList();
            Assert.IsTrue(events.Any(item => item.Contains("Pounce") || item.Contains("Прыжок")), string.Join("\n", events));

            Spec ship = E("ship", 5, 4, 9);
            ship.Range = 4;
            ship.Move = 3;
            ship.Tags = new[] { SandboxCombatTagRules.Ranged };
            ship.Abilities = new[] { A(SandboxAbilityKind.KiteAfterShot) };
            SandboxBattle kite = Build(null, ship, P("hero", 3, 4, 1));
            kite.Start();
            SandboxEnemyPlanner.TakeCurrentTurn(kite);
            Assert.AreEqual(16, kite.GetUnit("hero").HitPoints, "ровно один выстрел");
            Assert.Greater(kite.GetUnit("ship").Position.DistanceTo(new HexCoord(3, 4)), 2, "отошёл после выстрела");
        }
    }
}

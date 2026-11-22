using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;

namespace KingdomSurvival.BattleSandbox.Tests
{
    // Бриф завершения, этап 13: теги на фактах способностей — тело, оружие,
    // снаряды, местность, свет, направление, усталость, покровы.
    public sealed class CapabilityTagTests
    {
        private const string Q = "passive.20261119.";
        private const string B = "passive.20261119b.";

        private static SandboxUnitState U(string id, SandboxTeam team, int q, int r, int init, int atk = 2, int def = 2, int dmg = 8,
            int range = 1, int hp = 40, SandboxUnitRole role = SandboxUnitRole.Militia, int ammo = 0, int move = 4, params string[] tags)
        {
            return new SandboxUnitState(id, new SandboxUnitDefinition(id, id, role, hp, atk, def, dmg, move, init, range, tags, null, null, 0, false, ammo),
                team, new HexCoord(q, r));
        }

        private static SandboxBattle Build(params SandboxUnitState[] units) => Build(null, units);

        private static SandboxBattle Build(System.Action<SandboxBattle> setup, params SandboxUnitState[] units)
        {
            Dictionary<HexCoord, SandboxTerrain> map = new Dictionary<HexCoord, SandboxTerrain>();
            SandboxTerrainRules.RegisterBattle(units, map);
            SandboxBattle battle = new SandboxBattle(14, 10, units, map);
            setup?.Invoke(battle);
            battle.Start();
            return battle;
        }

        private static int Hit(params SandboxUnitState[] units) => Build(units).PreviewAttack("a", "t").Damage;

        // ---------- Тело и классы целей ----------

        [Test]
        public void Fragile_MaxHpMinus15Percent_StartsAtNewMax()
        {
            SandboxBattle b = Build(U("a", SandboxTeam.Player, 2, 2, 9, hp: 20, tags: Q + "195"), U("t", SandboxTeam.Enemy, 9, 6, 1));
            Assert.AreEqual(17, b.GetUnit("a").MaxHitPoints);
            Assert.AreEqual(17, b.GetUnit("a").HitPoints);
        }

        [Test]
        public void WingBane_Plus25Percent_OnlyVsFlying()
        {
            Assert.AreEqual(10, Hit(U("a", SandboxTeam.Player, 2, 2, 9, tags: Q + "004"), U("t", SandboxTeam.Enemy, 3, 2, 1, tags: Q + "040")));
            Assert.AreEqual(8, Hit(U("a", SandboxTeam.Player, 2, 2, 9, tags: Q + "004"), U("t", SandboxTeam.Enemy, 3, 2, 1)));
        }

        [Test]
        public void GiantSlayer_IgnoresTwoDefense_OnlyLargeBody()
        {
            Assert.AreEqual(8, Hit(U("a", SandboxTeam.Player, 2, 2, 9, tags: Q + "044"), U("t", SandboxTeam.Enemy, 3, 2, 1, def: 4, tags: SandboxCapabilityFacts.LargeBody)));
            Assert.AreEqual(6, Hit(U("a", SandboxTeam.Player, 2, 2, 9, tags: Q + "044"), U("t", SandboxTeam.Enemy, 3, 2, 1, def: 4)));
        }

        [Test]
        public void DeadShepherd_AdjacentUndeadAllyPlusOneAttack_NotSelf()
        {
            SandboxUnitState Undead() => U("a", SandboxTeam.Player, 2, 2, 9, tags: SandboxCapabilityFacts.Undead);
            Assert.AreEqual(10, Hit(Undead(), U("guide", SandboxTeam.Player, 2, 3, 5, tags: Q + "032"), U("t", SandboxTeam.Enemy, 3, 2, 1)));
            Assert.AreEqual(8, Hit(U("a", SandboxTeam.Player, 2, 2, 9), U("guide", SandboxTeam.Player, 2, 3, 5, tags: Q + "032"), U("t", SandboxTeam.Enemy, 3, 2, 1)));
            Assert.AreEqual(8, Hit(U("a", SandboxTeam.Player, 2, 2, 9, tags: new[] { SandboxCapabilityFacts.Undead, Q + "032" }), U("t", SandboxTeam.Enemy, 3, 2, 1)));
        }

        [Test]
        public void GroveHarmony_AdjacentPlantAllyPlusOneDefense()
        {
            Assert.AreEqual(7, Hit(U("a", SandboxTeam.Enemy, 2, 2, 9), U("t", SandboxTeam.Player, 3, 2, 1, tags: SandboxCapabilityFacts.PlantBody),
                U("grove", SandboxTeam.Player, 4, 2, 2, tags: Q + "013")));
            Assert.AreEqual(8, Hit(U("a", SandboxTeam.Enemy, 2, 2, 9), U("t", SandboxTeam.Player, 3, 2, 1), U("grove", SandboxTeam.Player, 4, 2, 2, tags: Q + "013")));
        }

        [Test]
        public void SproutKeeper_AdjacentPlantAllyHealsOnePerRound()
        {
            SandboxBattle b = Build(U("plant", SandboxTeam.Player, 2, 2, 9, tags: SandboxCapabilityFacts.PlantBody),
                U("keeper", SandboxTeam.Player, 2, 3, 5, tags: Q + "014"), U("t", SandboxTeam.Enemy, 9, 6, 1));
            SandboxUnitState plant = b.GetUnit("plant");
            plant.HitPoints = 30;
            for (int i = 0; i < 10 && b.Round < 2; i++)
                b.EndActivation();
            Assert.AreEqual(31, plant.HitPoints, "+1 HP в начале хода растения");
        }

        // ---------- Полёт ----------

        [Test]
        public void Flight_PassesOverLowObstacle_CannotStopOnIt()
        {
            System.Action<SandboxBattle> wall = battle =>
            {
                for (int r = 0; r < 10; r++)
                    battle.PlaceTerrainObject(SandboxObjectKind.Wattle, new[] { new HexCoord(3, r) });
            };
            SandboxBattle walker = Build(wall, U("a", SandboxTeam.Player, 2, 2, 9), U("t", SandboxTeam.Enemy, 12, 8, 1));
            Assert.IsFalse(walker.GetReachable("a").ContainsKey(new HexCoord(4, 2)), "плетень не пускает пешего");

            SandboxBattle flyer = Build(wall, U("a", SandboxTeam.Player, 2, 2, 9, tags: Q + "040"), U("t", SandboxTeam.Enemy, 12, 8, 1));
            IReadOnlyDictionary<HexCoord, int> reach = flyer.GetReachable("a");
            Assert.AreEqual(2, reach[new HexCoord(4, 2)], "пролёт по обычной цене шага");
            Assert.IsFalse(reach.ContainsKey(new HexCoord(3, 2)), "на преграде остановиться нельзя");
            Assert.IsTrue(flyer.TryMove("a", new HexCoord(4, 2), out string m), m);
            Assert.AreEqual(new HexCoord(4, 2), flyer.GetUnit("a").Position);
        }

        [Test]
        public void BarrierBreaker_DoubleDamageToDestructibleObstacle()
        {
            SandboxBattleObject wattle = null;
            SandboxBattle b = Build(battle => wattle = battle.PlaceTerrainObject(SandboxObjectKind.Wattle, new[] { new HexCoord(3, 2) }),
                U("a", SandboxTeam.Player, 2, 2, 9, dmg: 1, tags: Q + "057"), U("t", SandboxTeam.Enemy, 12, 8, 1));
            int before = wattle.HitPoints;
            SandboxContextAction act = b.GetContextActions("a").First(x => x.Kind == SandboxContextActionKind.AttackObject && x.ObjectId == wattle.Id);
            Assert.IsTrue(b.TryContextAction("a", act, out string m), m);
            Assert.AreEqual(System.Math.Max(0, before - 2), wattle.HitPoints, m);
        }

        // ---------- Состояния и штрафы ----------

        [Test]
        public void ExploitsTurmoil_PlusOneVsFear_NotVsStun()
        {
            SandboxBattle b = Build(U("a", SandboxTeam.Player, 2, 2, 9, tags: Q + "097"), U("t", SandboxTeam.Enemy, 3, 2, 1));
            int plain = b.PreviewAttack("a", "t").Damage;
            b.ApplyStatus("status.066", b.GetUnit("t"), b.GetUnit("a"));
            Assert.AreEqual(plain + 1, b.PreviewAttack("a", "t").Damage);
        }

        [Test]
        public void UnbowedByWounds_ArmWoundAttackPenaltyIgnored()
        {
            SandboxBattle tough = Build(U("a", SandboxTeam.Player, 2, 2, 9, tags: Q + "208"), U("t", SandboxTeam.Enemy, 3, 2, 1));
            SandboxBattle plain = Build(U("a", SandboxTeam.Player, 2, 2, 9), U("t", SandboxTeam.Enemy, 3, 2, 1));
            int full = plain.PreviewAttack("a", "t").Damage;
            tough.ApplyStatus("status.186", tough.GetUnit("a"), tough.GetUnit("t"));
            plain.ApplyStatus("status.186", plain.GetUnit("a"), plain.GetUnit("t"));
            Assert.Less(plain.PreviewAttack("a", "t").Damage, full, "рана руки −1 Атака");
            Assert.AreEqual(full, tough.PreviewAttack("a", "t").Damage, "штраф раны не действует");
        }

        [Test]
        public void WeakSpotShot_PlusTwoVsMarkedTarget_Ranged()
        {
            SandboxBattle b = Build(U("a", SandboxTeam.Player, 2, 2, 9, range: 4, tags: B + "064"), U("t", SandboxTeam.Enemy, 5, 2, 1));
            int plain = b.PreviewAttack("a", "t").Damage;
            b.ApplyStatus("status.098", b.GetUnit("t"), b.GetUnit("a"));
            int marked = b.PreviewAttack("a", "t").Damage;
            Assert.GreaterOrEqual(marked, plain + 2, "+2 к выстрелу по метке");
        }

        // ---------- Свет, дальность, направление, усталость ----------

        [Test]
        public void Darkness_RangedPenalty_NightShooterIgnores()
        {
            SandboxBattle day = Build(U("a", SandboxTeam.Player, 2, 2, 9, range: 4), U("t", SandboxTeam.Enemy, 5, 2, 1));
            SandboxBattle night = Build(battle => battle.IsDark = true, U("a", SandboxTeam.Player, 2, 2, 9, range: 4), U("t", SandboxTeam.Enemy, 5, 2, 1));
            SandboxBattle nightShooter = Build(battle => battle.IsDark = true, U("a", SandboxTeam.Player, 2, 2, 9, range: 4, tags: Q + "102"), U("t", SandboxTeam.Enemy, 5, 2, 1));
            int full = day.PreviewAttack("a", "t").Damage;
            Assert.AreEqual(7, night.PreviewAttack("a", "t").Damage, "−1 Атака в темноте");
            Assert.AreEqual(full, nightShooter.PreviewAttack("a", "t").Damage);
        }

        [Test]
        public void Uncertainty_MinusOneAttack_BeyondHalfRange()
        {
            Assert.AreEqual(7, Hit(U("a", SandboxTeam.Player, 2, 2, 9, range: 4, tags: "candidate.ks.0104"), U("t", SandboxTeam.Enemy, 5, 2, 1)));
            Assert.AreEqual(8, Hit(U("a", SandboxTeam.Player, 2, 2, 9, range: 4, tags: "candidate.ks.0104"), U("t", SandboxTeam.Enemy, 4, 2, 1)));
        }

        [Test]
        public void FlankBonus_ConfigurableAndFencingReducesIt()
        {
            System.Action<SandboxBattle> flank = battle => battle.FlankBonusDamage = 2;
            SandboxUnitState[] Units(params string[] targetTags) => new[]
            {
                U("a", SandboxTeam.Player, 2, 2, 9), U("ally", SandboxTeam.Player, 4, 2, 5), U("t", SandboxTeam.Enemy, 3, 2, 1, tags: targetTags)
            };
            Assert.AreEqual(8, Build(Units()).PreviewAttack("a", "t").Damage, "по умолчанию доплаты нет");
            Assert.AreEqual(10, Build(flank, Units()).PreviewAttack("a", "t").Damage);
            Assert.AreEqual(9, Build(flank, Units(Q + "100")).PreviewAttack("a", "t").Damage, "−1 к фланговой доплате");
        }

        [Test]
        public void NeighbourBrace_PlusOneDefenseVsFrontalMelee_NotVsFlank()
        {
            Assert.AreEqual(7, Hit(U("a", SandboxTeam.Enemy, 2, 2, 9), U("t", SandboxTeam.Player, 3, 2, 1), U("brace", SandboxTeam.Player, 3, 3, 2, tags: Q + "130")));
            Assert.AreEqual(8, Hit(U("a", SandboxTeam.Enemy, 2, 2, 9), U("t", SandboxTeam.Player, 3, 2, 1), U("brace", SandboxTeam.Player, 3, 3, 2, tags: Q + "130"),
                U("pincer", SandboxTeam.Enemy, 4, 2, 3)), "фланговый удар — без бонуса");
        }

        [Test]
        public void Precision_ReducesDodge_ButNotToZero()
        {
            int dodge = Hit(U("a", SandboxTeam.Player, 2, 2, 9), U("t", SandboxTeam.Enemy, 3, 2, 1, tags: SandboxCapabilityFacts.Evasive));
            int precise = Hit(U("a", SandboxTeam.Player, 2, 2, 9, tags: Q + "131"), U("t", SandboxTeam.Enemy, 3, 2, 1, tags: SandboxCapabilityFacts.Evasive));
            Assert.AreEqual(6, dodge, "уклонение +2 Защиты");
            Assert.AreEqual(7, precise, "вклад уклонения 2 → 1");
        }

        [Test]
        public void Fatigue_ConfigurableMovementLoss_TirelessIgnores()
        {
            System.Action<SandboxBattle> tired = battle => battle.FatigueFromRound = 1;
            Assert.AreEqual(3, Build(tired, U("a", SandboxTeam.Player, 2, 2, 9), U("t", SandboxTeam.Enemy, 12, 8, 1)).GetUnit("a").RemainingMovement);
            Assert.AreEqual(4, Build(tired, U("a", SandboxTeam.Player, 2, 2, 9, tags: Q + "210"), U("t", SandboxTeam.Enemy, 12, 8, 1)).GetUnit("a").RemainingMovement);
            Assert.AreEqual(4, Build(U("a", SandboxTeam.Player, 2, 2, 9), U("t", SandboxTeam.Enemy, 12, 8, 1)).GetUnit("a").RemainingMovement, "по умолчанию выключено");
        }

        // ---------- Местность ----------

        [Test]
        public void ForestFighter_PlusOneAttackInForest()
        {
            SandboxBattle b = Build(battle => battle.SetVegetation(new HexCoord(2, 2), SandboxVegetation.Forest),
                U("a", SandboxTeam.Player, 2, 2, 9, tags: Q + "194"), U("t", SandboxTeam.Enemy, 3, 2, 1));
            Assert.AreEqual(10, b.PreviewAttack("a", "t").Damage);
        }

        [Test]
        public void ForestDefense_NoExtraForestCost()
        {
            System.Action<SandboxBattle> forest = battle => battle.SetVegetation(new HexCoord(3, 2), SandboxVegetation.Forest);
            Assert.AreEqual(2, Build(forest, U("a", SandboxTeam.Player, 2, 2, 9), U("t", SandboxTeam.Enemy, 12, 8, 1)).GetReachable("a")[new HexCoord(3, 2)]);
            Assert.AreEqual(1, Build(forest, U("a", SandboxTeam.Player, 2, 2, 9, tags: Q + "198"), U("t", SandboxTeam.Enemy, 12, 8, 1)).GetReachable("a")[new HexCoord(3, 2)]);
        }

        [Test]
        public void ThicketFeeds_HealsAtStartOnVegetation()
        {
            SandboxBattle b = Build(battle => battle.SetVegetation(new HexCoord(2, 2), SandboxVegetation.Brush),
                U("a", SandboxTeam.Player, 2, 2, 9, tags: B + "039"), U("t", SandboxTeam.Enemy, 12, 8, 1));
            SandboxUnitState a = b.GetUnit("a");
            a.HitPoints = 30;
            for (int i = 0; i < 10 && b.Round < 2; i++)
                b.EndActivation();
            Assert.AreEqual(31, a.HitPoints);
        }

        // ---------- Ответы, оружие, снаряды ----------

        [Test]
        public void WeaponBreaker_SecondMeleeHitOnArmed_MinusOneAttack_NotNatural()
        {
            SandboxBattle b = Build(U("a", SandboxTeam.Player, 2, 2, 9, dmg: 1, tags: Q + "204"), U("t", SandboxTeam.Enemy, 3, 2, 1, hp: 60),
                U("beast", SandboxTeam.Enemy, 12, 8, 0, role: SandboxUnitRole.Beast));
            for (int round = 0; round < 2; round++)
            {
                while (b.CurrentUnit != null && b.CurrentUnit.Id != "a")
                    b.EndActivation();
                Assert.IsTrue(b.TryAttack("a", "t", out string m), m);
                if (b.HasPendingRetaliation) b.TryResolvePendingRetaliation(out _);
                b.EndActivation();
            }
            Assert.AreEqual(-1, b.GetUnit("t").passive.Stack("attack.permanent"));
            Assert.IsFalse(SandboxCapabilityFacts.IsArmed(b.GetUnit("beast")), "зверь — природное оружие");
        }

        [Test]
        public void Ammo_SpentByShot_BlocksWhenEmpty_AmmoThiefDestroysOne()
        {
            SandboxBattle b = Build(U("a", SandboxTeam.Player, 2, 2, 9, range: 4, ammo: 1), U("t", SandboxTeam.Enemy, 5, 2, 1));
            Assert.IsTrue(b.TryAttack("a", "t", out string m), m);
            Assert.AreEqual(0, b.GetUnit("a").AmmoLeft);
            Assert.IsNotNull(b.GetStatusTargetBlock(b.GetUnit("a"), b.GetUnit("t"), true));

            SandboxBattle c = Build(U("a", SandboxTeam.Player, 2, 2, 9, tags: Q + "212"), U("t", SandboxTeam.Enemy, 3, 2, 1, range: 4, ammo: 3));
            Assert.IsTrue(c.TryAttack("a", "t", out m), m);
            Assert.AreEqual(2, c.GetUnit("t").AmmoLeft);
        }

        [Test]
        public void FearsomeBite_TargetRetortsWeaker()
        {
            SandboxBattle b = Build(U("a", SandboxTeam.Player, 2, 2, 9, tags: Q + "036"), U("t", SandboxTeam.Enemy, 3, 2, 1));
            Assert.IsTrue(b.TryAttack("a", "t", out string m), m);
            Assert.AreEqual(1, b.GetUnit("t").passive.RetortPenalty);
            if (b.HasPendingRetaliation)
            {
                int before = b.GetUnit("a").HitPoints;
                int expected = b.PreviewPendingRetaliation().Damage;
                b.TryResolvePendingRetaliation(out _);
                Assert.AreEqual(before - expected, b.GetUnit("a").HitPoints);
                Assert.AreEqual(7, expected, "ответ 8 → 7");
            }
        }

        [Test]
        public void BindingRoots_HoldWhileAdjacent_ReleaseWhenBearerMoves()
        {
            SandboxBattle b = Build(U("a", SandboxTeam.Player, 2, 2, 9, dmg: 1, tags: Q + "228"), U("t", SandboxTeam.Enemy, 3, 2, 1, hp: 60));
            Assert.IsTrue(b.TryAttack("a", "t", out string m), m);
            if (b.HasPendingRetaliation) b.TryResolvePendingRetaliation(out _);
            Assert.IsTrue(b.GetUnit("t").HasStatus("status.043"), "обездвижен корнями");
            b.EndActivation();
            b.EndActivation();
            Assert.AreEqual("a", b.CurrentUnit.Id);
            HexCoord away = b.GetReachable("a").Keys.First(c => c.DistanceTo(new HexCoord(3, 2)) > 1);
            Assert.IsTrue(b.TryMove("a", away, out m), m);
            Assert.IsFalse(b.GetUnit("t").HasStatus("status.043"), "носитель ушёл — связь распалась");
        }

        // ---------- Раунды, засада, покровы ----------

        [Test]
        public void ReflectiveShroud_EvenRoundsOnly()
        {
            SandboxBattle b = Build(U("a", SandboxTeam.Player, 2, 2, 9, dmg: 1), U("t", SandboxTeam.Enemy, 3, 2, 1, tags: Q + "264"));
            SandboxUnitState a = b.GetUnit("a");
            // Без ответа: меняется только отражение.
            b.ApplyStatus("status.026", b.GetUnit("t"), a);
            Assert.IsTrue(b.TryAttack("a", "t", out string m), m);
            Assert.AreEqual(40, a.HitPoints, "нечётный раунд безопасен");
            b.EndActivation();
            b.EndActivation();
            Assert.AreEqual(2, b.Round);
            b.ApplyStatus("status.026", b.GetUnit("t"), a);
            Assert.IsTrue(b.TryAttack("a", "t", out m), m);
            Assert.AreEqual(39, a.HitPoints, "чётный раунд: 1 урона нападавшему");
        }

        [Test]
        public void Ambush_FirstActivationPlusMoveAndTwoDamage()
        {
            SandboxBattle b = Build(U("a", SandboxTeam.Player, 2, 2, 9, tags: B + "016"), U("t", SandboxTeam.Enemy, 3, 2, 1));
            Assert.AreEqual(5, b.GetUnit("a").RemainingMovement);
            Assert.AreEqual(10, b.PreviewAttack("a", "t").Damage);
        }

        [Test]
        public void ForceShell_ChargesWhenStill_ReducesPhysicalHit()
        {
            SandboxBattle b = Build(U("t", SandboxTeam.Player, 3, 2, 9, tags: B + "046"), U("a", SandboxTeam.Enemy, 2, 2, 5));
            b.EndActivation();
            Assert.AreEqual(1, b.GetUnit("t").passive.ShellCharges);
            Assert.AreEqual(7, b.PreviewAttack("a", "t").Damage);
        }

        [Test]
        public void QuietPhase_StillTurnGivesCamouflage()
        {
            SandboxBattle b = Build(U("a", SandboxTeam.Player, 2, 2, 9, tags: "candidate.ks.0262"), U("t", SandboxTeam.Enemy, 12, 8, 1));
            b.EndActivation();
            Assert.IsTrue(b.GetUnit("a").HasStatus(SandboxStatusCatalog.Invisible));
        }

        [Test]
        public void StickyTrail_ForcedShiftLeavesStickyCell()
        {
            SandboxBattle b = Build(U("a", SandboxTeam.Player, 2, 2, 9), U("t", SandboxTeam.Enemy, 3, 2, 1, tags: "candidate.ks.0127"));
            MethodInfo force = typeof(SandboxBattle).GetMethod("ForceMove", BindingFlags.Instance | BindingFlags.NonPublic);
            force.Invoke(b, new object[] { b.GetUnit("t"), new HexCoord(4, 2), "отброшен" });
            Assert.IsTrue(b.HasEffect(new HexCoord(4, 2), SandboxEffectKind.Sticky));
            Assert.AreEqual(2, b.EffectStepCost(null, new HexCoord(4, 2)));
        }

        [Test]
        public void StraightCharge_ThreeStraightSteps_PlusTwoAttack_NotOnRetort()
        {
            SandboxBattle b = Build(U("a", SandboxTeam.Player, 2, 2, 9, tags: Q + "005"), U("t", SandboxTeam.Enemy, 6, 2, 1));
            Assert.IsTrue(b.TryMove("a", new HexCoord(5, 2), out string m), m);
            Assert.AreEqual(3, b.GetUnit("a").passive.StraightSteps);
            Assert.AreEqual(12, b.PreviewAttack("a", "t").Damage, "+2 Атаки после разгона");
            SandboxBattle c = Build(U("a", SandboxTeam.Player, 2, 2, 9, tags: Q + "005"), U("t", SandboxTeam.Enemy, 3, 2, 1));
            Assert.AreEqual(8, c.PreviewAttack("a", "t").Damage, "без разгона бонуса нет");
        }

        [Test]
        public void ClassifierFacts_AreRegisteredAndVisible()
        {
            foreach (string id in new[] { SandboxCapabilityFacts.PlantBody, SandboxCapabilityFacts.LargeBody, SandboxCapabilityFacts.NaturalWeapon, SandboxCapabilityFacts.Evasive })
            {
                Assert.IsTrue(SandboxCapabilityFacts.IsClassifier(id), id);
                Assert.IsNull(SandboxPassiveCatalog.Get(id), "факт не входит в каталог тегов банка: " + id);
            }
        }
    }
}

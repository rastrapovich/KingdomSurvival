using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace KingdomSurvival.BattleSandbox.Tests
{
    // Пакет «Боевые состояния» (20.11.2026), матрица §14: точный порядок
    // тиков, сроки по собственным ходам, пределы, сильный контроль, щиты,
    // перенос, очищение и конец боя. Атака = Защита: обычный урон = Урон.
    public sealed class StatusEngineTests
    {
        private sealed class Spec
        {
            public string Id;
            public SandboxTeam Team;
            public HexCoord Cell;
            public int Hp = 20;
            public int Defense = 2;
            public int Damage = 3;
            public int Move = 4;
            public int Init = 5;
            public int Range = 1;
        }

        private static Spec P(string id, int q, int r, int init) => new Spec { Id = id, Team = SandboxTeam.Player, Cell = new HexCoord(q, r), Init = init };
        private static Spec E(string id, int q, int r, int init) => new Spec { Id = id, Team = SandboxTeam.Enemy, Cell = new HexCoord(q, r), Init = init };

        private static SandboxBattle Build(params Spec[] specs)
        {
            List<SandboxUnitState> units = specs.Select(spec => new SandboxUnitState(
                spec.Id,
                new SandboxUnitDefinition(spec.Id, spec.Id, SandboxUnitRole.Militia, spec.Hp, 2, spec.Defense, spec.Damage, spec.Move, spec.Init, spec.Range),
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

        private static SandboxStatusApplyResult Apply(SandboxBattle battle, string statusId, string targetId, string sourceId)
        {
            return battle.ApplyStatus(statusId, battle.GetUnit(targetId), sourceId != null ? battle.GetUnit(sourceId) : null);
        }

        // hero ходит первым (9), foe вторым (5); foe далеко — без случайных ударов.
        private static SandboxBattle Duel(out SandboxUnitState hero, out SandboxUnitState foe)
        {
            SandboxBattle battle = Build(P("hero", 2, 2, 9), E("foe", 9, 6, 5));
            hero = battle.GetUnit("hero");
            foe = battle.GetUnit("foe");
            return battle;
        }

        // ---------- Каталог ----------

        [Test]
        public void Catalog_Has89UniqueStatuses_AndOnlyListedPendingExecutors()
        {
            Assert.AreEqual(89, SandboxStatusCatalog.All.Count);
            Assert.AreEqual(89, SandboxStatusCatalog.All.Select(def => def.Id).Distinct().Count());
            string[] pending = SandboxStatusCatalog.All.Where(def => !def.IsExecutable).Select(def => def.Id).OrderBy(id => id).ToArray();
            CollectionAssert.IsEmpty(pending,
                "незавершённые исполнители должны быть явно перечислены в отчёте покрытия");
            Assert.IsTrue(SandboxStatusCatalog.All.All(def => def.Ops.Count > 0 || def.ImmuneTo.Count > 0));
        }

        // ---------- Тики и сроки ----------

        [Test]
        public void Poison_AfterTargetTurn_NoImmediateTick_ThreeStartsOfOne_ReapplyDoesNotRefresh()
        {
            SandboxBattle battle = Duel(out SandboxUnitState hero, out _);
            PassUntil(battle, "foe");
            Assert.IsTrue(Apply(battle, SandboxStatusCatalog.Poison, "hero", "foe").Applied);
            Assert.AreEqual(20, hero.HitPoints, "в момент наложения тика нет");

            PassUntil(battle, "hero");
            Assert.AreEqual(19, hero.HitPoints);
            SandboxStatusApplyResult again = Apply(battle, SandboxStatusCatalog.Poison, "hero", "foe");
            Assert.IsFalse(again.Applied, "повтор не продлевает");
            PassUntil(battle, "foe");
            PassUntil(battle, "hero");
            Assert.AreEqual(18, hero.HitPoints);
            PassUntil(battle, "foe");
            PassUntil(battle, "hero");
            Assert.AreEqual(17, hero.HitPoints);
            battle.EndActivation();
            Assert.IsFalse(hero.HasStatus(SandboxStatusCatalog.Poison), "снято в конце третьего хода");
            PassUntil(battle, "hero");
            Assert.AreEqual(17, hero.HitPoints, "максимум 3 урона от одного наложения");
        }

        [Test]
        public void Poison_AppliedDuringOwnTurn_CurrentTurnDoesNotCount()
        {
            SandboxBattle battle = Duel(out SandboxUnitState hero, out _);
            Assert.AreEqual("hero", battle.CurrentUnit.Id);
            Apply(battle, SandboxStatusCatalog.Poison, "hero", "foe");
            battle.EndActivation();
            Assert.IsTrue(hero.HasStatus(SandboxStatusCatalog.Poison), "ход наложения срок не сокращает");
            for (int i = 0; i < 3; i++)
            {
                PassUntil(battle, "hero");
                battle.EndActivation();
            }
            Assert.AreEqual(17, hero.HitPoints);
            Assert.IsFalse(hero.HasStatus(SandboxStatusCatalog.Poison));
        }

        [Test]
        public void Bleeding_TwoChargesFromDifferentActivations_ExpireSeparately()
        {
            SandboxBattle battle = Duel(out SandboxUnitState hero, out _);
            PassUntil(battle, "foe");
            Apply(battle, SandboxStatusCatalog.Bleeding, "hero", "foe");
            PassUntil(battle, "hero");
            Assert.AreEqual(19, hero.HitPoints, "1 заряд");
            PassUntil(battle, "foe");
            Assert.IsTrue(Apply(battle, SandboxStatusCatalog.Bleeding, "hero", "foe").Applied, "второй заряд");
            Assert.AreEqual(2, hero.Statuses.Single().Charges.Count);
            PassUntil(battle, "hero");
            Assert.AreEqual(17, hero.HitPoints, "2 заряда по 1");
            battle.EndActivation();
            Assert.AreEqual(1, hero.Statuses.Single().Charges.Count, "первый заряд прожил свои 2 хода, второй не обновил его");
            PassUntil(battle, "hero");
            Assert.AreEqual(16, hero.HitPoints);
            battle.EndActivation();
            Assert.IsFalse(hero.HasStatus(SandboxStatusCatalog.Bleeding));
            List<SandboxStatusView> none = battle.GetStatusViews("hero", SandboxTeam.Player);
            Assert.AreEqual(0, none.Count);
        }

        [Test]
        public void NewTicksOnOneStart_CappedAtThree_ByApplicationOrder()
        {
            SandboxBattle battle = Duel(out SandboxUnitState hero, out _);
            PassUntil(battle, "foe");
            Apply(battle, SandboxStatusCatalog.Poison, "hero", "foe");
            Apply(battle, SandboxStatusCatalog.Burning, "hero", "foe");
            Apply(battle, "status.006", "hero", "foe");
            Apply(battle, "status.203", "hero", "foe");
            PassUntil(battle, "hero");
            Assert.AreEqual(17, hero.HitPoints, "4 тика по 1, но не больше 3 HP за начало хода");
        }

        [Test]
        public void Regeneration_BeforeTick_SameStart()
        {
            SandboxBattle battle = Duel(out SandboxUnitState hero, out _);
            hero.HitPoints = 15;
            PassUntil(battle, "foe");
            Apply(battle, SandboxStatusCatalog.Poison, "hero", "foe");
            Apply(battle, "status.146", "hero", "hero");
            battle.TakeEventLog();
            PassUntil(battle, "hero");
            Assert.AreEqual(15, hero.HitPoints, "+1 затем −1");
            List<string> log = battle.TakeEventLog();
            int heal = log.FindIndex(line => line.Contains("Постепенное восстановление"));
            int tick = log.FindIndex(line => line.Contains("Отравление"));
            Assert.IsTrue(heal >= 0 && tick > heal, "восстановление раньше тика: " + string.Join(" | ", log));
        }

        [Test]
        public void DelayedBurst_TriggersAtRoundStartPlusTwo()
        {
            SandboxBattle battle = Duel(out SandboxUnitState hero, out _);
            Assert.AreEqual(1, battle.Round);
            Apply(battle, "status.010", "hero", "foe");
            PassUntil(battle, "foe");
            PassUntil(battle, "hero");
            Assert.AreEqual(2, battle.Round);
            Assert.AreEqual(20, hero.HitPoints, "после 1 полного раунда предупреждения");
            PassUntil(battle, "foe");
            PassUntil(battle, "hero");
            Assert.AreEqual(3, battle.Round);
            Assert.AreEqual(17, hero.HitPoints);
            Assert.IsFalse(hero.HasStatus("status.010"));
        }

        // ---------- Сильный контроль ----------

        [Test]
        public void Stun_SkipsActivation_ThenRecontrolGuardRejectsFreeze_UntilCleanTurnEnds()
        {
            SandboxBattle battle = Duel(out SandboxUnitState hero, out _);
            PassUntil(battle, "foe");
            Assert.IsTrue(Apply(battle, SandboxStatusCatalog.Stunned, "hero", "foe").Applied);
            Assert.IsFalse(hero.CanRetaliate);
            battle.EndActivation();
            // Ход героя отнят: очередь сразу у врага следующего раунда.
            Assert.AreEqual("foe", battle.CurrentUnit.Id);
            Assert.AreEqual(2, battle.Round);
            Assert.IsFalse(hero.HasStatus(SandboxStatusCatalog.Stunned), "срок израсходован пропущенной активацией");
            SandboxStatusApplyResult freeze = Apply(battle, SandboxStatusCatalog.Frozen, "hero", "foe");
            Assert.IsFalse(freeze.Applied, "защита общей группы сильного контроля");
            StringAssert.Contains("повторного", freeze.RejectReason);
            PassUntil(battle, "hero");
            Assert.AreEqual(1, hero.ActionPoints, "следующая активация полноценная");
            Assert.Greater(hero.RemainingMovement, 0);
            battle.EndActivation();
            Assert.IsFalse(hero.StrongControlGuard, "защита снята в конце хода без контроля");
            Assert.IsTrue(Apply(battle, SandboxStatusCatalog.Frozen, "hero", "foe").Applied);
        }

        [Test]
        public void Numb_AllowsActionFromPlace_NoMove_NoRetaliation()
        {
            SandboxBattle battle = Build(P("hero", 2, 2, 9), E("foe", 3, 2, 5));
            SandboxUnitState hero = battle.GetUnit("hero");
            PassUntil(battle, "foe");
            Apply(battle, SandboxStatusCatalog.Numbed, "hero", "foe");
            Assert.IsTrue(hero.IsNumbed);
            Assert.IsFalse(hero.CanRetaliate);
            PassUntil(battle, "hero");
            Assert.AreEqual(0, hero.RemainingMovement);
            Assert.IsTrue(hero.ActsWithoutMovement);
            Assert.IsTrue(battle.PreviewAttack("hero", "foe").IsValid, "удар с места разрешён");
        }

        [Test]
        public void Sleep_FullyAbsorbedHitDoesNotWake_HpDamageWakes()
        {
            SandboxBattle battle = Build(P("hero", 2, 2, 5), E("foe", 3, 2, 9));
            SandboxUnitState hero = battle.GetUnit("hero");
            Assert.AreEqual("foe", battle.CurrentUnit.Id);
            Apply(battle, "status.030", "hero", "foe");
            Apply(battle, "status.132", "hero", "hero");
            Assert.IsTrue(battle.TryAttack("foe", "hero", out string message), message);
            Assert.AreEqual(20, hero.HitPoints, "щит поглотил удар");
            Assert.IsTrue(hero.HasStatus("status.030"), "без урона HP сон не снят");
            Assert.IsFalse(hero.HasStatus("status.132"), "щит потрачен");
            Assert.IsFalse(battle.HasPendingRetaliation, "спящий не отвечает");
            battle.EndActivation();
            // Ход спящего героя отнят; foe снова бьёт.
            PassUntil(battle, "foe");
            Assert.IsTrue(battle.TryAttack("foe", "hero", out message), message);
            Assert.AreEqual(17, hero.HitPoints);
            Assert.IsFalse(hero.HasStatus("status.030"), "фактический урон будит");
        }

        // ---------- Стойка, щиты, временные HP ----------

        [TestCase(0, 1)]
        [TestCase(1, 1)]
        [TestCase(3, 1)]
        [TestCase(4, 2)]
        public void GuardFormula_Unchanged_AndNotDoubledByStatus(int defense, int bonus)
        {
            Spec heroSpec = P("hero", 2, 2, 9);
            heroSpec.Defense = defense;
            SandboxBattle battle = Build(heroSpec, E("foe", 9, 6, 5));
            SandboxUnitState hero = battle.GetUnit("hero");
            decimal before = SandboxCombatTagRules.GetEffectiveDefense(hero);
            Assert.IsTrue(battle.TryGuard("hero", out _));
            Assert.AreEqual(before + bonus, SandboxCombatTagRules.GetEffectiveDefense(hero));
            Assert.AreEqual(bonus, SandboxCombatTagRules.GetGuardBonus(hero));
            Assert.IsTrue(battle.GetStatusViews("hero", SandboxTeam.Player).Any(view => view.StatusId == SandboxStatusCatalog.Guard),
                "стойка показана как status.130 без второго экземпляра");
            Assert.AreEqual(0, hero.Statuses.Count);
        }

        [Test]
        public void TemporaryHitPoints_AbsorbFirst_NoRefill_ExpiryKeepsRealHp()
        {
            SandboxBattle battle = Build(P("hero", 2, 2, 5), E("foe", 3, 2, 9));
            SandboxUnitState hero = battle.GetUnit("hero");
            Apply(battle, "status.148", "hero", "hero");
            Assert.IsTrue(battle.TryAttack("foe", "hero", out _));
            if (battle.HasPendingRetaliation)
                battle.TryResolvePendingRetaliation(out _);
            Assert.AreEqual(20, hero.HitPoints, "3 урона ушли во временные HP");
            Assert.IsFalse(hero.HasStatus("status.148"), "запас 3 израсходован полностью");
            Apply(battle, "status.148", "hero", "hero");
            Assert.IsFalse(Apply(battle, "status.148", "hero", "hero").Applied, "повторное наложение не пополняет");
            for (int i = 0; i < 6; i++)
            {
                PassUntil(battle, "hero");
                battle.EndActivation();
            }
            Assert.IsFalse(hero.HasStatus("status.148"));
            Assert.AreEqual(20, hero.HitPoints, "окончание не снимает обычные HP");
        }

        [Test]
        public void SharedWounds_SplitThreeIntoTwoPlusOne_NoRecursion()
        {
            SandboxBattle battle = Build(P("hero", 2, 2, 5), P("ally", 1, 2, 4), E("foe", 3, 2, 9));
            SandboxUnitState hero = battle.GetUnit("hero");
            SandboxUnitState ally = battle.GetUnit("ally");
            Apply(battle, SandboxStatusCatalog.SharedWounds, "hero", "ally");
            Assert.IsTrue(battle.TryAttack("foe", "hero", out string message), message);
            Assert.AreEqual(18, hero.HitPoints, "жертве 2");
            Assert.AreEqual(19, ally.HitPoints, "партнёру 1");
        }

        [Test]
        public void DoomMark_TwoHitsPlusOne_ThenConsumed()
        {
            SandboxBattle battle = Build(P("hero", 2, 2, 5), E("foe", 3, 2, 9));
            SandboxUnitState hero = battle.GetUnit("hero");
            Apply(battle, "status.098", "hero", "foe");
            Assert.AreEqual(4, battle.PreviewAttack("foe", "hero").Damage, "предпросмотр видит +1");
            Assert.IsTrue(hero.HasStatus("status.098"), "предпросмотр не расходует");
            battle.TryAttack("foe", "hero", out _);
            Assert.AreEqual(16, hero.HitPoints);
            Assert.AreEqual(1, hero.Statuses.Single().UsesLeft);
        }

        [Test]
        public void TemporaryAttackModifiers_CappedAtMinusTwo()
        {
            SandboxBattle battle = Duel(out SandboxUnitState hero, out SandboxUnitState foe);
            Apply(battle, "status.067", "hero", "foe");
            Apply(battle, "status.070", "hero", "foe");
            Apply(battle, "status.071", "hero", "foe");
            Apply(battle, "status.078", "hero", "foe");
            Assert.AreEqual(-2, battle.StatusAttackMod(hero, foe, false));
        }

        // ---------- Связи с источником, видимость, очищение, конец боя ----------

        [Test]
        public void SourceDeath_RemovesSourceBoundStatus_SameChain()
        {
            SandboxBattle battle = Build(P("hero", 2, 2, 9), E("foe", 3, 2, 5), E("other", 9, 6, 1));
            SandboxUnitState hero = battle.GetUnit("hero");
            SandboxUnitState foe = battle.GetUnit("foe");
            Apply(battle, "status.057", "hero", "foe");
            Assert.IsFalse(battle.PreviewAttack("hero", "foe").IsValid, "Ужас: нельзя бить источник");
            foe.HitPoints = 1;
            battle.ApplyStatus(SandboxStatusCatalog.Poison, foe, hero);
            battle.EndActivation();
            Assert.IsTrue(foe.IsDefeated, "тик в начале хода источника добил его");
            Assert.AreNotEqual("foe", battle.CurrentUnit?.Id, "павший не продолжает действовать");
            Assert.IsFalse(hero.HasStatus("status.057"), "состояние источника снято вместе с его гибелью");
        }

        [Test]
        public void Invisible_NotTargetable_AndIconsHiddenFromEnemy()
        {
            SandboxBattle battle = Build(P("hero", 2, 2, 9), E("foe", 3, 2, 5));
            Apply(battle, SandboxStatusCatalog.Invisible, "hero", "hero");
            PassUntil(battle, "foe");
            Assert.IsFalse(battle.PreviewAttack("foe", "hero").IsValid);
            Assert.AreEqual(0, battle.GetStatusViews("hero", SandboxTeam.Enemy).Count, "враг не видит значков невидимого");
            Assert.AreEqual(1, battle.GetStatusViews("hero", SandboxTeam.Player).Count, "своим видно");
        }

        [Test]
        public void AllyCleanse_RemovesPoison_ForOneAction()
        {
            SandboxBattle battle = Build(P("hero", 2, 2, 5), P("medic", 1, 2, 9), E("foe", 9, 6, 1));
            SandboxUnitState hero = battle.GetUnit("hero");
            SandboxUnitState medic = battle.GetUnit("medic");
            Apply(battle, SandboxStatusCatalog.Poison, "hero", "foe");
            Apply(battle, SandboxStatusCatalog.Stunned, "hero", "foe");
            Assert.AreEqual("medic", battle.CurrentUnit.Id);
            List<SandboxContextAction> actions = battle.GetContextActions("medic");
            SandboxContextAction cleanse = actions.Single(item => item.Kind == SandboxContextActionKind.CleanseStatus && item.StatusId == SandboxStatusCatalog.Poison);
            Assert.IsTrue(actions.Any(item => item.StatusId == SandboxStatusCatalog.Stunned), "союзник приводит оглушённого в чувство");
            Assert.IsTrue(battle.TryContextAction("medic", cleanse, out string message), message);
            Assert.IsFalse(hero.HasStatus(SandboxStatusCatalog.Poison));
            Assert.AreEqual(0, medic.ActionPoints, "очищение стоит действия");
        }

        [Test]
        public void SupportSurge_CleanseDoesNotSpendNormalAction()
        {
            SandboxBattle battle = Build(P("hero", 2, 2, 5), P("medic", 1, 2, 9), E("foe", 9, 6, 1));
            SandboxUnitState medic = battle.GetUnit("medic");
            PassUntil(battle, "foe");
            Apply(battle, SandboxStatusCatalog.ExtraSupport, "medic", "medic");
            Apply(battle, SandboxStatusCatalog.Poison, "hero", "foe");
            PassUntil(battle, "medic");
            SandboxContextAction cleanse = battle.GetContextActions("medic").First(item => item.Kind == SandboxContextActionKind.CleanseStatus);
            Assert.IsTrue(battle.TryContextAction("medic", cleanse, out _));
            Assert.AreEqual(1, medic.ActionPoints, "обычное действие сохранено");
            Assert.IsFalse(battle.GetContextActions("medic").Any(item => item.Kind == SandboxContextActionKind.CleanseStatus && item.StatusId == SandboxStatusCatalog.Poison));
        }

        [Test]
        public void BattleEnd_ClearsTemporaryStatuses_RestoresMaxHp_DoesNotHeal()
        {
            SandboxBattle battle = Build(P("hero", 2, 2, 9), E("foe", 3, 2, 5));
            SandboxUnitState hero = battle.GetUnit("hero");
            SandboxUnitState foe = battle.GetUnit("foe");
            Apply(battle, "status.077", "hero", "foe");
            Assert.AreEqual(18, hero.MaxHitPoints);
            Assert.AreEqual(18, hero.HitPoints, "HP только ограничено новым максимумом");
            foe.HitPoints = 1;
            Assert.IsTrue(battle.TryAttack("hero", "foe", out _));
            Assert.AreEqual(SandboxBattlePhase.PlayerVictory, battle.Phase);
            Assert.AreEqual(0, hero.Statuses.Count);
            Assert.AreEqual(20, hero.MaxHitPoints, "MaxHP возвращён");
            Assert.AreEqual(18, hero.HitPoints, "возврат MaxHP не лечит");
        }

        [Test]
        public void Frost_ThreeCharges_BecomeFrozen()
        {
            SandboxBattle battle = Duel(out SandboxUnitState hero, out _);
            PassUntil(battle, "foe");
            Apply(battle, SandboxStatusCatalog.Frost, "hero", null);
            Apply(battle, SandboxStatusCatalog.Frost, "hero", null);
            Assert.AreEqual(-1, SandboxBattle.StatusMovementMod(hero), "при 2 зарядах −1 движения");
            Apply(battle, SandboxStatusCatalog.Frost, "hero", null);
            Assert.IsFalse(hero.HasStatus(SandboxStatusCatalog.Frost));
            Assert.IsTrue(hero.HasStatus(SandboxStatusCatalog.Frozen));
        }

        [Test]
        public void Inspired_RemovesAndBlocksFear()
        {
            SandboxBattle battle = Duel(out SandboxUnitState hero, out _);
            Apply(battle, SandboxStatusCatalog.Fear, "hero", "foe");
            Apply(battle, SandboxStatusCatalog.Inspired, "hero", "hero");
            Assert.IsFalse(hero.HasStatus(SandboxStatusCatalog.Fear));
            SandboxStatusApplyResult again = Apply(battle, SandboxStatusCatalog.Fear, "hero", "foe");
            Assert.IsFalse(again.Applied);
            StringAssert.Contains("невосприимчив", again.RejectReason);
        }

        [Test]
        public void Tooltip_ShowsCurrentNumbersAndSource()
        {
            SandboxBattle battle = Duel(out _, out _);
            PassUntil(battle, "foe");
            Apply(battle, SandboxStatusCatalog.Poison, "hero", "foe");
            SandboxStatusView view = battle.GetStatusViews("hero", SandboxTeam.Player).Single();
            Assert.AreEqual(3, view.TurnsLeft);
            StringAssert.Contains("1 урона в начале твоего хода", view.Tooltip);
            StringAssert.Contains("Источник: foe", view.Tooltip);
            StringAssert.Contains("за 1 действие", view.Tooltip);
        }
    }
}

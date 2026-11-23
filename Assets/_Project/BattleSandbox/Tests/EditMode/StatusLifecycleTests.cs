using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace KingdomSurvival.BattleSandbox.Tests
{
    // Бриф завершения, этапы 7–12: Окружён, Смятение, Подчинение, перехват
    // защитником, При смерти и спасение, кокон и выводок.
    public sealed class StatusLifecycleTests
    {
        private static SandboxUnitState U(string id, SandboxTeam team, int q, int r, int init, int hp = 20, bool rescuable = false, params string[] tags)
        {
            return new SandboxUnitState(id, new SandboxUnitDefinition(id, id, SandboxUnitRole.Militia, hp, 2, 2, 3, 4, init, 1, tags, null, null, 0, rescuable),
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

        private static void PassUntil(SandboxBattle b, string id)
        {
            b.EndActivation();
            for (int i = 0; i < 40 && b.CurrentUnit != null && b.CurrentUnit.Id != id; i++)
                b.EndActivation();
        }

        // ---------- Окружён ----------

        [Test]
        public void Surrounded_AppearsWithTwoEnemies_NoAlly_AndDisappears()
        {
            SandboxBattle b = Build(U("hero", SandboxTeam.Player, 5, 4, 9), U("e1", SandboxTeam.Enemy, 6, 4, 5), U("e2", SandboxTeam.Enemy, 4, 4, 4));
            SandboxUnitState hero = b.GetUnit("hero");
            Assert.IsTrue(hero.HasStatus(SandboxBattle.SurroundedId), "2 врага рядом, союзника нет");
            b.GetUnit("e2").HitPoints = 0;
            b.ApplyStatus(SandboxStatusCatalog.Poison, b.GetUnit("e1"), hero);
            b.TryMove("hero", new HexCoord(5, 3), out _);
            Assert.IsFalse(hero.HasStatus(SandboxBattle.SurroundedId), "окружение разорвано");
        }

        // Гибель одного из окружающих от удара снимает «Окружён» сразу, без шага героя.
        [Test]
        public void Surrounded_RemovedImmediatelyWhenEncirclerKilledByAttack()
        {
            SandboxBattle b = Build(U("hero", SandboxTeam.Player, 5, 4, 9), U("e1", SandboxTeam.Enemy, 6, 4, 5), U("e2", SandboxTeam.Enemy, 4, 4, 4, hp: 1));
            SandboxUnitState hero = b.GetUnit("hero");
            Assert.IsTrue(hero.HasStatus(SandboxBattle.SurroundedId), "2 врага рядом, союзника нет");
            Assert.IsTrue(b.GetStatusViews("hero", SandboxTeam.Player).Any(v => v.StatusId == SandboxBattle.SurroundedId));
            Assert.IsTrue(b.TryAttack("hero", "e2", out string m), m);
            Assert.IsTrue(b.GetUnit("e2").IsDefeated, "предусловие: e2 убит");
            Assert.IsFalse(hero.HasStatus(SandboxBattle.SurroundedId), "остался один враг — не окружён");
            Assert.IsFalse(b.GetStatusViews("hero", SandboxTeam.Player).Any(v => v.StatusId == SandboxBattle.SurroundedId), "значок тоже исчез");
        }

        // Гибель окружающего от ответного удара.
        [Test]
        public void Surrounded_RemovedWhenEncirclerKilledByRetaliation()
        {
            SandboxBattle b = Build(U("hero", SandboxTeam.Player, 5, 4, 9, hp: 40), U("e1", SandboxTeam.Enemy, 6, 4, 5), U("e2", SandboxTeam.Enemy, 4, 4, 4, hp: 1));
            SandboxUnitState hero = b.GetUnit("hero");
            Assert.IsTrue(hero.HasStatus(SandboxBattle.SurroundedId));
            PassUntil(b, "e2");
            Assert.IsTrue(b.TryAttack("e2", "hero", out string m), m);
            if (b.HasPendingRetaliation)
                Assert.IsTrue(b.TryResolvePendingRetaliation(out string r), r);
            Assert.IsTrue(b.GetUnit("e2").IsDefeated, "предусловие: ответный удар убил e2");
            Assert.IsFalse(hero.HasStatus(SandboxBattle.SurroundedId), "окружение снято сразу");
        }

        // Описание состояния доходит до игрока: у «Окружён» оно не пустое.
        [Test]
        public void Surrounded_ViewHasDescription()
        {
            SandboxBattle b = Build(U("hero", SandboxTeam.Player, 5, 4, 9), U("e1", SandboxTeam.Enemy, 6, 4, 5), U("e2", SandboxTeam.Enemy, 4, 4, 4));
            SandboxStatusView view = b.GetStatusViews("hero", SandboxTeam.Player).First(v => v.StatusId == SandboxBattle.SurroundedId);
            Assert.IsFalse(string.IsNullOrEmpty(view.Effect), "описание состояния «Окружён»");
        }

        // Каждое состояние с экземпляром (не фасад связи) имеет текст описания для подсказки и панели.
        [Test]
        public void AllInstanceStatuses_HaveDescription()
        {
            List<string> empty = SandboxStatusCatalog.All
                .Where(def => def.Clock != SandboxStatusClock.LegacyFacade && string.IsNullOrEmpty(SandboxStatusText.Describe(def)))
                .Select(def => def.Id + " " + def.Name).ToList();
            Assert.IsEmpty(empty, "без описания: " + string.Join("; ", empty));
        }

        [Test]
        public void Surrounded_FirstExitCostsOneMore_OncePerActivation()
        {
            SandboxBattle b = Build(U("hero", SandboxTeam.Player, 5, 4, 9), U("e1", SandboxTeam.Enemy, 6, 4, 5), U("e2", SandboxTeam.Enemy, 4, 4, 4));
            IReadOnlyDictionary<HexCoord, int> reach = b.GetReachable("hero");
            HexCoord away = new HexCoord(5, 4).Neighbors().First(c => c.DistanceTo(new HexCoord(4, 4)) > 1 && b.IsFreeCell(c));
            Assert.AreEqual(2, reach[away], "1 + 1: шаг выводит из соседства одного из окружающих");
        }

        [Test]
        public void Surrounded_NotWithAdjacentAlly()
        {
            SandboxBattle b = Build(U("hero", SandboxTeam.Player, 5, 4, 9), U("ally", SandboxTeam.Player, 5, 5, 8), U("e1", SandboxTeam.Enemy, 6, 4, 5), U("e2", SandboxTeam.Enemy, 4, 4, 4));
            Assert.IsFalse(b.GetUnit("hero").HasStatus(SandboxBattle.SurroundedId));
        }

        // ---------- Смятение и Подчинение ----------

        [Test]
        public void Confusion_AttacksNearest_EvenAlly_NoExtraActivation()
        {
            SandboxBattle b = Build(U("hero", SandboxTeam.Player, 2, 2, 9), U("ally", SandboxTeam.Player, 3, 2, 8), U("foe", SandboxTeam.Enemy, 9, 6, 5));
            PassUntil(b, "foe");
            Assert.IsTrue(b.ApplyStatus(SandboxBattle.ConfusionId, b.GetUnit("hero"), b.GetUnit("foe")).Applied);
            Assert.AreEqual(20, b.GetUnit("ally").HitPoints, "в момент наложения хода нет");
            PassUntil(b, "ally");
            Assert.AreEqual(17, b.GetUnit("ally").HitPoints, "ближайшая цель — союзник");
            Assert.AreEqual(SandboxTeam.Player, b.GetUnit("hero").Team, "исходная сторона не меняется");
        }

        [Test]
        public void Domination_HitsSourceEnemies_TeamUnchanged_BattleNotEnded()
        {
            SandboxBattle b = Build(U("hero", SandboxTeam.Player, 2, 2, 9), U("ally", SandboxTeam.Player, 3, 2, 8), U("witch", SandboxTeam.Enemy, 9, 6, 5));
            PassUntil(b, "witch");
            b.ApplyStatus(SandboxBattle.DominationId, b.GetUnit("hero"), b.GetUnit("witch"));
            PassUntil(b, "ally");
            Assert.AreEqual(17, b.GetUnit("ally").HitPoints, "подчинённый бьёт противника стороны источника");
            Assert.AreEqual(SandboxBattlePhase.InProgress, b.Phase);
            Assert.AreEqual(SandboxTeam.Player, b.GetUnit("hero").Team);
        }

        // ---------- Перехват ----------

        [Test]
        public void Guardian_InterceptsAttack_DamageByGuardianDefense()
        {
            SandboxBattle b = Build(U("foe", SandboxTeam.Enemy, 4, 4, 9), U("ward", SandboxTeam.Player, 5, 4, 5), U("guard", SandboxTeam.Player, 5, 5, 4));
            Assert.IsTrue(b.ApplyStatus(SandboxBattle.GuardedId, b.GetUnit("ward"), b.GetUnit("guard")).Applied);
            Assert.IsTrue(b.TryAttack("foe", "ward", out string m), m);
            Assert.AreEqual(20, b.GetUnit("ward").HitPoints);
            Assert.Less(b.GetUnit("guard").HitPoints, 20, m);
            Assert.IsFalse(b.GetUnit("ward").HasStatus(SandboxBattle.GuardedId), "заряд перехвата один");
        }

        [Test]
        public void Guardian_NotAdjacent_Rejected_OneWardOnly()
        {
            SandboxBattle b = Build(U("foe", SandboxTeam.Enemy, 9, 6, 9), U("ward", SandboxTeam.Player, 5, 4, 5), U("guard", SandboxTeam.Player, 5, 5, 4), U("far", SandboxTeam.Player, 1, 1, 3), U("ward2", SandboxTeam.Player, 4, 5, 2));
            Assert.IsFalse(b.ApplyStatus(SandboxBattle.GuardedId, b.GetUnit("far"), b.GetUnit("guard")).Applied, "не рядом");
            Assert.IsTrue(b.ApplyStatus(SandboxBattle.GuardedId, b.GetUnit("ward"), b.GetUnit("guard")).Applied);
            Assert.IsFalse(b.ApplyStatus(SandboxBattle.GuardedId, b.GetUnit("ward2"), b.GetUnit("guard")).Applied, "один подопечный");
        }

        // ---------- При смерти ----------

        [Test]
        public void Dying_OnlyWithCapability_RescueGivesOneHp_SecondTimeFinal()
        {
            SandboxBattle b = Build(U("foe", SandboxTeam.Enemy, 4, 4, 9), U("hero", SandboxTeam.Player, 5, 4, 5, hp: 3, rescuable: true), U("medic", SandboxTeam.Player, 4, 5, 7));
            SandboxUnitState hero = b.GetUnit("hero");
            b.TryAttack("foe", "hero", out _);
            Assert.IsTrue(hero.IsDying, "лежит при смерти");
            Assert.IsTrue(hero.HasStatus(SandboxBattle.DyingId));
            Assert.IsFalse(b.CombatEvents.Any(e => e.Kind == SandboxCombatEventKind.FinalDeath && e.TargetUnitId == "hero"));
            PassUntil(b, "medic");
            SandboxContextAction rescue = b.GetContextActions("medic").Single(a => a.Kind == SandboxContextActionKind.Rescue);
            Assert.IsTrue(b.TryContextAction("medic", rescue, out _));
            Assert.AreEqual(1, hero.HitPoints);
            Assert.IsFalse(hero.IsDying);
            hero.HitPoints = 0;
            b.ApplyStatus(SandboxStatusCatalog.Poison, b.GetUnit("medic"), b.GetUnit("foe"));
            b.EndActivation();
            Assert.IsFalse(hero.IsDying, "второго спасения в бою нет — окончательная смерть");
        }

        [Test]
        public void Dying_NotForOrdinaryUnit_WindowExpires()
        {
            SandboxBattle plain = Build(U("foe", SandboxTeam.Enemy, 4, 4, 9), U("hero", SandboxTeam.Player, 5, 4, 5, hp: 3), U("other", SandboxTeam.Player, 9, 8, 4));
            plain.TryAttack("foe", "hero", out _);
            Assert.IsFalse(plain.GetUnit("hero").IsDying, "по умолчанию — окончательная смерть");

            SandboxBattle b = Build(U("foe", SandboxTeam.Enemy, 4, 4, 9), U("hero", SandboxTeam.Player, 5, 4, 5, hp: 3, rescuable: true), U("other", SandboxTeam.Player, 9, 8, 4));
            b.TryAttack("foe", "hero", out _);
            Assert.IsTrue(b.GetUnit("hero").IsDying);
            PassUntil(b, "foe");
            Assert.IsFalse(b.GetUnit("hero").IsDying, "окно истекло на пропущенной активации");
            Assert.IsTrue(b.CombatEvents.Any(e => e.Kind == SandboxCombatEventKind.FinalDeath && e.TargetUnitId == "hero"));
        }

        // ---------- Кокон ----------

        [Test]
        public void Embryo_FinalDeathCreatesCocoon_HatchesAfterFullRound_NotImmediately()
        {
            SandboxBattle b = Build(U("mother", SandboxTeam.Enemy, 9, 6, 9), U("hero", SandboxTeam.Player, 4, 4, 5, hp: 1), U("ally", SandboxTeam.Player, 1, 1, 4));
            b.ApplyStatus(SandboxBattle.EmbryoId, b.GetUnit("hero"), b.GetUnit("mother"));
            b.EndActivation();
            Assert.IsTrue(b.GetUnit("hero").IsDefeated, "тик убил носителя: " + string.Join(" | ", b.TakeEventLog()));
            SandboxBattleObject cocoon = b.Objects.Single(o => o.Kind == SandboxObjectKind.Cocoon);
            Assert.AreEqual(3, cocoon.HitPoints);
            int hatchRound = cocoon.ExpiresAtRound;
            Assert.AreEqual(b.Round + 2, hatchRound, "полный раунд предупреждения");
            while (b.Round < hatchRound && b.Phase == SandboxBattlePhase.InProgress)
                b.EndActivation();
            SandboxUnitState brood = b.Units.Single(u => u.Definition.Id == "brood.parasite");
            Assert.AreEqual(SandboxTeam.Enemy, brood.Team, "сторона источника");
            Assert.AreEqual(b.Round + 1, brood.ActsFromRound, "первая активация — со следующего раунда");
        }

        [Test]
        public void Cocoon_CanBeDestroyedByAttack_AndKeepsBattleGoing()
        {
            SandboxBattle b = Build(U("hero", SandboxTeam.Player, 4, 4, 9), U("host", SandboxTeam.Enemy, 5, 4, 5, hp: 1), U("witch", SandboxTeam.Player, 1, 1, 4));
            // Источник заражения — на стороне игрока: кокон его; проверяем объект-цель и победу врага-носителя.
            b.ApplyStatus(SandboxBattle.EmbryoId, b.GetUnit("host"), b.GetUnit("hero"));
            Assert.IsTrue(b.TryAttack("hero", "host", out _));
            Assert.IsTrue(b.Objects.Any(o => o.Kind == SandboxObjectKind.Cocoon), "кокон на месте павшего");
            Assert.AreEqual(SandboxBattlePhase.PlayerVictory, b.Phase, "свой кокон не держит бой");
            SandboxBattle e = Build(U("hero", SandboxTeam.Player, 4, 4, 9), U("host", SandboxTeam.Player, 5, 4, 5, hp: 1), U("witch", SandboxTeam.Enemy, 9, 8, 4));
            e.ApplyStatus(SandboxBattle.EmbryoId, e.GetUnit("host"), e.GetUnit("witch"));
            e.GetUnit("witch").HitPoints = 1;
            e.ApplyStatus(SandboxStatusCatalog.Poison, e.GetUnit("witch"), e.GetUnit("hero"));
            e.GetUnit("host").HitPoints = 0;
            e.EndActivation();
            Assert.IsTrue(e.Objects.Any(o => o.Kind == SandboxObjectKind.Cocoon && !o.IsDestroyed && o.SnapshotTeam == SandboxTeam.Enemy), string.Join(" | ", e.TakeEventLog()));
        }
    }
}

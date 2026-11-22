using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace KingdomSurvival.BattleSandbox.Tests
{
    // Бриф завершения, этапы 3–4: лечение и помощь, типы урона, огонь/холод,
    // заклинания и развеивание через обычный путь способности.
    public sealed class SupportAndTypedDamageTests
    {
        private static SandboxUnitState U(string id, SandboxTeam team, int q, int r, int init, int hp = 20, string[] tags = null, params SandboxAbilityDefinition[] abilities)
        {
            return new SandboxUnitState(id, new SandboxUnitDefinition(id, id, SandboxUnitRole.Militia, hp, 2, 2, 3, 4, init, 1, tags, null, abilities), team, new HexCoord(q, r));
        }

        private static SandboxBattle Build(params SandboxUnitState[] units)
        {
            Dictionary<HexCoord, SandboxTerrain> map = new Dictionary<HexCoord, SandboxTerrain>();
            SandboxTerrainRules.RegisterBattle(units, map);
            SandboxBattle battle = new SandboxBattle(14, 10, units, map);
            battle.Start();
            return battle;
        }

        private static SandboxAbilityDefinition Heal() => SandboxAbilityDefaults.Create(SandboxAbilityKind.Heal, "heal");

        private static SandboxAbilityDefinition Spell(SandboxDamageType type, string payload = null) =>
            new SandboxAbilityDefinition("spell", "Заклинание", SandboxAbilityKind.Spell, cooldown: 2, rangeMin: 1, rangeMax: 3, statusPayloadId: payload, damageType: type);

        private static SandboxAbilityCommand Cmd(SandboxBattle battle, string unit, string ability, string target)
        {
            return battle.GetAbilityCandidates(unit, ability).First(c => c.Command.TargetUnitId == target).Command;
        }

        [Test]
        public void Heal_SevenOfTwelve_ToEleven_AndOverhealRecorded()
        {
            SandboxBattle b = Build(U("medic", SandboxTeam.Player, 2, 2, 9, abilities: Heal()), U("ally", SandboxTeam.Player, 3, 2, 5, hp: 12), U("foe", SandboxTeam.Enemy, 9, 6, 1));
            SandboxUnitState ally = b.GetUnit("ally");
            ally.HitPoints = 7;
            Assert.IsTrue(b.TryUseAbility("medic", Cmd(b, "medic", "heal", "ally"), out string m), m);
            Assert.AreEqual(11, ally.HitPoints);
            SandboxCombatEvent heal = b.CombatEvents.Last(e => e.Kind == SandboxCombatEventKind.HealingResolved);
            Assert.AreEqual(4, heal.ActualHeal);
            Assert.AreEqual(0, heal.Overheal);
            Assert.AreEqual(0, b.GetUnit("medic").ActionPoints, "1 действие вместо атаки");
        }

        [Test]
        public void Heal_NearFull_RecordsOverheal_FullHpWithoutPayloadRejected()
        {
            SandboxBattle b = Build(U("medic", SandboxTeam.Player, 2, 2, 9, abilities: Heal()), U("ally", SandboxTeam.Player, 3, 2, 5, hp: 12), U("foe", SandboxTeam.Enemy, 9, 6, 1));
            SandboxUnitState ally = b.GetUnit("ally");
            SandboxAbilityPreview full = b.PreviewAbility("medic", new SandboxAbilityCommand { AbilityId = "heal", TargetUnitId = "ally" });
            Assert.IsFalse(full.IsValid, "полное HP без полезного эффекта — отказ до расхода действия");
            ally.HitPoints = 11;
            Assert.IsTrue(b.TryUseAbility("medic", Cmd(b, "medic", "heal", "ally"), out _));
            Assert.AreEqual(12, ally.HitPoints);
            Assert.AreEqual(3, b.CombatEvents.Last(e => e.Kind == SandboxCombatEventKind.HealingResolved).Overheal);
        }

        [Test]
        public void Heal_DoesNotRemovePoison_ButAntidoteTagDoesForOther_NotSelf()
        {
            SandboxBattle b = Build(U("medic", SandboxTeam.Player, 2, 2, 9, tags: new[] { "passive.status.20261120.002" }, abilities: Heal()),
                U("ally", SandboxTeam.Player, 3, 2, 5), U("foe", SandboxTeam.Enemy, 9, 6, 1));
            SandboxUnitState medic = b.GetUnit("medic");
            medic.HitPoints = 10;
            b.ApplyStatus(SandboxStatusCatalog.Poison, medic, b.GetUnit("foe"));
            Assert.IsTrue(b.TryUseAbility("medic", Cmd(b, "medic", "heal", "medic"), out _));
            Assert.IsTrue(medic.HasStatus(SandboxStatusCatalog.Poison), "самолечение не запускает «вылечил другого», яд остаётся");
        }

        [Test]
        public void SkilledHands_PlusTwoOnFirstHealOfAlly()
        {
            SandboxBattle b = Build(U("medic", SandboxTeam.Player, 2, 2, 9, tags: new[] { "passive.20261119.136" }, abilities: Heal()),
                U("ally", SandboxTeam.Player, 3, 2, 5), U("foe", SandboxTeam.Enemy, 9, 6, 1));
            b.GetUnit("ally").HitPoints = 10;
            b.TryUseAbility("medic", Cmd(b, "medic", "heal", "ally"), out _);
            Assert.AreEqual(16, b.GetUnit("ally").HitPoints, "4 + 2");
        }

        [Test]
        public void Heal_CannotRaiseDefeated()
        {
            SandboxBattle b = Build(U("medic", SandboxTeam.Player, 2, 2, 9, abilities: Heal()), U("ally", SandboxTeam.Player, 3, 2, 5), U("foe", SandboxTeam.Enemy, 9, 6, 1));
            b.GetUnit("ally").HitPoints = 0;
            Assert.IsFalse(b.GetAbilityCandidates("medic", "heal").Any(c => c.Command.TargetUnitId == "ally"));
        }

        [Test]
        public void FireSpell_DamagesWithoutBurning_PayloadBurns_FireMeltsFrozen()
        {
            SandboxBattle b = Build(U("mage", SandboxTeam.Enemy, 2, 2, 9, abilities: Spell(SandboxDamageType.Fire, SandboxStatusCatalog.Burning)),
                U("hero", SandboxTeam.Player, 4, 2, 5));
            SandboxUnitState hero = b.GetUnit("hero");
            b.ApplyStatus(SandboxStatusCatalog.Frozen, hero, null);
            Assert.IsTrue(b.TryUseAbility("mage", Cmd(b, "mage", "spell", "hero"), out string m), m);
            Assert.Less(hero.HitPoints, 20);
            Assert.IsFalse(hero.HasStatus(SandboxStatusCatalog.Frozen), "огонь растопил лёд");
            Assert.IsTrue(hero.HasStatus(SandboxStatusCatalog.Burning), "Горение — только payload способности");
            SandboxCombatEvent hit = b.CombatEvents.Last(e => e.Kind == SandboxCombatEventKind.HitResolved);
            Assert.AreEqual(SandboxDamageType.Fire, hit.DamageType);
            Assert.IsTrue(hit.IsMagical);
        }

        [Test]
        public void WardAgainstSpells_CancelsDamageAndPayload()
        {
            SandboxBattle b = Build(U("mage", SandboxTeam.Enemy, 2, 2, 9, abilities: Spell(SandboxDamageType.Supernatural, "status.071")),
                U("hero", SandboxTeam.Player, 4, 2, 5));
            SandboxUnitState hero = b.GetUnit("hero");
            b.ApplyStatus("status.137", hero, hero);
            b.TryUseAbility("mage", Cmd(b, "mage", "spell", "hero"), out _);
            Assert.AreEqual(20, hero.HitPoints);
            Assert.IsFalse(hero.HasStatus("status.071"), "связанное состояние тоже отменено");
            Assert.IsFalse(hero.HasStatus("status.137"), "заряд защиты потрачен");
        }

        [Test]
        public void HeatResistantHide_FireMinusForty_PhysicalNormal()
        {
            SandboxBattle fire = Build(U("mage", SandboxTeam.Enemy, 2, 2, 9, abilities: Spell(SandboxDamageType.Fire)),
                U("hero", SandboxTeam.Player, 4, 2, 5, tags: new[] { "passive.20261119.024" }));
            SandboxAbilityPreview p = fire.PreviewAbility("mage", Cmd(fire, "mage", "spell", "hero"));
            Assert.AreEqual(1, p.Damage, "3 × 0,6 = 1 (вниз)");
            SandboxBattle physical = Build(U("mage", SandboxTeam.Enemy, 3, 2, 9), U("hero", SandboxTeam.Player, 4, 2, 5, tags: new[] { "passive.20261119.024" }));
            Assert.AreEqual(3, physical.PreviewAttack("mage", "hero").Damage);
        }

        [Test]
        public void ColdSpell_FrostCharge_OnePerSourcePerRound()
        {
            SandboxBattle b = Build(U("mage", SandboxTeam.Enemy, 2, 2, 9, abilities: Spell(SandboxDamageType.Cold, SandboxStatusCatalog.Frost)),
                U("hero", SandboxTeam.Player, 4, 2, 5));
            SandboxUnitState hero = b.GetUnit("hero");
            b.TryUseAbility("mage", Cmd(b, "mage", "spell", "hero"), out _);
            Assert.AreEqual(1, hero.Statuses.Single(s => s.StatusId == SandboxStatusCatalog.Frost).Charges.Count);
            Assert.IsFalse(b.ApplyStatus(SandboxStatusCatalog.Frost, hero, b.GetUnit("mage")).Applied, "второй заряд от того же источника в раунде");
        }

        [Test]
        public void AreaSpell_HitsAlliesUnlessTagSparesThem()
        {
            SandboxAbilityDefinition area = new SandboxAbilityDefinition("area", "Площадь", SandboxAbilityKind.SpellArea, cooldown: 3, rangeMin: 1, rangeMax: 4, push: 1, damageType: SandboxDamageType.Fire);
            SandboxBattle plain = Build(U("mage", SandboxTeam.Enemy, 2, 2, 9, abilities: area), U("hero", SandboxTeam.Player, 5, 2, 5), U("minion", SandboxTeam.Enemy, 6, 2, 1));
            SandboxAbilityCommand cmd = plain.GetAbilityCandidates("mage", "area").First(c => c.Command.TargetCell == new HexCoord(5, 2)).Command;
            Assert.IsTrue(plain.TryUseAbility("mage", cmd, out string m), m);
            Assert.Less(plain.GetUnit("minion").HitPoints, 20, "свой в области задет");

            SandboxBattle careful = Build(U("mage", SandboxTeam.Enemy, 2, 2, 9, tags: new[] { "passive.20261119b.024" }, abilities: area),
                U("hero", SandboxTeam.Player, 5, 2, 5), U("minion", SandboxTeam.Enemy, 6, 2, 1));
            SandboxAbilityCommand cmd2 = careful.GetAbilityCandidates("mage", "area").First(c => c.Command.TargetCell == new HexCoord(5, 2)).Command;
            careful.TryUseAbility("mage", cmd2, out _);
            Assert.AreEqual(20, careful.GetUnit("minion").HitPoints, "«Бережёт своих внутри чар»");
            Assert.Less(careful.GetUnit("hero").HitPoints, 20);
        }

        [Test]
        public void Dispel_RemovesBeneficial_SiphonHeals()
        {
            SandboxAbilityDefinition dispel = SandboxAbilityDefaults.Create(SandboxAbilityKind.Dispel, "dispel");
            SandboxBattle b = Build(U("mage", SandboxTeam.Enemy, 2, 2, 9, tags: new[] { "passive.20261119.128" }, abilities: dispel), U("hero", SandboxTeam.Player, 4, 2, 5));
            SandboxUnitState mage = b.GetUnit("mage");
            mage.HitPoints = 10;
            b.ApplyStatus(SandboxStatusCatalog.Invisible, b.GetUnit("hero"), b.GetUnit("hero"));
            b.RemoveStatus(b.GetUnit("hero"), SandboxStatusCatalog.Invisible, "тест");
            b.ApplyStatus("status.137", b.GetUnit("hero"), b.GetUnit("hero"));
            Assert.IsTrue(b.TryUseAbility("mage", Cmd(b, "mage", "dispel", "hero"), out string m), m);
            Assert.IsFalse(b.GetUnit("hero").HasStatus("status.137"));
            Assert.AreEqual(12, mage.HitPoints, "«Сифон чужой силы»: +2 за магический эффект");
        }

        [Test]
        public void MagicReserve_LimitsSpells_WhenSet()
        {
            SandboxUnitState mage = new SandboxUnitState("mage",
                new SandboxUnitDefinition("mage", "mage", SandboxUnitRole.Militia, 20, 2, 2, 3, 4, 9, 1, null, null,
                    new[] { new SandboxAbilityDefinition("spell", "Заклинание", SandboxAbilityKind.Spell, cooldown: 0, rangeMin: 1, rangeMax: 3) }, magicReserve: 1),
                SandboxTeam.Enemy, new HexCoord(2, 2));
            SandboxBattle b = Build(mage, U("hero", SandboxTeam.Player, 4, 2, 5));
            Assert.IsTrue(b.TryUseAbility("mage", Cmd(b, "mage", "spell", "hero"), out _));
            Assert.AreEqual(0, b.MagicLeft(mage));
            b.EndActivation();
            for (int i = 0; i < 4 && b.CurrentUnit?.Id != "mage"; i++) b.EndActivation();
            Assert.IsFalse(b.PreviewAbility("mage", new SandboxAbilityCommand { AbilityId = "spell", TargetUnitId = "hero" }).IsValid, "запас исчерпан");
        }
    }
}

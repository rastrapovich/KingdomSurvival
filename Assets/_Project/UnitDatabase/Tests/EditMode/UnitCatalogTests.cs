using System.Collections.Generic;
using System.Linq;
using KingdomSurvival.ProgressionDatabase;
using KingdomSurvival.UnitDatabase.Editor;
using NUnit.Framework;
using UnityEngine;

namespace KingdomSurvival.UnitDatabase.Tests
{
    // ПР-12Ж: прототипный боевой каталог из 24 существ и 8 составов
    // (BESTIARY_COMBAT_PASSPORTS.md, «Прототипный каталог 24»).
    public sealed class UnitCatalogTests
    {
        private static UnitDatabaseAsset Database()
        {
            UnitDatabaseAsset database = Resources.Load<UnitDatabaseAsset>(UnitDatabaseAsset.ResourcesPath);
            Assert.IsNotNull(database);
            return database;
        }

        [Test]
        public void Catalog_Has24Creatures_WithCatalogStats()
        {
            UnitDatabaseAsset database = Database();
            List<UnitDefinitionData> catalog = UnitCatalogSeed.Creatures();
            Assert.AreEqual(24, catalog.Count);
            foreach (UnitDefinitionData expected in catalog)
            {
                UnitDefinitionData actual = database.FindById(expected.Id);
                Assert.IsNotNull(actual, "Нет существа " + expected.Id + " — нужен засев каталога.");
                Assert.AreEqual(UnitCategory.Creature, actual.Category, expected.Id);
                Assert.AreEqual(
                    new[] { expected.MaxHitPoints, expected.Attack, expected.Defense, expected.Damage, expected.Movement, expected.Initiative, expected.AttackRange },
                    new[] { actual.MaxHitPoints, actual.Attack, actual.Defense, actual.Damage, actual.Movement, actual.Initiative, actual.AttackRange },
                    expected.Id);
                CollectionAssert.AreEquivalent(expected.TagIds, actual.TagIds, expected.Id);
                Assert.AreEqual(expected.Size, actual.Size, expected.Id);
            }

            UnitDefinitionData treshchotka = database.FindById("treshchotka");
            Assert.AreEqual(5, treshchotka.AttackRange);
            Assert.IsTrue(treshchotka.HasTag("combat.ranged"));
            Assert.IsTrue(database.FindById("chernolob").HasTag("trait.armored"));
        }

        [Test]
        public void Abilities_WaitForMechanic_UntilPR16()
        {
            UnitDatabaseAsset database = Database();
            List<UnitAbilityData> abilities = UnitCatalogSeed.Creatures()
                .SelectMany(unit => database.FindById(unit.Id).Abilities)
                .ToList();
            Assert.Greater(abilities.Count, 15);
            Assert.IsTrue(abilities.All(ability => ability.Status == UnitAbilityStatus.WaitsForMechanic));
            Assert.AreEqual(UnitCombatBrick.AttachPersistent,
                database.FindById("blood_kleshchen").Abilities.Single().Brick);
            CollectionAssert.AreEquivalent(new[] { UnitCombatBrick.StatusMark, UnitCombatBrick.Telegraph },
                database.FindById("zvonnik").Abilities.Select(ability => ability.Brick));
        }

        [Test]
        public void Database_Valid_MissingArtIsAGap_NotAnError()
        {
            UnitDatabaseAsset database = Database();
            List<string> issues = new List<string>();
            database.CollectValidationIssues(issues);
            Assert.IsEmpty(issues, string.Join("\n", issues));

            // Существо без рисунка — пробел «ждёт рисунка», а не ошибка базы.
            // Проверка не зависит от того, кому художник уже загрузил арт.
            UnitDatabaseAsset copy = Object.Instantiate(database);
            try
            {
                copy.AddUnitIfMissing(UnitDefinitionData.CreateCreature(
                    "zz_no_art", "Без рисунка", UnitSize.Medium, 10, 1, 1, 1, 3, 3, 1, new string[0], new UnitAbilityData[0]));
                copy.CollectValidationIssues(issues);
                Assert.IsEmpty(issues, string.Join("\n", issues));
                List<string> gaps = new List<string>();
                copy.CollectArtGaps(gaps);
                Assert.IsTrue(gaps.Any(gap => gap.StartsWith("zz_no_art:")));
                Assert.IsFalse(gaps.Any(gap => gap.StartsWith("guard:")), "У Гвардейца рисунки есть.");
            }
            finally
            {
                Object.DestroyImmediate(copy);
            }
        }

        [Test]
        public void Seed_IsIdempotent()
        {
            UnitDatabaseAsset database = Object.Instantiate(Database());
            try
            {
                int units = database.Units.Count;
                Assert.IsEmpty(UnitCatalogSeed.AddMissing(database), "Всё уже засеяно.");
                Assert.AreEqual(units, database.Units.Count);
            }
            finally
            {
                Object.DestroyImmediate(database);
            }
        }

        [Test]
        public void EveryCatalogCreature_HasNonProgressingProfile()
        {
            ProgressionDatabaseAsset progression = Resources.Load<ProgressionDatabaseAsset>(ProgressionDatabaseAsset.ResourcesPath);
            Assert.IsNotNull(progression);
            foreach (UnitDefinitionData creature in UnitCatalogSeed.Creatures())
            {
                ProgressionProfileRecord profile = progression.FindProfile(creature.Id);
                Assert.IsNotNull(profile, "Нет карты развития для " + creature.Id);
                Assert.IsFalse(profile.progresses, creature.Id + ": существо не копит опыт.");
            }
        }
    }
}

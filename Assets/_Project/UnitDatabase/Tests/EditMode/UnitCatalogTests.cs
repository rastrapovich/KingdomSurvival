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
        public void Catalog_Has22Creatures_WithCatalogStats()
        {
            UnitDatabaseAsset database = Database();
            List<UnitDefinitionData> catalog = UnitCatalogSeed.Creatures();
            // 24 существа ПР-12Ж без Криволапа и Бивнестого громилы (удалены 19.11.2026).
            Assert.AreEqual(22, catalog.Count);
            foreach (UnitDefinitionData expected in catalog)
            {
                UnitDefinitionData actual = database.FindById(expected.Id);
                Assert.IsNotNull(actual, "Нет существа " + expected.Id + " — нужен засев каталога.");
                Assert.AreEqual(UnitCategory.Creature, actual.Category, expected.Id);
                // Числа, теги и размер после засева правятся в окне Базы
                // существ (см. UnitCatalogSeed): тест не требует совпадения с
                // засевом, только осмысленные значения.
                Assert.That(actual.MaxHitPoints >= 1 && actual.Damage >= 1 && actual.Movement >= 1 && actual.AttackRange >= 1,
                    expected.Id + ": обязательные характеристики не меньше 1.");
            }

            UnitDefinitionData treshchotka = database.FindById("treshchotka");
            Assert.AreEqual(5, treshchotka.AttackRange);
            Assert.IsTrue(treshchotka.HasTag("combat.ranged"));
            Assert.IsTrue(database.FindById("chernolob").HasTag("trait.armored"));
        }

        [Test]
        // ПР-16 (решения пользователя 19.11.2026): способности включены
        // механикой, исключённые — отключены, Береговик выведен из набора.
        public void Abilities_PR16_DecisionsApplied()
        {
            UnitDatabaseAsset database = Database();
            Assert.AreEqual(UnitDatabaseAsset.CurrentSchemaVersion, database.SchemaVersion);
            List<UnitAbilityData> abilities = UnitCatalogSeed.Creatures()
                .SelectMany(unit => database.FindById(unit.Id).Abilities)
                .ToList();
            Assert.IsFalse(abilities.Any(ability => ability.Status == UnitAbilityStatus.WaitsForMechanic),
                string.Join(", ", abilities.Where(ability => ability.Status == UnitAbilityStatus.WaitsForMechanic).Select(ability => ability.Id)));

            UnitDefinitionData wolf = database.FindById("wolf");
            Assert.IsTrue(wolf.HasTag("combat.pack"));
            Assert.IsNull(wolf.FindAbility("wolf.pack"), "«Стая» — тег, не способность");
            Assert.AreEqual(UnitAbilityKind.Pounce, database.FindById("lynx").FindAbility("lynx.pounce").Kind);
            CollectionAssert.AreEquivalent(new[] { UnitAbilityKind.Attach, UnitAbilityKind.StunSpit },
                database.FindById("blood_kleshchen").Abilities.Select(ability => ability.Kind));
            foreach (string id in new[] { "belorev.scatter", "chernolob.breakthrough", "chernolob.telegraph", "beregovik.pull", "beregovik.wet" })
            {
                UnitAbilityData disabled = abilities.Single(ability => ability.Id == id);
                Assert.AreEqual(UnitAbilityStatus.Disabled, disabled.Status, id);
            }
            Assert.IsTrue(database.FindById("beregovik").ExcludedFromNewEncounters);
            Assert.IsNull(database.FindById("krivolap"), "Криволап удалён пользователем 19.11.2026");
            Assert.IsNull(database.FindById("bivnesty_gromila"), "Бивнестый громила удалён пользователем 19.11.2026");
            foreach (UnitDefinitionData creature in UnitAbilityMigration.SecondTier())
                Assert.IsNotNull(database.FindById(creature.Id), creature.Id);
            Assert.IsNull(database.FindById("rzhavnik"), "Ржавник пока убран");
        }

        [Test]
        public void AbilityMigration_IsIdempotent_AndKeepsAuthorValues()
        {
            UnitDatabaseAsset copy = Object.Instantiate(Database());
            try
            {
                int units = copy.Units.Count;
                float lynxMultiplier = copy.FindById("lynx").FindAbility("lynx.pounce").DamageMultiplier;
                Assert.IsEmpty(UnitAbilityMigration.Apply(copy), "повторный запуск ничего не меняет");
                Assert.IsEmpty(UnitCatalogSeed.AddMissing(copy), "засев не возвращает исключённое");
                Assert.AreEqual(units, copy.Units.Count);
                Assert.AreEqual(lynxMultiplier, copy.FindById("lynx").FindAbility("lynx.pounce").DamageMultiplier);
            }
            finally
            {
                Object.DestroyImmediate(copy);
            }
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

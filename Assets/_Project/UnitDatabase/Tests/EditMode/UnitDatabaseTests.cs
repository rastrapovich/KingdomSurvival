using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using KingdomSurvival.UnitDatabase;
using NUnit.Framework;
using UnityEngine;

namespace KingdomSurvival.UnitDatabase.Tests
{
    public sealed class UnitDatabaseTests
    {
        [Test]
        public void DefaultDatabaseContainsUniqueStableTypeIds()
        {
            UnitDatabaseAsset database = Resources.Load<UnitDatabaseAsset>(
                UnitDatabaseAsset.ResourcesPath);

            Assert.That(database, Is.Not.Null);
            Assert.That(database.Units.Count, Is.EqualTo(9 + 24), "9 исходных типов и 24 существа каталога ПР-12Ж.");
            Assert.That(
                database.Units.Select(unit => unit.Id).Distinct().Count(),
                Is.EqualTo(database.Units.Count));
            Assert.That(database.FindById("guard").DisplayLabel, Is.EqualTo("Гвардеец"));
        }

        [Test]
        public void DefaultUnitsUseCompactFirstLevelStatScale()
        {
            UnitDatabaseAsset database = Resources.Load<UnitDatabaseAsset>(
                UnitDatabaseAsset.ResourcesPath);
            // Каталог ПР-12Ж сознательно шире: от Шешки до Чернолоба.
            UnitDefinitionData[] original = database.Units
                .Where(unit => !UnitCatalogIds.Contains(unit.Id))
                .ToArray();

            Assert.That(original.Length, Is.EqualTo(9));
            Assert.That(original.All(unit =>
                unit.MaxHitPoints >= 10 && unit.MaxHitPoints <= 18), Is.True);
            Assert.That(original.All(unit =>
                unit.Attack >= 1 && unit.Attack <= 4), Is.True);
            Assert.That(original.All(unit =>
                unit.Defense >= 1 && unit.Defense <= 4), Is.True);
            Assert.That(original.All(unit =>
                unit.Damage >= 3 && unit.Damage <= 5), Is.True);
        }

        private static readonly HashSet<string> UnitCatalogIds = new HashSet<string>(
            KingdomSurvival.UnitDatabase.Editor.UnitCatalogSeed.Creatures().Select(unit => unit.Id));

        [Test]
        public void BeastTagDefinesFourCreatureInstancesForSandboxEncounter()
        {
            UnitDatabaseAsset database = Resources.Load<UnitDatabaseAsset>(
                UnitDatabaseAsset.ResourcesPath);
            // Фиксированная засада тестового боя — только старые звери;
            // существа каталога в неё не входят.
            UnitDefinitionData[] creatures = database.Units
                .Where(unit => unit.Category == UnitCategory.Creature && unit.SandboxEncounterCount > 0)
                .ToArray();

            Assert.That(creatures.Length, Is.EqualTo(3));
            Assert.That(creatures.All(unit => unit.HasTag("species.beast")), Is.True);
            Assert.That(creatures.Sum(unit => unit.SandboxEncounterCount), Is.EqualTo(4));
        }

        [Test]
        public void DefaultDatabaseDoesNotUseRedundantMeleeTag()
        {
            UnitDatabaseAsset database = Resources.Load<UnitDatabaseAsset>(
                UnitDatabaseAsset.ResourcesPath);

            Assert.That(database.FindTag("combat.melee"), Is.Null);
            Assert.That(database.Units.All(unit => !unit.HasTag("combat.melee")), Is.True);
            Assert.That(database.Units.All(unit => unit.AttackRange >= 1), Is.True);
        }

        [Test]
        public void DefaultUnitPortraitsUseValidCanonicalFramingData()
        {
            UnitDatabaseAsset database = Resources.Load<UnitDatabaseAsset>(
                UnitDatabaseAsset.ResourcesPath);

            Assert.That(database.SchemaVersion, Is.EqualTo(UnitDatabaseAsset.CurrentSchemaVersion));

            // Кадрирование управляется автором отдельно для каждого существа:
            // Cover/Contain, масштаб, смещение и отражение не обязаны иметь
            // одинаковые значения по умолчанию. Здесь проверяется только то,
            // что сохранённые параметры принадлежат допустимой новой модели.
            Assert.That(database.Units.All(unit =>
                System.Enum.IsDefined(typeof(PortraitFitMode), unit.PortraitFitMode)), Is.True);
            Assert.That(database.Units.All(unit =>
                unit.PortraitScale >= 0.05f &&
                !float.IsNaN(unit.PortraitScale) &&
                !float.IsInfinity(unit.PortraitScale)), Is.True);
            Assert.That(database.Units.All(unit =>
                !float.IsNaN(unit.PortraitOffsetNormalized.x) &&
                !float.IsInfinity(unit.PortraitOffsetNormalized.x) &&
                !float.IsNaN(unit.PortraitOffsetNormalized.y) &&
                !float.IsInfinity(unit.PortraitOffsetNormalized.y)), Is.True);
        }

        [Test]
        public void UnitPortraitFramingUsesSameFullSpriteMathForCoverAndContain()
        {
            Vector2 source = new Vector2(900f, 1200f);
            Vector2 frame = new Vector2(200f, 280f);

            Rect cover = UnitPortraitFraming.ResolveImageRect(
                source,
                frame,
                PortraitFitMode.Cover,
                1f,
                Vector2.zero);
            Rect contain = UnitPortraitFraming.ResolveImageRect(
                source,
                frame,
                PortraitFitMode.Contain,
                1f,
                Vector2.zero);

            Assert.That(cover.x, Is.EqualTo(-5f).Within(0.001f));
            Assert.That(cover.y, Is.EqualTo(0f).Within(0.001f));
            Assert.That(cover.width, Is.EqualTo(210f).Within(0.001f));
            Assert.That(cover.height, Is.EqualTo(280f).Within(0.001f));

            Assert.That(contain.x, Is.EqualTo(0f).Within(0.001f));
            Assert.That(contain.y, Is.EqualTo(6.666667f).Within(0.001f));
            Assert.That(contain.width, Is.EqualTo(200f).Within(0.001f));
            Assert.That(contain.height, Is.EqualTo(266.666667f).Within(0.001f));
        }

        [Test]
        public void UnitPortraitFramingDoesNotClampNormalizedOffset()
        {
            Rect result = UnitPortraitFraming.ResolveImageRect(
                new Vector2(900f, 1200f),
                new Vector2(200f, 280f),
                PortraitFitMode.Cover,
                2f,
                new Vector2(3f, -2f));

            Assert.That(result.x, Is.EqualTo(490f).Within(0.001f));
            Assert.That(result.y, Is.EqualTo(-700f).Within(0.001f));
            Assert.That(result.width, Is.EqualTo(420f).Within(0.001f));
            Assert.That(result.height, Is.EqualTo(560f).Within(0.001f));
        }

        [Test]
        public void LegacyPortraitOffsetMigratesToNormalizedCoordinatesOnce()
        {
            UnitDatabaseAsset database = ScriptableObject.CreateInstance<UnitDatabaseAsset>();
            try
            {
                UnitDefinitionData unit = new UnitDefinitionData();
                SetPrivateField(unit, "portraitScale", 1.25f);
                SetPrivateField(unit, "portraitOffset", new Vector2(15f, -20f));
                SetPrivateField(
                    database,
                    "units",
                    new List<UnitDefinitionData> { unit });
                SetPrivateField(database, "schemaVersion", 0);

                Assert.That(database.MigrateIfNeeded(), Is.True);
                Assert.That(database.SchemaVersion, Is.EqualTo(UnitDatabaseAsset.CurrentSchemaVersion));
                Assert.That(unit.PortraitScale, Is.EqualTo(1.25f).Within(0.0001f));
                Assert.That(unit.PortraitOffsetNormalized.x, Is.EqualTo(0.1f).Within(0.0001f));
                Assert.That(unit.PortraitOffsetNormalized.y, Is.EqualTo(-0.1f).Within(0.0001f));
                Assert.That(unit.PortraitFitMode, Is.EqualTo(PortraitFitMode.Cover));
                Assert.That(unit.PortraitFlipX, Is.False);
                Assert.That(database.MigrateIfNeeded(), Is.False);
            }
            finally
            {
                Object.DestroyImmediate(database);
            }
        }

        // Схема 1 → текущая не должна повторно переносить кадрирование портретов.
        // Схема 3 (ПР-12З): у старой записи нет набора анимаций — это не ошибка.
        [Test]
        public void SchemaOneMigratesToCurrent_WithoutTouchingPortraitFraming()
        {
            UnitDatabaseAsset database = ScriptableObject.CreateInstance<UnitDatabaseAsset>();
            try
            {
                UnitDefinitionData unit = new UnitDefinitionData();
                SetPrivateField(unit, "portraitOffsetNormalized", new Vector2(0.3f, 0.2f));
                SetPrivateField(unit, "portraitOffset", new Vector2(15f, -20f));
                SetPrivateField(database, "units", new List<UnitDefinitionData> { unit });
                SetPrivateField(database, "schemaVersion", 1);

                Assert.That(database.MigrateIfNeeded(), Is.True);
                Assert.That(database.SchemaVersion, Is.EqualTo(UnitDatabaseAsset.CurrentSchemaVersion));
                Assert.That(database.SchemaVersion, Is.EqualTo(3));
                Assert.That(unit.PortraitOffsetNormalized.x, Is.EqualTo(0.3f).Within(0.0001f));
                Assert.That(unit.Size, Is.EqualTo(UnitSize.Medium));
                Assert.That(unit.Abilities, Is.Empty);
                Assert.That(unit.AnimationSetId, Is.Empty);
                Assert.That(database.EncounterPresets, Is.Empty);
                Assert.That(database.MigrateIfNeeded(), Is.False, "Повторная миграция ничего не меняет.");
            }
            finally
            {
                Object.DestroyImmediate(database);
            }
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, "Missing field: " + fieldName);
            field.SetValue(target, value);
        }
    }
}

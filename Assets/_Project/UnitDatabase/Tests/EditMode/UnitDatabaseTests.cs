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
            Assert.That(database.Units.Count, Is.EqualTo(9));
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

            Assert.That(database.Units.All(unit =>
                unit.MaxHitPoints >= 10 && unit.MaxHitPoints <= 18), Is.True);
            Assert.That(database.Units.All(unit =>
                unit.Attack >= 1 && unit.Attack <= 4), Is.True);
            Assert.That(database.Units.All(unit =>
                unit.Defense >= 1 && unit.Defense <= 4), Is.True);
            Assert.That(database.Units.All(unit =>
                unit.Damage >= 3 && unit.Damage <= 5), Is.True);
        }

        [Test]
        public void BeastTagDefinesFourCreatureInstancesForSandboxEncounter()
        {
            UnitDatabaseAsset database = Resources.Load<UnitDatabaseAsset>(
                UnitDatabaseAsset.ResourcesPath);
            UnitDefinitionData[] creatures = database.Units
                .Where(unit => unit.Category == UnitCategory.Creature)
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
        public void DefaultUnitPortraitsUseNewCanonicalFramingData()
        {
            UnitDatabaseAsset database = Resources.Load<UnitDatabaseAsset>(
                UnitDatabaseAsset.ResourcesPath);

            Assert.That(database.SchemaVersion, Is.EqualTo(UnitDatabaseAsset.CurrentSchemaVersion));
            Assert.That(database.Units.All(unit =>
                unit.PortraitFitMode == PortraitFitMode.Cover), Is.True);
            Assert.That(database.Units.All(unit =>
                Mathf.Approximately(unit.PortraitScale, 1f)), Is.True);
            Assert.That(database.Units.All(unit =>
                unit.PortraitOffsetNormalized == Vector2.zero), Is.True);
            Assert.That(database.Units.All(unit => !unit.PortraitFlipX), Is.True);
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

using System.Linq;
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
    }
}

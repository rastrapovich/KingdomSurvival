using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace KingdomSurvival.BattlefieldDatabase.Tests
{
    public sealed class BattlefieldDatabaseTests
    {
        [Test]
        public void ResourcesDatabaseHasValidDefaultBattlefield()
        {
            BattlefieldDatabaseAsset database = Resources.Load<BattlefieldDatabaseAsset>(
                BattlefieldDatabaseAsset.ResourcesPath);

            Assert.That(database, Is.Not.Null);
            BattlefieldDefinitionData battlefield = database.GetSandboxBattlefield();
            Assert.That(battlefield, Is.Not.Null);
            Assert.That(battlefield.Id, Is.EqualTo("forest_clearing_01"));
            Assert.That(battlefield.Background, Is.Not.Null);
        }

        [Test]
        public void ResourcesDatabaseHasUniqueBattlefieldIds()
        {
            BattlefieldDatabaseAsset database = Resources.Load<BattlefieldDatabaseAsset>(
                BattlefieldDatabaseAsset.ResourcesPath);
            Assert.That(database, Is.Not.Null);

            HashSet<string> ids = new HashSet<string>();
            foreach (BattlefieldDefinitionData battlefield in database.Battlefields)
            {
                Assert.That(battlefield, Is.Not.Null);
                Assert.That(string.IsNullOrWhiteSpace(battlefield.Id), Is.False);
                Assert.That(ids.Add(battlefield.Id), Is.True, "Duplicate battlefield id: " + battlefield.Id);
            }
        }

        [Test]
        public void ResourcesDatabasePassesValidation()
        {
            BattlefieldDatabaseAsset database = Resources.Load<BattlefieldDatabaseAsset>(
                BattlefieldDatabaseAsset.ResourcesPath);
            Assert.That(database, Is.Not.Null);

            List<string> issues = new List<string>();
            database.CollectValidationIssues(issues);
            Assert.That(issues, Is.Empty, string.Join("\n", issues));
        }
    }
}

using System.Collections.Generic;
using System.Reflection;
using KingdomSurvival.Encounters;
using NUnit.Framework;
using UnityEngine;

// E01-T01: EncounterDefinition/EncounterPoolDefinition/EncounterDatabaseAsset.
// EncounterDatabaseAsset хранит данные в private [SerializeField]-списках
// (как BattlefieldDatabaseAsset) — для юнит-тестов без реального .asset
// файла заполняем их через reflection, это единственный способ построить
// населённый ScriptableObject в памяти без AssetDatabase.
public sealed class EncounterDefinitionTests
{
    private static EncounterDatabaseAsset CreateDatabase(
        List<EncounterPoolDefinition> pools,
        List<EncounterDefinition> encounters)
    {
        EncounterDatabaseAsset database = ScriptableObject.CreateInstance<EncounterDatabaseAsset>();
        SetPrivateField(database, "pools", pools ?? new List<EncounterPoolDefinition>());
        SetPrivateField(database, "encounters", encounters ?? new List<EncounterDefinition>());
        return database;
    }

    private static void SetPrivateField(object target, string fieldName, object value)
    {
        FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.That(field, Is.Not.Null, "Поле не найдено: " + fieldName);
        field.SetValue(target, value);
    }

    private static EncounterDefinition MakeEncounter(string id, string poolId = "POOL_01")
    {
        return new EncounterDefinition
        {
            EncounterId = id,
            DisplayName = id,
            Status = EncounterStatus.Production,
            SelectionMode = EncounterSelectionMode.Pool,
            PoolId = poolId,
            DialogueId = "some_dialogue",
            SelectionWeight = 1,
            MaxOccurrencesPerGame = 1
        };
    }

    [Test]
    public void FindById_Returns_Null_For_Unknown_Id()
    {
        EncounterDatabaseAsset database = CreateDatabase(null, new List<EncounterDefinition> { MakeEncounter("A") });
        Assert.That(database.FindById("MISSING"), Is.Null);
    }

    [Test]
    public void FindById_Returns_Matching_Encounter()
    {
        EncounterDefinition encounter = MakeEncounter("A");
        EncounterDatabaseAsset database = CreateDatabase(null, new List<EncounterDefinition> { encounter });
        Assert.That(database.FindById("A"), Is.SameAs(encounter));
    }

    [Test]
    public void GetByPool_Filters_By_PoolId_And_SelectionMode()
    {
        EncounterDefinition inPool = MakeEncounter("A", "POOL_01");
        EncounterDefinition otherPool = MakeEncounter("B", "POOL_02");
        EncounterDefinition direct = MakeEncounter("C", "POOL_01");
        direct.SelectionMode = EncounterSelectionMode.Direct;

        EncounterDatabaseAsset database = CreateDatabase(null, new List<EncounterDefinition> { inPool, otherPool, direct });

        List<EncounterDefinition> result = database.GetByPool("POOL_01");
        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].EncounterId, Is.EqualTo("A"));
    }

    [Test]
    public void CollectValidationIssues_Finds_Duplicate_EncounterId()
    {
        EncounterDatabaseAsset database = CreateDatabase(
            new List<EncounterPoolDefinition> { new EncounterPoolDefinition { PoolId = "POOL_01" } },
            new List<EncounterDefinition> { MakeEncounter("A"), MakeEncounter("A") });

        List<string> issues = new List<string>();
        database.CollectValidationIssues(issues);
        Assert.That(issues, Has.Some.Contains("Повторяющийся EncounterId"));
    }

    [Test]
    public void CollectValidationIssues_Finds_Pool_Without_PoolId()
    {
        EncounterDefinition encounter = MakeEncounter("A");
        encounter.PoolId = string.Empty;
        EncounterDatabaseAsset database = CreateDatabase(null, new List<EncounterDefinition> { encounter });

        List<string> issues = new List<string>();
        database.CollectValidationIssues(issues);
        Assert.That(issues, Has.Some.Contains("без PoolId"));
    }

    [Test]
    public void CollectValidationIssues_Finds_Missing_DialogueId_For_Production_DialogueDriven()
    {
        EncounterDefinition encounter = MakeEncounter("A");
        encounter.DialogueId = string.Empty;
        EncounterDatabaseAsset database = CreateDatabase(
            new List<EncounterPoolDefinition> { new EncounterPoolDefinition { PoolId = "POOL_01" } },
            new List<EncounterDefinition> { encounter });

        List<string> issues = new List<string>();
        database.CollectValidationIssues(issues);
        Assert.That(issues, Has.Some.Contains("без DialogueId"));
    }

    [Test]
    public void CollectValidationIssues_Finds_NonPositive_Weight_For_Pool_Encounter()
    {
        EncounterDefinition encounter = MakeEncounter("A");
        encounter.SelectionWeight = 0;
        EncounterDatabaseAsset database = CreateDatabase(
            new List<EncounterPoolDefinition> { new EncounterPoolDefinition { PoolId = "POOL_01" } },
            new List<EncounterDefinition> { encounter });

        List<string> issues = new List<string>();
        database.CollectValidationIssues(issues);
        Assert.That(issues, Has.Some.Contains("SelectionWeight"));
    }

    [Test]
    public void CollectValidationIssues_Finds_MaxOccurrences_Below_One_When_Not_Unlimited()
    {
        EncounterDefinition encounter = MakeEncounter("A");
        encounter.UnlimitedOccurrences = false;
        encounter.MaxOccurrencesPerGame = 0;
        EncounterDatabaseAsset database = CreateDatabase(
            new List<EncounterPoolDefinition> { new EncounterPoolDefinition { PoolId = "POOL_01" } },
            new List<EncounterDefinition> { encounter });

        List<string> issues = new List<string>();
        database.CollectValidationIssues(issues);
        Assert.That(issues, Has.Some.Contains("MaxOccurrencesPerGame"));
    }

    [Test]
    public void CollectValidationIssues_Finds_Flag_In_Both_Required_And_Forbidden()
    {
        EncounterDefinition encounter = MakeEncounter("A");
        encounter.RequiredFlagsAll.Add("SOME_FLAG");
        encounter.ForbiddenFlags.Add("SOME_FLAG");
        EncounterDatabaseAsset database = CreateDatabase(
            new List<EncounterPoolDefinition> { new EncounterPoolDefinition { PoolId = "POOL_01" } },
            new List<EncounterDefinition> { encounter });

        List<string> issues = new List<string>();
        database.CollectValidationIssues(issues);
        Assert.That(issues, Has.Some.Contains("Required и Forbidden"));
    }
}

using System.Collections.Generic;
using System.Reflection;
using KingdomSurvival.Encounters;
using NUnit.Framework;
using UnityEngine;

// E01-T05: EncounterSelector — pure, алгоритм §24.
public sealed class EncounterSelectorTests
{
    private static EncounterDatabaseAsset CreateDatabase(
        List<EncounterPoolDefinition> pools, List<EncounterDefinition> encounters)
    {
        EncounterDatabaseAsset database = ScriptableObject.CreateInstance<EncounterDatabaseAsset>();
        SetPrivateField(database, "pools", pools);
        SetPrivateField(database, "encounters", encounters);
        return database;
    }

    private static void SetPrivateField(object target, string fieldName, object value)
    {
        FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
        field.SetValue(target, value);
    }

    private static EncounterDefinition MakeEncounter(string id, int discoveryChance, int weight)
    {
        return new EncounterDefinition
        {
            EncounterId = id,
            Status = EncounterStatus.Production,
            SelectionMode = EncounterSelectionMode.Pool,
            PoolId = "POOL_01",
            DialogueId = "dlg",
            DiscoveryChancePercent = discoveryChance,
            SelectionWeight = weight,
            MaxOccurrencesPerGame = 1000,
            UnlimitedOccurrences = true
        };
    }

    private static NarrativeEvaluationContext MakeContext(int worldSeed = 1)
    {
        return new NarrativeEvaluationContext(new HeroProfileData(), new NarrativeStateData(), worldSeed: worldSeed);
    }

    private static EncounterOpportunity MakeOpportunity(string id = "OP_1")
    {
        return new EncounterOpportunity { OpportunityId = id, PoolId = "POOL_01", WorldHour = 0, RegionId = "road" };
    }

    [Test]
    public void Disabled_Pool_Never_Selects()
    {
        EncounterDatabaseAsset database = CreateDatabase(
            new List<EncounterPoolDefinition> { new EncounterPoolDefinition { PoolId = "POOL_01", Enabled = false, GlobalTriggerChancePercent = 100 } },
            new List<EncounterDefinition> { MakeEncounter("A", 100, 1) });

        EncounterSelectionResult result = EncounterSelector.Select(
            MakeOpportunity(), database, MakeContext(), new EncounterRuntimeStateData());

        Assert.That(result.HasSelection, Is.False);
    }

    [Test]
    public void GlobalTriggerChance_Zero_Never_Selects()
    {
        EncounterDatabaseAsset database = CreateDatabase(
            new List<EncounterPoolDefinition> { new EncounterPoolDefinition { PoolId = "POOL_01", Enabled = true, GlobalTriggerChancePercent = 0 } },
            new List<EncounterDefinition> { MakeEncounter("A", 100, 1) });

        EncounterSelectionResult result = EncounterSelector.Select(
            MakeOpportunity(), database, MakeContext(), new EncounterRuntimeStateData());

        Assert.That(result.HasSelection, Is.False);
    }

    [Test]
    public void GlobalTriggerChance_Hundred_With_Single_Guaranteed_Encounter_Always_Selects()
    {
        EncounterDatabaseAsset database = CreateDatabase(
            new List<EncounterPoolDefinition> { new EncounterPoolDefinition { PoolId = "POOL_01", Enabled = true, GlobalTriggerChancePercent = 100 } },
            new List<EncounterDefinition> { MakeEncounter("A", 100, 1) });

        for (int i = 0; i < 20; i++)
        {
            EncounterSelectionResult result = EncounterSelector.Select(
                MakeOpportunity("OP_" + i), database, MakeContext(), new EncounterRuntimeStateData());

            Assert.That(result.HasSelection, Is.True);
            Assert.That(result.SelectedEncounter.EncounterId, Is.EqualTo("A"));
        }
    }

    [Test]
    public void Ineligible_Encounter_Is_Never_Selected_Even_With_Full_Discovery_Chance()
    {
        EncounterDefinition encounter = MakeEncounter("A", 100, 1);
        encounter.Status = EncounterStatus.Draft;

        EncounterDatabaseAsset database = CreateDatabase(
            new List<EncounterPoolDefinition> { new EncounterPoolDefinition { PoolId = "POOL_01", Enabled = true, GlobalTriggerChancePercent = 100 } },
            new List<EncounterDefinition> { encounter });

        EncounterSelectionResult result = EncounterSelector.Select(
            MakeOpportunity(), database, MakeContext(), new EncounterRuntimeStateData());

        Assert.That(result.HasSelection, Is.False);
    }

    [Test]
    public void Pool_Cooldown_Blocks_Repeated_Selection()
    {
        EncounterDatabaseAsset database = CreateDatabase(
            new List<EncounterPoolDefinition> { new EncounterPoolDefinition { PoolId = "POOL_01", Enabled = true, GlobalTriggerChancePercent = 100, MinimumHoursBetweenEncounters = 24 } },
            new List<EncounterDefinition> { MakeEncounter("A", 100, 1) });

        EncounterRuntimeStateData runtime = new EncounterRuntimeStateData();
        runtime.MarkPoolTriggered("POOL_01", 0);

        EncounterOpportunity soonAfter = MakeOpportunity();
        soonAfter.WorldHour = 5;
        EncounterSelectionResult blocked = EncounterSelector.Select(soonAfter, database, MakeContext(), runtime);
        Assert.That(blocked.HasSelection, Is.False);

        EncounterOpportunity later = MakeOpportunity("OP_2");
        later.WorldHour = 30;
        EncounterSelectionResult allowed = EncounterSelector.Select(later, database, MakeContext(), runtime);
        Assert.That(allowed.HasSelection, Is.True);
    }

    [Test]
    public void Weighted_Selection_Favors_Higher_Weight_Over_Many_Opportunities()
    {
        EncounterDatabaseAsset database = CreateDatabase(
            new List<EncounterPoolDefinition> { new EncounterPoolDefinition { PoolId = "POOL_01", Enabled = true, GlobalTriggerChancePercent = 100 } },
            new List<EncounterDefinition> { MakeEncounter("HEAVY", 100, 90), MakeEncounter("LIGHT", 100, 10) });

        int heavyCount = 0;
        int lightCount = 0;
        const int trials = 300;
        for (int i = 0; i < trials; i++)
        {
            EncounterSelectionResult result = EncounterSelector.Select(
                MakeOpportunity("OP_" + i), database, MakeContext(), new EncounterRuntimeStateData());
            Assert.That(result.HasSelection, Is.True);
            if (result.SelectedEncounter.EncounterId == "HEAVY") heavyCount++; else lightCount++;
        }

        // Мягкая статистическая проверка (§87): не точное число, но явное
        // преобладание тяжёлого веса.
        Assert.That(heavyCount, Is.GreaterThan(lightCount));
    }
}

using System.Collections.Generic;
using System.Reflection;
using KingdomSurvival.Encounters;
using NUnit.Framework;
using UnityEngine;

// E01-T02.
public sealed class EncounterFlagRegistryTests
{
    private static EncounterFlagRegistryAsset CreateRegistry(List<EncounterFlagDefinition> flags)
    {
        EncounterFlagRegistryAsset registry = ScriptableObject.CreateInstance<EncounterFlagRegistryAsset>();
        FieldInfo field = typeof(EncounterFlagRegistryAsset).GetField("flags", BindingFlags.NonPublic | BindingFlags.Instance);
        field.SetValue(registry, flags ?? new List<EncounterFlagDefinition>());
        return registry;
    }

    [Test]
    public void CollectValidationIssues_Finds_Duplicate_FlagId()
    {
        EncounterFlagRegistryAsset registry = CreateRegistry(new List<EncounterFlagDefinition>
        {
            new EncounterFlagDefinition { FlagId = "F1", DisplayName = "One" },
            new EncounterFlagDefinition { FlagId = "F1", DisplayName = "Two" }
        });

        List<string> issues = new List<string>();
        registry.CollectValidationIssues(issues);
        Assert.That(issues, Has.Some.Contains("Повторяющийся FlagId"));
    }

    [Test]
    public void Active_Flag_Without_DisplayName_Produces_Warning()
    {
        EncounterFlagRegistryAsset registry = CreateRegistry(new List<EncounterFlagDefinition>
        {
            new EncounterFlagDefinition { FlagId = "F1", DisplayName = string.Empty, Status = EncounterFlagStatus.Active }
        });

        List<string> issues = new List<string>();
        registry.CollectValidationIssues(issues);
        Assert.That(issues, Has.Some.Contains("без DisplayName"));
    }

    [Test]
    public void Reserved_Flag_Without_FutureUseNotes_Is_Not_An_Issue()
    {
        // §31/§71: Reserved-флаг — намеренная закладка, не мусор. Отсутствие
        // FutureUseNotes не должно порождать issue (в отличие от Active без
        // DisplayName).
        EncounterFlagRegistryAsset registry = CreateRegistry(new List<EncounterFlagDefinition>
        {
            new EncounterFlagDefinition { FlagId = "F1", DisplayName = "Something", Status = EncounterFlagStatus.Reserved, FutureUseNotes = string.Empty }
        });

        List<string> issues = new List<string>();
        registry.CollectValidationIssues(issues);
        Assert.That(issues, Is.Empty);
    }

    [Test]
    public void IsKnownFlag_True_For_Registered_False_For_Unknown()
    {
        EncounterFlagRegistryAsset registry = CreateRegistry(new List<EncounterFlagDefinition>
        {
            new EncounterFlagDefinition { FlagId = "F1", DisplayName = "One" }
        });

        Assert.That(registry.IsKnownFlag("F1"), Is.True);
        Assert.That(registry.IsKnownFlag("UNKNOWN"), Is.False);
    }
}

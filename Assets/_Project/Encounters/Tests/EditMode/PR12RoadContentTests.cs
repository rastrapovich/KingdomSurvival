using System.Collections.Generic;
using KingdomSurvival.DialogueDatabase;
using KingdomSurvival.Encounters;
using NUnit.Framework;
using UnityEngine;

public sealed class PR12RoadContentTests
{
    [Test]
    public void EveryProductionRoadEncounter_OpensItsDialogue()
    {
        EncounterDatabaseAsset encounters = Resources.Load<EncounterDatabaseAsset>(
            EncounterDatabaseAsset.ResourcesPath);
        DialogueDatabaseAsset dialogues = Resources.Load<DialogueDatabaseAsset>(
            DialogueDatabaseAsset.ResourcesPath);
        Assert.That(encounters, Is.Not.Null);
        Assert.That(dialogues, Is.Not.Null);

        int small = 0, checks = 0;
        foreach (EncounterDefinition encounter in encounters.Encounters)
        {
            if (encounter == null || encounter.Status != EncounterStatus.Production ||
                encounter.PoolId != RoadEncounterIds.FirstRegionPoolId) continue;
            DialogueDefinitionData dialogue = dialogues.FindDialogue(encounter.DialogueId);
            Assert.That(dialogue, Is.Not.Null, encounter.EncounterId);
            List<string> issues = new List<string>();
            dialogues.CollectValidationIssuesForDialogue(encounter.DialogueId, issues);
            Assert.That(issues, Is.Empty, encounter.EncounterId);
            GameState state = new GameState();
            state.CreateNewGame();
            EncounterRunResult result = EncounterRuntimeService.StartEncounter(
                state, new HeroProfileData(), encounter,
                new EncounterOpportunity { OpportunityId = "TEST_" + encounter.EncounterId,
                    WorldHour = 24, RegionId = "road" }, dialogues, out _);
            Assert.That(result.Started, Is.True, encounter.EncounterId + ": " + result.Error);
            if (encounter.DurationClass != EncounterDurationClass.Micro &&
                encounter.DurationClass != EncounterDurationClass.Short) continue;
            small++;
            foreach (DialogueNodeData node in dialogue.Nodes)
                foreach (DialogueChoiceData choice in node.Choices)
                    if (choice.IsActiveCheck) checks++;
        }
        // 26.09.2026: 12 старых дорожных встреч списаны автором — в пуле 8 коротких сцен, 4 из них с активной проверкой.
        Assert.That(small, Is.EqualTo(8));
        Assert.That(checks, Is.EqualTo(4));
    }

    [Test]
    public void SixtyDayJourney_UsesHistoryAndNeverRepeatsAOneOffScene()
    {
        EncounterDatabaseAsset encounters = Resources.Load<EncounterDatabaseAsset>(
            EncounterDatabaseAsset.ResourcesPath);
        DialogueDatabaseAsset dialogues = Resources.Load<DialogueDatabaseAsset>(
            DialogueDatabaseAsset.ResourcesPath);
        GameState state = new GameState();
        state.CreateNewGame();
        HashSet<string> seen = new HashSet<string>();
        int silent = 0;
        for (int day = 1; day <= 60; day++)
        {
            EncounterRunResult result = EncounterRuntimeService.TryResolveOpportunity(
                state, new HeroProfileData(),
                new EncounterOpportunity { OpportunityId = "JOURNEY_" + day,
                    PoolId = RoadEncounterIds.FirstRegionPoolId,
                    RegionId = "road", WorldHour = day * 24 },
                encounters, dialogues, out _);
            if (!result.Started)
            {
                Assert.That(result.Error, Does.StartWith("no_selection:"));
                silent++;
                continue;
            }
            Assert.That(seen.Add(result.EncounterId), Is.True,
                "Одноразовое событие повторилось: " + result.EncounterId);
        }
        Assert.That(seen.Count, Is.GreaterThanOrEqualTo(8));
        Assert.That(silent, Is.GreaterThan(0));
    }
}

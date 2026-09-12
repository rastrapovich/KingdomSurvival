using System.Collections.Generic;
using System.Reflection;
using KingdomSurvival.DialogueDatabase;
using KingdomSurvival.Encounters;
using NUnit.Framework;
using UnityEngine;

// E01-T07: EncounterRuntimeService — единственное место с побочными
// эффектами. Диалоговая база строится программно через reflection по
// приватным полям (тот же паттерн, что и DialogueDatabaseCheckSystemTests.cs) —
// в удалённой среде нет Unity Editor, чтобы собрать её вручную.
public sealed class EncounterRuntimeServiceTests
{
    private const string DialogueId = "enc_test_dialogue";

    private static void SetField(object target, string fieldName, object value)
    {
        FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.IsNotNull(field, "Не найдено приватное поле " + target.GetType().Name + "." + fieldName);
        field.SetValue(target, value);
    }

    private static DialogueDatabaseAsset BuildDialogueDatabase()
    {
        DialogueSpeakerData speaker = new DialogueSpeakerData();
        SetField(speaker, "id", "narrator");
        SetField(speaker, "displayName", "Рассказчик");
        SetField(speaker, "role", string.Empty);

        DialogueTextBlockData block = new DialogueTextBlockData();
        SetField(block, "blockId", "b_main");
        SetField(block, "kind", DialogueTextBlockKind.MainLine);
        SetField(block, "speakerIdOverride", string.Empty);
        SetField(block, "text", "Тестовая сцена.");
        SetField(block, "conditions", new NarrativeConditionGroup());
        SetField(block, "hasPassiveCheck", false);
        SetField(block, "passiveCheck", new NarrativeCheckSpec { Kind = NarrativeCheckKind.Passive });
        SetField(block, "onRevealEffects", new List<NarrativeEffect>());

        DialogueChoiceData exitChoice = new DialogueChoiceData();
        SetField(exitChoice, "text", "Уйти.");
        SetField(exitChoice, "nextNodeId", string.Empty);
        SetField(exitChoice, "endsDialogue", true);
        SetField(exitChoice, "choiceId", "c_exit");
        SetField(exitChoice, "kind", DialogueChoiceKind.Exit);
        SetField(exitChoice, "conditions", new NarrativeConditionGroup());
        SetField(exitChoice, "unavailablePresentation", DialogueChoiceUnavailablePresentation.Hidden);
        SetField(exitChoice, "check", new NarrativeCheckSpec());
        SetField(exitChoice, "successNodeId", string.Empty);
        SetField(exitChoice, "failureNodeId", string.Empty);
        SetField(exitChoice, "successEffects", new List<NarrativeEffect>());
        SetField(exitChoice, "failureEffects", new List<NarrativeEffect>());

        DialogueNodeData startNode = new DialogueNodeData();
        SetField(startNode, "id", "start");
        SetField(startNode, "speakerId", "narrator");
        SetField(startNode, "text", string.Empty);
        SetField(startNode, "textBlocks", new List<DialogueTextBlockData> { block });
        SetField(startNode, "choices", new List<DialogueChoiceData> { exitChoice });
        SetField(startNode, "editorPosition", Vector2.zero);
        SetField(startNode, "hasEditorPosition", false);

        DialogueDefinitionData dialogue = new DialogueDefinitionData();
        SetField(dialogue, "id", DialogueId);
        SetField(dialogue, "title", "Тестовая сцена");
        SetField(dialogue, "category", DialogueCategory.Test);
        SetField(dialogue, "status", DialogueProductionStatus.Working);
        SetField(dialogue, "developerComment", string.Empty);
        SetField(dialogue, "startNodeId", "start");
        SetField(dialogue, "tags", new List<string>());
        SetField(dialogue, "nodes", new List<DialogueNodeData> { startNode });
        SetField(dialogue, "schemaVersion", 1);

        DialogueDatabaseAsset asset = ScriptableObject.CreateInstance<DialogueDatabaseAsset>();
        SetField(asset, "speakers", new List<DialogueSpeakerData> { speaker });
        SetField(asset, "dialogues", new List<DialogueDefinitionData> { dialogue });
        return asset;
    }

    private static EncounterDefinition MakeEncounter(string dialogueId)
    {
        return new EncounterDefinition
        {
            EncounterId = "TEST_ENC",
            Status = EncounterStatus.Production,
            SelectionMode = EncounterSelectionMode.Pool,
            PoolId = "POOL_01",
            DialogueId = dialogueId,
            SelectionWeight = 1,
            MaxOccurrencesPerGame = 1,
            FlagsSetOnStart = new List<string> { "TEST_ENC_SEEN" },
            FlagsSetOnComplete = new List<string> { "TEST_ENC_DONE" },
            ClearFlagsOnComplete = new List<string> { "TEST_ENC_SEEN" }
        };
    }

    private static GameState MakeGameState()
    {
        GameState state = new GameState();
        state.CreateNewGame();
        return state;
    }

    [Test]
    public void StartEncounter_With_Missing_Dialogue_Does_Not_Increment_TimesStarted()
    {
        GameState state = MakeGameState();
        EncounterDefinition encounter = MakeEncounter("missing_dialogue_id");
        EncounterOpportunity opportunity = new EncounterOpportunity { OpportunityId = "OP_1", WorldHour = 0 };

        EncounterRunResult result = EncounterRuntimeService.StartEncounter(
            state, new HeroProfileData(), encounter, opportunity, BuildDialogueDatabase(), out _);

        Assert.That(result.Started, Is.False);
        Assert.That(result.Error, Is.Not.Empty);
        Assert.That(state.Encounters.FindEntry("TEST_ENC"), Is.Null);
    }

    [Test]
    public void StartEncounter_With_Valid_Dialogue_Increments_TimesStarted_And_Sets_Flags()
    {
        GameState state = MakeGameState();
        EncounterDefinition encounter = MakeEncounter(DialogueId);
        EncounterOpportunity opportunity = new EncounterOpportunity { OpportunityId = "OP_1", WorldHour = 12 };

        EncounterRunResult result = EncounterRuntimeService.StartEncounter(
            state, new HeroProfileData(), encounter, opportunity, BuildDialogueDatabase(), out NarrativeDialogueRuntimeSession session);

        Assert.That(result.Started, Is.True);
        Assert.That(session.IsActive, Is.True);

        EncounterRuntimeEntry entry = state.Encounters.FindEntry("TEST_ENC");
        Assert.That(entry, Is.Not.Null);
        Assert.That(entry.TimesStarted, Is.EqualTo(1));
        Assert.That(entry.LastStartedWorldHour, Is.EqualTo(12));
        Assert.That(state.Narrative.HasFlag("TEST_ENC_SEEN"), Is.True);
    }

    [Test]
    public void TryResolveOpportunity_Does_Not_Reprocess_Same_OpportunityId()
    {
        GameState state = MakeGameState();
        EncounterDefinition encounter = MakeEncounter(DialogueId);

        EncounterDatabaseAsset database = ScriptableObject.CreateInstance<EncounterDatabaseAsset>();
        typeof(EncounterDatabaseAsset).GetField("pools", BindingFlags.NonPublic | BindingFlags.Instance)
            .SetValue(database, new List<EncounterPoolDefinition> { new EncounterPoolDefinition { PoolId = "POOL_01", Enabled = true, GlobalTriggerChancePercent = 100 } });
        typeof(EncounterDatabaseAsset).GetField("encounters", BindingFlags.NonPublic | BindingFlags.Instance)
            .SetValue(database, new List<EncounterDefinition> { encounter });

        EncounterOpportunity opportunity = new EncounterOpportunity { OpportunityId = "OP_FIXED", PoolId = "POOL_01", WorldHour = 0, RegionId = "road" };
        DialogueDatabaseAsset dialogueDatabase = BuildDialogueDatabase();

        EncounterRunResult first = EncounterRuntimeService.TryResolveOpportunity(
            state, new HeroProfileData(), opportunity, database, dialogueDatabase, out _);
        Assert.That(first.Started, Is.True);
        Assert.That(state.Encounters.FindEntry("TEST_ENC").TimesStarted, Is.EqualTo(1));

        EncounterRunResult second = EncounterRuntimeService.TryResolveOpportunity(
            state, new HeroProfileData(), opportunity, database, dialogueDatabase, out _);
        Assert.That(second.Started, Is.False);
        Assert.That(state.Encounters.FindEntry("TEST_ENC").TimesStarted, Is.EqualTo(1), "Повторная обработка той же Opportunity не должна запускать Encounter снова.");
    }

    [Test]
    public void CompleteEncounter_Increments_TimesCompleted_And_Applies_Complete_Flags()
    {
        GameState state = MakeGameState();
        EncounterDefinition encounter = MakeEncounter(DialogueId);
        state.Narrative.SetFlag("TEST_ENC_SEEN");

        EncounterRuntimeService.CompleteEncounter(state, encounter, 15);

        EncounterRuntimeEntry entry = state.Encounters.FindEntry("TEST_ENC");
        Assert.That(entry.TimesCompleted, Is.EqualTo(1));
        Assert.That(entry.LastCompletedWorldHour, Is.EqualTo(15));
        Assert.That(state.Narrative.HasFlag("TEST_ENC_DONE"), Is.True);
        Assert.That(state.Narrative.HasFlag("TEST_ENC_SEEN"), Is.False);
    }

    [Test]
    public void EnsureState_Recovers_GameState_With_Null_Encounters()
    {
        GameState state = new GameState { Narrative = new NarrativeStateData(), Encounters = null };
        Assert.DoesNotThrow(() => EncounterRuntimeService.EnsureState(state));
        Assert.That(state.Encounters, Is.Not.Null);
    }
}

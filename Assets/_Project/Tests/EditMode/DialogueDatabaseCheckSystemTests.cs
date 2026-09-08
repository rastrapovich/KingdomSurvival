using System.Collections.Generic;
using System.Reflection;
using KingdomSurvival.DialogueDatabase;
using KingdomSurvival.DialogueDatabase.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

// Демонстрационный диалог для вертикального среза "качества, проверки и
// реактивный текст" строится программно через reflection по приватным
// сериализуемым полям, а не хранится в общем KingdomSurvivalDialogues.asset:
// в удалённой среде нет Unity Editor, чтобы вручную собрать и один раз
// провизуально проверить новый ScriptableObject-граф, а рискованная ручная
// правка YAML без компилятора могла бы незаметно повредить общий ассет.
// prototype_miller и его тесты (DialogueDatabaseTests.cs) не затронуты.
public sealed class DialogueDatabaseCheckSystemTests
{
    [Test]
    public void DemoDialogue_Passes_Validation()
    {
        DialogueDatabaseAsset asset = BuildDemoDatabase();
        List<string> issues = new List<string>();
        asset.CollectValidationIssuesForDialogue("demo_checks", issues);
        Assert.That(issues, Is.Empty, string.Join("\n", issues));
    }

    [Test]
    public void BuildView_Shows_Passive_Block_And_Respects_Choice_Visibility()
    {
        DialogueDatabaseAsset asset = BuildDemoDatabase();
        HeroProfileData hero = new HeroProfileData();
        NarrativeStateData state = new NarrativeStateData();
        NarrativeDialogueRuntimeSession session = new NarrativeDialogueRuntimeSession();

        bool started = session.Start(asset, "demo_checks", hero, state, out NarrativeDialogueView view, out string error);
        Assert.IsTrue(started, error);

        Assert.AreEqual(2, view.VisibleTextBlocks.Count);
        Assert.IsTrue(HasChoice(view.AvailableChoices, "c_decisive"));
        Assert.IsTrue(HasChoice(view.AvailableChoices, "c_returnable"));
        Assert.IsTrue(HasChoice(view.AvailableChoices, "c_exit"));
        Assert.IsFalse(HasChoice(view.AvailableChoices, "c_secret_hidden"));
        Assert.IsFalse(HasChoice(view.DisabledChoices, "c_secret_hidden"));
        Assert.IsTrue(HasChoice(view.DisabledChoices, "c_secret_disabled"));

        state.SetFlag("has_key");
        NarrativeDialogueView refreshed = session.BuildView();
        Assert.IsTrue(HasChoice(refreshed.AvailableChoices, "c_secret_hidden"));
    }

    [Test]
    public void ActiveDecisive_Choice_Does_Not_Reroll_On_Revisit()
    {
        DialogueDatabaseAsset asset = BuildDemoDatabase();
        HeroProfileData hero = new HeroProfileData();
        NarrativeStateData state = new NarrativeStateData();
        NarrativeDialogueRuntimeSession session = new NarrativeDialogueRuntimeSession();
        bool started = session.Start(asset, "demo_checks", hero, state, out _, out string error);
        Assert.IsTrue(started, error);

        NarrativeDialogueSelectionResult first = session.SelectChoicePreview("c_decisive", NarrativeCheckForcedOutcome.ForceSuccess);
        Assert.IsTrue(first.CheckResult.Success);
        Assert.AreEqual("decisive_success", first.View.NodeId);

        session.SelectChoice("back1");
        Assert.AreEqual("start", session.CurrentNodeId);

        NarrativeDialogueSelectionResult second = session.SelectChoicePreview("c_decisive", NarrativeCheckForcedOutcome.ForceFailure);
        Assert.AreEqual("decisive_success", second.View.NodeId);
        Assert.IsTrue(second.CheckResult.Success);
    }

    [Test]
    public void ActiveReturnable_Locks_Then_Unlocks_Via_Effect_And_Effect_Applies_Once()
    {
        DialogueDatabaseAsset asset = BuildDemoDatabase();
        HeroProfileData hero = new HeroProfileData();
        NarrativeStateData state = new NarrativeStateData();
        NarrativeDialogueRuntimeSession session = new NarrativeDialogueRuntimeSession();
        bool started = session.Start(asset, "demo_checks", hero, state, out _, out string error);
        Assert.IsTrue(started, error);

        NarrativeDialogueSelectionResult failed = session.SelectChoicePreview("c_returnable", NarrativeCheckForcedOutcome.ForceFailure);
        Assert.IsFalse(failed.CheckResult.Success);
        Assert.AreEqual("returnable_failed", failed.View.NodeId);

        // Reveal-эффект текстового блока узла "returnable_failed" уже
        // применился внутри BuildView(), вызванного из TransitionTo.
        Assert.IsFalse(state.IsCheckLocked("chk_returnable"));
        Assert.AreEqual(1, state.AppliedEffectExecutionIds.FindAll(id => id == "eff_unlock_returnable").Count);

        session.BuildView();
        session.BuildView();
        Assert.AreEqual(1, state.AppliedEffectExecutionIds.FindAll(id => id == "eff_unlock_returnable").Count);

        session.SelectChoice("back3");
        NarrativeDialogueView backAtStart = session.BuildView();
        Assert.IsTrue(HasChoice(backAtStart.AvailableChoices, "c_returnable"));
    }

    [Test]
    public void SelectChoice_Uses_Natural_Dice_Not_Forced()
    {
        DialogueDatabaseAsset asset = BuildDemoDatabase();
        HeroProfileData hero = new HeroProfileData();
        NarrativeStateData state = new NarrativeStateData();
        NarrativeDialogueRuntimeSession session = new NarrativeDialogueRuntimeSession();
        bool started = session.Start(asset, "demo_checks", hero, state, out _, out string error, worldSeedValue: 777);
        Assert.IsTrue(started, error);

        NarrativeDialogueSelectionResult result = session.SelectChoice("c_returnable");
        Assert.IsNotNull(result.CheckResult);
        Assert.IsFalse(result.CheckResult.IsForcedByPreview);
        Assert.IsTrue(result.CheckResult.HasDice);
        Assert.AreEqual(result.CheckResult.Success ? "returnable_success" : "returnable_failed", result.View.NodeId);
    }

    [Test]
    public void Migration_Preserves_Legacy_Text_And_Adds_Main_Block()
    {
        DialogueDatabaseAsset asset = ScriptableObject.CreateInstance<DialogueDatabaseAsset>();
        SetField(asset, "speakers", new List<DialogueSpeakerData> { MakeSpeaker("narrator", "Рассказчик") });

        DialogueNodeData legacyNode = MakeNode(
            "only",
            "narrator",
            new List<DialogueTextBlockData>(),
            new List<DialogueChoiceData> { MakeExitChoice("only_exit", "Выйти.") },
            legacyText: "Старый текст без блоков.");
        DialogueDefinitionData dialogue = MakeDialogue("legacy_dialogue", "only", new List<DialogueNodeData> { legacyNode }, schemaVersion: 0);
        SetField(asset, "dialogues", new List<DialogueDefinitionData> { dialogue });

        DialogueDatabaseWindow window = ScriptableObject.CreateInstance<DialogueDatabaseWindow>();
        SetField(window, "database", asset);

        SerializedObject serializedAsset = new SerializedObject(asset);
        SerializedProperty dialogueProperty = serializedAsset.FindProperty("dialogues").GetArrayElementAtIndex(0);

        MethodInfo migrate = typeof(DialogueDatabaseWindow).GetMethod("MigrateDialogueSchema", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.IsNotNull(migrate);
        migrate.Invoke(window, new object[] { dialogueProperty });

        DialogueDefinitionData migrated = asset.FindDialogue("legacy_dialogue");
        Assert.AreEqual(DialogueDefinitionData.CurrentSchemaVersion, migrated.SchemaVersion);
        Assert.AreEqual("Старый текст без блоков.", migrated.Nodes[0].Text);
        Assert.AreEqual(1, migrated.Nodes[0].TextBlocks.Count);
        Assert.AreEqual("Старый текст без блоков.", migrated.Nodes[0].TextBlocks[0].Text);

        Object.DestroyImmediate(window);
        Object.DestroyImmediate(asset);
    }

    [Test]
    public void RegenerateNarrativeIdentifiers_Creates_New_Ids_And_Remaps_Unlock_Reference()
    {
        DialogueDatabaseAsset asset = BuildDemoDatabase();
        SerializedObject serializedAsset = new SerializedObject(asset);
        SerializedProperty dialogues = serializedAsset.FindProperty("dialogues");
        SerializedProperty source = dialogues.GetArrayElementAtIndex(0);

        source.DuplicateCommand();
        serializedAsset.ApplyModifiedProperties();
        serializedAsset.Update();
        SerializedProperty duplicate = dialogues.GetArrayElementAtIndex(1);

        MethodInfo regenerate = typeof(DialogueDatabaseWindow).GetMethod("RegenerateNarrativeIdentifiers", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.IsNotNull(regenerate);
        regenerate.Invoke(null, new object[] { duplicate });
        serializedAsset.ApplyModifiedProperties();

        DialogueDefinitionData original = asset.Dialogues[0];
        DialogueDefinitionData copy = asset.Dialogues[1];

        DialogueChoiceData originalChoice = FindChoiceById(original, "c_returnable");
        DialogueChoiceData copyChoice = FindChoiceById(copy, "c_returnable");
        Assert.AreNotEqual(originalChoice.Check.CheckId, copyChoice.Check.CheckId);

        DialogueNodeData copyFailedNode = FindNodeById(copy, "returnable_failed");
        NarrativeEffect copyUnlockEffect = copyFailedNode.TextBlocks[0].OnRevealEffects[0];
        Assert.AreEqual(copyChoice.Check.CheckId, copyUnlockEffect.StringParam);
        Assert.AreNotEqual("eff_unlock_returnable", copyUnlockEffect.EffectExecutionId);

        Object.DestroyImmediate(asset);
    }

    private static bool HasChoice(IReadOnlyList<NarrativeDialogueChoiceView> list, string choiceId)
    {
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i].ChoiceId == choiceId)
                return true;
        }
        return false;
    }

    private static DialogueChoiceData FindChoiceById(DialogueDefinitionData dialogue, string choiceId)
    {
        foreach (DialogueNodeData node in dialogue.Nodes)
        {
            foreach (DialogueChoiceData choice in node.Choices)
            {
                if (choice.ChoiceId == choiceId)
                    return choice;
            }
        }
        return null;
    }

    private static DialogueNodeData FindNodeById(DialogueDefinitionData dialogue, string nodeId)
    {
        foreach (DialogueNodeData node in dialogue.Nodes)
        {
            if (node.Id == nodeId)
                return node;
        }
        return null;
    }

    private static void SetField(object target, string fieldName, object value)
    {
        FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.IsNotNull(field, "Не найдено приватное поле " + target.GetType().Name + "." + fieldName);
        field.SetValue(target, value);
    }

    private static DialogueSpeakerData MakeSpeaker(string id, string displayName)
    {
        DialogueSpeakerData speaker = new DialogueSpeakerData();
        SetField(speaker, "id", id);
        SetField(speaker, "displayName", displayName);
        SetField(speaker, "role", string.Empty);
        return speaker;
    }

    private static DialogueTextBlockData MakeTextBlock(
        string blockId,
        DialogueTextBlockKind kind,
        string text,
        NarrativeConditionGroup conditions = null,
        NarrativeCheckSpec passiveCheck = null,
        List<NarrativeEffect> onRevealEffects = null)
    {
        DialogueTextBlockData block = new DialogueTextBlockData();
        SetField(block, "blockId", blockId);
        SetField(block, "kind", kind);
        SetField(block, "speakerIdOverride", string.Empty);
        SetField(block, "text", text);
        SetField(block, "conditions", conditions ?? new NarrativeConditionGroup());
        SetField(block, "hasPassiveCheck", passiveCheck != null);
        SetField(block, "passiveCheck", passiveCheck ?? new NarrativeCheckSpec { Kind = NarrativeCheckKind.Passive });
        SetField(block, "onRevealEffects", onRevealEffects ?? new List<NarrativeEffect>());
        return block;
    }

    private static DialogueChoiceData MakeNormalChoice(
        string choiceId,
        string text,
        string nextNodeId,
        NarrativeConditionGroup conditions = null,
        DialogueChoiceUnavailablePresentation presentation = DialogueChoiceUnavailablePresentation.Hidden)
    {
        DialogueChoiceData choice = new DialogueChoiceData();
        SetField(choice, "text", text);
        SetField(choice, "nextNodeId", nextNodeId ?? string.Empty);
        SetField(choice, "endsDialogue", false);
        SetField(choice, "choiceId", choiceId);
        SetField(choice, "kind", DialogueChoiceKind.Normal);
        SetField(choice, "conditions", conditions ?? new NarrativeConditionGroup());
        SetField(choice, "unavailablePresentation", presentation);
        SetField(choice, "check", new NarrativeCheckSpec());
        SetField(choice, "successNodeId", string.Empty);
        SetField(choice, "failureNodeId", string.Empty);
        SetField(choice, "successEffects", new List<NarrativeEffect>());
        SetField(choice, "failureEffects", new List<NarrativeEffect>());
        return choice;
    }

    private static DialogueChoiceData MakeExitChoice(string choiceId, string text)
    {
        DialogueChoiceData choice = MakeNormalChoice(choiceId, text, null);
        SetField(choice, "endsDialogue", true);
        SetField(choice, "kind", DialogueChoiceKind.Exit);
        return choice;
    }

    private static DialogueChoiceData MakeActiveChoice(
        string choiceId,
        string text,
        DialogueChoiceKind kind,
        NarrativeCheckSpec check,
        string successNodeId,
        string failureNodeId,
        List<NarrativeEffect> successEffects = null,
        List<NarrativeEffect> failureEffects = null)
    {
        DialogueChoiceData choice = new DialogueChoiceData();
        SetField(choice, "text", text);
        SetField(choice, "nextNodeId", string.Empty);
        SetField(choice, "endsDialogue", false);
        SetField(choice, "choiceId", choiceId);
        SetField(choice, "kind", kind);
        SetField(choice, "conditions", new NarrativeConditionGroup());
        SetField(choice, "unavailablePresentation", DialogueChoiceUnavailablePresentation.DisabledWithHint);
        SetField(choice, "check", check);
        SetField(choice, "successNodeId", successNodeId);
        SetField(choice, "failureNodeId", failureNodeId);
        SetField(choice, "successEffects", successEffects ?? new List<NarrativeEffect>());
        SetField(choice, "failureEffects", failureEffects ?? new List<NarrativeEffect>());
        return choice;
    }

    private static DialogueNodeData MakeNode(
        string id,
        string speakerId,
        List<DialogueTextBlockData> textBlocks,
        List<DialogueChoiceData> choices,
        string legacyText = "")
    {
        DialogueNodeData node = new DialogueNodeData();
        SetField(node, "id", id);
        SetField(node, "speakerId", speakerId);
        SetField(node, "text", legacyText);
        SetField(node, "textBlocks", textBlocks);
        SetField(node, "choices", choices);
        SetField(node, "editorPosition", Vector2.zero);
        SetField(node, "hasEditorPosition", false);
        return node;
    }

    private static DialogueDefinitionData MakeDialogue(
        string id,
        string startNodeId,
        List<DialogueNodeData> nodes,
        int schemaVersion = 1)
    {
        DialogueDefinitionData dialogue = new DialogueDefinitionData();
        SetField(dialogue, "id", id);
        SetField(dialogue, "title", "Демонстрация проверок");
        SetField(dialogue, "category", DialogueCategory.Test);
        SetField(dialogue, "status", DialogueProductionStatus.Working);
        SetField(dialogue, "developerComment", string.Empty);
        SetField(dialogue, "startNodeId", startNodeId);
        SetField(dialogue, "tags", new List<string>());
        SetField(dialogue, "nodes", nodes);
        SetField(dialogue, "schemaVersion", schemaVersion);
        return dialogue;
    }

    private static DialogueDatabaseAsset BuildDemoDatabase()
    {
        DialogueDatabaseAsset asset = ScriptableObject.CreateInstance<DialogueDatabaseAsset>();
        SetField(asset, "speakers", new List<DialogueSpeakerData> { MakeSpeaker("narrator", "Рассказчик") });

        NarrativeCheckSpec passiveCheck = new NarrativeCheckSpec
        {
            CheckId = "chk_passive",
            Kind = NarrativeCheckKind.Passive,
            Quality = HeroQuality.Instinct,
            CompetencyId = NarrativeCompetencyIds.Fieldcraft,
            Difficulty = NarrativeDifficulty.Obvious
        };

        NarrativeCheckSpec decisiveCheck = new NarrativeCheckSpec
        {
            CheckId = "chk_decisive",
            Kind = NarrativeCheckKind.ActiveDecisive,
            Quality = HeroQuality.Judgment,
            Difficulty = NarrativeDifficulty.Obvious
        };

        NarrativeCheckSpec returnableCheck = new NarrativeCheckSpec
        {
            CheckId = "chk_returnable",
            Kind = NarrativeCheckKind.ActiveReturnable,
            Quality = HeroQuality.Dexterity,
            Difficulty = NarrativeDifficulty.Obvious
        };

        NarrativeConditionGroup hiddenCondition = new NarrativeConditionGroup
        {
            Combinator = NarrativeConditionCombinator.All,
            Conditions = new List<NarrativeCondition>
            {
                new NarrativeCondition { Type = NarrativeConditionType.FlagSet, StringParam = "has_key" }
            }
        };

        NarrativeConditionGroup disabledCondition = new NarrativeConditionGroup
        {
            Combinator = NarrativeConditionCombinator.All,
            Conditions = new List<NarrativeCondition>
            {
                new NarrativeCondition { Type = NarrativeConditionType.FlagSet, StringParam = "has_key2" }
            }
        };

        DialogueNodeData startNode = MakeNode(
            "start",
            "narrator",
            new List<DialogueTextBlockData>
            {
                MakeTextBlock("b_main", DialogueTextBlockKind.MainLine, "У оврага прохладно."),
                MakeTextBlock("b_obs", DialogueTextBlockKind.Observation, "Ты замечаешь свежие следы у края.", passiveCheck: passiveCheck)
            },
            new List<DialogueChoiceData>
            {
                MakeActiveChoice("c_decisive", "Оценить обстановку.", DialogueChoiceKind.ActiveDecisive, decisiveCheck, "decisive_success", "decisive_failure"),
                MakeActiveChoice("c_returnable", "Пройти по следу.", DialogueChoiceKind.ActiveReturnable, returnableCheck, "returnable_success", "returnable_failed"),
                MakeNormalChoice("c_secret_hidden", "Достать спрятанный ключ.", "start", hiddenCondition, DialogueChoiceUnavailablePresentation.Hidden),
                MakeNormalChoice("c_secret_disabled", "Показать второй ключ.", "start", disabledCondition, DialogueChoiceUnavailablePresentation.DisabledWithHint),
                MakeExitChoice("c_exit", "Уйти.")
            });

        DialogueNodeData decisiveSuccess = MakeNode(
            "decisive_success",
            "narrator",
            new List<DialogueTextBlockData> { MakeTextBlock("b_ds", DialogueTextBlockKind.MainLine, "Ты справился.") },
            new List<DialogueChoiceData> { MakeNormalChoice("back1", "Вернуться.", "start") });

        DialogueNodeData decisiveFailure = MakeNode(
            "decisive_failure",
            "narrator",
            new List<DialogueTextBlockData> { MakeTextBlock("b_df", DialogueTextBlockKind.MainLine, "Не получилось, но ты понял направление.") },
            new List<DialogueChoiceData> { MakeNormalChoice("back2", "Вернуться.", "start") });

        DialogueNodeData returnableSuccess = MakeNode(
            "returnable_success",
            "narrator",
            new List<DialogueTextBlockData> { MakeTextBlock("b_rs", DialogueTextBlockKind.MainLine, "Прошёл по следу.") },
            new List<DialogueChoiceData> { MakeExitChoice("exit_rs", "Закончить.") });

        DialogueNodeData returnableFailed = MakeNode(
            "returnable_failed",
            "narrator",
            new List<DialogueTextBlockData>
            {
                MakeTextBlock(
                    "b_rf",
                    DialogueTextBlockKind.MainLine,
                    "Потерял след, но кое-что заметил.",
                    onRevealEffects: new List<NarrativeEffect>
                    {
                        new NarrativeEffect
                        {
                            EffectExecutionId = "eff_unlock_returnable",
                            Type = NarrativeEffectType.UnlockCheck,
                            StringParam = "chk_returnable"
                        }
                    })
            },
            new List<DialogueChoiceData> { MakeNormalChoice("back3", "Вернуться.", "start") });

        DialogueDefinitionData dialogue = MakeDialogue(
            "demo_checks",
            "start",
            new List<DialogueNodeData> { startNode, decisiveSuccess, decisiveFailure, returnableSuccess, returnableFailed });

        SetField(asset, "dialogues", new List<DialogueDefinitionData> { dialogue });
        return asset;
    }
}

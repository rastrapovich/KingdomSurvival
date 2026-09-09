using System;
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

    // §20 инструкции по визуализации проверок, пункты 1, 12: успешная
    // пассивная проверка создаёт видимый блок с CheckPresentation, и он не
    // несёт кубиков (пассивная проверка их не бросает).
    [Test]
    public void Successful_Passive_Check_Block_Has_CheckPresentation_Without_Dice()
    {
        DialogueDatabaseAsset asset = BuildDemoDatabase();
        HeroProfileData hero = new HeroProfileData();
        NarrativeStateData state = new NarrativeStateData();
        NarrativeDialogueRuntimeSession session = new NarrativeDialogueRuntimeSession();
        bool started = session.Start(asset, "demo_checks", hero, state, out NarrativeDialogueView view, out string error);
        Assert.IsTrue(started, error);

        NarrativeDialogueVisibleBlock block = FindBlock(view.VisibleTextBlocks, "b_obs");
        Assert.IsNotNull(block);
        Assert.IsNotNull(block.CheckPresentation);
        Assert.IsTrue(block.CheckPresentation.Success);
        Assert.AreEqual("Чутьё", block.CheckPresentation.QualityLabel);
        Assert.AreEqual("Следопытство", block.CheckPresentation.CompetencyLabel);
        Assert.IsFalse(block.CheckPresentation.HasDice);
        Assert.AreEqual(0, block.CheckPresentation.DieOne);
        Assert.AreEqual(0, block.CheckPresentation.DieTwo);
        Assert.IsFalse(block.IsPassiveFailurePreviewOnly);

        Object.DestroyImmediate(asset);
    }

    // §20, пункт 2: проваленная пассивная проверка отсутствует в
    // production-view — игрок не видит, что там что-то было спрятано.
    [Test]
    public void Failed_Passive_Check_Block_Is_Absent_From_Production_View()
    {
        DialogueDatabaseAsset asset = BuildPassiveFailureDatabase();
        HeroProfileData hero = new HeroProfileData();
        NarrativeStateData state = new NarrativeStateData();
        NarrativeDialogueRuntimeSession session = new NarrativeDialogueRuntimeSession();
        bool started = session.Start(asset, "passive_fail_demo", hero, state, out NarrativeDialogueView view, out string error);
        Assert.IsTrue(started, error);

        Assert.IsTrue(HasBlock(view.VisibleTextBlocks, "b_main"));
        Assert.IsFalse(HasBlock(view.VisibleTextBlocks, "b_secret"));

        Object.DestroyImmediate(asset);
    }

    // §20, пункт 14: Preview может явно запросить провалившийся пассивный
    // блок, production-путь (BuildView()) — никогда.
    [Test]
    public void Preview_Can_Show_Failed_Passive_Block_Production_Cannot()
    {
        DialogueDatabaseAsset asset = BuildPassiveFailureDatabase();
        HeroProfileData hero = new HeroProfileData();
        NarrativeStateData state = new NarrativeStateData();
        NarrativeDialogueRuntimeSession session = new NarrativeDialogueRuntimeSession();
        bool started = session.Start(asset, "passive_fail_demo", hero, state, out _, out string error);
        Assert.IsTrue(started, error);

        NarrativeDialogueView productionView = session.BuildView();
        Assert.IsFalse(HasBlock(productionView.VisibleTextBlocks, "b_secret"));

        NarrativeDialogueView previewView = session.BuildViewPreview(includeFailedPassiveChecks: true);
        NarrativeDialogueVisibleBlock failedBlock = FindBlock(previewView.VisibleTextBlocks, "b_secret");
        Assert.IsNotNull(failedBlock);
        Assert.IsTrue(failedBlock.IsPassiveFailurePreviewOnly);
        Assert.IsNotNull(failedBlock.CheckPresentation);
        Assert.IsFalse(failedBlock.CheckPresentation.Success);

        Object.DestroyImmediate(asset);
    }

    // §20, пункты 9, 10: активный успех и активный провал используют тот же
    // presentation-компонент, что и пассивная проверка.
    [Test]
    public void Active_Decisive_Success_Uses_Same_Presentation_Component()
    {
        DialogueDatabaseAsset asset = BuildDemoDatabase();
        HeroProfileData hero = new HeroProfileData();
        NarrativeStateData state = new NarrativeStateData();
        NarrativeDialogueRuntimeSession session = new NarrativeDialogueRuntimeSession();
        bool started = session.Start(asset, "demo_checks", hero, state, out _, out string error);
        Assert.IsTrue(started, error);

        NarrativeDialogueSelectionResult result = session.SelectChoicePreview("c_decisive", NarrativeCheckForcedOutcome.ForceSuccess);

        Assert.IsNotNull(result.CheckPresentation);
        Assert.AreEqual(NarrativeCheckKind.ActiveDecisive, result.CheckPresentation.Kind);
        Assert.IsTrue(result.CheckPresentation.Success);
        Assert.AreEqual("chk_decisive", result.CheckPresentation.CheckId);

        Object.DestroyImmediate(asset);
    }

    [Test]
    public void Active_Returnable_Failure_Uses_Same_Presentation_Component()
    {
        DialogueDatabaseAsset asset = BuildDemoDatabase();
        HeroProfileData hero = new HeroProfileData();
        NarrativeStateData state = new NarrativeStateData();
        NarrativeDialogueRuntimeSession session = new NarrativeDialogueRuntimeSession();
        bool started = session.Start(asset, "demo_checks", hero, state, out _, out string error);
        Assert.IsTrue(started, error);

        NarrativeDialogueSelectionResult result = session.SelectChoicePreview("c_returnable", NarrativeCheckForcedOutcome.ForceFailure);

        Assert.IsNotNull(result.CheckPresentation);
        Assert.AreEqual(NarrativeCheckKind.ActiveReturnable, result.CheckPresentation.Kind);
        Assert.IsFalse(result.CheckPresentation.Success);
        Assert.AreEqual("chk_returnable", result.CheckPresentation.CheckId);

        Object.DestroyImmediate(asset);
    }

    // §20, пункт 11: для активной проверки presentation обязан правильно
    // отражать оба кубика натурального (не Preview-принудительного) броска.
    [Test]
    public void Active_Check_Presentation_Shows_Both_Dice_On_Natural_Roll()
    {
        DialogueDatabaseAsset asset = BuildDemoDatabase();
        HeroProfileData hero = new HeroProfileData();
        NarrativeStateData state = new NarrativeStateData();
        NarrativeDialogueRuntimeSession session = new NarrativeDialogueRuntimeSession();
        bool started = session.Start(asset, "demo_checks", hero, state, out _, out string error, worldSeedValue: 555);
        Assert.IsTrue(started, error);

        NarrativeDialogueSelectionResult result = session.SelectChoice("c_returnable");

        Assert.IsNotNull(result.CheckPresentation);
        Assert.IsTrue(result.CheckPresentation.HasDice);
        Assert.AreEqual(result.CheckResult.DieOne, result.CheckPresentation.DieOne);
        Assert.AreEqual(result.CheckResult.DieTwo, result.CheckPresentation.DieTwo);
        Assert.GreaterOrEqual(result.CheckPresentation.DieOne, 1);
        Assert.LessOrEqual(result.CheckPresentation.DieOne, 6);
        Assert.GreaterOrEqual(result.CheckPresentation.DieTwo, 1);
        Assert.LessOrEqual(result.CheckPresentation.DieTwo, 6);

        Object.DestroyImmediate(asset);
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

    // §13: достижимость в режиме «Граф» должна учитывать ветвление
    // успех/провал активной проверки, а не только NextNodeId.
    [Test]
    public void GraphMode_Reachability_Follows_Active_Check_Branches()
    {
        DialogueDatabaseAsset asset = BuildDemoDatabase();
        DialogueDatabaseWindow window = ScriptableObject.CreateInstance<DialogueDatabaseWindow>();
        SetField(window, "database", asset);

        SerializedObject serializedAsset = new SerializedObject(asset);
        SerializedProperty dialogueProperty = serializedAsset.FindProperty("dialogues").GetArrayElementAtIndex(0);

        MethodInfo collectReachable = typeof(DialogueDatabaseWindow).GetMethod("CollectReachableNodeIds", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.IsNotNull(collectReachable);
        HashSet<string> reachable = (HashSet<string>)collectReachable.Invoke(window, new object[] { dialogueProperty });

        Assert.IsTrue(reachable.Contains("returnable_success"));
        Assert.IsTrue(reachable.Contains("returnable_failed"));
        Assert.IsTrue(reachable.Contains("decisive_success"));
        Assert.IsTrue(reachable.Contains("decisive_failure"));

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

        // RegenerateNarrativeIdentifiers меняет и сам ChoiceId (§13), поэтому
        // искать копию по старому "c_returnable" нельзя — берём тот же выбор
        // по позиции в узле: порядок узлов/выборов при дублировании не
        // меняется, регенерируются только сами идентификаторы.
        FindChoicePosition(original, "c_returnable", out int returnableNodeIndex, out int returnableChoiceIndex);
        DialogueChoiceData originalChoice = original.Nodes[returnableNodeIndex].Choices[returnableChoiceIndex];
        DialogueChoiceData copyChoice = copy.Nodes[returnableNodeIndex].Choices[returnableChoiceIndex];
        Assert.AreNotEqual(originalChoice.Check.CheckId, copyChoice.Check.CheckId);

        DialogueNodeData copyFailedNode = FindNodeById(copy, "returnable_failed");
        NarrativeEffect copyUnlockEffect = copyFailedNode.TextBlocks[0].OnRevealEffects[0];
        Assert.AreEqual(copyChoice.Check.CheckId, copyUnlockEffect.StringParam);
        Assert.AreNotEqual("eff_unlock_returnable", copyUnlockEffect.EffectExecutionId);

        Object.DestroyImmediate(asset);
    }

    // §19: "единственная обязательная улика, закрытая одной проверкой".
    [Test]
    public void SingleDecisiveKnowledgeGrant_Is_Flagged_As_Fragile()
    {
        DialogueDatabaseAsset asset = BuildKnowledgeGateDatabase(grantOnDecisiveOnly: true);
        List<string> issues = new List<string>();
        asset.CollectValidationIssuesForDialogue("knowledge_gate", issues);

        Assert.IsTrue(issues.Exists(issue => issue.Contains("secret_clue")), string.Join("\n", issues));

        Object.DestroyImmediate(asset);
    }

    [Test]
    public void KnowledgeGrant_With_Alternate_Source_Is_Not_Flagged()
    {
        DialogueDatabaseAsset asset = BuildKnowledgeGateDatabase(grantOnDecisiveOnly: false);
        List<string> issues = new List<string>();
        asset.CollectValidationIssuesForDialogue("knowledge_gate", issues);

        Assert.IsFalse(issues.Exists(issue => issue.Contains("secret_clue")), string.Join("\n", issues));

        Object.DestroyImmediate(asset);
    }

    private static DialogueDatabaseAsset BuildKnowledgeGateDatabase(bool grantOnDecisiveOnly)
    {
        DialogueDatabaseAsset asset = ScriptableObject.CreateInstance<DialogueDatabaseAsset>();
        SetField(asset, "speakers", new List<DialogueSpeakerData> { MakeSpeaker("narrator", "Рассказчик") });

        NarrativeConditionGroup requiresClue = new NarrativeConditionGroup
        {
            Combinator = NarrativeConditionCombinator.All,
            Conditions = new List<NarrativeCondition>
            {
                new NarrativeCondition { Type = NarrativeConditionType.KnowledgeKnown, StringParam = "secret_clue" }
            }
        };

        NarrativeCheckSpec decisiveCheck = new NarrativeCheckSpec
        {
            CheckId = "gate_decisive",
            Kind = NarrativeCheckKind.ActiveDecisive,
            Quality = HeroQuality.Judgment,
            Difficulty = NarrativeDifficulty.Obvious
        };

        List<DialogueChoiceData> startChoices = new List<DialogueChoiceData>
        {
            MakeActiveChoice(
                "c_decisive_grant",
                "Разгадать тайну.",
                DialogueChoiceKind.ActiveDecisive,
                decisiveCheck,
                "granted",
                "not_granted",
                successEffects: new List<NarrativeEffect>
                {
                    new NarrativeEffect { EffectExecutionId = "eff_grant_clue", Type = NarrativeEffectType.AddKnowledge, StringParam = "secret_clue" }
                }),
            MakeNormalChoice("c_use_clue", "Использовать разгадку.", "start", requiresClue, DialogueChoiceUnavailablePresentation.Hidden),
            MakeExitChoice("c_exit", "Уйти.")
        };

        if (!grantOnDecisiveOnly)
            startChoices.Add(MakeNormalChoice("c_alt_grant", "Спросить прямо.", "granted_alt"));

        DialogueNodeData startNode = MakeNode(
            "start",
            "narrator",
            new List<DialogueTextBlockData> { MakeTextBlock("b_main", DialogueTextBlockKind.MainLine, "Начало.") },
            startChoices);

        DialogueNodeData grantedNode = MakeNode(
            "granted",
            "narrator",
            new List<DialogueTextBlockData> { MakeTextBlock("b_g", DialogueTextBlockKind.MainLine, "Тайна раскрыта.") },
            new List<DialogueChoiceData> { MakeExitChoice("exit_g", "Уйти.") });

        DialogueNodeData notGrantedNode = MakeNode(
            "not_granted",
            "narrator",
            new List<DialogueTextBlockData> { MakeTextBlock("b_ng", DialogueTextBlockKind.MainLine, "Тайна осталась загадкой.") },
            new List<DialogueChoiceData> { MakeExitChoice("exit_ng", "Уйти.") });

        List<DialogueNodeData> nodes = new List<DialogueNodeData> { startNode, grantedNode, notGrantedNode };

        if (!grantOnDecisiveOnly)
        {
            DialogueNodeData grantedAltNode = MakeNode(
                "granted_alt",
                "narrator",
                new List<DialogueTextBlockData>
                {
                    MakeTextBlock(
                        "b_ga",
                        DialogueTextBlockKind.MainLine,
                        "Тебе рассказали прямо.",
                        onRevealEffects: new List<NarrativeEffect>
                        {
                            new NarrativeEffect
                            {
                                EffectExecutionId = "eff_grant_clue_alt",
                                Type = NarrativeEffectType.AddKnowledge,
                                StringParam = "secret_clue"
                            }
                        })
                },
                new List<DialogueChoiceData> { MakeExitChoice("exit_ga", "Уйти.") });
            nodes.Add(grantedAltNode);
        }

        DialogueDefinitionData dialogue = MakeDialogue("knowledge_gate", "start", nodes);
        SetField(asset, "dialogues", new List<DialogueDefinitionData> { dialogue });
        return asset;
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

    private static bool HasBlock(IReadOnlyList<NarrativeDialogueVisibleBlock> list, string blockId)
    {
        return FindBlock(list, blockId) != null;
    }

    private static NarrativeDialogueVisibleBlock FindBlock(IReadOnlyList<NarrativeDialogueVisibleBlock> list, string blockId)
    {
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i].BlockId == blockId)
                return list[i];
        }
        return null;
    }

    // Провал гарантирован для героя по умолчанию (все качества = 5):
    // база(6) + Чутьё(5) = 11 < VeryHard(19), но проверка остаётся
    // теоретически достижимой (6+10 качество+3 контекст = 19) — иначе
    // валидатор базы пометил бы её как невозможную (§19 инструкции по
    // Главе 01). Используется §20, пункты 2 и 14 — "провал не показан в
    // production" / "Preview может показать".
    private static DialogueDatabaseAsset BuildPassiveFailureDatabase()
    {
        DialogueDatabaseAsset asset = ScriptableObject.CreateInstance<DialogueDatabaseAsset>();
        SetField(asset, "speakers", new List<DialogueSpeakerData> { MakeSpeaker("narrator", "Рассказчик") });

        NarrativeCheckSpec passiveCheck = new NarrativeCheckSpec
        {
            CheckId = "chk_passive_fail",
            Kind = NarrativeCheckKind.Passive,
            Quality = HeroQuality.Instinct,
            Difficulty = NarrativeDifficulty.VeryHard
        };

        DialogueNodeData startNode = MakeNode(
            "start",
            "narrator",
            new List<DialogueTextBlockData>
            {
                MakeTextBlock("b_main", DialogueTextBlockKind.MainLine, "Всё как обычно."),
                MakeTextBlock("b_secret", DialogueTextBlockKind.Observation, "Скрытая деталь.", passiveCheck: passiveCheck)
            },
            new List<DialogueChoiceData> { MakeExitChoice("c_exit", "Уйти.") });

        DialogueDefinitionData dialogue = MakeDialogue("passive_fail_demo", "start", new List<DialogueNodeData> { startNode });
        SetField(asset, "dialogues", new List<DialogueDefinitionData> { dialogue });
        return asset;
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

    // Позиция выбора по его текущему ChoiceId. В отличие от FindChoiceById,
    // используется до регенерации идентификаторов — сама позиция (индекс
    // узла + индекс выбора в узле) остаётся стабильной после дублирования,
    // даже когда RegenerateNarrativeIdentifiers меняет ChoiceId.
    private static void FindChoicePosition(DialogueDefinitionData dialogue, string choiceId, out int nodeIndex, out int choiceIndex)
    {
        for (int n = 0; n < dialogue.Nodes.Count; n++)
        {
            DialogueNodeData node = dialogue.Nodes[n];
            for (int c = 0; c < node.Choices.Count; c++)
            {
                if (node.Choices[c].ChoiceId == choiceId)
                {
                    nodeIndex = n;
                    choiceIndex = c;
                    return;
                }
            }
        }

        throw new InvalidOperationException("Choice not found: " + choiceId);
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

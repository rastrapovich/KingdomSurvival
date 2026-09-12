using System.Collections.Generic;
using System.Reflection;
using KingdomSurvival.DialogueDatabase;
using NUnit.Framework;
using UnityEngine;

// P04-T05 — четыре постоянных жителя Дома (Остафий/Лада/Мирон/Ульяна) и
// синхронизация портрета со SpeakerId. Раздел 21 производственной
// инструкции. Синтетические спикеры строятся через reflection по приватным
// полям — тот же паттерн, что и в DialogueDatabaseCheckSystemTests.cs —
// чтобы не зависеть от Unity Editor для сборки ScriptableObject-графа.
// Тесты против chapter01_dialogue_01/02/03 читают реальный
// KingdomSurvivalDialogues.asset (тот же способ, что DialogueDatabaseTests.cs).
public sealed class Chapter01FourResidentsTests
{
    private const string D01 = "chapter01_dialogue_01_ordinary_morning";
    private const string D02 = "chapter01_dialogue_02_people_of_the_house";
    private const string D03 = "chapter01_dialogue_03_first_pressure";

    private static readonly string[] HouseResidentDialogueIds = { D01, D02, D03 };

    private static NarrativeDialogueView AdvanceCurrentNodeText(
        NarrativeDialogueRuntimeSession session,
        NarrativeDialogueView view,
        List<NarrativeDialogueVisibleBlock> observedBlocks = null)
    {
        int guard = 0;
        while (true)
        {
            Assert.LessOrEqual(view.VisibleTextBlocks.Count, 1, "Один шаг показал несколько реплик.");
            if (view.VisibleTextBlocks.Count == 1)
                observedBlocks?.Add(view.VisibleTextBlocks[0]);

            if (view.AvailableChoices.Count != 1 ||
                view.AvailableChoices[0].ChoiceId != NarrativeDialogueRuntimeSession.SequentialContinueChoiceId)
            {
                return view;
            }

            Assert.Less(guard++, 64, "Зациклен runtime-переход между репликами.");
            NarrativeDialogueSelectionResult result = session.SelectChoice(
                NarrativeDialogueRuntimeSession.SequentialContinueChoiceId);
            Assert.IsFalse(result.DialogueEnded);
            view = result.View;
        }
    }

    [Test]
    public void FourResidentSpeakers_Exist_With_StableIds_And_DisplayNames()
    {
        DialogueDatabaseAsset database = Resources.Load<DialogueDatabaseAsset>(DialogueDatabaseAsset.ResourcesPath);
        Assert.IsNotNull(database);

        AssertSpeaker(database, "ostafiy", "Остафий");
        AssertSpeaker(database, "lada", "Лада");
        AssertSpeaker(database, "miron", "Мирон");
        AssertSpeaker(database, "ulyana", "Ульяна");
    }

    private static void AssertSpeaker(DialogueDatabaseAsset database, string id, string expectedDisplayName)
    {
        DialogueSpeakerData speaker = database.FindSpeaker(id);
        Assert.IsNotNull(speaker, "Не найден Speaker '" + id + "'.");
        Assert.AreEqual(expectedDisplayName, speaker.DisplayName, "DisplayName для '" + id + "'.");
        Assert.That(speaker.Role, Is.Not.Null.And.Not.Empty, "Role для '" + id + "' пуст.");
    }

    [Test]
    public void SpeakerPortrait_RoundTrips_After_Assignment()
    {
        Texture2D texture = new Texture2D(4, 4);
        Sprite sprite = Sprite.Create(texture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f));

        try
        {
            DialogueDatabaseAsset asset = ScriptableObject.CreateInstance<DialogueDatabaseAsset>();
            DialogueSpeakerData speaker = MakeSpeaker("ostafiy", "Остафий", "Хранитель старого порядка", sprite);
            SetField(asset, "speakers", new List<DialogueSpeakerData> { speaker });

            DialogueSpeakerData found = asset.FindSpeaker("ostafiy");
            Assert.IsNotNull(found);
            Assert.AreEqual(sprite, found.Portrait);
        }
        finally
        {
            Object.DestroyImmediate(sprite);
            Object.DestroyImmediate(texture);
        }
    }

    [Test]
    public void SpeakerWithoutPortrait_Is_Safe_To_Query()
    {
        DialogueSpeakerData speaker = MakeSpeaker("test_speaker", "Тест", string.Empty, null);
        Assert.IsNull(speaker.Portrait);
        Assert.AreEqual("Тест", speaker.DisplayName);
    }

    [Test]
    public void RealDatabase_FourResidents_Have_No_Portrait_Assigned_Yet_And_Do_Not_Throw()
    {
        // Пока никто не назначил Sprite в окне "Говорящие" — все четверо
        // должны безопасно возвращать null, а не бросать исключение.
        DialogueDatabaseAsset database = Resources.Load<DialogueDatabaseAsset>(DialogueDatabaseAsset.ResourcesPath);
        Assert.IsNotNull(database);

        foreach (string id in new[] { "ostafiy", "lada", "miron", "ulyana" })
        {
            DialogueSpeakerData speaker = database.FindSpeaker(id);
            Assert.IsNotNull(speaker);
            Assert.DoesNotThrow(() => { Sprite _ = speaker.Portrait; });
        }
    }

    [Test]
    public void HouseResidentDialogues_Pass_Validation()
    {
        DialogueDatabaseAsset database = Resources.Load<DialogueDatabaseAsset>(DialogueDatabaseAsset.ResourcesPath);
        Assert.IsNotNull(database);

        foreach (string dialogueId in HouseResidentDialogueIds)
        {
            List<string> issues = new List<string>();
            database.CollectValidationIssuesForDialogue(dialogueId, issues);
            Assert.That(issues, Is.Empty, dialogueId + ":\n" + string.Join("\n", issues));
        }
    }

    [Test]
    public void HouseResidentDialogues_Use_Only_Known_SpeakerIds_Including_Overrides()
    {
        // DialogueDatabaseAsset.CollectValidationIssues проверяет только
        // node.SpeakerId, но не speakerIdOverride текстовых блоков — этот
        // тест закрывает именно override, которым активно пользуются N01–N03.
        DialogueDatabaseAsset database = Resources.Load<DialogueDatabaseAsset>(DialogueDatabaseAsset.ResourcesPath);
        Assert.IsNotNull(database);

        foreach (string dialogueId in HouseResidentDialogueIds)
        {
            DialogueDefinitionData dialogue = database.FindDialogue(dialogueId);
            Assert.IsNotNull(dialogue, dialogueId);

            for (int nodeIndex = 0; nodeIndex < dialogue.Nodes.Count; nodeIndex++)
            {
                DialogueNodeData node = dialogue.Nodes[nodeIndex];
                Assert.IsNotNull(
                    database.FindSpeaker(node.SpeakerId),
                    dialogueId + "/" + node.Id + ": неизвестный speakerId '" + node.SpeakerId + "'.");

                for (int blockIndex = 0; blockIndex < node.TextBlocks.Count; blockIndex++)
                {
                    string overrideId = node.TextBlocks[blockIndex].SpeakerIdOverride;
                    if (string.IsNullOrWhiteSpace(overrideId))
                        continue;

                    Assert.IsNotNull(
                        database.FindSpeaker(overrideId),
                        dialogueId + "/" + node.Id + "/" + node.TextBlocks[blockIndex].BlockId +
                        ": неизвестный speakerIdOverride '" + overrideId + "'.");
                }
            }
        }
    }

    [Test]
    public void N02_View_Reports_Correct_SpeakerId_And_DisplayName_Per_Block()
    {
        DialogueDatabaseAsset database = Resources.Load<DialogueDatabaseAsset>(DialogueDatabaseAsset.ResourcesPath);
        Assert.IsNotNull(database);

        NarrativeDialogueRuntimeSession session = new NarrativeDialogueRuntimeSession();
        bool started = session.Start(
            database,
            D02,
            new HeroProfileData(),
            new NarrativeStateData(),
            out NarrativeDialogueView view,
            out string error);

        Assert.IsTrue(started, error);

        List<NarrativeDialogueVisibleBlock> observed = new List<NarrativeDialogueVisibleBlock>();
        AdvanceCurrentNodeText(session, view, observed);

        string[] expectedSpeakerSequence = { "narrator", "ostafiy", "lada", "miron", "ulyana", "ostafiy", "lada" };
        Assert.AreEqual(expectedSpeakerSequence.Length, observed.Count);
        for (int i = 0; i < expectedSpeakerSequence.Length; i++)
        {
            Assert.AreEqual(expectedSpeakerSequence[i], observed[i].SpeakerId, "block #" + i);
        }

        Assert.AreEqual("Остафий", observed[1].SpeakerDisplayName);
        Assert.AreEqual("Лада", observed[2].SpeakerDisplayName);
        Assert.AreEqual("Мирон", observed[3].SpeakerDisplayName);
        Assert.AreEqual("Ульяна", observed[4].SpeakerDisplayName);
    }

    [Test]
    public void N01_Transition_Between_Nodes_Updates_SpeakerId_Each_Time()
    {
        DialogueDatabaseAsset database = Resources.Load<DialogueDatabaseAsset>(DialogueDatabaseAsset.ResourcesPath);
        Assert.IsNotNull(database);

        NarrativeDialogueRuntimeSession session = new NarrativeDialogueRuntimeSession();
        bool started = session.Start(
            database,
            D01,
            new HeroProfileData(),
            new NarrativeStateData(),
            out NarrativeDialogueView view,
            out string error);

        Assert.IsTrue(started, error);
        Assert.AreEqual("ulyana", view.VisibleTextBlocks[0].SpeakerId);
        view = AdvanceCurrentNodeText(session, view);

        NarrativeDialogueSelectionResult toOstafiy = session.SelectChoice("chapter01.node.01_skip_choice");
        Assert.IsFalse(toOstafiy.DialogueEnded);
        Assert.AreEqual("ostafiy", toOstafiy.View.VisibleTextBlocks[0].SpeakerId);
        Assert.AreEqual("Остафий", toOstafiy.View.VisibleTextBlocks[0].SpeakerDisplayName);

        NarrativeDialogueSelectionResult toLada = session.SelectChoice("chapter01.node.01_ostafiy_exit");
        Assert.IsFalse(toLada.DialogueEnded);
        Assert.AreEqual("lada", toLada.View.VisibleTextBlocks[0].SpeakerId);
        Assert.AreEqual("Лада", toLada.View.VisibleTextBlocks[0].SpeakerDisplayName);

        NarrativeDialogueSelectionResult toMill = session.SelectChoice("chapter01.node.01_lada_exit");
        Assert.IsFalse(toMill.DialogueEnded);
        Assert.AreEqual("narrator", toMill.View.VisibleTextBlocks[0].SpeakerId);
    }

    [Test]
    public void N01_PassiveJudgment_Failure_Does_Not_Block_Mandatory_Path()
    {
        DialogueDatabaseAsset database = Resources.Load<DialogueDatabaseAsset>(DialogueDatabaseAsset.ResourcesPath);
        Assert.IsNotNull(database);

        HeroProfileData hero = new HeroProfileData { Judgment = HeroProfileData.MinQualityValue };
        NarrativeStateData state = new NarrativeStateData();
        NarrativeDialogueRuntimeSession session = new NarrativeDialogueRuntimeSession();

        bool started = session.Start(database, D01, hero, state, out NarrativeDialogueView view, out string error);
        Assert.IsTrue(started, error);
        view = AdvanceCurrentNodeText(session, view);

        // Суждение 1: пассивная проверка (сложность 11) провалена. Новая
        // presentation-семантика (инструкция "новое отображение пассивных
        // наблюдений и проверок") — блок наблюдения остаётся видимым
        // (СУЖДЕНИЕ: ПРОВАЛ), но текст не раскрыт и путь не заблокирован.
        NarrativeDialogueVisibleBlock observationBlock = null;
        foreach (NarrativeDialogueVisibleBlock candidate in view.VisibleTextBlocks)
        {
            if (candidate.BlockId == "chapter01.node.01_observation")
                observationBlock = candidate;
        }
        Assert.IsNotNull(observationBlock);
        Assert.IsNotNull(observationBlock.CheckPresentation);
        Assert.IsFalse(observationBlock.CheckPresentation.Success);
        Assert.IsFalse(observationBlock.IsTextRevealed);
        Assert.IsEmpty(observationBlock.Text);
        Assert.IsFalse(state.HasKnowledge("chapter01.knowledge.second_loaf_is_ration"));

        // P04-T04: baseline фиксируется на первом обязательном блоке
        // независимо от исхода пассивного Суждения (раздел 21 инструкции
        // P04-T04 — "Passive check independence").
        Assert.IsTrue(state.HasFlag("chapter01.flag.home_baseline_captured"));

        view = session.SelectChoice("chapter01.node.01_skip_choice").View;
        view = AdvanceCurrentNodeText(session, view);
        view = session.SelectChoice("chapter01.node.01_ostafiy_exit").View;
        view = AdvanceCurrentNodeText(session, view);
        view = session.SelectChoice("chapter01.node.01_lada_exit").View;
        view = AdvanceCurrentNodeText(session, view);
        NarrativeDialogueSelectionResult final = session.SelectChoice("chapter01.node.01_exit");

        Assert.IsTrue(final.DialogueEnded);
        Assert.IsTrue(state.HasFlag("chapter01.flag.started"));
        Assert.IsTrue(state.HasFlag("chapter01.flag.home_intro_seen"));
    }

    [Test]
    public void N01_PassiveJudgment_Success_Grants_SecondLoaf_Knowledge_Without_Blocking_Path()
    {
        DialogueDatabaseAsset database = Resources.Load<DialogueDatabaseAsset>(DialogueDatabaseAsset.ResourcesPath);
        Assert.IsNotNull(database);

        HeroProfileData hero = new HeroProfileData { Judgment = HeroProfileData.MaxQualityValue };
        NarrativeStateData state = new NarrativeStateData();
        NarrativeDialogueRuntimeSession session = new NarrativeDialogueRuntimeSession();

        bool started = session.Start(database, D01, hero, state, out NarrativeDialogueView view, out string error);
        Assert.IsTrue(started, error);
        view = AdvanceCurrentNodeText(session, view);

        NarrativeDialogueVisibleBlock observationBlock = null;
        foreach (NarrativeDialogueVisibleBlock candidate in view.VisibleTextBlocks)
        {
            if (candidate.BlockId == "chapter01.node.01_observation")
                observationBlock = candidate;
        }
        Assert.IsNotNull(observationBlock);
        Assert.IsTrue(observationBlock.CheckPresentation.Success);
        Assert.IsTrue(observationBlock.IsTextRevealed);
        Assert.IsNotEmpty(observationBlock.Text);
        Assert.IsTrue(state.HasKnowledge("chapter01.knowledge.second_loaf_is_ration"));

        // P04-T04: тот же маркер фиксируется и при успехе проверки — не
        // зависит от исхода Суждения.
        Assert.IsTrue(state.HasFlag("chapter01.flag.home_baseline_captured"));

        view = session.SelectChoice("chapter01.node.01_skip_choice").View;
        view = AdvanceCurrentNodeText(session, view);
        view = session.SelectChoice("chapter01.node.01_ostafiy_exit").View;
        view = AdvanceCurrentNodeText(session, view);
        view = session.SelectChoice("chapter01.node.01_lada_exit").View;
        view = AdvanceCurrentNodeText(session, view);
        NarrativeDialogueSelectionResult final = session.SelectChoice("chapter01.node.01_exit");

        Assert.IsTrue(final.DialogueEnded);
    }

    [Test]
    public void N01_Opening_Captures_HomeBaseline_Marker()
    {
        DialogueDatabaseAsset database = Resources.Load<DialogueDatabaseAsset>(DialogueDatabaseAsset.ResourcesPath);
        Assert.IsNotNull(database);

        NarrativeStateData state = new NarrativeStateData();
        Assert.IsFalse(state.HasFlag("chapter01.flag.home_baseline_captured"));

        NarrativeDialogueRuntimeSession session = new NarrativeDialogueRuntimeSession();
        bool started = session.Start(
            database,
            D01,
            new HeroProfileData(),
            state,
            out NarrativeDialogueView view,
            out string error);

        Assert.IsTrue(started, error);
        Assert.IsTrue(state.HasFlag("chapter01.flag.home_baseline_captured"));
    }

    [Test]
    public void N03_Distributes_One_Reaction_Per_Resident_Without_A_Single_Ready_Truth()
    {
        DialogueDatabaseAsset database = Resources.Load<DialogueDatabaseAsset>(DialogueDatabaseAsset.ResourcesPath);
        Assert.IsNotNull(database);

        NarrativeDialogueRuntimeSession session = new NarrativeDialogueRuntimeSession();
        bool started = session.Start(
            database,
            D03,
            new HeroProfileData(),
            new NarrativeStateData(),
            out NarrativeDialogueView view,
            out string error);

        Assert.IsTrue(started, error);

        List<NarrativeDialogueVisibleBlock> observed = new List<NarrativeDialogueVisibleBlock>();
        AdvanceCurrentNodeText(session, view, observed);
        HashSet<string> speakerIds = new HashSet<string>();
        foreach (NarrativeDialogueVisibleBlock block in observed)
            speakerIds.Add(block.SpeakerId);

        Assert.That(speakerIds, Is.EquivalentTo(new[] { "narrator", "miron", "lada", "ostafiy", "ulyana" }));
    }

    // --- Синтетический builder (reflection по приватным полям), тот же
    // паттерн, что DialogueDatabaseCheckSystemTests.MakeSpeaker/SetField. ---

    private static void SetField(object target, string fieldName, object value)
    {
        FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.IsNotNull(field, "Не найдено приватное поле " + target.GetType().Name + "." + fieldName);
        field.SetValue(target, value);
    }

    private static DialogueSpeakerData MakeSpeaker(string id, string displayName, string role, Sprite portrait)
    {
        DialogueSpeakerData speaker = new DialogueSpeakerData();
        SetField(speaker, "id", id);
        SetField(speaker, "displayName", displayName);
        SetField(speaker, "role", role ?? string.Empty);
        SetField(speaker, "portrait", portrait);
        return speaker;
    }
}

using System.Collections.Generic;
using KingdomSurvival.DialogueDatabase;
using NUnit.Framework;

// Каноническое presentation-правило: одна реплика = один шаг. Runtime
// обязан отдавать максимум один VisibleTextBlock, а группировка служит
// последней защитой перед UI и отклоняет многорепличный view.
public sealed class NarrativeUiHistoryGroupingTests
{
    private static NarrativeDialogueVisibleBlock MakeBlock(
        string speaker,
        string role,
        string text,
        DialogueTextBlockKind kind = DialogueTextBlockKind.MainLine,
        NarrativeCheckPresentationData checkPresentation = null)
    {
        return new NarrativeDialogueVisibleBlock
        {
            BlockId = "b_" + text.GetHashCode(),
            Kind = kind,
            SpeakerId = speaker,
            SpeakerDisplayName = speaker,
            SpeakerRole = role,
            Text = text,
            CheckPresentation = checkPresentation,
            IsTextRevealed = checkPresentation == null || checkPresentation.Success
        };
    }

    private static NarrativeCheckPresentationData MakePresentation(bool success)
    {
        return new NarrativeCheckPresentationData
        {
            CheckId = "chk_test",
            Kind = NarrativeCheckKind.Passive,
            Quality = HeroQuality.Judgment,
            QualityLabel = "Суждение",
            QualityValue = 5,
            Success = success,
            Difficulty = 11,
            DifficultyLabel = "Простая",
            PassiveBase = 6,
            AppliedModifiers = new List<NarrativeAppliedModifierSnapshot>(),
            Total = success ? 11 : 7
        };
    }

    [Test]
    public void SingleBlock_ProducesSingleSpeakerSegment()
    {
        List<NarrativeDialogueVisibleBlock> blocks = new List<NarrativeDialogueVisibleBlock>
        {
            MakeBlock("narrator", "Голос сцены", "У мельницы уже шумит колесо.")
        };

        List<NarrativeUiHistorySegment> segments = NarrativeUiHistoryGrouping.BuildSegments(null, blocks);

        Assert.AreEqual(1, segments.Count);
        Assert.AreEqual(NarrativeUiSegmentKind.SpeakerParagraphs, segments[0].Kind);
        Assert.AreEqual("narrator", segments[0].SpeakerDisplayName);
        Assert.AreEqual(1, segments[0].Paragraphs.Count);
        Assert.AreEqual("У мельницы уже шумит колесо.", segments[0].Paragraphs[0]);
    }

    [Test]
    public void MultipleBlocks_AreRejected_EvenForSameSpeaker()
    {
        List<NarrativeDialogueVisibleBlock> blocks = new List<NarrativeDialogueVisibleBlock>
        {
            MakeBlock("narrator", "Голос сцены", "Первая фраза."),
            MakeBlock("narrator", "Голос сцены", "Вторая фраза.")
        };

        Assert.Throws<System.InvalidOperationException>(
            () => NarrativeUiHistoryGrouping.BuildSegments(null, blocks));
    }

    [Test]
    public void MultipleSpeakers_AreRejectedInsteadOfGroupedAsDialogue()
    {
        List<NarrativeDialogueVisibleBlock> blocks = new List<NarrativeDialogueVisibleBlock>
        {
            MakeBlock("ostafiy", "Хранитель", "Опять к седьмому."),
            MakeBlock("lada", "Мастер", "Задвижку могло подпереть веткой.")
        };

        Assert.Throws<System.InvalidOperationException>(
            () => NarrativeUiHistoryGrouping.BuildSegments(null, blocks));
    }

    [Test]
    public void CheckedObservation_ProducesOneCheckedSegment()
    {
        NarrativeCheckPresentationData presentation = MakePresentation(success: true);
        List<NarrativeDialogueVisibleBlock> blocks = new List<NarrativeDialogueVisibleBlock>
        {
            MakeBlock(
                "ulyana",
                "Семейный голос",
                "Дело, а не дар.",
                DialogueTextBlockKind.Observation,
                presentation)
        };

        List<NarrativeUiHistorySegment> segments = NarrativeUiHistoryGrouping.BuildSegments(null, blocks);

        Assert.AreEqual(1, segments.Count);
        Assert.AreEqual(NarrativeUiSegmentKind.CheckedObservation, segments[0].Kind);
        Assert.AreSame(presentation, segments[0].CheckPresentation);
        Assert.AreEqual("Дело, а не дар.", segments[0].Paragraphs[0]);
    }

    // Провал: IsTextRevealed=false в реальном runtime means Text == "" —
    // сегмент всё равно строится (блок присутствует), просто с пустым
    // текстом; UI сам решает не приклеивать тире (BuildNarrativePassiveCheckLine).
    [Test]
    public void CheckedObservation_Failure_ProducesSegmentWithEmptyText()
    {
        NarrativeCheckPresentationData presentation = MakePresentation(success: false);
        NarrativeDialogueVisibleBlock failedBlock = new NarrativeDialogueVisibleBlock
        {
            BlockId = "b_secret",
            Kind = DialogueTextBlockKind.Observation,
            SpeakerId = "ulyana",
            SpeakerDisplayName = "Ульяна",
            SpeakerRole = "Семейный голос",
            Text = string.Empty,
            CheckPresentation = presentation,
            IsTextRevealed = false
        };

        List<NarrativeUiHistorySegment> segments = NarrativeUiHistoryGrouping.BuildSegments(
            null, new List<NarrativeDialogueVisibleBlock> { failedBlock });

        Assert.AreEqual(1, segments.Count);
        Assert.AreEqual(NarrativeUiSegmentKind.CheckedObservation, segments[0].Kind);
        Assert.IsFalse(segments[0].CheckPresentation.Success);
        Assert.AreEqual(string.Empty, segments[0].Paragraphs[0]);
    }

    // Ведущий результат активной проверки (переход после SelectChoice) —
    // первый сегмент группы, отдельно от последующих текстовых блоков.
    [Test]
    public void LeadingActiveCheck_IsFirstSegment_SeparateFromBlocks()
    {
        NarrativeCheckPresentationData activeResult = MakePresentation(success: true);
        List<NarrativeDialogueVisibleBlock> blocks = new List<NarrativeDialogueVisibleBlock>
        {
            MakeBlock("narrator", string.Empty, "Ты справился.")
        };

        List<NarrativeUiHistorySegment> segments = NarrativeUiHistoryGrouping.BuildSegments(activeResult, blocks);

        Assert.AreEqual(2, segments.Count);
        Assert.AreEqual(NarrativeUiSegmentKind.CheckResult, segments[0].Kind);
        Assert.AreSame(activeResult, segments[0].CheckPresentation);
        Assert.AreEqual(NarrativeUiSegmentKind.SpeakerParagraphs, segments[1].Kind);
    }

    // Отсутствие ведущей проверки не добавляет лишний сегмент.
    [Test]
    public void NoLeadingActiveCheck_DoesNotAddExtraSegment()
    {
        List<NarrativeDialogueVisibleBlock> blocks = new List<NarrativeDialogueVisibleBlock>
        {
            MakeBlock("narrator", string.Empty, "Обычный переход.")
        };

        List<NarrativeUiHistorySegment> segments = NarrativeUiHistoryGrouping.BuildSegments(null, blocks);

        Assert.AreEqual(1, segments.Count);
        Assert.AreEqual(NarrativeUiSegmentKind.SpeakerParagraphs, segments[0].Kind);
    }

    [Test]
    public void EmptyBlocks_ProduceEmptySegmentList()
    {
        List<NarrativeUiHistorySegment> segments = NarrativeUiHistoryGrouping.BuildSegments(
            null, new List<NarrativeDialogueVisibleBlock>());

        Assert.IsEmpty(segments);
    }

    [Test]
    public void NullBlocks_ProduceEmptySegmentList()
    {
        List<NarrativeUiHistorySegment> segments = NarrativeUiHistoryGrouping.BuildSegments(null, null);

        Assert.IsEmpty(segments);
    }

    [TestCase(DialogueTextBlockKind.Observation)]
    [TestCase(DialogueTextBlockKind.Memory)]
    [TestCase(DialogueTextBlockKind.HeroThought)]
    [TestCase(DialogueTextBlockKind.Narration)]
    public void IsObservationLikeKind_TrueForAllFourKinds(DialogueTextBlockKind kind)
    {
        Assert.IsTrue(NarrativeUiHistoryGrouping.IsObservationLikeKind(kind));
    }

    [TestCase(DialogueTextBlockKind.MainLine)]
    [TestCase(DialogueTextBlockKind.CompanionLine)]
    public void IsObservationLikeKind_FalseForRealLines(DialogueTextBlockKind kind)
    {
        Assert.IsFalse(NarrativeUiHistoryGrouping.IsObservationLikeKind(kind));
    }
}

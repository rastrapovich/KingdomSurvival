using System.Collections.Generic;
using KingdomSurvival.DialogueDatabase;
using NUnit.Framework;

// Дополнение к инструкции "новое отображение пассивных наблюдений и
// проверок" — "новый текст всегда появляется цельным" (§12 дополнения).
// NarrativeUiHistoryGrouping — чистая презентационная логика без UI
// Toolkit, поэтому полностью проверяема в EditMode; сама геометрия
// scroll/opacity (§12: пункты 7-9) требует Play Mode/ручной проверки —
// см. DEVELOPMENT_STATUS.md.
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

    // §12, пункт 1/2: два TextBlock одного говорящего без своей проверки —
    // одна группа, один сегмент SpeakerParagraphs, повторный заголовок не нужен.
    [Test]
    public void TwoBlocks_SameSpeaker_NoCheck_MergeIntoOneSegment()
    {
        List<NarrativeDialogueVisibleBlock> blocks = new List<NarrativeDialogueVisibleBlock>
        {
            MakeBlock("narrator", "Голос сцены", "У мельницы уже шумит колесо."),
            MakeBlock("narrator", "Голос сцены", "У водопоя скот пьёт спокойно.")
        };

        List<NarrativeUiHistorySegment> segments = NarrativeUiHistoryGrouping.BuildSegments(null, blocks);

        Assert.AreEqual(1, segments.Count);
        Assert.AreEqual(NarrativeUiSegmentKind.SpeakerParagraphs, segments[0].Kind);
        Assert.AreEqual("narrator", segments[0].SpeakerDisplayName);
        Assert.AreEqual(2, segments[0].Paragraphs.Count);
        Assert.AreEqual("У мельницы уже шумит колесо.", segments[0].Paragraphs[0]);
        Assert.AreEqual("У водопоя скот пьёт спокойно.", segments[0].Paragraphs[1]);
    }

    // §12, пункт 3: разные говорящие остаются разными секциями (сегментами),
    // но обе получены одним вызовом BuildSegments — то есть принадлежат
    // одной группе по построению.
    [Test]
    public void TwoBlocks_DifferentSpeakers_RemainSeparateSegments_OneGroup()
    {
        List<NarrativeDialogueVisibleBlock> blocks = new List<NarrativeDialogueVisibleBlock>
        {
            MakeBlock("ostafiy", "Хранитель", "Опять к седьмому."),
            MakeBlock("lada", "Мастер", "Если бы Остафий не берёг каждую щепку...")
        };

        List<NarrativeUiHistorySegment> segments = NarrativeUiHistoryGrouping.BuildSegments(null, blocks);

        Assert.AreEqual(2, segments.Count);
        Assert.AreEqual("ostafiy", segments[0].SpeakerDisplayName);
        Assert.AreEqual("lada", segments[1].SpeakerDisplayName);
        Assert.AreEqual(1, segments[0].Paragraphs.Count);
        Assert.AreEqual(1, segments[1].Paragraphs.Count);
    }

    // §12, пункт 4: passive check + наблюдение — одна checked-observation
    // секция, полученная тем же вызовом BuildSegments, что и соседний обычный
    // блок — обе части одной группы.
    [Test]
    public void CheckedObservation_And_PlainBlock_BelongToSameGroup()
    {
        NarrativeCheckPresentationData presentation = MakePresentation(success: true);
        List<NarrativeDialogueVisibleBlock> blocks = new List<NarrativeDialogueVisibleBlock>
        {
            MakeBlock("ulyana", "Семейный голос", "Ульяна вынимает из печи второй каравай."),
            MakeBlock(
                "ulyana",
                "Семейный голос",
                "Дело, а не дар.",
                DialogueTextBlockKind.Observation,
                presentation)
        };

        List<NarrativeUiHistorySegment> segments = NarrativeUiHistoryGrouping.BuildSegments(null, blocks);

        Assert.AreEqual(2, segments.Count);
        Assert.AreEqual(NarrativeUiSegmentKind.SpeakerParagraphs, segments[0].Kind);
        Assert.AreEqual(NarrativeUiSegmentKind.CheckedObservation, segments[1].Kind);
        Assert.AreSame(presentation, segments[1].CheckPresentation);
        Assert.AreEqual("Дело, а не дар.", segments[1].Paragraphs[0]);
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

    // Смена говорящего прерывает слияние абзацев, но затем снова сливает
    // подряд идущие блоки нового говорящего — три блока (A, A, B) дают
    // ровно два сегмента.
    [Test]
    public void SpeakerChange_BreaksMerge_ButStillMergesFollowingRunOfSameSpeaker()
    {
        List<NarrativeDialogueVisibleBlock> blocks = new List<NarrativeDialogueVisibleBlock>
        {
            MakeBlock("miron", "Мельник", "Колесо два раза сбилось за час."),
            MakeBlock("miron", "Мельник", "Отметил, дальше видно будет."),
            MakeBlock("lada", "Мастер", "Задвижку, может, подперло веткой.")
        };

        List<NarrativeUiHistorySegment> segments = NarrativeUiHistoryGrouping.BuildSegments(null, blocks);

        Assert.AreEqual(2, segments.Count);
        Assert.AreEqual("miron", segments[0].SpeakerDisplayName);
        Assert.AreEqual(2, segments[0].Paragraphs.Count);
        Assert.AreEqual("lada", segments[1].SpeakerDisplayName);
        Assert.AreEqual(1, segments[1].Paragraphs.Count);
    }

    // Одинаковое отображаемое имя, но разная роль — не считается тем же
    // говорящим для слияния (например, один и тот же спикер сменил роль
    // в узле — редкий, но не запрещённый случай).
    [Test]
    public void SameDisplayName_DifferentRole_DoesNotMerge()
    {
        List<NarrativeDialogueVisibleBlock> blocks = new List<NarrativeDialogueVisibleBlock>
        {
            MakeBlock("narrator", "Голос сцены", "Первый абзац."),
            MakeBlock("narrator", "Другая роль", "Второй абзац.")
        };

        List<NarrativeUiHistorySegment> segments = NarrativeUiHistoryGrouping.BuildSegments(null, blocks);

        Assert.AreEqual(2, segments.Count);
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

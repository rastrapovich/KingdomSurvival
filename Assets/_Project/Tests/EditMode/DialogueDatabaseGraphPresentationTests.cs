using System.Collections.Generic;
using KingdomSurvival.DialogueDatabase;
using NUnit.Framework;
using static KingdomSurvival.DialogueDatabase.Editor.DialogueDatabaseWindow;

// "Инструкция: информативные ноды Dialogue Graph" — §41 (чистая
// presentation-логика: сводки, предупреждения, бейджи) и §42 (инварианты
// раскладки, насколько их можно проверить без пиксельного рендера/реального
// шрифта — сама геометрия портов/canvas остаётся ручной проверкой в Unity,
// см. DEVELOPMENT_STATUS.md).
public sealed class DialogueDatabaseGraphPresentationTests
{
    // ---- Сводки проверок (§9, §17) ------------------------------------

    [Test]
    public void BuildGraphPassiveCheckSummary_QualityOnly()
    {
        NarrativeCheckSpec check = new NarrativeCheckSpec
        {
            Kind = NarrativeCheckKind.Passive,
            Quality = HeroQuality.Judgment,
            Difficulty = NarrativeDifficulty.Simple
        };

        Assert.AreEqual("ПАССИВНАЯ · СУЖДЕНИЕ · 11", BuildGraphPassiveCheckSummary(check));
    }

    [Test]
    public void BuildGraphPassiveCheckSummary_QualityPlusCompetency()
    {
        NarrativeCheckSpec check = new NarrativeCheckSpec
        {
            Kind = NarrativeCheckKind.Passive,
            Quality = HeroQuality.Instinct,
            CompetencyId = NarrativeCompetencyIds.Fieldcraft,
            Difficulty = NarrativeDifficulty.Ordinary
        };

        Assert.AreEqual("ПАССИВНАЯ · ЧУТЬЁ + СЛЕДОПЫТСТВО · 13", BuildGraphPassiveCheckSummary(check));
    }

    [Test]
    public void BuildGraphActiveCheckSummary_DoesNotRepeatCheckKind()
    {
        NarrativeCheckSpec check = new NarrativeCheckSpec
        {
            Kind = NarrativeCheckKind.ActiveDecisive,
            Quality = HeroQuality.Strength,
            CompetencyId = NarrativeCompetencyIds.Craft,
            Difficulty = NarrativeDifficulty.Ordinary
        };

        Assert.AreEqual("СИЛА + РЕМЕСЛО · 13", BuildGraphActiveCheckSummary(check));
    }

    [Test]
    public void BuildGraphChoiceKindLabel_DistinguishesReturnableFromDecisive()
    {
        Assert.AreEqual("ВОЗВРАТНАЯ ПРОВЕРКА", BuildGraphChoiceKindLabel(DialogueChoiceKind.ActiveReturnable));
        Assert.AreEqual("РЕШАЮЩАЯ ПРОВЕРКА", BuildGraphChoiceKindLabel(DialogueChoiceKind.ActiveDecisive));
        Assert.AreEqual("EXIT", BuildGraphChoiceKindLabel(DialogueChoiceKind.Exit));
    }

    // ---- Сводки условий/эффектов (§10-11) ------------------------------

    [Test]
    public void BuildGraphConditionSummary_FlagSet_PositiveAndNegated()
    {
        NarrativeCondition positive = new NarrativeCondition { Type = NarrativeConditionType.FlagSet, StringParam = "repair_new" };
        NarrativeCondition negated = new NarrativeCondition { Type = NarrativeConditionType.FlagSet, StringParam = "repair_new", Negate = true };

        Assert.AreEqual("FLAG: repair_new", BuildGraphConditionSummary(positive));
        Assert.AreEqual("NOT FLAG: repair_new", BuildGraphConditionSummary(negated));
    }

    [Test]
    public void BuildGraphConditionSummary_RelationAtLeast_And_AtMost()
    {
        NarrativeCondition atLeast = new NarrativeCondition { Type = NarrativeConditionType.RelationAtLeast, StringParam = "miron", IntParam = 2 };
        NarrativeCondition atMost = new NarrativeCondition { Type = NarrativeConditionType.RelationAtMost, StringParam = "miron", IntParam = -1 };

        Assert.AreEqual("REL miron ≥ 2", BuildGraphConditionSummary(atLeast));
        Assert.AreEqual("REL miron ≤ -1", BuildGraphConditionSummary(atMost));
    }

    [Test]
    public void BuildGraphEffectSummary_SetFlag_ClearFlag_AddKnowledge()
    {
        NarrativeEffect setFlag = new NarrativeEffect { EffectExecutionId = "e1", Type = NarrativeEffectType.SetFlag, StringParam = "home_baseline_captured" };
        NarrativeEffect clearFlag = new NarrativeEffect { EffectExecutionId = "e2", Type = NarrativeEffectType.ClearFlag, StringParam = "x" };
        NarrativeEffect knowledge = new NarrativeEffect { EffectExecutionId = "e3", Type = NarrativeEffectType.AddKnowledge, StringParam = "second_loaf" };

        Assert.AreEqual("+ FLAG home_baseline_captured", BuildGraphEffectSummary(setFlag));
        Assert.AreEqual("− FLAG x", BuildGraphEffectSummary(clearFlag));
        Assert.AreEqual("+ KNOW second_loaf", BuildGraphEffectSummary(knowledge));
    }

    [Test]
    public void BuildGraphEffectSummary_ChangeRelation_ShowsSign()
    {
        NarrativeEffect positive = new NarrativeEffect { EffectExecutionId = "e1", Type = NarrativeEffectType.ChangeRelation, StringParam = "lada", IntParam = 1 };
        NarrativeEffect negative = new NarrativeEffect { EffectExecutionId = "e2", Type = NarrativeEffectType.ChangeRelation, StringParam = "lada", IntParam = -2 };

        Assert.AreEqual("ОТН lada +1", BuildGraphEffectSummary(positive));
        Assert.AreEqual("ОТН lada -2", BuildGraphEffectSummary(negative));
    }

    // ---- Предупреждения (§25) -------------------------------------------

    private static GraphNodeInfo MakeMinimalNode()
    {
        return new GraphNodeInfo
        {
            NodeId = "n1",
            SpeakerId = "ulyana",
            SpeakerKnown = true,
            TextBlocks = new List<GraphTextBlockInfo> { new GraphTextBlockInfo { Kind = DialogueTextBlockKind.MainLine, Text = "Текст." } },
            Choices = new List<GraphChoiceInfo> { new GraphChoiceInfo { Text = "Ответ", Kind = DialogueChoiceKind.Normal, NextNodeId = "n2" } },
            IsStart = true,
            IsReachable = true
        };
    }

    [Test]
    public void CollectGraphNodeWarnings_MissingSpeaker()
    {
        GraphNodeInfo node = MakeMinimalNode();
        node.SpeakerId = string.Empty;
        node.SpeakerKnown = false;

        List<string> warnings = CollectGraphNodeWarnings(node, new HashSet<string> { "n1", "n2" });

        AssertAnyContains(warnings, "говорящий");
    }

    [Test]
    public void CollectGraphNodeWarnings_UnknownSpeaker()
    {
        GraphNodeInfo node = MakeMinimalNode();
        node.SpeakerId = "phantom";
        node.SpeakerKnown = false;

        List<string> warnings = CollectGraphNodeWarnings(node, new HashSet<string> { "n1", "n2" });

        AssertAnyContains(warnings, "phantom");
    }

    [Test]
    public void CollectGraphNodeWarnings_NoTextBlocks()
    {
        GraphNodeInfo node = MakeMinimalNode();
        node.TextBlocks = new List<GraphTextBlockInfo>();

        List<string> warnings = CollectGraphNodeWarnings(node, new HashSet<string> { "n1", "n2" });

        AssertAnyContains(warnings, "текстовых блоков");
    }

    [Test]
    public void CollectGraphNodeWarnings_NoChoices()
    {
        GraphNodeInfo node = MakeMinimalNode();
        node.Choices = new List<GraphChoiceInfo>();

        List<string> warnings = CollectGraphNodeWarnings(node, new HashSet<string> { "n1" });

        AssertAnyContains(warnings, "вариантов ответа");
    }

    [Test]
    public void CollectGraphNodeWarnings_DuplicateChoiceId()
    {
        GraphNodeInfo node = MakeMinimalNode();
        node.Choices = new List<GraphChoiceInfo>
        {
            new GraphChoiceInfo { ChoiceId = "c1", Text = "A", Kind = DialogueChoiceKind.Normal, NextNodeId = "n2" },
            new GraphChoiceInfo { ChoiceId = "c1", Text = "B", Kind = DialogueChoiceKind.Normal, NextNodeId = "n2" }
        };

        List<string> warnings = CollectGraphNodeWarnings(node, new HashSet<string> { "n1", "n2" });

        AssertAnyContains(warnings, "ChoiceId");
    }

    [Test]
    public void CollectGraphNodeWarnings_ActiveChoice_MissingCheckIdAndTargets()
    {
        GraphNodeInfo node = MakeMinimalNode();
        node.Choices = new List<GraphChoiceInfo>
        {
            new GraphChoiceInfo
            {
                Text = "Проверить",
                Kind = DialogueChoiceKind.ActiveDecisive,
                Check = new NarrativeCheckSpec { CheckId = string.Empty }
            }
        };

        List<string> warnings = CollectGraphNodeWarnings(node, new HashSet<string> { "n1" });

        AssertAnyContains(warnings, "CheckId");
        AssertAnyContains(warnings, "успеха");
        AssertAnyContains(warnings, "провала");
    }

    [Test]
    public void CollectGraphNodeWarnings_ActiveChoice_TargetNotFound()
    {
        GraphNodeInfo node = MakeMinimalNode();
        node.Choices = new List<GraphChoiceInfo>
        {
            new GraphChoiceInfo
            {
                Text = "Проверить",
                Kind = DialogueChoiceKind.ActiveReturnable,
                Check = new NarrativeCheckSpec { CheckId = "chk" },
                SuccessNodeId = "ghost_success",
                FailureNodeId = "n1"
            }
        };

        List<string> warnings = CollectGraphNodeWarnings(node, new HashSet<string> { "n1" });

        AssertAnyContains(warnings, "ghost_success");
    }

    [Test]
    public void CollectGraphNodeWarnings_NormalChoice_MissingTarget()
    {
        GraphNodeInfo node = MakeMinimalNode();
        node.Choices = new List<GraphChoiceInfo>
        {
            new GraphChoiceInfo { Text = "Ответ", Kind = DialogueChoiceKind.Normal, NextNodeId = string.Empty, EndsDialogue = false }
        };

        List<string> warnings = CollectGraphNodeWarnings(node, new HashSet<string> { "n1" });

        AssertAnyContains(warnings, "цели перехода");
    }

    [Test]
    public void CollectGraphNodeWarnings_ExitWithTransition_IsFlagged()
    {
        GraphNodeInfo node = MakeMinimalNode();
        node.Choices = new List<GraphChoiceInfo>
        {
            new GraphChoiceInfo { Text = "Уйти", Kind = DialogueChoiceKind.Exit, NextNodeId = "n2" }
        };

        List<string> warnings = CollectGraphNodeWarnings(node, new HashSet<string> { "n1", "n2" });

        AssertAnyContains(warnings, "EXIT");
    }

    [Test]
    public void CollectGraphNodeWarnings_UnreachableNode()
    {
        GraphNodeInfo node = MakeMinimalNode();
        node.IsStart = false;
        node.IsReachable = false;

        List<string> warnings = CollectGraphNodeWarnings(node, new HashSet<string> { "n1", "n2" });

        AssertAnyContains(warnings, "недостижим");
    }

    [Test]
    public void CollectGraphNodeWarnings_ValidNode_HasNoWarnings()
    {
        GraphNodeInfo node = MakeMinimalNode();

        List<string> warnings = CollectGraphNodeWarnings(node, new HashSet<string> { "n1", "n2" });

        Assert.IsEmpty(warnings);
    }

    private static void AssertAnyContains(List<string> lines, string substring)
    {
        foreach (string line in lines)
        {
            if (line.Contains(substring))
                return;
        }

        Assert.Fail("Ни одна строка не содержит '" + substring + "'. Строки: " + string.Join(" | ", lines));
    }

    // ---- Бейджи (§5, §24) ------------------------------------------------

    [Test]
    public void BuildGraphNodeBadges_StartNodeGetsStartBadge()
    {
        GraphNodeInfo node = MakeMinimalNode();

        List<string> badges = BuildGraphNodeBadges(node, 0);

        Assert.Contains("СТАРТ", badges);
    }

    [Test]
    public void BuildGraphNodeBadges_ChecksConditionsEffectsAndKnowledgeFlagItem()
    {
        GraphNodeInfo node = MakeMinimalNode();
        node.TextBlocks[0].PassiveCheck = new NarrativeCheckSpec { Kind = NarrativeCheckKind.Passive };
        node.TextBlocks[0].Conditions = new NarrativeConditionGroup
        {
            Conditions = new List<NarrativeCondition>
            {
                new NarrativeCondition { Type = NarrativeConditionType.KnowledgeKnown, StringParam = "k" },
                new NarrativeCondition { Type = NarrativeConditionType.ItemPresent, StringParam = "item" }
            }
        };
        node.TextBlocks[0].OnRevealEffects = new List<NarrativeEffect>
        {
            new NarrativeEffect { EffectExecutionId = "e1", Type = NarrativeEffectType.SetFlag, StringParam = "f" }
        };

        List<string> badges = BuildGraphNodeBadges(node, 0);

        Assert.Contains("CHECK", badges);
        Assert.Contains("COND", badges);
        Assert.Contains("FX", badges);
        Assert.Contains("KNOW", badges);
        Assert.Contains("FLAG", badges);
        Assert.Contains("ITEM", badges);
    }

    [Test]
    public void BuildGraphNodeBadges_WarningCountAddsExclamationBadge()
    {
        GraphNodeInfo node = MakeMinimalNode();

        List<string> withoutWarnings = BuildGraphNodeBadges(node, 0);
        List<string> withWarnings = BuildGraphNodeBadges(node, 2);

        Assert.IsFalse(withoutWarnings.Contains("!"));
        Assert.IsTrue(withWarnings.Contains("!"));
    }

    [Test]
    public void BuildGraphNodeBadges_UnreachableNonStartNode_GetsUnreachableBadge()
    {
        GraphNodeInfo node = MakeMinimalNode();
        node.IsStart = false;
        node.IsReachable = false;

        List<string> badges = BuildGraphNodeBadges(node, 0);

        Assert.Contains("UNREACHABLE", badges);
    }

    // ---- Режим детализации (§4, §29) -------------------------------------

    [Test]
    public void ResolveEffectiveGraphDetailMode_SelectedCompactUpgradesToStandard()
    {
        Assert.AreEqual(GraphDetailMode.Standard, ResolveEffectiveGraphDetailMode(GraphDetailMode.Compact, isSelected: true));
    }

    [Test]
    public void ResolveEffectiveGraphDetailMode_UnselectedCompactStaysCompact()
    {
        Assert.AreEqual(GraphDetailMode.Compact, ResolveEffectiveGraphDetailMode(GraphDetailMode.Compact, isSelected: false));
    }

    [Test]
    public void ResolveEffectiveGraphDetailMode_FullNeverDowngrades()
    {
        Assert.AreEqual(GraphDetailMode.Full, ResolveEffectiveGraphDetailMode(GraphDetailMode.Full, isSelected: false));
        Assert.AreEqual(GraphDetailMode.Full, ResolveEffectiveGraphDetailMode(GraphDetailMode.Full, isSelected: true));
    }

    // ---- Обрезка превью (§8) ---------------------------------------------

    [Test]
    public void TruncateForGraphPreview_ShortTextIsUnchanged()
    {
        Assert.AreEqual("Короткий текст.", TruncateForGraphPreview("Короткий текст.", 50));
    }

    [Test]
    public void TruncateForGraphPreview_LongTextIsTruncatedWithEllipsis()
    {
        string longText = "Слово раз два три четыре пять шесть семь восемь девять десять";
        string truncated = TruncateForGraphPreview(longText, 20);

        Assert.LessOrEqual(truncated.Length, 21);
        Assert.IsTrue(truncated.EndsWith("…"));
    }

    [Test]
    public void BuildOverflowLabel_ZeroReturnsNull()
    {
        Assert.IsNull(BuildOverflowLabel(0));
    }

    [Test]
    public void BuildOverflowLabel_PositiveReturnsCount()
    {
        Assert.AreEqual("+ ещё 3", BuildOverflowLabel(3));
    }

    // ---- Раскладка (§42) — инварианты высоты/ширины ----------------------

    [Test]
    public void ComputeNodeLayoutMetrics_FullWidthAtLeastStandardAtLeastCompact()
    {
        GraphNodeInfo node = MakeMinimalNode();
        HashSet<string> allIds = new HashSet<string> { "n1", "n2" };

        float compactWidth = ComputeNodeLayoutMetrics(node, allIds, GraphDetailMode.Compact, false).Width;
        float standardWidth = ComputeNodeLayoutMetrics(node, allIds, GraphDetailMode.Standard, false).Width;
        float fullWidth = ComputeNodeLayoutMetrics(node, allIds, GraphDetailMode.Full, false).Width;

        Assert.LessOrEqual(compactWidth, standardWidth);
        Assert.LessOrEqual(standardWidth, fullWidth);
    }

    [Test]
    public void ComputeNodeLayoutMetrics_FullHeightAtLeastStandardAtLeastCompact()
    {
        GraphNodeInfo node = MakeMinimalNode();
        node.TextBlocks[0].Conditions = new NarrativeConditionGroup
        {
            Conditions = new List<NarrativeCondition> { new NarrativeCondition { Type = NarrativeConditionType.FlagSet, StringParam = "x" } }
        };
        node.Choices[0].Conditions = node.TextBlocks[0].Conditions;
        HashSet<string> allIds = new HashSet<string> { "n1", "n2" };

        float compactHeight = ComputeNodeLayoutMetrics(node, allIds, GraphDetailMode.Compact, false).TotalHeight;
        float standardHeight = ComputeNodeLayoutMetrics(node, allIds, GraphDetailMode.Standard, false).TotalHeight;
        float fullHeight = ComputeNodeLayoutMetrics(node, allIds, GraphDetailMode.Full, false).TotalHeight;

        Assert.LessOrEqual(compactHeight, standardHeight);
        Assert.LessOrEqual(standardHeight, fullHeight);
    }

    [Test]
    public void ComputeNodeLayoutMetrics_MoreTextBlocksWithinCap_IncreasesHeight()
    {
        GraphNodeInfo oneBlock = MakeMinimalNode();
        GraphNodeInfo twoBlocks = MakeMinimalNode();
        twoBlocks.TextBlocks.Add(new GraphTextBlockInfo { Kind = DialogueTextBlockKind.Observation, Text = "Ещё один блок." });
        HashSet<string> allIds = new HashSet<string> { "n1", "n2" };

        float oneHeight = ComputeNodeLayoutMetrics(oneBlock, allIds, GraphDetailMode.Standard, false).TotalHeight;
        float twoHeight = ComputeNodeLayoutMetrics(twoBlocks, allIds, GraphDetailMode.Standard, false).TotalHeight;

        Assert.Greater(twoHeight, oneHeight);
    }

    [Test]
    public void ComputeNodeLayoutMetrics_MoreChoices_IncreasesHeight()
    {
        GraphNodeInfo oneChoice = MakeMinimalNode();
        GraphNodeInfo twoChoices = MakeMinimalNode();
        twoChoices.Choices.Add(new GraphChoiceInfo { Text = "Второй ответ", Kind = DialogueChoiceKind.Normal, NextNodeId = "n2" });
        HashSet<string> allIds = new HashSet<string> { "n1", "n2" };

        float oneHeight = ComputeNodeLayoutMetrics(oneChoice, allIds, GraphDetailMode.Standard, false).TotalHeight;
        float twoHeight = ComputeNodeLayoutMetrics(twoChoices, allIds, GraphDetailMode.Standard, false).TotalHeight;

        Assert.Greater(twoHeight, oneHeight);
    }

    [Test]
    public void ComputeNodeLayoutMetrics_ChoicePortStaysWithinChoiceRect()
    {
        GraphNodeInfo node = MakeMinimalNode();
        node.Choices.Add(new GraphChoiceInfo { Text = "Второй ответ", Kind = DialogueChoiceKind.Normal, NextNodeId = "n2" });
        HashSet<string> allIds = new HashSet<string> { "n1", "n2" };

        GraphNodeLayoutMetrics metrics = ComputeNodeLayoutMetrics(node, allIds, GraphDetailMode.Standard, false);

        foreach (GraphChoiceLayout choice in metrics.Choices)
        {
            float portY = choice.Y + choice.Height * 0.5f;
            Assert.GreaterOrEqual(portY, choice.Y);
            Assert.LessOrEqual(portY, choice.Y + choice.Height);
        }
    }

    [Test]
    public void ComputeNodeLayoutMetrics_TotalHeightContainsChoicesAndFooter()
    {
        GraphNodeInfo node = MakeMinimalNode();
        HashSet<string> allIds = new HashSet<string> { "n1", "n2" };

        GraphNodeLayoutMetrics metrics = ComputeNodeLayoutMetrics(node, allIds, GraphDetailMode.Standard, false);
        GraphChoiceLayout lastChoice = metrics.Choices[metrics.Choices.Count - 1];

        Assert.LessOrEqual(lastChoice.Y + lastChoice.Height, metrics.FooterY + metrics.FooterHeight);
        Assert.AreEqual(metrics.FooterY + metrics.FooterHeight, metrics.TotalHeight, 0.01f);
    }

    [Test]
    public void ComputeNodeLayoutMetrics_SelectedCompactNodeUsesStandardEffectiveMode()
    {
        GraphNodeInfo node = MakeMinimalNode();
        HashSet<string> allIds = new HashSet<string> { "n1", "n2" };

        GraphNodeLayoutMetrics metrics = ComputeNodeLayoutMetrics(node, allIds, GraphDetailMode.Compact, isSelected: true);

        Assert.AreEqual(GraphDetailMode.Standard, metrics.EffectiveMode);
    }

    // ---- "Читаемые ноды" (§12/§40-42): предупреждение ответа и высота ----

    [Test]
    public void ChoiceHasWarning_MatchesByPrefix()
    {
        List<string> warnings = new List<string> { "Ответ #2: нет цели перехода." };
        Assert.IsTrue(ChoiceHasWarning(warnings, 1));
    }

    [Test]
    public void ChoiceHasWarning_NoMatchingPrefix_ReturnsFalse()
    {
        List<string> warnings = new List<string> { "Ответ #2: нет цели перехода." };
        Assert.IsFalse(ChoiceHasWarning(warnings, 0));
    }

    [Test]
    public void ChoiceHasWarning_NullOrEmptyList_ReturnsFalse()
    {
        Assert.IsFalse(ChoiceHasWarning(null, 0));
        Assert.IsFalse(ChoiceHasWarning(new List<string>(), 0));
    }

    [Test]
    public void ComputeChoiceHeight_WithWarning_IsTallerThanWithoutWarning()
    {
        GraphChoiceInfo choice = new GraphChoiceInfo { Text = "Ответ", Kind = DialogueChoiceKind.Normal, NextNodeId = "n2" };

        float withoutWarning = ComputeChoiceHeight(choice, GraphDetailMode.Standard, hasWarning: false);
        float withWarning = ComputeChoiceHeight(choice, GraphDetailMode.Standard, hasWarning: true);

        Assert.Greater(withWarning, withoutWarning);
    }

    [Test]
    public void ComputeChoiceHeight_SeparateSuccessAndFailureEffects_BothCountTowardHeight()
    {
        GraphChoiceInfo successOnly = new GraphChoiceInfo
        {
            Text = "Проверить",
            Kind = DialogueChoiceKind.ActiveDecisive,
            SuccessNodeId = "n2",
            FailureNodeId = "n1",
            SuccessEffects = new List<NarrativeEffect> { new NarrativeEffect { EffectExecutionId = "e1", Type = NarrativeEffectType.SetFlag, StringParam = "x" } }
        };
        GraphChoiceInfo both = new GraphChoiceInfo
        {
            Text = "Проверить",
            Kind = DialogueChoiceKind.ActiveDecisive,
            SuccessNodeId = "n2",
            FailureNodeId = "n1",
            SuccessEffects = successOnly.SuccessEffects,
            FailureEffects = new List<NarrativeEffect> { new NarrativeEffect { EffectExecutionId = "e2", Type = NarrativeEffectType.ClearFlag, StringParam = "y" } }
        };

        float successOnlyHeight = ComputeChoiceHeight(successOnly, GraphDetailMode.Standard, hasWarning: false);
        float bothHeight = ComputeChoiceHeight(both, GraphDetailMode.Standard, hasWarning: false);

        Assert.Greater(bothHeight, successOnlyHeight);
    }

    // ---- "Полноценное редактирование нод" (§3-5, §44): полный текст без
    // обрезания в Standard/Full, реальная высота через CalcHeight ---------

    private const string LongParagraph =
        "Ульяна вынимает из печи второй каравай — сверх обычной нормы — и заворачивает его в холстину. " +
        "«Отнеси к седьмому затвору, как всегда», — говорит она, не поднимая глаз от теста. " +
        "Дело, а не дар: этот хлеб давно и без спора считается чужим.";

    [Test]
    public void ResolveGraphTextForDisplay_Compact_StillTruncates()
    {
        string result = ResolveGraphTextForDisplay(LongParagraph, GraphDetailMode.Compact, 50);

        Assert.LessOrEqual(result.Length, 51);
        Assert.IsTrue(result.EndsWith("…"));
    }

    [Test]
    public void ResolveGraphTextForDisplay_Standard_ReturnsFullTextUntruncated()
    {
        string result = ResolveGraphTextForDisplay(LongParagraph, GraphDetailMode.Standard, 50);

        Assert.AreEqual(LongParagraph, result);
        Assert.IsFalse(result.EndsWith("…"));
    }

    [Test]
    public void ResolveGraphTextForDisplay_Full_ReturnsFullTextUntruncated()
    {
        string result = ResolveGraphTextForDisplay(LongParagraph, GraphDetailMode.Full, 220);

        Assert.AreEqual(LongParagraph, result);
    }

    [Test]
    public void ComputeNarrativeTextHeight_LongerTextIsTaller()
    {
        float shortHeight = ComputeNarrativeTextHeight("Коротко.", 300f);
        float longHeight = ComputeNarrativeTextHeight(LongParagraph, 300f);

        Assert.Greater(longHeight, shortHeight);
    }

    [Test]
    public void ComputeNarrativeTextHeight_NarrowerWidthIsTallerOrEqual()
    {
        // Тот же текст в более узкой ширине переносится на больше строк
        // (или на столько же, если текст короче строки) — никогда меньше.
        float wideHeight = ComputeNarrativeTextHeight(LongParagraph, 480f);
        float narrowHeight = ComputeNarrativeTextHeight(LongParagraph, 200f);

        Assert.GreaterOrEqual(narrowHeight, wideHeight);
    }

    [Test]
    public void ComputeNarrativeTextHeight_EmptyTextStillMeasuresPlaceholder()
    {
        float height = ComputeNarrativeTextHeight(string.Empty, 300f);

        Assert.Greater(height, 0f);
    }

    [Test]
    public void ComputeTextBlockHeight_Standard_GrowsWithLongerText()
    {
        GraphTextBlockInfo shortBlock = new GraphTextBlockInfo { Kind = DialogueTextBlockKind.MainLine, Text = "Коротко." };
        GraphTextBlockInfo longBlock = new GraphTextBlockInfo { Kind = DialogueTextBlockKind.MainLine, Text = LongParagraph };

        float shortHeight = ComputeTextBlockHeight(shortBlock, GraphDetailMode.Standard);
        float longHeight = ComputeTextBlockHeight(longBlock, GraphDetailMode.Standard);

        Assert.Greater(longHeight, shortHeight);
    }

    [Test]
    public void ComputeChoiceHeight_Standard_GrowsWithLongerText()
    {
        GraphChoiceInfo shortChoice = new GraphChoiceInfo { Text = "Да.", Kind = DialogueChoiceKind.Normal, NextNodeId = "n2" };
        GraphChoiceInfo longChoice = new GraphChoiceInfo { Text = LongParagraph, Kind = DialogueChoiceKind.Normal, NextNodeId = "n2" };

        float shortHeight = ComputeChoiceHeight(shortChoice, GraphDetailMode.Standard, hasWarning: false);
        float longHeight = ComputeChoiceHeight(longChoice, GraphDetailMode.Standard, hasWarning: false);

        Assert.Greater(longHeight, shortHeight);
    }

    // §6: ноды заметно шире, чем в предыдущей инструкции (280-320 →
    // 310-500), чтобы русский литературный текст не раздувал высоту.
    [Test]
    public void GetGraphNodeWidth_MatchesWidenedRanges()
    {
        Assert.AreEqual(310f, GetGraphNodeWidth(GraphDetailMode.Compact));
        Assert.AreEqual(430f, GetGraphNodeWidth(GraphDetailMode.Standard));
        Assert.AreEqual(500f, GetGraphNodeWidth(GraphDetailMode.Full));
    }

    [Test]
    public void GetGraphTextContentWidth_NarrowerThanNodeWidth()
    {
        Assert.Less(GetGraphTextContentWidth(GraphDetailMode.Standard), GetGraphNodeWidth(GraphDetailMode.Standard));
    }

    [Test]
    public void GetGraphChoiceContentWidth_NarrowerThanTextContentWidth()
    {
        // Карточка ответа имеет собственный внутренний отступ поверх
        // общего margin ноды — уже, чем ширина текстового блока.
        Assert.Less(GetGraphChoiceContentWidth(GraphDetailMode.Standard), GetGraphTextContentWidth(GraphDetailMode.Standard));
    }

    // ---- Автораскладка ("свободный граф" §2-7) -------------------------

    [Test]
    public void AutoLayoutHorizontalGap_WithinInstructedRange()
    {
        Assert.GreaterOrEqual(AutoLayoutHorizontalGap, 180f);
        Assert.LessOrEqual(AutoLayoutHorizontalGap, 240f);
    }

    [Test]
    public void AutoLayoutVerticalGap_WithinInstructedRange()
    {
        Assert.GreaterOrEqual(AutoLayoutVerticalGap, 100f);
        Assert.LessOrEqual(AutoLayoutVerticalGap, 140f);
    }

    [Test]
    public void ComputeAutoLayoutColumnX_FirstColumnStartsAtMargin()
    {
        Assert.AreEqual(60f, ComputeAutoLayoutColumnX(0, GetGraphNodeWidth(GraphDetailMode.Standard)));
    }

    [Test]
    public void ComputeAutoLayoutColumnX_NextColumnClearsPreviousNodeWidthPlusGap()
    {
        float nodeWidth = GetGraphNodeWidth(GraphDetailMode.Standard);
        float column0 = ComputeAutoLayoutColumnX(0, nodeWidth);
        float column1 = ComputeAutoLayoutColumnX(1, nodeWidth);

        // Следующая колонка должна начинаться не раньше, чем правый край
        // предыдущей карточки плюс горизонтальный зазор — иначе колонки
        // перекрывались бы при любой ширине карточки.
        Assert.GreaterOrEqual(column1, column0 + nodeWidth + AutoLayoutHorizontalGap);
    }

    [Test]
    public void ComputeAutoLayoutColumnX_WiderModeStillGrowsMonotonically()
    {
        float compactWidth = GetGraphNodeWidth(GraphDetailMode.Compact);
        float fullWidth = GetGraphNodeWidth(GraphDetailMode.Full);

        float compactColumn3 = ComputeAutoLayoutColumnX(3, compactWidth);
        float fullColumn3 = ComputeAutoLayoutColumnX(3, fullWidth);

        Assert.Greater(fullColumn3, compactColumn3);
    }

    // ---- "Раздвинуть" — определение колонок по X-пересечению -----------

    [Test]
    public void RangesOverlap_IdenticalRangesOverlap()
    {
        Assert.IsTrue(RangesOverlap(0f, 100f, 0f, 100f));
    }

    [Test]
    public void RangesOverlap_PartiallyOverlappingRangesOverlap()
    {
        Assert.IsTrue(RangesOverlap(0f, 100f, 50f, 150f));
    }

    [Test]
    public void RangesOverlap_TouchingEdgesDoNotOverlap()
    {
        // Строгое неравенство: соседние колонки, стоящие впритык (без
        // зазора), не должны считаться одной колонкой.
        Assert.IsFalse(RangesOverlap(0f, 100f, 100f, 200f));
    }

    [Test]
    public void RangesOverlap_FarApartRangesDoNotOverlap()
    {
        Assert.IsFalse(RangesOverlap(0f, 100f, 500f, 600f));
    }
}

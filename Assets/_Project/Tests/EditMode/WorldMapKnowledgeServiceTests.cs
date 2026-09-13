using System.Collections.Generic;
using NUnit.Framework;

// AM-07 (канон v1.33, §9.9, раздел 12 инструкции): слух — авторские
// сужающиеся стадии поверх уже существующих флагов знания NarrativeState.
public class WorldMapKnowledgeServiceTests
{
    private static WorldMapSearchAreaDefinition BuildTestSearch()
    {
        return new WorldMapSearchAreaDefinition
        {
            SearchId = "search-mill",
            TargetLocationId = "mill",
            Stages = new List<WorldMapSearchStageDefinition>
            {
                new WorldMapSearchStageDefinition
                {
                    StageId = "wide",
                    RequiredKnowledgeFlags = new List<string> { "rumor.mill.a" },
                    MinXPercent = 0f, MaxXPercent = 100f,
                    MinYPercent = 0f, MaxYPercent = 100f,
                    NeutralLabel = "Где-то в холмах есть мельница"
                },
                new WorldMapSearchStageDefinition
                {
                    StageId = "narrow",
                    RequiredKnowledgeFlags = new List<string> { "rumor.mill.a", "rumor.mill.b" },
                    MinXPercent = 30f, MaxXPercent = 50f,
                    MinYPercent = 30f, MaxYPercent = 50f,
                    NeutralLabel = "Мельница где-то у брода"
                },
                new WorldMapSearchStageDefinition
                {
                    StageId = "found",
                    RequiredKnowledgeFlags = new List<string> { "location.mill.discovered" },
                    MinXPercent = 39f, MaxXPercent = 41f,
                    MinYPercent = 39f, MaxYPercent = 41f,
                    NeutralLabel = "Мельница"
                }
            }
        };
    }

    [Test]
    public void GetActiveStage_ReturnsNullWithoutAnyKnowledge()
    {
        WorldMapSearchAreaDefinition search = BuildTestSearch();
        NarrativeStateData narrative = new NarrativeStateData();

        Assert.That(WorldMapKnowledgeService.GetActiveStage(search, narrative), Is.Null);
    }

    [Test]
    public void GetActiveStage_NarrowsAsMoreFlagsBecomeKnown()
    {
        WorldMapSearchAreaDefinition search = BuildTestSearch();
        NarrativeStateData narrative = new NarrativeStateData();

        narrative.SetFlag("rumor.mill.a");
        Assert.That(WorldMapKnowledgeService.GetActiveStage(search, narrative).StageId, Is.EqualTo("wide"));

        narrative.SetFlag("rumor.mill.b");
        Assert.That(WorldMapKnowledgeService.GetActiveStage(search, narrative).StageId, Is.EqualTo("narrow"));

        narrative.SetFlag("location.mill.discovered");
        Assert.That(WorldMapKnowledgeService.GetActiveStage(search, narrative).StageId, Is.EqualTo("found"));
    }

    [Test]
    public void GetActiveStage_IsIdempotentAndDoesNotShrinkAreaTwice()
    {
        WorldMapSearchAreaDefinition search = BuildTestSearch();
        NarrativeStateData narrative = new NarrativeStateData();
        narrative.SetFlag("rumor.mill.a");
        narrative.SetFlag("rumor.mill.b");

        string first = WorldMapKnowledgeService.GetActiveStage(search, narrative).StageId;

        // Повторное применение того же знания (например, из идемпотентного
        // эффекта нарратива или после Load) не должно менять результат.
        narrative.SetFlag("rumor.mill.a");
        narrative.SetFlag("rumor.mill.b");
        string second = WorldMapKnowledgeService.GetActiveStage(search, narrative).StageId;

        Assert.That(second, Is.EqualTo(first));
    }

    [Test]
    public void Validator_FlagsStageThatDoesNotContainRealTarget()
    {
        WorldMapSearchAreaDefinition search = BuildTestSearch();

        // Реальная цель мельницы — вне "narrow"/"found"-стадий (30..50 x,
        // 30..50 y и уже), но всё ещё внутри "wide" (0..100 — весь мир).
        List<string> issues = WorldMapSearchAreaValidator.Validate(search, 80f, 80f);

        Assert.That(issues, Has.None.Contains("'wide'"));
        Assert.That(issues, Has.Some.Contains("narrow"));
        Assert.That(issues, Has.Some.Contains("found"));
    }

    [Test]
    public void Validator_PassesWhenAllStagesContainTargetAndNarrow()
    {
        WorldMapSearchAreaDefinition search = BuildTestSearch();

        List<string> issues = WorldMapSearchAreaValidator.Validate(search, 40f, 40f);

        Assert.That(issues, Is.Empty);
    }

    [Test]
    public void Validator_FlagsStageThatExpandsBeyondPreviousStage()
    {
        WorldMapSearchAreaDefinition search = new WorldMapSearchAreaDefinition
        {
            SearchId = "search-bad",
            Stages = new List<WorldMapSearchStageDefinition>
            {
                new WorldMapSearchStageDefinition
                {
                    StageId = "first",
                    MinXPercent = 30f, MaxXPercent = 50f, MinYPercent = 30f, MaxYPercent = 50f
                },
                new WorldMapSearchStageDefinition
                {
                    StageId = "second-wider",
                    RequiredKnowledgeFlags = new List<string> { "flag" },
                    MinXPercent = 0f, MaxXPercent = 100f, MinYPercent = 0f, MaxYPercent = 100f
                }
            }
        };

        List<string> issues = WorldMapSearchAreaValidator.Validate(search, 40f, 40f);

        Assert.That(issues, Has.Some.Contains("second-wider"));
        Assert.That(issues, Has.Some.Contains("больше"));
    }
}

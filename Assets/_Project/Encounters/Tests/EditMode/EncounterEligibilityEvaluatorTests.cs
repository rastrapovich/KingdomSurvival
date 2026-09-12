using System.Collections.Generic;
using KingdomSurvival.Encounters;
using NUnit.Framework;

// E01-T04.
public sealed class EncounterEligibilityEvaluatorTests
{
    private static EncounterDefinition MakeEncounter()
    {
        return new EncounterDefinition
        {
            EncounterId = "A",
            Status = EncounterStatus.Production,
            SelectionMode = EncounterSelectionMode.Pool,
            PoolId = "POOL_01",
            DialogueId = "some_dialogue",
            SelectionWeight = 1,
            MaxOccurrencesPerGame = 1
        };
    }

    private static NarrativeEvaluationContext MakeContext()
    {
        return new NarrativeEvaluationContext(new HeroProfileData(), new NarrativeStateData());
    }

    [Test]
    public void Draft_Status_Is_Blocked()
    {
        EncounterDefinition encounter = MakeEncounter();
        encounter.Status = EncounterStatus.Draft;

        EncounterEligibilityResult result = EncounterEligibilityEvaluator.Evaluate(
            encounter, MakeContext(), new EncounterRuntimeStateData(), "road", new List<string>(), 0);

        Assert.That(result.Eligible, Is.False);
    }

    [Test]
    public void Disabled_Status_Is_Blocked()
    {
        EncounterDefinition encounter = MakeEncounter();
        encounter.Status = EncounterStatus.Disabled;

        EncounterEligibilityResult result = EncounterEligibilityEvaluator.Evaluate(
            encounter, MakeContext(), new EncounterRuntimeStateData(), "road", new List<string>(), 0);

        Assert.That(result.Eligible, Is.False);
    }

    [Test]
    public void Production_Status_With_No_Other_Constraints_Is_Eligible()
    {
        EncounterDefinition encounter = MakeEncounter();

        EncounterEligibilityResult result = EncounterEligibilityEvaluator.Evaluate(
            encounter, MakeContext(), new EncounterRuntimeStateData(), "road", new List<string>(), 0);

        Assert.That(result.Eligible, Is.True);
    }

    [Test]
    public void Forbidden_Flag_Present_Blocks()
    {
        EncounterDefinition encounter = MakeEncounter();
        encounter.ForbiddenFlags.Add("SEEN");

        NarrativeStateData state = new NarrativeStateData();
        state.SetFlag("SEEN");
        NarrativeEvaluationContext context = new NarrativeEvaluationContext(new HeroProfileData(), state);

        EncounterEligibilityResult result = EncounterEligibilityEvaluator.Evaluate(
            encounter, context, new EncounterRuntimeStateData(), "road", new List<string>(), 0);

        Assert.That(result.Eligible, Is.False);
    }

    [Test]
    public void RequiredFlagsAll_Blocks_Until_All_Set()
    {
        EncounterDefinition encounter = MakeEncounter();
        encounter.RequiredFlagsAll.Add("F1");
        encounter.RequiredFlagsAll.Add("F2");

        NarrativeStateData state = new NarrativeStateData();
        state.SetFlag("F1");
        NarrativeEvaluationContext context = new NarrativeEvaluationContext(new HeroProfileData(), state);

        EncounterEligibilityResult blocked = EncounterEligibilityEvaluator.Evaluate(
            encounter, context, new EncounterRuntimeStateData(), "road", new List<string>(), 0);
        Assert.That(blocked.Eligible, Is.False);

        state.SetFlag("F2");
        EncounterEligibilityResult eligible = EncounterEligibilityEvaluator.Evaluate(
            encounter, context, new EncounterRuntimeStateData(), "road", new List<string>(), 0);
        Assert.That(eligible.Eligible, Is.True);
    }

    [Test]
    public void RequiredConditions_Uses_NarrativeConditionGroup()
    {
        EncounterDefinition encounter = MakeEncounter();
        encounter.RequiredConditions.Conditions.Add(new NarrativeCondition
        {
            Type = NarrativeConditionType.PartySizeAtLeast,
            IntParam = 3
        });

        NarrativeEvaluationContext soloContext = new NarrativeEvaluationContext(
            new HeroProfileData(), new NarrativeStateData(), partySize: 1);
        Assert.That(EncounterEligibilityEvaluator.Evaluate(
            encounter, soloContext, new EncounterRuntimeStateData(), "road", new List<string>(), 0).Eligible, Is.False);

        NarrativeEvaluationContext groupContext = new NarrativeEvaluationContext(
            new HeroProfileData(), new NarrativeStateData(), partySize: 3);
        Assert.That(EncounterEligibilityEvaluator.Evaluate(
            encounter, groupContext, new EncounterRuntimeStateData(), "road", new List<string>(), 0).Eligible, Is.True);
    }

    [Test]
    public void MaxOccurrences_Blocks_When_Reached_Unless_Unlimited()
    {
        EncounterDefinition encounter = MakeEncounter();
        encounter.MaxOccurrencesPerGame = 1;

        EncounterRuntimeStateData runtime = new EncounterRuntimeStateData();
        runtime.FindOrCreateEntry("A").TimesStarted = 1;

        Assert.That(EncounterEligibilityEvaluator.Evaluate(
            encounter, MakeContext(), runtime, "road", new List<string>(), 0).Eligible, Is.False);

        encounter.UnlimitedOccurrences = true;
        Assert.That(EncounterEligibilityEvaluator.Evaluate(
            encounter, MakeContext(), runtime, "road", new List<string>(), 0).Eligible, Is.True);
    }

    [Test]
    public void Cooldown_Blocks_Until_Elapsed()
    {
        EncounterDefinition encounter = MakeEncounter();
        encounter.MaxOccurrencesPerGame = 100;
        encounter.CooldownHours = 24;

        EncounterRuntimeStateData runtime = new EncounterRuntimeStateData();
        EncounterRuntimeEntry entry = runtime.FindOrCreateEntry("A");
        entry.LastStartedWorldHour = 10;

        Assert.That(EncounterEligibilityEvaluator.Evaluate(
            encounter, MakeContext(), runtime, "road", new List<string>(), 20).Eligible, Is.False);

        Assert.That(EncounterEligibilityEvaluator.Evaluate(
            encounter, MakeContext(), runtime, "road", new List<string>(), 40).Eligible, Is.True);
    }

    [Test]
    public void Region_And_LocationTags_Are_Checked()
    {
        EncounterDefinition encounter = MakeEncounter();
        encounter.AllowedRegionIds.Add("road");
        encounter.RequiredLocationTags.Add("forest-edge");
        encounter.ForbiddenLocationTags.Add("night");

        Assert.That(EncounterEligibilityEvaluator.Evaluate(
            encounter, MakeContext(), new EncounterRuntimeStateData(), "camp", new List<string> { "forest-edge" }, 0).Eligible,
            Is.False, "Неверный регион должен блокировать.");

        Assert.That(EncounterEligibilityEvaluator.Evaluate(
            encounter, MakeContext(), new EncounterRuntimeStateData(), "road", new List<string>(), 0).Eligible,
            Is.False, "Отсутствие обязательного тега должно блокировать.");

        Assert.That(EncounterEligibilityEvaluator.Evaluate(
            encounter, MakeContext(), new EncounterRuntimeStateData(), "road", new List<string> { "forest-edge", "night" }, 0).Eligible,
            Is.False, "Запрещённый тег должен блокировать.");

        Assert.That(EncounterEligibilityEvaluator.Evaluate(
            encounter, MakeContext(), new EncounterRuntimeStateData(), "road", new List<string> { "forest-edge" }, 0).Eligible,
            Is.True);
    }

    [Test]
    public void Empty_Region_And_Tag_Lists_Do_Not_Block()
    {
        EncounterDefinition encounter = MakeEncounter();

        EncounterEligibilityResult result = EncounterEligibilityEvaluator.Evaluate(
            encounter, MakeContext(), new EncounterRuntimeStateData(), "any_region", new List<string> { "any_tag" }, 0);

        Assert.That(result.Eligible, Is.True);
    }
}

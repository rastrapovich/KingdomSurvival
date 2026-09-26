using System.Linq;
using KingdomSurvival.Encounters;
using NUnit.Framework;

// Канон v1.48 §27.4: законченная встреча — опыт тем, кто её пережил; микро —
// без опыта; повторное завершение не даёт опыта снова.
public sealed class EncounterExperienceTests
{
    [Test]
    public void DurationClass_MicroGivesNothing_LongerGivesMore()
    {
        Assert.AreEqual(0, EncounterExperience.ForDurationClass(EncounterDurationClass.Reaction));
        Assert.AreEqual(0, EncounterExperience.ForDurationClass(EncounterDurationClass.Micro));
        Assert.Greater(EncounterExperience.ForDurationClass(EncounterDurationClass.Standard),
            EncounterExperience.ForDurationClass(EncounterDurationClass.Short));
        Assert.Greater(EncounterExperience.ForDurationClass(EncounterDurationClass.Complex),
            EncounterExperience.ForDurationClass(EncounterDurationClass.Standard));
    }

    [Test]
    public void Award_HeroOnce()
    {
        GameState state = new CampaignSetup { WorldSeed = 5 }.CreateCampaign();
        EncounterDefinition encounter = new EncounterDefinition
        {
            EncounterId = "TEST_STANDARD",
            DurationClass = EncounterDurationClass.Standard
        };

        Assert.AreEqual(1, EncounterExperience.Award(state, encounter).Count);
        Assert.AreEqual(0, EncounterExperience.Award(state, encounter).Count, "Повторное завершение — не новый опыт.");
        string heroId = state.GetSelectedCommander().Id;
        Assert.AreEqual(EncounterExperience.ForDurationClass(EncounterDurationClass.Standard),
            CharacterProgressionService.Get(state, heroId).Experience);
        Assert.IsTrue(state.Progression.AppliedSources.Any(source => source.StartsWith("encounter.TEST_STANDARD")));
    }
}

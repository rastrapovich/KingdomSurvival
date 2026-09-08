using NUnit.Framework;

// Этап 4 производственной инструкции "качества, проверки и реактивный
// текст": один дорожный Encounter, подключённый к общему
// NarrativeCheckResolver через ExpeditionIncidentSystem.
public sealed class RoadPredatorEncounterTests
{
    [Test]
    public void Failure_Gives_Unaware_State()
    {
        HeroProfileData hero = new HeroProfileData();
        hero.SetQuality(HeroQuality.Instinct, 1);

        ExpeditionIncidentSystem.RoadPredatorEntryState state = ExpeditionIncidentSystem.ResolveRoadPredatorEntryState(
            hero, new NarrativeStateData(), "test_unaware", out bool naturalistBonus);

        Assert.AreEqual(ExpeditionIncidentSystem.RoadPredatorEntryState.Unaware, state);
        Assert.IsFalse(naturalistBonus);
    }

    [Test]
    public void Success_Without_KnowsTheWay_Gives_Aware_State()
    {
        HeroProfileData hero = new HeroProfileData();
        hero.SetQuality(HeroQuality.Instinct, 8);

        ExpeditionIncidentSystem.RoadPredatorEntryState state = ExpeditionIncidentSystem.ResolveRoadPredatorEntryState(
            hero, new NarrativeStateData(), "test_aware", out _);

        Assert.AreEqual(ExpeditionIncidentSystem.RoadPredatorEntryState.Aware, state);
    }

    [Test]
    public void Success_With_KnowsTheWay_Gives_Prepared_State()
    {
        HeroProfileData hero = new HeroProfileData();
        hero.SetQuality(HeroQuality.Instinct, 8);
        hero.GrantTrait(NarrativeTraitIds.KnowsTheWay);

        ExpeditionIncidentSystem.RoadPredatorEntryState state = ExpeditionIncidentSystem.ResolveRoadPredatorEntryState(
            hero, new NarrativeStateData(), "test_prepared", out _);

        Assert.AreEqual(ExpeditionIncidentSystem.RoadPredatorEntryState.Prepared, state);
    }

    // §15: "Особенность не гарантирует хороший исход" — knows_the_way не
    // отменяет провал пассивного обнаружения.
    [Test]
    public void KnowsTheWay_Does_Not_Guarantee_Success()
    {
        HeroProfileData hero = new HeroProfileData();
        hero.SetQuality(HeroQuality.Instinct, 1);
        hero.GrantTrait(NarrativeTraitIds.KnowsTheWay);

        ExpeditionIncidentSystem.RoadPredatorEntryState state = ExpeditionIncidentSystem.ResolveRoadPredatorEntryState(
            hero, new NarrativeStateData(), "test_knows_the_way_fail", out _);

        Assert.AreEqual(ExpeditionIncidentSystem.RoadPredatorEntryState.Unaware, state);
    }

    // §4: naturalist не работает независимо от исхода обнаружения — только
    // когда встреча вообще замечена.
    [Test]
    public void Naturalist_Only_Applies_When_Detected()
    {
        HeroProfileData failingHero = new HeroProfileData();
        failingHero.SetQuality(HeroQuality.Instinct, 1);
        failingHero.GrantTrait(NarrativeTraitIds.Naturalist);
        ExpeditionIncidentSystem.ResolveRoadPredatorEntryState(
            failingHero, new NarrativeStateData(), "test_naturalist_fail", out bool naturalistBonusOnFailure);
        Assert.IsFalse(naturalistBonusOnFailure);

        HeroProfileData succeedingHero = new HeroProfileData();
        succeedingHero.SetQuality(HeroQuality.Instinct, 8);
        succeedingHero.GrantTrait(NarrativeTraitIds.Naturalist);
        ExpeditionIncidentSystem.ResolveRoadPredatorEntryState(
            succeedingHero, new NarrativeStateData(), "test_naturalist_success", out bool naturalistBonusOnSuccess);
        Assert.IsTrue(naturalistBonusOnSuccess);
    }
}

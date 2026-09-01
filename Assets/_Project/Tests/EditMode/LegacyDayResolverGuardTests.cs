using NUnit.Framework;

#pragma warning disable CS0618

public class LegacyDayResolverGuardTests
{
    [Test]
    public void LegacyResolveDay_CannotAdvanceOrMutateSimulation()
    {
        GameState state = new GameState();
        state.CreateNewGame(91234);

        int day = state.Day;
        int gold = state.Gold;
        int food = state.Food;
        int population = state.Population;
        int mood = state.Mood;

        DayResolutionResult result = DayResolver.ResolveDay(state);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Messages, Is.Empty);
        Assert.That(state.Day, Is.EqualTo(day));
        Assert.That(state.Gold, Is.EqualTo(gold));
        Assert.That(state.Food, Is.EqualTo(food));
        Assert.That(state.Population, Is.EqualTo(population));
        Assert.That(state.Mood, Is.EqualTo(mood));
    }
}

#pragma warning restore CS0618

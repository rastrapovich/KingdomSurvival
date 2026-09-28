using System.Linq;
using KingdomSurvival.Chapter01;
using KingdomSurvival.FreePlay;
using NUnit.Framework;

// ПР-12Б: контрольный итог свободной игры — после первого возвращения в
// «Делах», каждые три похода — запись Хроники и донесение; партия не
// завершается.
public sealed class FreePlaySummaryTests
{
    [SetUp]
    public void SetUp()
    {
        Chapter01Content.Register();
        FreePlayContent.Register();
    }

    private static GameState NewFreePlay()
    {
        GameState state = new CampaignSetup { CrisisId = CampaignStartOptions.FreePlayId, WorldSeed = 20260928 }.CreateCampaign();
        if (state.Narrative == null)
            state.Narrative = new NarrativeStateData();
        return state;
    }

    // Поход туда и обратно — те же записи, что ведёт FreePlayContent.Refresh.
    private static void Trip(GameState state)
    {
        int n = FreePlayContent.CountEntries(state, FreePlayContent.ReturnPrefix) + 1;
        Chronicle.Record(state, FreePlayContent.DeparturePrefix + n, "Выход в поход", "Отряд ушёл из Дома.");
        Chronicle.Record(state, FreePlayContent.ReturnPrefix + n, "Возвращение", "Отряд вернулся в Дом.");
    }

    private static JournalGoalViewData Goal(GameState state) =>
        CampaignContent.BuildGoals(state).FirstOrDefault(g => g.Id == FreePlaySummary.GoalId);

    [Test]
    public void NoTrips_NoSummary()
    {
        Assert.IsNull(Goal(NewFreePlay()));
    }

    [Test]
    public void AfterTrip_SummaryShowsHomeDecisionsAndOpenThreads()
    {
        GameState state = NewFreePlay();
        Trip(state);
        state.Narrative.SetFlag(FreePlayCoalStory.Flags.Asked);
        state.Narrative.SetFlag(FreePlayCoalStory.Flags.Trade);
        FreePlayContent.RefreshWithReports(state);
        HomePeopleService.MarkDead(state, "garrick", "test");

        JournalGoalViewData goal = Goal(state);
        Assert.IsNotNull(goal);
        Assert.AreEqual(JournalGoalCategory.Main, goal.Category);
        StringAssert.Contains("Походов: 1", goal.Description);
        StringAssert.Contains("Погибли: Гаррик", goal.Description);
        StringAssert.Contains("Уголь у Лады: есть", goal.Description);
        StringAssert.Contains("Что было:", goal.Description);
        StringAssert.Contains("Уголь для Лады: Углежоги отдали уголь", goal.Description, "Решение и его след.");
        StringAssert.Contains("Что ещё ждёт:", goal.Description);
        Assert.IsFalse(goal.Description.Contains("Выход в поход"), "Выходы и возвращения — не решения.");
    }

    [Test]
    public void Checkpoint_EveryThreeTrips_Once_NotWhileAway()
    {
        GameState state = NewFreePlay();
        Trip(state);
        Trip(state);
        Assert.IsEmpty(FreePlaySummary.Refresh(state));

        Trip(state);
        string report = FreePlaySummary.Refresh(state).Single();
        StringAssert.Contains("после 3 походов", report);
        Assert.IsNotNull(Chronicle.Find(state, FreePlaySummary.CheckpointPrefix + "3"));
        Assert.IsEmpty(FreePlaySummary.Refresh(state), "Один раз.");
        Assert.IsFalse(Goal(state).Description.Contains("Итог Дома: походов"), "Итог не пересказывает сам себя.");

        GameState away = NewFreePlay();
        Trip(away);
        Trip(away);
        Trip(away);
        away.ArmySupply = 50;
        LocationData target = away.Locations.First(l => !l.IsWaypoint);
        Assert.IsTrue(away.TryStartExpedition(target.Id, new System.Collections.Generic.List<string> { "garrick" }, out string message), message);
        Assert.IsEmpty(FreePlaySummary.Refresh(away), "В походе итог не подводят — дождётся возвращения.");
    }
}

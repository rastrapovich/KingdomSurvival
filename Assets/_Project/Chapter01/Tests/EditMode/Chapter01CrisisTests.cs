using KingdomSurvival.Chapter01;
using NUnit.Framework;

// ПР-05: первая глава ведёт только кампанию своего кризиса.
public sealed class Chapter01CrisisTests
{
    [Test]
    public void Chapter_IsActive_ForItsCrisis_AndForCampaignsWithoutConfiguration()
    {
        GameState configured = new CampaignSetup().CreateCampaign();
        Assert.IsTrue(Chapter01Crisis.IsActive(configured));

        GameState legacy = new GameState();
        legacy.CreateNewGame(1);
        Assert.IsNull(legacy.Configuration);
        Assert.IsTrue(Chapter01Crisis.IsActive(legacy), "Партии старше ПР-05 — кампании единственного кризиса.");
    }

    [Test]
    public void Chapter_IsSilent_InAnotherCrisis()
    {
        GameState other = new CampaignSetup().CreateCampaign();
        other.Configuration.CrisisId = "crisis.other";

        Assert.IsFalse(Chapter01Crisis.IsActive(other));
        Assert.IsNull(Chapter01StoryDirector.GetAutoOpenHomeDialogueId(other), "N01 не открывается в чужом кризисе.");
        Assert.IsEmpty(Chapter01HomeActivities.GetAvailable(other));
    }
}

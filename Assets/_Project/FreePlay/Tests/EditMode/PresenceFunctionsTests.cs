using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

// 12Е-9 плана: функции присутствия — что человек даёт отряду в пути, как
// данные (PresenceFunctions); их читают дела лагеря. Марта и Агнесса —
// персонажи-заготовки (канон v1.59), в отряд их принимают явно.
public sealed class PresenceFunctionsTests
{
    private static GameState OnTheRoad(params string[] fighters)
    {
        GameState gameState = new CampaignSetup { WorldSeed = 20260928 }.CreateCampaign();
        TestPeople.AdmitAll(gameState);
        gameState.ArmySupply = 100;
        LocationData target = gameState.Locations.First(location => !location.IsWaypoint);
        Assert.IsTrue(gameState.TryStartExpedition(target.Id, fighters.ToList(), out string message), message);
        gameState.ActiveExpedition.RouteIndex = 1;
        return gameState;
    }

    [Test]
    public void Camp_WithoutMarta_NoBandage()
    {
        GameState gameState = OnTheRoad("garrick");
        CampActionOption bandage = CampRest.GetActions(gameState).Single(option => option.Kind == CampActionKind.Bandage);
        Assert.IsFalse(bandage.Available);
        StringAssert.Contains("Марта не в отряде", bandage.UnavailableReason);
    }

    [Test]
    public void Provider_PersonalAndAnyFighter()
    {
        GameState gameState = OnTheRoad("garrick", CampRest.MartaId);
        List<string> party = CampRest.PartyIds(gameState);
        List<string> fighters = gameState.ActiveExpedition.FighterIds;
        Assert.AreEqual(CampRest.MartaId, PresenceFunctions.Provider(gameState, PresenceFunctions.Bandage, party, fighters));
        Assert.IsNull(PresenceFunctions.Provider(gameState, PresenceFunctions.Inspect, party, fighters), "Ни Агнессы, ни Остафия.");
        Assert.AreEqual("garrick", PresenceFunctions.Provider(gameState, PresenceFunctions.Watch, party, fighters), "Дозор — любой боец.");

        PeopleService.MarkDead(gameState, CampRest.MartaId, "test");
        Assert.IsNull(PresenceFunctions.Provider(gameState, PresenceFunctions.Bandage, CampRest.PartyIds(gameState), gameState.ActiveExpedition.FighterIds));
    }
}

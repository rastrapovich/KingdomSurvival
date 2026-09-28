using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using static Chapter01PlaythroughWalker;

// 12Е-9 плана: функции присутствия — что человек даёт отряду в пути, как
// данные (PresenceFunctions), и их показ на подготовке похода: «без Марты
// не будет перевязки в пути». Марта и Агнесса — люди кампании главы.
public sealed class PresenceFunctionsTests
{
    private static List<string> Road(GameState gameState) => HomeOverview.DescribeRoad(gameState);

    [Test]
    public void Genitive_ForHomeNames()
    {
        Assert.AreEqual("Марты", PresenceFunctions.Genitive("Марта"));
        Assert.AreEqual("Агнессы", PresenceFunctions.Genitive("Агнесса"));
        Assert.AreEqual("Остафия", PresenceFunctions.Genitive("Остафий"));
    }

    [Test]
    public void Preparation_WithoutSpecialists_SaysWhatWillBeMissing()
    {
        GameState gameState = NewGame(20260928);

        List<string> road = Road(gameState);
        CollectionAssert.Contains(road, "Без Марты не будет: перевязка раненых.");
        CollectionAssert.Contains(road, "Без Агнессы или Остафия не будет: осмотр окрестностей.");
        CollectionAssert.Contains(road, "Без бойца не будет: дозор.");
    }

    [Test]
    public void Preparation_WithMarta_BandageAndWatchOnTheRoad()
    {
        GameState gameState = NewGame(20260928);
        Assert.IsTrue(ExpeditionPreparation.TryAddFighter(gameState, CampRest.MartaId, out string message), message);

        List<string> road = Road(gameState);
        Assert.IsTrue(road.Any(line => line.StartsWith("Перевязка раненых — Марта:")), string.Join("\n", road));
        Assert.IsTrue(road.Any(line => line.StartsWith("Дозор — бойцы отряда:")));
        Assert.IsFalse(road.Any(line => line.Contains("Без Марты")));
        Assert.IsTrue(road.First().StartsWith("Без "), "Сначала то, чего не будет.");
    }

    [Test]
    public void DeadSpecialist_FunctionNotListed()
    {
        GameState gameState = NewGame(20260928);
        HomePeopleService.MarkDead(gameState, CampRest.MartaId, "test");

        Assert.IsFalse(Road(gameState).Any(line => line.Contains("перевязка")), "В этом Доме перевязывать некому вовсе.");
    }

    [Test]
    public void CandidateCard_ShowsOnlyPersonalFunctions()
    {
        CollectionAssert.AreEqual(new[] { PresenceFunctions.Bandage }, PresenceFunctions.ProvidedBy(CampRest.MartaId));
        CollectionAssert.AreEqual(new[] { PresenceFunctions.Inspect }, PresenceFunctions.ProvidedBy(HomePeopleService.OstafiyId));
        Assert.IsEmpty(PresenceFunctions.ProvidedBy("garrick"), "Дозор может любой боец — на карточке не повторяется.");
    }

    [Test]
    public void Camp_UsesTheSameFunctions()
    {
        GameState gameState = NewGame(20260928);
        gameState.ArmySupply = 100;
        LocationData target = gameState.Locations.First(location => !location.IsWaypoint);
        Assert.IsTrue(gameState.TryStartExpedition(target.Id, new List<string> { "garrick" }, out string message), message);
        gameState.ActiveExpedition.RouteIndex = 1;

        CampActionOption bandage = CampRest.GetActions(gameState).Single(option => option.Kind == CampActionKind.Bandage);
        Assert.IsFalse(bandage.Available);
        StringAssert.Contains("Марта не в отряде", bandage.UnavailableReason);
    }
}

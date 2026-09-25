using System.Collections.Generic;
using System.Linq;
using KingdomSurvival.Chapter01;
using KingdomSurvival.DialogueDatabase;
using NUnit.Framework;
using UnityEngine;
using static Chapter01PlaythroughWalker;

// ПР-10: сюжетный бой главы «Звери у стоянки» — когда открывается, что
// несёт запрос, и что сцена-вступление есть в базе диалогов.
public sealed class Chapter01CampBattleTests
{
    private static GameState RestingOnTheRoad(double hoursIntoRest)
    {
        GameState gameState = NewGame(20260925);
        gameState.Narrative.SetFlag(Chapter01Ids.Flags.ExpeditionStarted);
        gameState.ArmySupply = 50;
        LocationData target = gameState.Locations.First(location => !location.IsWaypoint);
        Assert.IsTrue(gameState.TryStartExpedition(target.Id, new List<string> { "garrick" }, out string message), message);
        gameState.ActiveExpedition.RouteIndex = 1;
        Assert.IsTrue(CampRest.TryStartRest(gameState, out message), message);
        gameState.ActiveExpedition.ActiveActivity.RemainingHours -= hoursIntoRest;
        return gameState;
    }

    [Test]
    public void Intro_OpensOnSecondHourOfFirstRest_Once()
    {
        Assert.IsNull(Chapter01CampBattle.GetPendingDialogueId(RestingOnTheRoad(1.0)));

        GameState gameState = RestingOnTheRoad(2.0);
        Assert.AreEqual(Chapter01Ids.Dialogues.CampBeasts, Chapter01CampBattle.GetPendingDialogueId(gameState));

        gameState.Narrative.SetFlag(Chapter01Ids.Flags.CampBeastsTriggered);
        Assert.IsNull(Chapter01CampBattle.GetPendingDialogueId(gameState), "Один раз за главу.");
    }

    [Test]
    public void Intro_NotOnReturnOrWithoutRest()
    {
        GameState returning = RestingOnTheRoad(3.0);
        returning.Narrative.SetFlag(Chapter01Ids.Flags.ReturnStarted);
        Assert.IsNull(Chapter01CampBattle.GetPendingDialogueId(returning));

        GameState notResting = RestingOnTheRoad(3.0);
        notResting.ActiveExpedition.ActiveActivity = null;
        Assert.IsNull(Chapter01CampBattle.GetPendingDialogueId(notResting));
    }

    [Test]
    public void Request_HasBeasts_Retreat_AndSource()
    {
        GameState gameState = RestingOnTheRoad(2.0);
        CampaignBattleRequest request = Chapter01CampBattle.CreateRequest(gameState);

        Assert.AreEqual(Chapter01CampBattle.BattleId, request.BattleId);
        Assert.IsTrue(request.AllowRetreat);
        Assert.AreEqual(Chapter01Ids.Dialogues.CampBeasts, request.SourceId);
        Assert.AreEqual(3, request.Enemies.Sum(e => e.Count));
        CollectionAssert.AreEquivalent(new[] { "forest_beast", "forest_beast_alpha" }, request.Enemies.Select(e => e.UnitTypeId));
        Assert.IsTrue(request.Participants.Any(p => p.IsHero));
    }

    [Test]
    public void IntroDialogue_ExistsAndEndsWithFight()
    {
        DialogueDatabaseAsset database = Resources.Load<DialogueDatabaseAsset>(DialogueDatabaseAsset.ResourcesPath);
        GameState gameState = RestingOnTheRoad(2.0);
        NarrativeDialogueRuntimeSession session = new NarrativeDialogueRuntimeSession();
        Assert.IsTrue(session.Start(database, Chapter01Ids.Dialogues.CampBeasts,
            gameState.GetSelectedCommander().HeroProfile ?? new HeroProfileData(), gameState.Narrative,
            out NarrativeDialogueView view, out string error, new List<string>(), new List<string>(),
            gameState.WorldSeed, 2, gameState), error);

        for (int i = 0; i < 10 && view.AvailableChoices.Count == 1 && view.AvailableChoices[0].Kind == DialogueChoiceKind.Continue; i++)
            view = session.SelectChoice(view.AvailableChoices[0].ChoiceId).View;
        Assert.AreEqual("К оружию!", view.AvailableChoices.Single().Text);
        Assert.IsTrue(session.SelectChoice(view.AvailableChoices[0].ChoiceId).DialogueEnded);
    }
}

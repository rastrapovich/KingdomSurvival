using KingdomSurvival.Chapter01;
using NUnit.Framework;
using UnityEngine;

// ПР-04: после загрузки уже полученная награда и уже обработанная встреча
// не повторяются — отметки о применении живут в сохранении.
public sealed class Chapter01SaveLoadIdempotencyTests
{
    private static GameState SaveAndLoad(GameState state)
    {
        string json = JsonUtility.ToJson(CampaignSaveService.ExportCampaign(state));
        return CampaignSaveService.RestoreCampaign(JsonUtility.FromJson<CampaignSaveData>(json));
    }

    private static int CountItem(GameState state, string itemId)
    {
        int count = 0;
        foreach (string item in state.Narrative.Items)
        {
            if (item == itemId)
                count++;
        }
        return count;
    }

    [Test]
    public void ChapterReward_IsNotGrantedAgain_AfterLoad()
    {
        GameState state = new GameState();
        state.CreateNewGame(20260924);
        state.Narrative.SetFlag(Chapter01Ids.Flags.OldTraceFound);
        Chapter01OutcomeApplier.ApplySevenTeethInvestigationConsequences(state);
        Assert.AreEqual(1, CountItem(state, Chapter01Ids.Items.SevenToothGauge));

        GameState restored = SaveAndLoad(state);
        Chapter01OutcomeApplier.ApplySevenTeethInvestigationConsequences(restored);

        Assert.AreEqual(1, CountItem(restored, Chapter01Ids.Items.SevenToothGauge),
            "Семизубая планка не выдаётся второй раз после загрузки.");
    }

    [Test]
    public void ProcessedEncounterOpportunity_StaysProcessed_AfterLoad()
    {
        GameState state = new GameState();
        state.CreateNewGame(20260924);
        state.Encounters = new EncounterRuntimeStateData();
        state.Encounters.MarkOpportunityProcessed("road.day3");

        GameState restored = SaveAndLoad(state);

        Assert.IsTrue(restored.Encounters.WasOpportunityProcessed("road.day3"),
            "Уже обработанная встреча не выпадает повторно после загрузки.");
    }
}

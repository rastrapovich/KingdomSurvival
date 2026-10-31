using System.Collections.Generic;
using System.Linq;
using KingdomSurvival.DialogueDatabase;
using KingdomSurvival.Encounters;
using KingdomSurvival.FreePlay;
using NUnit.Framework;
using UnityEngine;

// Канон v1.59 (бриф удаления «Дома на чужой воде»): свободная игра —
// единственный режим. Новая партия начинается в лагере с одним Командиром,
// без исходного Дома, его людей, запасов и суточных итогов; персонажи
// удалённой кампании — неактивные заготовки; старые сохранения свободной
// игры преобразуются явно, сохранения удалённой кампании не загружаются.
public sealed class FreePlayIsolationTests
{
    private const int Seed = 20260925;

    private static readonly string[] HomeWords = { "Дом", "дома", "Живых людей", "Лада", "Остафий", "уголь", "Уголь" };

    [SetUp]
    public void SetUp()
    {
        FreePlayContent.Register();
    }

    private static GameState NewFreePlay()
    {
        return new CampaignSetup { WorldSeed = Seed }.CreateCampaign();
    }

    private static List<string> AdvanceHours(GameState state, double hours)
    {
        ContinuousSimulationSystem.SetPaused(state, false);
        ContinuousSimulationBatch batch = ContinuousSimulationSystem.Advance(
            state, (float)(hours / ContinuousSimulationSystem.GameHoursPerRealSecond), false);
        List<string> messages = new List<string>(batch.Result.Messages);
        messages.AddRange(FreePlayContent.RefreshWithReports(state));
        return messages;
    }

    private static void AssertNoHomeReports(IEnumerable<string> messages, string when)
    {
        foreach (string message in messages)
        {
            foreach (string word in HomeWords)
                StringAssert.DoesNotContain(word, message, when + ": донесение исходного Дома.");
        }
    }

    [Test]
    public void NewGame_IsFreePlayInCamp_WithOnlyTheCommander()
    {
        GameState state = NewFreePlay();

        Assert.AreEqual(CampaignStartOptions.FreePlayId, state.Configuration.CrisisId);
        Assert.AreEqual(CampaignStartOptions.RoadCampStartId, state.Configuration.StartingConditionId);
        Assert.AreEqual(1, state.People.Residents.Count, "Только Командир — без жителей Дома.");
        Assert.AreEqual(state.GetSelectedCommander().Id, state.People.Residents[0].PersonId);
        Assert.IsEmpty(state.Fighters, "Бойцы приходят наймом.");
        Assert.IsFalse(state.HasActiveExpedition, "Отряд стоит на стоянке.");
        Assert.AreEqual(FreePlayContent.StartingSupply, state.ArmySupply);
        Assert.IsNotNull(Chronicle.Find(state, FreePlayContent.StartCampChronicleId), "Партия начинается в лагере.");
        foreach (CharacterTemplate template in CharacterTemplates.All)
            Assert.IsNull(PeopleService.Find(state, template.Id), template.DisplayName + " не знаком без встречи.");

        Assert.IsNull(state.FindLocation(FreePlayContent.RemovedRuinsLocationId), "Затопленных руин нет.");
        Assert.IsFalse(CampaignContent.BuildGoals(state).Any(), "Главной цели и дел без источника нет.");
    }

    [Test]
    public void SeveralDays_TravelRestAndReturn_NoHomeReports_HistorySurvivesSave()
    {
        GameState state = NewFreePlay();
        state.ArmySupply = 50;
        List<string> messages = new List<string>();
        for (int hour = 0; hour < 30; hour++)
            messages.AddRange(AdvanceHours(state, 1.0));
        Assert.GreaterOrEqual(state.Day, 2, "Сутки прошли на стоянке.");

        LocationData target = state.Locations.First(location => !location.IsWaypoint);
        target.IsVisibleOnMap = true;
        target.IsDiscovered = true;
        Assert.IsTrue(state.TryStartExpedition(target.Id, new List<string>(), out string message), message);
        messages.AddRange(AdvanceHours(state, 2.0));

        Assert.IsTrue(CampRest.TryStartRest(state, out message), message);
        for (int hour = 0; hour < 30; hour++)
            messages.AddRange(AdvanceHours(state, 1.0));

        Assert.IsTrue(state.TryOrderReturn(out message), message);
        for (int step = 0; step < 400 && state.HasActiveExpedition; step++)
            messages.AddRange(AdvanceHours(state, 1.0));
        Assert.IsFalse(state.HasActiveExpedition, "Отряд вернулся на стоянку.");
        messages.AddRange(FreePlayContent.RefreshWithReports(state));

        Assert.GreaterOrEqual(state.Day, 3);
        AssertNoHomeReports(messages, "несколько суток");
        Assert.AreEqual(1, state.People.Residents.Count, "Жители Дома не появились.");

        Assert.IsNotNull(Chronicle.Find(state, FreePlayContent.DeparturePrefix + "1"));
        ChronicleEntryData back = Chronicle.Find(state, FreePlayContent.ReturnPrefix + "1");
        Assert.IsNotNull(back);
        Assert.AreEqual(FreePlayContent.DeparturePrefix + "1", back.CauseId);

        GameState restored = CampaignSaveService.RestoreCampaign(
            JsonUtility.FromJson<CampaignSaveData>(JsonUtility.ToJson(CampaignSaveService.ExportCampaign(state))));
        FreePlayContent.Refresh(restored);
        Assert.AreEqual(Chronicle.Get(state).Entries.Count, Chronicle.Get(restored).Entries.Count, "Загрузка не дублирует записи.");
        Assert.AreEqual(state.Configuration.WorldSeed, restored.Configuration.WorldSeed);
    }

    [Test]
    public void OldFreePlaySave_Format2_IsConverted_HomeRemnantsDropped()
    {
        GameState state = NewFreePlay();
        state.ArmySupply = 40;
        // Формат 2: в партии жили все люди исходного Дома, Гаррик ушёл в поход.
        TestPeople.AdmitAll(state);
        LocationData target = state.Locations.First(location => !location.IsWaypoint);
        Assert.IsTrue(state.TryStartExpedition(target.Id, new List<string> { "garrick" }, out string message), message);
        // Кладовая Дома: находка без владельца и вещь удалённой кампании.
        state.Inventory.Items.Add(new ItemInstanceData { InstanceId = "item.9001", ItemId = ItemCatalog.Axe, OwnerPersonId = string.Empty });
        state.Inventory.Items.Add(new ItemInstanceData { InstanceId = "item.9002", ItemId = "item.storeroom_mail", OwnerPersonId = string.Empty });
        state.Narrative.GrantItem("chapter01.item.seven_tooth_gauge");

        CampaignSaveData data = CampaignSaveService.ExportCampaign(state);
        data.SaveFormatVersion = CampaignSaveService.FreePlayHomeSaveFormatVersion;
        // Поля Дома старого формата — JsonUtility их просто пропускает.
        string json = JsonUtility.ToJson(data).Replace("\"Day\":", "\"Food\":72,\"Population\":24,\"Gold\":120,\"Day\":");
        CampaignSaveData loaded = JsonUtility.FromJson<CampaignSaveData>(json);
        Assert.IsTrue(CampaignSaveService.IsLoadable(loaded, null, 0, out string reason), reason);

        GameState restored = CampaignSaveService.RestoreCampaign(loaded);
        string hero = restored.GetSelectedCommander().Id;

        CollectionAssert.AreEquivalent(new[] { hero, "garrick" }, restored.People.Residents.Select(r => r.PersonId),
            "Остаются Командир и те, кто в походе; жители Дома не оживают.");
        CollectionAssert.AreEquivalent(new[] { "garrick" }, restored.Fighters.Select(f => f.Id));
        Assert.IsFalse(restored.Inventory.Items.Any(i => string.IsNullOrEmpty(i.OwnerPersonId)), "Кладовой нет.");
        Assert.AreEqual(hero, restored.Inventory.Items.Single(i => i.InstanceId == "item.9001").OwnerPersonId, "Находка из кладовой — герою.");
        Assert.IsFalse(restored.Inventory.Items.Any(i => i.ItemId == "item.storeroom_mail"), "Вещь удалённой кампании снята.");
        Assert.IsFalse(restored.Inventory.Items.Any(i => i.OwnerPersonId == "torvin"), "Вещи ушедших жителей сняты вместе с ними.");
        CollectionAssert.DoesNotContain(restored.Narrative.Items, "chapter01.item.seven_tooth_gauge");
        Assert.IsTrue(restored.HasActiveExpedition);

        // После загрузки сутки идут без домашних итогов.
        AssertNoHomeReports(AdvanceHours(restored, 30.0), "после загрузки");

        // Следующее сохранение — уже в новом формате, повторной конвертации нет.
        Assert.AreEqual(CampaignSaveService.CurrentSaveFormatVersion, CampaignSaveService.ExportCampaign(restored).SaveFormatVersion);
    }

    [Test]
    public void RemovedCampaignSave_IsRejected_WithClearReason()
    {
        GameState state = NewFreePlay();
        CampaignSaveData data = CampaignSaveService.ExportCampaign(state);
        data.SaveFormatVersion = CampaignSaveService.FreePlayHomeSaveFormatVersion;
        data.State.Configuration.CrisisId = CampaignSaveService.RemovedCrisisId;

        Assert.IsFalse(CampaignSaveService.IsLoadable(data, null, 0, out string reason));
        StringAssert.Contains("Дом на чужой воде", reason);
        StringAssert.Contains("новую свободную игру", reason);

        data.State.Configuration = null;
        Assert.IsFalse(CampaignSaveService.IsLoadable(data, null, 0, out reason), "Партия до ПР-05 — тоже удалённая кампания.");
    }

    [Test]
    public void CharacterTemplates_KeepCampaignPeople_WithPortraits_AndNeutralTexts()
    {
        string[] expected = { "ostafiy", "lada", "miron", "ulyana", "tikhon", "varvara", "garrick", "edric", "marta", "torvin", "agnessa" };
        CollectionAssert.AreEquivalent(expected, CharacterTemplates.All.Select(t => t.Id));

        DialogueDatabaseAsset database = DialogueDatabaseRuntime.LoadDefaultDatabase();
        Assert.IsNotNull(database);
        foreach (CharacterTemplate template in CharacterTemplates.All)
        {
            Assert.IsNotEmpty(template.DisplayName, template.Id);
            Assert.IsNotEmpty(template.ShortDescription, template.Id);
            foreach (string removed in new[] { "Дом", "паводок", "плотин", "вода ведёт" })
                StringAssert.DoesNotContain(removed, template.ShortDescription, template.Id + ": ссылка на удалённую кампанию.");
            if (string.IsNullOrEmpty(template.DialogueSpeakerId))
                continue;
            DialogueSpeakerData speaker = database.FindSpeaker(template.DialogueSpeakerId);
            Assert.IsNotNull(speaker, template.Id + ": говорящий сохранён.");
        }
        foreach (string id in new[] { "ostafiy", "lada", "miron", "ulyana" })
        {
            DialogueSpeakerData speaker = database.FindSpeaker(id);
            Assert.IsNotNull(speaker?.Portrait, id + ": портрет на месте.");
        }
    }

    [Test]
    public void AdmitFromTemplate_IsExplicit_AndIdempotent()
    {
        GameState state = NewFreePlay();
        ResidentState lada = PeopleService.AdmitFromTemplate(state, "lada");
        Assert.IsNotNull(lada);
        Assert.AreEqual(ResidentTravelRole.Retinue, lada.TravelRole);
        Assert.AreSame(lada, PeopleService.AdmitFromTemplate(state, "lada"));
        Assert.AreEqual(2, state.People.Residents.Count);

        PeopleService.AdmitFromTemplate(state, "garrick");
        Assert.IsNotNull(state.FindFighter("garrick"), "Боец получает место в списке бойцов.");
        Assert.AreEqual(ItemCatalog.SwordAndShield, ItemService.Equipped(state, "garrick", ItemSlot.Weapon)?.ItemId);
    }

    [Test]
    public void ContentDatabases_HaveNoRemovedCampaignContent()
    {
        DialogueDatabaseAsset dialogues = DialogueDatabaseRuntime.LoadDefaultDatabase();
        foreach (DialogueDefinitionData dialogue in dialogues.Dialogues)
        {
            Assert.IsFalse(dialogue.Id.StartsWith("chapter01"), dialogue.Id);
            foreach (string removed in new[] { "freeplay_coal_", "freeplay_kurgan_", "freeplay_miller_", "freeplay_mine_ostafiy", "freeplay_mine_mark_talk" })
                Assert.IsFalse(dialogue.Id.StartsWith(removed), dialogue.Id);
        }

        EncounterDatabaseAsset encounters = Resources.Load<EncounterDatabaseAsset>("Encounters/KingdomSurvivalEncounters");
        Assert.IsNotNull(encounters);
        foreach (EncounterDefinition encounter in encounters.Encounters)
        {
            StringAssert.DoesNotStartWith("FREEPLAY_COAL", encounter.EncounterId);
            StringAssert.DoesNotStartWith("FREEPLAY_KURGAN", encounter.EncounterId);
            Assert.IsTrue(string.IsNullOrEmpty(encounter.DialogueId) || dialogues.FindDialogue(encounter.DialogueId) != null,
                encounter.EncounterId + ": сцена встречи существует.");
        }

        Assert.IsNull(ItemCatalog.Find("chapter01.item.seven_tooth_gauge"));
    }
}

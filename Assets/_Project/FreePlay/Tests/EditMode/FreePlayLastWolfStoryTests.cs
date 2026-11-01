using System.Collections.Generic;
using System.Linq;
using KingdomSurvival.DialogueDatabase;
using KingdomSurvival.FreePlay;
using KingdomSurvival.SettlementDatabase;
using NUnit.Framework;
using UnityEngine;

// «Последний волк» (квест мастерской quest_free_i1_coal, аудит A-04): один
// диалог с точками входа по сценам; у Чёрного леса — встреча, возвращение за
// добычей и эхо на хуторе; время пути и переноски один раз; еда и серебро один
// раз; местные выносят Савву к утру после сообщения или ухода героя; срок за
// добычей — до утра; весть до поселений — рассказом или сообщением углежогов.
public sealed class FreePlayLastWolfStoryTests
{
    private DialogueDatabaseAsset dialogues;

    [SetUp]
    public void SetUp()
    {
        FreePlayContent.Register();
        dialogues = Resources.Load<DialogueDatabaseAsset>(DialogueDatabaseAsset.ResourcesPath);
    }

    private static GameState NewState()
    {
        GameState state = new CampaignSetup { WorldSeed = 20261101 }.CreateCampaign();
        state.Narrative ??= new NarrativeStateData();
        state.ArmySupply = 10;
        state.ArmyGold = 0;
        return state;
    }

    private static void Flags(GameState state, params string[] flags)
    {
        foreach (string flag in flags)
            state.Narrative.SetFlag(flag);
    }

    private static string EntryNode(GameState state)
    {
        LocationEntryView view = FreePlayLastWolfStory.LocationEntry(state, FreePlayLastWolfStory.LocationId);
        if (view == null)
            return null;
        Assert.AreEqual(FreePlayLastWolfStory.DialogueId, DialogueEntryRef.DialogueId(view.DialogueId));
        return DialogueEntryRef.NodeId(view.DialogueId);
    }

    [Test]
    public void Dialogue_IsTransferred_WithSceneEntries_AndValidates()
    {
        DialogueDefinitionData dialogue = dialogues.FindDialogue(FreePlayLastWolfStory.DialogueId);
        Assert.IsNotNull(dialogue, "Диалог квеста перенесён из мастерской.");
        Assert.AreEqual(FreePlayLastWolfStory.Nodes.Arrival, dialogue.StartNodeId);
        foreach (string node in new[]
                 {
                     FreePlayLastWolfStory.Nodes.ReturnCorpse, FreePlayLastWolfStory.Nodes.ReturnEmpty, FreePlayLastWolfStory.Nodes.HutAgain,
                     FreePlayLastWolfStory.Nodes.InnBefore, FreePlayLastWolfStory.Nodes.InnReport, FreePlayLastWolfStory.Nodes.InnAfter
                 })
            Assert.IsTrue(dialogue.IsEntryNode(node), "Точка входа: " + node);
        Assert.IsFalse(dialogue.IsEntryNode("lw_settle"), "Середина разговора — не вход.");

        List<string> issues = new List<string>();
        dialogues.CollectValidationIssuesForDialogue(FreePlayLastWolfStory.DialogueId, issues);
        CollectionAssert.IsEmpty(issues, string.Join("\n", issues));
        Assert.IsNotNull(dialogues.FindSpeaker("last_wolf_savva"), "Савва — говорящий базы.");
    }

    [Test]
    public void RuntimeSession_StartsAtDeclaredEntry_RejectsOtherNodes()
    {
        GameState state = NewState();
        HeroProfileData hero = new HeroProfileData();
        NarrativeDialogueRuntimeSession session = new NarrativeDialogueRuntimeSession();
        Assert.IsTrue(session.Start(dialogues, FreePlayLastWolfStory.DialogueId, hero, state.Narrative, out _, out string error,
            gameState: state, startNodeId: FreePlayLastWolfStory.Nodes.InnBefore), error);
        Assert.AreEqual(FreePlayLastWolfStory.Nodes.InnBefore, session.CurrentNodeId);
        session.End();

        Assert.IsFalse(session.Start(dialogues, FreePlayLastWolfStory.DialogueId, hero, state.Narrative, out _, out error,
            gameState: state, startNodeId: "lw_settle"));
        StringAssert.Contains("точкой входа", error);

        Assert.IsTrue(session.Start(dialogues, FreePlayLastWolfStory.DialogueId, hero, state.Narrative, out _, out error, gameState: state), error);
        Assert.AreEqual(FreePlayLastWolfStory.Nodes.Arrival, session.CurrentNodeId, "Без узла — обычный старт.");
    }

    [Test]
    public void DialogueEntryRef_SplitsDialogueAndNode()
    {
        Assert.AreEqual("freeplay_last_wolf#lw_inn_report", DialogueEntryRef.Make("freeplay_last_wolf", "lw_inn_report"));
        Assert.AreEqual("freeplay_last_wolf", DialogueEntryRef.DialogueId("freeplay_last_wolf#lw_inn_report"));
        Assert.AreEqual("lw_inn_report", DialogueEntryRef.NodeId("freeplay_last_wolf#lw_inn_report"));
        Assert.AreEqual("settlement_village_hostess", DialogueEntryRef.DialogueId("settlement_village_hostess"));
        Assert.AreEqual(string.Empty, DialogueEntryRef.NodeId("settlement_village_hostess"));
    }

    [Test]
    public void Forest_RoutesMeeting_Return_LateReturn_Echo_ThenNothing()
    {
        GameState state = NewState();
        Assert.AreEqual(FreePlayLastWolfStory.Nodes.Arrival, EntryNode(state), "Встреча у выворотня.");
        Assert.IsNull(FreePlayLastWolfStory.LocationEntry(state, "mine"), "Только у Чёрного леса.");

        // Савва доставлен первым, добыча оставлена до утра.
        Flags(state, FreePlayLastWolfStory.Flags.Seen, FreePlayLastWolfStory.Flags.DeadSeen, FreePlayLastWolfStory.Flags.ProofLeft,
            FreePlayLastWolfStory.Flags.HunterSafe, FreePlayLastWolfStory.Flags.ReturnAvailable, FreePlayLastWolfStory.Flags.RescueComplete);
        FreePlayLastWolfStory.Refresh(state);
        Assert.AreEqual(FreePlayLastWolfStory.Nodes.ReturnCorpse, EntryNode(state), "До утра — за добычей.");

        state.Day += 2;
        List<string> reports = FreePlayLastWolfStory.Refresh(state);
        Assert.IsTrue(state.Narrative.HasFlag(FreePlayLastWolfStory.Flags.LocalsCollect), "Срок вышел — тушу забрали местные.");
        Assert.IsTrue(reports.Any(r => r.Contains("углежоги")), string.Join("\n", reports));
        Assert.AreEqual(FreePlayLastWolfStory.Nodes.ReturnEmpty, EntryNode(state), "После срока — пустое место, один раз.");

        Flags(state, FreePlayLastWolfStory.Flags.ReturnEmptySeen);
        Assert.AreEqual(FreePlayLastWolfStory.Nodes.HutAgain, EntryNode(state), "Затем — Савва на хуторе.");
        Flags(state, FreePlayLastWolfStory.Flags.HutAgainSeen);
        Assert.IsNull(EntryNode(state), "Исчерпанное место — обычное исследование.");
    }

    [Test]
    public void ReturnStartedBeforeDeadline_IsNotCutByMorning()
    {
        GameState state = NewState();
        Flags(state, FreePlayLastWolfStory.Flags.Seen, FreePlayLastWolfStory.Flags.DeadSeen, FreePlayLastWolfStory.Flags.ProofLeft,
            FreePlayLastWolfStory.Flags.HunterSafe, FreePlayLastWolfStory.Flags.ReturnAvailable);
        FreePlayLastWolfStory.Refresh(state);
        // Переноска завершена в разговоре до срока; часы прокручиваются после.
        Flags(state, FreePlayLastWolfStory.Flags.ProofRetrieved, FreePlayLastWolfStory.Flags.ProofDelivered);
        state.Day += 2;
        FreePlayLastWolfStory.Refresh(state);
        Assert.IsFalse(state.Narrative.HasFlag(FreePlayLastWolfStory.Flags.LocalsCollect));
        Assert.IsNotNull(Chronicle.Find(state, FreePlayLastWolfStory.ChronicleProofRetrieved));
    }

    [Test]
    public void SceneHours_AreGivenOncePerSegment()
    {
        GameState state = NewState();
        Flags(state, FreePlayLastWolfStory.Flags.ProofLeft, FreePlayLastWolfStory.Flags.HunterSafe);
        Assert.AreEqual(1.0, FreePlayLastWolfStory.HoursAfterDialogue(state, FreePlayLastWolfStory.DialogueId), 1e-6);
        Assert.AreEqual(0.0, FreePlayLastWolfStory.HoursAfterDialogue(state, FreePlayLastWolfStory.DialogueId), 1e-6, "Повтор не тратит время.");
        Flags(state, FreePlayLastWolfStory.Flags.ProofRetrieved, FreePlayLastWolfStory.Flags.ProofDelivered);
        Assert.AreEqual(2.0, FreePlayLastWolfStory.HoursAfterDialogue(state, FreePlayLastWolfStory.DialogueId), 1e-6, "Второй круг за добычей.");
        Assert.AreEqual(0.0, FreePlayLastWolfStory.HoursAfterDialogue(state, "freeplay_mine_entry"), 1e-6);

        GameState alone = NewState();
        Flags(alone, FreePlayLastWolfStory.Flags.HunterSafe, FreePlayLastWolfStory.Flags.ProofDelivered);
        Assert.AreEqual(3.0, FreePlayLastWolfStory.HoursAfterDialogue(alone, FreePlayLastWolfStory.DialogueId), 1e-6,
            "Волокуша в одиночку — три часа.");

        GameState message = NewState();
        Flags(message, FreePlayLastWolfStory.Flags.HelpSent, FreePlayLastWolfStory.Flags.WaitedHelpers,
            FreePlayLastWolfStory.Flags.HunterSafe, FreePlayLastWolfStory.Flags.ProofDelivered);
        Assert.AreEqual(2.5, FreePlayLastWolfStory.HoursAfterDialogue(message, FreePlayLastWolfStory.DialogueId), 1e-6,
            "Полчаса до хутора и два часа ожидания; выноса не было.");
    }

    [Test]
    public void FoodAndSilver_ArePaidOnce_AndRefusedShareGivesOnlyFood()
    {
        GameState state = NewState();
        Flags(state, FreePlayLastWolfStory.Flags.FeePaid);
        FreePlayLastWolfStory.Refresh(state);
        FreePlayLastWolfStory.Refresh(state);
        Assert.AreEqual(10 + FreePlayLastWolfStory.FoodReward, state.ArmySupply);
        Assert.AreEqual(FreePlayLastWolfStory.SilverShare, state.ArmyGold);

        GameState refused = NewState();
        Flags(refused, FreePlayLastWolfStory.Flags.FeeRefused, FreePlayLastWolfStory.Flags.FeeWaived);
        FreePlayLastWolfStory.Refresh(refused);
        Assert.AreEqual(10 + FreePlayLastWolfStory.FoodReward, refused.ArmySupply, "Еда выдаётся и при отказе от доли.");
        Assert.AreEqual(0, refused.ArmyGold, "Серебро остаётся Савве.");

        GameState withdrawn = NewState();
        Flags(withdrawn, FreePlayLastWolfStory.Flags.Seen, FreePlayLastWolfStory.Flags.DeadSeen, FreePlayLastWolfStory.Flags.Withdrawn,
            FreePlayLastWolfStory.Flags.Closed);
        FreePlayLastWolfStory.Refresh(withdrawn);
        Assert.AreEqual(10, withdrawn.ArmySupply, "За уход ничего не выдаётся.");
    }

    [Test]
    public void Message_LocalsBringSavvaByMorning()
    {
        GameState state = NewState();
        Flags(state, FreePlayLastWolfStory.Flags.Seen, FreePlayLastWolfStory.Flags.DeadSeen, FreePlayLastWolfStory.Flags.HelpSent,
            FreePlayLastWolfStory.Flags.DeathReported, FreePlayLastWolfStory.Flags.MessageEnding, FreePlayLastWolfStory.Flags.Closed);
        FreePlayLastWolfStory.Refresh(state);
        Assert.IsFalse(state.Narrative.HasFlag(FreePlayLastWolfStory.Flags.HunterSafe), "До утра Савва ещё в лесу.");
        Assert.IsNull(EntryNode(state), "Вернувшийся раньше не видит готового спасения.");

        state.Day += 2;
        FreePlayLastWolfStory.Refresh(state);
        Assert.IsTrue(state.Narrative.HasFlag(FreePlayLastWolfStory.Flags.HunterSafe));
        Assert.IsTrue(state.Narrative.HasFlag(FreePlayLastWolfStory.Flags.ProofDelivered));
        Assert.IsNull(Chronicle.Find(state, FreePlayLastWolfStory.ChronicleHunterSafe), "Не видели сами — записи нет.");
        Assert.AreEqual(FreePlayLastWolfStory.Nodes.HutAgain, EntryNode(state));
        Assert.AreEqual(0, state.ArmyGold, "Доли за вынос, которого не было, нет.");
    }

    [Test]
    public void Withdrawn_SavvaFoundSilently_NoEchoAndNoBlame()
    {
        GameState state = NewState();
        Flags(state, FreePlayLastWolfStory.Flags.Seen, FreePlayLastWolfStory.Flags.DeadSeen, FreePlayLastWolfStory.Flags.Withdrawn,
            FreePlayLastWolfStory.Flags.Closed);
        FreePlayLastWolfStory.Refresh(state);
        Assert.IsNotNull(Chronicle.Find(state, FreePlayLastWolfStory.ChronicleWithdrawn));
        state.Day += 2;
        List<string> reports = FreePlayLastWolfStory.Refresh(state);
        Assert.IsTrue(state.Narrative.HasFlag(FreePlayLastWolfStory.Flags.HunterSafe), "Вечерний поиск нашёл Савву.");
        CollectionAssert.IsEmpty(reports, "Отряд об этом не знает.");
        Assert.IsNull(Chronicle.Find(state, FreePlayLastWolfStory.ChronicleHunterSafe));
        Assert.IsNull(EntryNode(state), "Эха «после помощи» нет; волк и раненый не повторяются.");
    }

    [Test]
    public void News_ReachesSettlements_OnlyByToldOrCarriedChannel()
    {
        GameState told = NewState();
        Flags(told, FreePlayLastWolfStory.Flags.Seen, FreePlayLastWolfStory.Flags.DeadSeen);
        FreePlayLastWolfStory.Refresh(told);
        Assert.IsFalse(told.Narrative.HasFlag(FreePlayLastWolfStory.Flags.NewsLocal), "Видеть мёртвого волка — не значит, что знают все.");
        Flags(told, FreePlayLastWolfStory.Flags.RumorShared);
        FreePlayLastWolfStory.Refresh(told);
        Assert.IsTrue(told.Narrative.HasFlag(FreePlayLastWolfStory.Flags.NewsLocal), "Рассказ хозяйке избы.");

        GameState carried = NewState();
        Flags(carried, FreePlayLastWolfStory.Flags.Seen, FreePlayLastWolfStory.Flags.DeadSeen, FreePlayLastWolfStory.Flags.DeathReported,
            FreePlayLastWolfStory.Flags.HunterSafe, FreePlayLastWolfStory.Flags.ProofLeft);
        FreePlayLastWolfStory.Refresh(carried);
        Assert.IsFalse(carried.Narrative.HasFlag(FreePlayLastWolfStory.Flags.NewsLocal));
        carried.Day += FreePlayLastWolfStory.NewsDays;
        FreePlayLastWolfStory.Refresh(carried);
        Assert.IsTrue(carried.Narrative.HasFlag(FreePlayLastWolfStory.Flags.NewsLocal), "Сообщение углежогов дошло через торг.");
    }

    [Test]
    public void DirectionFromInn_RevealsForestOnMap()
    {
        GameState state = NewState();
        LocationData forest = state.FindLocation(FreePlayLastWolfStory.LocationId);
        Assume.That(forest, Is.Not.Null, "В этой партии генератор поставил глухой лес.");
        forest.IsVisibleOnMap = false;
        forest.IsDiscovered = false;
        Flags(state, FreePlayLastWolfStory.Flags.RumorHeard, FreePlayLastWolfStory.Flags.DirectionKnown);
        List<string> reports = FreePlayLastWolfStory.Refresh(state);
        Assert.IsTrue(forest.IsVisibleOnMap && forest.IsDiscovered);
        Assert.AreEqual(1, reports.Count);
    }

    [Test]
    public void Settlements_UseLocalNews_AndInnOffersOpenQuestEntries()
    {
        SettlementDatabaseAsset database = Resources.Load<SettlementDatabaseAsset>(SettlementDatabaseAsset.ResourcesPath);
        SettlementCatalogData catalog = database.ToCatalog();
        string all = string.Join("\n", catalog.Profiles.SelectMany(Conditions).Select(c => c.StringParam));
        StringAssert.DoesNotContain("freeplay.coal.leader_killed", all, "A-04: флага удалённой истории больше нет.");
        StringAssert.Contains(FreePlayLastWolfStory.Flags.NewsLocal, all);

        SettlementProfileDefinition village = catalog.Profiles.First(p => p.Id == "settlement.village.road");
        List<TavernOfferDefinition> offers = village.Tavern.Offers.Where(o => o.Id.StartsWith("offer.last_wolf.")).ToList();
        Assert.AreEqual(3, offers.Count);
        foreach (TavernOfferDefinition offer in offers)
        {
            DialogueDefinitionData dialogue = dialogues.FindDialogue(DialogueEntryRef.DialogueId(offer.DialogueId));
            Assert.IsNotNull(dialogue, offer.Id);
            Assert.IsTrue(dialogue.IsEntryNode(DialogueEntryRef.NodeId(offer.DialogueId)), offer.Id + " → " + offer.DialogueId);
        }

        SettlementCatalogValidator.Context context = new SettlementCatalogValidator.Context
        {
            DialogueExists = id =>
            {
                DialogueDefinitionData d = dialogues.FindDialogue(DialogueEntryRef.DialogueId(id));
                string node = DialogueEntryRef.NodeId(id);
                return d != null && (node.Length == 0 || d.IsEntryNode(node));
            },
            SpeakerExists = id => dialogues.FindSpeaker(id) != null
        };
        List<string> problems = SettlementCatalogValidator.ValidateProfile(village, context);
        CollectionAssert.IsEmpty(problems.Where(p => p.Contains("диалог")), string.Join("\n", problems));
    }

    private static IEnumerable<NarrativeCondition> Conditions(SettlementProfileDefinition profile)
    {
        IEnumerable<NarrativeConditionGroup> groups = profile.Variants.Select(v => v.Conditions)
            .Concat(profile.Board != null ? profile.Board.Notices.SelectMany(n => new[] { n.ShowConditions, n.HideConditions }) : Enumerable.Empty<NarrativeConditionGroup>())
            .Concat(profile.Tavern != null ? profile.Tavern.Offers.Select(o => o.Conditions) : Enumerable.Empty<NarrativeConditionGroup>());
        return groups.Where(g => g?.Conditions != null).SelectMany(g => g.Conditions);
    }
}

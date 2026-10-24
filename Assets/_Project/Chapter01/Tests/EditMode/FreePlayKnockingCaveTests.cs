using System.Collections.Generic;
using System.Linq;
using KingdomSurvival.BattlefieldDatabase;
using KingdomSurvival.Chapter01;
using KingdomSurvival.DialogueDatabase;
using KingdomSurvival.FreePlay;
using KingdomSurvival.UnitDatabase;
using NUnit.Framework;
using UnityEngine;

// Квест-локация «Стук за завалом» [РАБОЧЕЕ] — первая история, собранная по
// гайду Мастерской квестов. Место, сцены и волки — данные Баз (перенесены
// из мастерской), Хроника и «Дело» — FreePlayCaveStory. Проверяется:
// место проходит проверку и есть только в свободной игре; путь хитростью
// без боя; бой → флаг победы → дальний зал; однократность припасов и
// наград; знак узнаётся только со знанием клейма шахты; отход не
// исчерпывает столкновение; состояние переживает сохранение.
public sealed class FreePlayKnockingCaveTests
{
    private const string EncounterId = "cave.den";
    private DialogueDatabaseAsset dialogues;

    [SetUp]
    public void SetUp()
    {
        Chapter01Content.Register();
        FreePlayContent.Register();
        LocalLocationDatabaseAsset.ApplyToCatalog();
        dialogues = Resources.Load<DialogueDatabaseAsset>(DialogueDatabaseAsset.ResourcesPath);
    }

    [TearDown]
    public void TearDown()
    {
        LocalLocationCatalog.SetOverrides(null);
        CampaignSession.Reset();
    }

    private static List<WorldMapLocationTemplateData> Templates()
    {
        List<WorldMapLocationTemplateData> templates = WorldMapLocationDefaults.Create().ToList();
        templates.Add(new WorldMapLocationTemplateData
        {
            Id = FreePlayCaveStory.LocationId,
            Name = "Пещера за завалом",
            Threat = "средняя",
            InitiallyVisibleOnMap = true,
            ModeId = CampaignStartOptions.FreePlayId
        });
        return templates;
    }

    private static GameState AtCave(int seed, params string[] fighters)
    {
        GameState state = new CampaignSetup { CrisisId = CampaignStartOptions.FreePlayId, WorldSeed = seed }.CreateCampaign(Templates());
        if (state.Narrative == null)
            state.Narrative = new NarrativeStateData();
        state.ArmySupply = 30;
        Assert.IsTrue(state.TryStartExpedition(FreePlayCaveStory.LocationId, fighters.ToList(), out string message), message);
        state.ActiveExpedition.Phase = CommanderState.AtLocation;
        LocalLocationDefinition cave = Cave(state);
        Assert.IsTrue(LocalExplorationService.Enter(state, cave, null, out string reason), reason);
        return state;
    }

    private static LocalLocationDefinition Cave(GameState state) => LocalLocationCatalog.ForWorldLocation(state, FreePlayCaveStory.LocationId);

    private NarrativeDialogueView Play(GameState state, string dialogueId, params string[] choiceIds)
    {
        NarrativeDialogueRuntimeSession session = new NarrativeDialogueRuntimeSession();
        Assert.IsTrue(session.Start(dialogues, dialogueId, state.GetSelectedCommander().HeroProfile ?? new HeroProfileData(),
            state.Narrative, out NarrativeDialogueView view, out string error,
            Chapter01ContextBuilder.GetPresentCompanionIds(state), new List<string>(),
            state.WorldSeed, Chapter01ContextBuilder.GetPartySize(state), state), error);
        foreach (string choiceId in choiceIds)
        {
            view = Skip(session, view);
            Assert.IsNotNull(view, "Разговор кончился раньше ответа " + choiceId);
            Assert.IsTrue(view.AvailableChoices.Any(c => c.ChoiceId == choiceId),
                "Нет ответа " + choiceId + ": " + string.Join(", ", view.AvailableChoices.Select(c => c.ChoiceId)));
            NarrativeDialogueSelectionResult result = session.SelectChoice(choiceId);
            view = result.DialogueEnded ? null : result.View;
        }
        return view == null ? null : Skip(session, view);
    }

    private static NarrativeDialogueView Skip(NarrativeDialogueRuntimeSession session, NarrativeDialogueView view)
    {
        for (int i = 0; i < 20 && view != null && view.AvailableChoices.Count == 1 &&
                        view.AvailableChoices[0].Kind == DialogueChoiceKind.Continue; i++)
        {
            NarrativeDialogueSelectionResult result = session.SelectChoice(view.AvailableChoices[0].ChoiceId);
            view = result.DialogueEnded ? null : result.View;
        }
        return view;
    }

    private static void Win(GameState state, CampaignBattleOutcome outcome)
    {
        LocalLocationDefinition cave = Cave(state);
        CampaignBattleRequest request = LocalExplorationService.BuildEncounterRequest(state, cave, cave.FindEncounter(EncounterId), null, null, null);
        StringAssert.StartsWith("freeplay.battle.cave_den.", request.BattleId);
        string hero = state.GetSelectedCommander().Id;
        CampaignBattleResult result = new CampaignBattleResult
        {
            BattleId = request.BattleId,
            Outcome = outcome,
            SourceKind = CampaignBattleSourceKind.Local,
            LocalLocationId = cave.Id,
            EncounterId = EncounterId,
            Survivors = new List<CampaignBattleSurvivor> { new CampaignBattleSurvivor { PersonId = hero, HitPoints = 5 } },
            Enemies = new List<CampaignBattleEnemyRecord>
            {
                new CampaignBattleEnemyRecord { UnitTypeId = "wolf", InstanceId = "cave.wolf.1", Defeated = outcome == CampaignBattleOutcome.Victory, HitPoints = 2 },
                new CampaignBattleEnemyRecord { UnitTypeId = "wolf", InstanceId = "cave.wolf.2", Defeated = outcome == CampaignBattleOutcome.Victory, HitPoints = 2 }
            }
        };
        CampaignBattleBridge.ApplyResult(state, result, new List<string>());
        CampaignBattleBridge.ApplyResult(state, result, new List<string>());
    }

    // Как в игре: действие объекта отмечается до показа сцены.
    private NarrativeDialogueView Use(GameState state, string objectId, params string[] choiceIds)
    {
        LocalObjectDefinition item = Cave(state).FindObject(objectId);
        Assert.IsTrue(LocalExplorationService.MarkInteraction(state, Cave(state), item), objectId + " недоступен.");
        return Play(state, item.DialogueId, choiceIds);
    }

    private static bool Available(GameState state, string objectId) =>
        LocalExplorationService.IsObjectAvailable(state, Cave(state), Cave(state).FindObject(objectId));

    [Test]
    public void CavePlace_PassesValidation_AndExistsOnlyInFreePlay()
    {
        LocalLocationDatabaseAsset asset = Resources.Load<LocalLocationDatabaseAsset>(LocalLocationDatabaseAsset.ResourcesPath);
        LocalLocationDefinition cave = asset.locations.Single(location => location.Id == FreePlayCaveStory.LocalLocationId);
        UnitDatabaseAsset units = Resources.Load<UnitDatabaseAsset>(UnitDatabaseAsset.ResourcesPath);
        List<string> errors = LocalLocationValidator.Validate(cave, Resources.Load<BattlefieldDatabaseAsset>(BattlefieldDatabaseAsset.ResourcesPath),
            id => dialogues.FindDialogue(id) != null, id => units.FindById(id) != null);
        Assert.IsEmpty(errors, string.Join("\n", errors));
        Assert.AreEqual(CampaignStartOptions.FreePlayId, cave.ModeId);
        Assert.Greater(cave.FindObject("cave.chamber").Point.X, cave.FindEnemy("cave.wolf.1").Point.X, "Дальний зал — за волками.");
        Assert.Greater(cave.FindEnemy("cave.wolf.1").Point.X, cave.FindEncounter(EncounterId).TriggerArea.X, "Волки — за зоной угрозы.");

        GameState chapter = new CampaignSetup { CrisisId = CampaignStartOptions.HomeOnForeignWaterCrisisId, WorldSeed = 7 }.CreateCampaign(Templates());
        Assert.IsNull(chapter.FindLocation(FreePlayCaveStory.LocationId), "В мире кризиса пещеры нет.");

        foreach (string id in new[] { "freeplay_cave_wall", "freeplay_cave_crack", "freeplay_cave_den", "freeplay_cave_cutter", "freeplay_cave_old_wall" })
        {
            List<string> issues = new List<string>();
            dialogues.CollectValidationIssuesForDialogue(id, issues);
            Assert.IsEmpty(issues, id + ": " + string.Join("\n", issues));
        }
    }

    [Test]
    public void Ruse_FreesTheCutterWithoutABattle_Once()
    {
        for (int seed = 1; seed <= 80; seed++)
        {
            GameState state = AtCave(seed, "garrick", "agnessa");
            Assert.IsNull(Use(state, "cave.wall", "cave.wall.leave"));
            Assert.IsTrue(state.Narrative.HasFlag(FreePlayCaveStory.Flags.Entered));
            Assert.IsFalse(Available(state, "cave.wall"), "Кладку осматривают один раз.");
            Assert.AreEqual("Найти того, кто стучит за завалом.", FreePlayCaveStory.BuildGoal(state).CurrentStep);

            int supply = state.ArmySupply;
            Play(state, "freeplay_cave_crack", "cave.crack.knock", "cave.crack.ruse");
            Assert.IsTrue(state.Narrative.HasFlag(FreePlayCaveStory.Flags.CutterKnown));
            if (!state.Narrative.HasFlag(FreePlayCaveStory.Flags.FreedByRuse))
            {
                Assert.IsTrue(state.Narrative.HasFlag("freeplay.cave.cutter_promised"), "После неудачи Нежата просит выгнать волков.");
                NarrativeDialogueView again = Play(state, "freeplay_cave_crack");
                Assert.IsFalse(again.AvailableChoices.Any(c => c.ChoiceId == "cave.crack.knock"), "Второй раз не знакомятся.");
                Assert.IsNull(Play(state, "freeplay_cave_crack", "cave.crack.wait", "cave.crack.waiting.end"));
                continue;
            }

            Assert.AreEqual(supply - 2, state.ArmySupply, "Приманка — 2 припаса.");
            Assert.IsTrue(state.Narrative.HasFlag(FreePlayCaveStory.Flags.CutterGone));
            Assert.IsFalse(Available(state, "cave.crack"), "Трещина больше не зовёт: Нежата ушёл.");
            Assert.IsFalse(Available(state, "cave.chamber"), "Дальний зал — только после волков.");
            FreePlayContent.RefreshWithReports(state);
            Assert.IsNotNull(Chronicle.Find(state, FreePlayCaveStory.ChronicleFreed));
            StringAssert.Contains("Волки остались", FreePlayCaveStory.BuildGoal(state).CurrentStep);
            Assert.IsTrue(LocalExplorationService.IsEncounterActive(state, Cave(state), Cave(state).FindEncounter(EncounterId)),
                "Волки остались в среднем зале.");

            // Позже — бой; в пустом дальнем зале встречи и раздачи припасов нет.
            Win(state, CampaignBattleOutcome.Victory);
            NarrativeDialogueView chamber = Play(state, "freeplay_cave_cutter");
            Assert.IsFalse(chamber.AvailableChoices.Any(c => c.ChoiceId == "cave.cutter.share"));
            Assert.IsTrue(chamber.AvailableChoices.Any(c => c.ChoiceId == "cave.cutter.leave"));
            return;
        }
        Assert.Fail("Ни при одном seed хитрость не удалась.");
    }

    [Test]
    public void Battle_OpensTheFarChamber_ShareOnce_MarkNeedsTheMine()
    {
        GameState state = AtCave(20261024, "garrick");
        Use(state, "cave.wall", "cave.wall.leave");
        Play(state, "freeplay_cave_crack", "cave.crack.knock", "cave.crack.promise");
        Assert.IsFalse(Available(state, "cave.chamber"));

        Win(state, CampaignBattleOutcome.Retreat);
        Assert.IsFalse(state.Narrative.HasFlag(FreePlayCaveStory.Flags.DenCleared), "Отход не исчерпывает столкновение.");
        Assert.IsFalse(Available(state, "cave.chamber"));

        Win(state, CampaignBattleOutcome.Victory);
        Assert.IsTrue(state.Narrative.HasFlag(FreePlayCaveStory.Flags.DenCleared), "Победа ставит флаг столкновения.");
        Assert.IsTrue(Available(state, "cave.chamber"));
        Assert.IsTrue(Available(state, "cave.old_wall"));
        Assert.IsFalse(Available(state, "cave.tracks"), "Следы волков больше не показываются.");

        int supply = state.ArmySupply;
        Assert.IsNull(Play(state, "freeplay_cave_cutter", "cave.cutter.share", "cave.cutter.shared.end"));
        Assert.AreEqual(supply - 3, state.ArmySupply);
        Assert.IsTrue(state.Narrative.HasKnowledge(FreePlayCaveStory.OldMarkKnowledge), "Знак на кладке записан сведением.");
        NarrativeDialogueView again = Play(state, "freeplay_cave_cutter");
        Assert.IsFalse(again.AvailableChoices.Any(c => c.ChoiceId == "cave.cutter.share"), "Припасы отдают один раз.");
        Assert.AreEqual(supply - 3, state.ArmySupply);

        FreePlayContent.RefreshWithReports(state);
        Assert.IsNotNull(Chronicle.Find(state, FreePlayCaveStory.ChronicleMet));
        Assert.IsNotNull(Chronicle.Find(state, FreePlayCaveStory.ChronicleDen));

        // Без клейма шахты знак не узнать: ответ виден, но отключён с подсказкой.
        NarrativeDialogueView wall = Play(state, "freeplay_cave_old_wall");
        Assert.IsFalse(wall.AvailableChoices.Any(c => c.ChoiceId == "cave.old_wall.compare"));
        Assert.IsTrue(wall.DisabledChoices.Any(c => c.ChoiceId == "cave.old_wall.compare" && c.IsDisabledWithHint));

        // Возвращение: клеймо прочитано в шахте — знак узнаётся.
        state.Narrative.SetFlag(FreePlayMineStory.Flags.Mark);
        Assert.IsNull(Play(state, "freeplay_cave_old_wall", "cave.old_wall.compare", "cave.old_wall.match.end"));
        Assert.IsTrue(state.Narrative.HasFlag(FreePlayCaveStory.Flags.MarkMatched));
        Assert.IsFalse(Available(state, "cave.old_wall"));
        Assert.IsTrue(Available(state, "cave.old_wall_known"));
        FreePlayContent.RefreshWithReports(state);
        Assert.IsNotNull(Chronicle.Find(state, FreePlayCaveStory.ChronicleMark));
        Assert.AreEqual(JournalGoalState.Completed, FreePlayCaveStory.BuildGoal(state).State);
        Assert.IsTrue(FreePlayContent.BuildGoals(state).Any(goal => goal.Id == "freeplay.goal.cave"));
    }

    [Test]
    public void CaveState_SurvivesSaveAndLoad()
    {
        GameState state = AtCave(20261024, "garrick");
        Use(state, "cave.wall", "cave.wall.leave");
        Win(state, CampaignBattleOutcome.Victory);

        CampaignSaveData data = JsonUtility.FromJson<CampaignSaveData>(JsonUtility.ToJson(CampaignSaveService.ExportCampaign(state)));
        GameState loaded = CampaignSaveService.RestoreCampaign(data);
        LocalExplorationService.NormalizeAfterLoad(loaded);
        Assert.IsTrue(loaded.Narrative.HasFlag(FreePlayCaveStory.Flags.DenCleared));
        Assert.IsFalse(LocalExplorationService.IsObjectAvailable(loaded, Cave(loaded), Cave(loaded).FindObject("cave.wall")), "Однократный объект остаётся отмеченным.");
        Assert.IsEmpty(LocalExplorationService.AliveEnemies(loaded, Cave(loaded)), "Волки не возрождаются.");
    }
}

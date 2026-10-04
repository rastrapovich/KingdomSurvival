using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

// ПР-12К (канон v1.53 §28.3, §28.9): ядро исследуемых мест и присутствия.
// Бой на месте собирается только из присутствующих боеспособных, его итог
// меняет место ровно один раз вместе с кампанией, отход ведёт к безопасной
// точке, а состояние места переживает настоящий JsonUtility roundtrip.
public sealed class LocalExplorationCoreTests
{
    private const string TestLocationId = "test.local.cave";
    private const string TestWorldLocationId = "test.world.cave";
    private const string EncounterId = "test.cave.lair";

    [SetUp]
    public void SetUp()
    {
        CampaignSession.Reset();
        LocalLocationCatalog.RegisterDefault(CreateDefinition());
    }

    [TearDown]
    public void TearDown()
    {
        CampaignSession.Reset();
    }

    private static LocalLocationDefinition CreateDefinition()
    {
        LocalLocationDefinition cave = new LocalLocationDefinition
        {
            Id = TestLocationId,
            WorldLocationId = TestWorldLocationId,
            DisplayName = "Пещера",
            BattlefieldId = "test_cave"
        };
        cave.Entrances.Add(new LocalEntranceDefinition { Id = "in", Cell = new LocalCellData(0, 3) });
        cave.Objects.Add(new LocalObjectDefinition { Id = "pile", Kind = LocalObjectKind.Inspect, Text = "Куча.", Cell = new LocalCellData(2, 0) });
        cave.Enemies.Add(new LocalEnemyDefinition { InstanceId = "cave.beast.1", UnitTypeId = "forest_beast", Cell = new LocalCellData(8, 2), EncounterId = EncounterId });
        cave.Enemies.Add(new LocalEnemyDefinition { InstanceId = "cave.beast.2", UnitTypeId = "forest_beast", Cell = new LocalCellData(8, 4), EncounterId = EncounterId });
        LocalEncounterDefinition lair = new LocalEncounterDefinition
        {
            Id = EncounterId,
            BattleIdPrefix = "test.battle.cave.",
            AllowRetreat = true,
            RetreatCell = new LocalCellData(1, 3)
        };
        lair.TriggerCells.Add(new LocalCellData(6, 3));
        cave.Encounters.Add(lair);
        return cave;
    }

    private static GameState NewStateAtCave(int fighters)
    {
        GameState state = new GameState();
        state.CreateNewGame(20261004);
        state.ArmySupply = 100;
        LocationData target = state.Locations.First(location => !location.IsWaypoint);
        List<string> selected = state.Fighters.Take(fighters).Select(f => f.Id).ToList();
        Assert.IsTrue(state.TryStartExpedition(target.Id, selected, out string message), message);
        state.ActiveExpedition.Phase = CommanderState.AtLocation;
        state.ActiveExpedition.LocationId = TestWorldLocationId;
        return state;
    }

    private static Dictionary<string, LocalCellData> CellsFor(GameState state)
    {
        Dictionary<string, LocalCellData> cells = new Dictionary<string, LocalCellData>();
        int q = 1;
        foreach (string id in PartyPresence.BattleCandidateIds(state))
            cells[id] = new LocalCellData(q++, 3);
        return cells;
    }

    [Test]
    public void Enter_RequiresStandingAtTheEntrance()
    {
        GameState state = NewStateAtCave(1);
        LocalLocationDefinition cave = LocalLocationCatalog.Find(TestLocationId);
        state.ActiveExpedition.LocationId = "elsewhere";
        Assert.IsFalse(LocalExplorationService.Enter(state, cave, "in", out _));

        state.ActiveExpedition.LocationId = TestWorldLocationId;
        Assert.IsTrue(LocalExplorationService.Enter(state, cave, "in", out string reason), reason);
        Assert.IsTrue(LocalExplorationService.IsActive(state));
        Assert.AreEqual(PartyPresence.PresentIds(state).Count, state.LocalExploration.Party.Count,
            "Внутри видны все присутствующие участники похода.");
        Assert.AreEqual(2, LocalExplorationService.AliveEnemies(state, cave).Count);
    }

    [Test]
    public void EncounterRequest_TakesOnlyPresentCombatants_WithCellsAndEnemyInstances()
    {
        GameState state = NewStateAtCave(2);
        string wounded = state.ActiveExpedition.FighterIds[1];
        HomePeopleService.Find(state, wounded).Injury = ResidentInjury.Recovering;
        state.ActiveExpedition.RetinueIds.Add(HomePeopleService.OstafiyId);
        LocalLocationDefinition cave = LocalLocationCatalog.Find(TestLocationId);
        Assert.IsTrue(LocalExplorationService.Enter(state, cave, "in", out _));

        CampaignBattleRequest request = LocalExplorationService.BuildEncounterRequest(
            state, cave, cave.FindEncounter(EncounterId), CellsFor(state));

        Assert.IsTrue(request.IsLocal);
        Assert.AreEqual(TestLocationId, request.LocalLocationId);
        Assert.AreEqual("test_cave", request.BattlefieldId);
        Assert.AreEqual("test.battle.cave.1", request.BattleId);
        List<string> ids = request.Participants.Select(p => p.PersonId).ToList();
        CollectionAssert.DoesNotContain(ids, wounded, "Тяжелораненый не сражается.");
        CollectionAssert.DoesNotContain(ids, HomePeopleService.OstafiyId, "Свита в бой не вступает.");
        Assert.AreEqual(2, ids.Count, "Герой и один боеспособный боец.");
        Assert.IsTrue(request.Participants.All(p => p.HasCell));
        CollectionAssert.AreEquivalent(new[] { "cave.beast.1", "cave.beast.2" }, request.Enemies.Select(e => e.InstanceId));
        Assert.IsTrue(request.Enemies.All(e => e.HasCell && e.Count == 1));
        CollectionAssert.Contains(request.BlockedCells.Select(c => c.Q * 100 + c.R).ToList(), 200, "Объект места непроходим и в бою.");
    }

    [Test]
    public void LocalVictory_ChangesThePlaceOnce_AndDefeatedDoNotReturn()
    {
        GameState state = NewStateAtCave(1);
        LocalLocationDefinition cave = LocalLocationCatalog.Find(TestLocationId);
        Assert.IsTrue(LocalExplorationService.Enter(state, cave, "in", out _));
        CampaignBattleRequest request = LocalExplorationService.BuildEncounterRequest(
            state, cave, cave.FindEncounter(EncounterId), CellsFor(state));
        string hero = state.GetSelectedCommander().Id;

        CampaignBattleResult result = new CampaignBattleResult
        {
            BattleId = request.BattleId,
            Outcome = CampaignBattleOutcome.Victory,
            SourceKind = CampaignBattleSourceKind.Local,
            LocalLocationId = TestLocationId,
            EncounterId = EncounterId,
            Survivors = new List<CampaignBattleSurvivor> { new CampaignBattleSurvivor { PersonId = hero, HitPoints = 5, HasCell = true, CellQ = 7, CellR = 3 } },
            Enemies = new List<CampaignBattleEnemyRecord>
            {
                new CampaignBattleEnemyRecord { UnitTypeId = "forest_beast", InstanceId = "cave.beast.1", Defeated = true },
                new CampaignBattleEnemyRecord { UnitTypeId = "forest_beast", InstanceId = "cave.beast.2", Defeated = true }
            }
        };

        Assert.AreEqual(CampaignBattleApplyStatus.SquadSurvived, CampaignBattleBridge.ApplyResult(state, result, new List<string>()));
        int experience = state.Progression.People.Sum(p => p.Experience);
        Assert.AreEqual(CampaignBattleApplyStatus.AlreadyApplied, CampaignBattleBridge.ApplyResult(state, result, new List<string>()));
        Assert.AreEqual(experience, state.Progression.People.Sum(p => p.Experience), "Повтор не начисляет опыт.");

        Assert.IsEmpty(LocalExplorationService.AliveEnemies(state, cave), "Побеждённые не появляются снова.");
        Assert.IsFalse(LocalExplorationService.IsEncounterActive(state, cave, cave.FindEncounter(EncounterId)));
        Assert.IsNull(LocalExplorationService.EncounterAt(state, cave, 6, 3), "Зона угрозы больше не начинает бой.");
        LocalActorStateData heroActor = state.LocalExploration.Party.First(a => a.ActorId == hero);
        Assert.AreEqual(7, heroActor.Q, "Выживший продолжает с того места, где закончил бой.");

        LocalExplorationService.Exit(state);
        Assert.IsTrue(LocalExplorationService.Enter(state, cave, "in", out _));
        Assert.IsEmpty(LocalExplorationService.AliveEnemies(state, cave), "Повторный вход не возрождает зверей.");
    }

    [Test]
    public void LocalRetreat_GoesToSafePoint_BeastsKeepWoundsInTheirDen()
    {
        GameState state = NewStateAtCave(1);
        LocalLocationDefinition cave = LocalLocationCatalog.Find(TestLocationId);
        Assert.IsTrue(LocalExplorationService.Enter(state, cave, "in", out _));
        CampaignBattleRequest request = LocalExplorationService.BuildEncounterRequest(
            state, cave, cave.FindEncounter(EncounterId), CellsFor(state));
        string hero = state.GetSelectedCommander().Id;
        int supply = state.ArmySupply;

        CampaignBattleResult result = new CampaignBattleResult
        {
            BattleId = request.BattleId,
            Outcome = CampaignBattleOutcome.Retreat,
            SourceKind = CampaignBattleSourceKind.Local,
            LocalLocationId = TestLocationId,
            EncounterId = EncounterId,
            Survivors = new List<CampaignBattleSurvivor> { new CampaignBattleSurvivor { PersonId = hero, HitPoints = 6, HasCell = true, CellQ = 6, CellR = 3 } },
            Enemies = new List<CampaignBattleEnemyRecord>
            {
                new CampaignBattleEnemyRecord { InstanceId = "cave.beast.1", Defeated = true },
                new CampaignBattleEnemyRecord { InstanceId = "cave.beast.2", HitPoints = 3, HasCell = true, CellQ = 6, CellR = 4 }
            }
        };
        CampaignBattleBridge.ApplyResult(state, result, new List<string>());
        CampaignBattleBridge.ApplyResult(state, result, new List<string>());

        Assert.AreEqual(supply - CampaignBattleBridge.RetreatSupplyLoss, state.ArmySupply, "Цена отхода — один раз.");
        LocalActorStateData heroActor = state.LocalExploration.Party.First(a => a.ActorId == hero);
        Assert.AreEqual(1, heroActor.Q, "Отряд у безопасной точки.");
        Assert.AreEqual(3, heroActor.R);
        Assert.IsNull(LocalExplorationService.EncounterAt(state, cave, heroActor.Q, heroActor.R), "Бой не начинается в следующем кадре.");

        List<LocalActorStateData> alive = LocalExplorationService.AliveEnemies(state, cave);
        Assert.AreEqual(1, alive.Count, "Убитый зверь не воскресает.");
        Assert.AreEqual(3, alive[0].HitPoints, "Раны остаются.");
        Assert.AreEqual(8, alive[0].Q, "Выживший зверь вернулся в логово.");
        Assert.IsTrue(LocalExplorationService.IsEncounterActive(state, cave, cave.FindEncounter(EncounterId)),
            "Незавершённая встреча допускает новый бой при осмысленном входе.");
        Assert.AreEqual("test.battle.cave.2", LocalExplorationService.NextBattleId(state, cave.FindEncounter(EncounterId)));
    }

    [Test]
    public void SaveRoundTrip_RestoresLayerPositionsAndPlace_WithoutFakeCamp()
    {
        GameState state = NewStateAtCave(1);
        LocalLocationDefinition cave = LocalLocationCatalog.Find(TestLocationId);
        Assert.IsTrue(LocalExplorationService.Enter(state, cave, "in", out _));
        string hero = state.GetSelectedCommander().Id;
        LocalExplorationService.StorePartyPosition(state, hero, 4, 2, 3);
        LocalExplorationService.MarkInteraction(state, cave, cave.FindObject("pile"));

        CampaignSaveData data = CampaignSaveService.ExportCampaign(state);
        Assert.IsFalse(data.HasSettlementCamp);
        CampaignSaveData loaded = JsonUtility.FromJson<CampaignSaveData>(JsonUtility.ToJson(data));
        GameState restored = CampaignSaveService.RestoreCampaign(loaded);
        LocalExplorationService.NormalizeAfterLoad(restored);

        Assert.IsNull(restored.SettlementCamp, "Null-маркер не создаёт фиктивную стоянку.");
        Assert.IsTrue(LocalExplorationService.IsActive(restored));
        Assert.AreEqual(TestLocationId, restored.LocalExploration.ActiveLocalLocationId);
        LocalActorStateData heroActor = restored.LocalExploration.Party.First(a => a.ActorId == hero);
        Assert.AreEqual(4, heroActor.Q);
        Assert.AreEqual(2, heroActor.R);
        Assert.AreEqual(3, heroActor.Facing);
        Assert.IsTrue(LocalExplorationService.IsObjectDone(restored, cave, cave.FindObject("pile")), "Однократное действие не повторяется после загрузки.");
        Assert.AreEqual(2, LocalExplorationService.AliveEnemies(restored, cave).Count);
    }

    [Test]
    public void OldSaveWithoutLocalFields_LoadsOnTheWorldMap()
    {
        GameState state = NewStateAtCave(1);
        string json = JsonUtility.ToJson(CampaignSaveService.ExportCampaign(state));
        // Сохранение формата 2 до ПР-12К: новых полей в файле нет.
        json = json.Replace("\"LocalExploration\":", "\"LocalExplorationOld\":")
                   .Replace("\"SettlementCamp\":", "\"SettlementCampOld\":")
                   .Replace("\"HasSettlementCamp\":false,", string.Empty);
        CampaignSaveData loaded = JsonUtility.FromJson<CampaignSaveData>(json);
        Assert.IsTrue(CampaignSaveService.IsLoadable(loaded, string.Empty, 0, out string reason), reason);
        GameState restored = CampaignSaveService.RestoreCampaign(loaded);
        LocalExplorationService.NormalizeAfterLoad(restored);

        Assert.IsFalse(LocalExplorationService.IsActive(restored), "Без локальных данных — прежний слой.");
        Assert.IsNull(restored.SettlementCamp);
        CollectionAssert.AreEquivalent(PartyPresence.ExpeditionIds(restored).Where(id => HomePeopleService.Find(restored, id) == null || HomePeopleService.Find(restored, id).IsAlive),
            PartyPresence.PresentIds(restored), "Без разделения все участники похода вместе.");
    }

    [Test]
    public void MissingPlace_AfterLoad_ReturnsToTheEntranceWithNotice()
    {
        GameState state = NewStateAtCave(1);
        LocalLocationDefinition cave = LocalLocationCatalog.Find(TestLocationId);
        Assert.IsTrue(LocalExplorationService.Enter(state, cave, "in", out _));
        state.LocalExploration.ActiveLocalLocationId = "local.removed";

        LocalExplorationService.NormalizeAfterLoad(state);

        Assert.IsFalse(LocalExplorationService.IsActive(state));
        Assert.IsTrue(state.HasActiveExpedition, "Кампания цела.");
        Assert.IsNotEmpty(state.LocalExploration.PendingNotice);
    }
}

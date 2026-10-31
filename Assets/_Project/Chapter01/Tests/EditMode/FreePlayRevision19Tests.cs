using System.Collections.Generic;
using System.Linq;
using KingdomSurvival.Chapter01;
using KingdomSurvival.FreePlay;
using NUnit.Framework;
using UnityEngine;

// Бриф правок (19 пунктов): старт свободной игры — 20 припасов и пустая свита
// (пп. 5, 17); без Дома сутки не пишут домашних донесений и не трогают его
// запасы (п. 14); характеристики собираются из текущих источников — подсказка
// показателя видит только свои вклады, снятая вещь ничего не оставляет (п. 7).
public sealed class FreePlayRevision19Tests
{
    private sealed class FixedStats : IUnitStatsProvider
    {
        public bool TryGetCombatStats(string unitTypeId, out UnitCombatStats stats)
        {
            stats = new UnitCombatStats { MaxHitPoints = 14, Attack = 3, Defense = 2, Damage = 3, Movement = 4, Initiative = 2, AttackRange = 1 };
            return !string.IsNullOrEmpty(unitTypeId);
        }
    }

    private IUnitStatsProvider previousProvider;

    [SetUp]
    public void SetUp()
    {
        Chapter01Content.Register();
        FreePlayContent.Register();
        previousProvider = GameState.UnitStatsProvider;
        GameState.UnitStatsProvider = new FixedStats();
    }

    [TearDown]
    public void TearDown()
    {
        GameState.UnitStatsProvider = previousProvider;
    }

    private static GameState NewFreePlay() =>
        new CampaignSetup { CrisisId = CampaignStartOptions.FreePlayId, WorldSeed = 20261031 }.CreateCampaign();

    [Test]
    public void NewFreePlay_StartsWithTwentySupplies_AndEmptyRetinue()
    {
        GameState state = NewFreePlay();
        Assert.AreEqual(FreePlayContent.StartingSupply, state.ArmySupply);
        Assert.AreEqual(20, state.ArmySupply);
        Assert.IsEmpty(ExpeditionPreparation.GetRetinueId(state) ?? string.Empty, "Свита на старте пуста.");

        GameState loaded = CampaignSaveService.RestoreCampaign(JsonUtility.FromJson<CampaignSaveData>(
            JsonUtility.ToJson(CampaignSaveService.ExportCampaign(state))));
        loaded.ArmySupply = 7;
        GameState again = CampaignSaveService.RestoreCampaign(JsonUtility.FromJson<CampaignSaveData>(
            JsonUtility.ToJson(CampaignSaveService.ExportCampaign(loaded))));
        Assert.AreEqual(7, again.ArmySupply, "Загрузка не возвращает стартовые 20.");
    }

    [Test]
    public void FreePlay_DaysPass_WithoutHomeReportsOrHomeStocksChange()
    {
        GameState state = NewFreePlay();
        int food = state.Food;
        int gold = state.Gold;
        List<string> messages = new List<string>();
        for (int day = 0; day < 3; day++)
        {
            ContinuousSimulationBatch batch = ContinuousSimulationSystem.AdvanceLocalHours(state, 24.0);
            messages.AddRange(batch.Result.Messages);
        }
        Assert.AreEqual(food, state.Food, "Запасы Дома не идут.");
        Assert.AreEqual(gold, state.Gold, "Казна Дома не идёт.");
        Assert.IsFalse(messages.Any(m => m.Contains("Дом") || m.Contains("Лад") || m.Contains("угл")),
            "Домашних донесений нет: " + string.Join(" | ", messages));
        Assert.IsEmpty(FreePlaySummary.Refresh(state), "«Итог Дома» не подводится.");
    }

    [Test]
    public void Stats_EquipSwapUnequip_NoAccumulation_AndSourcesFilteredPerStat()
    {
        GameState state = NewFreePlay();
        string hero = state.GetSelectedCommander().Id;
        ItemInstanceData worn0 = ItemService.Equipped(state, hero, ItemSlot.Protection);
        if (worn0 != null)
            Assert.IsTrue(ItemService.TryUnequip(state, worn0.InstanceId, out string off0), off0);
        AssembledCombatStats before = CombatStatsAssembler.Compute(state, hero);
        ItemInstanceData vest = ItemService.CreateInstance(state, ItemCatalog.ScaleVest, hero);

        for (int i = 0; i < 3; i++)
        {
            Assert.IsTrue(ItemService.TryEquip(state, vest.InstanceId, hero, out string message), message);
            AssembledCombatStats worn = CombatStatsAssembler.Compute(state, hero);
            StatModifier modifier = ItemCatalog.Find(ItemCatalog.ScaleVest).Modifier;
            Assert.AreEqual(before.Final.Defense + modifier.Defense, worn.Final.Defense, "Надетая вещь — ровно её вклад.");
            Assert.IsTrue(worn.For(CombatStat.Defense).Any(c => c.FromItem && c.Source == ItemCatalog.Find(ItemCatalog.ScaleVest).Name));
            Assert.IsFalse(worn.For(CombatStat.Attack).Any(c => c.Source == ItemCatalog.Find(ItemCatalog.ScaleVest).Name) && modifier.Attack == 0,
                "Вещь не попадает в подсказку чужого показателя.");

            Assert.IsTrue(ItemService.TryUnequip(state, vest.InstanceId, out message), message);
            AssembledCombatStats off = CombatStatsAssembler.Compute(state, hero);
            Assert.AreEqual(before.Final.Defense, off.Final.Defense, "После снятия — без накопленных добавок.");
            Assert.AreEqual(before.Final.MaxHitPoints, off.Final.MaxHitPoints);
            Assert.IsFalse(off.Contributions.Any(c => c.Source == ItemCatalog.Find(ItemCatalog.ScaleVest).Name), "Снятая вещь ничего не оставляет.");
        }
    }

    private static GameState Travelling()
    {
        GameState state = NewFreePlay();
        LocationData target = state.Locations.First(l => !l.IsWaypoint);
        target.IsVisibleOnMap = true;
        Assert.IsTrue(state.TryStartExpedition(target.Id, new List<string>(), out string message), message);
        return state;
    }

    private static ResidentState Hero(GameState state) => HomePeopleService.Find(state, state.GetSelectedCommander().Id);

    [Test]
    public void Hunger_LosesHpByGameHours_SameForAnySplit_StopsWhenFed()
    {
        GameState a = Travelling();
        GameState b = Travelling();
        a.ArmySupply = 0;
        b.ArmySupply = 0;
        int hp = Hero(a).CurrentHitPoints;
        Hunger.Advance(a, 12.0, null);
        for (int i = 0; i < 12; i++)
            Hunger.Advance(b, 1.0, null);
        Assert.AreEqual(hp - 2, Hero(a).CurrentHitPoints, "12 игровых часов без еды — 2 HP (рабочие 6 ч на HP).");
        Assert.AreEqual(Hero(a).CurrentHitPoints, Hero(b).CurrentHitPoints, "Одинаковые часы — одинаковый урон, как их ни дели.");
        Hunger.Advance(a, 0.0, null);
        Assert.AreEqual(hp - 2, Hero(a).CurrentHitPoints, "Без хода времени урона нет.");
        a.ArmySupply = 5;
        Hunger.Advance(a, 24.0, null);
        Assert.AreEqual(hp - 2, Hero(a).CurrentHitPoints, "С едой урон прекращается.");
        a.ArmySupply = 0;
        Hunger.Advance(a, 1000.0, null);
        Assert.AreEqual(Hunger.MinimumHitPoints, Hero(a).CurrentHitPoints, "Не ниже минимума — исход поражения не меняется.");
    }

    [Test]
    public void EmptySupplyAtMidnight_DoesNotStopOrTurnBackTheTrip()
    {
        GameState state = Travelling();
        state.ArmySupply = 0;
        ContinuousSimulationBatch batch = ContinuousSimulationSystem.AdvanceLocalHours(state, 24.0);
        Assert.IsFalse(batch.RequestAutoPause, "Голод не ставит паузу.");
        Assert.IsNull(batch.MandatoryNotice, "И не требует «Понятно».");
        Assert.IsTrue(state.HasActiveExpedition && state.ActiveExpedition.Phase == CommanderState.TravellingToLocation, "Поход идёт дальше.");
        Assert.IsTrue(batch.Result.Messages.Contains(Hunger.StarvingMessage));
    }

    [Test]
    public void Hire_OneCandidateOnce_WagePaidOncePerGameDay()
    {
        SettlementProfileDefinition post = new SettlementProfileDefinition { Id = SettlementTradeDevFixture.TradingPostProfileId, Name = "Село" };
        post.Tavern = new TavernDefinition { Enabled = true, Id = "t", Name = "Корчма" };
        SettlementCatalog.Current = new SettlementCatalogData(new[] { post });
        try
        {
            GameState state = null;
            List<HireCandidateData> candidates = null;
            for (int seed = 1; seed < 50 && (candidates == null || candidates.Count == 0); seed++)
            {
                state = new CampaignSetup { CrisisId = CampaignStartOptions.FreePlayId, WorldSeed = seed }.CreateCampaign();
                LocationData target = state.Locations.First(l => !l.IsWaypoint);
                target.IsVisibleOnMap = true;
                Assert.IsTrue(state.TryStartExpedition(target.Id, new List<string>(), out string m), m);
                state.ActiveExpedition.RouteIndex = 1;
                Assert.IsTrue(SettlementTradeDevFixture.Apply(state, post.Id, out string report), report);
                state.ActiveExpedition.FighterIds.Clear();
                candidates = HireService.Candidates(state, SettlementCampDevFixture.SettlementId);
            }
            Assert.IsNotEmpty(candidates, "На каком-то seed в корчме есть кандидат.");
            Assert.LessOrEqual(candidates.Count, 2);
            Assert.AreEqual(candidates.Count, HireService.Candidates(state, SettlementCampDevFixture.SettlementId).Count, "Повторный вход не плодит копий.");

            HireCandidateData candidate = candidates[0];
            int fighters = state.Fighters.Count;
            Assert.IsTrue(HireService.TryHire(state, SettlementCampDevFixture.SettlementId, candidate.Id, out string message), message);
            Assert.IsFalse(HireService.TryHire(state, SettlementCampDevFixture.SettlementId, candidate.Id, out _), "Второй раз не нанимается.");
            Assert.AreEqual(fighters + 1, state.Fighters.Count, "Один найм — один боец.");
            Assert.IsTrue(state.ActiveExpedition.FighterIds.Contains(candidate.Id), "Встал на место бойца.");
            Assert.AreEqual(candidate.DailyWage, HireService.Contract(state, candidate.Id).DailyWage, "Плата — та же, что до найма.");
            Assert.IsFalse(HireService.Candidates(state, SettlementCampDevFixture.SettlementId).Exists(c => c.Id == candidate.Id), "Нанятый больше не предлагается.");

            state.ArmyGold = 100;
            state.ArmySupply = 50;
            ContinuousSimulationSystem.AdvanceLocalHours(state, 1.0);
            Assert.AreEqual(100, state.ArmyGold, "До полуночи плата не списывается.");
            double toMidnight = 24.0 - ContinuousSimulationSystem.GetClock(state).HourOfDay;
            ContinuousSimulationSystem.AdvanceLocalHours(state, toMidnight + 0.5);
            Assert.AreEqual(100 - candidate.DailyWage, state.ArmyGold, "Полночь — одна плата.");
            ContinuousSimulationSystem.AdvanceLocalHours(state, 2.0);
            Assert.AreEqual(100 - candidate.DailyWage, state.ArmyGold, "Без новой полуночи — без повторной платы.");
            ContinuousSimulationSystem.AdvanceLocalHours(state, 24.0);
            Assert.AreEqual(100 - 2 * candidate.DailyWage, state.ArmyGold, "Следующие сутки — следующая плата.");
        }
        finally
        {
            SettlementCatalog.Reset();
        }
    }
}

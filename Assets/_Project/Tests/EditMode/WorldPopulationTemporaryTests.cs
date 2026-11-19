using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

// Временные необязательные встречи (просьба пользователя 19.11.2026):
// появление вне обзора, своё занятие, срок по игровому времени, уход и
// исчезновение только вне обзора, защита при взаимодействии, память.
public sealed class WorldPopulationTemporaryTests
{
    private sealed class FixedStats : IUnitStatsProvider
    {
        public bool TryGetCombatStats(string unitTypeId, out UnitCombatStats stats)
        {
            stats = new UnitCombatStats { MaxHitPoints = 20, Attack = 2, Defense = 2, Damage = 3, Movement = 3, Initiative = 3, AttackRange = 1 };
            return !string.IsNullOrEmpty(unitTypeId);
        }
    }

    private IUnitStatsProvider previousStats;
    private WorldPopulationSettings previousRules;
    private const double Sight = WorldMapMovementRules.DefaultFogRevealRadiusHexes + 1.0;

    [SetUp]
    public void SetUp()
    {
        previousStats = GameState.UnitStatsProvider;
        previousRules = WorldPopulationRules.Current;
        GameState.UnitStatsProvider = new FixedStats();
    }

    [TearDown]
    public void TearDown()
    {
        GameState.UnitStatsProvider = previousStats;
        WorldPopulationRules.Current = previousRules;
        WorldMapNavigation.ConfigureDefaultTerrain();
    }

    // Правила: появляется только выбранный вид, сразу и наверняка.
    private static void Rules(string onlyKind)
    {
        WorldPopulationSettings s = WorldPopulationSettings.CreateDefault();
        s.TemporaryChance = 1f;
        s.TemporaryCheckHours = 1f;
        s.TemporaryMaxActive = 1;
        s.TemporaryNearbyThreatLimit = 100;
        s.TemporaryMinHours = 3f;
        s.TemporaryMaxHours = 3f;
        foreach (WorldTemporaryEncounterKind k in s.TemporaryKinds)
        {
            k.Enabled = k.Id == onlyKind;
            k.Terrains.Clear();
            k.NightWeight = 1f;
        }
        WorldPopulationRules.Current = s;
    }

    private static GameState NewState(int seed)
    {
        GeneratedWorldMap map = WorldMapGenerator.Generate(WorldMapGenerationSettings.CreateDefault(), seed);
        WorldMapNavigation.ConfigureFromDefinition(map.ToDefinitionData());
        GameState state = new GameState();
        state.CreateNewGame(map.Seed, null, map.ToDefinitionData());
        state.GeneratedMap = map;
        TestPeople.AdmitAll(state);
        state.ActiveExpedition = new ExpeditionData
        {
            IsActive = true, CommanderId = state.SelectedCommanderId, Phase = CommanderState.AtLocation,
            CurrentMapXPercent = map.StartXPercent, CurrentMapYPercent = map.StartYPercent
        };
        WorldPopulationService.EnsureState(state, ContinuousSimulationSystem.GetAbsoluteHours(state));
        // Постоянные группы не мешают: стоят в углу и спокойны.
        foreach (PopulationGroupState g in state.Population.Groups)
        {
            g.Activity = PopulationGroupState.Wait;
            g.ActivityUntilHour = 1e9;
            g.CalmUntilHour = 1e9;
            g.X = 1f;
            g.Y = 1f;
        }
        foreach (PopulationSourceState s in state.Population.Sources)
            s.CampCooldownUntil = 1e9;
        return state;
    }

    private static void Hours(GameState state, double hours) => ContinuousSimulationSystem.AdvanceLocalHours(state, hours);

    private static double Distance(GameState state, PopulationGroupState g)
    {
        WorldMapHexGrid grid = state.GeneratedMap.CreateGrid();
        return grid.DistanceHexes(state.ActiveExpedition.CurrentMapXPercent, state.ActiveExpedition.CurrentMapYPercent, g.X, g.Y);
    }

    private static PopulationGroupState Temp(GameState state) => state.Population.Groups.FirstOrDefault(g => g.Temporary);

    private static PopulationGroupState SpawnWithin(GameState state, double maxHours)
    {
        for (double h = 0; h < maxHours && Temp(state) == null; h += 0.5)
            Hours(state, 0.5);
        return Temp(state);
    }

    [Test]
    public void Appears_Noticed_PassedBy_Leaves_AndDoesNotReturnAfterLoad()
    {
        Rules("temp-roadside-boar");
        GameState state = NewState(31);
        PopulationGroupState boar = SpawnWithin(state, 6);
        Assert.That(boar, Is.Not.Null, "Временная встреча появилась.");
        Assert.That(Distance(state, boar), Is.GreaterThan(Sight), "Не прямо перед героем — вне обзора.");
        Assert.That(boar.Members.Count, Is.GreaterThan(0));
        Assert.That(boar.ExpireHour, Is.GreaterThan(ContinuousSimulationSystem.GetAbsoluteHours(state)), "Срок по игровому времени.");
        string id = boar.GroupId;

        // Игрок подходит на расстояние обзора, но не вплотную — заметил.
        WorldMapHexGrid grid = state.GeneratedMap.CreateGrid();
        float hex = (float)(grid.HexWidth / grid.CanvasWidth * 100.0);
        state.ActiveExpedition.CurrentMapXPercent = boar.X - hex * 2.8f;
        state.ActiveExpedition.CurrentMapYPercent = boar.Y;
        Hours(state, 0.1);
        Assert.That(boar.Knowledge, Is.EqualTo(3), "Игрок её заметил.");
        Assert.That(WorldPopulationService.IsPopulationDecision(state), Is.False, "Можно пройти мимо без окна.");

        // Проходит мимо: уходит далеко. Срок истёк — группа уходит и исчезает
        // уже вне обзора.
        state.ActiveExpedition.CurrentMapXPercent = boar.X - hex * 15f;
        Hours(state, 4.0);
        Assert.That(boar.Status, Is.EqualTo(PopulationGroupState.Gone), "Закончила занятие и ушла.");
        int records = state.Population.Groups.Count(g => g.Temporary);

        CampaignSaveData data = CampaignSaveService.ExportCampaign(state);
        GameState loaded = CampaignSaveService.RestoreCampaign(JsonUtility.FromJson<CampaignSaveData>(JsonUtility.ToJson(data)));
        Assert.That(loaded.Population.Groups.Single(g => g.GroupId == id).Status, Is.EqualTo(PopulationGroupState.Gone), "После загрузки не ожила.");
        Assert.That(loaded.Population.Groups.Count(g => g.Temporary), Is.EqualTo(records), "Загрузка не пересоздаёт встречу.");
        Assert.That(loaded.Population.Groups.Count(g => g.Temporary && g.IsActive && g.GroupId == id), Is.Zero);
    }

    [Test]
    public void LeavingGroup_IsNotRemovedInFrontOfThePlayer()
    {
        Rules("temp-roadside-boar");
        GameState state = NewState(32);
        PopulationGroupState boar = SpawnWithin(state, 6);
        Assert.That(boar, Is.Not.Null);
        WorldMapHexGrid grid = state.GeneratedMap.CreateGrid();
        float hex = (float)(grid.HexWidth / grid.CanvasWidth * 100.0);
        for (int i = 0; i < 80 && boar.IsActive; i++)
        {
            // Отряд всё время держится в 2,5 клетки от группы.
            state.ActiveExpedition.CurrentMapXPercent = boar.X - hex * 2.5f;
            state.ActiveExpedition.CurrentMapYPercent = boar.Y;
            Hours(state, 0.1);
        }
        Assert.That(boar.IsActive, Is.True, "На глазах у игрока фигурка не растворяется.");
        Assert.That(boar.Activity, Is.EqualTo(PopulationGroupState.Leave), "Срок вышел — группа уходит.");
    }

    [Test]
    public void EngagedGroup_DoesNotExpire()
    {
        Rules("temp-roadside-boar");
        GameState state = NewState(33);
        PopulationGroupState boar = SpawnWithin(state, 6);
        Assert.That(boar, Is.Not.Null);
        state.Population.Contacts.Add(new PopulationContactState
        {
            ContactId = "c-test", Kind = PopulationContactState.BeastAttack, GroupId = boar.GroupId, Status = PopulationContactState.BattlePending
        });
        state.ActiveExpedition.CurrentMapXPercent = 1f;
        Hours(state, 8.0);
        Assert.That(boar.IsActive, Is.True, "Во время боя встреча не исчезает по таймеру.");
        Assert.That(boar.Activity, Is.Not.EqualTo(PopulationGroupState.Leave));
    }

    [Test]
    public void PassingGroup_EntersViewFromTheEdge()
    {
        Rules("temp-passing-band");
        // Темп мира — как в игре (ассет движения): время идёт само.
        WorldMapMovementRules previousMovement = WorldMapMovementRules.Current;
        WorldMapMovementRules game = WorldMapMovementRules.CreateDefault();
        game.IndependentTime = true;
        game.HeroRunSpeedHexesPerSecond = 0.28f;
        game.WorldGameHoursPerRealSecond = 0.12f;
        WorldMapMovementRules.Current = game;
        try
        {
        GameState state = NewState(34);
        PopulationGroupState band = SpawnWithin(state, 6);
        Assert.That(band, Is.Not.Null);
        band.CalmUntilHour = 1e9;
        double start = Distance(state, band), closest = start;
        for (int i = 0; i < 120 && band.IsActive; i++)
        {
            Hours(state, 0.1);
            closest = System.Math.Min(closest, Distance(state, band));
        }
        Assert.That(start, Is.GreaterThan(Sight), "Появилась за пределами обзора.");
        Assert.That(closest, Is.LessThanOrEqualTo(Sight), "Сама вошла в обзор, проходя мимо.");
        Assert.That(band.TempReason, Is.EqualTo("идут через округу"));
        }
        finally
        {
            WorldMapMovementRules.Current = previousMovement;
        }
    }

    [Test]
    public void ClearedArea_RespiteAndRepeat_LimitAppearance()
    {
        Rules("temp-roadside-boar");
        GameState state = NewState(35);
        // Передышка после боя: появления нет.
        state.Population.RespiteUntilHour = 1e9;
        Hours(state, 4.0);
        Assert.That(Temp(state), Is.Null, "Передышка после боя.");
        state.Population.RespiteUntilHour = 0;
        // Вокруг героя недавно разбита группа — участок не наполняется.
        WorldMapHexGrid grid = state.GeneratedMap.CreateGrid();
        PopulationGroupState anchor = state.Population.Groups[0];
        anchor.Status = PopulationGroupState.Destroyed;
        anchor.DestroyedHour = ContinuousSimulationSystem.GetAbsoluteHours(state);
        anchor.X = state.ActiveExpedition.CurrentMapXPercent;
        anchor.Y = state.ActiveExpedition.CurrentMapYPercent;
        WorldPopulationRules.Current.TemporaryClearedHexes = 30f;
        Hours(state, 4.0);
        Assert.That(Temp(state), Is.Null, "Зачищенная дорога не заполняется новыми врагами.");
        Assert.That(grid, Is.Not.Null);
    }
}

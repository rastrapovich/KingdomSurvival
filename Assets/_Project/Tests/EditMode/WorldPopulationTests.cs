using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Debug = UnityEngine.Debug;

// Бриф «Живое население глобальной карты» (19.11.2026): контракты слоя
// населения — генерация по географии, жизнь групп по часам мира, контакт,
// бой тем же составом, источники и пополнение, сохранения и старые партии.
public sealed class WorldPopulationTests
{
    private sealed class FixedStats : IUnitStatsProvider
    {
        public bool TryGetCombatStats(string unitTypeId, out UnitCombatStats stats)
        {
            stats = new UnitCombatStats { MaxHitPoints = 20, Attack = 2, Defense = 2, Damage = 3, Movement = 3, Initiative = 3, AttackRange = 1 };
            return !string.IsNullOrEmpty(unitTypeId);
        }
    }

    private sealed class Excluding : IUnitCatalogAvailability
    {
        private readonly string excluded;
        public Excluding(string excluded) => this.excluded = excluded;

        public bool IsAvailableForNewGroups(string unitTypeId, out string reason)
        {
            reason = unitTypeId == excluded ? "исключено из новых встреч" : string.Empty;
            return unitTypeId != excluded;
        }
    }

    private IUnitStatsProvider previousStats;
    private IUnitCatalogAvailability previousAvailability;
    private WorldPopulationSettings previousRules;

    [SetUp]
    public void SetUp()
    {
        previousStats = GameState.UnitStatsProvider;
        previousAvailability = WorldPopulationCatalog.Availability;
        previousRules = WorldPopulationRules.Current;
        GameState.UnitStatsProvider = new FixedStats();
        WorldPopulationCatalog.Availability = null;
        WorldPopulationRules.Current = WorldPopulationSettings.CreateDefault();
    }

    [TearDown]
    public void TearDown()
    {
        GameState.UnitStatsProvider = previousStats;
        WorldPopulationCatalog.Availability = previousAvailability;
        WorldPopulationRules.Current = previousRules;
        WorldMapNavigation.ConfigureDefaultTerrain();
    }

    private static WorldMapGenerationSettings Settings(WorldMapRegionKind kind = WorldMapRegionKind.Mixed)
    {
        WorldMapGenerationSettings s = WorldMapGenerationSettings.CreateDefault();
        s.RegionKind = kind;
        return s;
    }

    private static GeneratedWorldMap FindMap(Func<GeneratedWorldMap, bool> wanted, WorldMapGenerationSettings settings = null)
    {
        for (int seed = 1; seed <= 40; seed++)
        {
            GeneratedWorldMap map = WorldMapGenerator.Generate(settings ?? Settings(), seed);
            if (wanted(map))
                return map;
        }
        Assert.Inconclusive("Нет подходящей карты среди 40 seed.");
        return null;
    }

    private static GameState NewState(GeneratedWorldMap map)
    {
        WorldMapNavigation.ConfigureFromDefinition(map.ToDefinitionData());
        GameState state = new GameState();
        state.CreateNewGame(map.Seed, null, map.ToDefinitionData());
        state.GeneratedMap = map;
        TestPeople.AdmitAll(state);
        state.ArmySupply = 100;
        state.ArmyGold = 50;
        state.ActiveExpedition = new ExpeditionData
        {
            IsActive = true,
            CommanderId = state.SelectedCommanderId,
            Phase = CommanderState.AtLocation,
            CurrentMapXPercent = map.StartXPercent,
            CurrentMapYPercent = map.StartYPercent
        };
        WorldPopulationService.EnsureState(state, ContinuousSimulationSystem.GetAbsoluteHours(state));
        return state;
    }

    private static void PlaceHero(GameState state, float x, float y)
    {
        state.ActiveExpedition.CurrentMapXPercent = x;
        state.ActiveExpedition.CurrentMapYPercent = y;
    }

    // Часы мира идут штатно (без движения героя) — население живёт на них.
    private static ContinuousSimulationBatch Hours(GameState state, double hours) =>
        ContinuousSimulationSystem.AdvanceLocalHours(state, hours);

    private static PopulationGroupState Group(GameState state, string role, string behavior = null) =>
        state.Population.Groups.FirstOrDefault(g => g.IsActive && g.Role == role && (behavior == null || g.Behavior == behavior) &&
                                                    WorldPopulationService.Definition(state, g.DefinitionId)?.State == "Travel");

    // ------------------------------------------------------------------
    // Генерация

    [Test]
    public void SameSeed_GivesSameSourcesCompositionsAndRoutes()
    {
        GeneratedWorldMap a = WorldMapGenerator.Generate(Settings(), 11);
        GeneratedWorldMap b = WorldMapGenerator.Generate(Settings(), 11);
        Assert.That(a.PopulationReport.Enabled, Is.True);
        Assert.That(a.Actors.Select(x => x.ActorId), Is.EqualTo(b.Actors.Select(x => x.ActorId)));
        Assert.That(a.Actors.SelectMany(x => x.Members).Select(m => m.MemberId), Is.EqualTo(b.Actors.SelectMany(x => x.Members).Select(m => m.MemberId)));
        Assert.That(a.Actors.SelectMany(x => x.RouteCells), Is.EqualTo(b.Actors.SelectMany(x => x.RouteCells)));
        Assert.That(a.PopulationSources.Select(s => s.SourceId + "@" + s.Cell), Is.EqualTo(b.PopulationSources.Select(s => s.SourceId + "@" + s.Cell)));
        Assert.That(a.Actors.Any(x => x.Role == WorldMapGroupRule.Beast || x.Role == WorldMapGroupRule.Creature), Is.True, "Есть звери или существа.");
        Assert.That(a.Actors.Where(x => x.Role == WorldMapGroupRule.Patrol || x.Role == WorldMapGroupRule.Caravan || x.Role == WorldMapGroupRule.Band)
            .All(x => x.State != "Travel" || x.Members.Count > 0), Is.True, "Людские группы получили составы.");
    }

    [Test]
    public void PresetOrder_DoesNotMovePopulation()
    {
        WorldMapGenerationSettings s = Settings();
        GeneratedWorldMap a = WorldMapGenerator.Generate(s, 12);
        WorldMapGenerationSettings reversed = Settings();
        reversed.Population.Presets.Reverse();
        GeneratedWorldMap b = WorldMapGenerator.Generate(reversed, 12);
        Assert.That(b.Actors.Select(x => x.ActorId + ":" + x.HomeXPercent.ToString("0.###")),
            Is.EqualTo(a.Actors.Select(x => x.ActorId + ":" + x.HomeXPercent.ToString("0.###"))),
            "Порядок записей каталога не переставляет группы.");
    }

    [Test]
    public void UnavailableCreature_IsSkippedWithReason_AndBeregovikIsOffByDefault()
    {
        WorldPopulationCatalog.Availability = new Excluding("wolf");
        GeneratedWorldMap map = WorldMapGenerator.Generate(Settings(), 13);
        Assert.That(map.Actors.Any(a => a.Members.Any(m => m.UnitTypeId == "wolf")), Is.False);
        Assert.That(map.PopulationReport.Notes.Any(n => !n.Placed && n.PresetId == "wolf-pack" && n.Message.Contains("исключено")), Is.True);
        Assert.That(map.Actors.Any(a => a.PresetId == "beregovik"), Is.False, "Береговик выключен решением 19.11.2026.");
        Assert.That(map.PopulationReport.Notes.Any(n => n.PresetId == "beregovik" && !n.Placed), Is.True);
        Assert.That(map.Report.Issues.Any(i => i.Code == "P01"), Is.True, "Пропуски видны в проверке генератора.");
    }

    [Test]
    public void ManySeeds_PlacementRules_Hold()
    {
        WorldMapRegionKind[] kinds = { WorldMapRegionKind.RiverValley, WorldMapRegionKind.ForestLake, WorldMapRegionKind.HillAndLedge };
        Stopwatch watch = Stopwatch.StartNew();
        List<string> failures = new List<string>();
        int maps = 0, groups = 0, threats = 0;
        double calmShareSum = 0;
        foreach (WorldMapRegionKind kind in kinds)
        {
            for (int seed = 100; seed < 120; seed++)
            {
                GeneratedWorldMap map = WorldMapGenerator.Generate(Settings(kind), seed);
                WorldPopulationSettings p = WorldPopulationSettings.CreateDefault();
                WorldMapHexGrid grid = map.CreateGrid();
                WorldMapTerrainLayer layer = map.CreateTerrainLayer();
                maps++;
                groups += map.Actors.Count;
                foreach (GeneratedMapActor actor in map.Actors.Where(a => WorldPopulationBehavior.IsHostileCapable(a.Behavior) && a.State == "Travel"))
                {
                    threats++;
                    double toStart = grid.DistanceHexes(actor.HomeXPercent, actor.HomeYPercent, map.StartXPercent, map.StartYPercent);
                    if (toStart < p.StartSafeRadiusHexes + 1)
                        failures.Add($"{kind}/{seed}: {actor.ActorId} у старта ({toStart:0.0} кл.)");
                    WorldMapGameplayTerrainType home = layer.GetAtIndex(grid.IndexOf(grid.CellAtPercent(actor.HomeXPercent, actor.HomeYPercent)));
                    if (home == WorldMapGameplayTerrainType.Water || home == WorldMapGameplayTerrainType.Cliffs || home == WorldMapGameplayTerrainType.Mountains)
                        failures.Add($"{kind}/{seed}: {actor.ActorId} на {home}");
                    if (actor.Role == WorldMapGroupRule.Beast || actor.Role == WorldMapGroupRule.Creature)
                    {
                        foreach (GeneratedMapSite v in map.Villages)
                        {
                            double d = grid.DistanceHexes(actor.HomeXPercent, actor.HomeYPercent, v.XPercent, v.YPercent);
                            if (d < p.ServiceSafeRadiusHexes)
                                failures.Add($"{kind}/{seed}: {actor.ActorId} у деревни ({d:0.0} кл.)");
                        }
                        if (actor.PresetId == "tinny-polzun" && home != WorldMapGameplayTerrainType.Swamp)
                            failures.Add($"{kind}/{seed}: ползун не на болоте ({home})");
                    }
                }
                if (map.Actors.Select(a => a.ActorId).Distinct().Count() != map.Actors.Count)
                    failures.Add($"{kind}/{seed}: повторный ID группы");
                if (map.PopulationReport.EarlyRouteHexes > 0)
                {
                    double calm = map.PopulationReport.EarlyRouteCalmHexes / (double)map.PopulationReport.EarlyRouteHexes;
                    calmShareSum += calm;
                    if (map.PopulationReport.EarlyRouteCalmHexes == 0)
                        failures.Add($"{kind}/{seed}: на раннем пути нет спокойного участка");
                }
            }
        }
        long withPopulation = watch.ElapsedMilliseconds;
        // Цена слоя: те же 10 карт с населением и без него.
        Stopwatch on = Stopwatch.StartNew();
        for (int seed = 100; seed < 110; seed++)
            WorldMapGenerator.Generate(Settings(kinds[0]), seed);
        on.Stop();
        WorldMapGenerationSettings off = Settings(kinds[0]);
        off.Population.Enabled = false;
        Stopwatch offWatch = Stopwatch.StartNew();
        for (int seed = 100; seed < 110; seed++)
            WorldMapGenerator.Generate(off, seed);
        offWatch.Stop();
        Debug.Log($"Население: {maps} карт (3 типа × 20 seed, {Settings().HexesAcross} клеток в ширину) за {withPopulation} мс; групп {groups}, угроз {threats}, " +
                  $"средняя доля спокойного раннего пути {calmShareSum / maps:P0}. Нарушений: {failures.Count}. " +
                  $"10 карт: с населением {on.ElapsedMilliseconds} мс, без — {offWatch.ElapsedMilliseconds} мс.");
        Assert.That(failures, Is.Empty, string.Join("\n", failures.Take(20)));
    }

    [Test]
    public void EdgeCases_SmallMap_NoSwamp_OneVillage_NoThreats()
    {
        WorldMapGenerationSettings small = Settings();
        small.HexesAcross = 30;
        small.SwampShare = 0f;
        small.VillageCount = 1;
        GeneratedWorldMap map = WorldMapGenerator.Generate(small, 21);
        Assert.That(map.Actors.Any(a => a.PresetId == "tinny-polzun"), Is.False, "Нет болота — нет болотного обитателя.");
        Assert.That(map.PopulationReport.Notes.Any(n => !n.Placed), Is.True, "Пропуски объяснены.");
        WorldMapHexGrid grid = map.CreateGrid();
        foreach (GeneratedMapActor actor in map.Actors.Where(a => WorldPopulationBehavior.IsHostileCapable(a.Behavior)))
            Assert.That(grid.DistanceHexes(actor.HomeXPercent, actor.HomeYPercent, map.StartXPercent, map.StartYPercent), Is.GreaterThanOrEqualTo(3.0), actor.ActorId);

        WorldMapGenerationSettings peaceful = Settings();
        peaceful.Population.ThreatScale = 0f;
        GeneratedWorldMap calm = WorldMapGenerator.Generate(peaceful, 21);
        Assert.That(calm.Actors.Any(a => a.Role == WorldMapGroupRule.Beast || a.Role == WorldMapGroupRule.Creature), Is.False);
        Assert.That(calm.Actors.Any(a => a.Role == WorldMapGroupRule.Caravan), Is.True, "Мирное движение не сокращается вместе с врагами.");
    }

    // ------------------------------------------------------------------
    // Симуляция

    [Test]
    public void Pause_DoesNotMoveGroups()
    {
        GameState state = NewState(WorldMapGenerator.Generate(Settings(), 14));
        Hours(state, 0.5);
        string before = string.Join(";", state.Population.Groups.Select(g => g.X.ToString("0.0000") + "," + g.Y.ToString("0.0000")));
        ContinuousSimulationSystem.SetPaused(state, true);
        ContinuousSimulationSystem.Advance(state, 5f);
        string after = string.Join(";", state.Population.Groups.Select(g => g.X.ToString("0.0000") + "," + g.Y.ToString("0.0000")));
        Assert.That(after, Is.EqualTo(before));
    }

    [Test]
    public void SameGameTime_GivesSameWorld_ForDifferentStepSizes()
    {
        GeneratedWorldMap map = WorldMapGenerator.Generate(Settings(), 15);
        GameState a = NewState(map);
        GameState b = NewState(WorldMapGenerator.Generate(Settings(), 15));
        // Герой вдали от всех — сравниваем только жизнь групп.
        double start = ContinuousSimulationSystem.GetAbsoluteHours(a);
        WorldPopulationService.Advance(a, start + 30.0, 0f, 0f, 0f, 0f, start, null);
        for (double t = start; t < start + 30.0 - 1e-9; t += 0.013)
            WorldPopulationService.Advance(b, Math.Min(start + 30.0, t + 0.013), 0f, 0f, 0f, 0f, t, null);
        WorldPopulationService.Advance(b, start + 30.0, 0f, 0f, 0f, 0f, start + 30.0, null);
        Assert.That(b.Population.Groups.Select(g => g.GroupId + g.Activity + g.X.ToString("0.000") + g.Y.ToString("0.000")),
            Is.EqualTo(a.Population.Groups.Select(g => g.GroupId + g.Activity + g.X.ToString("0.000") + g.Y.ToString("0.000"))),
            "Одинаковое игровое время и действия — одинаковый мир при любом шаге кадра.");
    }

    [Test]
    public void ActivityChange_StartsFromCurrentPosition_WithoutTeleport()
    {
        GeneratedWorldMap map = FindMap(m => m.Actors.Any(a => a.Role == WorldMapGroupRule.Beast));
        GameState state = NewState(map);
        PlaceHero(state, 0f, 0f);
        Hours(state, 3.3);
        PopulationGroupState g = Group(state, WorldMapGroupRule.Beast);
        WorldMapHexGrid grid = map.CreateGrid();
        float x0 = g.X, y0 = g.Y;
        g.Activity = PopulationGroupState.Wait;
        g.ActivityUntilHour = ContinuousSimulationSystem.GetAbsoluteHours(state) + 1.0;
        Hours(state, 0.5);
        Assert.That(grid.DistanceHexes(x0, y0, g.X, g.Y), Is.LessThan(0.01), "Ожидание — на месте.");
        double maxStep = WorldMapMovementRules.Current.GroupHexesPerGameHour(WorldMapActorMotion.SpeedHexesPerHour(g.Role)) * WorldPopulationService.TickHours * 1.3 + 0.01;
        GeneratedMapActor def = WorldPopulationService.Definition(state, g.DefinitionId);
        for (int i = 0; i < 200; i++)
        {
            float px = g.X, py = g.Y;
            Hours(state, WorldPopulationService.TickHours);
            if (g.Activity != PopulationGroupState.Route)
                Assert.That(grid.DistanceHexes(px, py, g.X, g.Y), Is.LessThanOrEqualTo(maxStep), "Возвращение без скачка к началу маршрута.");
        }
        Assert.That(g.Activity, Is.EqualTo(PopulationGroupState.Route), "Группа вернулась к своему распорядку.");
        Assert.That(def, Is.Not.Null);
    }

    [Test]
    public void Band_ChasesWeakHeroInSight_IgnoresHeroFarAway()
    {
        GeneratedWorldMap map = FindMap(m => m.Actors.Any(a => a.Role == WorldMapGroupRule.Band && a.State == "Travel" && a.Members.Count > 0));
        GameState state = NewState(map);
        PopulationGroupState band = Group(state, WorldMapGroupRule.Band);
        PlaceHero(state, 0f, 0f);
        Hours(state, 1.0);
        Assert.That(band.Activity, Is.Not.EqualTo(PopulationGroupState.Chase), "Без обнаружения банда не знает, где герой.");
        WorldMapHexGrid grid = map.CreateGrid();
        PlaceHero(state, band.X + (float)(2.0 * grid.HexWidth / grid.CanvasWidth * 100.0), band.Y);
        state.Population.RespiteUntilHour = 0;
        band.CalmUntilHour = 0;
        Hours(state, WorldPopulationService.TickHours * 2);
        Assert.That(band.Activity == PopulationGroupState.Chase || WorldPopulationService.PendingContact(state) != null, Is.True,
            "Слабая цель в зоне обнаружения — перехват.");
    }

    [Test]
    public void Band_AvoidsStrongPatrol_AndHuntsWeakCaravan()
    {
        GeneratedWorldMap map = FindMap(m => m.Actors.Any(a => a.Role == WorldMapGroupRule.Band && a.State == "Travel" && a.Members.Count > 0) &&
                                             m.Actors.Any(a => a.Role == WorldMapGroupRule.Caravan && a.State == "Travel") &&
                                             m.Actors.Any(a => a.Role == WorldMapGroupRule.Patrol && a.State == "Travel"));
        GameState state = NewState(map);
        state.ActiveExpedition = null;
        PopulationGroupState band = Group(state, WorldMapGroupRule.Band);
        PopulationGroupState caravan = Group(state, WorldMapGroupRule.Caravan);
        PopulationGroupState patrol = Group(state, WorldMapGroupRule.Patrol);
        WorldMapHexGrid grid = map.CreateGrid();
        float hex = (float)(grid.HexWidth / grid.CanvasWidth * 100.0);
        // Остальные группы — в стороне и стоят: проверяется одна пара.
        foreach (PopulationGroupState g in state.Population.Groups.Where(g => g != band))
        {
            g.Activity = PopulationGroupState.Wait;
            g.ActivityUntilHour = 1e9;
            g.X = 99f;
            g.Y = 99f;
        }
        Hours(state, 0.1);
        band.Activity = PopulationGroupState.Route;
        band.CalmUntilHour = 0;
        band.ConflictId = string.Empty;

        // Сильный дозор рядом — банда уходит (подготовленная банда сильнее
        // обычного дозора — тогда дозор усиливается).
        while (WorldPopulationService.GroupStrength(patrol) < WorldPopulationService.GroupStrength(band))
            patrol.Members.Add(new PopulationMemberState { MemberId = "test-guard-" + patrol.Members.Count, UnitTypeId = "guard" });
        patrol.Activity = PopulationGroupState.Wait;
        patrol.ActivityUntilHour = 1e9;
        patrol.X = band.X + hex;
        patrol.Y = band.Y;
        Hours(state, WorldPopulationService.TickHours);
        Assert.That(band.Activity, Is.EqualTo(PopulationGroupState.Avoid), $"цель {band.TargetId}, до дозора {grid.DistanceHexes(band.X, band.Y, patrol.X, patrol.Y):0.00} кл., силы {WorldPopulationService.GroupStrength(band)}/{WorldPopulationService.GroupStrength(patrol)}, покой до {band.CalmUntilHour}, такт {state.Population.LastSimHour}");

        // Дозор далеко, рядом слабый обоз — банда идёт на перехват.
        patrol.X = 0f;
        patrol.Y = 0f;
        band.Activity = PopulationGroupState.Route;
        band.CalmUntilHour = 0;
        caravan.Members.RemoveAt(0);
        caravan.Activity = PopulationGroupState.Wait;
        caravan.ActivityUntilHour = 1e9;
        Hours(state, WorldPopulationService.TickHours);
        caravan.X = band.X + hex * 2f;
        caravan.Y = band.Y;
        Hours(state, WorldPopulationService.TickHours);
        Assert.That(band.Activity, Is.EqualTo(PopulationGroupState.Chase));
        Assert.That(band.TargetId, Is.EqualTo(caravan.GroupId));
    }

    [Test]
    public void Crossing_WithinOneBigStep_IsCaught_AndLongStepStopsOnContact()
    {
        GeneratedWorldMap map = FindMap(m => m.Actors.Any(a => a.Behavior == WorldPopulationBehavior.Territorial));
        GameState state = NewState(map);
        PopulationGroupState beast = state.Population.Groups.First(g => g.Behavior == WorldPopulationBehavior.Territorial);
        WorldMapHexGrid grid = map.CreateGrid();
        float hex = (float)(grid.HexWidth / grid.CanvasWidth * 100.0);
        double now = ContinuousSimulationSystem.GetAbsoluteHours(state);
        WorldPopulationService.Advance(state, now, 0f, 0f, 0f, 0f, now, null);
        beast.Activity = PopulationGroupState.Wait;
        beast.ActivityUntilHour = 1e9;
        // Герой за один такт пролетает сквозь зверя: концы отрезка в 3 клетках.
        List<string> messages = new List<string>();
        bool raised = WorldPopulationService.Advance(state, now + WorldPopulationService.TickHours,
            beast.X - hex * 3f, beast.Y, beast.X + hex * 3f, beast.Y, now, messages);
        Assert.That(raised, Is.True, "Сближение по ходу, а не только в конце шага.");
        Assert.That(WorldPopulationService.IsPopulationDecision(state), Is.True, "Штатное окно решения похода.");

        // Пока окно открыто, долгий шаг не открывает очередь новых контактов.
        double simBefore = state.Population.LastSimHour;
        bool again = WorldPopulationService.Advance(state, now + 10.0, beast.X, beast.Y, beast.X, beast.Y, now, messages);
        Assert.That(again, Is.False);
        Assert.That(state.Population.Contacts.Count, Is.EqualTo(1));
        Assert.That(state.Population.LastSimHour, Is.EqualTo(simBefore), "Мир стоит на времени события.");
    }

    // ------------------------------------------------------------------
    // Контакт, бой и последствия

    private static (GameState State, PopulationGroupState Beast) ContactWithBeast()
    {
        GeneratedWorldMap map = FindMap(m => m.Actors.Any(a => a.Behavior == WorldPopulationBehavior.Territorial && a.Members.Count > 0));
        GameState state = NewState(map);
        PopulationGroupState beast = state.Population.Groups.First(g => g.Behavior == WorldPopulationBehavior.Territorial);
        Hours(state, 0.2);
        beast.Activity = PopulationGroupState.Wait;
        beast.ActivityUntilHour = 1e9;
        PlaceHero(state, beast.X, beast.Y);
        Hours(state, WorldPopulationService.TickHours * 2);
        Assert.That(WorldPopulationService.IsPopulationDecision(state), Is.True, "Контакт со зверем открыт.");
        return (state, beast);
    }

    [Test]
    public void Battle_UsesTheSameComposition_AndAppliesOnce()
    {
        (GameState state, PopulationGroupState beast) = ContactWithBeast();
        beast.Members[0].HitPoints = 5;
        Assert.That(ExpeditionDecisionSystem.TryApplyChoice(state, WorldPopulationService.OptionA, out string message), Is.True, message);
        PopulationContactState contact = WorldPopulationService.BattleToStart(state);
        Assert.That(contact, Is.Not.Null);
        CampaignBattleRequest request = WorldPopulationService.CreateBattleRequest(state, contact);
        Assert.That(request.Enemies.Select(e => e.InstanceId), Is.EqualTo(beast.Members.Where(m => m.Alive).Select(m => m.MemberId)));
        Assert.That(request.Enemies[0].CurrentHitPoints, Is.EqualTo(5), "Без незаметного полного лечения.");
        WorldPopulationService.MarkBattleStarted(state, contact);

        CampaignBattleResult result = new CampaignBattleResult
        {
            BattleId = request.BattleId,
            Outcome = CampaignBattleOutcome.Victory,
            Enemies = beast.Members.Select(m => new CampaignBattleEnemyRecord { UnitTypeId = m.UnitTypeId, InstanceId = m.MemberId, Defeated = true }).ToList()
        };
        Assert.That(CampaignBattleBridge.ApplyResult(state, result, new List<string>()), Is.EqualTo(CampaignBattleApplyStatus.SquadSurvived));
        Assert.That(beast.IsActive, Is.False, "Побеждённая группа больше не действует.");
        PopulationSourceState source = WorldPopulationService.FindSource(state, beast.SourceId);
        Assert.That(source.Status, Is.EqualTo(source.Reserve > 0 ? PopulationSourceState.Weakened : PopulationSourceState.Exhausted));
        int chronicle = state.Chronicle.Entries.Count;
        Assert.That(CampaignBattleBridge.ApplyResult(state, result, null), Is.EqualTo(CampaignBattleApplyStatus.AlreadyApplied));
        Assert.That(state.Chronicle.Entries.Count, Is.EqualTo(chronicle), "Повторное применение ничего не меняет.");
        Assert.That(WorldPopulationService.SuppressesRoadEpisodes(state, ContinuousSimulationSystem.GetAbsoluteHours(state)), Is.True, "Передышка после боя.");
    }

    [Test]
    public void Retreat_KeepsResidualGroup_AndRetreatChoiceCalmsIt()
    {
        (GameState state, PopulationGroupState beast) = ContactWithBeast();
        Assert.That(ExpeditionDecisionSystem.TryApplyChoice(state, WorldPopulationService.OptionA, out _), Is.True);
        PopulationContactState contact = WorldPopulationService.BattleToStart(state);
        CampaignBattleRequest request = WorldPopulationService.CreateBattleRequest(state, contact);
        CampaignBattleResult result = new CampaignBattleResult
        {
            BattleId = request.BattleId,
            Outcome = CampaignBattleOutcome.Retreat,
            Enemies = beast.Members.Select(m => new CampaignBattleEnemyRecord { UnitTypeId = m.UnitTypeId, InstanceId = m.MemberId, HitPoints = 3 }).ToList()
        };
        CampaignBattleBridge.ApplyResult(state, result, null);
        Assert.That(beast.IsActive, Is.True);
        Assert.That(beast.Members.All(m => m.HitPoints == 3), Is.True, "Остаточный состав сохраняется.");
        Assert.That(beast.CalmUntilHour, Is.GreaterThan(ContinuousSimulationSystem.GetAbsoluteHours(state)), "Повторный контакт ограничен.");
        Hours(state, 1.0);
        Assert.That(WorldPopulationService.IsPopulationDecision(state), Is.False, "Нет бесконечного повторного окна.");
    }

    [Test]
    public void BandSource_Refills_ByReserveAndDelay_AndRazedCampStaysSilent()
    {
        GeneratedWorldMap map = FindMap(m => m.PopulationSources.Any(s => s.Kind == GeneratedPopulationSource.BandCamp));
        GameState state = NewState(map);
        state.ActiveExpedition = null;
        PopulationSourceState camp = state.Population.Sources.First(s => s.Kind == GeneratedPopulationSource.BandCamp);
        int reserve = camp.Reserve;
        Assert.That(reserve, Is.EqualTo(WorldPopulationRules.Current.BandReserve));
        PopulationGroupState band = state.Population.Groups.First(g => g.SourceId == camp.SourceId);
        double now = ContinuousSimulationSystem.GetAbsoluteHours(state);

        // Разгром вылазки — временное облегчение: пополнение не раньше срока.
        typeof(WorldPopulationService).GetMethod("Destroy", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
            .Invoke(null, new object[] { state, band, now, "hero" });
        Assert.That(camp.Status, Is.EqualTo(PopulationSourceState.Weakened));
        Hours(state, WorldPopulationRules.Current.BandRefillHours - 2);
        Assert.That(state.Population.Groups.Count(g => g.IsActive && g.SourceId == camp.SourceId), Is.Zero, "До срока лагерь молчит.");
        Hours(state, 4);
        PopulationGroupState second = state.Population.Groups.FirstOrDefault(g => g.IsActive && g.SourceId == camp.SourceId);
        Assert.That(second, Is.Not.Null, "После срока — новая вылазка.");
        Assert.That(second.GroupId, Is.Not.EqualTo(band.GroupId), "С новым ID — погибшие не оживают.");
        Assert.That(camp.Reserve, Is.EqualTo(reserve - 1));

        // Окончательное устранение: лагерь сожжён — пополнения нет никогда.
        WorldPopulationService.EliminateSource(state, camp, ContinuousSimulationSystem.GetAbsoluteHours(state));
        typeof(WorldPopulationService).GetMethod("Destroy", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
            .Invoke(null, new object[] { state, second, ContinuousSimulationSystem.GetAbsoluteHours(state), "hero" });
        Hours(state, 24 * 10);
        Assert.That(state.Population.Groups.Count(g => g.IsActive && g.SourceId == camp.SourceId), Is.Zero);
        Assert.That(camp.Status, Is.EqualTo(PopulationSourceState.Eliminated));
    }

    [Test]
    public void CampApproach_EmptyCamp_CanBeRazedByThePlayer()
    {
        GeneratedWorldMap map = FindMap(m => m.PopulationSources.Any(s => s.Kind == GeneratedPopulationSource.BandCamp));
        GameState state = NewState(map);
        PopulationSourceState camp = state.Population.Sources.First(s => s.Kind == GeneratedPopulationSource.BandCamp);
        foreach (PopulationGroupState g in state.Population.Groups.Where(g => g.SourceId == camp.SourceId))
        {
            g.X = 0f;
            g.Y = 0f;
            g.Activity = PopulationGroupState.Wait;
            g.ActivityUntilHour = 1e9;
        }
        PlaceHero(state, camp.X, camp.Y);
        Hours(state, WorldPopulationService.TickHours * 2);
        Assert.That(WorldPopulationService.IsPopulationDecision(state), Is.True, "Подход к лагерю — окно решения.");
        Assert.That(state.ActiveExpedition.PendingDecision.OptionA.Label, Does.Contain("Разорить"));
        Assert.That(ExpeditionDecisionSystem.TryApplyChoice(state, WorldPopulationService.OptionA, out _), Is.True);
        Assert.That(camp.Status, Is.EqualTo(PopulationSourceState.Eliminated));
        Assert.That(state.Chronicle.Entries.Any(e => e.Id == "population.eliminated." + camp.SourceId), Is.True);
    }

    [Test]
    public void HeroIntervention_FreezesBackgroundConflict_AndRescuesCaravan()
    {
        GeneratedWorldMap map = FindMap(m => m.Actors.Any(a => a.Role == WorldMapGroupRule.Band && a.State == "Travel" && a.Members.Count > 0) &&
                                             m.Actors.Any(a => a.Role == WorldMapGroupRule.Caravan && a.State == "Travel"));
        GameState state = NewState(map);
        PopulationGroupState band = Group(state, WorldMapGroupRule.Band);
        PopulationGroupState caravan = Group(state, WorldMapGroupRule.Caravan);
        foreach (PopulationGroupState g in state.Population.Groups.Where(g => g.Role == WorldMapGroupRule.Patrol))
            g.Status = PopulationGroupState.Destroyed;
        // Лагерь банды рядом с торговым путём: подход к нему здесь не проверяется.
        foreach (PopulationSourceState s in state.Population.Sources)
            s.CampCooldownUntil = 1e9;
        foreach (PopulationGroupState g in state.Population.Groups.Where(g => g != band && g != caravan))
        {
            g.Activity = PopulationGroupState.Wait;
            g.ActivityUntilHour = 1e9;
            g.X = 99f;
            g.Y = 99f;
        }
        PlaceHero(state, 0f, 0f);
        Hours(state, 0.1);
        state.Population.Conflicts.Clear();
        band.ConflictId = caravan.ConflictId = string.Empty;
        caravan.Activity = PopulationGroupState.Wait;
        caravan.ActivityUntilHour = 1e9;
        caravan.X = band.X;
        caravan.Y = band.Y;
        band.Activity = PopulationGroupState.Chase;
        band.TargetId = caravan.GroupId;
        band.ActivityStartHour = ContinuousSimulationSystem.GetAbsoluteHours(state);
        band.ActivityOriginX = band.X;
        band.ActivityOriginY = band.Y;
        Hours(state, WorldPopulationService.TickHours * 2);
        PopulationConflictState conflict = state.Population.Conflicts.Single();
        Assert.That(conflict.Status, Is.EqualTo(PopulationConflictState.Open), "Один зарегистрированный конфликт пары.");

        PlaceHero(state, conflict.X, conflict.Y);
        Hours(state, WorldPopulationService.TickHours * 2);
        Assert.That(WorldPopulationService.IsPopulationDecision(state), Is.True, "Окно вмешательства.");
        Assert.That(WorldPopulationService.PendingContact(state).Kind, Is.EqualTo(PopulationContactState.ConflictHelp));
        Assert.That(ExpeditionDecisionSystem.TryApplyChoice(state, WorldPopulationService.OptionA, out _), Is.True);
        PopulationContactState contact = WorldPopulationService.BattleToStart(state);
        CampaignBattleRequest request = WorldPopulationService.CreateBattleRequest(state, contact);
        WorldPopulationService.MarkBattleStarted(state, contact);
        Hours(state, WorldPopulationRules.Current.ConflictWindowHours * 3);
        Assert.That(conflict.Status, Is.EqualTo(PopulationConflictState.Open), "Пока идёт бой героя, фон не решает тот же конфликт.");

        CampaignBattleBridge.ApplyResult(state, new CampaignBattleResult
        {
            BattleId = request.BattleId,
            Outcome = CampaignBattleOutcome.Victory,
            Enemies = band.Members.Select(m => new CampaignBattleEnemyRecord { InstanceId = m.MemberId, UnitTypeId = m.UnitTypeId, Defeated = true }).ToList()
        }, null);
        Assert.That(conflict.Outcome, Is.EqualTo("rescued"));
        Assert.That(caravan.IsActive, Is.True, "Обоз не гибнет второй раз от фонового расчёта.");
        Hours(state, 10);
        Assert.That(caravan.IsActive, Is.True);
        Assert.That(caravan.Outcome, Is.EqualTo("rescued"));
    }

    // ------------------------------------------------------------------
    // Сохранения и старые партии

    private static GameState RoundTrip(GameState state)
    {
        CampaignSaveData data = CampaignSaveService.ExportCampaign(state);
        string json = JsonUtility.ToJson(data);
        return CampaignSaveService.RestoreCampaign(JsonUtility.FromJson<CampaignSaveData>(json));
    }

    [Test]
    public void SaveLoad_KeepsPositionsActivitiesAndDeaths()
    {
        GeneratedWorldMap map = FindMap(m => m.Actors.Count(a => a.Role == WorldMapGroupRule.Beast) >= 2);
        GameState state = NewState(map);
        PlaceHero(state, 0f, 0f);
        Hours(state, 5.3);
        List<PopulationGroupState> beasts = state.Population.Groups.Where(g => g.Role == WorldMapGroupRule.Beast).ToList();
        beasts[0].Activity = PopulationGroupState.Wait;
        beasts[0].ActivityUntilHour = ContinuousSimulationSystem.GetAbsoluteHours(state) + 3;
        typeof(WorldPopulationService).GetMethod("Destroy", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
            .Invoke(null, new object[] { state, beasts[1], ContinuousSimulationSystem.GetAbsoluteHours(state), "hero" });

        GameState loaded = RoundTrip(state);
        Assert.That(loaded.Population.Groups.Select(g => g.GroupId + "|" + g.Status + "|" + g.Activity + "|" + g.X.ToString("0.0000")),
            Is.EqualTo(state.Population.Groups.Select(g => g.GroupId + "|" + g.Status + "|" + g.Activity + "|" + g.X.ToString("0.0000"))));
        Assert.That(loaded.Population.Legacy, Is.False);
        GameState again = RoundTrip(loaded);
        Assert.That(again.Population.Groups.Count, Is.EqualTo(state.Population.Groups.Count), "Повторная загрузка не добавляет групп.");
        Assert.That(again.Population.Groups.Single(g => g.GroupId == beasts[1].GroupId).IsActive, Is.False, "Разгромленная не воскресает.");
    }

    [Test]
    public void SaveLoad_StartedBattleIsNotLost_AndNotRepeatedAfterApply()
    {
        (GameState state, PopulationGroupState beast) = ContactWithBeast();
        ExpeditionDecisionSystem.TryApplyChoice(state, WorldPopulationService.OptionA, out _);
        PopulationContactState contact = WorldPopulationService.BattleToStart(state);
        WorldPopulationService.MarkBattleStarted(state, contact);
        GameState loaded = RoundTrip(state);
        Assert.That(WorldPopulationService.BattleToStart(loaded), Is.Not.Null, "Не вернувшийся бой снова ждёт начала.");

        CampaignBattleRequest request = WorldPopulationService.CreateBattleRequest(state, contact);
        CampaignBattleBridge.ApplyResult(state, new CampaignBattleResult { BattleId = request.BattleId, Outcome = CampaignBattleOutcome.Retreat }, null);
        GameState after = RoundTrip(state);
        Assert.That(WorldPopulationService.BattleToStart(after), Is.Null, "Применённый бой не запускается повторно.");
    }

    [Test]
    public void LegacyParty_WrapsExistingGroups_WithoutNewPopulationOrAmbush()
    {
        WorldMapGenerationSettings old = Settings();
        old.Population.Enabled = false;
        GeneratedWorldMap map = WorldMapGenerator.Generate(old, 16);
        foreach (GeneratedMapActor a in map.Actors)
        {
            a.Members.Clear();
            a.Behavior = string.Empty;
        }
        map.PopulationSources.Clear();
        int actorsBefore = map.Actors.Count;
        GameState state = NewState(map);
        Assert.That(state.Population.Legacy, Is.True);
        Assert.That(map.Actors.Count, Is.EqualTo(actorsBefore), "Старой партии новые звери не добавляются.");
        PopulationGroupState band = state.Population.Groups.FirstOrDefault(g => g.Role == WorldMapGroupRule.Band);
        if (band != null)
        {
            Assert.That(band.Members.Count, Is.GreaterThan(0), "Состав назначен по роли.");
            Assert.That(band.CalmUntilHour, Is.GreaterThan(ContinuousSimulationSystem.GetAbsoluteHours(state)), "Без внезапной засады после загрузки.");
        }
        int groups = state.Population.Groups.Count;
        GameState loaded = RoundTrip(state);
        Assert.That(loaded.Population.Groups.Count, Is.EqualTo(groups), "Миграция идемпотентна.");
    }

    // ------------------------------------------------------------------
    // Долгая жизнь региона без героя

    [Test]
    public void ThirtyDays_WithoutHero_StaysFiniteAndAlive([Values(17, 18, 19)] int seed)
    {
        GeneratedWorldMap map = WorldMapGenerator.Generate(Settings(), seed);
        GameState state = NewState(map);
        state.ActiveExpedition = null;
        int initial = state.Population.Groups.Count;
        int caravansAtStart = state.Population.Groups.Count(g => g.Role == WorldMapGroupRule.Caravan && g.IsActive);
        Stopwatch watch = Stopwatch.StartNew();
        Hours(state, 72);
        long ms72 = watch.ElapsedMilliseconds;
        int conflicts72 = state.Population.Conflicts.Count;
        Hours(state, 24 * 27);
        long ms30 = watch.ElapsedMilliseconds;
        double now = ContinuousSimulationSystem.GetAbsoluteHours(state);
        int reserveSum = map.PopulationSources.Where(s => s.Reserve > 0).Sum(s => s.Reserve);
        int villageSpawns = state.Population.Sources.Where(s => s.Kind == GeneratedPopulationSource.Village).Sum(s => s.Spawned);
        Assert.That(state.Population.Groups.Count, Is.LessThanOrEqualTo(initial + reserveSum + villageSpawns), "Пополнение конечно.");
        Assert.That(state.Population.Groups.Count(g => g.IsActive && g.Role == WorldMapGroupRule.Band), Is.LessThanOrEqualTo(
            map.PopulationSources.Count(s => s.Kind == GeneratedPopulationSource.BandCamp)), "Без роста банд.");
        Assert.That(state.Population.Groups.Where(g => g.IsActive && g.Activity == PopulationGroupState.Chase)
            .All(g => now - g.ActivityStartHour <= WorldPopulationRules.Current.ChaseMaxHours + 0.1), Is.True, "Нет вечных погонь.");
        Assert.That(state.Population.Conflicts.All(c => c.Status == PopulationConflictState.Resolved || now - c.StartHour < 3), Is.True);
        int caravansAtEnd = state.Population.Groups.Count(g => g.Role == WorldMapGroupRule.Caravan && g.IsActive);
        Debug.Log($"Население без героя, seed {seed}: групп в начале {initial}, в конце активных {state.Population.Groups.Count(g => g.IsActive)} " +
                  $"(всего записей {state.Population.Groups.Count}); конфликтов за 72 ч — {conflicts72}, за 30 сут — {state.Population.Conflicts.Count}; " +
                  $"обозов {caravansAtStart} → {caravansAtEnd}; замен из деревень {villageSpawns}. Время: 72 ч — {ms72} мс, 30 сут — {ms30} мс.");
        if (caravansAtStart > 0)
            Assert.That(caravansAtEnd + state.Population.Sources.Count(s => s.Kind == GeneratedPopulationSource.Village && s.NextRefillHour > now), Is.GreaterThan(0),
                "Мирное движение не исчезает целиком.");
    }
}

using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

// 12У-D: структура региона генератора — районы, причины и семейства деревень,
// бюджет открытий, связанность, восстановление обязательных мест, проверки.
public sealed class WorldMapRegionStructureTests
{
    private static WorldMapGenerationSettings Settings(WorldMapRegionKind kind = WorldMapRegionKind.Mixed)
    {
        WorldMapGenerationSettings s = WorldMapGenerationSettings.CreateDefault();
        s.RegionKind = kind;
        return s;
    }

    [Test]
    public void SameKey_GivesTheSameStructure()
    {
        WorldMapGenerationSettings s = Settings(WorldMapRegionKind.BySeed);
        GeneratedWorldMap a = WorldMapGenerator.Generate(s, 31);
        GeneratedWorldMap b = WorldMapGenerator.Generate(s, 31);
        Assert.That(b.DistrictCells, Is.EqualTo(a.DistrictCells));
        Assert.That(b.Passport.Fingerprint, Is.EqualTo(a.Passport.Fingerprint));
        Assert.That(b.TerrainCells, Is.EqualTo(a.TerrainCells));
        Assert.That(b.Report.Issues.Select(i => i.Code + i.Message), Is.EqualTo(a.Report.Issues.Select(i => i.Code + i.Message)));
        Assert.That(b.WorkSites.Select(w => w.Cell), Is.EqualTo(a.WorkSites.Select(w => w.Cell)));
    }

    [Test]
    public void DistrictsCoverEveryCell_AndVillagesExplainTheirPlace()
    {
        GeneratedWorldMap map = WorldMapGenerator.Generate(Settings(), 7);
        byte[] districts = WorldMapRegionValidator.DecodeDistricts(map);
        Assert.That(districts, Is.Not.Null);
        Assert.That(districts.Length, Is.EqualTo(map.CreateGrid().CellCount));
        Assert.That(districts.All(d => d > 0 && d <= map.Districts.Count), Is.True, "Каждая клетка принадлежит району.");
        Assert.That(map.Districts.Select(d => d.Id).Distinct().Count(), Is.EqualTo(map.Districts.Count));
        Assert.That(map.Districts.Select(d => d.Kind).Distinct().Count(), Is.GreaterThanOrEqualTo(4), "Районы разного характера.");
        foreach (GeneratedMapSite village in map.Villages)
        {
            Assert.That(village.Family, Is.Not.Empty);
            Assert.That(village.DistrictId, Is.Not.Empty);
        }
        Assert.That(map.Villages.Count(v => !string.IsNullOrEmpty(v.Reason)), Is.GreaterThan(map.Villages.Count / 2));
        Assert.That(map.Passport.Character, Is.Not.Empty);
        Assert.That(map.Passport.Excluded, Does.Contain("плавание"));
        foreach (GeneratedMapSite landmark in map.Landmarks)
            Assert.That(landmark.Signals, Is.Not.Null);
    }

    [TestCase(1)]
    [TestCase(5)]
    [TestCase(17)]
    [TestCase(123)]
    [TestCase(9001)]
    public void GeneratedMaps_HaveNoCorrectnessErrors(int seed)
    {
        foreach (WorldMapRegionKind kind in WorldMapRegionKinds.Concrete)
        {
            GeneratedWorldMap map = WorldMapGenerator.Generate(Settings(kind), seed);
            List<GeneratedRegionIssue> errors = map.Report.Issues.Where(i => i.Error).ToList();
            Assert.That(errors, Is.Empty, $"{kind} seed {seed}: " + string.Join("; ", errors.Select(e => e.Code + " " + e.Message)));
            Assert.That(map.Report.RequiredReachable, Is.EqualTo(1f));
        }
    }

    [Test]
    public void RegionKinds_GiveDifferentStructures()
    {
        HashSet<string> prints = new HashSet<string>();
        foreach (WorldMapRegionKind kind in WorldMapRegionKinds.Concrete)
            prints.Add(WorldMapGenerator.Generate(Settings(kind), 5).Passport.Fingerprint);
        Assert.That(prints.Count, Is.GreaterThanOrEqualTo(3));

        int valleyRivers = 0, lakeRivers = 0, hillUpland = 0, settledUpland = 0;
        for (int seed = 1; seed <= 4; seed++)
        {
            valleyRivers += WorldMapGenerator.Generate(Settings(WorldMapRegionKind.RiverValley), seed).Rivers.Count;
            lakeRivers += WorldMapGenerator.Generate(Settings(WorldMapRegionKind.ForestLake), seed).Rivers.Count;
            hillUpland += CountKind(WorldMapGenerator.Generate(Settings(WorldMapRegionKind.HillAndLedge), seed), GeneratedMapDistrict.Upland);
            settledUpland += CountKind(WorldMapGenerator.Generate(Settings(WorldMapRegionKind.SettledLand), seed), GeneratedMapDistrict.Upland);
        }
        Assert.That(valleyRivers, Is.GreaterThan(lakeRivers));
        Assert.That(hillUpland, Is.GreaterThan(settledUpland));
    }

    private static int CountKind(GeneratedWorldMap map, string kind) => map.Districts.Where(d => d.Kind == kind).Sum(d => d.CellCount);

    [Test]
    public void BySeed_VariesTheStructureAcrossSeeds()
    {
        List<GeneratedWorldMap> maps = new List<GeneratedWorldMap>();
        for (int seed = 1; seed <= 10; seed++)
            maps.Add(WorldMapGenerator.Generate(Settings(WorldMapRegionKind.BySeed), seed));
        Assert.That(maps.Select(m => m.Passport.Kind).Distinct().Count(), Is.GreaterThanOrEqualTo(2));
        Assert.That(WorldMapRegionValidator.Diversity(maps), Is.GreaterThanOrEqualTo(0.5f));
    }

    [Test]
    public void Discoveries_ScaleThePlaceBudget_AndKeepRequiredContent()
    {
        int Count(WorldMapDiscoveryRate rate)
        {
            int total = 0;
            for (int seed = 1; seed <= 3; seed++)
            {
                WorldMapGenerationSettings s = Settings();
                s.Discoveries = rate;
                GeneratedWorldMap map = WorldMapGenerator.Generate(s, seed);
                foreach (string id in map.Report.RequiredLocations)
                    Assert.That(map.Landmarks.Any(l => l.LocationId == id), Is.True, $"{rate}: нет обязательного места {id}");
                total += map.Landmarks.Count;
            }
            return total;
        }
        int rare = Count(WorldMapDiscoveryRate.Rare), normal = Count(WorldMapDiscoveryRate.Normal), rich = Count(WorldMapDiscoveryRate.Rich);
        Assert.That(rare, Is.LessThan(normal));
        Assert.That(rich, Is.GreaterThan(normal));
    }

    [Test]
    public void SmallerWorld_GetsAProportionalPlaceBudget()
    {
        WorldMapGenerationSettings big = Settings();
        WorldMapGenerationSettings small = Settings();
        small.HexesAcross = 50;
        Assert.That(WorldMapGenerator.Generate(small, 3).Landmarks.Count, Is.LessThan(WorldMapGenerator.Generate(big, 3).Landmarks.Count));
    }

    [Test]
    public void Connectivity_TradesBranchesForLoops()
    {
        int loopsLow = 0, loopsHigh = 0, workLow = 0, workHigh = 0;
        for (int seed = 1; seed <= 4; seed++)
        {
            WorldMapGenerationSettings low = Settings();
            low.RouteConnectivity = 0f;
            WorldMapGenerationSettings high = Settings();
            high.RouteConnectivity = 1f;
            GeneratedWorldMap a = WorldMapGenerator.Generate(low, seed), b = WorldMapGenerator.Generate(high, seed);
            loopsLow += a.Report.Loops;
            loopsHigh += b.Report.Loops;
            workLow += a.Report.WorkTrails;
            workHigh += b.Report.WorkTrails;
        }
        Assert.That(loopsLow, Is.EqualTo(0), "Без связанности петель между деревнями и обходов нет.");
        Assert.That(loopsHigh, Is.GreaterThan(loopsLow));
        Assert.That(workLow, Is.GreaterThan(workHigh), "Низкая связанность — больше хозяйственных ответвлений.");
    }

    [Test]
    public void WorkTrails_LeadToAWorkSiteThatMatchesTheVillage()
    {
        GeneratedWorldMap map = WorldMapGenerator.Generate(Settings(), 9);
        Assert.That(map.WorkSites, Is.Not.Empty);
        WorldMapTerrainLayer layer = map.CreateTerrainLayer();
        foreach (GeneratedMapSite site in map.WorkSites)
        {
            Assert.That(site.Kind, Does.StartWith("work-"));
            Assert.That(WorldMapMovementRules.CreateDefault().IsTraversable(layer.GetAtIndex(site.Cell)), Is.True);
        }
    }

    [Test]
    public void RequiredPlace_IsRepairedWithinTheBudget_WhenItsTerrainIsMissing()
    {
        WorldMapGenerationSettings s = Settings();
        s.HillsShare = 0f;
        s.MountainsShare = 0f;
        GeneratedWorldMap map = WorldMapGenerator.Generate(s, 4);
        Assert.That(map.Landmarks.Any(l => l.LocationId == "mine"), Is.True, "Шахта с содержанием должна встать хотя бы с ослабленными условиями.");
        Assert.That(map.Report.Issues.Any(i => i.Code == "R06" && i.Message.Contains("mine")), Is.True, "Автор видит, что условия ослаблены.");
        Assert.That(map.Report.RepairAttempts, Is.InRange(1, 8 * s.Landmarks.Count));
    }

    [Test]
    public void Validator_NamesTheObjectAndTheReason()
    {
        GeneratedWorldMap map = WorldMapGenerator.Generate(Settings(), 12);
        WorldMapTerrainLayer layer = map.CreateTerrainLayer();
        int water = Enumerable.Range(0, layer.Grid.CellCount).First(i => layer.GetAtIndex(i) == WorldMapGameplayTerrainType.Water);
        GeneratedMapSite landmark = map.Landmarks[0];
        landmark.Cell = water;
        List<GeneratedRegionIssue> issues = WorldMapRegionValidator.Validate(map);
        Assert.That(issues.Any(i => i.Error && i.Code == "H02" && i.Message.Contains(landmark.Name)), Is.True);

        map.Report.RequiredLocations.Add("no-such-place");
        Assert.That(WorldMapRegionValidator.Validate(map).Any(i => i.Code == "H06" && i.Message.Contains("no-such-place")), Is.True);
    }

    [Test]
    public void OldMapWithoutStructure_IsReadSafely()
    {
        GeneratedWorldMap map = WorldMapGenerator.Generate(Settings(), 2);
        string json = JsonUtility.ToJson(map);
        // Прежнее сохранение: полей структуры нет.
        GeneratedWorldMap old = JsonUtility.FromJson<GeneratedWorldMap>(json.Replace("\"Passport\"", "\"PassportOld\"").Replace("\"DistrictCells\"", "\"DistrictCellsOld\"").Replace("\"Report\"", "\"ReportOld\"").Replace("\"Districts\"", "\"DistrictsOld\""));
        Assert.That(old.IsValid, Is.True);
        Assert.That(old.DistrictCells, Is.Empty);
        Assert.DoesNotThrow(() => WorldMapRegionValidator.Validate(old));
        Assert.DoesNotThrow(() => WorldMapRegionValidator.Fingerprint(old));
        Assert.That(old.TerrainCells, Is.EqualTo(map.TerrainCells));

        GeneratedWorldMap again = JsonUtility.FromJson<GeneratedWorldMap>(json);
        Assert.That(again.Passport.Fingerprint, Is.EqualTo(map.Passport.Fingerprint));
        Assert.That(again.WorkSites.Count, Is.EqualTo(map.WorkSites.Count));
        Assert.That(again.Villages[0].Family, Is.EqualTo(map.Villages[0].Family));
    }

    [Test]
    public void Features_StandWhereTheirRulesSay_AndAreNotGamePlaces()
    {
        GeneratedWorldMap map = WorldMapGenerator.Generate(Settings(WorldMapRegionKind.SettledLand), 8);
        Assert.That(map.Features.Count, Is.GreaterThanOrEqualTo(6), "Детали мест расставлены.");
        WorldMapTerrainLayer layer = map.CreateTerrainLayer();
        WorldMapHexGrid grid = layer.Grid;
        HashSet<int> river = new HashSet<int>(map.RiverCells);
        foreach (GeneratedMapSite f in map.Features)
        {
            Assert.That(f.Visual, Is.Not.Empty);
            WorldMapGameplayTerrainType t = layer.GetAtIndex(f.Cell);
            if (f.Kind == "beaver-dam")
                Assert.That(river.Contains(f.Cell), Is.True, "Запруда — на русле реки.");
            else
                Assert.That(t, Is.Not.EqualTo(WorldMapGameplayTerrainType.Water).And.Not.EqualTo(WorldMapGameplayTerrainType.Road).And.Not.EqualTo(WorldMapGameplayTerrainType.Field));
            Assert.That(map.Villages.All(v => grid.CellAt(v.Cell).DistanceTo(grid.CellAt(f.Cell)) >= 2), Is.True, f.Name + " не внутри деревни");
            if (f.Kind == "fishing-nets")
                Assert.That(Enumerable.Range(0, 6).Any(d => { WorldMapHexCell n = grid.CellAt(f.Cell).Neighbor(d); return grid.IsInside(n) && river.Contains(grid.IndexOf(n)); }), Is.True, "Сети — у реки.");
        }
        // Детали не становятся местами игры и слотами.
        Assert.That(map.SpawnSlots.Any(slot => map.Features.Any(f => slot.Id.StartsWith(f.Kind))), Is.False);
        Assert.That(map.Landmarks.Any(l => map.Features.Any(f => f.Kind == l.Kind)), Is.False);
    }

    // Составные места пакета POI: грот и оползень — у крутизны (скалы/горы),
    // оползень ещё и у дороги; запруда — на русле реки, не у переправы;
    // кладбище — отдельные могилы (несколько рисунков одного места).
    [Test]
    public void CompositePlaces_StandWhereTheirRulesSay()
    {
        Dictionary<string, int> seen = new Dictionary<string, int>();
        for (int seed = 1; seed <= 10; seed++)
        {
            GeneratedWorldMap map = WorldMapGenerator.Generate(Settings(WorldMapRegionKind.HillAndLedge), seed);
            WorldMapTerrainLayer layer = map.CreateTerrainLayer();
            WorldMapHexGrid grid = layer.Grid;
            HashSet<int> river = new HashSet<int>(map.RiverCells);
            bool Near(int cell, int radius, System.Func<WorldMapGameplayTerrainType, bool> test)
            {
                WorldMapHexCell c = grid.CellAt(cell);
                for (int i = 0; i < grid.CellCount; i++)
                    if (grid.CellAt(i).DistanceTo(c) <= radius && test(layer.GetAtIndex(i)))
                        return true;
                return false;
            }
            bool Steep(WorldMapGameplayTerrainType t) => t == WorldMapGameplayTerrainType.Cliffs || t == WorldMapGameplayTerrainType.Mountains;
            foreach (GeneratedMapSite f in map.Features)
            {
                seen[f.Kind] = seen.TryGetValue(f.Kind, out int n) ? n + 1 : 1;
                if (f.Kind == "dry-grotto")
                    Assert.That(Near(f.Cell, 1, Steep), Is.True, "Грот — под скалой.");
                if (f.Kind == "landslide")
                {
                    Assert.That(Near(f.Cell, 1, Steep), Is.True, "Оползень — под крутым склоном.");
                    Assert.That(Near(f.Cell, 2, t => t == WorldMapGameplayTerrainType.Road || t == WorldMapGameplayTerrainType.Trail), Is.True, "Оползень — у дороги.");
                    Assert.That(layer.GetAtIndex(f.Cell), Is.Not.EqualTo(WorldMapGameplayTerrainType.Road), "Оползень не перекрывает дорогу.");
                }
                if (f.Kind == "beaver-dam")
                    Assert.That(river.Contains(f.Cell), Is.True, "Запруда — на русле.");
                if (f.Kind == "cemetery")
                    Assert.That(f.Visual, Does.Contain("*"), "Кладбище — несколько отдельных могил.");
            }
        }
        Assert.That(seen.ContainsKey("beaver-dam") && seen.ContainsKey("cemetery"), Is.True,
            "За 10 карт встречаются запруда и кладбище: " + string.Join(", ", seen.Keys));
    }

    // Гайд исследования: прогон 30 seed всех типов региона — ни одной ошибки
    // (H01–H10), все карты разные; проверка при генерации совпадает с
    // проверкой готовой карты (рельеф записан до проверок).
    [Test]
    public void ThirtySeeds_HaveNoErrors_AndDiffer()
    {
        WorldMapRegionKind[] kinds = { WorldMapRegionKind.BySeed, WorldMapRegionKind.RiverValley, WorldMapRegionKind.ForestLake, WorldMapRegionKind.HillAndLedge, WorldMapRegionKind.SettledLand, WorldMapRegionKind.Mixed };
        HashSet<string> prints = new HashSet<string>();
        List<string> errors = new List<string>();
        for (int i = 0; i < 30; i++)
        {
            int seed = 1000 + i * 37;
            GeneratedWorldMap map = WorldMapGenerator.Generate(Settings(kinds[i % kinds.Length]), seed);
            prints.Add(map.Passport.Fingerprint);
            errors.AddRange(map.Report.Issues.Where(x => x.Error).Select(x => "seed " + seed + ": " + x.Code + " " + x.Message));
            Assert.That(WorldMapRegionValidator.Validate(map).Count(x => x.Error), Is.EqualTo(map.Report.Issues.Count(x => x.Error)), "seed " + seed);
        }
        Assert.That(errors, Is.Empty, string.Join("\n", errors));
        Assert.That(prints.Count, Is.EqualTo(30), "30 разных карт.");
    }

    [Test]
    public void UnknownNearWord_IsReportedAndTheRuleIsSkipped()
    {
        WorldMapGenerationSettings s = Settings();
        s.Features = new List<WorldMapLandmarkRule>
        {
            new WorldMapLandmarkRule { Id = "test-odd", Name = "Проба", Visual = "X", Count = 2, Near = new List<string> { "moonlight" } }
        };
        GeneratedWorldMap map = WorldMapGenerator.Generate(s, 3);
        Assert.That(map.Features.Where(f => f.Kind == "test-odd"), Is.Empty);
        Assert.That(map.Report.Issues.Any(i => i.Code == "W13" && i.Message.Contains("moonlight")), Is.True);
    }

    [Test]
    public void NewFeatureType_ComesFromDataWithoutCode()
    {
        WorldMapGenerationSettings s = Settings();
        s.Features.Add(new WorldMapLandmarkRule { Id = "test-well", Name = "Пробный колодец", Visual = "Well_Test", Count = 1, MinDistanceHexes = 2f, Hidden = false, Near = new List<string> { "road" }, Description = "Проба." });
        GeneratedWorldMap map = WorldMapGenerator.Generate(s, 3);
        Assert.That(map.Features.Any(f => f.Kind == "test-well" && f.Visual == "Well_Test"), Is.True);
    }

    [Test]
    public void Watermill_StandsOnARiverbank()
    {
        for (int seed = 1; seed <= 5; seed++)
        {
            GeneratedWorldMap map = WorldMapGenerator.Generate(Settings(WorldMapRegionKind.RiverValley), seed);
            WorldMapHexGrid grid = map.CreateGrid();
            HashSet<int> river = new HashSet<int>(map.RiverCells);
            foreach (GeneratedMapSite mill in map.Landmarks.Where(l => l.Kind == "watermill"))
            {
                Assert.That(mill.Visual, Is.EqualTo("Watermill_A_FrontLeft"));
                Assert.That(Enumerable.Range(0, 6).Any(d => { WorldMapHexCell n = grid.CellAt(mill.Cell).Neighbor(d); return grid.IsInside(n) && river.Contains(grid.IndexOf(n)); }), Is.True, "Мельница — у проточной воды.");
            }
        }
    }

    [Test]
    public void Polities_ControlConnectedVillageGroups()
    {
        GeneratedWorldMap map = WorldMapGenerator.Generate(Settings(), 6);
        HashSet<string> owners = new HashSet<string>(map.Villages.Select(v => v.ControllerFactionId));
        Assert.That(owners, Is.SubsetOf(new[] { "draft_court_lands", "draft_river_union" }));
        Assert.That(owners.Count, Is.EqualTo(2), "Две рабочие державы делят деревни.");
        Assert.That(map.Villages[0].ControllerFactionId, Is.EqualTo("draft_court_lands"), "Старт — у первой державы.");

        WorldMapGenerationSettings none = Settings();
        none.ControllingPolities = 0;
        none.Groups.Clear();
        GeneratedWorldMap plain = WorldMapGenerator.Generate(none, 6);
        Assert.That(plain.Villages.All(v => string.IsNullOrEmpty(v.ControllerFactionId)), Is.True);
        Assert.That(plain.Actors, Is.Empty);
    }

    [Test]
    public void Groups_HaveStableIdentity_AndWalkRealRoads()
    {
        GeneratedWorldMap a = WorldMapGenerator.Generate(Settings(), 6);
        GeneratedWorldMap b = WorldMapGenerator.Generate(Settings(), 6);
        Assert.That(a.Actors.Select(x => x.ActorId), Is.EqualTo(b.Actors.Select(x => x.ActorId)));
        Assert.That(a.Actors.Select(x => x.ActorId).Distinct().Count(), Is.EqualTo(a.Actors.Count));
        Assert.That(a.Actors.Select(x => x.Role).Distinct().Count(), Is.EqualTo(4));
        WorldMapTerrainLayer layer = a.CreateTerrainLayer();
        WorldMapMovementRules rules = WorldMapMovementRules.CreateDefault();
        foreach (GeneratedMapActor actor in a.Actors.Where(x => x.State == "Travel"))
        {
            Assert.That(actor.RouteCells.Count, Is.GreaterThan(1), actor.ActorId);
            Assert.That(actor.RouteCells.All(c => rules.IsTraversable(layer.GetAtIndex(c))), Is.True, actor.ActorId + ": маршрут по проходимым клеткам");
            if (actor.Role != WorldMapGroupRule.Band)
                Assert.That(actor.RouteCells.Skip(1).All(c => layer.GetAtIndex(c) == WorldMapGameplayTerrainType.Road || layer.GetAtIndex(c) == WorldMapGameplayTerrainType.Trail), Is.True, actor.ActorId + ": по дорогам");
            // Положение — функция времени: одинаково при повторе, в пределах маршрута.
            MapPointData p1 = WorldMapActorMotion.PositionAt(actor, 37.5, layer.Grid, out _, out _, out _);
            MapPointData p2 = WorldMapActorMotion.PositionAt(actor, 37.5, layer.Grid, out _, out _, out _);
            Assert.That(p1.XPercent, Is.EqualTo(p2.XPercent));
            float minX = actor.Route.Min(r => r.XPercent) - 0.01f, maxX = actor.Route.Max(r => r.XPercent) + 0.01f;
            Assert.That(p1.XPercent, Is.InRange(minX, maxX));
        }
        GeneratedMapSite camp = a.Features.FirstOrDefault(f => f.Kind == "band-camp");
        GeneratedMapActor band = a.Actors.First(x => x.Role == WorldMapGroupRule.Band);
        if (band.State == "Travel")
            Assert.That(camp, Is.Not.Null, "У ватаги постоянный лагерь отдельно от людей.");
    }

    [Test]
    public void DirectionIndex_FollowsTheHeroConvention()
    {
        Assert.That(WorldMapActorMotion.DirectionIndex(0f, 1f), Is.EqualTo(0), "К зрителю — Front.");
        Assert.That(WorldMapActorMotion.DirectionIndex(0f, -1f), Is.EqualTo(3), "От зрителя — Back.");
        Assert.That(WorldMapActorMotion.DirectionIndex(1f, 0.6f), Is.Not.EqualTo(WorldMapActorMotion.DirectionIndex(-1f, 0.6f)), "Право и лево различаются, без зеркала.");
    }

    [Test]
    public void FactionDefinitions_AreValidated_AndExtendableByData()
    {
        List<WorldMapFactionDefinition> factions = WorldMapFactionCatalog.DefaultFactions();
        Assert.That(WorldMapFactionCatalog.Validate(factions, WorldMapFactionCatalog.DefaultGroups()).Where(i => i.Error), Is.Empty);
        factions.Add(new WorldMapFactionDefinition { Id = "test_loop_a", ParentId = "test_loop_b", Kind = WorldMapFactionDefinition.Organization });
        factions.Add(new WorldMapFactionDefinition { Id = "test_loop_b", ParentId = "test_loop_a", Kind = WorldMapFactionDefinition.Organization });
        factions.Add(new WorldMapFactionDefinition { Id = "test_orphan", ParentId = "nobody", Kind = WorldMapFactionDefinition.Company });
        List<GeneratedRegionIssue> issues = WorldMapFactionCatalog.Validate(factions, null);
        Assert.That(issues.Any(i => i.Code == "F02" && i.Message.Contains("цикл")), Is.True);
        Assert.That(issues.Any(i => i.Code == "F02" && i.Message.Contains("nobody")), Is.True);
        Assert.That(WorldMapFactionCatalog.TopPolity(factions, "draft_watch_court_branch")?.Id, Is.EqualTo("draft_court_lands"));
        Assert.That(WorldMapFactionCatalog.TopPolity(factions, "test_loop_a"), Is.Null);

        // Новая сторона и группа подключаются данными.
        WorldMapGenerationSettings s = Settings();
        s.Factions.Add(new WorldMapFactionDefinition { Id = "test_guild", Name = "Пробная артель", Kind = WorldMapFactionDefinition.Organization });
        s.Groups.Add(new WorldMapGroupRule { Id = "test-guild-master", Role = WorldMapGroupRule.Master, FactionId = "test_guild", FigureSet = "Wandering_Master", Name = "Пробный мастер" });
        GeneratedWorldMap map = WorldMapGenerator.Generate(s, 6);
        Assert.That(map.Actors.Any(x => x.ActorId == "test-guild-master-1" && x.FactionId == "test_guild"), Is.True);
    }
}

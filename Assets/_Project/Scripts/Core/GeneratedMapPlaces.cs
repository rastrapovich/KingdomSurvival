using System;
using System.Collections.Generic;

// Канон v1.58 §9.9.1: деревни и приметные места сгенерированной карты —
// настоящие места карты игры. К ним приходят, их исследуют и с ними
// взаимодействуют обычным входом в место (описание, исследование, итог,
// сцена режима). Место, связанное с записью Базы карты (LocationId правила),
// уже поставлено расстановкой в слот своего знака — оно получает тип места,
// а знак второй раз не дублируется. Локальная карта (вход внутрь) — у
// связанной записи Базы карты: её WorldLocationId в Базе локаций.
public static class GeneratedMapPlaces
{
    public const string IdPrefix = "gen-";
    public const string VillageKind = GeneratedMapSite.VillageKind;

    public static void AddTo(GameState state)
    {
        if (state == null || !state.HasGeneratedMap)
            return;
        GeneratedWorldMap map = state.GeneratedMap;
        state.Locations ??= new List<LocationData>();

        WorldMapHexGrid grid = map.CreateGrid();
        HashSet<GeneratedMapSite> hosted = new HashSet<GeneratedMapSite>();
        foreach (GeneratedMapSite site in map.Landmarks)
        {
            if (site == null || string.IsNullOrWhiteSpace(site.LocationId))
                continue;
            LocationData linked = state.FindLocation(site.LocationId);
            if (linked == null || !string.IsNullOrEmpty(linked.PlaceKind))
                continue;
            GeneratedMapSite nearest = NearestSite(map.Landmarks, site.LocationId, linked, grid, hosted);
            if (nearest == null)
                continue;
            hosted.Add(nearest);
            linked.PlaceKind = nearest.Kind;
        }

        List<WorldMapLocationTemplateData> templates = new List<WorldMapLocationTemplateData>();
        Dictionary<string, string> kinds = new Dictionary<string, string>();
        Dictionary<string, int> numbers = new Dictionary<string, int>();

        foreach (GeneratedMapSite village in map.Villages)
        {
            if (village == null || village.IsStart)
                continue;
            string id = NextId(VillageKind, numbers);
            kinds[id] = VillageKind;
            templates.Add(new WorldMapLocationTemplateData
            {
                Id = id,
                Name = "Деревня",
                InteractionDescription = "Несколько срубов за плетнём, над крышами стелется дым.",
                Threat = "низкая",
                ExplorationHours = 0.0,
                InitiallyDiscovered = true,
                InitiallyVisibleOnMap = true,
                Mode = WorldMapPlacementMode.Fixed,
                FixedXPercent = village.XPercent,
                FixedYPercent = village.YPercent
            });
        }

        foreach (GeneratedMapSite site in map.Landmarks)
        {
            if (site == null || hosted.Contains(site))
                continue;
            string id = NextId(site.Kind, numbers);
            kinds[id] = site.Kind;
            templates.Add(new WorldMapLocationTemplateData
            {
                Id = id,
                Name = string.IsNullOrWhiteSpace(site.Name) ? "Приметное место" : site.Name,
                InteractionDescription = site.Description ?? string.Empty,
                Threat = string.IsNullOrWhiteSpace(site.Threat) ? "неизвестна" : site.Threat,
                ExplorationHours = site.ExplorationHours,
                RewardArmySupply = site.RewardArmySupply,
                RewardArmyGold = site.RewardArmyGold,
                InitiallyDiscovered = !site.Hidden,
                InitiallyVisibleOnMap = !site.Hidden,
                Mode = WorldMapPlacementMode.Fixed,
                FixedXPercent = site.XPercent,
                FixedYPercent = site.YPercent
            });
        }

        foreach (LocationData location in WorldMapPopulationService.Populate(state.WorldSeed, templates))
        {
            if (kinds.TryGetValue(location.Id, out string kind))
                location.PlaceKind = kind ?? string.Empty;
            // 12Т: деревня генератора — посещаемое поселение (вход, лагерь,
            // экран поселения). Поток случайных чисел генератора не меняется.
            location.IsSettlement = location.PlaceKind == VillageKind;
            state.Locations.Add(location);
        }
    }

    private static string NextId(string kind, Dictionary<string, int> numbers)
    {
        string safeKind = string.IsNullOrWhiteSpace(kind) ? "place" : kind.Trim();
        numbers.TryGetValue(safeKind, out int number);
        numbers[safeKind] = number + 1;
        return IdPrefix + safeKind + "-" + (number + 1);
    }

    private static GeneratedMapSite NearestSite(
        List<GeneratedMapSite> sites, string locationId, LocationData location, WorldMapHexGrid grid, HashSet<GeneratedMapSite> hosted)
    {
        GeneratedMapSite best = null;
        double bestDistance = double.MaxValue;
        foreach (GeneratedMapSite site in sites)
        {
            if (site == null || hosted.Contains(site) || site.LocationId != locationId)
                continue;
            double distance = grid.DistanceHexes(site.XPercent, site.YPercent, location.MapXPercent, location.MapYPercent);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = site;
            }
        }
        return best;
    }
}

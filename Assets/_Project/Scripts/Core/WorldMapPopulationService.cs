using System;
using System.Collections.Generic;

// AM-04 (канон v1.33, §9.9): наполнение новой партии вынесено из
// GameState.CreateNewGame в отдельный чистый сервис — GameState по-прежнему
// создаёт кампанию и вызывает этот сервис, но не отвечает за расстановку.
//
// Последовательность (раздел 10 инструкции по миграции):
//   создать обязательные Fixed-места -> отобрать слоты для Anchored ->
//   разместить -> вернуть итог. Temporary-локации не входят в стартовое
//   наполнение (появляются через зоны Encounter, AM-08 — ещё не реализовано).
public static class WorldMapPopulationService
{
    public static List<LocationData> Populate(
        int worldSeed,
        IReadOnlyList<WorldMapLocationTemplateData> templates,
        IReadOnlyList<WorldMapSpawnSlotDefinition> slots = null)
    {
        List<LocationData> result = new List<LocationData>();
        if (templates == null)
            return result;

        // AM-07.5: авторские слоты мира (когда художник их задаст) вместо
        // захардкоженного WorldMapSpawnSlotRegistry — тот остаётся только
        // запасным вариантом для миров без собственных слотов.
        IReadOnlyList<WorldMapSpawnSlotDefinition> effectiveSlots =
            slots != null && slots.Count > 0
                ? slots
                : WorldMapSpawnSlotRegistry.StartingLocationSlots;

        List<WorldMapLocationTemplateData> fixedTemplates = new List<WorldMapLocationTemplateData>();
        List<WorldMapLocationTemplateData> anchoredTemplates = new List<WorldMapLocationTemplateData>();

        foreach (WorldMapLocationTemplateData template in templates)
        {
            if (template == null || string.IsNullOrWhiteSpace(template.Id))
                continue;

            switch (template.Mode)
            {
                case WorldMapPlacementMode.Fixed:
                    fixedTemplates.Add(template);
                    break;
                case WorldMapPlacementMode.Temporary:
                    // Не участвует в стартовом наполнении — см. комментарий выше.
                    break;
                default:
                    anchoredTemplates.Add(template);
                    break;
            }
        }

        // WM-08: отдельный поток случайности для расстановки локаций,
        // производный от WorldSeed, но не сам WorldSeed напрямую.
        Random locationRandom = new Random(DeriveStreamSeed(worldSeed, "location"));

        // Стабильный порядок перед перемешиванием: сортировка по Id, а не
        // порядок исходного списка/Dictionary — иначе визуальный штамп или
        // изменение порядка в базе данных художника незаметно сдвинет
        // расстановку, ломая воспроизводимость по seed (раздел 10 инструкции).
        anchoredTemplates.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
        ShuffleLocations(anchoredTemplates, locationRandom);

        foreach (WorldMapLocationTemplateData template in fixedTemplates)
            result.Add(PlaceFixed(template));

        for (int i = 0; i < anchoredTemplates.Count; i++)
        {
            WorldMapLocationTemplateData template = anchoredTemplates[i];
            WorldMapSpawnSlotDefinition slot =
                ResolveSlotForTemplate(template, effectiveSlots, i, locationRandom);

            result.Add(PlaceAnchored(template, slot, i, locationRandom));
        }

        return result;
    }

    // AM-07.5: конкретный именованный слот побеждает; иначе, если локация
    // требует теги — случайный выбор среди слотов, содержащих ВСЕ требуемые
    // теги (не round-robin по индексу — у разных локаций разные по размеру
    // совместимые множества); иначе — прежнее поведение round-robin по всем
    // слотам без изменений (обратная совместимость).
    private static WorldMapSpawnSlotDefinition ResolveSlotForTemplate(
        WorldMapLocationTemplateData template,
        IReadOnlyList<WorldMapSpawnSlotDefinition> slots,
        int index,
        Random random)
    {
        if (!string.IsNullOrWhiteSpace(template.SpawnSlotId))
        {
            WorldMapSpawnSlotDefinition named = FindSlotById(slots, template.SpawnSlotId);
            if (named != null)
                return named;
        }

        if (template.RequiredSlotTags != null && template.RequiredSlotTags.Count > 0)
        {
            List<WorldMapSpawnSlotDefinition> matches = new List<WorldMapSpawnSlotDefinition>();
            if (slots != null)
            {
                foreach (WorldMapSpawnSlotDefinition slot in slots)
                {
                    if (slot != null && slot.HasAllTags(template.RequiredSlotTags))
                        matches.Add(slot);
                }
            }

            // Нет ни одного совместимого слота — не подменяем требование
            // случайным несовместимым слотом молча; PlaceAnchored отправит
            // локацию к Дому как честный, заметный запасной вариант.
            return matches.Count > 0 ? matches[random.Next(matches.Count)] : null;
        }

        if (slots == null || slots.Count == 0)
            return null;

        return slots[index % slots.Count];
    }

    private static WorldMapSpawnSlotDefinition FindSlotById(
        IReadOnlyList<WorldMapSpawnSlotDefinition> slots,
        string id)
    {
        if (slots == null)
            return null;

        foreach (WorldMapSpawnSlotDefinition slot in slots)
        {
            if (slot != null && slot.Id == id)
                return slot;
        }

        return null;
    }

    private static LocationData PlaceFixed(WorldMapLocationTemplateData template)
    {
        LocationData location = template.CreateRuntimeLocation();
        float x = WorldMapNavigation.ClampMapX(template.FixedXPercent);
        float y = WorldMapNavigation.ClampMapY(template.FixedYPercent);

        List<MapPointData> route = WorldMapNavigation.FindPath(
            WorldMapNavigation.CapitalXPercent,
            WorldMapNavigation.CapitalYPercent,
            x,
            y);

        WorldMapRegionDefinition region = WorldMapRegionRegistry.FindRegion(x, y);
        location.AssignToRegion(
            region.Id,
            region.Name,
            -1,
            x,
            y,
            ContinuousSimulationSystem.CalculateTravelHours(route));

        location.IsDiscovered = template.InitiallyDiscovered;
        location.IsVisibleOnMap = template.InitiallyVisibleOnMap;
        return location;
    }

    private static LocationData PlaceAnchored(
        WorldMapLocationTemplateData template,
        WorldMapSpawnSlotDefinition slot,
        int slotIndex,
        Random locationRandom)
    {
        LocationData location = template.CreateRuntimeLocation();

        float x;
        float y;
        if (slot != null)
        {
            x = slot.PickXPercent(locationRandom);
            y = slot.PickYPercent(locationRandom);
        }
        else
        {
            // Нет ни одного авторского слота — не должно происходить в
            // настоящей базе (Validate предупредит), но не должно и
            // швырять локацию в угол карты молча.
            x = WorldMapNavigation.CapitalXPercent;
            y = WorldMapNavigation.CapitalYPercent;
        }

        List<MapPointData> candidateRoute = WorldMapNavigation.FindPath(
            WorldMapNavigation.CapitalXPercent,
            WorldMapNavigation.CapitalYPercent,
            x,
            y);

        if (candidateRoute.Count > 0)
        {
            x = candidateRoute[candidateRoute.Count - 1].XPercent;
            y = candidateRoute[candidateRoute.Count - 1].YPercent;
        }

        // AM-04: RegionId раньше был "sector-" + индекс слота — техническая
        // метка, не совпадавшая с реальным регионом из GetRegionName/
        // WorldMapRegionRegistry. UI и фильтры Encounter по региону могли
        // считать одно и то же место принадлежащим разным регионам.
        // Теперь RegionId — это настоящий Id региона, где физически
        // оказалась точка.
        WorldMapRegionDefinition region = WorldMapRegionRegistry.FindRegion(x, y);
        location.AssignToRegion(
            region.Id,
            region.Name,
            slotIndex,
            x,
            y,
            ContinuousSimulationSystem.CalculateTravelHours(candidateRoute));

        location.IsDiscovered = template.InitiallyDiscovered;
        location.IsVisibleOnMap = template.InitiallyVisibleOnMap;
        return location;
    }

    private static void ShuffleLocations(
        List<WorldMapLocationTemplateData> locations,
        Random random)
    {
        for (int i = locations.Count - 1; i > 0; i--)
        {
            int swapIndex = random.Next(i + 1);
            WorldMapLocationTemplateData temporary = locations[i];
            locations[i] = locations[swapIndex];
            locations[swapIndex] = temporary;
        }
    }

    // WM-08: детерминированно производит отдельный сид под конкретный поток
    // случайности от общего WorldSeed — идентична приватной копии в
    // GameState (сознательно не обобщал в третье место ради переноса,
    // чтобы не трогать существующий числовой контракт GameState).
    private static int DeriveStreamSeed(int worldSeed, string streamTag)
    {
        unchecked
        {
            int hash = 17;
            hash = hash * 31 + worldSeed;

            foreach (char character in streamTag)
                hash = hash * 31 + character;

            return hash;
        }
    }
}

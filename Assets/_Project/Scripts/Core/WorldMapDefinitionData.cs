using System;
using System.Collections.Generic;

// AM-01 (канон v1.33, §9.9): чистый контракт авторского постоянного мира —
// без UnityEngine, чтобы KingdomSurvival.Core по-прежнему собирался с
// noEngineReferences=true. WorldMapWorldDefinitionAsset (WorldMapVisual)
// заполняет этот класс из авторских данных и передаёт его WorldMapNavigation
// через ConfigureFromDefinition. GeographyVersion — версия географии и
// размещения фиксированных объектов; версия арта хранится отдельно в теме и
// не влияет на совместимость сохранений.
[Serializable]
public sealed class WorldMapDefinitionData
{
    public string WorldDefinitionId = "default-world";
    public int GeographyVersion = 1;

    // Размер сетки первой миграции не увеличивается относительно текущего
    // WorldMapNavigation.GridWidth/GridHeight — поля здесь только для
    // проверки согласованности данных, а не для динамического ресайза.
    public int GridWidth = WorldMapNavigation.GridWidth;
    public int GridHeight = WorldMapNavigation.GridHeight;

    public string HomeLocationId = "home";
    public float HomeXPercent = WorldMapNavigation.CapitalXPercent;
    public float HomeYPercent = WorldMapNavigation.CapitalYPercent;

    // Река/озёра/берега больше не часть этого контракта: карта переходит на
    // вручную нарисованное полотно, где вода уже присутствует как арт, а не
    // как процедурная/авторская геометрия пути (см. решение "нарисованная
    // карта — источник истины"). Будущая невидимая ручная разметка River
    // Cells (после готового арта) — отдельная задача, не путь точек.
    public List<WorldMapRegionDefinition> Regions = new List<WorldMapRegionDefinition>();
    public List<WorldMapTerrainAreaData> TerrainAreas = new List<WorldMapTerrainAreaData>();
    public List<WorldMapSpawnSlotDefinition> SpawnSlots = new List<WorldMapSpawnSlotDefinition>();

    public bool IsValid =>
        !string.IsNullOrWhiteSpace(WorldDefinitionId) &&
        GridWidth == WorldMapNavigation.GridWidth &&
        GridHeight == WorldMapNavigation.GridHeight;
}

// Прямоугольная авторская область расчётной местности — заменяет случайную
// GenerateTerrain для конкретного WorldMapDefinitionData. Как и
// WorldMapRegionDefinition/WorldMapSpawnSlotDefinition, простой прямоугольник
// в процентах карты; полигоны/маски не нужны на этом этапе (см. §2 и §4
// инструкции по миграции). При равном Priority побеждает последняя область
// в списке — точная проверка конфликтов равных приоритетов относится к
// редактору (AM-03), не к рантайму.
[Serializable]
public sealed class WorldMapTerrainAreaData
{
    public string Id;
    public WorldMapTerrainType Terrain;
    public float MinXPercent;
    public float MaxXPercent;
    public float MinYPercent;
    public float MaxYPercent;
    public int Priority;

    public bool Contains(float xPercent, float yPercent) =>
        xPercent >= MinXPercent && xPercent < MaxXPercent &&
        yPercent >= MinYPercent && yPercent < MaxYPercent;
}

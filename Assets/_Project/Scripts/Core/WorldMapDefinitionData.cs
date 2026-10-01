using System;
using System.Collections.Generic;

// AM-01 (канон v1.33, §9.9): чистый контракт авторского постоянного мира —
// без UnityEngine, чтобы KingdomSurvival.Core по-прежнему собирался с
// noEngineReferences=true. WorldMapWorldDefinitionAsset (WorldMapVisual)
// заполняет этот класс из авторских данных и передаёт его WorldMapNavigation
// через ConfigureFromDefinition. GeographyVersion — версия географии и
// размещения фиксированных объектов; версия арта хранится отдельно в теме и
// не влияет на совместимость сохранений.
//
// 12И (канон v1.50 §9.9): геймплейная разметка — шестиугольная сетка на всё
// полотно, каждая клетка которой размечена типом местности. Прежние
// прямоугольные зоны Plains/Hills/Mountains и ломаные дороги отменены.
[Serializable]
public sealed class WorldMapDefinitionData
{
    public string WorldDefinitionId = "default-world";
    public int GeographyVersion = 1;

    public string HomeLocationId = "home";
    public float HomeXPercent = WorldMapNavigation.CapitalXPercent;
    public float HomeYPercent = WorldMapNavigation.CapitalYPercent;

    // Полотно карты в пикселях и размер сетки — клеток по ширине полотна.
    public float CanvasWidth = WorldMapHexGrid.DefaultCanvasWidth;
    public float CanvasHeight = WorldMapHexGrid.DefaultCanvasHeight;
    public int HexesAcross = WorldMapHexGrid.DefaultHexesAcross;

    // Разметка в компактной записи WorldMapTerrainLayer.Encode. Пусто — вся
    // карта открытая местность.
    public string TerrainCells = string.Empty;

    public List<WorldMapRegionDefinition> Regions = new List<WorldMapRegionDefinition>();
    public List<WorldMapSpawnSlotDefinition> SpawnSlots = new List<WorldMapSpawnSlotDefinition>();

    public bool IsValid =>
        !string.IsNullOrWhiteSpace(WorldDefinitionId) &&
        CanvasWidth > 1f &&
        CanvasHeight > 1f &&
        HexesAcross > 0;

    public WorldMapHexGrid CreateGrid() => new WorldMapHexGrid(CanvasWidth, CanvasHeight, HexesAcross);

    public WorldMapTerrainLayer CreateTerrainLayer() => WorldMapTerrainLayer.Decode(CreateGrid(), TerrainCells);
}

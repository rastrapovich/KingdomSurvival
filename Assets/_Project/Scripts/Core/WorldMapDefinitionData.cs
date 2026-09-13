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

    // Задача "пересобрать масштаб путешествия": сколько игровых часов
    // занимает пересечение одной обычной (OpenGround) клетки маршрута на
    // обычной скорости — балансировочное значение мира, а не константа кода
    // (раньше было жёстко зашито как "1 клетка = 24 часа" через
    // RealSecondsPerGameDay/GameHoursPerRealSecond, что не позволяло менять
    // масштаб путешествия без переписывания симуляции). Дороги и Hills/
    // Mountains изменяют итоговое время поверх этого базового значения — не
    // заменяют его. 4f — рабочий эталон, безопасный fallback для старых/
    // невалидных (<=0) значений см. ContinuousSimulationSystem.CellsPerGameHour.
    public float BaseTravelHoursPerCell = 4f;

    // Река/озёра/берега больше не часть этого контракта: карта переходит на
    // вручную нарисованное полотно, где вода уже присутствует как арт, а не
    // как процедурная/авторская геометрия пути (см. решение "нарисованная
    // карта — источник истины"). Будущая невидимая ручная разметка River
    // Cells (после готового арта) — отдельная задача, не путь точек.
    public List<WorldMapRegionDefinition> Regions = new List<WorldMapRegionDefinition>();
    public List<WorldMapTerrainAreaData> TerrainAreas = new List<WorldMapTerrainAreaData>();
    public List<WorldMapSpawnSlotDefinition> SpawnSlots = new List<WorldMapSpawnSlotDefinition>();

    // WM-T01/T02 (задача "gameplay-география дорог"): независимый слой от
    // TerrainAreas/WorldMapTerrainType выше — тот отвечает только за
    // стоимость пути в WorldMapNavigation.FindPath и не трогается. Пустые
    // списки — безопасное поведение по умолчанию (раздел 18 задачи):
    // Roads.Count == 0 → вся карта OpenGround, множитель 1.0, движение как
    // до этой задачи.
    public List<WorldMapGameplayTerrainSettings> GameplayTerrainSettings =
        new List<WorldMapGameplayTerrainSettings>();
    public List<WorldMapRoadDefinition> Roads = new List<WorldMapRoadDefinition>();

    public bool IsValid =>
        !string.IsNullOrWhiteSpace(WorldDefinitionId) &&
        GridWidth == WorldMapNavigation.GridWidth &&
        GridHeight == WorldMapNavigation.GridHeight;
}

// Прямоугольная авторская область над уже нарисованной художником картой —
// невидимая gameplay-разметка, а не источник визуала (AM-07.5, канон v1.35,
// §9.9). Простой прямоугольник в процентах карты, как и
// WorldMapRegionDefinition/WorldMapSpawnSlotDefinition; полигоны/маски не
// нужны на этом этапе. При равном Priority побеждает последняя область
// в списке — точная проверка конфликтов равных приоритетов относится к
// редактору (AM-03), не к рантайму.
[Serializable]
public sealed class WorldMapTerrainAreaData
{
    public string Id;

    // Terrain — единственное, что влияет на стоимость/скорость пути
    // (GetTerrainTravelCost/GetTerrainSpeedMultiplier). Ровно три класса —
    // Plains/Hills/Mountains, без изменений.
    public WorldMapTerrainType Terrain;

    // Контекстные теги — чисто описательные ("здесь на арте нарисован лес/
    // поле/берег/дорога"), НЕ создают новую стоимость движения и не
    // participate в GetTerrainTravelCost. Используются для подбора
    // совместимых Spawn Slot'ов (AM-07.5) и зон Encounter (AM-08).
    public List<string> Tags = new List<string>();

    public float MinXPercent;
    public float MaxXPercent;
    public float MinYPercent;
    public float MaxYPercent;
    public int Priority;

    public bool Contains(float xPercent, float yPercent) =>
        xPercent >= MinXPercent && xPercent < MaxXPercent &&
        yPercent >= MinYPercent && yPercent < MaxYPercent;
}

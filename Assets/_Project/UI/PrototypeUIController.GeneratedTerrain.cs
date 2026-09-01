using UnityEngine;
using UnityEngine.UIElements;

public partial class PrototypeUIController
{
    private bool generatedTerrainUiInitialized;
    private int renderedTerrainSeed = int.MinValue;
    private IVisualElementScheduledItem generatedTerrainPoll;
    private VisualElement generatedTerrainLayer;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void InitializeGeneratedTerrainRuntime()
    {
        PrototypeUIController controller =
            UnityEngine.Object.FindAnyObjectByType<PrototypeUIController>();
        if (controller == null)
            return;

        UIDocument document = controller.GetComponent<UIDocument>();
        if (document == null)
            return;

        document.rootVisualElement.schedule
            .Execute(controller.TryInitializeGeneratedTerrainUi)
            .ExecuteLater(120);
    }

    private void TryInitializeGeneratedTerrainUi()
    {
        if (generatedTerrainUiInitialized)
            return;

        if (interfaceRoot == null || worldMap == null || gameState == null)
        {
            UIDocument document = GetComponent<UIDocument>();
            if (document != null)
            {
                document.rootVisualElement.schedule
                    .Execute(TryInitializeGeneratedTerrainUi)
                    .ExecuteLater(50);
            }
            return;
        }

        EnsureGeneratedTerrainLayer();
        generatedTerrainPoll = interfaceRoot.schedule
            .Execute(TickGeneratedTerrainUi)
            .Every(50);
        generatedTerrainUiInitialized = true;
        TickGeneratedTerrainUi();
    }

    private void TickGeneratedTerrainUi()
    {
        if (gameState == null || worldMap == null)
            return;

        WorldMapNavigation.ConfigureTerrain(gameState.WorldSeed);
        EnsureGeneratedTerrainLayer();

        if (renderedTerrainSeed != gameState.WorldSeed)
        {
            RefreshLocationTravelEstimatesForTerrain();
            DrawGeneratedTerrain();
            renderedTerrainSeed = gameState.WorldSeed;
        }
        else if (generatedTerrainLayer.childCount == 0)
        {
            DrawGeneratedTerrain();
        }
    }

    private void EnsureGeneratedTerrainLayer()
    {
        if (worldMap == null)
            return;

        if (generatedTerrainLayer != null &&
            generatedTerrainLayer.parent == worldMap)
        {
            return;
        }

        if (generatedTerrainLayer != null)
            generatedTerrainLayer.RemoveFromHierarchy();

        generatedTerrainLayer = new VisualElement
        {
            name = "generated-terrain-layer",
            pickingMode = PickingMode.Ignore
        };
        generatedTerrainLayer.style.position = Position.Absolute;
        generatedTerrainLayer.style.left = 0f;
        generatedTerrainLayer.style.right = 0f;
        generatedTerrainLayer.style.top = 0f;
        generatedTerrainLayer.style.bottom = 0f;

        // Это отдельный постоянный слой. RefreshWorldMapPanel очищает старый
        // worldMapTerrain, но больше не затрагивает визуал холмов и гор.
        worldMap.Add(generatedTerrainLayer);
        generatedTerrainLayer.SendToBack();
    }

    private void RefreshLocationTravelEstimatesForTerrain()
    {
        if (gameState == null || gameState.Locations == null)
            return;

        foreach (LocationData location in gameState.Locations)
        {
            if (location == null || location.IsWaypoint)
                continue;

            var route = WorldMapNavigation.FindPath(
                WorldMapNavigation.CapitalXPercent,
                WorldMapNavigation.CapitalYPercent,
                location.MapXPercent,
                location.MapYPercent);
            location.TravelHoursFromCapital =
                ContinuousSimulationSystem.CalculateTravelHours(route);
        }
    }

    private void DrawGeneratedTerrain()
    {
        EnsureGeneratedTerrainLayer();
        if (generatedTerrainLayer == null)
            return;

        generatedTerrainLayer.Clear();
        generatedTerrainLayer.SendToBack();

        float cellWidth = 100f / WorldMapNavigation.GridWidth;
        float cellHeight = 100f / WorldMapNavigation.GridHeight;

        for (int y = 0; y < WorldMapNavigation.GridHeight; y++)
        {
            for (int x = 0; x < WorldMapNavigation.GridWidth; x++)
            {
                WorldMapTerrainType terrain =
                    WorldMapNavigation.GetTerrainAtGridCell(x, y);
                if (terrain == WorldMapTerrainType.Plains)
                    continue;

                VisualElement cell = new VisualElement();
                cell.pickingMode = PickingMode.Ignore;
                cell.style.position = Position.Absolute;
                cell.style.left = new Length(x * cellWidth, LengthUnit.Percent);
                cell.style.top = new Length(y * cellHeight, LengthUnit.Percent);
                cell.style.width = new Length(cellWidth, LengthUnit.Percent);
                cell.style.height = new Length(cellHeight, LengthUnit.Percent);
                cell.style.alignItems = Align.Center;
                cell.style.justifyContent = Justify.Center;

                Label symbol = new Label(
                    terrain == WorldMapTerrainType.Mountains ? "▲" : "⌃");
                symbol.pickingMode = PickingMode.Ignore;
                symbol.style.unityTextAlign = TextAnchor.MiddleCenter;
                symbol.style.fontSize =
                    terrain == WorldMapTerrainType.Mountains ? 16f : 14f;
                symbol.style.unityFontStyleAndWeight = FontStyle.Bold;

                if (terrain == WorldMapTerrainType.Mountains)
                {
                    cell.style.backgroundColor =
                        new Color(0.20f, 0.20f, 0.22f, 0.58f);
                    symbol.style.color =
                        new Color(0.62f, 0.61f, 0.59f, 0.92f);
                    cell.tooltip = "Горы · скорость отряда ×0,33";
                }
                else
                {
                    cell.style.backgroundColor =
                        new Color(0.31f, 0.27f, 0.18f, 0.46f);
                    symbol.style.color =
                        new Color(0.67f, 0.57f, 0.35f, 0.90f);
                    cell.tooltip = "Холмы · скорость отряда ×0,5";
                }

                cell.Add(symbol);
                generatedTerrainLayer.Add(cell);
            }
        }
    }
}

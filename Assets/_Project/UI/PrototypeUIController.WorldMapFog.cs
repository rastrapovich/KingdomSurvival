using KingdomSurvival.WorldMapVisual;
using UnityEngine;
using UnityEngine.UIElements;

// Бриф правок (19 пунктов) п. 1: рисунок тумана войны — одна маленькая
// текстура на весь слой «world-map-fog» (по пикселю на четверть клетки,
// с билинейным сглаживанием), растянутая на полотно карты. Пиксели
// перерисовываются, только когда ядро открыло новые клетки (версия
// WorldMapFog), и не чаще нескольких раз в секунду — не каждый кадр.
// У самой границы исследованного туман светлее (мягкий край). Слой лежит
// под маршрутами, областями поиска и метками: известное место видно поверх
// тумана, а неизвестное и так не имеет метки, подписи и подсказки.
public partial class PrototypeUIController
{
    private const int FogPixelsPerHex = 4;
    private const float FogMinRedrawSeconds = 0.25f;

    private Texture2D worldMapFogTexture;
    private Color32[] worldMapFogPixels;
    private int[] worldMapFogPixelCells;
    private byte[] worldMapFogCellAlpha;
    private GameState worldMapFogState;
    private int worldMapFogVersion = -1;
    private float worldMapFogTimer;
    private int worldMapFogVisiblePlaces = -1;

    private void TickWorldMapFog(bool force = false)
    {
        if (worldMapFog == null)
            return;
        if (gameState == null || !WorldMapFog.Applies(gameState))
        {
            if (worldMapFogState != null)
            {
                worldMapFog.style.backgroundImage = new StyleBackground(StyleKeyword.None);
                worldMapFogState = null;
            }
            return;
        }

        worldMapFogTimer += Time.unscaledDeltaTime;
        bool stale = !ReferenceEquals(worldMapFogState, gameState) || worldMapFogVersion != WorldMapFog.Version;
        if (!stale || (!force && worldMapFogTimer < FogMinRedrawSeconds && ReferenceEquals(worldMapFogState, gameState)))
            return;
        worldMapFogTimer = 0f;

        WorldMapFog.EnsureInitialized(gameState);
        WorldMapHexGrid grid = WorldMapFog.Grid(gameState);
        if (!ReferenceEquals(worldMapFogState, gameState) || worldMapFogPixelCells == null ||
            worldMapFogCellAlpha == null || worldMapFogCellAlpha.Length != grid.CellCount)
        {
            BuildWorldMapFogLookup(grid);
        }
        worldMapFogState = gameState;
        worldMapFogVersion = WorldMapFog.Version;
        RedrawWorldMapFog(grid);

        // Открытие клетки сделало место известным — метка появляется сразу.
        int visible = CountVisibleWorldMapPlaces();
        if (worldMapFogVisiblePlaces >= 0 && visible != worldMapFogVisiblePlaces)
        {
            worldMapFogVisiblePlaces = visible;
            RefreshWorldMapPanel();
            return;
        }
        worldMapFogVisiblePlaces = visible;
    }

    private int CountVisibleWorldMapPlaces()
    {
        int count = 0;
        if (gameState?.Locations == null)
            return count;
        foreach (LocationData location in gameState.Locations)
        {
            if (location != null && !location.IsWaypoint && location.IsVisibleOnMap)
                count++;
        }
        return count;
    }

    private void BuildWorldMapFogLookup(WorldMapHexGrid grid)
    {
        int width = Mathf.Max(8, grid.Columns * FogPixelsPerHex);
        int height = Mathf.Max(8, Mathf.RoundToInt(width * grid.CanvasHeight / Mathf.Max(1f, grid.CanvasWidth)));
        if (worldMapFogTexture == null || worldMapFogTexture.width != width || worldMapFogTexture.height != height)
        {
            if (worldMapFogTexture != null)
                Destroy(worldMapFogTexture);
            worldMapFogTexture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = "WorldMapFog",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave
            };
        }
        worldMapFogPixels = new Color32[width * height];
        worldMapFogPixelCells = new int[width * height];
        for (int y = 0; y < height; y++)
        {
            // Строка 0 текстуры — низ картинки.
            double canvasY = (height - 1 - y + 0.5) / height * grid.CanvasHeight;
            for (int x = 0; x < width; x++)
            {
                double canvasX = (x + 0.5) / width * grid.CanvasWidth;
                worldMapFogPixelCells[y * width + x] = grid.IndexOf(grid.CellAtPixel(canvasX, canvasY));
            }
        }
        worldMapFogCellAlpha = new byte[grid.CellCount];
        worldMapFog.style.backgroundImage = new StyleBackground(Background.FromTexture2D(worldMapFogTexture));
        worldMapFog.style.unityBackgroundImageTintColor = Color.white;
        worldMapFog.style.backgroundSize = new BackgroundSize(Length.Percent(100f), Length.Percent(100f));
    }

    private void RedrawWorldMapFog(WorldMapHexGrid grid)
    {
        WorldMapMovementSettingsAsset settings = WorldMapVisualRuntime.LoadMovementSettings();
        Color fogColor = settings != null ? settings.FogColor : new Color(0.055f, 0.06f, 0.07f, 0.86f);
        float softness = settings != null ? settings.FogEdgeSoftness : 0.45f;
        byte full = (byte)Mathf.RoundToInt(Mathf.Clamp01(fogColor.a) * 255f);
        byte edge = (byte)Mathf.RoundToInt(full * (1f - softness));

        for (int index = 0; index < worldMapFogCellAlpha.Length; index++)
        {
            if (WorldMapFog.IsExplored(gameState, index))
            {
                worldMapFogCellAlpha[index] = 0;
                continue;
            }
            WorldMapHexCell cell = grid.CellAt(index);
            bool border = false;
            for (int direction = 0; direction < 6 && !border; direction++)
            {
                WorldMapHexCell neighbor = cell.Neighbor(direction);
                border = grid.IsInside(neighbor) && WorldMapFog.IsExplored(gameState, grid.IndexOf(neighbor));
            }
            worldMapFogCellAlpha[index] = border ? edge : full;
        }

        Color32 rgb = fogColor;
        for (int i = 0; i < worldMapFogPixels.Length; i++)
        {
            int cell = worldMapFogPixelCells[i];
            worldMapFogPixels[i] = new Color32(rgb.r, rgb.g, rgb.b, worldMapFogCellAlpha[cell]);
        }
        worldMapFogTexture.SetPixels32(worldMapFogPixels);
        worldMapFogTexture.Apply(false, false);
        worldMapFog.MarkDirtyRepaint();
    }
}

using System.Collections.Generic;
using KingdomSurvival.MapLabBridge;
using KingdomSurvival.WorldMapVisual;
using UnityEngine;
using UnityEngine.UIElements;

// Облака тумана войны (19.11.2026, бриф «KingdomSurvival_Fog_Clouds_3_Styles»).
// Отдельный слой «world-map-fog-clouds» сразу над маской тумана (под
// маршрутами и метками, клики не ловит). Местность скрывает маска
// WorldMapFog — облака лишь придают границе объём, поэтому без рисунков или
// с выключенными облаками туман работает как раньше.
//
// Раскладка стабильна по seed карты (WorldMapFogClouds.Layout); облако
// рассеяно, если в радиусе запуска от него есть исследованная клетка, —
// своего состояния в сохранении нет, после загрузки и смены вида рассеянные
// облака не возвращаются. Путь героя между кадрами проверяется отрезком
// (быстрый ход и рывок кадра не оставляют пропусков); тронутое облако один
// раз отплывает от героя и тает, затем убирается из слоя. Проверяются только
// облака в корзинах пространственной сетки рядом с отрезком; полный проход —
// лишь при новом открытии тумана, не чаще нескольких раз в секунду.
// Рисунки облаков — окно «Генератор карты» → «Облака тумана войны»; числа — ассет
// настроек движения, раздел «Облака тумана войны».
public partial class PrototypeUIController
{
    private sealed class WorldMapFogCloud
    {
        public WorldMapFogCloudAnchor Anchor;
        public VisualElement Element;
        public float WidthPx;
        public float HeightPx;
        public bool Gone;
        public float Progress = -1f;
        public Vector2 Drift;
        // Тень облака на земле (слой под облаками); сдвиг при таянии — в
        // долях размера тени.
        public VisualElement Shadow;
        public Vector2 ShadowDrift;
    }

    // Поле тени вокруг силуэта (доля размера), чтобы размытие не резал край.
    private const float FogCloudShadowPad = 0.1f;
    private VisualElement worldMapFogCloudShadows;
    private readonly Dictionary<Texture2D, Texture2D> worldMapFogCloudShadowTextures = new Dictionary<Texture2D, Texture2D>();
    private string worldMapFogCloudShadowTexturesKey;
    private float worldMapFogCloudShadowLightFactor = 1f;

    private const float FogCloudsMaxSweepHexes = 8f;
    private const float FogCloudsMinCheckSeconds = 0.25f;

    private VisualElement worldMapFogClouds;
    private readonly List<WorldMapFogCloud> worldMapFogCloudList = new List<WorldMapFogCloud>();
    private readonly List<WorldMapFogCloud> worldMapFogCloudsDissolving = new List<WorldMapFogCloud>();
    private List<int>[] worldMapFogCloudBuckets;
    private int worldMapFogCloudBucketColumns;
    private int worldMapFogCloudBucketRows;
    private double worldMapFogCloudBucketSize;
    private GameState worldMapFogCloudsState;
    private GeneratedWorldMap worldMapFogCloudsMap;
    private string worldMapFogCloudsLayoutKey;
    private string worldMapFogCloudsTextureKey;
    private List<Texture2D> worldMapFogCloudTextures = new List<Texture2D>();
    private int worldMapFogCloudsVersion = -1;
    private float worldMapFogCloudsCheckTimer;
    private bool worldMapFogCloudsHeroValid;
    private Vector2 worldMapFogCloudsHeroPx;
    private string worldMapFogCloudsWarned;
    // Облака на месте и закрывают неизвестное — маску тумана можно не рисовать.
    private bool worldMapFogCloudsCovering;
    // Свет суток (PrototypeUIController.DayNight): множитель цвета рисунка
    // облаков, как у живой карты. Применяется к живым облакам только при
    // заметной смене (не каждый кадр), новые облака берут его сразу.
    private Color worldMapFogCloudsLight = Color.white;
    private const float FogCloudsLightStep = 0.01f;

    private void SetWorldMapFogCloudsLight(Color light)
    {
        light.a = 1f;
        if (Mathf.Abs(light.r - worldMapFogCloudsLight.r) < FogCloudsLightStep &&
            Mathf.Abs(light.g - worldMapFogCloudsLight.g) < FogCloudsLightStep &&
            Mathf.Abs(light.b - worldMapFogCloudsLight.b) < FogCloudsLightStep)
            return;
        worldMapFogCloudsLight = light;
        // Ночью тень слабее: доля по яркости света суток.
        float luminance = light.r * 0.299f + light.g * 0.587f + light.b * 0.114f;
        WorldMapMovementSettingsAsset lightSettings = WorldMapVisualRuntime.LoadMovementSettings();
        float night = lightSettings != null ? lightSettings.FogCloudShadowNight : 0.7f;
        worldMapFogCloudShadowLightFactor = Mathf.Lerp(night, 1f, Mathf.InverseLerp(0.35f, 1f, luminance));
        foreach (WorldMapFogCloud cloud in worldMapFogCloudList)
            if (!cloud.Gone && cloud.Element != null)
                cloud.Element.style.unityBackgroundImageTintColor = light;
    }

    private void TickWorldMapFogClouds()
    {
        if (worldMapFog == null)
            return;
        WorldMapMovementSettingsAsset settings = WorldMapVisualRuntime.LoadMovementSettings();
        bool enabled = settings == null || settings.FogCloudsEnabled;
        if (gameState == null || !enabled || !WorldMapFog.Applies(gameState))
        {
            ClearWorldMapFogClouds();
            return;
        }
        EnsureWorldMapFogCloudsLayer();
        if (worldMapFogClouds == null)
            return;

        WorldMapHexGrid grid = WorldMapFog.Grid(gameState);
        float trigger = settings != null ? settings.FogCloudTriggerRadiusHexes : 3f;
        string layoutKey = settings == null ? "-" :
            settings.FogCloudSpacingHexes + "|" + settings.FogCloudRowSpacingHexes + "|" + settings.FogCloudJitter + "|" + trigger + "|" +
            settings.FogCloudWidthHexes + "|" + settings.FogCloudSizeVariation;
        if (!ReferenceEquals(worldMapFogCloudsState, gameState) ||
            !ReferenceEquals(worldMapFogCloudsMap, gameState.GeneratedMap) ||
            worldMapFogCloudsLayoutKey != layoutKey)
        {
            WorldMapFog.EnsureInitialized(gameState);
            BuildWorldMapFogClouds(grid, settings, trigger);
            worldMapFogCloudsLayoutKey = layoutKey;
        }

        ApplyWorldMapFogCloudTextures(settings);
        float opacity = settings != null ? settings.FogCloudOpacity : 1f;
        worldMapFogClouds.style.opacity = opacity;
        worldMapFogClouds.style.display = worldMapFogCloudTextures.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
        if (worldMapFogCloudShadows != null)
        {
            bool shadows = settings == null || settings.FogCloudShadows;
            worldMapFogCloudShadows.style.display = shadows && worldMapFogCloudTextures.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            worldMapFogCloudShadows.style.opacity = opacity * (settings != null ? settings.FogCloudShadowOpacity : 0.9f) * worldMapFogCloudShadowLightFactor;
        }

        SweepWorldMapFogCloudsAlongHero(grid, settings, trigger);

        // Новое открытие тумана (путь, событие, загрузка) — проверить облака.
        worldMapFogCloudsCheckTimer += Time.unscaledDeltaTime;
        if (worldMapFogCloudsVersion != WorldMapFog.Version && worldMapFogCloudsCheckTimer >= FogCloudsMinCheckSeconds)
        {
            worldMapFogCloudsCheckTimer = 0f;
            worldMapFogCloudsVersion = WorldMapFog.Version;
            Vector2 hero = worldMapFogCloudsHeroValid ? worldMapFogCloudsHeroPx : Vector2.zero;
            CheckWorldMapFogCloudsAgainstExplored(grid, settings, trigger, hero);
        }

        AnimateWorldMapFogClouds(settings);
    }

    // Открытые клетки на момент прошлой проверки облаков.
    private bool[] worldMapFogCloudsExplored;
    private readonly List<int> worldMapFogCloudsNewCells = new List<int>();

    // Облако тает, когда в радиусе от его опоры появилась исследованная
    // клетка (WorldMapFogClouds.IsCleared). 24.11.2026: раньше на каждое
    // открытие проверялись все облака карты (5–27 мс за кадр); теперь —
    // только облака рядом с клетками, открытыми с прошлой проверки (итог тот
    // же: прежде открытые клетки уже растопили свои облака).
    private void CheckWorldMapFogCloudsAgainstExplored(WorldMapHexGrid grid, WorldMapMovementSettingsAsset settings, float trigger, Vector2 hero)
    {
        int cellCount = grid.CellCount;
        bool full = worldMapFogCloudsExplored == null || worldMapFogCloudsExplored.Length != cellCount || worldMapFogCloudBuckets == null;
        if (full)
            worldMapFogCloudsExplored = new bool[cellCount];
        worldMapFogCloudsNewCells.Clear();
        for (int index = 0; index < cellCount; index++)
        {
            bool explored = WorldMapFog.IsExplored(gameState, index);
            if (explored && !worldMapFogCloudsExplored[index])
                worldMapFogCloudsNewCells.Add(index);
            worldMapFogCloudsExplored[index] = explored;
        }

        // Первая проверка или большое разом открытое (загрузка, событие) — все облака.
        if (full || worldMapFogCloudsNewCells.Count > 512)
        {
            foreach (WorldMapFogCloud cloud in worldMapFogCloudList)
            {
                if (cloud.Gone || cloud.Progress >= 0f)
                    continue;
                if (WorldMapFogClouds.IsCleared(gameState, grid, cloud.Anchor.X, cloud.Anchor.Y, trigger))
                    StartWorldMapFogCloudDissolve(cloud, hero.x, hero.y, grid, settings);
            }
            return;
        }

        // Та же граница, что в IsCleared: центр клетки в радиусе + полклетки.
        double limit = System.Math.Max(0.0, trigger) * grid.HexWidth + grid.HexWidth * 0.5;
        double limitSquared = limit * limit;
        foreach (int index in worldMapFogCloudsNewCells)
        {
            grid.CellCenter(grid.CellAt(index), out double cx, out double cy);
            int bx0 = Mathf.Clamp((int)((cx - limit) / worldMapFogCloudBucketSize) - 1, 0, worldMapFogCloudBucketColumns - 1);
            int bx1 = Mathf.Clamp((int)((cx + limit) / worldMapFogCloudBucketSize) + 1, 0, worldMapFogCloudBucketColumns - 1);
            int by0 = Mathf.Clamp((int)((cy - limit) / worldMapFogCloudBucketSize) - 1, 0, worldMapFogCloudBucketRows - 1);
            int by1 = Mathf.Clamp((int)((cy + limit) / worldMapFogCloudBucketSize) + 1, 0, worldMapFogCloudBucketRows - 1);
            for (int by = by0; by <= by1; by++)
            {
                for (int bx = bx0; bx <= bx1; bx++)
                {
                    List<int> bucket = worldMapFogCloudBuckets[by * worldMapFogCloudBucketColumns + bx];
                    if (bucket == null)
                        continue;
                    foreach (int cloudIndex in bucket)
                    {
                        WorldMapFogCloud cloud = worldMapFogCloudList[cloudIndex];
                        if (cloud.Gone || cloud.Progress >= 0f)
                            continue;
                        double dx = cloud.Anchor.X - cx;
                        double dy = cloud.Anchor.Y - cy;
                        if (dx * dx + dy * dy <= limitSquared)
                            StartWorldMapFogCloudDissolve(cloud, hero.x, hero.y, grid, settings);
                    }
                }
            }
        }
    }

    private void EnsureWorldMapFogCloudsLayer()
    {
        if (worldMapFogClouds != null && worldMapFogClouds.parent != null)
            return;
        VisualElement parent = worldMapFog.parent;
        if (parent == null)
            return;
        worldMapFogClouds = new VisualElement { name = "world-map-fog-clouds", pickingMode = PickingMode.Ignore };
        worldMapFogClouds.AddToClassList("world-map-layer");
        parent.Insert(parent.IndexOf(worldMapFog) + 1, worldMapFogClouds);
        // Тени — сразу под облаками: видны только на земле, где облака нет.
        worldMapFogCloudShadows = new VisualElement { name = "world-map-fog-cloud-shadows", pickingMode = PickingMode.Ignore };
        worldMapFogCloudShadows.AddToClassList("world-map-layer");
        parent.Insert(parent.IndexOf(worldMapFogClouds), worldMapFogCloudShadows);
    }

    private void ClearWorldMapFogClouds()
    {
        if (worldMapFogCloudsState == null && worldMapFogCloudList.Count == 0)
            return;
        worldMapFogClouds?.Clear();
        if (worldMapFogClouds != null)
            worldMapFogClouds.style.display = DisplayStyle.None;
        worldMapFogCloudShadows?.Clear();
        if (worldMapFogCloudShadows != null)
            worldMapFogCloudShadows.style.display = DisplayStyle.None;
        worldMapFogCloudList.Clear();
        worldMapFogCloudsDissolving.Clear();
        worldMapFogCloudBuckets = null;
        worldMapFogCloudsExplored = null;
        worldMapFogCloudsState = null;
        worldMapFogCloudsMap = null;
        worldMapFogCloudsLayoutKey = null;
        worldMapFogCloudsTextureKey = null;
        worldMapFogCloudsHeroValid = false;
        worldMapFogCloudsCovering = false;
    }

    private void BuildWorldMapFogClouds(WorldMapHexGrid grid, WorldMapMovementSettingsAsset settings, float trigger)
    {
        worldMapFogClouds.Clear();
        worldMapFogCloudShadows?.Clear();
        worldMapFogCloudList.Clear();
        worldMapFogCloudsDissolving.Clear();
        worldMapFogCloudsState = gameState;
        worldMapFogCloudsMap = gameState.GeneratedMap;
        worldMapFogCloudsTextureKey = null;
        worldMapFogCloudsVersion = WorldMapFog.Version;
        worldMapFogCloudsHeroValid = false;
        // Облака у уже открытого не создаются — следующая проверка смотрит
        // только на клетки, открытые после этой сборки.
        worldMapFogCloudsExplored = new bool[grid.CellCount];
        for (int index = 0; index < grid.CellCount; index++)
            worldMapFogCloudsExplored[index] = WorldMapFog.IsExplored(gameState, index);

        float spacing = settings != null ? settings.FogCloudSpacingHexes : 4.5f;
        float rowSpacing = settings != null ? settings.FogCloudRowSpacingHexes : 1.2f;
        float jitter = settings != null ? settings.FogCloudJitter : 0.45f;
        float width = settings != null ? settings.FogCloudWidthHexes : 8f;
        float variation = settings != null ? settings.FogCloudSizeVariation : 0.25f;

        worldMapFogCloudBucketSize = Mathf.Max(1f, spacing) * grid.HexWidth;
        worldMapFogCloudBucketColumns = Mathf.Max(1, Mathf.CeilToInt((float)(grid.CanvasWidth / worldMapFogCloudBucketSize)) + 1);
        worldMapFogCloudBucketRows = Mathf.Max(1, Mathf.CeilToInt((float)(grid.CanvasHeight / worldMapFogCloudBucketSize)) + 1);
        worldMapFogCloudBuckets = new List<int>[worldMapFogCloudBucketColumns * worldMapFogCloudBucketRows];

        // Уже рассеянные (у исследованного) облака не создаются вовсе —
        // после загрузки сразу виден итог, без повторной анимации.
        foreach (WorldMapFogCloudAnchor anchor in WorldMapFogClouds.Layout(grid, gameState.GeneratedMap.Seed, spacing, rowSpacing, jitter))
        {
            if (WorldMapFogClouds.IsCleared(gameState, grid, anchor.X, anchor.Y, trigger))
                continue;
            float scale = 1f + (anchor.Fraction(5) * 2f - 1f) * variation;
            WorldMapFogCloud cloud = new WorldMapFogCloud
            {
                Anchor = anchor,
                WidthPx = (float)(width * scale * grid.HexWidth)
            };
            VisualElement element = new VisualElement { pickingMode = PickingMode.Ignore };
            element.style.position = Position.Absolute;
            element.style.width = Length.Percent((float)(cloud.WidthPx / grid.CanvasWidth * 100.0));
            element.style.left = Length.Percent((float)((anchor.X - cloud.WidthPx * 0.5) / grid.CanvasWidth * 100.0));
            if (anchor.Flip)
                element.style.scale = new Scale(new Vector3(-1f, 1f, 1f));
            cloud.Element = element;
            VisualElement shadow = new VisualElement { pickingMode = PickingMode.Ignore };
            shadow.style.position = Position.Absolute;
            if (anchor.Flip)
                shadow.style.scale = new Scale(new Vector3(-1f, 1f, 1f));
            cloud.Shadow = shadow;
            int index = worldMapFogCloudList.Count;
            worldMapFogCloudList.Add(cloud);
            int bucket = FogCloudBucketIndex(anchor.X, anchor.Y);
            (worldMapFogCloudBuckets[bucket] ??= new List<int>()).Add(index);
        }

        // Порядок отрисовки — по рисунку: соседние элементы с одной
        // текстурой UI Toolkit сводит в меньшее число вызовов отрисовки.
        // Сами элементы добавляются в ApplyWorldMapFogCloudTextures.
    }

    private int FogCloudBucketIndex(double x, double y)
    {
        int bx = Mathf.Clamp((int)(x / worldMapFogCloudBucketSize), 0, worldMapFogCloudBucketColumns - 1);
        int by = Mathf.Clamp((int)(y / worldMapFogCloudBucketSize), 0, worldMapFogCloudBucketRows - 1);
        return by * worldMapFogCloudBucketColumns + bx;
    }

    // Рисунки облаков (облик карты MapLabMapLook). Смена картинок или их цвета —
    // только смена картинок: раскладка, рассеянные облака и данные исследования не трогаются.
    private void ApplyWorldMapFogCloudTextures(WorldMapMovementSettingsAsset settings)
    {
        List<Texture2D> textures = MapLabGameLook.FogCloudsAdjusted(out string adjustKey);
        string shadowKey = settings == null ? "-" : settings.FogCloudShadows + "|" + settings.FogCloudShadowOffsetHexes.x + "|" + settings.FogCloudShadowOffsetHexes.y + "|" +
            settings.FogCloudShadowScale + "|" + settings.FogCloudShadowBlur + "|" + settings.FogCloudShadowColor;
        string key = adjustKey + ":" + shadowKey + ":";
        foreach (Texture2D t in textures)
            key += t.GetHashCode() + ",";
        if (key == worldMapFogCloudsTextureKey)
            return;
        worldMapFogCloudsTextureKey = key;
        worldMapFogCloudTextures = textures;
        worldMapFogCloudsCovering = textures.Count > 0;

        string warning = textures.Count == 0
            ? "Облака тумана: у облика карты нет рисунков облаков — показана только маска тумана (Генератор карты → Облака тумана войны)."
            : null;
        if (warning != null && warning != worldMapFogCloudsWarned)
            Debug.LogWarning(warning);
        worldMapFogCloudsWarned = warning;

        worldMapFogClouds.Clear();
        worldMapFogCloudShadows?.Clear();
        if (textures.Count == 0)
            return;
        WorldMapHexGrid grid = WorldMapFog.Grid(gameState);
        StyleBackground[] shadowBackgrounds = WorldMapFogCloudShadowBackgrounds(textures, settings);
        Vector2 shadowOffset = (settings != null ? settings.FogCloudShadowOffsetHexes : new Vector2(1.2f, 1.8f)) * (float)grid.HexWidth;
        float shadowScale = FogCloudShadowElementScale(settings);
        Color shadowColor = settings != null ? settings.FogCloudShadowColor : new Color(0.09f, 0.085f, 0.12f, 1f);
        shadowColor.a = 1f;
        // Порядок — по рядам сверху вниз (список раскладки уже так упорядочен):
        // нижнее облако ложится поверх верхнего, плотный покров читается
        // слоями. Пять рисунков укладываются в слоты одной пачки отрисовки.
        StyleBackground[] backgrounds = new StyleBackground[textures.Count];
        float[] aspects = new float[textures.Count];
        for (int i = 0; i < textures.Count; i++)
        {
            backgrounds[i] = new StyleBackground(Background.FromTexture2D(textures[i]));
            aspects[i] = textures[i].width > 0 ? textures[i].height / (float)textures[i].width : 0.35f;
        }
        {
            foreach (WorldMapFogCloud cloud in worldMapFogCloudList)
            {
                if (cloud.Gone)
                    continue;
                int variant = cloud.Anchor.Variant(textures.Count);
                float aspect = aspects[variant];
                StyleBackground background = backgrounds[variant];
                float heightPx = cloud.WidthPx * aspect;
                cloud.HeightPx = heightPx;
                VisualElement element = cloud.Element;
                element.style.backgroundImage = background;
                element.style.unityBackgroundImageTintColor = worldMapFogCloudsLight;
                element.style.height = Length.Percent((float)(heightPx / grid.CanvasHeight * 100.0));
                element.style.top = Length.Percent((float)((cloud.Anchor.Y - heightPx * 0.5) / grid.CanvasHeight * 100.0));
                worldMapFogClouds.Add(element);

                VisualElement shadow = cloud.Shadow;
                if (shadow != null && worldMapFogCloudShadows != null && shadowBackgrounds[variant] != null)
                {
                    float sw = cloud.WidthPx * shadowScale, sh = heightPx * shadowScale;
                    shadow.style.backgroundImage = shadowBackgrounds[variant];
                    shadow.style.unityBackgroundImageTintColor = shadowColor;
                    shadow.style.width = Length.Percent((float)(sw / grid.CanvasWidth * 100.0));
                    shadow.style.height = Length.Percent((float)(sh / grid.CanvasHeight * 100.0));
                    shadow.style.left = Length.Percent((float)((cloud.Anchor.X + shadowOffset.x - sw * 0.5) / grid.CanvasWidth * 100.0));
                    shadow.style.top = Length.Percent((float)((cloud.Anchor.Y + shadowOffset.y - sh * 0.5) / grid.CanvasHeight * 100.0));
                    worldMapFogCloudShadows.Add(shadow);
                }
            }
        }
    }

    // Весь пройденный отрезок пути героя с прошлого кадра: облака ближе
    // радиуса запуска начинают таять. Стоящий герой тоже «касается» облаков —
    // отрезок нулевой длины (после загрузки облако в радиусе тает сразу).
    private void SweepWorldMapFogCloudsAlongHero(WorldMapHexGrid grid, WorldMapMovementSettingsAsset settings, float trigger)
    {
        if (!gameState.HasActiveExpedition || worldMapFogCloudBuckets == null)
        {
            worldMapFogCloudsHeroValid = false;
            return;
        }
        ExpeditionData expedition = gameState.ActiveExpedition;
        Vector2 now = new Vector2(
            (float)grid.PercentToPixelX(expedition.CurrentMapXPercent),
            (float)grid.PercentToPixelY(expedition.CurrentMapYPercent));
        Vector2 from = worldMapFogCloudsHeroValid ? worldMapFogCloudsHeroPx : now;
        // Скачок дальше разумного шага кадра (загрузка, переход) — не «путь»:
        // облака по дороге не трогаются, их уберёт проверка исследованного.
        if ((now - from).magnitude > FogCloudsMaxSweepHexes * grid.HexWidth)
            from = now;
        worldMapFogCloudsHeroPx = now;
        worldMapFogCloudsHeroValid = true;

        double radius = trigger * grid.HexWidth;
        double radiusSquared = radius * radius;
        int bx0 = Mathf.Clamp((int)((Mathf.Min(from.x, now.x) - radius) / worldMapFogCloudBucketSize) - 1, 0, worldMapFogCloudBucketColumns - 1);
        int bx1 = Mathf.Clamp((int)((Mathf.Max(from.x, now.x) + radius) / worldMapFogCloudBucketSize) + 1, 0, worldMapFogCloudBucketColumns - 1);
        int by0 = Mathf.Clamp((int)((Mathf.Min(from.y, now.y) - radius) / worldMapFogCloudBucketSize) - 1, 0, worldMapFogCloudBucketRows - 1);
        int by1 = Mathf.Clamp((int)((Mathf.Max(from.y, now.y) + radius) / worldMapFogCloudBucketSize) + 1, 0, worldMapFogCloudBucketRows - 1);
        for (int by = by0; by <= by1; by++)
        {
            for (int bx = bx0; bx <= bx1; bx++)
            {
                List<int> bucket = worldMapFogCloudBuckets[by * worldMapFogCloudBucketColumns + bx];
                if (bucket == null)
                    continue;
                foreach (int index in bucket)
                {
                    WorldMapFogCloud cloud = worldMapFogCloudList[index];
                    if (cloud.Gone || cloud.Progress >= 0f)
                        continue;
                    WorldMapFogClouds.ClosestOnSegment(cloud.Anchor.X, cloud.Anchor.Y, from.x, from.y, now.x, now.y, out double cx, out double cy);
                    double dx = cloud.Anchor.X - cx;
                    double dy = cloud.Anchor.Y - cy;
                    if (dx * dx + dy * dy <= radiusSquared)
                        StartWorldMapFogCloudDissolve(cloud, (float)cx, (float)cy, grid, settings);
                }
            }
        }
    }

    private void StartWorldMapFogCloudDissolve(WorldMapFogCloud cloud, float heroX, float heroY, WorldMapHexGrid grid, WorldMapMovementSettingsAsset settings)
    {
        if (cloud.Gone || cloud.Progress >= 0f)
            return;
        WorldMapFogClouds.DriftDirection(cloud.Anchor, heroX, heroY, grid.HexWidth * 0.05, out double dx, out double dy);
        float drift = (float)((settings != null ? settings.FogCloudDriftHexes : 3f) * grid.HexWidth);
        // Сдвиг — в долях собственного размера облака (translate в процентах
        // не требует перестройки раскладки и не зависит от масштаба карты).
        // Translate в UI Toolkit применяется в осях родителя, после зеркала
        // (scale -1), поэтому знак X от зеркальности не зависит.
        float heightPx = Mathf.Max(1f, cloud.HeightPx > 0f ? cloud.HeightPx : cloud.WidthPx * 0.35f);
        cloud.Drift = new Vector2((float)(dx * drift / cloud.WidthPx * 100.0), (float)(dy * drift / heightPx * 100.0));
        cloud.ShadowDrift = cloud.Drift / Mathf.Max(0.01f, FogCloudShadowElementScale(settings));
        cloud.Progress = 0f;
        int max = settings != null ? settings.FogCloudMaxDissolving : 48;
        while (worldMapFogCloudsDissolving.Count >= max)
        {
            FinishWorldMapFogCloud(worldMapFogCloudsDissolving[0]);
            worldMapFogCloudsDissolving.RemoveAt(0);
        }
        worldMapFogCloudsDissolving.Add(cloud);
    }

    private void AnimateWorldMapFogClouds(WorldMapMovementSettingsAsset settings)
    {
        if (worldMapFogCloudsDissolving.Count == 0)
            return;
        float duration = settings != null ? settings.FogCloudDissolveSeconds : 1.6f;
        float step = Time.unscaledDeltaTime / duration;
        for (int i = worldMapFogCloudsDissolving.Count - 1; i >= 0; i--)
        {
            WorldMapFogCloud cloud = worldMapFogCloudsDissolving[i];
            cloud.Progress = Mathf.Min(1f, cloud.Progress + step);
            if (cloud.Progress >= 1f)
            {
                FinishWorldMapFogCloud(cloud);
                worldMapFogCloudsDissolving.RemoveAt(i);
                continue;
            }
            float e = settings != null ? settings.EvaluateFogCloudEase(cloud.Progress) : cloud.Progress * cloud.Progress * (3f - 2f * cloud.Progress);
            cloud.Element.style.translate = new Translate(Length.Percent(cloud.Drift.x * e), Length.Percent(cloud.Drift.y * e));
            cloud.Element.style.opacity = 1f - e;
            if (cloud.Shadow != null)
            {
                cloud.Shadow.style.translate = new Translate(Length.Percent(cloud.ShadowDrift.x * e), Length.Percent(cloud.ShadowDrift.y * e));
                cloud.Shadow.style.opacity = 1f - e;
            }
        }
    }

    private static void FinishWorldMapFogCloud(WorldMapFogCloud cloud)
    {
        cloud.Gone = true;
        cloud.Progress = 1f;
        cloud.Element?.RemoveFromHierarchy();
        cloud.Shadow?.RemoveFromHierarchy();
    }

    // Размер элемента тени относительно облака: масштаб тени и поле вокруг силуэта.
    private static float FogCloudShadowElementScale(WorldMapMovementSettingsAsset settings) =>
        (settings != null ? settings.FogCloudShadowScale : 1.2f) / (1f - 2f * FogCloudShadowPad);

    // Текстуры теней по рисункам облаков (размытый силуэт). Считаются один
    // раз на рисунок и мягкость; при смене вида — для новых рисунков.
    private StyleBackground[] WorldMapFogCloudShadowBackgrounds(List<Texture2D> textures, WorldMapMovementSettingsAsset settings)
    {
        StyleBackground[] result = new StyleBackground[textures.Count];
        if (settings != null && !settings.FogCloudShadows)
            return result;
        float blur = settings != null ? settings.FogCloudShadowBlur : 0.03f;
        string key = blur.ToString("0.####");
        if (key != worldMapFogCloudShadowTexturesKey)
        {
            foreach (Texture2D t in worldMapFogCloudShadowTextures.Values)
                if (t != null)
                    Destroy(t);
            worldMapFogCloudShadowTextures.Clear();
            worldMapFogCloudShadowTexturesKey = key;
        }
        for (int i = 0; i < textures.Count; i++)
        {
            if (!worldMapFogCloudShadowTextures.TryGetValue(textures[i], out Texture2D shadow) || shadow == null)
            {
                shadow = MapLabGameLook.CreateFogCloudShadow(textures[i], FogCloudShadowPad, blur);
                worldMapFogCloudShadowTextures[textures[i]] = shadow;
            }
            if (shadow != null)
                result[i] = new StyleBackground(Background.FromTexture2D(shadow));
        }
        return result;
    }
}

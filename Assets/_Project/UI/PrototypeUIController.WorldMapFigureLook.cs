using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

// 12У-H: фигуры на живой карте.
// - Тень: мягкий тёмный овал под ногами героя и групп (свет карты слева
//   сверху — тень чуть вправо).
// - Лес: герой проходит за деревьями — заливка почти скрыта, виден светлый
//   контур фигуры. Контур строится один раз на кадр рисунка (кэш).
public partial class PrototypeUIController
{
    private VisualElement worldMapHeroShadow;
    private Image worldMapHeroOutline;
    private readonly Dictionary<string, VisualElement> worldMapGroupShadows = new Dictionary<string, VisualElement>();
    private static readonly Dictionary<Texture2D, Texture2D> figureOutlines = new Dictionary<Texture2D, Texture2D>();
    private static readonly Color FigureOutlineColor = new Color(1f, 0.93f, 0.72f, 1f);
    private const float HeroForestFillAlpha = 0.18f;

    private static VisualElement MakeFigureShadow(string name)
    {
        VisualElement shadow = new VisualElement { name = name, pickingMode = PickingMode.Ignore };
        shadow.style.position = Position.Absolute;
        shadow.style.left = 0f;
        shadow.style.top = 0f;
        shadow.style.backgroundColor = new Color(0.12f, 0.1f, 0.07f, 0.34f);
        shadow.style.borderTopLeftRadius = new Length(50f, LengthUnit.Percent);
        shadow.style.borderTopRightRadius = new Length(50f, LengthUnit.Percent);
        shadow.style.borderBottomLeftRadius = new Length(50f, LengthUnit.Percent);
        shadow.style.borderBottomRightRadius = new Length(50f, LengthUnit.Percent);
        return shadow;
    }

    // ground — точка опоры фигуры (пиксели полотна), height — рост фигуры.
    private static void PlaceFigureShadow(VisualElement shadow, Vector2 ground, float height, bool visible)
    {
        if (shadow == null)
            return;
        shadow.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        if (!visible)
            return;
        float w = height * 0.46f, h = height * 0.12f;
        shadow.style.width = w;
        shadow.style.height = h;
        shadow.style.translate = new Translate(ground.x - w * 0.4f, ground.y - h * 0.55f);
    }

    // Тень и вид героя в лесу — после того как фигура поставлена.
    private void UpdateHeroFigureLook(Vector2 percent, Texture2D texture, Vector2 ground, float width, float height, Vector2 pivot, float bob)
    {
        if (worldMapHeroImage == null || worldMapViewport == null)
            return;
        if (worldMapHeroShadow == null)
        {
            worldMapHeroShadow = MakeFigureShadow("world-map-hero-shadow");
            worldMapViewport.Insert(worldMapViewport.IndexOf(worldMapHeroImage), worldMapHeroShadow);
        }
        if (worldMapHeroOutline == null)
        {
            worldMapHeroOutline = new Image { name = "world-map-hero-outline", pickingMode = PickingMode.Ignore, scaleMode = ScaleMode.StretchToFill };
            worldMapHeroOutline.style.position = Position.Absolute;
            worldMapHeroOutline.style.left = 0f;
            worldMapHeroOutline.style.top = 0f;
            worldMapViewport.Insert(worldMapViewport.IndexOf(worldMapHeroImage) + 1, worldMapHeroOutline);
        }
        bool forest = WorldMapNavigation.GetTerrainAtPercent(percent.x, percent.y) == WorldMapGameplayTerrainType.Forest;
        // В лесу тени под деревьями не видно.
        PlaceFigureShadow(worldMapHeroShadow, ground, height, !forest);
        worldMapHeroImage.tintColor = new Color(1f, 1f, 1f, forest ? HeroForestFillAlpha : 1f);
        Texture2D outline = forest ? FigureOutline(texture) : null;
        worldMapHeroOutline.style.display = outline != null ? DisplayStyle.Flex : DisplayStyle.None;
        if (outline == null)
            return;
        if (!ReferenceEquals(worldMapHeroOutline.image, outline))
            worldMapHeroOutline.image = outline;
        worldMapHeroOutline.style.width = width;
        worldMapHeroOutline.style.height = height;
        worldMapHeroOutline.style.translate = new Translate(ground.x - pivot.x * width, ground.y - (1f - pivot.y) * height - bob);
    }

    private void HideHeroFigureLook()
    {
        if (worldMapHeroShadow != null)
            worldMapHeroShadow.style.display = DisplayStyle.None;
        if (worldMapHeroOutline != null)
            worldMapHeroOutline.style.display = DisplayStyle.None;
    }

    // Тень группы — в слое групп под всеми фигурами.
    private void PlaceGroupShadow(string actorId, float left, float top, float figureHeight, bool visible)
    {
        if (worldMapGroupLayer == null)
            return;
        if (!worldMapGroupShadows.TryGetValue(actorId, out VisualElement shadow) || shadow.parent != worldMapGroupLayer)
        {
            shadow = MakeFigureShadow("world-map-group-shadow-" + actorId);
            worldMapGroupLayer.Insert(0, shadow);
            worldMapGroupShadows[actorId] = shadow;
        }
        PlaceFigureShadow(shadow, new Vector2(left + figureHeight * 0.5f, top + figureHeight * 0.97f), figureHeight, visible);
    }

    // Светлый контур вокруг силуэта рисунка (снаружи, толщина ~2% высоты).
    private static Texture2D FigureOutline(Texture2D source)
    {
        if (source == null)
            return null;
        if (figureOutlines.TryGetValue(source, out Texture2D cached) && cached != null)
            return cached;
        int w = source.width, h = source.height;
        RenderTexture rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32);
        RenderTexture keep = RenderTexture.active;
        Graphics.Blit(source, rt);
        RenderTexture.active = rt;
        Texture2D readable = new Texture2D(w, h, TextureFormat.RGBA32, false);
        readable.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        readable.Apply();
        RenderTexture.active = keep;
        RenderTexture.ReleaseTemporary(rt);
        Color32[] pixels = readable.GetPixels32();
        Object.Destroy(readable);

        bool[] mask = new bool[w * h];
        for (int i = 0; i < mask.Length; i++)
            mask[i] = pixels[i].a > 90;
        int r = Mathf.Max(2, Mathf.RoundToInt(h * 0.018f));
        // Расширение маски: по строкам, затем по столбцам (квадратное ядро).
        bool[] rows = new bool[w * h];
        for (int y = 0; y < h; y++)
        {
            int last = -100000;
            for (int x = 0; x < w; x++)
                if (mask[y * w + x])
                    last = x;
                else if (x - last <= r)
                    rows[y * w + x] = true;
            last = 100000;
            for (int x = w - 1; x >= 0; x--)
                if (mask[y * w + x])
                    last = x;
                else if (last - x <= r)
                    rows[y * w + x] = true;
            for (int x = 0; x < w; x++)
                rows[y * w + x] |= mask[y * w + x];
        }
        bool[] grown = new bool[w * h];
        for (int x = 0; x < w; x++)
        {
            int last = -100000;
            for (int y = 0; y < h; y++)
                if (rows[y * w + x])
                    last = y;
                else if (y - last <= r)
                    grown[y * w + x] = true;
            last = 100000;
            for (int y = h - 1; y >= 0; y--)
                if (rows[y * w + x])
                    last = y;
                else if (last - y <= r)
                    grown[y * w + x] = true;
            for (int y = 0; y < h; y++)
                grown[y * w + x] |= rows[y * w + x];
        }
        Color32 line = FigureOutlineColor;
        Color32 clear = new Color32(0, 0, 0, 0);
        Color32[] result = new Color32[w * h];
        for (int i = 0; i < result.Length; i++)
            result[i] = grown[i] && !mask[i] ? line : clear;
        Texture2D outline = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = source.name + " · контур", wrapMode = TextureWrapMode.Clamp };
        outline.SetPixels32(result);
        outline.Apply(false, true);
        figureOutlines[source] = outline;
        return outline;
    }
}

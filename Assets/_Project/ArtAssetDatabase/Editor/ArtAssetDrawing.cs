using System.Collections.Generic;
using KingdomSurvival.BattlefieldDatabase;
using UnityEditor;
using UnityEngine;

namespace KingdomSurvival.ArtAssets.Editor
{
    public enum ArtAssetDrawMode { Color, Normal }

    // ПР-12Н: рисование ассета в окнах редактора (холст, галерея, карточка,
    // выбор, палитра). Геометрия частей — тот же LocationVisualResolver, что у
    // рендерера мест, поэтому пропорции, опора и части совпадают с игрой.
    // Рисуются уже загруженные текстуры — отдельные превью не создаются.
    public static class ArtAssetDrawing
    {
        public readonly struct Layout
        {
            public readonly LocationResolvedVisual Resolved;
            // Границы рисунка относительно опоры, единицы мира (Y вверх).
            public readonly Rect Bounds;

            public Layout(LocationResolvedVisual resolved, Rect bounds)
            {
                Resolved = resolved;
                Bounds = bounds;
            }
        }

        private static readonly Dictionary<(string, ArtAssetView, int), Layout> cache = new Dictionary<(string, ArtAssetView, int), Layout>();
        private static ArtAssetDatabaseAsset cachedCatalog;
        private static int cachedRevision = -1;

        public static Layout Resolve(ArtAssetDatabaseAsset catalog, ArtAssetDefinition asset, ArtAssetView view)
        {
            if (cachedCatalog != catalog || cachedRevision != (catalog != null ? catalog.Revision : -1))
            {
                cache.Clear();
                cachedCatalog = catalog;
                cachedRevision = catalog != null ? catalog.Revision : -1;
            }
            (string, ArtAssetView, int) key = (asset.Id, view, 0);
            if (cache.TryGetValue(key, out Layout layout)) return layout;
            LocationVisualObject probe = new LocationVisualObject { Id = "probe", AssetId = asset.Id, View = view, Scale = 1 };
            LocationResolvedVisual resolved = LocationVisualResolver.Resolve(probe, null, catalog);
            Rect bounds = default;
            bool any = false;
            foreach (LocationResolvedPart part in resolved.Parts)
            {
                Rect rect = PartRect(part);
                if (!any) { bounds = rect; any = true; }
                else bounds = Rect.MinMaxRect(Mathf.Min(bounds.xMin, rect.xMin), Mathf.Min(bounds.yMin, rect.yMin),
                    Mathf.Max(bounds.xMax, rect.xMax), Mathf.Max(bounds.yMax, rect.yMax));
            }
            if (!any) bounds = new Rect(-.5f, 0, 1, 1);
            layout = new Layout(resolved, bounds);
            cache[key] = layout;
            return layout;
        }

        // Рисунок части относительно опоры, единицы мира, Y вверх.
        public static Rect PartRect(LocationResolvedPart part)
        {
            float height = Mathf.Max(.01f, part.Height);
            float aspect = part.Sprite != null && part.Sprite.rect.height > 0 ? part.Sprite.rect.width / part.Sprite.rect.height : 1;
            float width = height * aspect;
            return new Rect(-part.Pivot.x * width, -part.Pivot.y * height, width, height);
        }

        // Ассет на экране: anchor — опора в GUI, pixelsPerUnit — масштаб.
        public static void DrawAsset(ArtAssetDatabaseAsset catalog, ArtAssetDefinition asset, ArtAssetView view, Vector2 anchor, float pixelsPerUnit,
            ArtAssetDrawMode mode = ArtAssetDrawMode.Color, Color? tint = null, int highlightPart = -1)
        {
            if (Event.current.type != EventType.Repaint) return;
            Layout layout = Resolve(catalog, asset, view);
            List<LocationResolvedPart> parts = new List<LocationResolvedPart>(layout.Resolved.Parts);
            List<int> order = new List<int>();
            for (int i = 0; i < parts.Count; i++) order.Add(i);
            order.Sort((a, b) =>
            {
                int band = parts[a].Band.CompareTo(parts[b].Band);
                return band != 0 ? band : parts[a].OrderOffset != parts[b].OrderOffset ? parts[a].OrderOffset.CompareTo(parts[b].OrderOffset) : a.CompareTo(b);
            });
            foreach (int i in order)
            {
                LocationResolvedPart part = parts[i];
                Rect world = PartRect(part);
                Rect gui = new Rect(anchor.x + world.xMin * pixelsPerUnit, anchor.y - world.yMax * pixelsPerUnit, world.width * pixelsPerUnit, world.height * pixelsPerUnit);
                Color color = tint ?? Color.white;
                if (highlightPart >= 0 && highlightPart != PartIndex(asset, part)) color *= new Color(1, 1, 1, .35f);
                if (part.Sprite == null)
                {
                    EditorGUI.DrawRect(gui, new Color(1, .45f, .75f, .35f));
                    continue;
                }
                if (mode == ArtAssetDrawMode.Normal)
                {
                    Texture2D normal = NormalPreview(NormalOf(asset, part, layout.Resolved.ShownView));
                    if (normal != null) GUI.DrawTexture(gui, normal, ScaleMode.StretchToFill, true, 0, color, 0, 0);
                    else EditorGUI.DrawRect(gui, new Color(.5f, .5f, 1, .25f));
                    continue;
                }
                DrawSprite(gui, part.Sprite, false, color);
            }
        }

        private static int PartIndex(ArtAssetDefinition asset, LocationResolvedPart part)
        {
            for (int i = 0; i < asset.Parts.Count; i++)
                if (asset.Parts[i] != null && asset.Parts[i].Name == part.Name) return i;
            return -1;
        }

        public static Texture2D NormalOf(ArtAssetDefinition asset, LocationResolvedPart part, ArtAssetView view)
        {
            foreach (ArtAssetPart item in asset.Parts)
            {
                ArtAssetPartView slot = item?.FindView(view);
                if (slot != null && slot.Sprite == part.Sprite) return slot.NormalMap;
            }
            return null;
        }

        // Карта нормалей в настоящих цветах: импортированная нормаль сжата
        // (на экране — розовая), поэтому для показа читается исходный PNG.
        private static readonly Dictionary<string, (System.DateTime stamp, Texture2D texture)> normalPreviews =
            new Dictionary<string, (System.DateTime, Texture2D)>();

        public static Texture2D NormalPreview(Texture2D normal)
        {
            if (normal == null) return null;
            string path = AssetDatabase.GetAssetPath(normal);
            if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path)) return normal;
            System.DateTime stamp = System.IO.File.GetLastWriteTimeUtc(path);
            if (normalPreviews.TryGetValue(path, out (System.DateTime stamp, Texture2D texture) cached) && cached.stamp == stamp && cached.texture != null)
                return cached.texture;
            if (cached.texture != null) Object.DestroyImmediate(cached.texture);
            Texture2D preview = new Texture2D(2, 2, TextureFormat.RGBA32, false, true) { hideFlags = HideFlags.HideAndDontSave, name = "Нормаль · показ" };
            if (!preview.LoadImage(System.IO.File.ReadAllBytes(path)))
            {
                Object.DestroyImmediate(preview);
                return normal;
            }
            normalPreviews[path] = (stamp, preview);
            return preview;
        }

        // Средняя длина вектора карты нормалей по непрозрачным пикселям: у
        // правильной карты ≈ 1; ≈ 1,1–1,2 — карта записана с гамма-коррекцией
        // sRGB (в Blender не Raw). 0 — прочитать не удалось.
        private static readonly Dictionary<string, (System.DateTime stamp, float length)> normalLengths =
            new Dictionary<string, (System.DateTime, float)>();

        public static float NormalLength(Texture2D normal)
        {
            if (normal == null) return 0;
            string path = AssetDatabase.GetAssetPath(normal);
            if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path)) return 0;
            System.DateTime stamp = System.IO.File.GetLastWriteTimeUtc(path);
            if (normalLengths.TryGetValue(path, out (System.DateTime stamp, float length) cached) && cached.stamp == stamp) return cached.length;
            Texture2D preview = NormalPreview(normal);
            float length = 0;
            if (preview != null && preview != normal)
            {
                Color32[] pixels = preview.GetPixels32();
                int step = Mathf.Max(1, pixels.Length / 40000), count = 0;
                double sum = 0;
                for (int i = 0; i < pixels.Length; i += step)
                {
                    Color32 c = pixels[i];
                    if (c.a < 200) continue;
                    float x = c.r / 127.5f - 1, y = c.g / 127.5f - 1, z = c.b / 127.5f - 1;
                    sum += Mathf.Sqrt(x * x + y * y + z * z);
                    count++;
                }
                length = count > 0 ? (float)(sum / count) : 0;
            }
            normalLengths[path] = (stamp, length);
            return length;
        }

        public static void DrawSprite(Rect rect, Sprite sprite, bool flip = false, Color? tint = null)
        {
            if (sprite == null || sprite.texture == null || Event.current.type != EventType.Repaint) return;
            // Полный прямоугольник спрайта, а не textureRect: у «плотной» сетки
            // textureRect обрезан по прозрачности, и часть растянулась бы.
            Rect source = sprite.rect;
            Texture texture = sprite.texture;
            Rect uv = new Rect(source.x / texture.width, source.y / texture.height, source.width / texture.width, source.height / texture.height);
            if (flip) uv = new Rect(uv.xMax, uv.y, -uv.width, uv.height);
            Color previous = GUI.color;
            GUI.color = tint ?? Color.white;
            GUI.DrawTextureWithTexCoords(rect, texture, uv, true);
            GUI.color = previous;
        }

        // Ассет целиком в прямоугольнике (миниатюра) — пропорции сохраняются.
        public static void DrawFitted(ArtAssetDatabaseAsset catalog, ArtAssetDefinition asset, ArtAssetView view, Rect rect,
            ArtAssetDrawMode mode = ArtAssetDrawMode.Color, Color? tint = null)
        {
            Layout layout = Resolve(catalog, asset, view);
            Rect bounds = layout.Bounds;
            float scale = Mathf.Min(rect.width / Mathf.Max(.01f, bounds.width), rect.height / Mathf.Max(.01f, bounds.height));
            Vector2 anchor = new Vector2(rect.center.x - bounds.center.x * scale, rect.center.y + bounds.center.y * scale);
            DrawAsset(catalog, asset, view, anchor, scale, mode, tint);
        }

        public static void Checker(Rect rect, float cell = 12)
        {
            if (Event.current.type != EventType.Repaint) return;
            EditorGUI.DrawRect(rect, new Color(.78f, .78f, .78f));
            Color dark = new Color(.62f, .62f, .62f);
            int columns = Mathf.CeilToInt(rect.width / cell), rows = Mathf.CeilToInt(rect.height / cell);
            if (columns * rows > 20000) return;
            for (int y = 0; y < rows; y++)
                for (int x = (y & 1); x < columns; x += 2)
                {
                    Rect square = new Rect(rect.x + x * cell, rect.y + y * cell, cell, cell);
                    square.xMax = Mathf.Min(square.xMax, rect.xMax);
                    square.yMax = Mathf.Min(square.yMax, rect.yMax);
                    EditorGUI.DrawRect(square, dark);
                }
        }

        // Строка состояния ассета: «Ракурсы 6/6 · Нормали 6/6».
        public static string Completeness(ArtAssetDefinition asset) =>
            "Ракурсы: " + asset.ViewCount + "/6 · Нормали: " + asset.NormalCount + "/6";
    }
}

using System;
using System.IO;
using KingdomSurvival.ArtAssets.Editor;
using KingdomSurvival.BattlefieldDatabase;
using UnityEditor;
using UnityEngine;

namespace KingdomSurvival.LocationRendering.Editor
{
    // Кадрирование обычного рисунка места рамкой: обрезанная копия рисунка
    // (и его нормалей) сохраняется рядом с исходным файлом, исходный не
    // меняется. Раскладку места сдвигает LocationRebase — всё остаётся на
    // своих местах рисунка. Земля из участков Blender кадрируется GroundCrop.
    public static class LocationPictureCrop
    {
        public const int MinSize = 64;
        private const string ExternalFolder = "Assets/_Project/Art/Locations";

        // Размер рисунка в пикселях исходного файла (импорт мог его уменьшить).
        public static Vector2 SourceSize(Sprite sprite)
        {
            if (sprite == null) return Vector2.zero;
            Vector2Int file = SpriteNormalMaps.SourceSize(sprite.texture);
            float k = sprite.texture.width > 0 ? file.x / (float)sprite.texture.width : 1;
            return new Vector2(Mathf.Round(sprite.rect.width * k), Mathf.Round(sprite.rect.height * k));
        }

        // Рамка в пределах места (пиксели места, Y вниз).
        public static RectInt Clamp(RectInt rect, Vector2 canvas)
        {
            int width = Mathf.RoundToInt(canvas.x), height = Mathf.RoundToInt(canvas.y);
            int x0 = Mathf.Clamp(Mathf.Min(rect.xMin, rect.xMax), 0, width), x1 = Mathf.Clamp(Mathf.Max(rect.xMin, rect.xMax), 0, width);
            int y0 = Mathf.Clamp(Mathf.Min(rect.yMin, rect.yMax), 0, height), y1 = Mathf.Clamp(Mathf.Max(rect.yMin, rect.yMax), 0, height);
            return new RectInt(x0, y0, x1 - x0, y1 - y0);
        }

        // null — можно кадрировать.
        public static string Problem(LocalLocationDefinition location, LocationVisualDefinition visual, RectInt rect)
        {
            if (location == null || visual?.Background == null) return "У места нет рисунка.";
            if (visual.Ground != null && visual.Ground.IsTiled) return "Земля из участков Blender кадрируется на вкладке «Земля» («Обрезка»).";
            if (rect.width < MinSize || rect.height < MinSize) return "Рамка меньше " + MinSize + " px.";
            Vector2 canvas = LocationVisualGeometry.CanvasSize(location);
            int width = Mathf.RoundToInt(canvas.x), height = Mathf.RoundToInt(canvas.y);
            if (rect.xMin < 0 || rect.yMin < 0 || rect.xMax > width || rect.yMax > height) return "Рамка выходит за рисунок.";
            if (rect.xMin == 0 && rect.yMin == 0 && rect.xMax == width && rect.yMax == height) return "Рамка совпадает со всем рисунком — кадрировать нечего.";
            return null;
        }

        // Обрезанные копии рисунка и нормалей. Раскладку не трогает.
        public static bool CreateAssets(LocalLocationDefinition location, LocationVisualDefinition visual, RectInt rect,
            out Sprite sprite, out Texture2D normal, out string message)
        {
            sprite = null;
            normal = null;
            message = Problem(location, visual, rect);
            if (message != null) return false;

            Vector2 canvas = LocationVisualGeometry.CanvasSize(location);
            // Доли рисунка, Y вниз.
            Rect fraction = Rect.MinMaxRect(rect.xMin / canvas.x, rect.yMin / canvas.y, rect.xMax / canvas.x, rect.yMax / canvas.y);
            Sprite picture = visual.Background;
            string picturePath = CropFile(picture.texture, picture.rect, fraction, "_crop", out message);
            if (picturePath == null) return false;
            CopyImportSettings(AssetDatabase.GetAssetPath(picture.texture), picturePath);
            sprite = AssetDatabase.LoadAssetAtPath<Sprite>(picturePath);
            if (sprite == null) { message = "Не удалось импортировать " + picturePath + " как спрайт."; return false; }

            Texture2D sourceNormal = visual.BackgroundNormalMap;
            if (sourceNormal != null)
            {
                float k = sourceNormal.width / (float)Mathf.Max(1, picture.texture.width);
                Rect normalRect = new Rect(picture.rect.x * k, picture.rect.y * k, picture.rect.width * k, picture.rect.height * k);
                string normalPath = CropFile(sourceNormal, normalRect, fraction, "_crop", out message);
                if (normalPath == null) return false;
                normal = AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath);
                if (normal == null) { message = "Не удалось импортировать нормали " + normalPath + "."; return false; }
                SpriteNormalMaps.PrepareNormalTexture(normal);
                if (!SpriteNormalMaps.Assign(sprite, normal, out string assign)) { message = assign; return false; }
            }
            message = "Рисунок кадрирован: " + Path.GetFileName(picturePath) + (normal != null ? " (с нормалями)" : "") + ".";
            return true;
        }

        // Часть файла текстуры: spriteRect — в пикселях импортированной
        // текстуры (Y вверх), fraction — доли спрайта (Y вниз).
        private static string CropFile(Texture texture, Rect spriteRect, Rect fraction, string suffix, out string message)
        {
            message = null;
            string path = AssetDatabase.GetAssetPath(texture);
            Texture2D source = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!File.Exists(path) || !source.LoadImage(File.ReadAllBytes(path), false))
                {
                    message = "Не удалось прочитать " + path + " — кадрировать можно PNG или JPG.";
                    return null;
                }
                // Исходный файл может быть крупнее импортированной текстуры.
                float k = source.width / (float)Mathf.Max(1, texture.width);
                float sx = spriteRect.x * k, sy = spriteRect.y * k, sw = spriteRect.width * k, sh = spriteRect.height * k;
                int x0 = Mathf.Clamp(Mathf.RoundToInt(sx + fraction.xMin * sw), 0, source.width);
                int x1 = Mathf.Clamp(Mathf.RoundToInt(sx + fraction.xMax * sw), 0, source.width);
                int y0 = Mathf.Clamp(Mathf.RoundToInt(sy + (1 - fraction.yMax) * sh), 0, source.height);
                int y1 = Mathf.Clamp(Mathf.RoundToInt(sy + (1 - fraction.yMin) * sh), 0, source.height);
                int width = x1 - x0, height = y1 - y0;
                if (width < 1 || height < 1)
                {
                    message = "Рамка пуста в файле " + path + ".";
                    return null;
                }

                Color32[] all = source.GetPixels32();
                Color32[] part = new Color32[width * height];
                for (int row = 0; row < height; row++)
                    Array.Copy(all, (y0 + row) * source.width + x0, part, row * width, width);
                Texture2D result = new Texture2D(width, height, TextureFormat.RGBA32, false);
                byte[] png;
                try
                {
                    result.SetPixels32(part);
                    result.Apply();
                    png = result.EncodeToPNG();
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(result);
                }

                string folder = path.StartsWith("Assets/", StringComparison.Ordinal) ? Path.GetDirectoryName(path)?.Replace('\\', '/') : ExternalFolder;
                if (string.IsNullOrEmpty(folder)) folder = ExternalFolder;
                Directory.CreateDirectory(folder);
                string target = AssetDatabase.GenerateUniqueAssetPath(folder + "/" + Path.GetFileNameWithoutExtension(path) + suffix + ".png");
                File.WriteAllBytes(target, png);
                AssetDatabase.ImportAsset(target, ImportAssetOptions.ForceSynchronousImport);
                return target;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(source);
            }
        }

        // Копия как исходный рисунок: спрайт, PPU, фильтр, сжатие, размер.
        private static void CopyImportSettings(string fromPath, string toPath)
        {
            if (!(AssetImporter.GetAtPath(toPath) is TextureImporter to)) return;
            if (AssetImporter.GetAtPath(fromPath) is TextureImporter from)
            {
                TextureImporterSettings settings = new TextureImporterSettings();
                from.ReadTextureSettings(settings);
                settings.textureType = TextureImporterType.Sprite;
                settings.spriteMode = (int)SpriteImportMode.Single;
                to.SetTextureSettings(settings);
                to.textureCompression = from.textureCompression;
                to.maxTextureSize = from.maxTextureSize;
            }
            else
            {
                to.textureType = TextureImporterType.Sprite;
                to.spriteImportMode = SpriteImportMode.Single;
                to.alphaIsTransparency = true;
                to.sRGBTexture = true;
            }
            to.SaveAndReimport();
        }
    }
}

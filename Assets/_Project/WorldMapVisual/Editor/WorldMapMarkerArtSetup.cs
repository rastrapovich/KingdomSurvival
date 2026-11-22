using System.IO;
using UnityEditor;
using UnityEngine;

namespace KingdomSurvival.WorldMapVisual.Editor
{
    // Метки карты: PNG из Assets/_Project/Art/MapMarkers/classic/
    // (MapMarker_Question.png, MapMarker_Exclamation.png) → ассет
    // WorldMapMarkerArt в Resources. Импорт — UI-картинка: без mipmaps, Clamp,
    // Bilinear, прозрачность, без сжатия (края символа на 24–48 px чистые).
    // Видимая часть символа считается по альфе исходного PNG.
    public static class WorldMapMarkerArtSetup
    {
        private const string QuestionFile = "MapMarker_Question.png";
        private const string ExclamationFile = "MapMarker_Exclamation.png";

        [MenuItem("Kingdom Survival/Карта/Метки карты: собрать", priority = 320)]
        public static void BuildMenu()
        {
            string message = Build();
            Debug.Log("MAP_MARKERS: " + message);
            EditorUtility.DisplayDialog("Метки карты", message, "OK");
        }

        // Пакетно: -executeMethod KingdomSurvival.WorldMapVisual.Editor.WorldMapMarkerArtSetup.BuildBatch
        public static void BuildBatch() => Debug.Log("MAP_MARKERS: " + Build());

        public static string Build()
        {
            WorldMapMarkerArt asset = AssetDatabase.LoadAssetAtPath<WorldMapMarkerArt>(WorldMapMarkerArt.AssetPath);
            if (asset == null)
            {
                string folder = Path.GetDirectoryName(WorldMapMarkerArt.AssetPath)?.Replace('\\', '/');
                if (!string.IsNullOrEmpty(folder) && !AssetDatabase.IsValidFolder(folder))
                    Directory.CreateDirectory(folder);
                asset = ScriptableObject.CreateInstance<WorldMapMarkerArt>();
                AssetDatabase.CreateAsset(asset, WorldMapMarkerArt.AssetPath);
            }
            asset.Question = Symbol(WorldMapMarkerArt.ArtRoot + "/" + QuestionFile);
            asset.Exclamation = Symbol(WorldMapMarkerArt.ArtRoot + "/" + ExclamationFile);
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssetIfDirty(asset);
            return (asset.IsComplete ? "оба символа на месте" : "нет одного из символов") + $" ({WorldMapMarkerArt.AssetPath}).";
        }

        private static WorldMapMarkerSymbol Symbol(string path)
        {
            WorldMapMarkerSymbol symbol = new WorldMapMarkerSymbol();
            if (!File.Exists(path))
                return symbol;
            Import(path);
            symbol.Texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            symbol.Visible = VisibleRect(path);
            return symbol;
        }

        private static void Import(string path)
        {
            if (!(AssetImporter.GetAtPath(path) is TextureImporter importer))
            {
                AssetDatabase.ImportAsset(path);
                importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null)
                    return;
            }
            bool changed = importer.textureType != TextureImporterType.Default || importer.mipmapEnabled || !importer.alphaIsTransparency ||
                           importer.wrapMode != TextureWrapMode.Clamp || importer.filterMode != FilterMode.Bilinear ||
                           importer.textureCompression != TextureImporterCompression.Uncompressed || importer.maxTextureSize != 256 ||
                           !importer.sRGBTexture;
            if (!changed)
                return;
            importer.textureType = TextureImporterType.Default;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.sRGBTexture = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = 256;
            importer.SaveAndReimport();
        }

        // Рамка alpha ≥ 128 исходного PNG, доли холста от левого верхнего угла.
        private static Rect VisibleRect(string path)
        {
            Texture2D source = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!source.LoadImage(File.ReadAllBytes(path)))
                    return new Rect(0f, 0f, 1f, 1f);
                Color32[] pixels = source.GetPixels32();
                int w = source.width, h = source.height;
                int x0 = w, y0 = h, x1 = -1, y1 = -1;
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                        if (pixels[y * w + x].a >= 128)
                        {
                            if (x < x0) x0 = x;
                            if (x > x1) x1 = x;
                            if (y < y0) y0 = y;
                            if (y > y1) y1 = y;
                        }
                if (x1 < 0)
                    return new Rect(0f, 0f, 1f, 1f);
                // Строки текстуры — снизу вверх; рамка — от верхнего края.
                float top = (h - 1 - y1) / (float)h;
                return new Rect(x0 / (float)w, top, (x1 - x0 + 1) / (float)w, (y1 - y0 + 1) / (float)h);
            }
            finally
            {
                Object.DestroyImmediate(source);
            }
        }
    }
}

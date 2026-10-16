using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace KingdomSurvival.LocationRendering.Editor
{
    // ПР-12М: LUT для «Тона» обработки кадра. Полоска URP — 32 клетки 32×32
    // (1024×32): x = синий · 32 + красный, y = зелёный (снизу вверх), без sRGB.
    // Работа художника: снимок кадра без LUT с нейтральной полоской в левом
    // верхнем углу → покраска всего снимка в Photoshop / Krita → извлечение
    // полоски обратно в LUT проекта.
    public static class LutTools
    {
        public const int Size = 32;
        public const string Folder = "Assets/_Project/LocationRendering/Luts";

        public static Color NeutralAt(int x, int y)
        {
            int red = x % Size, blue = x / Size;
            return new Color(red / (Size - 1f), y / (Size - 1f), blue / (Size - 1f), 1);
        }

        // Что не так с LUT (null — годится).
        public static string Problem(Texture2D lut)
        {
            if (lut == null) return null;
            if (lut.width != Size * Size || lut.height != Size)
                return "LUT должен быть полоской " + Size * Size + "×" + Size + " (сейчас " + lut.width + "×" + lut.height + "). Сделайте его кнопками ниже.";
            if (AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(lut)) is TextureImporter importer)
            {
                if (importer.sRGBTexture) return "У LUT включён sRGB — URP его не примет.";
                if (importer.mipmapEnabled || importer.textureCompression != TextureImporterCompression.Uncompressed || importer.npotScale != TextureImporterNPOTScale.None)
                    return "Импорт LUT: нужны без мип-карт, без сжатия и без подгонки размера — иначе цвета поплывут.";
            }
            return null;
        }

        public static bool CanFix(Texture2D lut) => lut != null && AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(lut)) is TextureImporter;

        public static void FixImport(Texture2D lut)
        {
            if (!(AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(lut)) is TextureImporter importer)) return;
            Configure(importer);
            importer.SaveAndReimport();
        }

        private static void Configure(TextureImporter importer)
        {
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = false;
            importer.mipmapEnabled = false;
            importer.alphaSource = TextureImporterAlphaSource.None;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.isReadable = false;
        }

        // Снимок с полоской: PNG, который красит художник.
        public static void ExportSnapshot(Func<Texture2D> snapshot, Action openWindow)
        {
            if (snapshot == null)
            {
                if (EditorUtility.DisplayDialog("Снимок для покраски", "Снимок кадра делается в окне «Обработка кадра» (там свой большой предпросмотр). Открыть его?", "Открыть", "Отмена"))
                    openWindow?.Invoke();
                return;
            }
            Texture2D shot = snapshot();
            if (shot == null) { EditorUtility.DisplayDialog("Снимок для покраски", "Не удалось снять кадр предпросмотра.", "Понятно"); return; }
            try
            {
                if (shot.width < Size * Size || shot.height < Size * 2)
                {
                    EditorUtility.DisplayDialog("Снимок для покраски", "Кадр слишком мал для полоски LUT (нужно не меньше 1024 по ширине).", "Понятно");
                    return;
                }
                // Полоска — в левом верхнем углу, поверх кадра, пиксель в пиксель.
                for (int y = 0; y < Size; y++)
                    for (int x = 0; x < Size * Size; x++)
                        shot.SetPixel(x, shot.height - Size + y, NeutralAt(x, y));
                shot.Apply();
                string path = EditorUtility.SaveFilePanel("Снимок для покраски (PNG)", "", "кадр_для_LUT.png", "png");
                if (string.IsNullOrEmpty(path)) return;
                File.WriteAllBytes(path, shot.EncodeToPNG());
                EditorUtility.RevealInFinder(path);
                EditorUtility.DisplayDialog("Снимок сохранён",
                    "Покрасьте снимок целиком вместе с полоской в левом верхнем углу (только цвет: кривые, баланс, оттенок; без размытия, обрезки и смены размера), " +
                    "сохраните PNG и нажмите «Извлечь LUT из покрашенного снимка».", "Понятно");
            }
            finally { UnityEngine.Object.DestroyImmediate(shot); }
        }

        // Покрашенный снимок → LUT в проекте.
        public static Texture2D ImportPainted()
        {
            string path = EditorUtility.OpenFilePanel("Покрашенный снимок (PNG)", "", "png");
            if (string.IsNullOrEmpty(path)) return null;
            Texture2D lut = FromImageFile(path, out string error);
            if (lut == null) EditorUtility.DisplayDialog("LUT", error, "Понятно");
            return lut;
        }

        // LUT из картинки: полоска 1024×32 — как есть; больше — покрашенный
        // снимок, полоска из его левого верхнего угла. Сохраняется в проект.
        public static Texture2D FromImageFile(string path, out string error)
        {
            error = null;
            Texture2D painted = new Texture2D(2, 2, TextureFormat.RGBA32, false, false);
            try
            {
                if (!File.Exists(path) || !painted.LoadImage(File.ReadAllBytes(path)))
                {
                    error = "Не удалось прочитать картинку (нужен PNG или JPG): " + path;
                    return null;
                }
                if (painted.width < Size * Size || painted.height < Size)
                {
                    error = "В картинке нет полоски LUT (она меньше 1024×32). Нужен PNG, сделанный кнопкой «Снимок для покраски», или сама полоска 1024×32.";
                    return null;
                }
                Texture2D lut = new Texture2D(Size * Size, Size, TextureFormat.RGBA32, false, true);
                for (int y = 0; y < Size; y++)
                    for (int x = 0; x < Size * Size; x++)
                    {
                        Color c = painted.GetPixel(x, painted.height - Size + y);
                        c.a = 1;
                        lut.SetPixel(x, y, c);
                    }
                lut.Apply();
                string name = Path.GetFileNameWithoutExtension(path);
                return Save(lut, name.EndsWith("_LUT") ? name : name + "_LUT");
            }
            finally { UnityEngine.Object.DestroyImmediate(painted); }
        }

        public static Texture2D SaveNeutral()
        {
            Texture2D lut = new Texture2D(Size * Size, Size, TextureFormat.RGBA32, false, true);
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size * Size; x++)
                    lut.SetPixel(x, y, NeutralAt(x, y));
            lut.Apply();
            return Save(lut, "Нейтральный_LUT");
        }

        private static Texture2D Save(Texture2D lut, string name)
        {
            try
            {
                Directory.CreateDirectory(Folder);
                string asset = AssetDatabase.GenerateUniqueAssetPath(Folder + "/" + Sanitize(name) + ".png");
                File.WriteAllBytes(asset, lut.EncodeToPNG());
                AssetDatabase.ImportAsset(asset, ImportAssetOptions.ForceSynchronousImport);
                if (AssetImporter.GetAtPath(asset) is TextureImporter importer)
                {
                    Configure(importer);
                    importer.SaveAndReimport();
                }
                Texture2D result = AssetDatabase.LoadAssetAtPath<Texture2D>(asset);
                if (result != null) EditorGUIUtility.PingObject(result);
                return result;
            }
            finally { UnityEngine.Object.DestroyImmediate(lut); }
        }

        private static string Sanitize(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return string.IsNullOrWhiteSpace(name) ? "LUT" : name;
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KingdomSurvival.BattlefieldDatabase;
using UnityEditor;
using UnityEngine;

namespace KingdomSurvival.ArtAssets.Editor
{
    public sealed class ArtAssetReduceResult
    {
        public bool Done;
        public string Error;
        public int Files;
        public float OldPixelsPerUnit, NewPixelsPerUnit;
        public string BackupFolder;
        public readonly List<string> Warnings = new List<string>();
    }

    // ПР-12М: плотность рисунка ассета. Место рисуется при 108 пикселях на
    // единицу мира (LocationVisualGeometry.PixelsPerUnit) — примерно пиксель
    // рисунка на пиксель экрана. Рисунок с PPU заметно выше ужимается на
    // экране в разы, а мипмапов у управляемых файлов нет: мелкий шум и
    // «подробнее, чем деревья рядом». Лечится уменьшением самих PNG (не
    // maxTextureSize: листы кадров собираются из исходных файлов, и размер
    // кадра должен совпадать с размером файла) с делением PPU на тот же
    // коэффициент — высота в мире не меняется.
    public static class ArtAssetDensity
    {
        public const float GamePixelsPerUnit = LocationVisualGeometry.PixelsPerUnit;
        // Выше — рисунок ужимается больше чем в ~1,4 раза.
        public const float WarnPixelsPerUnit = 150f;
        public const string BackupRootName = "ArtBackups";

        public static string Problem(ArtAssetDefinition asset)
        {
            if (asset == null || asset.IsEmpty || asset.PixelsPerUnit <= WarnPixelsPerUnit) return null;
            float shrink = asset.PixelsPerUnit / GamePixelsPerUnit;
            return "рисунок подробнее, чем видно в игре: " + Mathf.RoundToInt(asset.PixelsPerUnit) + " пикс. на ед. мира при " +
                   Mathf.RoundToInt(GamePixelsPerUnit) + " у мест (ужимается в " + shrink.ToString("0.#") +
                   " раза) — в месте он рябит мелким шумом. Кнопка «Уменьшить рисунок» — в разделе «Общее».";
        }

        // Коэффициенты, которые имеет смысл предложить: после уменьшения
        // рисунок не становится заметно грубее места.
        public static IEnumerable<int> UsefulFactors(ArtAssetDefinition asset)
        {
            if (asset == null) yield break;
            foreach (int factor in new[] { 2, 4 })
                if (asset.PixelsPerUnit / factor >= GamePixelsPerUnit * .65f) yield return factor;
        }

        // PNG верхнего уровня папки ассета: кадры, ракурсы, части, тени,
        // нормали. Папка листов не входит — она пересобирается.
        public static List<string> Files(ArtAssetDefinition asset)
        {
            string folder = ArtAssetImporter.AssetFolder(asset);
            if (!Directory.Exists(folder)) return new List<string>();
            return Directory.GetFiles(folder, "*.png", SearchOption.TopDirectoryOnly)
                .Select(path => path.Replace('\\', '/')).OrderBy(path => path, StringComparer.Ordinal).ToList();
        }

        // Почему ассет нельзя уменьшить (null — можно).
        public static string Check(ArtAssetDefinition asset, int factor)
        {
            if (asset == null) return "ассет не выбран.";
            if (factor != 2 && factor != 4) return "уменьшать можно вдвое или вчетверо.";
            List<string> files = Files(asset);
            if (files.Count == 0) return "в папке ассета (" + ArtAssetImporter.AssetFolder(asset) + ") нет рисунков.";
            HashSet<string> own = new HashSet<string>(files, StringComparer.OrdinalIgnoreCase);
            string sheets = ArtAssetImporter.AssetFolder(asset) + "/" + ArtAssetSheets.FolderName + "/";
            foreach (Texture texture in Referenced(asset))
            {
                string path = AssetDatabase.GetAssetPath(texture);
                if (string.IsNullOrEmpty(path) || path.StartsWith(sheets, StringComparison.OrdinalIgnoreCase)) continue;
                if (!own.Contains(path))
                    return "«" + path + "» лежит вне папки ассета — такой рисунок не уменьшится вместе с остальными. Загрузите его в ассет заново.";
                if (AssetImporter.GetAtPath(path) is TextureImporter importer && importer.textureType == TextureImporterType.Sprite &&
                    importer.spriteImportMode == SpriteImportMode.Multiple)
                    return "«" + Path.GetFileName(path) + "» — лист из нескольких спрайтов: их рамки в .meta уменьшение не пересчитывает.";
            }
            foreach (string path in files)
            {
                Vector2Int size = SourceSize(path);
                if (size.x % factor != 0 || size.y % factor != 0)
                    return "«" + Path.GetFileName(path) + "» — " + size.x + "×" + size.y + ": размер не делится на " + factor +
                           ", иначе рисунок съедет на полпикселя. Перерендерите его с размером, кратным " + factor + ".";
            }
            return null;
        }

        // Уменьшить все PNG ассета в factor раз и поделить PPU на factor.
        // backupRoot — куда скопировать исходники до перезаписи (null — без копии).
        public static ArtAssetReduceResult Reduce(ArtAssetDatabaseAsset catalog, ArtAssetDefinition asset, int factor, string backupRoot)
        {
            ArtAssetReduceResult result = new ArtAssetReduceResult { OldPixelsPerUnit = asset?.PixelsPerUnit ?? 0 };
            result.Error = Check(asset, factor);
            if (result.Error != null) return result;
            List<string> files = Files(asset);
            HashSet<string> normals = new HashSet<string>(
                Referenced(asset, normalsOnly: true).Select(AssetDatabase.GetAssetPath).Where(path => !string.IsNullOrEmpty(path)),
                StringComparer.OrdinalIgnoreCase);

            if (!string.IsNullOrEmpty(backupRoot))
            {
                string backup = Path.Combine(backupRoot, asset.Id + "_" + DateTime.Now.ToString("yyyyMMdd-HHmmss")).Replace('\\', '/');
                Directory.CreateDirectory(backup);
                foreach (string path in files) File.Copy(path, Path.Combine(backup, Path.GetFileName(path)), true);
                File.WriteAllText(Path.Combine(backup, "README.txt"),
                    "Исходные рисунки ассета «" + asset.Name + "» (" + asset.Id + ") до уменьшения в " + factor + " раза.\n" +
                    "Папка в проекте: " + ArtAssetImporter.AssetFolder(asset) + "\n" +
                    "Пикселей на ед. мира было: " + asset.PixelsPerUnit + "\n" +
                    "Вернуть: скопировать PNG обратно в папку проекта и поставить прежнее значение «Пикселей на ед. мира».\n");
                result.BackupFolder = backup;
            }

            foreach (string path in files)
            {
                bool normal = normals.Contains(path) || ArtAssetTexturePostprocessor.IsManagedNormal(path);
                if (!Downscale(path, factor, normal))
                {
                    result.Warnings.Add("«" + Path.GetFileName(path) + "» не прочитан как PNG — пропущен.");
                    continue;
                }
                result.Files++;
            }
            foreach (string path in files)
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);

            asset.PixelsPerUnit /= factor;
            result.NewPixelsPerUnit = asset.PixelsPerUnit;
            if (catalog != null)
            {
                ArtAssetImporter.Changed(catalog);
                ArtAssetSheets.Refresh(catalog, asset, result.Warnings, force: true);
                // Файлы уже переписаны: Undo вернул бы прежний масштаб к
                // уменьшенным рисункам. Откат — только из копии исходников.
                Undo.ClearUndo(catalog);
                if (AssetDatabase.Contains(catalog)) AssetDatabase.SaveAssetIfDirty(catalog);
            }
            result.Done = true;
            return result;
        }

        // Уменьшить PNG на месте усреднением блоков factor×factor. Цвет — с
        // премультипликацией альфы (иначе на краях кайма от цвета прозрачных
        // пикселей); нормаль — средним вектором, нормализованным, альфа как есть.
        public static bool Downscale(string path, int factor, bool normal)
        {
            Texture2D source = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            Texture2D target = null;
            try
            {
                if (!source.LoadImage(File.ReadAllBytes(path), false)) return false;
                int width = source.width / factor, height = source.height / factor;
                if (width < 1 || height < 1 || source.width % factor != 0 || source.height % factor != 0) return false;
                Color[] pixels = source.GetPixels();
                Color[] result = new Color[width * height];
                float count = factor * factor;
                for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    float r = 0, g = 0, b = 0, a = 0;
                    for (int dy = 0; dy < factor; dy++)
                    {
                        int row = (y * factor + dy) * source.width + x * factor;
                        for (int dx = 0; dx < factor; dx++)
                        {
                            Color c = pixels[row + dx];
                            if (normal) { r += c.r * 2 - 1; g += c.g * 2 - 1; b += c.b * 2 - 1; }
                            else { r += c.r * c.a; g += c.g * c.a; b += c.b * c.a; }
                            a += c.a;
                        }
                    }
                    Color value;
                    if (normal)
                    {
                        Vector3 n = new Vector3(r, g, b);
                        n = n.sqrMagnitude > 1e-8f ? n.normalized : Vector3.forward;
                        value = new Color(n.x * .5f + .5f, n.y * .5f + .5f, n.z * .5f + .5f, a / count);
                    }
                    else value = a > 1e-6f ? new Color(r / a, g / a, b / a, a / count) : new Color(0, 0, 0, 0);
                    result[y * width + x] = value;
                }
                // Шестнадцатибитные PNG остаются шестнадцатибитными.
                TextureFormat format = source.format == TextureFormat.RGBA64 ? TextureFormat.RGBA64 : TextureFormat.RGBA32;
                target = new Texture2D(width, height, format, false, true);
                target.SetPixels(result);
                target.Apply(false);
                File.WriteAllBytes(path, target.EncodeToPNG());
                return true;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(source);
                if (target != null) UnityEngine.Object.DestroyImmediate(target);
            }
        }

        public static string DefaultBackupRoot() =>
            Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", BackupRootName).Replace('\\', '/');

        private static Vector2Int SourceSize(string path)
        {
            if (AssetImporter.GetAtPath(path) is TextureImporter importer)
            {
                importer.GetSourceTextureWidthAndHeight(out int width, out int height);
                return new Vector2Int(width, height);
            }
            return Vector2Int.zero;
        }

        // Текстуры, на которые ссылается запись (кроме листов кадров).
        private static IEnumerable<Texture> Referenced(ArtAssetDefinition asset, bool normalsOnly = false)
        {
            foreach (ArtAssetPart part in asset.Parts.Where(item => item != null))
            foreach (ArtAssetPartView slot in (part.Views ?? new List<ArtAssetPartView>()).Where(item => item != null))
            {
                if (slot.NormalMap != null) yield return slot.NormalMap;
                if (!normalsOnly && slot.Sprite != null) yield return slot.Sprite.texture;
                if (!normalsOnly && slot.ShadowSprite != null) yield return slot.ShadowSprite.texture;
                foreach (ArtAssetFrame frame in (slot.Frames ?? new List<ArtAssetFrame>()).Where(item => item != null))
                {
                    if (frame.NormalMap != null) yield return frame.NormalMap;
                    if (!normalsOnly && frame.Sprite != null) yield return frame.Sprite.texture;
                }
            }
        }
    }
}

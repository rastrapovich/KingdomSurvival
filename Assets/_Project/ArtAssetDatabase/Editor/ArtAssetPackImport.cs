using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace KingdomSurvival.ArtAssets.Editor
{
    // Пакет отдельных рисунков окружения одного вида (первый набор арены,
    // 69 PNG, 09.11.2026): CATALOG_RU.csv «ID;Название;Категория;Файл;…;
    // Предложенная высота дерева». Каждый PNG — отдельный ассет с русским
    // названием, категорией, назначением и высотой по видимому силуэту.
    // Повторный импорт находит запись по ключу пакета (ImportKey) и обновляет
    // её — ID и размещения сохраняются. Исходные файлы не меняются: копия
    // проходит очистку каймы (цвет полупрозрачного края берётся от соседних
    // непрозрачных пикселей; альфа, силуэт и размер холста те же).
    public static class ArtAssetPackImport
    {
        public const string PackTag = "arena-pack-69";
        private const string Staging = "Temp/ArtAssetPack";

        public sealed class Entry
        {
            public string Id, Name, Category, File;
            public float TreeRatio;
        }

        // Видимая высота силуэта в единицах мира мест (человек ≈ 1,7).
        // Деревья — от предложенного отношения пакета (молодое = 1).
        private const float YoungTreeHeight = 3f;
        private static readonly Dictionary<string, float> Heights = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase)
        {
            ["Reedgrass"] = 1.1f, ["Fireweed"] = 1.4f, ["LowGrassModule"] = .5f, ["Nettle"] = 1.1f, ["Burdock"] = .9f,
            ["GrassCarpet"] = .25f, ["GrassShoots"] = .35f, ["WildRosette"] = .25f, ["Fern"] = .8f, ["Plantain"] = .25f,
            ["ThinSparseGrass"] = .45f, ["SparseLowGrass"] = .45f, ["Cattail"] = 1.3f, ["TallCattail"] = 1.8f, ["TuftedHairgrass"] = .6f,
            ["Bilberry"] = .35f, ["Lingonberry"] = .3f, ["WildStrawberry"] = .25f, ["Raspberry"] = 1.2f,
            ["MossyStump"] = .6f, ["HollowWillowStump"] = .8f, ["BirchStump"] = .55f, ["PineStump"] = .6f, ["AlderStump"] = .55f,
            ["SmallRock"] = .35f, ["LargeBoulder"] = 1.3f, ["GroundCobble"] = .22f,
        };

        public static List<Entry> ReadCatalog(string folder)
        {
            string path = Path.Combine(folder, "CATALOG_RU.csv");
            List<Entry> entries = new List<Entry>();
            foreach (string raw in File.ReadAllLines(path, Encoding.UTF8).Skip(1))
            {
                string line = raw.Trim('﻿', ' ');
                if (line.Length == 0) continue;
                string[] cells = line.Split(';');
                if (cells.Length < 4) continue;
                float.TryParse(cells.Length > 6 ? cells[6] : "", NumberStyles.Float, CultureInfo.InvariantCulture, out float ratio);
                entries.Add(new Entry { Id = cells[0].Trim(), Name = cells[1].Trim(), Category = cells[2].Trim(), File = cells[3].Trim(), TreeRatio = ratio });
            }
            return entries;
        }

        public static ArtAssetCategory CategoryOf(Entry entry)
        {
            switch (entry.Category)
            {
                case "Trees": return ArtAssetCategory.Trees;
                case "Stumps": return ArtAssetCategory.Trees;
                case "Rocks": return ArtAssetCategory.Rocks;
                default: return ArtAssetCategory.Vegetation;
            }
        }

        // Деревья, пни и камни — общие для карты и арены; низкие растения
        // пакета нарисованы для крупного плана арены.
        public static ArtAssetPurpose PurposeOf(Entry entry) =>
            entry.Category == "Vegetation" ? ArtAssetPurpose.ArenaOnly : ArtAssetPurpose.Both;

        public static float HeightOf(Entry entry) =>
            entry.TreeRatio > 0 ? entry.TreeRatio * YoungTreeHeight : Heights.TryGetValue(entry.Id, out float height) ? height : 1f;

        [MenuItem("Kingdom Survival/База ассетов: импорт пакета рисунков…")]
        private static void ImportFromMenu()
        {
            string folder = EditorUtility.OpenFolderPanel("Папка пакета (с CATALOG_RU.csv)", "", "");
            if (string.IsNullOrEmpty(folder)) return;
            Debug.Log(Import(folder));
        }

        // Пакетный режим: -executeMethod … -packFolder <папка>.
        public static void ImportBatch()
        {
            string[] args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, "-packFolder");
            if (index < 0 || index + 1 >= args.Length) throw new ArgumentException("нужен -packFolder <папка пакета>");
            Debug.Log(Import(args[index + 1]));
            AssetDatabase.SaveAssets();
        }

        public static string Import(string folder, ArtAssetDatabaseAsset catalog = null, bool save = true)
        {
            catalog = catalog != null ? catalog : AssetDatabase.LoadAssetAtPath<ArtAssetDatabaseAsset>(ArtAssetDatabaseAsset.AssetPath);
            if (catalog == null) throw new InvalidOperationException("нет каталога " + ArtAssetDatabaseAsset.AssetPath);
            List<Entry> entries = ReadCatalog(folder);
            Directory.CreateDirectory(Staging);
            ArtAssetImportPlan plan = new ArtAssetImportPlan();
            Dictionary<ArtAssetImportGroup, Entry> byGroup = new Dictionary<ArtAssetImportGroup, Entry>();
            int defringed = 0;
            List<string> missing = new List<string>();
            foreach (Entry entry in entries)
            {
                string source = Path.Combine(folder, entry.File);
                if (!File.Exists(source)) { missing.Add(entry.File); continue; }
                string staged = Path.GetFullPath(Path.Combine(Staging, entry.Id + ".png")).Replace('\\', '/');
                defringed += Defringe(source, staged);
                ArtAssetImportGroup group = new ArtAssetImportGroup { Name = entry.Name, Key = PackKey(entry) };
                group.Slot(ArtAssetView.Front, string.Empty).ColorPath = staged;
                plan.Groups.Add(group);
                byGroup[group] = entry;
            }
            Dictionary<string, ArtAssetDefinition> existing = catalog.assets.Where(item => item != null && !string.IsNullOrEmpty(item.ImportKey))
                .GroupBy(item => item.ImportKey, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            Undo.RecordObject(catalog, "Импорт пакета рисунков");
            ArtAssetImportResult result = ArtAssetImporter.Import(catalog, plan,
                group => existing.TryGetValue(group.Key, out ArtAssetDefinition found) ? found : null, !Application.isBatchMode);
            foreach (ArtAssetImportGroup group in plan.Groups)
            {
                ArtAssetDefinition asset = catalog.assets.FirstOrDefault(item => item != null && string.Equals(item.ImportKey, group.Key, StringComparison.OrdinalIgnoreCase));
                if (asset == null) continue;
                Entry entry = byGroup[group];
                bool created = result.Created.Contains(asset.Id);
                asset.Name = entry.Name;
                asset.MainView = ArtAssetView.Front;
                asset.SourceFolder = string.Empty;
                if (created)
                {
                    asset.Category = CategoryOf(entry);
                    asset.Purpose = PurposeOf(entry);
                    asset.Tags = new List<string> { PackTag, entry.Id };
                    asset.Credit = "Kingdom Survival · пакет ассетов арены, первый набор";
                    asset.LitBySun = true;
                    asset.LitByFire = true;
                    SetVisibleHeight(asset, HeightOf(entry));
                    ArtAssetImporter.InitializePivot(asset);
                }
            }
            catalog.MarkChanged();
            if (save)
            {
                EditorUtility.SetDirty(catalog);
                AssetDatabase.SaveAssets();
            }
            return $"Пакет «{Path.GetFileName(folder)}»: создано {result.Created.Count}, обновлено {result.Updated.Count}, " +
                   $"очищено пикселей каймы {defringed}" + (missing.Count > 0 ? ", нет файлов: " + string.Join(", ", missing) : "") +
                   (result.Errors.Count > 0 ? ". Ошибки: " + string.Join("; ", result.Errors) : "") +
                   (result.Warnings.Count > 0 ? ". Замечания: " + string.Join("; ", result.Warnings.Take(5)) : "") + ".";
        }

        public static string PackKey(Entry entry) => "pack69_" + ArtAssetImportParser.NormalizeKey(entry.Id);

        // Высота — по видимому силуэту, прозрачные поля холста не в счёт.
        private static void SetVisibleHeight(ArtAssetDefinition asset, float height)
        {
            Sprite sprite = asset.MainSprite(asset.PrimaryView);
            if (sprite == null || height <= 0) return;
            string path = AssetDatabase.GetAssetPath(sprite.texture);
            Texture2D image = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!image.LoadImage(File.ReadAllBytes(path))) return;
                int visible = VisibleRows(image);
                float scale = sprite.rect.height / Mathf.Max(1f, image.height);
                if (visible > 0) asset.PixelsPerUnit = visible * scale / height;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(image);
            }
        }

        public static int VisibleRows(Texture2D image)
        {
            Color32[] pixels = image.GetPixels32();
            int w = image.width, h = image.height, bottom = -1, top = -1;
            for (int row = 0; row < h; row++)
            for (int col = 0; col < w; col++)
            {
                if (pixels[row * w + col].a <= 128) continue;
                if (bottom < 0) bottom = row;
                top = row;
                break;
            }
            return bottom < 0 ? 0 : top - bottom + 1;
        }

        // Кайма генерации: у полупрозрачных краёв яркий посторонний цвет.
        // Цвет такого пикселя заменяется цветом ближайшего непрозрачного
        // (на любом расстоянии); альфа не меняется. Возвращает число пикселей.
        public static int Defringe(string source, string target)
        {
            Texture2D image = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!image.LoadImage(File.ReadAllBytes(source))) throw new InvalidDataException("не PNG: " + source);
                int changed = Defringe(image);
                File.WriteAllBytes(target, image.EncodeToPNG());
                return changed;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(image);
            }
        }

        // Перенос цвета обходом в ширину от непрозрачных пикселей (8 соседей):
        // каждый полупрозрачный пиксель получает цвет ближайшего непрозрачного
        // на любом расстоянии — далёкая слабая кайма тоже очищается.
        public static int Defringe(Texture2D image)
        {
            Color32[] pixels = image.GetPixels32();
            int w = image.width, h = image.height, changed = 0;
            int[] source = new int[w * h];
            int[] steps = new int[w * h];
            Queue<int> queue = new Queue<int>();
            for (int i = 0; i < source.Length; i++)
            {
                source[i] = pixels[i].a >= 250 ? i : -1;
                if (source[i] >= 0) queue.Enqueue(i);
            }
            while (queue.Count > 0)
            {
                int i = queue.Dequeue();
                int x = i % w, y = i / w;
                for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int xx = x + dx, yy = y + dy;
                    if ((dx == 0 && dy == 0) || xx < 0 || yy < 0 || xx >= w || yy >= h) continue;
                    int j = yy * w + xx;
                    if (source[j] >= 0) continue;
                    source[j] = source[i];
                    steps[j] = steps[i] + 1;
                    queue.Enqueue(j);
                }
            }
            for (int i = 0; i < pixels.Length; i++)
            {
                Color32 p = pixels[i];
                if (p.a == 0 || p.a >= 250 || source[i] < 0) continue;
                // Дальняя пыль генерации (не у края силуэта) остаётся тёмной, как
                // вся пыль холста: красноватая гасится до чёрного, прочая не
                // трогается — иначе слабые точки проявились бы цветом рисунка.
                if (steps[i] > 6)
                {
                    if (p.r > p.g + 40 && p.r > p.b + 40) { pixels[i] = new Color32(0, 0, 0, p.a); changed++; }
                    continue;
                }
                Color32 color = pixels[source[i]];
                pixels[i] = new Color32(color.r, color.g, color.b, p.a);
                changed++;
            }
            image.SetPixels32(pixels);
            image.Apply();
            return changed;
        }
    }
}

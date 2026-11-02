using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace KingdomSurvival.MapLab.Editor
{
    // «Подключить пакет этапа 02»: пользователь выбирает распакованную папку
    // ZIP. Проверяются manifest (schema_version, обязательные поля, пути только
    // внутри выбранной папки), наличие файлов, SHA-256 и размеры PNG. Годные
    // файлы копируются в локальную папку арта, настраивается импорт текстур и
    // обновляются MapLabArtSet и MapLabMarkerSet по стабильным ID. Скрипты из
    // пакета не выполняются, абсолютные пути из JSON не читаются.
    public static class MapLabArtImport
    {
        public const string ArtFolder = "Assets/_Project/Art/MapLabStage02";
        public const string ArtSetPath = ArtFolder + "/MapLabArtSet_Stage02.asset";
        public const string MarkerSetPath = ArtFolder + "/MapLabMarkerSet_Stage02.asset";
        public const string ManifestName = "ASSET_MANIFEST.json";
        public const int SupportedSchema = 1;
        private const string LastFolderKey = "KingdomSurvival.MapLab.Stage02Package";

        private static readonly string[] KnownCategories = { "vegetation", "building", "rock", "cliff", "texture", "figure", "figure_calibration" };

        public sealed class Report
        {
            public readonly List<string> Errors = new List<string>();
            public readonly List<string> Warnings = new List<string>();
            public readonly List<string> Imported = new List<string>();
            public readonly List<string> Unchanged = new List<string>();
            public string PackageRoot;

            public bool Ok => Errors.Count == 0;

            public override string ToString()
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine($"Пакет: {PackageRoot}");
                sb.AppendLine($"Подключено/обновлено: {Imported.Count}, без изменений: {Unchanged.Count}, ошибок: {Errors.Count}, предупреждений: {Warnings.Count}");
                foreach (string e in Errors)
                    sb.AppendLine("Ошибка: " + e);
                foreach (string w in Warnings)
                    sb.AppendLine("Внимание: " + w);
                return sb.ToString();
            }
        }

        // Проверенная запись manifest (без побочных эффектов).
        public sealed class Item
        {
            public string Id;
            public string RelativePath;
            public string FullPath;
            public string Category;
            public string Kind;
            public int Width;
            public int Height;
            public string Sha256;
            public float[] PivotHint;
            public int[] BboxTopLeft;
            public float WorldHeightHint;
            public float CutoffHint = 0.5f;
            public string WrapHint;
            public string Direction;
            public float PitchDown = 50f;
            public string UsageNote;
            public float EdgeDifference;
        }

        [MenuItem("Kingdom Survival/MapLab (эксперимент)/Подключить пакет этапа 02…", priority = 410)]
        public static void ImportMenu()
        {
            string start = EditorPrefs.GetString(LastFolderKey, "");
            string folder = EditorUtility.OpenFolderPanel("Распакованная папка пакета MapLab этапа 02 (с ASSET_MANIFEST.json)", start, "");
            if (string.IsNullOrEmpty(folder))
                return;
            ImportInteractive(folder);
        }

        [MenuItem("Kingdom Survival/MapLab (эксперимент)/Переподключить пакет этапа 02", priority = 411)]
        public static void ReimportMenu()
        {
            string folder = EditorPrefs.GetString(LastFolderKey, "");
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
            {
                ImportMenu();
                return;
            }
            ImportInteractive(folder);
        }

        public static string LastPackageFolder => EditorPrefs.GetString(LastFolderKey, "");

        public static Report ImportInteractive(string folder)
        {
            Report report;
            try
            {
                report = Import(folder, (stage, f) => EditorUtility.DisplayProgressBar("MapLab — пакет этапа 02", stage, f));
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
            if (report.Imported.Count + report.Unchanged.Count > 0)
                EditorPrefs.SetString(LastFolderKey, folder);
            Debug.Log("MapLab: подключение пакета этапа 02\n" + report);
            EditorUtility.DisplayDialog("MapLab — пакет этапа 02", Shorten(report.ToString(), 1800), "OK");
            MapLabView view = MapLabAssets.FindView();
            if (view != null && view.Data != null)
                view.RebuildArt();
            return report;
        }

        // Запуск из командной строки: -executeMethod …MapLabArtImport.ImportBatch -mapLabPackage <папка>
        public static void ImportBatch()
        {
            int code = 0;
            try
            {
                string folder = null;
                string[] args = Environment.GetCommandLineArgs();
                for (int i = 0; i < args.Length - 1; i++)
                    if (args[i] == "-mapLabPackage")
                        folder = args[i + 1];
                Report report = Import(folder, null);
                Debug.Log("MapLab: подключение пакета этапа 02\n" + report);
                if (!report.Ok)
                    code = 2;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                code = 1;
            }
            if (Application.isBatchMode)
                EditorApplication.Exit(code);
        }

        public static Report Import(string packageRoot, Action<string, float> progress)
        {
            Report report = new Report { PackageRoot = packageRoot };
            List<Item> items = ReadManifest(packageRoot, report, out Dictionary<string, object> manifest, out string manifestSha);
            if (items == null)
                return report;
            Directory.CreateDirectory(ArtFolder);
            List<(Item item, string assetPath)> copied = new List<(Item, string)>();
            for (int i = 0; i < items.Count; i++)
            {
                Item item = items[i];
                progress?.Invoke("Проверка " + item.Id, i / (float)items.Count * 0.5f);
                if (!VerifyFile(item, report))
                    continue;
                string assetPath = ArtFolder + "/" + DestinationName(item.RelativePath);
                string full = Path.GetFullPath(assetPath);
                Directory.CreateDirectory(Path.GetDirectoryName(full));
                bool same = File.Exists(full) && Sha256(File.ReadAllBytes(full)) == item.Sha256;
                if (!same)
                    File.Copy(item.FullPath, full, true);
                copied.Add((item, assetPath));
                (same ? report.Unchanged : report.Imported).Add(item.Id);
            }
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            MapLabArtSet set = AssetDatabase.LoadAssetAtPath<MapLabArtSet>(ArtSetPath);
            if (set == null)
            {
                set = ScriptableObject.CreateInstance<MapLabArtSet>();
                AssetDatabase.CreateAsset(set, ArtSetPath);
            }
            Undo.RecordObject(set, "MapLab: подключение пакета");
            set.SchemaVersion = (int)MapLabJson.GetNumber(manifest, "schema_version", 0);
            set.PackageId = MapLabJson.GetString(manifest, "package_id") ?? "";
            set.ManifestSha256 = manifestSha;
            set.ImportVersion++;
            set.ImportedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            for (int i = 0; i < copied.Count; i++)
            {
                (Item item, string assetPath) = copied[i];
                progress?.Invoke("Настройка " + item.Id, 0.5f + i / (float)copied.Count * 0.45f);
                MapLabArtEntry entry = set.Find(item.Id);
                bool fresh = entry == null;
                if (fresh)
                {
                    entry = new MapLabArtEntry { Id = item.Id };
                    set.Entries.Add(entry);
                }
                byte[] bytes = File.ReadAllBytes(item.FullPath);
                FillSource(entry, item, bytes);
                if (fresh)
                    ApplyDefaults(entry, item);
                ConfigureImporter(assetPath, item, entry);
                entry.Texture = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
                if (entry.Texture == null)
                    report.Errors.Add($"{item.Id}: Unity не импортировала {assetPath}.");
            }
            // Записи, файлы которых в этот раз не прошли проверку, остаются с
            // прежней (проверенной ранее) картинкой или без неё — тогда fallback.
            foreach (MapLabArtEntry e in set.Entries)
                if (e != null && !e.HasTexture)
                    report.Warnings.Add($"{e.Id}: нет картинки — используется процедурный вариант.");

            RegisterFigure(set, report);
            EditorUtility.SetDirty(set);
            MapLabMarkerSet markers = EnsureMarkerSet(set);
            AssetDatabase.SaveAssets();
            AssignToScene(set, markers);
            progress?.Invoke("Готово", 1f);
            return report;
        }

        // ---------- manifest ----------

        public static List<Item> ReadManifest(string packageRoot, Report report, out Dictionary<string, object> manifest, out string manifestSha)
        {
            manifest = null;
            manifestSha = "";
            if (string.IsNullOrEmpty(packageRoot) || !Directory.Exists(packageRoot))
            {
                report.Errors.Add("Папка пакета не найдена: " + packageRoot);
                return null;
            }
            string root = Path.GetFullPath(packageRoot);
            string manifestPath = Path.Combine(root, ManifestName);
            if (!File.Exists(manifestPath))
            {
                // Пользователь мог выбрать папку уровнем выше (как в ZIP).
                foreach (string dir in Directory.GetDirectories(root))
                {
                    if (File.Exists(Path.Combine(dir, ManifestName)))
                    {
                        root = Path.GetFullPath(dir);
                        manifestPath = Path.Combine(root, ManifestName);
                        break;
                    }
                }
            }
            if (!File.Exists(manifestPath))
            {
                report.Errors.Add($"В выбранной папке нет {ManifestName}.");
                return null;
            }
            report.PackageRoot = root;
            byte[] manifestBytes = File.ReadAllBytes(manifestPath);
            manifestSha = Sha256(manifestBytes);
            try
            {
                manifest = MapLabJson.AsObject(MapLabJson.Parse(Encoding.UTF8.GetString(manifestBytes).TrimStart('﻿')));
            }
            catch (FormatException exception)
            {
                report.Errors.Add("manifest не читается: " + exception.Message);
                return null;
            }
            if (manifest == null)
            {
                report.Errors.Add("manifest: корень JSON — не объект.");
                return null;
            }
            if (!MapLabJson.TryGetNumber(manifest, "schema_version", out double schema))
            {
                report.Errors.Add("manifest: нет schema_version.");
                return null;
            }
            if ((int)schema != SupportedSchema)
            {
                report.Errors.Add($"manifest: schema_version {schema} не поддерживается (ожидается {SupportedSchema}).");
                return null;
            }
            List<object> rawItems = MapLabJson.AsArray(manifest.TryGetValue("items", out object v) ? v : null);
            if (rawItems == null)
            {
                report.Errors.Add("manifest: нет массива items.");
                return null;
            }
            if (MapLabJson.TryGetNumber(manifest, "asset_count", out double count) && (int)count != rawItems.Count)
                report.Warnings.Add($"manifest: asset_count = {count}, а записей {rawItems.Count}.");
            List<Item> items = new List<Item>();
            HashSet<string> ids = new HashSet<string>();
            for (int i = 0; i < rawItems.Count; i++)
            {
                Dictionary<string, object> raw = MapLabJson.AsObject(rawItems[i]);
                Item item = ParseItem(raw, root, i, report);
                if (item == null)
                    continue;
                if (!ids.Add(item.Id))
                {
                    report.Errors.Add($"{item.Id}: ID повторяется в manifest.");
                    continue;
                }
                items.Add(item);
            }
            return items;
        }

        private static Item ParseItem(Dictionary<string, object> raw, string root, int index, Report report)
        {
            if (raw == null)
            {
                report.Errors.Add($"items[{index}]: не объект.");
                return null;
            }
            string id = MapLabJson.GetString(raw, "id");
            string label = string.IsNullOrEmpty(id) ? $"items[{index}]" : id;
            if (string.IsNullOrEmpty(id) || !IsSafeId(id))
            {
                report.Errors.Add($"{label}: нет id или в нём недопустимые символы.");
                return null;
            }
            string path = MapLabJson.GetString(raw, "path");
            string error = ValidateRelativePath(path);
            if (error != null)
            {
                report.Errors.Add($"{id}: путь «{path}» отклонён — {error}");
                return null;
            }
            string category = MapLabJson.GetString(raw, "category");
            if (Array.IndexOf(KnownCategories, category) < 0)
            {
                report.Errors.Add($"{id}: неизвестная категория «{category}».");
                return null;
            }
            string sha = MapLabJson.GetString(raw, "sha256");
            if (sha == null || sha.Length != 64 || !IsHex(sha))
            {
                report.Errors.Add($"{id}: нет или неверный sha256.");
                return null;
            }
            if (!MapLabJson.TryGetNumber(raw, "width", out double w) || !MapLabJson.TryGetNumber(raw, "height", out double h) || w < 1 || h < 1)
            {
                report.Errors.Add($"{id}: нет width/height.");
                return null;
            }
            string full = Path.GetFullPath(Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar)));
            if (!full.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                report.Errors.Add($"{id}: путь выходит за пределы папки пакета.");
                return null;
            }
            Item item = new Item
            {
                Id = id,
                RelativePath = path,
                FullPath = full,
                Category = category,
                Kind = MapLabJson.GetString(raw, "kind"),
                Width = (int)w,
                Height = (int)h,
                Sha256 = sha.ToLowerInvariant(),
                WrapHint = MapLabJson.GetString(raw, "wrap_hint"),
                Direction = MapLabJson.GetString(raw, "direction"),
                UsageNote = MapLabJson.GetString(raw, "usage_note"),
                WorldHeightHint = (float)MapLabJson.GetNumber(raw, "world_height_hint", 0),
                CutoffHint = Mathf.Clamp((float)MapLabJson.GetNumber(raw, "cutoff_start_hint", 0.5), 0.05f, 0.95f),
                PitchDown = (float)MapLabJson.GetNumber(raw, "intended_pitch_down_degrees", 50),
                EdgeDifference = (float)Math.Max(MapLabJson.GetNumber(raw, "edge_mean_abs_rgb_difference_x", 0), MapLabJson.GetNumber(raw, "edge_mean_abs_rgb_difference_y", 0)),
            };
            double[] pivot = MapLabJson.GetNumbers(raw, "pivot_hint_unity_bottom_left");
            if (pivot != null && pivot.Length == 2)
                item.PivotHint = new[] { (float)pivot[0], (float)pivot[1] };
            double[] bbox = MapLabJson.GetNumbers(raw, "measured_bbox_alpha160_top_left");
            if (bbox != null && bbox.Length == 4)
                item.BboxTopLeft = new[] { (int)bbox[0], (int)bbox[1], (int)bbox[2], (int)bbox[3] };
            if (category != "texture" && item.PivotHint == null)
                report.Warnings.Add($"{id}: нет pivot_hint — опора будет найдена по силуэту.");
            return item;
        }

        // Относительный путь внутри пакета: без диска, корня, «..», только PNG.
        public static string ValidateRelativePath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return "пустой путь";
            if (path.IndexOf(':') >= 0 || path.StartsWith("/") || path.StartsWith("\\") || path.StartsWith("~"))
                return "абсолютный путь не принимается";
            string[] parts = path.Replace('\\', '/').Split('/');
            foreach (string part in parts)
                if (part == ".." || part == "." || part.Length == 0)
                    return "недопустимый сегмент пути";
            if (!path.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                return "ожидается PNG";
            if (path.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
                return "недопустимые символы";
            return null;
        }

        private static bool IsSafeId(string id)
        {
            foreach (char c in id)
                if (!(char.IsLetterOrDigit(c) || c == '_' || c == '-'))
                    return false;
            return id.Length <= 64;
        }

        private static bool IsHex(string s)
        {
            foreach (char c in s)
                if (!Uri.IsHexDigit(c))
                    return false;
            return true;
        }

        private static string DestinationName(string relative)
        {
            string p = relative.Replace('\\', '/');
            return p.StartsWith("Assets/") ? p.Substring("Assets/".Length) : p;
        }

        public static bool VerifyFile(Item item, Report report)
        {
            if (!File.Exists(item.FullPath))
            {
                report.Errors.Add($"{item.Id}: файл не найден ({item.RelativePath}) — останется прежняя картинка или процедурный вариант.");
                return false;
            }
            byte[] bytes = File.ReadAllBytes(item.FullPath);
            string hash = Sha256(bytes);
            if (hash != item.Sha256)
            {
                report.Errors.Add($"{item.Id}: SHA-256 не совпадает с manifest — файл не подключён.");
                return false;
            }
            if (!TryReadPngSize(bytes, out int w, out int h))
            {
                report.Errors.Add($"{item.Id}: это не PNG.");
                return false;
            }
            if (w != item.Width || h != item.Height)
            {
                report.Errors.Add($"{item.Id}: размер {w}×{h} не совпадает с manifest ({item.Width}×{item.Height}).");
                return false;
            }
            return true;
        }

        public static bool TryReadPngSize(byte[] bytes, out int width, out int height)
        {
            width = height = 0;
            byte[] signature = { 137, 80, 78, 71, 13, 10, 26, 10 };
            if (bytes == null || bytes.Length < 24)
                return false;
            for (int i = 0; i < 8; i++)
                if (bytes[i] != signature[i])
                    return false;
            if (bytes[12] != 'I' || bytes[13] != 'H' || bytes[14] != 'D' || bytes[15] != 'R')
                return false;
            width = bytes[16] << 24 | bytes[17] << 16 | bytes[18] << 8 | bytes[19];
            height = bytes[20] << 24 | bytes[21] << 16 | bytes[22] << 8 | bytes[23];
            return width > 0 && height > 0;
        }

        public static string Sha256(byte[] bytes)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(bytes);
                StringBuilder sb = new StringBuilder(64);
                foreach (byte b in hash)
                    sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        // ---------- записи ArtSet ----------

        private static MapLabArtCategory Category(string category)
        {
            switch (category)
            {
                case "building": return MapLabArtCategory.Building;
                case "rock": return MapLabArtCategory.Rock;
                case "cliff": return MapLabArtCategory.Cliff;
                case "texture": return MapLabArtCategory.Texture;
                case "figure": return MapLabArtCategory.Figure;
                case "figure_calibration": return MapLabArtCategory.FigureCalibration;
                default: return MapLabArtCategory.Vegetation;
            }
        }

        private static MapLabObjectKind Kind(string kind)
        {
            switch (kind)
            {
                case "Conifer": return MapLabObjectKind.Conifer;
                case "Bush": return MapLabObjectKind.Bush;
                case "House": return MapLabObjectKind.House;
                case "Barn": return MapLabObjectKind.Barn;
                case "Boulder": return MapLabObjectKind.Boulder;
                case "Reed": return MapLabObjectKind.Reed;
                case "Haystack": return MapLabObjectKind.Haystack;
                default: return MapLabObjectKind.Deciduous;
            }
        }

        private static void FillSource(MapLabArtEntry entry, Item item, byte[] bytes)
        {
            entry.Category = Category(item.Category);
            entry.Kind = Kind(item.Kind);
            entry.ManifestKind = item.Kind ?? "";
            entry.SourcePath = item.RelativePath;
            entry.Sha256 = item.Sha256;
            entry.Width = item.Width;
            entry.Height = item.Height;
            entry.Direction = item.Direction ?? "";
            entry.PitchDownDegrees = item.PitchDown;
            entry.Notes = item.UsageNote ?? "";
            entry.TileEdgeDifference = item.EdgeDifference;
            if (item.PivotHint != null)
                entry.PivotHint = new Vector2(item.PivotHint[0], item.PivotHint[1]);
            MapLabArtAnalysis.Result analysis = MapLabArtAnalysis.Analyze(bytes, 160);
            entry.SolidRect = analysis.Solid;
            entry.MeanColor = analysis.Mean;
            if (item.BboxTopLeft != null && entry.Category != MapLabArtCategory.Texture)
            {
                // Сверка своего измерения с manifest (alpha ≥ 160): расхождение — повод проверить.
                int[] b = item.BboxTopLeft;
                Rect m = new Rect(b[0] / (float)item.Width, 1f - b[3] / (float)item.Height, (b[2] - b[0]) / (float)item.Width, (b[3] - b[1]) / (float)item.Height);
                if (Mathf.Abs(m.yMin - analysis.Solid.yMin) > 0.01f || Mathf.Abs(m.height - analysis.Solid.height) > 0.01f)
                    Debug.LogWarning($"MapLab: {item.Id} — силуэт по manifest {m} и по измерению {analysis.Solid} расходятся.");
            }
            entry.FootX = analysis.FootX;
        }

        private static void ApplyDefaults(MapLabArtEntry entry, Item item)
        {
            entry.Cutoff = item.CutoffHint;
            entry.WorldHeight = item.WorldHeightHint > 0f ? item.WorldHeightHint : 4f;
            entry.Pivot = entry.PivotHint;
            entry.Light = MapLabArtLight.BakedInImage;
            switch (entry.Category)
            {
                case MapLabArtCategory.Vegetation:
                    entry.MaxSlope = entry.Kind == MapLabObjectKind.Bush ? 26f : 22f;
                    entry.ScaleRange = entry.Kind == MapLabObjectKind.Bush ? new Vector2(0.8f, 1.2f) : new Vector2(0.85f, 1.12f);
                    entry.Weight = item.Id.Contains("Oak") ? 1f : item.Id.Contains("Spreading") ? 0.45f : item.Id.Contains("Birch") ? 0.35f : 1f;
                    break;
                case MapLabArtCategory.Building:
                    entry.MaxSlope = 12f;
                    entry.ScaleRange = new Vector2(0.96f, 1.04f);
                    break;
                case MapLabArtCategory.Rock:
                    entry.MaxSlope = 32f;
                    entry.ScaleRange = new Vector2(0.65f, 1.15f);
                    break;
                case MapLabArtCategory.Cliff:
                    entry.MaxSlope = 60f;
                    entry.ScaleRange = new Vector2(0.9f, 1.1f);
                    break;
                case MapLabArtCategory.Figure:
                case MapLabArtCategory.FigureCalibration:
                    entry.MaxSlope = 35f;
                    entry.ScaleRange = Vector2.one;
                    break;
                case MapLabArtCategory.Texture:
                    entry.Pivot = Vector2.zero;
                    break;
            }
            float drawnWidth = entry.CardHeight * entry.Aspect * entry.SolidRect.width;
            entry.Footprint = new Vector2(drawnWidth * 0.72f, drawnWidth * 0.5f);
        }

        // Регистрация шести кадров фигурки: опора — центр основания (между
        // ступнями) по силуэту, рост выравнивается к медиане. Ширина не
        // нормализуется: боковой вид естественно уже.
        private static void RegisterFigure(MapLabArtSet set, Report report)
        {
            List<MapLabArtEntry> figure = new List<MapLabArtEntry>();
            foreach (MapLabArtEntry e in set.Entries)
                if (e != null && (e.Category == MapLabArtCategory.Figure || e.Category == MapLabArtCategory.FigureCalibration) && e.Width > 0)
                    figure.Add(e);
            if (figure.Count == 0)
                return;
            List<float> heights = new List<float>();
            foreach (MapLabArtEntry e in figure)
                if (e.Category == MapLabArtCategory.Figure)
                    heights.Add(e.SolidRect.height);
            heights.Sort();
            float median = heights.Count > 0 ? heights[heights.Count / 2] : 0f;
            StringBuilder sb = new StringBuilder("Регистрация фигурки (рост силуэта, поправка, опора):");
            foreach (MapLabArtEntry e in figure)
            {
                if (!e.PivotEdited)
                {
                    e.Pivot = new Vector2(e.FootX, e.SolidRect.yMin + e.SolidRect.height * 0.035f);
                    e.HeightCorrection = e.Category == MapLabArtCategory.Figure && median > 0f ? Mathf.Clamp(median / Mathf.Max(0.01f, e.SolidRect.height), 0.9f, 1.1f) : 1f;
                }
                sb.Append($"\n  {e.Id}: {e.SolidRect.height:0.000} → ×{e.HeightCorrection:0.000}, опора ({e.Pivot.x:0.000}; {e.Pivot.y:0.000}), подсказка ({e.PivotHint.x:0.000}; {e.PivotHint.y:0.000})");
            }
            report.Warnings.Add(sb.ToString());
        }

        private static void ConfigureImporter(string assetPath, Item item, MapLabArtEntry entry)
        {
            TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null)
                return;
            bool texture = entry.Category == MapLabArtCategory.Texture;
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = true;
            importer.alphaSource = texture ? TextureImporterAlphaSource.None : TextureImporterAlphaSource.FromInput;
            // Цвет затекает в прозрачные пиксели — без тёмной каймы при фильтрации.
            importer.alphaIsTransparency = !texture;
            importer.mipmapEnabled = true;
            importer.mipmapFilter = TextureImporterMipFilter.KaiserFilter;
            // Площадь силуэта сохраняется в мипах при данном пороге отсечения.
            importer.mipMapsPreserveCoverage = !texture;
            importer.alphaTestReferenceValue = entry.Cutoff;
            importer.wrapMode = texture || item.WrapHint == "Repeat" ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Trilinear;
            importer.anisoLevel = texture ? 4 : 2;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.maxTextureSize = 2048;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.isReadable = false;
            importer.SaveAndReimport();
        }

        private static MapLabMarkerSet EnsureMarkerSet(MapLabArtSet set)
        {
            MapLabMarkerSet markers = AssetDatabase.LoadAssetAtPath<MapLabMarkerSet>(MarkerSetPath);
            if (markers == null)
            {
                markers = ScriptableObject.CreateInstance<MapLabMarkerSet>();
                AssetDatabase.CreateAsset(markers, MarkerSetPath);
            }
            markers.ArtSet = set;
            if (markers.ViewIds == null || markers.ViewIds.Length != 6)
                markers.ViewIds = new string[6];
            foreach (MapLabArtEntry e in set.Entries)
            {
                if (e == null)
                    continue;
                if (e.Category == MapLabArtCategory.Figure)
                {
                    int index = Array.IndexOf(MapLabMarkerSet.DirectionNames, e.Direction);
                    if (index >= 0)
                        markers.ViewIds[index] = e.Id;
                }
                else if (e.Category == MapLabArtCategory.FigureCalibration)
                {
                    if (Mathf.Abs(e.PitchDownDegrees - 40f) < 1f)
                        markers.Front40Id = e.Id;
                    else if (Mathf.Abs(e.PitchDownDegrees - 60f) < 1f)
                        markers.Front60Id = e.Id;
                }
            }
            EditorUtility.SetDirty(markers);
            return markers;
        }

        private static void AssignToScene(MapLabArtSet set, MapLabMarkerSet markers)
        {
            MapLabView view = MapLabAssets.FindView();
            if (view == null)
                return;
            bool changed = false;
            if (view.ArtSet != set)
            {
                view.ArtSet = set;
                changed = true;
            }
            if (view.MarkerSet != markers)
            {
                view.MarkerSet = markers;
                changed = true;
            }
            if (changed)
            {
                EditorUtility.SetDirty(view);
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(view.gameObject.scene);
            }
        }

        public static MapLabArtSet LoadArtSet() => AssetDatabase.LoadAssetAtPath<MapLabArtSet>(ArtSetPath);

        public static MapLabMarkerSet LoadMarkerSet() => AssetDatabase.LoadAssetAtPath<MapLabMarkerSet>(MarkerSetPath);

        private static string Shorten(string text, int max) => text.Length <= max ? text : text.Substring(0, max) + "\n… (полный отчёт — в Console)";
    }
}

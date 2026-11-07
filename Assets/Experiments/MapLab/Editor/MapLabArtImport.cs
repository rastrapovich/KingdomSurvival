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
        public const string Stage03Folder = "Assets/_Project/Art/MapLabStage03";
        public const string Stage03SetPath = Stage03Folder + "/MapLabArtSet_Stage03.asset";
        public const string Stage03MarkerPath = Stage03Folder + "/MapLabMarkerSet_Stage03.asset";
        public const string ManifestName = "ASSET_MANIFEST.json";
        public const int SupportedSchema = 1;
        private const string LastFolderKey = "KingdomSurvival.MapLab.Stage02Package";

        // Этап импорта: своя папка, свой набор и допустимые ревизии пакета.
        // Пакет этапа 03 не попадает в папку и набор этапа 02.
        public sealed class Profile
        {
            public string Name;
            public string Folder;
            public string SetPath;
            public string MarkerPath;
            public string LastFolderKey;
            public int MinRevision;
            public int MaxRevision;
            // Необязательные копии этапа 02 (тот же SHA-256 уже подключён в
            // этапе 02) не копируются повторно: остаются прежние материалы,
            // фишка и их калибровка.
            public bool ReuseStage02;
            // Опора фигурок — по подсказке manifest (нижняя зона сапог), без плинта.
            public bool BootHintPivots;
            // Этап 05: набор, из которого берутся записи с тем же SHA-256 (без
            // копии файла и с сохранённой калибровкой), и регистрация построек
            // по углам основания.
            public string ReuseSetPath;
            public bool RegisterBuildings;
        }

        public static readonly Profile Stage02 = new Profile
        {
            Name = "этап 02", Folder = ArtFolder, SetPath = ArtSetPath, MarkerPath = MarkerSetPath,
            LastFolderKey = LastFolderKey, MinRevision = 0, MaxRevision = 2,
        };

        public static readonly Profile Stage03 = new Profile
        {
            Name = "этап 03", Folder = Stage03Folder, SetPath = Stage03SetPath, MarkerPath = Stage03MarkerPath,
            LastFolderKey = "KingdomSurvival.MapLab.Stage03Package", MinRevision = 3, MaxRevision = 3, ReuseStage02 = true,
        };

        public const string Stage04Folder = "Assets/_Project/Art/MapLabStage04";
        public const string Stage04SetPath = Stage04Folder + "/MapLabArtSet_Stage04.asset";
        public const string Stage04MarkerPath = Stage04Folder + "/MapLabMarkerSet_Stage04.asset";

        // Этап 04: герой — два полных набора направлений (50° и 35°) в своём
        // наборе фишки; наборы этапов 02 и 03 и их калибровка не меняются.
        public static readonly Profile Stage04 = new Profile
        {
            Name = "этап 04", Folder = Stage04Folder, SetPath = Stage04SetPath, MarkerPath = Stage04MarkerPath,
            LastFolderKey = "KingdomSurvival.MapLab.Stage04Package", MinRevision = 4, MaxRevision = 4, BootHintPivots = true,
        };

        public const string Stage05Folder = "Assets/_Project/Art/MapLabStage05";
        public const string Stage05SetPath = Stage05Folder + "/MapLabArtSet_Stage05.asset";
        public const string Stage05MarkerPath = Stage05Folder + "/MapLabMarkerSet_Stage05.asset";

        // Этап 05 (package_revision 5, art_revision 2): трава, тростник, камни,
        // сено, декали, модули оград и мостов, постройки в четырёх ракурсах,
        // реквизит двора. Шесть кадров героя P35 — те же файлы, что в этапе 04:
        // берутся из набора этапа 04 по SHA-256 (не дублируются).
        public static readonly Profile Stage05 = new Profile
        {
            Name = "этап 05", Folder = Stage05Folder, SetPath = Stage05SetPath, MarkerPath = Stage05MarkerPath,
            LastFolderKey = "KingdomSurvival.MapLab.Stage05Package", MinRevision = 5, MaxRevision = 5, BootHintPivots = true,
            ReuseSetPath = Stage04SetPath, RegisterBuildings = true,
        };

        // Вызывается из окна «Генератор карты» → «Сервис» (отдельного пункта меню нет).
        public static void ImportStage05Menu()
        {
            string start = EditorPrefs.GetString(Stage05.LastFolderKey, "");
            string folder = EditorUtility.OpenFolderPanel("Распакованная папка пакета MapLab этапа 05 (с ASSET_MANIFEST.json)", start, "");
            if (string.IsNullOrEmpty(folder))
                return;
            ImportInteractive(folder, Stage05);
        }

        // Вызывается из окна «Генератор карты» → «Сервис» (отдельного пункта меню нет).
        public static void ReimportStage05Menu()
        {
            string folder = EditorPrefs.GetString(Stage05.LastFolderKey, "");
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
            {
                ImportStage05Menu();
                return;
            }
            ImportInteractive(folder, Stage05);
        }

        public static string LastStage05Folder => EditorPrefs.GetString(Stage05.LastFolderKey, "");

        public static MapLabArtSet LoadStage05Set() => AssetDatabase.LoadAssetAtPath<MapLabArtSet>(Stage05SetPath);

        public static MapLabMarkerSet LoadStage05Markers() => AssetDatabase.LoadAssetAtPath<MapLabMarkerSet>(Stage05MarkerPath);

        public const string Cliffs06Folder = "Assets/_Project/Art/MapLabCliffs06";
        public const string Cliffs06SetPath = Cliffs06Folder + "/MapLabArtSet_Cliffs06.asset";

        // Ревизия 06: рисованные отвесные скалы (cliff_module) — стены, торцы,
        // контрфорс вида спереди и осыпи 50°. Свой набор; наборы этапов 02–05
        // не меняются. Фишки в пакете нет.
        public static readonly Profile Cliffs06 = new Profile
        {
            Name = "скалы 06", Folder = Cliffs06Folder, SetPath = Cliffs06SetPath, MarkerPath = Cliffs06Folder + "/MapLabMarkerSet_Cliffs06.asset",
            LastFolderKey = "KingdomSurvival.MapLab.Cliffs06Package", MinRevision = 6, MaxRevision = 99,
        };

        // Вызывается из окна «Генератор карты» → «Сервис» (отдельного пункта меню нет).
        public static void ImportCliffs06Menu()
        {
            string start = EditorPrefs.GetString(Cliffs06.LastFolderKey, "");
            string folder = EditorUtility.OpenFolderPanel("Распакованная папка пакета скал MapLab ревизии 06 (с ASSET_MANIFEST.json)", start, "");
            if (string.IsNullOrEmpty(folder))
                return;
            ImportInteractive(folder, Cliffs06);
        }

        // Вызывается из окна «Генератор карты» → «Сервис» (отдельного пункта меню нет).
        public static void ReimportCliffs06Menu()
        {
            string folder = EditorPrefs.GetString(Cliffs06.LastFolderKey, "");
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
            {
                ImportCliffs06Menu();
                return;
            }
            ImportInteractive(folder, Cliffs06);
        }

        public static string LastCliffs06Folder => EditorPrefs.GetString(Cliffs06.LastFolderKey, "");

        public static MapLabArtSet LoadCliffs06Set() => AssetDatabase.LoadAssetAtPath<MapLabArtSet>(Cliffs06SetPath);

        // Ревизия 06 пишет bbox как [x, y, ширина, высота] (coordinate_system).
        private static bool bboxIsSize;

        // Вызывается из окна «Генератор карты» → «Сервис» (отдельного пункта меню нет).
        public static void ImportStage04Menu()
        {
            string start = EditorPrefs.GetString(Stage04.LastFolderKey, "");
            string folder = EditorUtility.OpenFolderPanel("Распакованная папка пакета MapLab этапа 04 (с ASSET_MANIFEST.json)", start, "");
            if (string.IsNullOrEmpty(folder))
                return;
            ImportInteractive(folder, Stage04);
        }

        // Вызывается из окна «Генератор карты» → «Сервис» (отдельного пункта меню нет).
        public static void ReimportStage04Menu()
        {
            string folder = EditorPrefs.GetString(Stage04.LastFolderKey, "");
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
            {
                ImportStage04Menu();
                return;
            }
            ImportInteractive(folder, Stage04);
        }

        public static string LastStage04Folder => EditorPrefs.GetString(Stage04.LastFolderKey, "");

        private static readonly string[] KnownCategories = { "vegetation", "building", "rock", "cliff", "texture", "figure", "figure_calibration", "prop", "decal", "fence_module", "bridge_module", "cliff_module" };

        public sealed class Report
        {
            public readonly List<string> Errors = new List<string>();
            public readonly List<string> Warnings = new List<string>();
            public readonly List<string> Imported = new List<string>();
            public readonly List<string> Unchanged = new List<string>();
            public readonly List<string> Reused = new List<string>();
            public string PackageRoot;

            public bool Ok => Errors.Count == 0;

            public override string ToString()
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine($"Пакет: {PackageRoot}");
                sb.AppendLine($"Подключено/обновлено: {Imported.Count}, без изменений: {Unchanged.Count}, взято из прежних наборов (тот же SHA-256): {Reused.Count}, ошибок: {Errors.Count}, предупреждений: {Warnings.Count}");
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
            public string Role;
            public string VariantGroup;
            public string Provenance;
            public bool Optional;
            // Этап 05.
            public string AuthoringPlane;
            public string ViewTag;
            public string EntranceLocation;
            public float[] ModuleBox;      // visual_bbox: доли холста, левый верхний угол
            public float[] OpacityRange;
            // Ревизия 06 (скалы).
            public string HeightClass;
            public string Side;
            public float RockTopPx = -1f;
            public float BasePx = -1f;
            public float PxPerUnit = 100f;
            public float WorldWidthHint;
            public bool HasPitch;
        }

        // Вызывается из окна «Генератор карты» → «Сервис» (отдельного пункта меню нет).
        public static void ImportMenu()
        {
            string start = EditorPrefs.GetString(LastFolderKey, "");
            string folder = EditorUtility.OpenFolderPanel("Распакованная папка пакета MapLab этапа 02 (с ASSET_MANIFEST.json)", start, "");
            if (string.IsNullOrEmpty(folder))
                return;
            ImportInteractive(folder);
        }

        // Вызывается из окна «Генератор карты» → «Сервис» (отдельного пункта меню нет).
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

        // Вызывается из окна «Генератор карты» → «Сервис» (отдельного пункта меню нет).
        public static void ImportStage03Menu()
        {
            string start = EditorPrefs.GetString(Stage03.LastFolderKey, "");
            string folder = EditorUtility.OpenFolderPanel("Распакованная папка пакета MapLab этапа 03 (с ASSET_MANIFEST.json)", start, "");
            if (string.IsNullOrEmpty(folder))
                return;
            ImportInteractive(folder, Stage03);
        }

        // Вызывается из окна «Генератор карты» → «Сервис» (отдельного пункта меню нет).
        public static void ReimportStage03Menu()
        {
            string folder = EditorPrefs.GetString(Stage03.LastFolderKey, "");
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
            {
                ImportStage03Menu();
                return;
            }
            ImportInteractive(folder, Stage03);
        }

        public static string LastPackageFolder => EditorPrefs.GetString(LastFolderKey, "");

        public static string LastStage03Folder => EditorPrefs.GetString(Stage03.LastFolderKey, "");

        public static Report ImportInteractive(string folder, Profile profile = null)
        {
            profile = profile ?? Stage02;
            Report report;
            try
            {
                report = Import(folder, (stage, f) => EditorUtility.DisplayProgressBar("MapLab — пакет " + profile.Name, stage, f), profile);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
            if (report.Imported.Count + report.Unchanged.Count + report.Reused.Count > 0)
                EditorPrefs.SetString(profile.LastFolderKey, folder);
            Debug.Log("MapLab: подключение пакета " + profile.Name + "\n" + report);
            EditorUtility.DisplayDialog("MapLab — пакет " + profile.Name, Shorten(report.ToString(), 1800), "OK");
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
                Profile profile = Stage02;
                string[] args = Environment.GetCommandLineArgs();
                for (int i = 0; i < args.Length - 1; i++)
                {
                    if (args[i] == "-mapLabPackage")
                        folder = args[i + 1];
                    if (args[i] == "-mapLabStage" && args[i + 1] == "03")
                        profile = Stage03;
                    if (args[i] == "-mapLabStage" && args[i + 1] == "04")
                        profile = Stage04;
                    if (args[i] == "-mapLabStage" && args[i + 1] == "06")
                        profile = Cliffs06;
                    if (args[i] == "-mapLabStage" && args[i + 1] == "05")
                        profile = Stage05;
                }
                Report report = Import(folder, null, profile);
                Debug.Log("MapLab: подключение пакета " + profile.Name + "\n" + report);
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

        public static Report Import(string packageRoot, Action<string, float> progress, Profile profile = null)
        {
            profile = profile ?? Stage02;
            Report report = new Report { PackageRoot = packageRoot };
            List<Item> items = ReadManifest(packageRoot, report, out Dictionary<string, object> manifest, out string manifestSha);
            if (items == null)
                return report;
            int revision = (int)MapLabJson.GetNumber(manifest, "package_revision", 2);
            if (revision < profile.MinRevision || revision > profile.MaxRevision)
            {
                report.Errors.Add($"Пакет ревизии {revision} не подходит для пункта «{profile.Name}» (ожидается {profile.MinRevision}–{profile.MaxRevision}) — ничего не подключено, выберите пункт меню своего этапа.");
                return report;
            }
            MapLabArtSet stage02 = profile.ReuseStage02 ? AssetDatabase.LoadAssetAtPath<MapLabArtSet>(ArtSetPath) : null;
            MapLabArtSet reuseSet = !string.IsNullOrEmpty(profile.ReuseSetPath) ? AssetDatabase.LoadAssetAtPath<MapLabArtSet>(profile.ReuseSetPath) : null;
            Directory.CreateDirectory(profile.Folder);
            List<(Item item, string assetPath)> copied = new List<(Item, string)>();
            List<(Item item, MapLabArtEntry source)> borrowed = new List<(Item, MapLabArtEntry)>();
            for (int i = 0; i < items.Count; i++)
            {
                Item item = items[i];
                progress?.Invoke("Проверка " + item.Id, i / (float)items.Count * 0.5f);
                if (!VerifyFile(item, report))
                    continue;
                MapLabArtEntry existing = stage02 != null && item.Optional ? stage02.Find(item.Id) : null;
                if (existing != null && existing.HasTexture && existing.Sha256 == item.Sha256)
                {
                    // Тот же файл уже подключён (и, возможно, откалиброван) в этапе 02.
                    report.Reused.Add(item.Id);
                    continue;
                }
                MapLabArtEntry previous = reuseSet != null ? FindBySha(reuseSet, item.Sha256) : null;
                if (previous != null && previous.HasTexture)
                {
                    // Тот же файл уже подключён в наборе предыдущего этапа: запись
                    // ссылается на ту же текстуру, файл не копируется.
                    borrowed.Add((item, previous));
                    report.Reused.Add(item.Id);
                    continue;
                }
                string assetPath = profile.Folder + "/" + DestinationName(item.RelativePath);
                string full = Path.GetFullPath(assetPath);
                Directory.CreateDirectory(Path.GetDirectoryName(full));
                bool same = File.Exists(full) && Sha256(File.ReadAllBytes(full)) == item.Sha256;
                if (!same)
                    File.Copy(item.FullPath, full, true);
                copied.Add((item, assetPath));
                (same ? report.Unchanged : report.Imported).Add(item.Id);
            }
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            MapLabArtSet set = AssetDatabase.LoadAssetAtPath<MapLabArtSet>(profile.SetPath);
            if (set == null)
            {
                set = ScriptableObject.CreateInstance<MapLabArtSet>();
                AssetDatabase.CreateAsset(set, profile.SetPath);
            }
            Undo.RecordObject(set, "MapLab: подключение пакета");
            set.SchemaVersion = (int)MapLabJson.GetNumber(manifest, "schema_version", 0);
            set.PackageRevision = revision;
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
            foreach ((Item item, MapLabArtEntry source) in borrowed)
            {
                MapLabArtEntry entry = set.Find(item.Id);
                if (entry == null)
                {
                    entry = JsonUtility.FromJson<MapLabArtEntry>(JsonUtility.ToJson(source));
                    entry.Id = item.Id;
                    set.Entries.Add(entry);
                }
                entry.Texture = source.Texture;
                entry.ReusedFrom = profile.ReuseSetPath;
                entry.Sha256 = item.Sha256;
                entry.PitchDownDegrees = item.PitchDown;
                entry.Direction = item.Direction ?? entry.Direction;
                entry.VariantGroup = item.VariantGroup ?? entry.VariantGroup;
                entry.SourcePath = item.RelativePath;
                if (!entry.PivotEdited && source.PivotEdited)
                {
                    entry.Pivot = source.Pivot;
                    entry.HeightCorrection = source.HeightCorrection;
                    entry.PivotEdited = true;
                }
            }
            // Записи, файлы которых в этот раз не прошли проверку, остаются с
            // прежней (проверенной ранее) картинкой или без неё — тогда fallback.
            foreach (MapLabArtEntry e in set.Entries)
                if (e != null && !e.HasTexture)
                    report.Warnings.Add($"{e.Id}: нет картинки — используется процедурный вариант.");

            RegisterFigure(set, report, profile.BootHintPivots);
            if (profile.RegisterBuildings)
                RegisterBuildings(set, copied, report);
            EditorUtility.SetDirty(set);
            bool figures = set.Entries.Exists(e => e != null && e.Category == MapLabArtCategory.Figure);
            MapLabMarkerSet markers = figures || profile == Stage02 ? EnsureMarkerSet(set, profile.MarkerPath, manifest, report) : null;
            AssetDatabase.SaveAssets();
            if (profile == Stage04)
                AssignStage04ToScene(markers);
            if (profile == Stage05)
                AssignStage05ToScene(set, markers);
            if (profile == Cliffs06)
                AssignCliffs06ToScene(set);
            if (profile == Stage02)
                AssignToScene(set, markers);
            else if (profile == Stage03)
                AssignStage03ToScene(set);
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
            bboxIsSize = (MapLabJson.GetString(manifest, "coordinate_system") ?? "").Contains("width");
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
                HasPitch = MapLabJson.TryGetNumber(raw, "intended_pitch_down_degrees", out _),
                AuthoringPlane = MapLabJson.GetString(raw, "authoring_plane"),
                ViewTag = MapLabJson.GetString(raw, "view_tag"),
                EntranceLocation = MapLabJson.GetString(raw, "entrance_location"),
                OpacityRange = ToFloats(MapLabJson.GetNumbers(raw, "opacity_start_range"), 2),
                HeightClass = MapLabJson.GetString(raw, "height_class"),
                Side = MapLabJson.GetString(raw, "side"),
                RockTopPx = (float)MapLabJson.GetNumber(raw, "rock_top_y_px", -1),
                BasePx = (float)MapLabJson.GetNumber(raw, "base_y_px", -1),
                PxPerUnit = (float)MapLabJson.GetNumber(raw, "px_per_unit", 100),
                WorldWidthHint = (float)MapLabJson.GetNumber(raw, "world_width_hint", 0),
                EdgeDifference = (float)Math.Max(MapLabJson.GetNumber(raw, "edge_mean_abs_rgb_difference_x", 0), MapLabJson.GetNumber(raw, "edge_mean_abs_rgb_difference_y", 0)),
                Role = MapLabJson.GetString(raw, "role"),
                VariantGroup = MapLabJson.GetString(raw, "variant_group"),
                Provenance = MapLabJson.GetString(raw, "provenance"),
                Optional = MapLabJson.GetBool(raw, "optional", false),
            };
            if (!string.IsNullOrEmpty(item.VariantGroup) && !IsSafeId(item.VariantGroup))
            {
                report.Errors.Add($"{id}: недопустимые символы в variant_group.");
                return null;
            }
            Dictionary<string, object> connection = MapLabJson.AsObject(raw.TryGetValue("connection_hint", out object hint) ? hint : null);
            if (connection != null)
                item.ModuleBox = ToFloats(MapLabJson.GetNumbers(connection, "visual_bbox"), 4);
            double[] pivot = MapLabJson.GetNumbers(raw, "pivot_hint_unity_bottom_left");
            if (pivot != null && pivot.Length == 2)
                item.PivotHint = new[] { (float)pivot[0], (float)pivot[1] };
            double[] bbox = MapLabJson.GetNumbers(raw, "measured_bbox_alpha160_top_left");
            if (bbox != null && bbox.Length == 4)
                item.BboxTopLeft = bboxIsSize
                    ? new[] { (int)bbox[0], (int)bbox[1], (int)(bbox[0] + bbox[2]), (int)(bbox[1] + bbox[3]) }
                    : new[] { (int)bbox[0], (int)bbox[1], (int)bbox[2], (int)bbox[3] };
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

        private static float[] ToFloats(double[] values, int count)
        {
            if (values == null || values.Length != count)
                return null;
            float[] result = new float[count];
            for (int i = 0; i < count; i++)
                result[i] = (float)values[i];
            return result;
        }

        private static MapLabArtEntry FindBySha(MapLabArtSet set, string sha)
        {
            foreach (MapLabArtEntry e in set.Entries)
                if (e != null && e.Sha256 == sha)
                    return e;
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
                case "prop": return MapLabArtCategory.Prop;
                case "decal": return MapLabArtCategory.Decal;
                case "fence_module": return MapLabArtCategory.FenceModule;
                case "bridge_module": return MapLabArtCategory.BridgeModule;
                case "cliff_module": return MapLabArtCategory.CliffModule;
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
                case "Hay": return MapLabObjectKind.Haystack;
                case "Grass": return MapLabObjectKind.Grass;
                default: return MapLabObjectKind.Deciduous;
            }
        }

        private static void FillSource(MapLabArtEntry entry, Item item, byte[] bytes)
        {
            entry.Category = Category(item.Category);
            bool stage05Only = entry.Category == MapLabArtCategory.Prop || entry.Category == MapLabArtCategory.Decal || entry.Category == MapLabArtCategory.FenceModule || entry.Category == MapLabArtCategory.BridgeModule || entry.Category == MapLabArtCategory.CliffModule;
            entry.HeightClass = item.HeightClass ?? "";
            entry.Side = item.Side ?? "";
            entry.RockTopPx = item.RockTopPx;
            entry.BasePx = item.BasePx;
            entry.PxPerUnit = item.PxPerUnit > 0f ? item.PxPerUnit : 100f;
            entry.WorldWidthHint = item.WorldWidthHint;
            entry.Kind = stage05Only ? (item.Kind == "Hay" ? MapLabObjectKind.Haystack : MapLabObjectKind.Prop) : Kind(item.Kind);
            entry.AuthoringPlane = item.AuthoringPlane ?? "";
            entry.ViewTag = item.ViewTag ?? "";
            entry.EntranceLocation = item.EntranceLocation ?? "";
            if (item.ModuleBox != null)
            {
                float[] b = item.ModuleBox;
                entry.ModuleRect = new Rect(b[0], 1f - b[3], b[2] - b[0], b[3] - b[1]);
            }
            if (item.OpacityRange != null)
                entry.OpacityRange = new Vector2(item.OpacityRange[0], item.OpacityRange[1]);
            entry.ManifestKind = item.Kind ?? "";
            entry.SourcePath = item.RelativePath;
            entry.Sha256 = item.Sha256;
            entry.Width = item.Width;
            entry.Height = item.Height;
            entry.Direction = item.Direction ?? "";
            entry.PitchDownDegrees = item.PitchDown;
            entry.Notes = item.UsageNote ?? "";
            entry.TileEdgeDifference = item.EdgeDifference;
            entry.Role = item.Role ?? "";
            entry.VariantGroup = item.VariantGroup ?? "";
            entry.Provenance = item.Provenance ?? "";
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
                    // Начальный вес — данные набора (правится в калибровке); выбор в
                    // сцене этапа 03 идёт только по Weight, без проверок имён.
                    entry.Weight = InitialWeight(item.Id);
                    break;
                case MapLabArtCategory.Building:
                    entry.MaxSlope = 12f;
                    entry.ScaleRange = new Vector2(0.96f, 1.04f);
                    // Подсказка manifest — низ силуэта (передний угол сруба); опора
                    // пары ракурсов — середина основания, немного выше.
                    if (!string.IsNullOrEmpty(item.VariantGroup))
                        entry.Pivot = new Vector2(entry.SolidRect.center.x, entry.SolidRect.yMin + entry.SolidRect.height * 0.21f);
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
                case MapLabArtCategory.Prop:
                    entry.MaxSlope = 16f;
                    entry.ScaleRange = new Vector2(0.92f, 1.08f);
                    break;
                case MapLabArtCategory.Decal:
                    // Полный градиент альфы, без порога.
                    entry.Cutoff = 0.05f;
                    entry.Pivot = new Vector2(0.5f, 0.5f);
                    break;
                case MapLabArtCategory.FenceModule:
                case MapLabArtCategory.BridgeModule:
                    entry.Pivot = new Vector2(entry.ModuleRect.center.x, entry.ModuleRect.yMin);
                    entry.ScaleRange = Vector2.one;
                    break;
                case MapLabArtCategory.CliffModule:
                    // Полный холст и привязки manifest (не обрезать по альфе); осыпь —
                    // спрайт 50° с опорой в нижнем центре видимого рисунка.
                    entry.Pivot = entry.PivotHint;
                    entry.ScaleRange = entry.Role == "cliff_scree" ? new Vector2(0.85f, 1.15f) : Vector2.one;
                    entry.MaxSlope = 40f;
                    break;
            }
            // Этап 05: корни травы, тростника, камней и реквизита — чуть ниже
            // нижнего края силуэта (подсказка manifest — сам нижний край).
            if (item.HasPitch && string.IsNullOrEmpty(item.VariantGroup) && (entry.Category == MapLabArtCategory.Vegetation || entry.Category == MapLabArtCategory.Rock || entry.Category == MapLabArtCategory.Prop))
            {
                float sink = entry.Category == MapLabArtCategory.Rock ? 0.08f : 0.04f;
                entry.Pivot = new Vector2(entry.PivotHint.x, entry.SolidRect.yMin + entry.SolidRect.height * sink);
            }
            float drawnWidth = entry.CardHeight * entry.Aspect * entry.SolidRect.width;
            entry.Footprint = new Vector2(drawnWidth * 0.72f, drawnWidth * 0.5f);
        }

        public static float InitialWeight(string id)
        {
            switch (id)
            {
                case "Tree_Spreading_01": return 0.45f;
                case "Tree_Birch_01": return 0.35f;
                case "Tree_Broad_Wide_C": return 0.8f;
                case "Tree_Broad_Elongated_B": return 0.7f;
                case "Tree_Broad_Leaning_D": return 0.35f;
                default: return 1f;
            }
        }

        // Регистрация шести кадров фигурки: опора — центр основания (между
        // ступнями) по силуэту, рост выравнивается к медиане. Ширина не
        // нормализуется: боковой вид естественно уже.
        // bootHints (этап 04): опора — подсказка manifest по нижней зоне сапог
        // (середина между ними), чуть выше подошвы переднего сапога; плинта нет.
        private static void RegisterFigure(MapLabArtSet set, Report report, bool bootHints = false)
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
                    e.Pivot = bootHints ? new Vector2(e.PivotHint.x, e.PivotHint.y + e.SolidRect.height * 0.015f) : new Vector2(e.FootX, e.SolidRect.yMin + e.SolidRect.height * 0.035f);
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
            switch (entry.Category)
            {
                case MapLabArtCategory.Decal:
                    // Декаль запекается в краску на CPU: читаемая, без сжатия; плавная
                    // альфа — без сохранения покрытия (порога нет).
                    importer.isReadable = true;
                    importer.textureCompression = TextureImporterCompression.Uncompressed;
                    importer.mipMapsPreserveCoverage = false;
                    importer.maxTextureSize = 512;
                    break;
                case MapLabArtCategory.FenceModule:
                case MapLabArtCategory.BridgeModule:
                    // Поверхности под углом к камере — анизотропия против мыла.
                    importer.anisoLevel = 8;
                    importer.maxTextureSize = 1024;
                    break;
                case MapLabArtCategory.CliffModule:
                    // Полный холст 2400 px (без уменьшения до 2048 — стыки по краю
                    // точны), Clamp: куски стыкуются по совпадающим крайним столбцам.
                    importer.anisoLevel = 8;
                    importer.maxTextureSize = 4096;
                    importer.wrapMode = TextureWrapMode.Clamp;
                    break;
                case MapLabArtCategory.Prop:
                    importer.maxTextureSize = 1024;
                    break;
                case MapLabArtCategory.Vegetation:
                case MapLabArtCategory.Rock:
                    if (item.Kind == "Grass" || item.Kind == "Reed" || item.Kind == "Boulder")
                        importer.maxTextureSize = 1024;
                    // R03: контурная трава — тонкие штрихи с мягкой альфой. Без
                    // сохранения покрытия под порог (оно утолщает и рвёт линию в
                    // мипах): средняя альфа мипа плавно ослабляет штрих вдали.
                    if (item.Role == "grass_ink_tuft")
                        importer.mipMapsPreserveCoverage = false;
                    break;
            }
            importer.SaveAndReimport();
        }

        private static MapLabMarkerSet EnsureMarkerSet(MapLabArtSet set, string path, Dictionary<string, object> manifest = null, Report report = null)
        {
            MapLabMarkerSet markers = AssetDatabase.LoadAssetAtPath<MapLabMarkerSet>(path);
            if (markers == null)
            {
                markers = ScriptableObject.CreateInstance<MapLabMarkerSet>();
                AssetDatabase.CreateAsset(markers, path);
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
            ReadPitchSets(markers, set, manifest, report);
            EditorUtility.SetDirty(markers);
            return markers;
        }

        // Этап 04: таблица marker_pitch_sets — по набору на наклон, кадр
        // адресуется парой «наклон + направление». ID проверяются по набору до
        // присвоения (нет записи или картинки — слот пуст и предупреждение).
        // Без таблицы, но с кадрами разных наклонов — наборы по PitchDownDegrees.
        // Прежний ViewIds — набор, ближайший к 50°.
        public static void ReadPitchSets(MapLabMarkerSet markers, MapLabArtSet set, Dictionary<string, object> manifest, Report report)
        {
            List<MapLabMarkerPitchSet> sets = new List<MapLabMarkerPitchSet>();
            List<object> table = manifest != null ? MapLabJson.AsArray(manifest.TryGetValue("marker_pitch_sets", out object raw) ? raw : null) : null;
            if (table != null)
            {
                foreach (object rowObject in table)
                {
                    Dictionary<string, object> row = MapLabJson.AsObject(rowObject);
                    List<object> ids = row != null ? MapLabJson.AsArray(row.TryGetValue("view_ids", out object v) ? v : null) : null;
                    if (row == null || ids == null || ids.Count != 6 || !MapLabJson.TryGetNumber(row, "pitch_down_degrees", out double pitch))
                    {
                        report?.Warnings.Add("marker_pitch_sets: строка без pitch_down_degrees или без шести view_ids — пропущена.");
                        continue;
                    }
                    MapLabMarkerPitchSet ps = new MapLabMarkerPitchSet { Pitch = (float)pitch };
                    for (int k = 0; k < 6; k++)
                    {
                        string id = ids[k] as string;
                        MapLabArtEntry e = id != null ? set.Find(id) : null;
                        if (e == null || !e.HasTexture || e.Category != MapLabArtCategory.Figure)
                        {
                            report?.Warnings.Add($"marker_pitch_sets {pitch}°: кадр «{id}» ({MapLabMarkerSet.DirectionNames[k]}) не подключён — будет приближённая замена.");
                            continue;
                        }
                        ps.ViewIds[k] = id;
                    }
                    sets.Add(ps);
                }
            }
            else
            {
                Dictionary<int, MapLabMarkerPitchSet> byPitch = new Dictionary<int, MapLabMarkerPitchSet>();
                foreach (MapLabArtEntry e in set.Entries)
                {
                    if (e == null || e.Category != MapLabArtCategory.Figure)
                        continue;
                    int index = Array.IndexOf(MapLabMarkerSet.DirectionNames, e.Direction);
                    if (index < 0)
                        continue;
                    int key = Mathf.RoundToInt(e.PitchDownDegrees);
                    if (!byPitch.TryGetValue(key, out MapLabMarkerPitchSet ps))
                        byPitch[key] = ps = new MapLabMarkerPitchSet { Pitch = key };
                    ps.ViewIds[index] = e.Id;
                }
                if (byPitch.Count > 1 || (byPitch.Count == 1 && !byPitch.ContainsKey(50)))
                    sets.AddRange(byPitch.Values);
            }
            sets.Sort((a, b) => b.Pitch.CompareTo(a.Pitch));
            markers.PitchSets = sets;
            if (sets.Count > 0)
            {
                MapLabMarkerPitchSet main = sets[0];
                foreach (MapLabMarkerPitchSet ps in sets)
                    if (Mathf.Abs(ps.Pitch - 50f) < Mathf.Abs(main.Pitch - 50f))
                        main = ps;
                markers.ViewIds = (string[])main.ViewIds.Clone();
                markers.FullSetPitch = main.Pitch;
            }
        }

        // Этап 05: регистрация постройки по углам основания (не по нижнему пикселю).
        // Опора — центр основания; пятно — длина и ширина основания в мире; угол
        // конька — по длинной стороне; вход — фронтон по ракурсу (Front* — видимый,
        // Back* — противоположный скрытый). Ручная опора (PivotEdited) сохраняется.
        private static void RegisterBuildings(MapLabArtSet set, List<(Item item, string assetPath)> copied, Report report)
        {
            StringBuilder sb = new StringBuilder("Регистрация построек этапа 05 (опора, основание, конёк, вход):");
            foreach ((Item item, string _) in copied)
            {
                MapLabArtEntry e = set.Find(item.Id);
                if (e == null || e.Category != MapLabArtCategory.Building || string.IsNullOrEmpty(e.ViewTag))
                    continue;
                MapLabArtAnalysis.BaseCorners b = MapLabArtAnalysis.FindBase(File.ReadAllBytes(item.FullPath), 160);
                e.BaseRegistered = b.Found;
                if (!b.Found)
                {
                    report.Warnings.Add($"{e.Id}: углы основания не найдены — опора по запасной формуле (середина силуэта).");
                    if (!e.PivotEdited)
                        e.Pivot = new Vector2(e.SolidRect.center.x, e.SolidRect.yMin + e.SolidRect.height * 0.12f);
                    continue;
                }
                RegisterBuilding(e, b);
                sb.Append($"\n  {e.Id} [{e.ViewTag}]: опора ({e.Pivot.x:0.000}; {e.Pivot.y:0.000}), подсказка ({e.PivotHint.x:0.000}; {e.PivotHint.y:0.000}), основание {e.Footprint.x:0.00}×{e.Footprint.y:0.00}, конёк {e.DrawnYaw:0.#}°, вход ({e.EntranceDirection.x:0.00}; {e.EntranceDirection.y:0.00})");
            }
            report.Warnings.Add(sb.ToString());
        }

        public static void RegisterBuilding(MapLabArtEntry e, MapLabArtAnalysis.BaseCorners b)
        {
            if (!e.PivotEdited)
                e.Pivot = b.Center;
            // Мир на пиксель холста (экранная высота карточки), глубина земли — / sin(наклона рисунка).
            float k = e.CardHeight / Mathf.Max(1f, e.Height);
            float sinPitch = Mathf.Sin(Mathf.Clamp(e.PitchDownDegrees, 20f, 90f) * Mathf.Deg2Rad);
            Vector2 World(Vector2 d) => new Vector2(d.x * e.Width * k, d.y * e.Height * k / sinPitch);
            Vector2 eLeft = World(b.Left - b.Front), eRight = World(b.Right - b.Front);
            Vector2 ridge = eLeft.magnitude > eRight.magnitude ? eLeft : eRight;
            e.Footprint = new Vector2(Mathf.Max(eLeft.magnitude, eRight.magnitude), Mathf.Min(eLeft.magnitude, eRight.magnitude));
            float yaw = Mathf.Atan2(ridge.y, ridge.x) * Mathf.Rad2Deg;
            e.DrawnYaw = Mathf.Repeat(yaw + 90f, 180f) - 90f;
            // Видимый фронтон: …Left — ребро F→L, …Right — ребро F→R; его наружное
            // направление — вдоль другого ребра от центра.
            bool leftView = e.ViewTag.EndsWith("Left");
            bool frontView = e.ViewTag.StartsWith("Front");
            Vector2 visibleGableOut = -(leftView ? eRight : eLeft);
            Vector2 entrance = frontView ? visibleGableOut : -visibleGableOut;
            e.EntranceDirection = entrance.sqrMagnitude > 1e-6f ? entrance.normalized : Vector2.down;
        }

        private static void AssignCliffs06ToScene(MapLabArtSet set)
        {
            MapLabView view = MapLabAssets.FindView();
            if (view == null || view.ArtSet06 == set)
                return;
            view.ArtSet06 = set;
            EditorUtility.SetDirty(view);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(view.gameObject.scene);
        }

        private static void AssignStage05ToScene(MapLabArtSet set, MapLabMarkerSet markers)
        {
            MapLabView view = MapLabAssets.FindView();
            if (view == null || (view.ArtSet05 == set && (markers == null || view.MarkerSet05 == markers)))
                return;
            view.ArtSet05 = set;
            if (markers != null)
                view.MarkerSet05 = markers;
            EditorUtility.SetDirty(view);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(view.gameObject.scene);
        }

        private static void AssignStage04ToScene(MapLabMarkerSet markers)
        {
            MapLabView view = MapLabAssets.FindView();
            if (view == null || markers == null || view.MarkerSet04 == markers)
                return;
            view.MarkerSet04 = markers;
            EditorUtility.SetDirty(view);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(view.gameObject.scene);
        }

        public static MapLabMarkerSet LoadStage04Markers() => AssetDatabase.LoadAssetAtPath<MapLabMarkerSet>(Stage04MarkerPath);

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

        // Набор этапа 03 назначается отдельным полем: фишка и подложки по
        // умолчанию остаются от этапа 02.
        private static void AssignStage03ToScene(MapLabArtSet set)
        {
            MapLabView view = MapLabAssets.FindView();
            if (view == null || view.ArtSet03 == set)
                return;
            view.ArtSet03 = set;
            EditorUtility.SetDirty(view);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(view.gameObject.scene);
        }

        public static MapLabArtSet LoadArtSet() => AssetDatabase.LoadAssetAtPath<MapLabArtSet>(ArtSetPath);

        public static MapLabArtSet LoadStage03Set() => AssetDatabase.LoadAssetAtPath<MapLabArtSet>(Stage03SetPath);

        public static MapLabMarkerSet LoadMarkerSet() => AssetDatabase.LoadAssetAtPath<MapLabMarkerSet>(MarkerSetPath);

        private static string Shorten(string text, int max) => text.Length <= max ? text : text.Substring(0, max) + "\n… (полный отчёт — в Console)";
    }
}

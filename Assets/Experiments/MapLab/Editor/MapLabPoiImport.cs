using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace KingdomSurvival.MapLab.Editor
{
    // 12У-E: пакеты «Выбранные объекты для MapLab» (05.11.2026) и «Фракции на
    // карте» — отдельные PNG построек, реквизита, растений, фигур групп
    // (6 ракурсов), знаков событий, порталов, модулей, эмблем и знамён.
    // Их manifest описательный (не схема штатного импортёра): здесь он
    // переводится в схему MapLabArtImport и подключается тем же импортёром
    // (проверка SHA-256, копия в свою папку, импорт текстур, запись в набор).
    // Второго реестра ассетов нет: результат — обычный MapLabArtSet.
    //   • References/ не импортируются: это уже подключённый арт проекта.
    //   • Фигуры групп и эмблемы — категория «реквизит» с ролью и ракурсом,
    //     а не фишка героя (герой P35 и его кадры не меняются).
    //   • Высота в мире и опора — предварительные (по категории и низу
    //     силуэта); калибровка на карте — вкладка «Ассеты».
    //   • Повреждённый или отсутствующий файл пакета пропускается с причиной.
    // Запуск: меню или -executeMethod KingdomSurvival.MapLab.Editor.MapLabPoiImport.ImportBatch
    //   -mapLabPoiPackage <папка выбранных мест> [-mapLabFactionPackage <папка фракций>]
    public static class MapLabPoiImport
    {
        public const string Folder = "Assets/_Project/Art/MapLabPoi";
        public const string SetPath = Folder + "/MapLabArtSet_Poi.asset";
        public const int PackageRevision = 7;
        private const string StagingFolder = "Temp/MapLabPoiPackage";

        public static readonly MapLabArtImport.Profile Profile = new MapLabArtImport.Profile
        {
            Name = "выбранные места и фракции", Folder = Folder, SetPath = SetPath, MarkerPath = Folder + "/MapLabMarkerSet_Poi.asset",
            LastFolderKey = "KingdomSurvival.MapLab.PoiPackage", MinRevision = PackageRevision, MaxRevision = PackageRevision,
        };

        public static MapLabArtSet LoadSet() => AssetDatabase.LoadAssetAtPath<MapLabArtSet>(SetPath);

        // Вызывается из окна «Генератор карты» → «Сервис» (отдельного пункта меню нет).
        public static void ImportMenu()
        {
            string poi = EditorUtility.OpenFolderPanel("Распакованный пакет выбранных мест (с ASSET_MANIFEST.json)", EditorPrefs.GetString(Profile.LastFolderKey, ""), "");
            if (string.IsNullOrEmpty(poi))
                return;
            string factions = EditorUtility.OpenFolderPanel("Пакет фракций (необязательно — «Отмена», чтобы пропустить)", poi, "");
            MapLabArtImport.Report report;
            try
            {
                report = Import(poi, string.IsNullOrEmpty(factions) ? null : factions, (s, f) => EditorUtility.DisplayProgressBar("MapLab — выбранные места", s, f));
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
            EditorPrefs.SetString(Profile.LastFolderKey, poi);
            Debug.Log("MapLab: выбранные места и фракции\n" + report);
            EditorUtility.DisplayDialog("MapLab — выбранные места", report.ToString().Length > 1800 ? report.ToString().Substring(0, 1800) + "…" : report.ToString(), "OK");
        }

        public static void ImportBatch()
        {
            int code = 0;
            try
            {
                string poi = null, factions = null;
                string[] args = Environment.GetCommandLineArgs();
                for (int i = 0; i < args.Length - 1; i++)
                {
                    if (args[i] == "-mapLabPoiPackage") poi = args[i + 1];
                    if (args[i] == "-mapLabFactionPackage") factions = args[i + 1];
                }
                MapLabArtImport.Report report = Import(poi, factions, null);
                Debug.Log("MapLab: выбранные места и фракции\n" + report);
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

        public sealed class Converted
        {
            public string Id, SourceFull, RelativePath, Category, Kind, Role, Direction, Plane, Faction;
            public string Sha;
            public int Width, Height;
            public float[] Pivot;
            public int[] Bbox;
            public float WorldHeight;
        }

        public static MapLabArtImport.Report Import(string poiRoot, string factionRoot, Action<string, float> progress)
        {
            MapLabArtImport.Report report = new MapLabArtImport.Report { PackageRoot = poiRoot };
            List<Converted> items = new List<Converted>();
            foreach (string root in new[] { poiRoot, factionRoot })
            {
                if (string.IsNullOrEmpty(root))
                    continue;
                string resolved = ResolveRoot(root);
                if (resolved == null)
                {
                    report.Errors.Add("В папке нет ASSET_MANIFEST.json: " + root);
                    continue;
                }
                items.AddRange(Read(resolved, report));
            }
            if (items.Count == 0)
            {
                report.Errors.Add("Нечего подключать.");
                return report;
            }

            // Промежуточный пакет в схеме штатного импортёра (Temp — вне git).
            string staging = Path.GetFullPath(StagingFolder);
            if (Directory.Exists(staging))
                Directory.Delete(staging, true);
            Directory.CreateDirectory(staging);
            StringBuilder json = new StringBuilder();
            json.Append("{\"schema_version\":").Append(MapLabArtImport.SupportedSchema)
                .Append(",\"package_revision\":").Append(PackageRevision)
                .Append(",\"package_id\":\"ks-maplab-selected-poi-and-factions\",\"items\":[");
            HashSet<string> ids = new HashSet<string>();
            int written = 0;
            for (int i = 0; i < items.Count; i++)
            {
                Converted c = items[i];
                progress?.Invoke("Подготовка " + c.Id, i / (float)items.Count * 0.3f);
                if (!ids.Add(c.Id))
                {
                    report.Warnings.Add($"{c.Id}: повтор ID в пакетах — подключена первая запись.");
                    continue;
                }
                string target = Path.Combine(staging, c.RelativePath.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.Copy(c.SourceFull, target, true);
                if (written++ > 0)
                    json.Append(',');
                json.Append("{\"id\":").Append(Q(c.Id))
                    .Append(",\"path\":").Append(Q(c.RelativePath))
                    .Append(",\"category\":").Append(Q(c.Category))
                    .Append(",\"kind\":").Append(Q(c.Kind ?? "Prop"))
                    .Append(",\"sha256\":").Append(Q(c.Sha))
                    .Append(",\"width\":").Append(c.Width.ToString(CultureInfo.InvariantCulture))
                    .Append(",\"height\":").Append(c.Height.ToString(CultureInfo.InvariantCulture))
                    .Append(",\"role\":").Append(Q(c.Role ?? ""))
                    .Append(",\"authoring_plane\":").Append(Q(c.Plane ?? "billboard"))
                    .Append(",\"intended_pitch_down_degrees\":50")
                    .Append(",\"world_height_hint\":").Append(c.WorldHeight.ToString("0.###", CultureInfo.InvariantCulture))
                    .Append(",\"provenance\":").Append(Q(c.Faction != null ? "faction:" + c.Faction : "selected-poi"));
                if (!string.IsNullOrEmpty(c.Direction))
                    json.Append(",\"direction\":").Append(Q(c.Direction));
                if (c.Pivot != null)
                    json.Append(",\"pivot_hint_unity_bottom_left\":[").Append(F(c.Pivot[0])).Append(',').Append(F(c.Pivot[1])).Append(']');
                if (c.Bbox != null)
                    json.Append(",\"measured_bbox_alpha160_top_left\":[").Append(c.Bbox[0]).Append(',').Append(c.Bbox[1]).Append(',').Append(c.Bbox[2]).Append(',').Append(c.Bbox[3]).Append(']');
                json.Append('}');
            }
            json.Append("]}");
            File.WriteAllText(Path.Combine(staging, MapLabArtImport.ManifestName), json.ToString(), new UTF8Encoding(false));

            MapLabArtImport.Report imported = MapLabArtImport.Import(staging, (s, f) => progress?.Invoke(s, 0.3f + f * 0.7f), Profile);
            imported.Warnings.InsertRange(0, report.Warnings);
            imported.Errors.InsertRange(0, report.Errors);
            return imported;
        }

        private static string ResolveRoot(string root)
        {
            string full = Path.GetFullPath(root);
            if (File.Exists(Path.Combine(full, MapLabArtImport.ManifestName)))
                return full;
            if (!Directory.Exists(full))
                return null;
            foreach (string dir in Directory.GetDirectories(full))
                if (File.Exists(Path.Combine(dir, MapLabArtImport.ManifestName)))
                    return Path.GetFullPath(dir);
            return null;
        }

        // Обе описательные схемы: snake_case (выбранные места) и camelCase (фракции).
        public static List<Converted> Read(string root, MapLabArtImport.Report report)
        {
            List<Converted> result = new List<Converted>();
            Dictionary<string, object> manifest = MapLabJson.AsObject(MapLabJson.Parse(File.ReadAllText(Path.Combine(root, MapLabArtImport.ManifestName)).TrimStart('﻿')));
            List<object> assets = MapLabJson.AsArray(manifest != null && manifest.TryGetValue("assets", out object a) ? a : null);
            if (assets == null)
            {
                report.Errors.Add("manifest без массива assets: " + root);
                return result;
            }
            foreach (object rawObject in assets)
            {
                Dictionary<string, object> raw = MapLabJson.AsObject(rawObject);
                if (raw == null)
                    continue;
                string id = MapLabJson.GetString(raw, "asset_id") ?? MapLabJson.GetString(raw, "assetId");
                string path = MapLabJson.GetString(raw, "path");
                if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(path) || path.StartsWith("References/", StringComparison.Ordinal) || !path.StartsWith("Sprites/", StringComparison.Ordinal))
                    continue;
                if (MapLabArtImport.ValidateRelativePath(path) != null)
                {
                    report.Warnings.Add($"{id}: путь «{path}» не принят.");
                    continue;
                }
                string full = Path.GetFullPath(Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar)));
                string sha = (MapLabJson.GetString(raw, "sha256") ?? "").ToLowerInvariant();
                if (!File.Exists(full))
                {
                    report.Warnings.Add($"{id}: файла {path} нет в распакованном пакете — пропущен.");
                    continue;
                }
                if (Sha256(File.ReadAllBytes(full)) != sha)
                {
                    report.Warnings.Add($"{id}: SHA-256 файла {path} не совпадает с manifest (повреждён при распаковке?) — пропущен.");
                    continue;
                }
                string folder = path.Split('/')[1];
                string view = MapLabJson.GetString(raw, "view") ?? MapLabJson.GetString(raw, "direction");
                string kind = MapLabJson.GetString(raw, "kind");
                Converted c = new Converted
                {
                    Id = SafeId(id),
                    SourceFull = full,
                    RelativePath = path,
                    Sha = sha,
                    Width = (int)MapLabJson.GetNumber(raw, "width", 0),
                    Height = (int)MapLabJson.GetNumber(raw, "height", 0),
                    Role = MapLabJson.GetString(raw, "role") ?? (kind ?? folder).ToLowerInvariant(),
                    Plane = MapLabJson.GetString(raw, "authoring_plane") ?? MapLabJson.GetString(raw, "authoringPlane"),
                    Faction = MapLabJson.GetString(raw, "factionId"),
                    Direction = DirectionOf(view, id),
                };
                switch (folder)
                {
                    case "Buildings":
                        c.Category = "building"; c.Kind = "House"; c.WorldHeight = 7.5f; break;
                    case "Figures":
                        c.Category = "prop"; c.Kind = "Figure"; c.WorldHeight = 2.4f; break;
                    case "Vegetation":
                        c.Category = "prop"; c.Kind = "Plant"; c.WorldHeight = id.Contains("Tree") ? 6f : 1.2f; break;
                    case "Markers":
                        c.Category = "prop"; c.Kind = "Marker"; c.WorldHeight = 3f; break;
                    case "Modules":
                        c.Category = "prop"; c.Kind = "Module"; c.WorldHeight = 2.5f; break;
                    case "Emblems":
                        c.Category = "prop"; c.Kind = "Emblem"; c.WorldHeight = 1.2f; break;
                    case "Banners":
                        c.Category = "prop"; c.Kind = "Banner"; c.WorldHeight = 3.2f; break;
                    default:
                        c.Category = "prop"; c.Kind = "Prop"; c.WorldHeight = 1.8f; break;
                }
                double[] pivot = MapLabJson.GetNumbers(raw, "pivot_hint_bottom_left_normalized") ?? MapLabJson.GetNumbers(raw, "provisionalBBoxBottomCenterUV");
                if (pivot != null && pivot.Length == 2)
                    c.Pivot = new[] { (float)pivot[0], (float)pivot[1] };
                double[] bbox = MapLabJson.GetNumbers(raw, "solid_bbox_threshold_128_top_left") ?? MapLabJson.GetNumbers(raw, "opaqueBBoxPixels");
                if (bbox != null && bbox.Length == 4)
                    c.Bbox = new[] { (int)bbox[0], (int)bbox[1], (int)bbox[2], (int)bbox[3] };
                if (c.Width < 1 || c.Height < 1)
                {
                    report.Warnings.Add($"{id}: нет размеров — пропущен.");
                    continue;
                }
                result.Add(c);
            }
            return result;
        }

        private static string DirectionOf(string view, string id)
        {
            string v = view ?? "";
            if (string.IsNullOrEmpty(v))
            {
                foreach (string name in MapLabMarkerSet.DirectionNames)
                    if (id.EndsWith("_" + name, StringComparison.Ordinal))
                        v = name;
            }
            return v.Replace("_", "");
        }

        private static string SafeId(string id)
        {
            StringBuilder b = new StringBuilder();
            foreach (char ch in id)
                b.Append(char.IsLetterOrDigit(ch) || ch == '_' || ch == '-' ? ch : '_');
            string s = b.ToString();
            return s.Length > 64 ? s.Substring(0, 64) : s;
        }

        private static string Q(string s)
        {
            StringBuilder b = new StringBuilder("\"");
            foreach (char ch in s ?? "")
            {
                if (ch == '"' || ch == '\\') b.Append('\\').Append(ch);
                else if (ch < 32) b.Append("\\u").Append(((int)ch).ToString("x4"));
                else b.Append(ch);
            }
            return b.Append('"').ToString();
        }

        private static string F(float v) => v.ToString("0.#####", CultureInfo.InvariantCulture);

        private static string Sha256(byte[] bytes)
        {
            using (SHA256 sha = SHA256.Create())
            {
                StringBuilder b = new StringBuilder();
                foreach (byte x in sha.ComputeHash(bytes))
                    b.Append(x.ToString("x2"));
                return b.ToString();
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using KingdomSurvival.BattlefieldDatabase;
using UnityEngine;

namespace KingdomSurvival.LocationRendering.Editor
{
    // ПР-12О: пакет экспорта KS Ground Renderer 1.0.0 (manifest schema 1)
    // или ручной комплект PNG. Load — разбор и проверка структуры; Verify —
    // файлы: безопасный путь, наличие, sha256, размер и разрядность PNG,
    // статус прохода. Ничего не меняет в проекте.
    public sealed class GroundExportPass
    {
        public string Kind;
        public string Status = string.Empty;
        public string Path = string.Empty;
        public string Revision = string.Empty;
        public string Sha256 = string.Empty;
        public int BitDepth;
        public string FullPath;
        public bool Usable;
        public string Problem;
        public long SaturatedPixels;
        public long CoverageFilled;
        public PngCodec.Header Png;

        public bool NotRequested => Status == "not_requested";
    }

    public sealed class GroundExportTile
    {
        public int X, Y;
        public string Status = string.Empty;
        public readonly Dictionary<string, GroundExportPass> Passes = new Dictionary<string, GroundExportPass>(StringComparer.Ordinal);

        public string Key => LocationGroundTile.KeyOf(X, Y);
        public GroundExportPass Pass(string kind) => Passes.TryGetValue(kind, out GroundExportPass pass) ? pass : null;
        public bool Usable(string kind) => Pass(kind)?.Usable == true;
        public IEnumerable<string> Problems => Passes.Values.Where(pass => !string.IsNullOrEmpty(pass.Problem)).Select(pass => pass.Kind + ": " + pass.Problem);
    }

    public sealed partial class GroundExportPackage
    {
        public const string Color = "Color", Normal = "Normal", Height = "Height";
        public static readonly string[] Kinds = { Color, Normal, Height };
        public const string SupportedExporter = "1.0.0";

        public string Folder;
        public string ManifestPath;
        public string ManifestText;
        public bool Manual;

        public int Schema;
        public string MapId = string.Empty;
        public string RevisionId = string.Empty;
        public string ExporterVersion = string.Empty;
        public string Status = string.Empty;
        public string ConfigurationHash = string.Empty;
        public string SurfaceContract = string.Empty;
        public bool Single;
        public int Columns, Rows, TileWidth, TileHeight;
        public int Overscan;
        public double[] Origin = { 0, 0 };
        public double[] RequestedBounds;
        public double Q;
        public double[] Reference = { 0, 0, 0 };
        public double[][] Basis = { new double[] { 1, 0, 0 }, new double[] { 0, 1, 0 }, new double[] { 0, 0, 1 } };
        public double UnityPpu, MetersPerBlenderUnit = 1, Downscale = 1;
        public double HeightMin, HeightMax = 1;
        public int HeightBits = 16;
        public string HeightSource = string.Empty;
        public bool InvertGreen;
        public bool NormalVerified;
        public readonly List<string> RequestedPasses = new List<string>();
        public readonly List<GroundExportTile> Tiles = new List<GroundExportTile>();

        public readonly List<string> Errors = new List<string>();
        public readonly List<string> Warnings = new List<string>();
        public readonly List<string> Notes = new List<string>();

        public bool Requested(string kind) => RequestedPasses.Contains(kind);
        public GroundExportTile Find(int x, int y) => Tiles.Find(tile => tile.X == x && tile.Y == y);
        public int VirtualWidth => Columns * TileWidth;
        public int VirtualHeight => Rows * TileHeight;
        public int ReadyTiles => Tiles.Count(TileReady);
        public int ProblemTiles => Tiles.Count - ReadyTiles;
        // Пакет неполный: часть участков или проходов не готова (только предпросмотр).
        public bool Partial => Tiles.Count < Columns * Rows || Tiles.Any(tile => !TileReady(tile));
        public bool CanApply => Errors.Count == 0 && Tiles.Any(tile => tile.Usable(Color));

        public bool TileReady(GroundExportTile tile) => RequestedPasses.All(kind => tile.Usable(kind));

        // Наклон камеры: ровная земля (мировая Z) в базисе картинки — (r.z, u.z, b.z).
        public float NormalTiltDegrees => (float)(Math.Atan2(Basis[1][2], Basis[2][2]) * 180 / Math.PI);

        // Полезная область (requested_bounds) в пикселях виртуальной карты снизу слева.
        public Rect UsefulPixels
        {
            get
            {
                if (RequestedBounds == null || Q <= 0) return new Rect(0, 0, VirtualWidth, VirtualHeight);
                float x0 = (float)((RequestedBounds[0] - Origin[0]) / Q), y0 = (float)((RequestedBounds[1] - Origin[1]) / Q);
                float x1 = (float)((RequestedBounds[2] - Origin[0]) / Q), y1 = (float)((RequestedBounds[3] - Origin[1]) / Q);
                Rect rect = Rect.MinMaxRect(Mathf.Max(0, x0), Mathf.Max(0, y0), Mathf.Min(VirtualWidth, x1), Mathf.Min(VirtualHeight, y1));
                return rect.width > 0 && rect.height > 0 ? rect : new Rect(0, 0, VirtualWidth, VirtualHeight);
            }
        }

        // ------------------------------------------------------------------
        // Поиск manifest
        // ------------------------------------------------------------------

        // Файл manifest или папка экспорта (manifest в ней или на уровень ниже).
        public static string FindManifest(string path, out string problem)
        {
            problem = null;
            if (string.IsNullOrEmpty(path)) { problem = "Не выбран manifest или папка экспорта."; return null; }
            if (File.Exists(path))
            {
                if (path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) return path;
                problem = "Нужен JSON manifest экспорта (…_manifest.json), а не «" + System.IO.Path.GetFileName(path) + "».";
                return null;
            }
            if (!Directory.Exists(path)) { problem = "Путь не найден: " + path; return null; }
            List<string> found = Directory.GetFiles(path, "*_manifest.json").ToList();
            if (found.Count == 0)
                foreach (string child in Directory.GetDirectories(path))
                    found.AddRange(Directory.GetFiles(child, "*_manifest.json"));
            if (found.Count == 1) return found[0];
            problem = found.Count == 0
                ? "В папке нет manifest экспорта (…_manifest.json)."
                : "В папке несколько manifest (" + found.Count + "): выберите нужный файл manifest.";
            return null;
        }

        // ------------------------------------------------------------------
        // Разбор manifest schema 1
        // ------------------------------------------------------------------

        public static GroundExportPackage Load(string manifestOrFolder)
        {
            GroundExportPackage package = new GroundExportPackage();
            string manifest = FindManifest(manifestOrFolder, out string problem);
            if (manifest == null) { package.Errors.Add(problem); return package; }
            package.ManifestPath = System.IO.Path.GetFullPath(manifest);
            package.Folder = System.IO.Path.GetDirectoryName(package.ManifestPath);
            object root;
            try
            {
                package.ManifestText = File.ReadAllText(manifest, Encoding.UTF8);
                root = GroundJson.Parse(package.ManifestText);
            }
            catch (Exception error) when (error is IOException || error is FormatException || error is UnauthorizedAccessException)
            {
                package.Errors.Add("Manifest не прочитан: " + error.Message);
                return package;
            }
            // schema 2 — KS Ground Renderer 2: карта целиком тремя файлами.
            if (GroundJson.Num(root, "schema_version") == 2)
            {
                package.ReadWhole(root);
                return package;
            }
            package.ReadManifest(root);
            if (package.Errors.Count == 0) package.Verify();
            return package;
        }

        private void ReadManifest(object root)
        {
            if (!(root is Dictionary<string, object>)) { Errors.Add("Manifest — не JSON-объект."); return; }
            double? schema = GroundJson.Num(root, "schema_version");
            Schema = schema.HasValue ? (int)schema.Value : 0;
            if (Schema != 1)
            {
                Errors.Add("Неподдержанная версия manifest: schema_version = " + (schema?.ToString() ?? "нет") + ". Поддержаны 1 (KS Ground Renderer 1.0, участками) и 2 (KS Ground Renderer 2, карта целиком).");
                return;
            }
            MapId = GroundJson.Str(root, "map_id") ?? string.Empty;
            RevisionId = GroundJson.Str(root, "revision_id") ?? string.Empty;
            ExporterVersion = GroundJson.Str(root, "exporter_version") ?? string.Empty;
            Status = GroundJson.Str(root, "status") ?? string.Empty;
            ConfigurationHash = GroundJson.Str(root, "configuration_hash") ?? string.Empty;
            if (string.IsNullOrWhiteSpace(MapId) || MapId.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0)
                Errors.Add("Неверный map_id: «" + MapId + "».");
            if (ExporterVersion != SupportedExporter)
                Warnings.Add("Экспортёр " + (ExporterVersion.Length > 0 ? ExporterVersion : "неизвестной версии") + "; импорт проверен на " + SupportedExporter + " (schema 1).");

            Dictionary<string, object> config = GroundJson.Obj(root, "config");
            if (config == null) { Errors.Add("В manifest нет config."); return; }
            Single = GroundJson.Bool(config, "single") ?? false;
            Dictionary<string, object> grid = GroundJson.Obj(config, "grid");
            Columns = (int)(GroundJson.Num(grid, "nx") ?? 0);
            Rows = (int)(GroundJson.Num(grid, "ny") ?? 0);
            double[] size = GroundJson.Numbers(config, "size", 2);
            TileWidth = size != null ? (int)size[0] : 0;
            TileHeight = size != null ? (int)size[1] : 0;
            Origin = GroundJson.Numbers(grid, "origin", 2) ?? new double[] { 0, 0 };
            RequestedBounds = GroundJson.Numbers(grid, "requested_bounds", 4);
            Overscan = (int)(GroundJson.Num(config, "overscan") ?? 0);
            if (Columns <= 0 || Rows <= 0 || TileWidth <= 0 || TileHeight <= 0 || size == null || size[0] != Math.Floor(size[0]) || size[1] != Math.Floor(size[1]))
                Errors.Add("Неверная сетка или размер участка: nx=" + Columns + ", ny=" + Rows + ", size=" + TileWidth + "×" + TileHeight + ".");
            if ((long)Columns * Rows > 4096)
                Errors.Add("Слишком много участков (" + Columns + "×" + Rows + "): сначала проверьте размер карты.");

            Dictionary<string, object> projection = GroundJson.Obj(config, "projection");
            Q = GroundJson.Num(projection, "q") ?? 0;
            double qx = GroundJson.Num(projection, "qx") ?? Q, qy = GroundJson.Num(projection, "qy") ?? Q;
            if (!(Q > 0) || Math.Abs(qx - qy) > Q * 1e-6)
                Errors.Add("Неверная плотность проекции q (" + Q + ", qx=" + qx + ", qy=" + qy + "): нужны квадратные пиксели.");
            Reference = GroundJson.Numbers(projection, "reference", 3) ?? Reference;
            List<object> basis = GroundJson.Arr(projection, "basis");
            if (basis != null && basis.Count == 3)
            {
                for (int i = 0; i < 3; i++)
                {
                    List<object> axis = basis[i] as List<object>;
                    if (axis == null || axis.Count != 3 || !axis.All(v => v is double)) { Errors.Add("Неверный базис камеры в manifest."); break; }
                    Basis[i] = axis.Select(v => (double)v).ToArray();
                }
            }

            Dictionary<string, object> units = GroundJson.Obj(config, "units");
            UnityPpu = GroundJson.Num(units, "unity_ppu") ?? 0;
            MetersPerBlenderUnit = GroundJson.Num(units, "meters_per_blender_unit") ?? 1;
            Downscale = GroundJson.Num(units, "downscale") ?? 1;
            if (!(UnityPpu > 0)) Errors.Add("Неверный unity_ppu: " + UnityPpu + ".");
            if (!(MetersPerBlenderUnit > 0)) Errors.Add("Неверный meters_per_blender_unit: " + MetersPerBlenderUnit + ".");
            if (Math.Abs(Downscale - 1) > 1e-9)
                Notes.Add("downscale = " + Downscale + " — это метаданные плана, PNG не уменьшены; импорт использует их реальный размер.");

            List<object> passes = GroundJson.Arr(config, "passes");
            if (passes != null) RequestedPasses.AddRange(passes.OfType<string>().Where(Kinds.Contains));
            if (!Requested(Color)) Errors.Add("Экспорт без Color: земле нечего показывать.");

            Dictionary<string, object> height = GroundJson.Obj(config, "height");
            if (Requested(Height))
            {
                HeightMin = GroundJson.Num(height, "min") ?? double.NaN;
                HeightMax = GroundJson.Num(height, "max") ?? double.NaN;
                HeightBits = (int)(GroundJson.Num(height, "bit_depth") ?? 16);
                HeightSource = GroundJson.Str(height, "source") ?? string.Empty;
                if (!(HeightMax > HeightMin)) Errors.Add("Неверный Height Range: max (" + HeightMax + ") должен быть больше min (" + HeightMin + ").");
                if (HeightBits != 8 && HeightBits != 16) Errors.Add("Неверная разрядность высоты: " + HeightBits + " бит.");
                if (HeightSource != "world_position_z")
                    Errors.Add("Неизвестная семантика высоты «" + HeightSource + "»: импорт понимает только world_position_z (абсолютная Z Blender).");
                string alpha = GroundJson.Str(height, "alpha") ?? string.Empty;
                if (!alpha.StartsWith("coverage", StringComparison.Ordinal))
                    Warnings.Add("Альфа высоты описана как «" + alpha + "»; импорт считает её покрытием (0 — нет данных).");
                if (HeightBits == 8) Warnings.Add("Высота 8 бит: шаг " + ((HeightMax - HeightMin) / 255).ToString("0.####") + " BU — точность ниже 16 бит.");
            }
            Dictionary<string, object> normal = GroundJson.Obj(config, "normal");
            if (Requested(Normal))
            {
                InvertGreen = GroundJson.Bool(normal, "invert_green") ?? false;
                NormalVerified = GroundJson.Bool(normal, "unity_verified") ?? false;
                string space = GroundJson.Str(normal, "space") ?? string.Empty;
                if (space != "sprite_projection")
                    Warnings.Add("Пространство нормалей «" + space + "» — ожидалось sprite_projection.");
                if (InvertGreen)
                    Notes.Add("Нормали экспортированы с инвертированным зелёным (invert_green): импорт перевернёт его один раз под Unity (Y вверх).");
            }
            SurfaceContract = GroundJson.Str(config, "surface_contract") ?? string.Empty;
            if (SurfaceContract == "separate_explicit")
                Warnings.Add("Раздельные источники (separate_explicit): Color может показывать дополнительные объекты, а Height описывает только землю. " +
                             "Высота крыши или камня на рисунке не равна Height земли под ним.");
            else if (SurfaceContract != "matched_ground" && SurfaceContract.Length > 0)
                Warnings.Add("Неизвестный surface_contract «" + SurfaceContract + "».");

            string rows = GroundJson.Str(root, "image_rows"), indices = GroundJson.Str(root, "tile_indices");
            if (rows != "top_to_bottom") Errors.Add("Неизвестная ориентация строк изображения «" + rows + "» (ожидалось top_to_bottom).");
            if (indices != "bottom_left, X right, Y up") Errors.Add("Неизвестная ориентация индексов участков «" + indices + "».");
            double[] virtualSize = GroundJson.Numbers(root, "virtual_size", 2);
            if (virtualSize == null || (int)virtualSize[0] != VirtualWidth || (int)virtualSize[1] != VirtualHeight)
                Errors.Add("virtual_size не совпадает с сеткой: ожидалось " + VirtualWidth + "×" + VirtualHeight + ".");

            foreach (string warning in (GroundJson.Arr(root, "warnings") ?? new List<object>()).OfType<string>().Distinct())
                Warnings.Add("Экспорт: " + warning);
            foreach (object error in GroundJson.Arr(root, "errors") ?? new List<object>())
                Warnings.Add("Ошибка экспорта: " + (GroundJson.Str(error, "error") ?? "без описания"));
            if (Status != "complete")
                Warnings.Add("Статус экспорта: " + StatusName(Status) + ". Незавершённые проходы не используются.");

            List<object> tiles = GroundJson.Arr(root, "tiles");
            if (tiles == null) { Errors.Add("В manifest нет tiles."); return; }
            HashSet<string> keys = new HashSet<string>();
            foreach (object item in tiles)
            {
                double? x = GroundJson.Num(item, "x"), y = GroundJson.Num(item, "y");
                if (!x.HasValue || !y.HasValue || x != Math.Floor(x.Value) || y != Math.Floor(y.Value)) { Errors.Add("Участок без целых индексов x/y."); continue; }
                GroundExportTile tile = new GroundExportTile { X = (int)x.Value, Y = (int)y.Value, Status = GroundJson.Str(item, "status") ?? string.Empty };
                if (tile.X < 0 || tile.Y < 0 || tile.X >= Columns || tile.Y >= Rows) { Errors.Add("Участок " + tile.Key + " вне сетки " + Columns + "×" + Rows + "."); continue; }
                if (!keys.Add(tile.Key)) { Errors.Add("Участок " + tile.Key + " повторяется."); continue; }
                double[] rect = GroundJson.Numbers(item, "pixel_rect", 4);
                if (rect == null || rect[0] != tile.X * TileWidth || rect[1] != (Rows - 1 - tile.Y) * TileHeight || rect[2] != TileWidth || rect[3] != TileHeight)
                    Errors.Add("Участок " + tile.Key + ": pixel_rect не совпадает с сеткой (ожидалось [" + tile.X * TileWidth + ", " +
                               (Rows - 1 - tile.Y) * TileHeight + ", " + TileWidth + ", " + TileHeight + "]).");
                Dictionary<string, object> passMap = GroundJson.Obj(item, "passes");
                foreach (string kind in Kinds)
                {
                    Dictionary<string, object> entry = GroundJson.Obj(passMap, kind);
                    GroundExportPass pass = new GroundExportPass
                    {
                        Kind = kind,
                        Status = GroundJson.Str(entry, "status") ?? (Requested(kind) ? "pending" : "not_requested"),
                        Path = GroundJson.Str(entry, "path") ?? string.Empty,
                        Revision = GroundJson.Str(entry, "revision_id") ?? string.Empty,
                        Sha256 = (GroundJson.Str(entry, "sha256") ?? string.Empty).ToLowerInvariant(),
                        BitDepth = (int)(GroundJson.Num(entry, "bit_depth") ?? 0)
                    };
                    Dictionary<string, object> quality = GroundJson.Obj(entry, "quality");
                    pass.SaturatedPixels = (long)(GroundJson.Num(quality, "saturated_pixels") ?? 0);
                    pass.CoverageFilled = (long)(GroundJson.Num(quality, "coverage_filled_pixels") ?? 0);
                    double[] passSize = GroundJson.Numbers(entry, "size", 2);
                    if (pass.Status == "complete" && passSize != null && ((int)passSize[0] != TileWidth || (int)passSize[1] != TileHeight))
                        pass.Problem = "размер в manifest " + passSize[0] + "×" + passSize[1] + " не совпадает с config.size.";
                    tile.Passes[kind] = pass;
                }
                Tiles.Add(tile);
            }
            int missing = Columns * Rows - Tiles.Count;
            if (missing > 0 && Errors.Count == 0)
                Warnings.Add("В manifest нет записей для " + missing + " участков сетки.");
            if (Single && (Columns != 1 || Rows != 1)) Errors.Add("Режим SINGLE с сеткой " + Columns + "×" + Rows + ".");
        }

        public static string StatusName(string status)
        {
            switch (status)
            {
                case "complete": return "завершён";
                case "partial": return "частичный";
                case "running": return "идёт (не закончен)";
                case "pending": return "ожидает";
                case "cancelled": return "отменён";
                case "error": return "ошибка";
                case "incomplete": return "не завершён";
                default: return string.IsNullOrEmpty(status) ? "не указан" : status;
            }
        }

        // ------------------------------------------------------------------
        // Проверка файлов
        // ------------------------------------------------------------------

        public void Verify()
        {
            int revisions = Tiles.SelectMany(tile => tile.Passes.Values).Where(pass => pass.Status == "complete").Select(pass => pass.Revision).Distinct().Count();
            if (revisions > 1)
                Notes.Add("Проходы из " + revisions + " ревизий экспорта: экспортёр переиспользовал готовые файлы — это допустимо, конфигурация общая.");
            long saturated = 0;
            foreach (GroundExportTile tile in Tiles)
            {
                foreach (GroundExportPass pass in tile.Passes.Values)
                {
                    pass.Usable = false;
                    if (pass.NotRequested)
                    {
                        if (Requested(pass.Kind)) pass.Problem = "проход отмечен not_requested, хотя заказан.";
                        continue;
                    }
                    if (!Requested(pass.Kind))
                    {
                        pass.Problem = "проход не заказан в config.passes — не используется.";
                        continue;
                    }
                    if (pass.Status != "complete")
                    {
                        string full = TryResolve(Folder, pass.Path, out string resolved, out _) ? resolved : null;
                        pass.Problem = "проход не завершён (" + StatusName(pass.Status) + ")" +
                                       (full != null && File.Exists(full) ? "; файл есть, но не используется." : ".");
                        continue;
                    }
                    if (!string.IsNullOrEmpty(pass.Problem)) continue;
                    VerifyFile(pass);
                    if (pass.Usable && pass.Kind == Height) saturated += pass.SaturatedPixels;
                }
            }
            if (saturated > 0)
                Warnings.Add("Насыщенные высоты: " + saturated + " пикселей вышли за Height Range при экспорте и обрезаны до min/max.");
        }

        private void VerifyFile(GroundExportPass pass)
        {
            if (!TryResolve(Folder, pass.Path, out string full, out string problem)) { pass.Problem = problem; return; }
            pass.FullPath = full;
            if (!File.Exists(full)) { pass.Problem = "файл не найден: " + pass.Path; return; }
            byte[] bytes;
            try { bytes = File.ReadAllBytes(full); }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException) { pass.Problem = "файл не прочитан: " + error.Message; return; }
            string sha = Sha256Hex(bytes);
            if (string.IsNullOrEmpty(pass.Sha256))
            {
                if (!Manual) Warnings.Add("Нет sha256 у " + pass.Path + ": целостность не подтверждена.");
                pass.Sha256 = sha;
            }
            else if (!string.Equals(sha, pass.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                pass.Problem = "sha256 не совпадает с manifest — файл изменён или повреждён.";
                return;
            }
            if (!PngCodec.TryReadHeader(bytes, out PngCodec.Header header, out problem)) { pass.Problem = problem; return; }
            pass.Png = header;
            if (header.Width != TileWidth || header.Height != TileHeight)
            {
                pass.Problem = "PNG " + header.Width + "×" + header.Height + " вместо " + TileWidth + "×" + TileHeight + ".";
                return;
            }
            if (pass.BitDepth > 0 && header.BitDepth != pass.BitDepth)
            {
                pass.Problem = "PNG " + header.BitDepth + " бит, в manifest " + pass.BitDepth + ".";
                return;
            }
            if (pass.Kind != Color && !PngCodec.IsSupported(header, out problem)) { pass.Problem = problem; return; }
            if (pass.Kind == Height)
            {
                if (header.BitDepth != HeightBits) { pass.Problem = "Height " + header.BitDepth + " бит, в config " + HeightBits + "."; return; }
                if (!header.HasAlpha) Warnings.Add("Height " + pass.Path + " без альфы: покрытие считается полным.");
            }
            pass.Usable = true;
        }

        // Относительный путь внутри пакета: без абсолютных путей, «..» и
        // выхода через ссылки (reparse points) — иначе ошибка.
        public static bool TryResolve(string folder, string relative, out string full, out string problem)
        {
            full = null;
            problem = null;
            if (string.IsNullOrWhiteSpace(relative)) { problem = "пустой путь файла."; return false; }
            if (relative.IndexOf('\\') >= 0 || relative.IndexOf(':') >= 0 || relative.StartsWith("/", StringComparison.Ordinal) || System.IO.Path.IsPathRooted(relative))
            {
                problem = "недопустимый путь «" + relative + "»: нужен относительный путь внутри папки экспорта с «/».";
                return false;
            }
            string[] parts = relative.Split('/');
            if (parts.Any(part => part.Length == 0 || part == "." || part == ".." || part.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0))
            {
                problem = "недопустимый путь «" + relative + "»: выход за папку экспорта запрещён.";
                return false;
            }
            string root = System.IO.Path.GetFullPath(folder).TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar;
            string candidate = System.IO.Path.GetFullPath(System.IO.Path.Combine(root, relative.Replace('/', System.IO.Path.DirectorySeparatorChar)));
            if (!candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                problem = "путь «" + relative + "» ведёт за папку экспорта.";
                return false;
            }
            string current = root.TrimEnd(System.IO.Path.DirectorySeparatorChar);
            foreach (string part in parts)
            {
                current = System.IO.Path.Combine(current, part);
                if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                {
                    problem = "путь «" + relative + "» проходит через ссылку (symlink/junction) — запрещено.";
                    return false;
                }
            }
            full = candidate;
            return true;
        }

        public static string Sha256Hex(byte[] bytes)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(bytes);
                StringBuilder builder = new StringBuilder(64);
                foreach (byte b in hash) builder.Append(b.ToString("x2"));
                return builder.ToString();
            }
        }

        // ------------------------------------------------------------------
        // Сводка для окна
        // ------------------------------------------------------------------

        public string Summary()
        {
            StringBuilder text = new StringBuilder();
            text.AppendLine((Manual ? "Ручной комплект" : "Карта «" + MapId + "»") + (Manual ? "" : " · экспорт " + StatusName(Status)) +
                            (ExporterVersion.Length > 0 ? " · KS Ground Renderer " + ExporterVersion : ""));
            text.AppendLine("Сетка " + Columns + "×" + Rows + " · участок " + TileWidth + "×" + TileHeight + " px · вся карта " + VirtualWidth + "×" + VirtualHeight + " px");
            text.AppendLine("PPU экспорта " + UnityPpu.ToString("0.##") + " · в месте 1 пиксель рисунка = 1 пиксель места (" + LocationVisualGeometry.PixelsPerUnit + " px на единицу мира)");
            text.AppendLine("Проходы: " + (RequestedPasses.Count > 0 ? string.Join(", ", RequestedPasses) : "нет"));
            if (Requested(Height))
                text.AppendLine("Height Range " + HeightMin.ToString("0.####") + " … " + HeightMax.ToString("0.####") + " BU · " + HeightBits + " бит · шаг " +
                                ((HeightMax - HeightMin) / (HeightBits == 16 ? 65535 : 255)).ToString("0.######") + " BU · " + MetersPerBlenderUnit.ToString("0.###") + " м/BU");
            if (Requested(Normal))
                text.AppendLine("Нормали: наклон камеры " + NormalTiltDegrees.ToString("0.#") + "°" + (InvertGreen ? ", зелёный инвертирован" : "") +
                                (NormalVerified ? "" : " · совпадение с Unity не проверено экспортёром"));
            text.Append("Готовых участков " + ReadyTiles + " из " + Columns * Rows + (ProblemTiles > 0 || Tiles.Count < Columns * Rows ? " · проблемных " + (Columns * Rows - ReadyTiles) : ""));
            return text.ToString();
        }
    }
}

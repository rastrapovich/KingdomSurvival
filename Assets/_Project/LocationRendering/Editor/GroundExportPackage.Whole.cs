using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using KingdomSurvival.BattlefieldDatabase;
using UnityEditor;
using UnityEngine;

namespace KingdomSurvival.LocationRendering.Editor
{
    // ПР-12О: экспорт KS Ground Renderer 2 (manifest schema 2) — карта
    // целиком тремя файлами: Color, Normal, Height. База сама режет их по
    // сетке экспорта (участки — во временный кэш Temp), дальше работает тот
    // же импорт, что и для участков schema 1. Normal и Height проверяются по
    // sha256; Color может быть обрисован прямо в файле экспорта (и крупнее в
    // целое число раз) — тогда он загружается как обрисовка.
    public sealed partial class GroundExportPackage
    {
        public const string UnpackRoot = "Temp/KSGroundUnpack";

        public bool WholeMap;
        public bool ColorPainted;
        public float ColorScale = 1;
        public string ColorFile = string.Empty;
        public double CharacterHeightPx;
        public int MapWidth, MapHeight;

        private readonly Dictionary<string, string> wholeFiles = new Dictionary<string, string>();
        private readonly Dictionary<string, string> wholeSha = new Dictionary<string, string>();

        private void ReadWhole(object root)
        {
            WholeMap = true;
            Schema = 2;
            MapId = GroundJson.Str(root, "map_id") ?? string.Empty;
            RevisionId = GroundJson.Str(root, "revision_id") ?? string.Empty;
            ExporterVersion = GroundJson.Str(root, "exporter_version") ?? string.Empty;
            Status = GroundJson.Str(root, "status") ?? string.Empty;
            SurfaceContract = "matched_ground";
            UnityPpu = LocationVisualGeometry.PixelsPerUnit;
            if (string.IsNullOrWhiteSpace(MapId) || MapId.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0)
                Errors.Add("Неверный map_id: «" + MapId + "».");
            if (Status != "complete")
                Errors.Add("Экспорт не завершён (статус «" + StatusName(Status) + "»): экспортируйте карту заново.");

            Dictionary<string, object> image = GroundJson.Obj(root, "image");
            MapWidth = (int)(GroundJson.Num(image, "width") ?? 0);
            MapHeight = (int)(GroundJson.Num(image, "height") ?? 0);
            double[] tile = GroundJson.Numbers(image, "tile", 2), grid = GroundJson.Numbers(image, "grid", 2);
            if (tile == null || grid == null || MapWidth <= 0 || MapHeight <= 0)
            {
                Errors.Add("В manifest нет размера карты или сетки (image.width/height/tile/grid).");
                return;
            }
            TileWidth = (int)tile[0]; TileHeight = (int)tile[1];
            Columns = (int)grid[0]; Rows = (int)grid[1];
            if (TileWidth <= 0 || TileHeight <= 0 || Columns <= 0 || Rows <= 0 ||
                Columns * TileWidth != MapWidth || Rows * TileHeight != MapHeight)
                Errors.Add("Сетка " + Columns + "×" + Rows + " по " + TileWidth + "×" + TileHeight + " не совпадает с картой " + MapWidth + "×" + MapHeight + ".");
            if (GroundJson.Str(image, "rows") != "top_to_bottom") Errors.Add("Неизвестный порядок строк изображения.");

            Dictionary<string, object> projection = GroundJson.Obj(root, "projection");
            Q = GroundJson.Num(projection, "q") ?? 0;
            if (!(Q > 0)) Errors.Add("Неверная плотность проекции q.");
            Origin = GroundJson.Numbers(projection, "origin", 2) ?? new double[] { 0, 0 };
            RequestedBounds = GroundJson.Numbers(projection, "requested_bounds", 4);
            Reference = GroundJson.Numbers(projection, "reference", 3) ?? Reference;
            List<object> basis = GroundJson.Arr(projection, "basis");
            if (basis != null && basis.Count == 3)
                for (int i = 0; i < 3; i++)
                    if (basis[i] is List<object> axis && axis.Count == 3 && axis.All(v => v is double))
                        Basis[i] = axis.Select(v => (double)v).ToArray();
            MetersPerBlenderUnit = GroundJson.Num(GroundJson.Obj(root, "units"), "meters_per_blender_unit") ?? 1;
            CharacterHeightPx = GroundJson.Num(GroundJson.Obj(root, "character"), "height_px") ?? 0;

            Dictionary<string, object> files = GroundJson.Obj(root, "files");
            foreach (string kind in Kinds)
            {
                Dictionary<string, object> entry = GroundJson.Obj(files, kind);
                if (entry == null) { Errors.Add("В manifest нет файла " + kind + "."); continue; }
                RequestedPasses.Add(kind);
                if (!TryResolve(Folder, GroundJson.Str(entry, "path"), out string full, out string problem)) { Errors.Add(kind + ": " + problem); continue; }
                if (!File.Exists(full)) { Errors.Add(kind + ": нет файла " + GroundJson.Str(entry, "path") + "."); continue; }
                wholeFiles[kind] = full;
                wholeSha[kind] = (GroundJson.Str(entry, "sha256") ?? string.Empty).ToLowerInvariant();
                if (kind == Height)
                {
                    HeightMin = GroundJson.Num(entry, "min") ?? double.NaN;
                    HeightMax = GroundJson.Num(entry, "max") ?? double.NaN;
                    HeightBits = (int)(GroundJson.Num(entry, "bit_depth") ?? 16);
                    HeightSource = GroundJson.Str(entry, "source") ?? string.Empty;
                    if (!(HeightMax > HeightMin)) Errors.Add("Неверный диапазон высоты: max должен быть больше min.");
                    if (HeightSource != "world_position_z") Errors.Add("Неизвестная семантика высоты «" + HeightSource + "».");
                }
                if (kind == Normal) InvertGreen = GroundJson.Bool(entry, "invert_green") ?? false;
            }
            foreach (string warning in (GroundJson.Arr(root, "warnings") ?? new List<object>()).OfType<string>())
                Warnings.Add("Экспорт: " + warning);
            if (Errors.Count > 0) return;
            VerifyWhole();
            if (Errors.Count == 0) Unpack();
        }

        private void VerifyWhole()
        {
            foreach (string kind in Kinds)
            {
                string path = wholeFiles[kind];
                if (!ReadHeader(path, out PngCodec.Header header, out string problem) || !PngCodec.IsSupported(header, out problem))
                {
                    Errors.Add(kind + ": " + problem);
                    continue;
                }
                bool same = string.Equals(FileSha256(path), wholeSha[kind], StringComparison.OrdinalIgnoreCase);
                if (kind == Color)
                {
                    ColorFile = System.IO.Path.GetFileName(path);
                    if (header.Width % Columns != 0 || header.Height % Rows != 0 ||
                        (long)(header.Width / Columns) * TileHeight != (long)(header.Height / Rows) * TileWidth || header.Width < MapWidth)
                    {
                        Errors.Add("Color " + header.Width + "×" + header.Height + " не совпадает с картой " + MapWidth + "×" + MapHeight +
                                   ": обрисовка должна сохранить кадр (тот же размер или больше в целое число раз).");
                        continue;
                    }
                    ColorScale = header.Width / (float)MapWidth;
                    ColorPainted = !same || Math.Abs(ColorScale - 1) > 1e-6;
                    if (ColorPainted)
                        Notes.Add("Color изменён после экспорта — загружается как обрисовка" +
                                  (Math.Abs(ColorScale - 1) > 1e-6 ? " (разрешение ×" + ColorScale.ToString("0.###") + ")" : "") + ".");
                    continue;
                }
                if (!same)
                    Errors.Add(kind + " изменён после экспорта или повреждён (sha256 не совпадает). Normal и Height не обрисовываются — экспортируйте карту заново.");
                else if (header.Width != MapWidth || header.Height != MapHeight)
                    Errors.Add(kind + ": " + header.Width + "×" + header.Height + " вместо " + MapWidth + "×" + MapHeight + ".");
                else if (kind == Height && (header.BitDepth != HeightBits || !header.HasAlpha))
                    Errors.Add("Height: нужен PNG " + HeightBits + " бит с покрытием в альфе.");
            }
        }

        // Нарезка трёх файлов на участки в кэш (повторная проверка того же экспорта — без повторной нарезки).
        private void Unpack()
        {
            string key = Sha256Hex(Encoding.UTF8.GetBytes(string.Join("|", Kinds.Select(kind => FileSha256(wholeFiles[kind]))) + "|" + Columns + "x" + Rows)).Substring(0, 16);
            string folder = System.IO.Path.GetFullPath(System.IO.Path.Combine(UnpackRoot, GroundImporter.Sanitize(MapId) + "_" + key));
            string done = System.IO.Path.Combine(folder, "done.txt");
            Dictionary<string, string> hashes = new Dictionary<string, string>();
            try
            {
                if (File.Exists(done))
                {
                    foreach (string line in File.ReadAllLines(done))
                    {
                        string[] parts = line.Split('\t');
                        if (parts.Length == 2) hashes[parts[0]] = parts[1];
                    }
                }
                else
                {
                    if (Directory.Exists(folder)) Directory.Delete(folder, true);
                    int step = 0;
                    foreach (string kind in Kinds)
                    {
                        string sub = System.IO.Path.Combine(folder, kind);
                        Directory.CreateDirectory(sub);
                        int index = step++;
                        SliceFile(wholeFiles[kind], Columns, Rows, kind == Color, tileKey => System.IO.Path.Combine(sub, TileFile(kind, tileKey)), hashes,
                            progress => EditorUtility.DisplayProgressBar("Карта из Blender", "Нарезка " + kind + " на участки…", (index + progress) / 3f));
                    }
                    File.WriteAllLines(done, hashes.Select(pair => pair.Key + "\t" + pair.Value));
                }
            }
            catch (Exception error) when (error is IOException || error is InvalidDataException || error is UnauthorizedAccessException)
            {
                Errors.Add("Карта не нарезана: " + error.Message);
                return;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            for (int y = 0; y < Rows; y++)
            {
                for (int x = 0; x < Columns; x++)
                {
                    GroundExportTile tile = new GroundExportTile { X = x, Y = y, Status = "complete" };
                    foreach (string kind in Kinds)
                    {
                        string full = System.IO.Path.Combine(folder, kind, TileFile(kind, tile.Key));
                        hashes.TryGetValue(full.Substring(folder.Length + 1).Replace('\\', '/'), out string sha);
                        tile.Passes[kind] = new GroundExportPass
                        {
                            Kind = kind, Status = "complete", Path = ColorFile.Length > 0 && kind == Color ? ColorFile + " · " + tile.Key : kind + " · " + tile.Key,
                            Revision = RevisionId, Sha256 = sha ?? string.Empty, FullPath = full, BitDepth = kind == Height ? HeightBits : 8,
                            Usable = File.Exists(full) && !string.IsNullOrEmpty(sha),
                            Problem = File.Exists(full) && !string.IsNullOrEmpty(sha) ? null : "участок не нарезан."
                        };
                    }
                    Tiles.Add(tile);
                }
            }
            Notes.Add("Карта целиком " + MapWidth + "×" + MapHeight + " нарезана на " + Columns * Rows + " участков " + TileWidth + "×" + TileHeight + ".");
            if (CharacterHeightPx > 0)
                Notes.Add("Рост эталона персонажа в экспорте ≈ " + CharacterHeightPx.ToString("0") + " px — подогнать людей можно в разделе «Рост людей».");
        }

        private string TileFile(string kind, string tileKey) =>
            GroundImporter.Sanitize(MapId) + "_" + tileKey + (kind == Color ? "" : "_" + kind.ToLowerInvariant()) + ".png";

        // Режет PNG по сетке (участок (x, y) — снизу слева) без преобразования
        // формата: строки читаются потоком из файла, участок сжимается сразу.
        public static void SliceFile(string source, int columns, int rows, bool srgb, Func<string, string> target,
            Dictionary<string, string> hashes = null, Action<float> progress = null)
        {
            if (!ReadHeader(source, out PngCodec.Header header, out string problem)) throw new InvalidDataException(problem);
            if (header.Width % columns != 0 || header.Height % rows != 0)
                throw new InvalidDataException("Размер " + header.Width + "×" + header.Height + " не делится на сетку " + columns + "×" + rows + ".");
            int tileW = header.Width / columns, tileH = header.Height / rows;
            int bpp = header.Channels * header.BitDepth / 8;
            PngCodec.RowWriter[] writers = new PngCodec.RowWriter[columns];
            string root = null;
            using (FileStream stream = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20))
            {
                PngCodec.DecodeRows(stream, null, (y, raw) =>
                {
                    int bandRow = y % tileH, row = rows - 1 - y / tileH;
                    for (int x = 0; x < columns; x++)
                    {
                        if (bandRow == 0) writers[x] = new PngCodec.RowWriter(tileW, tileH, header.BitDepth, header.ColorType, srgb);
                        writers[x].WriteRow(raw, x * tileW * bpp);
                        if (bandRow != tileH - 1) continue;
                        string key = LocationGroundTile.KeyOf(x, row);
                        string path = target(key);
                        byte[] bytes = writers[x].Finish();
                        File.WriteAllBytes(path, bytes);
                        if (hashes != null)
                        {
                            root = root ?? System.IO.Path.GetDirectoryName(System.IO.Path.GetDirectoryName(path));
                            hashes[path.Substring(root.Length + 1).Replace('\\', '/')] = Sha256Hex(bytes);
                        }
                    }
                    if (bandRow == tileH - 1) progress?.Invoke((y + 1) / (float)header.Height);
                });
            }
        }

        public static bool ReadHeader(string path, out PngCodec.Header header, out string problem)
        {
            byte[] head = new byte[33];
            try
            {
                using (FileStream stream = File.OpenRead(path))
                    if (stream.Read(head, 0, head.Length) < head.Length) { header = default; problem = "файл слишком короткий для PNG."; return false; }
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
            {
                header = default;
                problem = "файл не прочитан: " + error.Message;
                return false;
            }
            return PngCodec.TryReadHeader(head, out header, out problem);
        }

        private readonly Dictionary<string, string> fileSha = new Dictionary<string, string>();

        public string FileSha256(string path)
        {
            if (fileSha.TryGetValue(path, out string cached)) return cached;
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20))
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(stream);
                StringBuilder builder = new StringBuilder(64);
                foreach (byte b in hash) builder.Append(b.ToString("x2"));
                return fileSha[path] = builder.ToString();
            }
        }
    }
}

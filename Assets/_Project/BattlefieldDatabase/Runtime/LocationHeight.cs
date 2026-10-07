using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using UnityEngine;

namespace KingdomSurvival.BattlefieldDatabase
{
    // ПР-12О: высота одного участка — CPU-данные без потерь. Значение —
    // нормализованная абсолютная мировая Z Blender: raw / (2^бит − 1) в общем
    // диапазоне [min, max] экспорта; 16 бит хранятся как есть (ushort),
    // 8 бит — как 8 бит (номинально не повышаются). Покрытие — альфа
    // экспорта (0 — нет данных, это не «высота ноль»). Строки хранятся снизу
    // вверх: переворот строк PNG сделан один раз при импорте.
    //
    // Файл (*.bytes, TextAsset): «KSH1», версия, ширина, высота, разрядность,
    // min, max, затем deflate: ushort LE [w·h], byte [w·h] покрытия.
    public sealed class LocationHeightTileData
    {
        public const int FormatVersion = 1;
        private static readonly byte[] Magic = { (byte)'K', (byte)'S', (byte)'H', (byte)'1' };

        public int Width { get; }
        public int Height { get; }
        public int BitDepth { get; }
        public double Min { get; }
        public double Max { get; }
        // Индекс — y * Width + x, y = 0 — нижняя строка.
        public ushort[] Values { get; }
        public byte[] Coverage { get; }

        public LocationHeightTileData(int width, int height, int bitDepth, double min, double max, ushort[] values, byte[] coverage)
        {
            if (width <= 0 || height <= 0) throw new ArgumentException("Неверный размер участка высоты.");
            if (bitDepth != 8 && bitDepth != 16) throw new ArgumentException("Высота: нужна разрядность 8 или 16 бит.");
            if (values == null || coverage == null || values.Length != width * height || coverage.Length != width * height)
                throw new ArgumentException("Высота: размер данных не совпадает с участком.");
            Width = width; Height = height; BitDepth = bitDepth; Min = min; Max = max;
            Values = values; Coverage = coverage;
        }

        public double MaxRaw => BitDepth == 16 ? 65535.0 : 255.0;
        public double Step => (Max - Min) / MaxRaw;
        public double Decode(ushort raw) => Min + raw / MaxRaw * (Max - Min);
        public long MemoryBytes => (long)Width * Height * 3;

        // Из PNG экспорта (RGBA/серый 8/16 бит, строки сверху вниз): R —
        // значение, альфа — покрытие; без альфы покрытие полное.
        public static LocationHeightTileData FromPng(byte[] png, double min, double max)
        {
            ushort[] values = null;
            byte[] coverage = null;
            int width = 0, height = 0, channels = 0, alphaChannel = -1;
            double alphaMax = 255;
            PngCodec.Header header = PngCodec.DecodeRows(png, (y, row) =>
            {
                if (values == null)
                {
                    PngCodec.TryReadHeader(png, out PngCodec.Header h, out _);
                    width = h.Width; height = h.Height; channels = h.Channels;
                    alphaChannel = h.HasAlpha ? channels - 1 : -1;
                    alphaMax = h.BitDepth == 16 ? 65535 : 255;
                    values = new ushort[width * height];
                    coverage = new byte[width * height];
                }
                int target = (height - 1 - y) * width;
                for (int x = 0; x < width; x++)
                {
                    int s = x * channels;
                    ushort alpha = alphaChannel >= 0 ? row[s + alphaChannel] : (ushort)alphaMax;
                    if (alpha == 0)
                    {
                        values[target + x] = 0;
                        coverage[target + x] = 0;
                        continue;
                    }
                    values[target + x] = row[s];
                    coverage[target + x] = (byte)Math.Max(1, Math.Round(alpha / alphaMax * 255));
                }
            });
            return new LocationHeightTileData(header.Width, header.Height, header.BitDepth, min, max, values, coverage);
        }

        public byte[] Serialize()
        {
            using (MemoryStream file = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(file))
            {
                writer.Write(Magic);
                writer.Write(FormatVersion);
                writer.Write(Width); writer.Write(Height); writer.Write(BitDepth);
                writer.Write(Min); writer.Write(Max);
                byte[] payload = new byte[Values.Length * 2 + Coverage.Length];
                for (int i = 0; i < Values.Length; i++)
                {
                    payload[i * 2] = (byte)Values[i];
                    payload[i * 2 + 1] = (byte)(Values[i] >> 8);
                }
                Buffer.BlockCopy(Coverage, 0, payload, Values.Length * 2, Coverage.Length);
                using (MemoryStream compressed = new MemoryStream())
                {
                    using (DeflateStream deflate = new DeflateStream(compressed, System.IO.Compression.CompressionLevel.Optimal, true))
                        deflate.Write(payload, 0, payload.Length);
                    writer.Write((int)compressed.Length);
                    writer.Write(compressed.GetBuffer(), 0, (int)compressed.Length);
                }
                writer.Flush();
                return file.ToArray();
            }
        }

        public static LocationHeightTileData Deserialize(byte[] data)
        {
            if (data == null || data.Length < 40)
                throw new InvalidDataException("Данные высоты повреждены или пусты.");
            using (MemoryStream file = new MemoryStream(data))
            using (BinaryReader reader = new BinaryReader(file))
            {
                byte[] magic = reader.ReadBytes(4);
                for (int i = 0; i < 4; i++)
                    if (magic[i] != Magic[i]) throw new InvalidDataException("Это не данные высоты Kingdom Survival.");
                int version = reader.ReadInt32();
                if (version != FormatVersion) throw new InvalidDataException("Неизвестная версия данных высоты: " + version + ".");
                int width = reader.ReadInt32(), height = reader.ReadInt32(), bitDepth = reader.ReadInt32();
                double min = reader.ReadDouble(), max = reader.ReadDouble();
                int length = reader.ReadInt32();
                if (width <= 0 || height <= 0 || width > 65535 || height > 65535 || length <= 0 || length > data.Length - file.Position)
                    throw new InvalidDataException("Данные высоты повреждены (заголовок).");
                int count = width * height;
                byte[] payload = new byte[count * 3];
                using (MemoryStream source = new MemoryStream(data, (int)file.Position, length))
                using (DeflateStream inflate = new DeflateStream(source, CompressionMode.Decompress))
                {
                    int read = 0;
                    while (read < payload.Length)
                    {
                        int n = inflate.Read(payload, read, payload.Length - read);
                        if (n <= 0) throw new InvalidDataException("Данные высоты обрезаны.");
                        read += n;
                    }
                }
                ushort[] values = new ushort[count];
                for (int i = 0; i < count; i++) values[i] = (ushort)(payload[i * 2] | payload[i * 2 + 1] << 8);
                byte[] coverage = new byte[count];
                Buffer.BlockCopy(payload, count * 2, coverage, 0, count);
                return new LocationHeightTileData(width, height, bitDepth, min, max, values, coverage);
            }
        }
    }

    // Результат выборки высоты под точкой.
    public struct HeightSample
    {
        public bool Valid;
        // Абсолютная мировая Z Blender (Blender Unit) и в метрах.
        public double Blender;
        public double Meters;
        // Достоверность 0..1: покрытие, взвешенное по соседям (у края и
        // силуэта — меньше 1).
        public float Coverage;
        public int Column;
        public int Row;
        // Ближайший пиксель (диагностика) или билинейно; true — разрыв
        // поверхности: интерполяция не смешивала соседей.
        public bool Nearest;
        public bool Discontinuity;
        public string Reason;

        public static HeightSample Invalid(string reason, int column = -1, int row = -1) =>
            new HeightSample { Valid = false, Reason = reason, Column = column, Row = row };
    }

    // Сервис выборки высоты места: позиция на рисунке места → виртуальная
    // карта → участок → декодирование общего диапазона. Данные грузятся один
    // раз (при сборке места) и освобождаются вместе с ним.
    public sealed class LocationHeightField
    {
        private readonly LocationHeightTileData[] tiles;
        public LocationGroundGrid Grid { get; }
        public double Min { get; }
        public double Max { get; }
        public double MetersPerBlenderUnit { get; }
        public double SeamlessBlender { get; }
        public long MemoryBytes { get; }
        public int LoadedTiles { get; }
        public IReadOnlyList<string> Problems { get; }

        public LocationHeightField(LocationGroundGrid grid, double min, double max, double metersPerUnit, double seamlessMeters,
            LocationHeightTileData[] data, IReadOnlyList<string> problems = null)
        {
            if (data == null || data.Length != grid.Columns * grid.Rows) throw new ArgumentException("Число участков высоты не совпадает с сеткой.");
            Grid = grid; Min = min; Max = max;
            MetersPerBlenderUnit = metersPerUnit > 0 ? metersPerUnit : 1;
            SeamlessBlender = seamlessMeters > 0 ? seamlessMeters / MetersPerBlenderUnit : double.PositiveInfinity;
            tiles = data;
            long memory = 0; int loaded = 0;
            foreach (LocationHeightTileData tile in data)
            {
                if (tile == null) continue;
                if (tile.Width != grid.TileWidth || tile.Height != grid.TileHeight)
                    throw new ArgumentException("Размер участка высоты " + tile.Width + "×" + tile.Height + " не совпадает с сеткой " + grid.TileWidth + "×" + grid.TileHeight + ".");
                memory += tile.MemoryBytes; loaded++;
            }
            MemoryBytes = memory; LoadedTiles = loaded;
            Problems = problems ?? Array.Empty<string>();
        }

        // Высота места по данным сборки; нет высоты — null, проблемы — в errors.
        public static LocationHeightField Load(LocationGroundDefinition ground, LocalLocationDefinition location, List<string> errors)
        {
            // Экспорт SINGLE импортируется сеткой 1×1; прежний рисунок места высоты не имеет.
            if (ground == null || !ground.IsTiled || !ground.HeightEnabled || !ground.HasAnyHeight) return null;
            LocationGroundGrid grid = LocationGroundGrid.For(ground, location);
            LocationHeightTileData[] data = new LocationHeightTileData[grid.Columns * grid.Rows];
            List<string> problems = new List<string>();
            foreach (LocationGroundTile tile in ground.Tiles)
            {
                if (tile?.Height == null || tile.X < 0 || tile.Y < 0 || tile.X >= grid.Columns || tile.Y >= grid.Rows) continue;
                try
                {
                    LocationHeightTileData decoded = LocationHeightTileData.Deserialize(tile.Height.bytes);
                    if (decoded.Width != grid.TileWidth || decoded.Height != grid.TileHeight)
                        problems.Add("Высота " + tile.Key + ": размер " + decoded.Width + "×" + decoded.Height + " вместо " + grid.TileWidth + "×" + grid.TileHeight + ".");
                    else if (Math.Abs(decoded.Min - ground.HeightMin) > 1e-9 || Math.Abs(decoded.Max - ground.HeightMax) > 1e-9)
                        problems.Add("Высота " + tile.Key + ": свой диапазон " + decoded.Min + "…" + decoded.Max + " вместо общего — участок не используется.");
                    else data[tile.Y * grid.Columns + tile.X] = decoded;
                }
                catch (Exception error) when (error is InvalidDataException || error is ArgumentException)
                {
                    problems.Add("Высота " + tile.Key + ": " + error.Message);
                }
            }
            errors?.AddRange(problems);
            return new LocationHeightField(grid, ground.HeightMin, ground.HeightMax, ground.MetersPerBlenderUnit, ground.SeamlessMeters, data, problems);
        }

        public bool HasTile(int column, int row) =>
            column >= 0 && row >= 0 && column < Grid.Columns && row < Grid.Rows && tiles[row * Grid.Columns + column] != null;

        // Пиксель виртуальной карты (целые индексы, снизу слева) — через
        // границы участков: соседние пиксели шва берутся у соседа.
        public bool TryPixel(long x, long y, out ushort raw, out byte coverage, out LocationHeightTileData tile)
        {
            raw = 0; coverage = 0; tile = null;
            if (x < 0 || y < 0 || x >= Grid.VirtualWidth || y >= Grid.VirtualHeight) return false;
            int column = (int)(x / Grid.TileWidth), row = (int)(y / Grid.TileHeight);
            tile = tiles[row * Grid.Columns + column];
            if (tile == null) return false;
            int index = (int)(y - (long)row * Grid.TileHeight) * tile.Width + (int)(x - (long)column * Grid.TileWidth);
            coverage = tile.Coverage[index];
            raw = tile.Values[index];
            return coverage > 0;
        }

        // Точка рисунка места (пиксели, Y вниз).
        public bool TrySampleCanvas(Vector2 canvasPixel, out HeightSample sample, bool nearest = false)
        {
            Vector2d v = Grid.CanvasToVirtual(canvasPixel.x, canvasPixel.y);
            return TrySampleVirtual(v.X, v.Y, out sample, nearest);
        }

        // Непрерывные координаты виртуальной карты (снизу слева); центр
        // пикселя k — k + 0,5.
        public bool TrySampleVirtual(double x, double y, out HeightSample sample, bool nearest = false)
        {
            if (!Grid.TryTileOf(x, y, out int column, out int row))
            {
                sample = HeightSample.Invalid("За пределами карты высот.");
                return false;
            }
            if (!HasTile(column, row))
            {
                sample = HeightSample.Invalid("Нет высоты у участка " + LocationGroundTile.KeyOf(column, row) + ".", column, row);
                return false;
            }
            long px = Math.Min(Grid.VirtualWidth - 1, (long)Math.Floor(x)), py = Math.Min(Grid.VirtualHeight - 1, (long)Math.Floor(y));
            bool hasCenter = TryPixel(px, py, out ushort centerRaw, out byte centerCoverage, out LocationHeightTileData centerTile);
            if (nearest)
            {
                if (!hasCenter)
                {
                    sample = HeightSample.Invalid("Нет данных высоты (прозрачный пиксель).", column, row);
                    return false;
                }
                sample = Make(centerTile.Decode(centerRaw), centerCoverage / 255f, column, row);
                sample.Nearest = true;
                return true;
            }

            double gx = x - .5, gy = y - .5;
            long x0 = (long)Math.Floor(gx), y0 = (long)Math.Floor(gy);
            double fx = gx - x0, fy = gy - y0;
            double sum = 0, weights = 0, coverage = 0, low = double.MaxValue, high = double.MinValue;
            for (int k = 0; k < 4; k++)
            {
                long sx = x0 + (k & 1), sy = y0 + (k >> 1);
                double weight = ((k & 1) == 1 ? fx : 1 - fx) * ((k >> 1) == 1 ? fy : 1 - fy);
                if (weight <= 0 || !TryPixel(sx, sy, out ushort raw, out byte cover, out LocationHeightTileData tile)) continue;
                double value = tile.Decode(raw);
                sum += value * weight; weights += weight; coverage += weight * cover / 255.0;
                low = Math.Min(low, value); high = Math.Max(high, value);
            }
            if (weights <= 0)
            {
                sample = HeightSample.Invalid("Нет данных высоты (прозрачная область).", column, row);
                return false;
            }
            if (high - low > SeamlessBlender)
            {
                // Разрыв поверхности (ступень, край моста): соседей не смешивать.
                if (!hasCenter)
                {
                    sample = HeightSample.Invalid("Разрыв поверхности у пустоты.", column, row);
                    return false;
                }
                sample = Make(centerTile.Decode(centerRaw), centerCoverage / 255f, column, row);
                sample.Nearest = true; sample.Discontinuity = true;
                return true;
            }
            sample = Make(sum / weights, (float)coverage, column, row);
            return true;
        }

        private HeightSample Make(double blender, float coverage, int column, int row) => new HeightSample
        {
            Valid = true, Blender = blender, Meters = blender * MetersPerBlenderUnit,
            Coverage = Mathf.Clamp01(coverage), Column = column, Row = row
        };
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace KingdomSurvival.BattlefieldDatabase.Tests
{
    // ПР-12О: PNG16 без потерь, семантика высоты, координаты участков,
    // выборка через швы и камера места.
    public sealed class LocationGroundTests
    {
        // ---------------- PNG ----------------

        [Test]
        public void Png16RoundTripsEveryFilterAndKeepsLowBits()
        {
            const int w = 37, h = 9;
            ushort[] samples = new ushort[w * h * 4];
            System.Random random = new System.Random(7);
            for (int i = 0; i < samples.Length; i++) samples[i] = (ushort)random.Next(0, 65536);
            samples[0] = 32768; samples[4] = 32769;
            byte[] png = PngCodec.Encode(w, h, 16, PngCodec.Rgba, samples, new[] { 0, 1, 2, 3, 4 });
            ushort[] decoded = PngCodec.Decode(png, out PngCodec.Header header);
            Assert.That(header.BitDepth, Is.EqualTo(16));
            Assert.That(header.ColorType, Is.EqualTo(PngCodec.Rgba));
            Assert.That(decoded, Is.EqualTo(samples));
            Assert.That(decoded[0], Is.Not.EqualTo(decoded[4]));
        }

        // Большие карты читаются потоком из файла: IDAT разбит на много мелких
        // блоков (как пишет аддон Blender), CRC каждого проверяется.
        [Test]
        public void StreamDecodeAcrossManySmallIdatChunks()
        {
            const int w = 23, h = 11;
            ushort[] samples = new ushort[w * h * 2];
            for (int i = 0; i < samples.Length; i++) samples[i] = (ushort)(i * 2654435761u >> 16);
            byte[] png = SplitIdat(PngCodec.Encode(w, h, 16, PngCodec.GrayAlpha, samples, new[] { 1, 4, 0 }), 7);
            List<ushort> streamed = new List<ushort>();
            int rawRows = 0;
            using (MemoryStream stream = new MemoryStream(png))
                PngCodec.DecodeRows(stream, (y, row) => streamed.AddRange(row), (y, raw) => { Assert.That(raw.Length, Is.EqualTo(w * 4)); rawRows++; });
            Assert.That(streamed, Is.EqualTo(samples));
            Assert.That(rawRows, Is.EqualTo(h));
            Assert.That(PngCodec.Decode(png, out _), Is.EqualTo(samples));
            // Повреждённый средний блок — ошибка CRC, а не тихо неверные данные.
            byte[] broken = (byte[])png.Clone();
            broken[png.Length / 2] ^= 0x40;
            using (MemoryStream stream = new MemoryStream(broken))
                Assert.That(() => PngCodec.DecodeRows(stream, (y, row) => { }), Throws.TypeOf<InvalidDataException>());
            // Строки через RowWriter в том же формате — тот же файл по значениям.
            PngCodec.RowWriter writer = new PngCodec.RowWriter(w, h, 16, PngCodec.GrayAlpha, false);
            using (MemoryStream stream = new MemoryStream(png))
                PngCodec.DecodeRows(stream, null, (y, raw) => writer.WriteRow(raw, 0));
            Assert.That(PngCodec.Decode(writer.Finish(), out PngCodec.Header header), Is.EqualTo(samples));
            Assert.That(header.ColorType, Is.EqualTo(PngCodec.GrayAlpha));
        }

        // Пересобрать PNG, разрезав данные IDAT на блоки по size байт.
        private static byte[] SplitIdat(byte[] png, int size)
        {
            List<byte> idat = new List<byte>();
            List<byte[]> before = new List<byte[]>();
            int offset = 8;
            while (offset < png.Length)
            {
                int length = png[offset] << 24 | png[offset + 1] << 16 | png[offset + 2] << 8 | png[offset + 3];
                string name = System.Text.Encoding.ASCII.GetString(png, offset + 4, 4);
                byte[] chunk = new byte[length + 12];
                Array.Copy(png, offset, chunk, 0, chunk.Length);
                if (name == "IDAT") for (int i = 0; i < length; i++) idat.Add(png[offset + 8 + i]);
                else if (name != "IEND") before.Add(chunk);
                offset += length + 12;
            }
            using (MemoryStream file = new MemoryStream())
            {
                file.Write(png, 0, 8);
                foreach (byte[] chunk in before) file.Write(chunk, 0, chunk.Length);
                void Chunk(string name, byte[] body)
                {
                    byte[] data = new byte[body.Length + 12];
                    data[0] = (byte)(body.Length >> 24); data[1] = (byte)(body.Length >> 16); data[2] = (byte)(body.Length >> 8); data[3] = (byte)body.Length;
                    for (int i = 0; i < 4; i++) data[4 + i] = (byte)name[i];
                    Array.Copy(body, 0, data, 8, body.Length);
                    uint crc = PngCodec.Crc32(data, 4, body.Length + 4);
                    data[body.Length + 8] = (byte)(crc >> 24); data[body.Length + 9] = (byte)(crc >> 16); data[body.Length + 10] = (byte)(crc >> 8); data[body.Length + 11] = (byte)crc;
                    file.Write(data, 0, data.Length);
                }
                for (int i = 0; i < idat.Count; i += size) Chunk("IDAT", idat.GetRange(i, Math.Min(size, idat.Count - i)).ToArray());
                Chunk("IEND", Array.Empty<byte>());
                return file.ToArray();
            }
        }

        [Test]
        public void Png8AndGrayDecodeExactly()
        {
            ushort[] gray = new ushort[16 * 3];
            for (int i = 0; i < gray.Length; i++) gray[i] = (ushort)(i * 997 % 65536);
            Assert.That(PngCodec.Decode(PngCodec.Encode(16, 3, 16, PngCodec.Gray, gray, new[] { 4, 3 }), out _), Is.EqualTo(gray));
            ushort[] rgb = new ushort[5 * 4 * 3];
            for (int i = 0; i < rgb.Length; i++) rgb[i] = (ushort)(i * 13 % 256);
            Assert.That(PngCodec.Decode(PngCodec.Encode(5, 4, 8, PngCodec.Rgb, rgb, new[] { 1, 2, 3, 4, 0 }), out _), Is.EqualTo(rgb));
        }

        [Test]
        public void DamagedOrUnsupportedPngFailsWithClearError()
        {
            byte[] png = PngCodec.Encode(4, 4, 16, PngCodec.Rgba, new ushort[64]);
            byte[] crc = (byte[])png.Clone();
            crc[40] ^= 0x55;
            Assert.That(() => PngCodec.Decode(crc, out _), Throws.TypeOf<InvalidDataException>().With.Message.Contains("контрольная сумма"));
            byte[] cut = new byte[png.Length - 20];
            Array.Copy(png, cut, cut.Length);
            Assert.That(() => PngCodec.Decode(cut, out _), Throws.TypeOf<InvalidDataException>());
            byte[] interlaced = (byte[])png.Clone();
            interlaced[28] = 1;
            Assert.That(PngCodec.TryReadHeader(interlaced, out PngCodec.Header header, out _), Is.True);
            Assert.That(PngCodec.IsSupported(header, out string error), Is.False);
            Assert.That(error, Does.Contain("Adam7"));
            Assert.That(PngCodec.TryReadHeader(new byte[40], out _, out error), Is.False);
            Assert.That(error, Does.Contain("не PNG"));
        }

        // ---------------- Высота ----------------

        // PNG высоты как у экспортёра: RGBA16, RGB = значение, A = покрытие,
        // строки сверху вниз. value(x, yTop) → raw16; alpha(x, yTop) → 0..65535.
        public static byte[] HeightPng(int w, int h, Func<int, int, int> value, Func<int, int, int> alpha = null, int bits = 16)
        {
            ushort[] samples = new ushort[w * h * 4];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = (y * w + x) * 4;
                    int raw = value(x, y);
                    samples[i] = samples[i + 1] = samples[i + 2] = (ushort)raw;
                    samples[i + 3] = (ushort)(alpha != null ? alpha(x, y) : bits == 16 ? 65535 : 255);
                }
            return PngCodec.Encode(w, h, bits, PngCodec.Rgba, samples, new[] { 0, 1, 2, 3, 4 });
        }

        [Test]
        public void HeightKeeps16BitsRangeAndInvalidPixels()
        {
            // 300 разных высот, пара 32768/32769, прозрачный и валидный чёрный пиксели.
            byte[] png = HeightPng(300, 2, (x, y) => y == 0 ? x * 200 : x == 0 ? 0 : x == 1 ? 32768 : x == 2 ? 32769 : 65535,
                (x, y) => y == 1 && x == 3 ? 0 : 65535);
            LocationHeightTileData data = LocationHeightTileData.FromPng(png, -2, 4);
            LocationHeightTileData copy = LocationHeightTileData.Deserialize(data.Serialize());
            Assert.That(copy.Values, Is.EqualTo(data.Values));
            Assert.That(copy.Coverage, Is.EqualTo(data.Coverage));
            Assert.That(copy.Min, Is.EqualTo(-2)); Assert.That(copy.Max, Is.EqualTo(4));
            // Верхняя строка файла — верхняя (y = 1) в данных: переворот один раз.
            Assert.That(copy.Values[1 * 300 + 299], Is.EqualTo(299 * 200));
            HashSet<ushort> distinct = new HashSet<ushort>();
            for (int x = 0; x < 300; x++) distinct.Add(copy.Values[300 + x]);
            Assert.That(distinct.Count, Is.EqualTo(300));
            // Нижняя строка: 0 → min, 32768 и 32769 различимы, прозрачный — нет данных.
            Assert.That(copy.Decode(copy.Values[0]), Is.EqualTo(-2).Within(1e-12));
            Assert.That(copy.Coverage[0], Is.EqualTo(255));
            Assert.That(copy.Values[1], Is.EqualTo(32768));
            Assert.That(copy.Values[2], Is.EqualTo(32769));
            Assert.That(copy.Decode(copy.Values[2]) - copy.Decode(copy.Values[1]), Is.EqualTo(6.0 / 65535).Within(1e-12));
            Assert.That(copy.Coverage[3], Is.EqualTo(0));
            Assert.That(copy.Decode(65535), Is.EqualTo(4).Within(1e-12));
            Assert.That(copy.Decode(32768), Is.EqualTo(-2 + 6 * 32768 / 65535.0).Within(copy.Step / 2));
        }

        [Test]
        public void EightBitHeightIsNotPromoted()
        {
            byte[] png = HeightPng(4, 1, (x, y) => x * 85, null, 8);
            LocationHeightTileData data = LocationHeightTileData.FromPng(png, 0, 10);
            Assert.That(data.BitDepth, Is.EqualTo(8));
            Assert.That(data.Decode(data.Values[3]), Is.EqualTo(10).Within(1e-9));
            Assert.That(data.Decode(data.Values[1]), Is.EqualTo(10 / 3.0).Within(1e-9));
            Assert.That(data.Step, Is.EqualTo(10 / 255.0).Within(1e-12));
        }

        // ---------------- Координаты ----------------

        [Test]
        public void TileGridMapsCornersBottomLeftOrigin()
        {
            // 2×2 участка 100×50; место 200×100 (1:1), Y вниз.
            LocationGroundGrid grid = new LocationGroundGrid(2, 2, 100, 50, 200, 100);
            Assert.That(grid.IsOneToOne, Is.True);
            AssertTile(grid, new Vector2(1, 1), 0, 1);       // верх слева на рисунке → X000_Y001
            AssertTile(grid, new Vector2(1, 99), 0, 0);      // низ слева → X000_Y000
            AssertTile(grid, new Vector2(199, 99), 1, 0);    // низ справа → X001_Y000
            AssertTile(grid, new Vector2(199, 1), 1, 1);     // верх справа → X001_Y001
            // Полуоткрытые интервалы: граница 100 — правый участок; внешний край — последний.
            Assert.That(grid.TryTileOf(100, 10, out int c, out _) && c == 1, Is.True);
            Assert.That(grid.TryTileOf(200, 100, out c, out int r) && c == 1 && r == 1, Is.True);
            Assert.That(grid.TryTileOf(200.01, 10, out _, out _), Is.False);
            Assert.That(grid.TryTileOf(-0.01, 10, out _, out _), Is.False);
            Rect bottomLeft = grid.TileCanvasRect(0, 0);
            Assert.That(bottomLeft, Is.EqualTo(new Rect(0, 50, 100, 50)));
            Assert.That(grid.TileCanvasRect(1, 1), Is.EqualTo(new Rect(100, 0, 100, 50)));
            Vector2d v = grid.CanvasToVirtual(0, 0);
            Assert.That(v.X, Is.EqualTo(0)); Assert.That(v.Y, Is.EqualTo(100));
            Assert.That(grid.VirtualToCanvas(v.X, v.Y), Is.EqualTo(Vector2.zero));
        }

        private static void AssertTile(LocationGroundGrid grid, Vector2 canvas, int column, int row)
        {
            Vector2d v = grid.CanvasToVirtual(canvas.x, canvas.y);
            Assert.That(grid.TryTileOf(v.X, v.Y, out int c, out int r), Is.True);
            Assert.That(new Vector2Int(c, r), Is.EqualTo(new Vector2Int(column, row)), "точка " + canvas);
        }

        // Сетка 2×1 участков по 16×4 с непрерывным градиентом по X через шов.
        private static LocationHeightField GradientField(out double perPixel, Func<int, int, int> alpha = null, int columns = 2)
        {
            const int w = 16, h = 4;
            perPixel = 100;
            LocationHeightTileData[] tiles = new LocationHeightTileData[columns];
            for (int c = 0; c < columns; c++)
            {
                int column = c;
                byte[] png = HeightPng(w, h, (x, y) => 1000 + (column * w + x) * 100, alpha != null ? (x, y) => alpha(column * w + x, y) : (Func<int, int, int>)null);
                tiles[c] = LocationHeightTileData.FromPng(png, 0, 65535);
            }
            return new LocationHeightField(new LocationGroundGrid(columns, 1, w, h, columns * w, h), 0, 65535, 1, 1e9, tiles);
        }

        [Test]
        public void BilinearSampleIsContinuousAcrossSeam()
        {
            LocationHeightField field = GradientField(out double step);
            // Центр пикселя k — k + 0,5: значение 1000 + k·100; между центрами — линейно.
            double previous = double.NaN;
            for (double x = 14.5; x <= 18.5; x += .25)
            {
                Assert.That(field.TrySampleVirtual(x, 2, out HeightSample sample), Is.True);
                Assert.That(sample.Blender, Is.EqualTo(1000 + (x - .5) * step).Within(1e-6), "x = " + x);
                if (!double.IsNaN(previous)) Assert.That(sample.Blender - previous, Is.EqualTo(step * .25).Within(1e-6));
                previous = sample.Blender;
            }
            Assert.That(field.TrySampleVirtual(16.0, 2, out HeightSample seam), Is.True);
            Assert.That(seam.Column, Is.EqualTo(1));
            Assert.That(seam.Blender, Is.EqualTo(1000 + 15.5 * step).Within(1e-6));
            Assert.That(field.TrySampleVirtual(15.2, 2, out HeightSample nearest, true), Is.True);
            Assert.That(nearest.Blender, Is.EqualTo(1000 + 15 * step).Within(1e-9));
        }

        [Test]
        public void OutsideMissingAndTransparentAreInvalidNotZero()
        {
            LocationHeightField field = GradientField(out _, (x, y) => x == 3 ? 0 : x == 4 ? 32768 : 65535);
            Assert.That(field.TrySampleVirtual(-1, 1, out HeightSample outside), Is.False);
            Assert.That(outside.Valid, Is.False);
            Assert.That(field.TrySampleVirtual(40, 1, out _), Is.False);
            Assert.That(field.TrySampleVirtual(3.5, 1.5, out HeightSample hole, true), Is.False);
            Assert.That(hole.Reason, Does.Contain("Нет данных"));
            // Билинейно у прозрачного пикселя: веса перенормированы, достоверность ниже 1.
            Assert.That(field.TrySampleVirtual(3.9, 1.5, out HeightSample edge), Is.True);
            Assert.That(edge.Blender, Is.EqualTo(1000 + 4 * 100).Within(1e-6));
            Assert.That(edge.Coverage, Is.LessThan(1));
            // Участка нет вовсе.
            LocationHeightField partial = new LocationHeightField(new LocationGroundGrid(2, 1, 16, 4, 32, 4), 0, 1, 1, 1,
                new[] { LocationHeightTileData.FromPng(HeightPng(16, 4, (x, y) => 5), 0, 1), null });
            Assert.That(partial.TrySampleVirtual(20, 2, out HeightSample missing), Is.False);
            Assert.That(missing.Reason, Does.Contain("X001_Y000"));
        }

        [Test]
        public void SharpStepIsNotBlended()
        {
            LocationHeightTileData tile = LocationHeightTileData.FromPng(HeightPng(4, 1, (x, y) => x < 2 ? 0 : 65535), 0, 10);
            LocationHeightField field = new LocationHeightField(new LocationGroundGrid(1, 1, 4, 1, 4, 1), 0, 10, 1, .5, new[] { tile });
            Assert.That(field.TrySampleVirtual(1.9, .5, out HeightSample left), Is.True);
            Assert.That(left.Discontinuity, Is.True);
            Assert.That(left.Blender, Is.EqualTo(0).Within(1e-9));
            Assert.That(field.TrySampleVirtual(2.1, .5, out HeightSample right), Is.True);
            Assert.That(right.Blender, Is.EqualTo(10).Within(1e-9));
        }

        [Test]
        public void NegativeRangeDecodesAbsoluteHeight()
        {
            LocationHeightTileData tile = LocationHeightTileData.FromPng(HeightPng(2, 1, (x, y) => x == 0 ? 0 : 65535), -12.5, -2.5);
            LocationHeightField field = new LocationHeightField(new LocationGroundGrid(1, 1, 2, 1, 2, 1), -12.5, -2.5, 2, 1e9, new[] { tile });
            Assert.That(field.TrySampleVirtual(.5, .5, out HeightSample low, true), Is.True);
            Assert.That(low.Blender, Is.EqualTo(-12.5).Within(1e-12));
            Assert.That(low.Meters, Is.EqualTo(-25).Within(1e-12));
            Assert.That(field.TrySampleVirtual(1.5, .5, out HeightSample high, true), Is.True);
            Assert.That(high.Blender, Is.EqualTo(-2.5).Within(1e-12));
        }

        [Test]
        public void MemoryIsThreeBytesPerPixel()
        {
            LocationHeightTileData tile = new LocationHeightTileData(2048, 2048, 16, 0, 1, new ushort[2048 * 2048], new byte[2048 * 2048]);
            Assert.That(tile.MemoryBytes * 16 / 1048576, Is.EqualTo(192));
        }

        // Замер для настоящего размера: участок 2048×2048, 16 бит, плавный рельеф.
        // Импорт (PNG → данные) и загрузка места (данные → память) — один раз.
        [Test]
        public void LargeTileHeightCostIsMeasured()
        {
            const int size = 2048;
            byte[] png = HeightPng(size, size, (x, y) => (int)(32768 + 20000 * Math.Sin(x * .01) * Math.Cos(y * .013)));
            System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
            LocationHeightTileData data = LocationHeightTileData.FromPng(png, -10, 30);
            long decode = clock.ElapsedMilliseconds;
            byte[] stored = data.Serialize();
            clock.Restart();
            LocationHeightTileData loaded = LocationHeightTileData.Deserialize(stored);
            long load = clock.ElapsedMilliseconds;
            Assert.That(loaded.Values, Is.EqualTo(data.Values));
            TestContext.WriteLine("Участок 2048×2048: PNG " + png.Length / 1048576f + " МиБ, импорт " + decode + " мс; данные на диске " +
                                  stored.Length / 1048576f + " МиБ, загрузка " + load + " мс; в памяти " + loaded.MemoryBytes / 1048576f + " МиБ");
            Assert.That(loaded.MemoryBytes, Is.EqualTo(size * size * 3));
        }

        // ---------------- Земля в данных места ----------------

        [Test]
        public void IncompleteGroundIsBlockedForRuntime()
        {
            LocalLocationDefinition location = new LocalLocationDefinition { Id = "t", CanvasWidth = 200, CanvasHeight = 100 };
            LocationVisualDefinition visual = new LocationVisualDefinition { LocationId = "t" };
            visual.Ground = new LocationGroundDefinition
            {
                Mode = LocationGroundMode.Tiles, MapId = "Map", Columns = 2, Rows = 2, TileWidth = 100, TileHeight = 50,
                HeightEnabled = true, HeightMin = 0, HeightMax = 1
            };
            Texture2D texture = new Texture2D(4, 4);
            Sprite sprite = Sprite.Create(texture, new Rect(0, 0, 4, 4), Vector2.one / 2);
            visual.Ground.Tiles.Add(new LocationGroundTile { X = 0, Y = 0, Color = sprite });
            List<string> errors = LocationGroundLayout.RuntimeErrors(visual, location);
            Assert.That(string.Join("\n", errors), Does.Contain("X001_Y001").And.Contain("Нет Color").And.Contain("нет Height"));
            visual.Ground.HeightPartialAllowed = true;
            Assert.That(string.Join("\n", LocationGroundLayout.RuntimeErrors(visual, location)), Does.Not.Contain("нет Height"));
            visual.Ground.Mode = LocationGroundMode.Single;
            Assert.That(LocationGroundLayout.RuntimeErrors(visual, location), Is.Empty, "прежний рисунок места — без проверок участков");
            List<LocationGroundLayout.Piece> pieces = LocationGroundLayout.Resolve(visual, location);
            Assert.That(pieces.Count, Is.EqualTo(1));
            Assert.That(pieces[0].CanvasRect, Is.EqualTo(new Rect(0, 0, 200, 100)));
            UnityEngine.Object.DestroyImmediate(sprite);
            UnityEngine.Object.DestroyImmediate(texture);
        }

        // ---------------- Камера ----------------

        private static readonly Rect Map = new Rect(0, 0, 4000, 2000);

        [Test]
        public void CameraViewportStaysInsideMapAndCentersSmallAxis()
        {
            foreach (float aspect in new[] { 16 / 9f, 16 / 10f, 21 / 9f })
            {
                Vector2 view = new Vector2(1080 * aspect, 1080);
                foreach (Vector2 leader in new[] { Vector2.zero, new Vector2(4000, 2000), new Vector2(4000, 0), new Vector2(0, 2000), new Vector2(2000, 1000) })
                {
                    Vector2 center = LocationCameraFollow.ClampView(leader, view, Map);
                    Assert.That(center.x - view.x / 2, Is.GreaterThanOrEqualTo(-1e-3f));
                    Assert.That(center.x + view.x / 2, Is.LessThanOrEqualTo(4000 + 1e-3f));
                    Assert.That(center.y - view.y / 2, Is.GreaterThanOrEqualTo(-1e-3f));
                    Assert.That(center.y + view.y / 2, Is.LessThanOrEqualTo(2000 + 1e-3f));
                }
            }
            // Карта уже кадра по ширине — кадр по центру, без перевёрнутых пределов.
            Vector2 narrow = LocationCameraFollow.ClampView(new Vector2(10, 300), new Vector2(1920, 1080), new Rect(0, 0, 1000, 3000));
            Assert.That(narrow.x, Is.EqualTo(500));
            Assert.That(narrow.y, Is.EqualTo(540));
        }

        [Test]
        public void CameraSmoothingIsFrameRateIndependentAndSettles()
        {
            LocationCameraSettings settings = new LocationCameraSettings { Version = 1, SmoothTime = .25f, SnapDistance = 0 };
            Vector2 view = new Vector2(1920, 1080);
            Vector2 Run(int fps, float seconds)
            {
                LocationCameraFollow follow = new LocationCameraFollow();
                follow.Step(new Vector2(1000, 1000), view, Map, settings, 0, true);
                float dt = 1f / fps;
                Vector2 center = follow.Center;
                int steps = Mathf.RoundToInt(seconds * fps);
                for (int i = 1; i <= steps; i++)
                {
                    // Командир идёт вправо 600 пикселей в секунду, через секунду стоит.
                    Vector2 leader = new Vector2(1000 + 600 * Mathf.Min(i * dt, 1), 1000);
                    Vector2 next = follow.Step(leader, view, Map, settings, dt);
                    Assert.That(next.x, Is.GreaterThanOrEqualTo(center.x - 1e-3f), "кадр не дёргается назад");
                    center = next;
                }
                return center;
            }
            // Одинаковое время (целое число кадров у обеих частот).
            Vector2 slow = Run(30, .5f), fast = Run(144, .5f);
            Assert.That(Mathf.Abs(slow.x - fast.x), Is.LessThan(10f), "30 и 144 кадров в секунду ведут камеру одинаково");
            Assert.That(Mathf.Abs(Run(30, 2f).x - Run(144, 2f).x), Is.LessThan(1f));
            Vector2 settled = Run(60, 3);
            Assert.That(settled.x, Is.EqualTo(1600).Within(1e-3f));
            // Стоящий командир: кадр больше не двигается (без дрожания).
            LocationCameraFollow still = new LocationCameraFollow();
            still.Step(new Vector2(1600, 1000), view, Map, settings, 0, true);
            for (int i = 0; i < 120; i++) still.Step(new Vector2(1600, 1000), view, Map, settings, 1 / 60f);
            Assert.That(still.Center, Is.EqualTo(new Vector2(1600, 1000)));
        }

        [Test]
        public void CameraSnapsOnTeleportAndHoldsInDeadZone()
        {
            LocationCameraSettings settings = new LocationCameraSettings { Version = 1, SmoothTime = .3f, DeadZone = new Vector2(.2f, .2f), SnapDistance = 1 };
            Vector2 view = new Vector2(1920, 1080);
            LocationCameraFollow follow = new LocationCameraFollow();
            Assert.That(follow.Step(new Vector2(1500, 1000), view, Map, settings, 1 / 60f), Is.EqualTo(new Vector2(1500, 1000)), "первое появление — сразу");
            Assert.That(follow.Step(new Vector2(1600, 1050), view, Map, settings, 1 / 60f), Is.EqualTo(new Vector2(1500, 1000)), "в мёртвой зоне кадр стоит");
            Assert.That(follow.Step(new Vector2(3000, 1000), view, Map, settings, 1 / 60f).x, Is.EqualTo(3000).Within(1e-3f),
                "телепорт дальше высоты кадра — сразу, без пролёта");
            // Прежние данные (Version 0, сглаживание 0) — кадр сразу на командире.
            LocationCameraFollow legacy = new LocationCameraFollow();
            legacy.Step(new Vector2(1500, 1000), view, Map, new LocationCameraSettings(), 1 / 60f);
            Assert.That(legacy.Step(new Vector2(1700, 1100), view, Map, new LocationCameraSettings(), 1 / 60f), Is.EqualTo(new Vector2(1700, 1100)));
        }
    }
}

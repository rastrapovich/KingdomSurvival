using System;
using System.IO;
using System.IO.Compression;

namespace KingdomSurvival.BattlefieldDatabase
{
    // ПР-12О: ограниченный проверенный PNG-кодек для данных земли из Blender
    // (KS Ground Renderer 1.0.0 пишет RGBA 8/16 бит). Texture2D.LoadImage и
    // Color32 теряют младшие биты 16-битной высоты, поэтому значения читаются
    // здесь без потерь: все пять фильтров строк, порядок байтов big endian,
    // CRC каждого блока и Adler-32 сжатых данных. Поддержаны серый, RGB,
    // серый+альфа и RGBA глубиной 8 и 16 бит без чересстрочности; палитра,
    // 1/2/4 бита и Adam7 — явная ошибка, а не тихое искажение.
    // Строки отдаются в порядке файла: сверху вниз.
    public static class PngCodec
    {
        public const int Gray = 0, Rgb = 2, GrayAlpha = 4, Rgba = 6;
        private static readonly byte[] Signature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

        public struct Header
        {
            public int Width;
            public int Height;
            public int BitDepth;
            public int ColorType;
            public int Interlace;
            public bool HasSrgbChunk;

            public int Channels => ChannelsOf(ColorType);
            public bool HasAlpha => ColorType == GrayAlpha || ColorType == Rgba;
            public string Describe() => Width + "×" + Height + ", " + ColorName(ColorType) + " " + BitDepth + " бит";
        }

        public static int ChannelsOf(int colorType)
        {
            switch (colorType)
            {
                case Gray: return 1;
                case Rgb: return 3;
                case GrayAlpha: return 2;
                case Rgba: return 4;
                default: return 0;
            }
        }

        public static string ColorName(int colorType)
        {
            switch (colorType)
            {
                case Gray: return "серый";
                case Rgb: return "RGB";
                case GrayAlpha: return "серый+альфа";
                case Rgba: return "RGBA";
                case 3: return "палитра";
                default: return "тип " + colorType;
            }
        }

        // Только заголовок (IHDR) — для проверки размера и разрядности без декодирования.
        public static bool TryReadHeader(byte[] data, out Header header, out string error)
        {
            header = default;
            error = null;
            if (data == null || data.Length < 33)
            {
                error = "Файл слишком короткий для PNG.";
                return false;
            }
            for (int i = 0; i < Signature.Length; i++)
            {
                if (data[i] != Signature[i])
                {
                    error = "Это не PNG (неверная подпись файла).";
                    return false;
                }
            }
            if (ReadUInt32(data, 8) != 13 || data[12] != 'I' || data[13] != 'H' || data[14] != 'D' || data[15] != 'R')
            {
                error = "Повреждённый PNG: нет заголовка IHDR.";
                return false;
            }
            long width = ReadUInt32(data, 16), height = ReadUInt32(data, 20);
            header = new Header
            {
                Width = (int)Math.Min(width, int.MaxValue),
                Height = (int)Math.Min(height, int.MaxValue),
                BitDepth = data[24],
                ColorType = data[25],
                Interlace = data[28]
            };
            if (width <= 0 || height <= 0 || width > 65535 || height > 65535)
            {
                error = "Недопустимый размер PNG: " + width + "×" + height + ".";
                return false;
            }
            if (data[26] != 0 || data[27] != 0)
            {
                error = "PNG с неизвестным методом сжатия или фильтрации.";
                return false;
            }
            return true;
        }

        // Проверка, что декодер справится (до чтения всего файла).
        public static bool IsSupported(Header header, out string error)
        {
            error = null;
            if (ChannelsOf(header.ColorType) == 0)
                error = "Неподдержанный PNG: " + ColorName(header.ColorType) + ". Нужен серый, RGB, серый+альфа или RGBA.";
            else if (header.BitDepth != 8 && header.BitDepth != 16)
                error = "Неподдержанная разрядность PNG: " + header.BitDepth + " бит. Нужны 8 или 16 бит на канал.";
            else if (header.Interlace != 0)
                error = "Чересстрочный PNG (Adam7) не поддержан. Сохраните без чересстрочности.";
            return error == null;
        }

        // Построчное декодирование: row — значения каналов строки y (0 — верх
        // файла), 16 бит без потерь; массив строки переиспользуется.
        public static Header DecodeRows(byte[] data, Action<int, ushort[]> row)
        {
            if (!TryReadHeader(data, out Header header, out string error) || !IsSupported(header, out error))
                throw new InvalidDataException(error);
            int bytesPerSample = header.BitDepth / 8;
            int bpp = header.Channels * bytesPerSample;
            long strideLong = (long)header.Width * bpp;
            if (strideLong > int.MaxValue / 2)
                throw new InvalidDataException("PNG слишком широкий.");
            int stride = (int)strideLong;

            byte[] compressed = CollectImageData(data, ref header);
            if (compressed.Length < 6)
                throw new InvalidDataException("Повреждённый PNG: нет сжатых данных.");
            int cmf = compressed[0], flg = compressed[1];
            if ((cmf & 0x0F) != 8 || ((cmf << 8) | flg) % 31 != 0 || (flg & 0x20) != 0)
                throw new InvalidDataException("Повреждённый PNG: неверный заголовок zlib.");
            uint expectedAdler = ReadUInt32(compressed, compressed.Length - 4);

            byte[] current = new byte[stride], previous = new byte[stride];
            ushort[] samples = new ushort[(long)header.Width * header.Channels];
            uint adlerA = 1, adlerB = 0;
            using (MemoryStream source = new MemoryStream(compressed, 2, compressed.Length - 6))
            using (DeflateStream inflate = new DeflateStream(source, CompressionMode.Decompress))
            {
                byte[] filter = new byte[1];
                for (int y = 0; y < header.Height; y++)
                {
                    if (!ReadExactly(inflate, filter, 1) || !ReadExactly(inflate, current, stride))
                        throw new InvalidDataException("Повреждённый PNG: данных меньше, чем строк (" + y + " из " + header.Height + ").");
                    Adler(filter, 1, ref adlerA, ref adlerB);
                    Adler(current, stride, ref adlerA, ref adlerB);
                    Unfilter(filter[0], current, previous, bpp);
                    if (bytesPerSample == 2)
                    {
                        for (int i = 0, s = 0; i < stride; i += 2, s++)
                            samples[s] = (ushort)((current[i] << 8) | current[i + 1]);
                    }
                    else
                    {
                        for (int i = 0; i < stride; i++)
                            samples[i] = current[i];
                    }
                    row(y, samples);
                    byte[] swap = previous; previous = current; current = swap;
                }
                if (inflate.ReadByte() >= 0)
                    throw new InvalidDataException("Повреждённый PNG: лишние данные после последней строки.");
            }
            if (((adlerB << 16) | adlerA) != expectedAdler)
                throw new InvalidDataException("Повреждённый PNG: не совпала контрольная сумма Adler-32.");
            return header;
        }

        // Всё изображение: значения каналов, строки сверху вниз.
        public static ushort[] Decode(byte[] data, out Header header)
        {
            ushort[] result = null;
            int rowLength = 0;
            header = DecodeRows(data, (y, row) =>
            {
                if (result == null)
                {
                    rowLength = row.Length;
                    result = new ushort[(long)rowLength * HeaderHeight(data)];
                }
                Array.Copy(row, 0, result, (long)y * rowLength, rowLength);
            });
            return result;
        }

        private static int HeaderHeight(byte[] data) => (int)ReadUInt32(data, 20);

        private static byte[] CollectImageData(byte[] data, ref Header header)
        {
            using (MemoryStream idat = new MemoryStream())
            {
                int offset = 8;
                bool end = false;
                while (offset + 12 <= data.Length)
                {
                    uint length = ReadUInt32(data, offset);
                    if (length > data.Length - offset - 12)
                        throw new InvalidDataException("Повреждённый PNG: блок выходит за конец файла.");
                    int type = offset + 4, body = offset + 8, size = (int)length;
                    if (Crc32(data, type, size + 4) != ReadUInt32(data, body + size))
                        throw new InvalidDataException("Повреждённый PNG: не совпала контрольная сумма блока " + ChunkName(data, type) + ".");
                    string name = ChunkName(data, type);
                    if (name == "IDAT") idat.Write(data, body, size);
                    else if (name == "sRGB") header.HasSrgbChunk = true;
                    else if (name == "IEND") { end = true; break; }
                    offset = body + size + 4;
                }
                if (!end)
                    throw new InvalidDataException("Повреждённый PNG: нет блока IEND (файл обрезан?).");
                return idat.ToArray();
            }
        }

        private static string ChunkName(byte[] data, int offset) =>
            new string(new[] { (char)data[offset], (char)data[offset + 1], (char)data[offset + 2], (char)data[offset + 3] });

        private static void Unfilter(int filter, byte[] row, byte[] previous, int bpp)
        {
            int length = row.Length;
            switch (filter)
            {
                case 0:
                    return;
                case 1:
                    for (int i = bpp; i < length; i++) row[i] = (byte)(row[i] + row[i - bpp]);
                    return;
                case 2:
                    for (int i = 0; i < length; i++) row[i] = (byte)(row[i] + previous[i]);
                    return;
                case 3:
                    for (int i = 0; i < length; i++)
                    {
                        int left = i >= bpp ? row[i - bpp] : 0;
                        row[i] = (byte)(row[i] + ((left + previous[i]) >> 1));
                    }
                    return;
                case 4:
                    for (int i = 0; i < length; i++)
                    {
                        int a = i >= bpp ? row[i - bpp] : 0, b = previous[i], c = i >= bpp ? previous[i - bpp] : 0;
                        row[i] = (byte)(row[i] + Paeth(a, b, c));
                    }
                    return;
                default:
                    throw new InvalidDataException("Повреждённый PNG: неизвестный фильтр строки " + filter + ".");
            }
        }

        private static int Paeth(int a, int b, int c)
        {
            int p = a + b - c;
            int pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
            return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
        }

        private static bool ReadExactly(Stream stream, byte[] buffer, int count)
        {
            int read = 0;
            while (read < count)
            {
                int n = stream.Read(buffer, read, count - read);
                if (n <= 0) return false;
                read += n;
            }
            return true;
        }

        // ------------------------------------------------------------------
        // Запись: тесты, синтетические экспорты и выпрямленные нормали.
        // samples — значения каналов, строки сверху вниз; filters — фильтр
        // каждой строки (null — без фильтра), чтобы проверять все пять.
        // ------------------------------------------------------------------

        public static byte[] Encode(int width, int height, int bitDepth, int colorType, ushort[] samples, int[] filters = null, bool srgb = false)
        {
            int channels = ChannelsOf(colorType);
            if (width <= 0 || height <= 0 || channels == 0 || (bitDepth != 8 && bitDepth != 16))
                throw new ArgumentException("Неверный формат PNG.");
            if (samples == null || samples.Length != (long)width * height * channels)
                throw new ArgumentException("Число значений не совпадает с размером PNG.");
            int bytesPerSample = bitDepth / 8, bpp = channels * bytesPerSample, stride = width * bpp;
            byte[] raw = new byte[(long)height * (stride + 1)];
            byte[] previous = new byte[stride], current = new byte[stride];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0, s = y * width * channels; x < stride; x += bytesPerSample, s++)
                {
                    if (bytesPerSample == 2) { current[x] = (byte)(samples[s] >> 8); current[x + 1] = (byte)samples[s]; }
                    else current[x] = (byte)samples[s];
                }
                int filter = filters != null && filters.Length > 0 ? filters[y % filters.Length] : 0;
                int offset = y * (stride + 1);
                raw[offset] = (byte)filter;
                for (int i = 0; i < stride; i++)
                {
                    int a = i >= bpp ? current[i - bpp] : 0, b = previous[i], c = i >= bpp ? previous[i - bpp] : 0;
                    int predictor = filter == 1 ? a : filter == 2 ? b : filter == 3 ? (a + b) >> 1 : filter == 4 ? Paeth(a, b, c) : 0;
                    raw[offset + 1 + i] = (byte)(current[i] - predictor);
                }
                byte[] swap = previous; previous = current; current = swap;
            }

            using (MemoryStream file = new MemoryStream())
            {
                file.Write(Signature, 0, Signature.Length);
                byte[] ihdr = new byte[13];
                WriteUInt32(ihdr, 0, (uint)width);
                WriteUInt32(ihdr, 4, (uint)height);
                ihdr[8] = (byte)bitDepth; ihdr[9] = (byte)colorType;
                WriteChunk(file, "IHDR", ihdr);
                if (srgb) WriteChunk(file, "sRGB", new byte[] { 0 });
                WriteChunk(file, "IDAT", Zlib(raw));
                WriteChunk(file, "IEND", Array.Empty<byte>());
                return file.ToArray();
            }
        }

        private static byte[] Zlib(byte[] raw)
        {
            using (MemoryStream output = new MemoryStream())
            {
                output.WriteByte(0x78); output.WriteByte(0x9C);
                using (DeflateStream deflate = new DeflateStream(output, CompressionLevel.Optimal, true))
                    deflate.Write(raw, 0, raw.Length);
                uint a = 1, b = 0;
                Adler(raw, raw.Length, ref a, ref b);
                byte[] adler = new byte[4];
                WriteUInt32(adler, 0, (b << 16) | a);
                output.Write(adler, 0, 4);
                return output.ToArray();
            }
        }

        private static void WriteChunk(Stream stream, string type, byte[] body)
        {
            byte[] chunk = new byte[body.Length + 12];
            WriteUInt32(chunk, 0, (uint)body.Length);
            for (int i = 0; i < 4; i++) chunk[4 + i] = (byte)type[i];
            Buffer.BlockCopy(body, 0, chunk, 8, body.Length);
            WriteUInt32(chunk, 8 + body.Length, Crc32(chunk, 4, body.Length + 4));
            stream.Write(chunk, 0, chunk.Length);
        }

        // ------------------------------------------------------------------
        // Построчная запись RGBA 8 бит: большие карты режутся и сшиваются без
        // массива всего изображения в памяти (держится только сжатый результат).
        // ------------------------------------------------------------------

        public sealed class RowWriter
        {
            private readonly MemoryStream compressed = new MemoryStream();
            private readonly DeflateStream deflate;
            private readonly int width, height, stride;
            private readonly bool srgb;
            private byte[] previous, filtered;
            private readonly byte[] filter = { 1 };
            private uint adlerA = 1, adlerB = 0;
            private int rows;

            public RowWriter(int width, int height, bool srgb = true)
            {
                if (width <= 0 || height <= 0) throw new ArgumentException("Неверный размер PNG.");
                this.width = width; this.height = height; this.srgb = srgb;
                stride = width * 4;
                previous = new byte[stride];
                filtered = new byte[stride];
                compressed.WriteByte(0x78); compressed.WriteByte(0x9C);
                deflate = new DeflateStream(compressed, CompressionLevel.Fastest, true);
            }

            // Строка RGBA 8 бит (сверху вниз), фильтр Sub.
            public void WriteRow(byte[] row, int offset)
            {
                if (rows >= height) throw new InvalidOperationException("Лишняя строка PNG.");
                for (int i = 0; i < stride; i++)
                    filtered[i] = (byte)(row[offset + i] - (i >= 4 ? row[offset + i - 4] : 0));
                deflate.Write(filter, 0, 1);
                deflate.Write(filtered, 0, stride);
                Adler(filter, 1, ref adlerA, ref adlerB);
                Adler(filtered, stride, ref adlerA, ref adlerB);
                rows++;
            }

            public byte[] Finish()
            {
                if (rows != height) throw new InvalidOperationException("PNG: записано строк " + rows + " из " + height + ".");
                deflate.Dispose();
                byte[] adler = new byte[4];
                WriteUInt32(adler, 0, (adlerB << 16) | adlerA);
                compressed.Write(adler, 0, 4);
                using (MemoryStream file = new MemoryStream())
                {
                    file.Write(Signature, 0, Signature.Length);
                    byte[] ihdr = new byte[13];
                    WriteUInt32(ihdr, 0, (uint)width);
                    WriteUInt32(ihdr, 4, (uint)height);
                    ihdr[8] = 8; ihdr[9] = Rgba;
                    WriteChunk(file, "IHDR", ihdr);
                    if (srgb) WriteChunk(file, "sRGB", new byte[] { 0 });
                    WriteChunk(file, "IDAT", compressed.ToArray());
                    WriteChunk(file, "IEND", Array.Empty<byte>());
                    return file.ToArray();
                }
            }
        }

        // Значения строки декодера (любой поддержанный тип и разрядность) →
        // RGBA 8 бит; без альфы — непрозрачно.
        public static void ToRgba8(ushort[] samples, Header header, byte[] target, int offset = 0)
        {
            int channels = header.Channels;
            bool wide = header.BitDepth == 16;
            for (int x = 0; x < header.Width; x++)
            {
                int s = x * channels, t = offset + x * 4;
                byte Read(int c) => wide ? (byte)((samples[s + c] * 255 + 32767) / 65535) : (byte)samples[s + c];
                if (channels >= 3) { target[t] = Read(0); target[t + 1] = Read(1); target[t + 2] = Read(2); }
                else target[t] = target[t + 1] = target[t + 2] = Read(0);
                target[t + 3] = header.HasAlpha ? Read(channels - 1) : (byte)255;
            }
        }

        // ------------------------------------------------------------------

        private static uint ReadUInt32(byte[] data, int offset) =>
            (uint)(data[offset] << 24 | data[offset + 1] << 16 | data[offset + 2] << 8 | data[offset + 3]);

        private static void WriteUInt32(byte[] data, int offset, uint value)
        {
            data[offset] = (byte)(value >> 24); data[offset + 1] = (byte)(value >> 16);
            data[offset + 2] = (byte)(value >> 8); data[offset + 3] = (byte)value;
        }

        private static void Adler(byte[] data, int count, ref uint a, ref uint b)
        {
            const uint Mod = 65521;
            int index = 0;
            while (count > 0)
            {
                int block = Math.Min(count, 3800);
                count -= block;
                for (int i = 0; i < block; i++) { a += data[index++]; b += a; }
                a %= Mod; b %= Mod;
            }
        }

        private static uint[] crcTable;

        public static uint Crc32(byte[] data, int offset, int count)
        {
            if (crcTable == null)
            {
                uint[] table = new uint[256];
                for (uint n = 0; n < 256; n++)
                {
                    uint c = n;
                    for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                    table[n] = c;
                }
                crcTable = table;
            }
            uint crc = 0xFFFFFFFFu;
            for (int i = offset, end = offset + count; i < end; i++)
                crc = crcTable[(crc ^ data[i]) & 0xFF] ^ (crc >> 8);
            return crc ^ 0xFFFFFFFFu;
        }
    }
}

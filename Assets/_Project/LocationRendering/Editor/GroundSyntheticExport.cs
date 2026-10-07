using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using KingdomSurvival.BattlefieldDatabase;
using UnityEngine;

namespace KingdomSurvival.LocationRendering.Editor
{
    // ПР-12О: технический экспорт земли в точном формате KS Ground Renderer
    // 1.0.0 (manifest schema 1, Color/Normal/Height, пути Color/…, Normal/…,
    // Height/…). Для тестов импорта и демонстрационного места: четыре угла
    // карты подписаны цветом (верх-лево красный, верх-право зелёный, низ-лево
    // синий, низ-право жёлтый), полосы и холмы непрерывны через швы. Это
    // заглушка для проверки техники, не арт и не факт мира.
    public sealed class GroundSyntheticExport
    {
        public string MapId = "KS_Test_Ground";
        public int Columns = 2, Rows = 2, Width = 64, Height = 48;
        public double HeightMin = -1, HeightMax = 3;
        public int HeightBits = 16;
        public double Q = .125;
        public double[] Origin = { 0, 0 };
        public bool Normals = true, Heights = true;
        public bool InvertGreen;
        public string Status = "complete";
        public string RevisionId = "00000000000000000000000000000001";
        public double UnityPpu = 108;
        // Высота, BU, в точке виртуальной карты (пиксели снизу слева).
        public Func<double, double, double> Surface;

        public GroundSyntheticExport()
        {
            Surface = (x, y) =>
            {
                double w = Columns * Width, h = Rows * Height;
                double hills = Math.Sin(x / w * Math.PI * 3) * Math.Cos(y / h * Math.PI * 2);
                return 1 + 1.5 * hills;
            };
        }

        public int VirtualWidth => Columns * Width;
        public int VirtualHeight => Rows * Height;

        public ushort RawHeight(double x, double y)
        {
            double t = (Surface(x, y) - HeightMin) / (HeightMax - HeightMin);
            double max = HeightBits == 16 ? 65535 : 255;
            return (ushort)Math.Round(Math.Max(0, Math.Min(1, t)) * max);
        }

        public static string TilePath(string kind, string mapId, int x, int y) =>
            kind + "/" + mapId + "_X" + x.ToString("000") + "_Y" + y.ToString("000") + (kind == "Color" ? "" : "_" + kind.ToLowerInvariant()) + ".png";

        // Пишет пакет в папку; возвращает путь manifest.
        public string Write(string folder)
        {
            Directory.CreateDirectory(folder);
            List<string> tiles = new List<string>();
            for (int ty = 0; ty < Rows; ty++)
            {
                for (int tx = 0; tx < Columns; tx++)
                {
                    StringBuilder passes = new StringBuilder();
                    passes.Append(Pass("Color", folder, tx, ty, ColorPng(tx, ty), 8)).Append(",\n");
                    passes.Append(Normals ? Pass("Normal", folder, tx, ty, NormalPng(tx, ty), 8) : "\"Normal\": {\"status\": \"not_requested\"}").Append(",\n");
                    passes.Append(Heights ? Pass("Height", folder, tx, ty, HeightPng(tx, ty), HeightBits) : "\"Height\": {\"status\": \"not_requested\"}");
                    tiles.Add("{\"x\": " + tx + ", \"y\": " + ty + ", \"center\": [" + N(Origin[0] + (tx + .5) * Width * Q) + ", " + N(Origin[1] + (ty + .5) * Height * Q) +
                              "], \"pixel_rect\": [" + tx * Width + ", " + (Rows - 1 - ty) * Height + ", " + Width + ", " + Height + "], \"status\": \"complete\", \"passes\": {\n" +
                              passes + "}}");
                }
            }
            List<string> passList = new List<string> { "\"Color\"" };
            if (Normals) passList.Add("\"Normal\"");
            if (Heights) passList.Add("\"Height\"");
            double[] bounds = { Origin[0], Origin[1], Origin[0] + VirtualWidth * Q, Origin[1] + VirtualHeight * Q };
            string manifest = "{\n\"schema_version\": 1,\n\"exporter_version\": \"1.0.0\",\n\"blender_version\": \"технический\",\n" +
                              "\"map_id\": \"" + MapId + "\",\n\"revision_id\": \"" + RevisionId + "\",\n\"created_utc\": \"2026-10-07T00:00:00+00:00\",\n" +
                              "\"config\": {\"single\": " + (Columns * Rows == 1 ? "true" : "false") +
                              ", \"grid\": {\"nx\": " + Columns + ", \"ny\": " + Rows + ", \"origin\": [" + N(Origin[0]) + ", " + N(Origin[1]) + "], \"requested_bounds\": [" +
                              string.Join(", ", Array.ConvertAll(bounds, N)) + "], \"padded_bounds\": [" + string.Join(", ", Array.ConvertAll(bounds, N)) + "]}" +
                              ", \"size\": [" + Width + ", " + Height + "], \"overscan\": 4, \"keep_overscan\": false, \"passes\": [" + string.Join(", ", passList) + "]" +
                              ", \"engine\": \"CYCLES\", \"projection\": {\"type\": \"ORTHO\", \"q\": " + N(Q) + ", \"qx\": " + N(Q) + ", \"qy\": " + N(Q) +
                              ", \"reference\": [0.0, -10.0, 0.0], \"basis\": [[1.0, 0.0, 0.0], [0.0, 0.5, 0.8660254037844386], [0.0, -0.8660254037844386, 0.5]]}" +
                              ", \"units\": {\"meters_per_blender_unit\": 1.0, \"pixels_per_projection_unit\": " + N(1 / Q) + ", \"unity_ppu\": " + N(UnityPpu) +
                              ", \"unity_units_per_projection_unit\": " + N(1 / (Q * UnityPpu)) + ", \"downscale\": 1.0}" +
                              ", \"height\": {\"min\": " + N(HeightMin) + ", \"max\": " + N(HeightMax) + ", \"axis\": [0, 0, 1], \"reference_origin\": [0, 0, 0], \"source\": \"world_position_z\"" +
                              ", \"render_engine\": \"CYCLES\", \"units\": \"Blender Unit\", \"bit_depth\": " + HeightBits + ", \"channels\": \"RGBA\", \"alpha\": \"coverage; alpha=0 invalid\"}" +
                              ", \"normal\": {\"source\": \"smoothed_geometry\", \"space\": \"sprite_projection\", \"encoding\": \"rgb = normalized(camera_basis_normal)*0.5+0.5\", \"invert_green\": " +
                              (InvertGreen ? "true" : "false") + ", \"strength\": 1.0, \"bit_depth\": 8, \"unity_verified\": false}" +
                              ", \"color\": {\"profile\": \"NEUTRAL\", \"bit_depth\": 8, \"alpha\": \"straight\", \"sources\": [\"GROUND\"]}" +
                              ", \"warnings\": [], \"sources\": {\"Color\": [\"GROUND\"], \"Normal\": [\"GROUND\"], \"Height\": [\"GROUND\"]}, \"surface_contract\": \"matched_ground\"},\n" +
                              "\"configuration_hash\": \"technical\",\n\"source\": {\"blend_path\": \"\", \"fingerprint\": \"technical\"},\n" +
                              "\"status\": \"" + Status + "\",\n\"warnings\": [],\n\"errors\": [],\n\"tiles\": [\n" + string.Join(",\n", tiles) + "\n],\n" +
                              "\"image_rows\": \"top_to_bottom\",\n\"tile_indices\": \"bottom_left, X right, Y up\",\n\"virtual_size\": [" + VirtualWidth + ", " + VirtualHeight + "],\n" +
                              "\"internal_size\": [" + (Width + 8) + ", " + (Height + 8) + "],\n\"useful_rect_projection\": [" + string.Join(", ", Array.ConvertAll(bounds, N)) + "]\n}\n";
            string path = Path.Combine(folder, MapId + "_manifest.json");
            File.WriteAllText(path, manifest, new UTF8Encoding(false));
            return path;
        }

        private string Pass(string kind, string folder, int x, int y, byte[] png, int bits)
        {
            string relative = TilePath(kind, MapId, x, y);
            string full = Path.Combine(folder, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(full));
            File.WriteAllBytes(full, png);
            return "\"" + kind + "\": {\"status\": \"complete\", \"path\": \"" + relative + "\", \"revision_id\": \"" + RevisionId + "\", \"sha256\": \"" +
                   GroundExportPackage.Sha256Hex(png) + "\", \"size\": [" + Width + ", " + Height + "], \"bit_depth\": " + bits + "}";
        }

        private static string N(double value) => value.ToString("R", CultureInfo.InvariantCulture);

        // Пиксель участка (строки сверху вниз) → точка виртуальной карты (снизу слева, центр пикселя).
        private void Virtual(int tx, int ty, int px, int py, out double x, out double y)
        {
            x = tx * Width + px + .5;
            y = ty * Height + (Height - 1 - py) + .5;
        }

        public byte[] ColorPng(int tx, int ty)
        {
            ushort[] samples = new ushort[Width * Height * 4];
            double w = VirtualWidth, h = VirtualHeight, corner = Math.Max(4, Math.Min(w, h) * .08);
            for (int py = 0; py < Height; py++)
            {
                for (int px = 0; px < Width; px++)
                {
                    Virtual(tx, ty, px, py, out double x, out double y);
                    double shade = (Surface(x, y) - HeightMin) / (HeightMax - HeightMin);
                    bool stripe = ((int)Math.Floor((x + y) / 24)) % 2 == 0;
                    Color color = Color.Lerp(new Color(.30f, .34f, .22f), new Color(.62f, .58f, .42f), (float)shade);
                    if (stripe) color *= .9f;
                    // Подписанные углы всей карты.
                    if (x < corner && y > h - corner) color = Color.red;
                    else if (x > w - corner && y > h - corner) color = Color.green;
                    else if (x < corner && y < corner) color = Color.blue;
                    else if (x > w - corner && y < corner) color = Color.yellow;
                    int i = (py * Width + px) * 4;
                    samples[i] = (ushort)Mathf.RoundToInt(Mathf.Clamp01(color.r) * 255);
                    samples[i + 1] = (ushort)Mathf.RoundToInt(Mathf.Clamp01(color.g) * 255);
                    samples[i + 2] = (ushort)Mathf.RoundToInt(Mathf.Clamp01(color.b) * 255);
                    samples[i + 3] = 255;
                }
            }
            return PngCodec.Encode(Width, Height, 8, PngCodec.Rgba, samples, null, true);
        }

        // Нормаль рельефа в базисе картинки (R вправо, G вверх, B к зрителю).
        public byte[] NormalPng(int tx, int ty)
        {
            ushort[] samples = new ushort[Width * Height * 4];
            for (int py = 0; py < Height; py++)
            {
                for (int px = 0; px < Width; px++)
                {
                    Virtual(tx, ty, px, py, out double x, out double y);
                    double dx = (Surface(x + 1, y) - Surface(x - 1, y)) / (2 * Q), dy = (Surface(x, y + 1) - Surface(x, y - 1)) / (2 * Q);
                    Vector3 n = new Vector3((float)-dx, (float)-dy, 1).normalized;
                    if (InvertGreen) n.y = -n.y;
                    int i = (py * Width + px) * 4;
                    samples[i] = (ushort)Mathf.RoundToInt((n.x * .5f + .5f) * 255);
                    samples[i + 1] = (ushort)Mathf.RoundToInt((n.y * .5f + .5f) * 255);
                    samples[i + 2] = (ushort)Mathf.RoundToInt((n.z * .5f + .5f) * 255);
                    samples[i + 3] = 255;
                }
            }
            return PngCodec.Encode(Width, Height, 8, PngCodec.Rgba, samples);
        }

        public byte[] HeightPng(int tx, int ty)
        {
            ushort[] samples = new ushort[Width * Height * 4];
            int max = HeightBits == 16 ? 65535 : 255;
            for (int py = 0; py < Height; py++)
            {
                for (int px = 0; px < Width; px++)
                {
                    Virtual(tx, ty, px, py, out double x, out double y);
                    int i = (py * Width + px) * 4;
                    samples[i] = samples[i + 1] = samples[i + 2] = RawHeight(x, y);
                    samples[i + 3] = (ushort)max;
                }
            }
            return PngCodec.Encode(Width, Height, HeightBits, PngCodec.Rgba, samples);
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KingdomSurvival.ArtAssets.Editor;
using KingdomSurvival.BattlefieldDatabase;
using UnityEditor;
using UnityEngine;

namespace KingdomSurvival.LocationRendering.Editor
{
    // ПР-12О: обрезка земли места по рамке — убрать пустоту (прозрачные поля
    // экспорта, добивку до целых участков). Земля перенарезается на новую
    // сетку (участки не крупнее прежних, можно прямоугольные): Color (и
    // обрисовка в её разрешении), Normal, Height — без потерь. Раскладка
    // места сдвигается вместе с землёй (LocationRebase). Рамка запоминается
    // в плоскости Blender: переимпорт с тем же ракурсом обрежет так же.
    public static class GroundCrop
    {
        public sealed class Plan
        {
            public RectInt Requested;
            public RectInt Canvas;
            public int Columns, Rows, TileWidth, TileHeight;
            public int ColorScale = 1;
            public readonly List<string> Errors = new List<string>();
            public readonly List<string> Warnings = new List<string>();
            public bool CanApply => Errors.Count == 0;

            public string Summary(Vector2 canvas) =>
                Mathf.RoundToInt(canvas.x) + "×" + Mathf.RoundToInt(canvas.y) + " → " + Canvas.width + "×" + Canvas.height + " px (участки " +
                Columns + "×" + Rows + " по " + TileWidth + "×" + TileHeight + "), пустоты убрано " +
                Mathf.RoundToInt(100f * (1 - Canvas.width * (float)Canvas.height / Mathf.Max(1, canvas.x * canvas.y))) + "%";
        }

        // Рамка земли по непрозрачным пикселям Color (пиксели рисунка, Y вниз).
        public static bool TryContentBounds(LocationGroundDefinition ground, LocalLocationDefinition location, out RectInt bounds, out string problem)
        {
            bounds = default;
            problem = null;
            LocationGroundGrid grid = LocationGroundGrid.For(ground, location);
            if (!ground.IsTiled || !grid.IsOneToOne) { problem = "Сначала подгоните размер места под землю (1 пиксель земли = 1 пиксель места)."; return false; }
            int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
            foreach (LocationGroundTile tile in ground.Tiles)
            {
                if (tile?.Color == null) continue;
                string path = AssetDatabase.GetAssetPath(tile.Color.texture);
                using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20))
                {
                    if (!GroundExportPackage.ReadHeader(path, out PngCodec.Header header, out problem)) return false;
                    float k = header.Width / (float)ground.TileWidth;
                    int left = tile.X * ground.TileWidth, top = (ground.Rows - 1 - tile.Y) * ground.TileHeight;
                    int channels = header.Channels;
                    PngCodec.DecodeRows(stream, (y, row) =>
                    {
                        int first = -1, last = -1;
                        for (int x = 0; x < header.Width; x++)
                        {
                            bool opaque = !header.HasAlpha || row[x * channels + channels - 1] > 0;
                            if (!opaque) continue;
                            if (first < 0) first = x;
                            last = x;
                        }
                        if (first < 0) return;
                        int cy = top + Mathf.FloorToInt(y / k);
                        minY = Math.Min(minY, cy); maxY = Math.Max(maxY, cy);
                        minX = Math.Min(minX, left + Mathf.FloorToInt(first / k)); maxX = Math.Max(maxX, left + Mathf.FloorToInt(last / k));
                    });
                }
            }
            if (minX > maxX) { problem = "На земле нет непрозрачных пикселей Color."; return false; }
            bounds = new RectInt(minX, minY, maxX - minX + 1, maxY - minY + 1);
            return true;
        }

        public static Plan Analyze(LocationGroundDefinition ground, LocalLocationDefinition location, RectInt requested)
        {
            Plan plan = new Plan { Requested = requested };
            LocationGroundGrid grid = LocationGroundGrid.For(ground, location);
            if (ground == null || !ground.IsTiled || ground.Tiles.Count == 0) { plan.Errors.Add("Обрезать можно землю из участков."); return plan; }
            if (!grid.IsOneToOne) { plan.Errors.Add("Сначала подгоните размер места под землю (1 пиксель земли = 1 пиксель места)."); return plan; }
            int width = (int)grid.CanvasWidth, height = (int)grid.CanvasHeight;
            RectInt rect = new RectInt(Mathf.Clamp(requested.x, 0, width - 1), Mathf.Clamp(requested.y, 0, height - 1), 0, 0);
            rect.width = Mathf.Clamp(requested.xMax, rect.x + 1, width) - rect.x;
            rect.height = Mathf.Clamp(requested.yMax, rect.y + 1, height) - rect.y;
            if (rect.width < 16 || rect.height < 16) { plan.Errors.Add("Рамка слишком мала (меньше 16 пикселей)."); return plan; }
            plan.Columns = Mathf.CeilToInt(rect.width / (float)ground.TileWidth);
            plan.Rows = Mathf.CeilToInt(rect.height / (float)ground.TileHeight);
            plan.TileWidth = Mathf.CeilToInt(rect.width / (float)plan.Columns);
            plan.TileHeight = Mathf.CeilToInt(rect.height / (float)plan.Rows);
            // Ширина кратна сетке: добираем до нескольких пикселей, не выходя за карту, если можно.
            int w = plan.Columns * plan.TileWidth, h = plan.Rows * plan.TileHeight;
            int x = rect.x + w > width ? Mathf.Max(0, width - w) : rect.x;
            int y = rect.y + h > height ? Mathf.Max(0, height - h) : rect.y;
            plan.Canvas = new RectInt(x, y, w, h);
            if (ground.IsPainted)
            {
                float k = ground.ColorScale;
                if (Mathf.Abs(k - Mathf.Round(k)) > 1e-4f) plan.Errors.Add("Обрисовка в дробном масштабе (×" + k.ToString("0.###") + ") — обрежьте после обрисовки в целое число раз.");
                plan.ColorScale = Mathf.Max(1, Mathf.RoundToInt(k));
                if (ground.Tiles.Any(tile => tile.IsPainted && !string.IsNullOrEmpty(tile.RenderColorGuid)))
                    plan.Warnings.Add("Рендер под обрисовкой не обрезается: «Вернуть Color из рендера» после обрезки будет недоступен.");
            }
            if (w == width && h == height) plan.Warnings.Add("Рамка совпадает с картой — обрезать нечего.");
            return plan;
        }

        // Плоская копия участков: Color/Normal — RGBA 8 сверху вниз, Height — данные снизу вверх.
        private sealed class Source
        {
            private readonly LocationGroundDefinition ground;
            private readonly Dictionary<string, byte[]> rgba = new Dictionary<string, byte[]>();
            private readonly Dictionary<string, LocationHeightTileData> heights = new Dictionary<string, LocationHeightTileData>();
            public Source(LocationGroundDefinition ground) { this.ground = ground; }

            public byte[] Image(LocationGroundTile tile, bool color, int scale)
            {
                UnityEngine.Object asset = color ? (UnityEngine.Object)tile?.Color?.texture : tile?.Normal;
                if (asset == null) return null;
                string key = (color ? "c" : "n") + tile.Key;
                if (rgba.TryGetValue(key, out byte[] cached)) return cached;
                byte[] png = File.ReadAllBytes(AssetDatabase.GetAssetPath(asset));
                PngCodec.TryReadHeader(png, out PngCodec.Header header, out _);
                int w = ground.TileWidth * (color ? scale : 1), h = ground.TileHeight * (color ? scale : 1);
                if (header.Width != w || header.Height != h)
                    throw new InvalidDataException(tile.Key + ": " + (color ? "Color" : "Normal") + " " + header.Width + "×" + header.Height + " вместо " + w + "×" + h + ".");
                byte[] result = new byte[w * h * 4];
                PngCodec.DecodeRows(png, (y, row) => PngCodec.ToRgba8(row, header, result, y * w * 4));
                return rgba[key] = result;
            }

            public LocationHeightTileData Height(LocationGroundTile tile)
            {
                if (tile?.Height == null) return null;
                if (heights.TryGetValue(tile.Key, out LocationHeightTileData cached)) return cached;
                return heights[tile.Key] = LocationHeightTileData.Deserialize(tile.Height.bytes);
            }

            // Освободить участки выше полосы (строки сверху вниз уже пройдены).
            public void Forget(int belowRow)
            {
                foreach (string key in rgba.Keys.Where(key => int.Parse(key.Substring(key.IndexOf('Y') + 1)) > belowRow).ToList()) rgba.Remove(key);
                foreach (string key in heights.Keys.Where(key => int.Parse(key.Substring(key.IndexOf('Y') + 1)) > belowRow).ToList()) heights.Remove(key);
            }
        }

        private sealed class NewTile
        {
            public int X, Y;
            public byte[] Color, Normal, Height;
            public string ColorPath, NormalPath, HeightPath, ColorSha;
        }

        public static bool Apply(Plan plan, LocalLocationDatabaseAsset database, string locationId, out string message)
        {
            message = string.Empty;
            LocalLocationDefinition location = database.locations.Find(item => item.Id == locationId);
            LocationVisualDefinition visual = database.FindVisual(locationId);
            LocationGroundDefinition ground = visual?.Ground;
            if (!plan.CanApply || ground == null || location == null) { message = string.Join("\n", plan.Errors.DefaultIfEmpty("Нет земли из участков.")); return false; }
            int oldW = ground.Columns * ground.TileWidth, oldH = ground.Rows * ground.TileHeight;
            bool painted = ground.IsPainted, anyNormal = ground.Tiles.Any(t => t?.Normal != null), anyHeight = ground.HasAnyHeight;
            int bits = 16;
            Source source = new Source(ground);
            List<NewTile> built = new List<NewTile>();
            string folder = GroundImporter.Root + "/" + GroundImporter.Sanitize(locationId) + "/" + GroundImporter.Sanitize(ground.MapId) + "/Crop";
            try
            {
                for (int row = plan.Rows - 1; row >= 0; row--)
                {
                    int top = plan.Canvas.y + (plan.Rows - 1 - row) * plan.TileHeight;
                    for (int column = 0; column < plan.Columns; column++)
                    {
                        EditorUtility.DisplayProgressBar("Обрезка земли", "Участок " + LocationGroundTile.KeyOf(column, row),
                            built.Count / (float)(plan.Columns * plan.Rows));
                        int left = plan.Canvas.x + column * plan.TileWidth;
                        NewTile tile = new NewTile { X = column, Y = row };
                        int k = painted ? plan.ColorScale : 1;
                        tile.Color = EncodeRgba(CopyRgba(ground, source, true, k, left, top, plan.TileWidth, plan.TileHeight), plan.TileWidth * k, plan.TileHeight * k, true);
                        if (anyNormal) tile.Normal = EncodeRgba(CopyRgba(ground, source, false, 1, left, top, plan.TileWidth, plan.TileHeight), plan.TileWidth, plan.TileHeight, false);
                        if (anyHeight)
                        {
                            LocationHeightTileData data = CopyHeight(ground, source, left, top, plan.TileWidth, plan.TileHeight, oldH, ref bits);
                            tile.Height = data.Serialize();
                        }
                        tile.ColorSha = GroundExportPackage.Sha256Hex(tile.Color);
                        string name = GroundImporter.Sanitize(ground.MapId) + "_crop_" + LocationGroundTile.KeyOf(column, row);
                        tile.ColorPath = folder + "/" + name + (painted ? "_paint" : "") + "__" + tile.ColorSha.Substring(0, 10) + ".png";
                        if (tile.Normal != null) tile.NormalPath = folder + "/" + name + "_normal__" + GroundExportPackage.Sha256Hex(tile.Normal).Substring(0, 10) + ".png";
                        if (tile.Height != null) tile.HeightPath = folder + "/" + name + "_height__" + GroundExportPackage.Sha256Hex(tile.Height).Substring(0, 10) + ".bytes";
                        built.Add(tile);
                    }
                    source.Forget(OldRowBelow(ground, top + plan.TileHeight, oldH));
                }
            }
            catch (Exception error) when (error is IOException || error is InvalidDataException || error is UnauthorizedAccessException)
            {
                EditorUtility.ClearProgressBar();
                message = "Обрезка не выполнена: " + error.Message + "\nЗемля не изменена.";
                return false;
            }

            List<string> created = new List<string>();
            try
            {
                Directory.CreateDirectory(folder);
                foreach (NewTile tile in built)
                {
                    Write(tile.ColorPath, tile.Color, created);
                    Write(tile.NormalPath, tile.Normal, created);
                    Write(tile.HeightPath, tile.Height, created);
                }
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                int done = 0;
                foreach (NewTile tile in built)
                {
                    EditorUtility.DisplayProgressBar("Обрезка земли", "Настройки импорта " + LocationGroundTile.KeyOf(tile.X, tile.Y), done++ / (float)built.Count);
                    if (tile.NormalPath != null) GroundImporter.ConfigureNormal(tile.NormalPath, Math.Max(plan.TileWidth, plan.TileHeight));
                    GroundImporter.ConfigureColor(tile.ColorPath, tile.NormalPath, Math.Max(plan.TileWidth, plan.TileHeight) * (painted ? plan.ColorScale : 1));
                    if (AssetDatabase.LoadAssetAtPath<Sprite>(tile.ColorPath) == null) throw new IOException("Unity не импортировал " + tile.ColorPath + ".");
                }

                Undo.RecordObject(database, "Обрезка земли");
                double q = ground.ProjectionQ;
                int bottom = oldH - plan.Canvas.yMax;
                ground.Origin += new Vector2((float)(plan.Canvas.x * q), (float)(bottom * q));
                Rect useful = ground.UsefulPixels;
                if (useful.width > 0)
                {
                    useful.position -= new Vector2(plan.Canvas.x, bottom);
                    useful = Rect.MinMaxRect(Mathf.Max(0, useful.xMin), Mathf.Max(0, useful.yMin), Mathf.Min(plan.Canvas.width, useful.xMax), Mathf.Min(plan.Canvas.height, useful.yMax));
                    ground.UsefulPixels = useful.width > 0 && useful.height > 0 ? useful : new Rect(0, 0, plan.Canvas.width, plan.Canvas.height);
                }
                ground.Columns = plan.Columns; ground.Rows = plan.Rows;
                ground.TileWidth = plan.TileWidth; ground.TileHeight = plan.TileHeight;
                ground.HeightBitDepth = bits;
                ground.Tiles = built.Select(tile => new LocationGroundTile
                {
                    X = tile.X, Y = tile.Y,
                    Color = AssetDatabase.LoadAssetAtPath<Sprite>(tile.ColorPath),
                    Normal = tile.NormalPath != null ? AssetDatabase.LoadAssetAtPath<Texture2D>(tile.NormalPath) : null,
                    Height = tile.HeightPath != null ? AssetDatabase.LoadAssetAtPath<TextAsset>(tile.HeightPath) : null,
                    ColorSha256 = tile.ColorSha, PaintSha256 = painted ? tile.ColorSha : string.Empty,
                    ColorRevision = "crop", NormalRevision = "crop", HeightRevision = "crop"
                }).OrderBy(tile => tile.Y).ThenBy(tile => tile.X).ToList();
                ground.Cropped = true;
                ground.CropPlane = new Rect(ground.Origin.x, ground.Origin.y, (float)(plan.Canvas.width * q), (float)(plan.Canvas.height * q));
                ground.Incomplete = GroundImporter.IsIncomplete(ground);
                LocationRebase.Result moved = LocationRebase.Translate(location, visual, new Vector2(plan.Canvas.x, plan.Canvas.y), new Vector2(plan.Canvas.width, plan.Canvas.height));
                EditorUtility.SetDirty(database);
                if (AssetDatabase.Contains(database)) AssetDatabase.SaveAssetIfDirty(database);
                int removed = GroundImporter.CleanupUnreferenced(locationId, ground);
                message = "Земля обрезана: " + plan.Canvas.width + "×" + plan.Canvas.height + " px, участков " + built.Count +
                          (removed > 0 ? ", удалено прежних файлов " + removed : "") + "." +
                          (moved.Outside > 0 ? " За рамкой оказались: " + string.Join(", ", moved.Names.Take(6)) + (moved.Outside > 6 ? "…" : "") + " — передвиньте их." : "");
                return true;
            }
            catch (Exception error)
            {
                foreach (string path in created) AssetDatabase.DeleteAsset(path);
                message = "Обрезка не применена: " + error.Message + "\nНовые файлы удалены, земля не изменена.";
                Debug.LogException(error);
                return false;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        // Старый ряд участков (снизу слева) под строкой рисунка места.
        private static int OldRowBelow(LocationGroundDefinition ground, int canvasY, int oldH) =>
            Mathf.Clamp((oldH - 1 - canvasY) / ground.TileHeight, -1, ground.Rows);

        // Прямоугольник рисунка места (left, top, w, h; масштаб scale) → RGBA 8 сверху вниз; вне карты — прозрачно.
        private static byte[] CopyRgba(LocationGroundDefinition ground, Source source, bool color, int scale, int left, int top, int w, int h)
        {
            int tw = ground.TileWidth * scale, th = ground.TileHeight * scale;
            int mapW = ground.Columns * tw, mapH = ground.Rows * th;
            int outW = w * scale, outH = h * scale;
            byte[] result = new byte[outW * outH * 4];
            for (int y = 0; y < outH; y++)
            {
                int cy = top * scale + y;
                if (cy < 0 || cy >= mapH) continue;
                int oldRow = ground.Rows - 1 - cy / th, inY = cy % th;
                int x = 0;
                while (x < outW)
                {
                    int cx = left * scale + x;
                    if (cx < 0 || cx >= mapW) { x++; continue; }
                    int oldColumn = cx / tw, inX = cx % tw;
                    int span = Math.Min(outW - x, tw - inX);
                    byte[] image = source.Image(ground.Find(oldColumn, oldRow), color, scale);
                    if (image != null) Buffer.BlockCopy(image, (inY * tw + inX) * 4, result, (y * outW + x) * 4, span * 4);
                    x += span;
                }
            }
            return result;
        }

        private static LocationHeightTileData CopyHeight(LocationGroundDefinition ground, Source source, int left, int top, int w, int h, int oldH, ref int bits)
        {
            ushort[] values = new ushort[w * h];
            byte[] coverage = new byte[w * h];
            int tw = ground.TileWidth, th = ground.TileHeight, mapW = ground.Columns * tw;
            for (int r = 0; r < h; r++)
            {
                int canvasY = top + h - 1 - r, v = oldH - 1 - canvasY;
                if (v < 0 || v >= oldH) continue;
                int oldRow = v / th, inY = v % th;
                int x = 0;
                while (x < w)
                {
                    int cx = left + x;
                    if (cx < 0 || cx >= mapW) { x++; continue; }
                    int oldColumn = cx / tw, inX = cx % tw;
                    int span = Math.Min(w - x, tw - inX);
                    LocationHeightTileData data = source.Height(ground.Find(oldColumn, oldRow));
                    if (data != null)
                    {
                        bits = data.BitDepth;
                        Array.Copy(data.Values, inY * tw + inX, values, r * w + x, span);
                        Array.Copy(data.Coverage, inY * tw + inX, coverage, r * w + x, span);
                    }
                    x += span;
                }
            }
            return new LocationHeightTileData(w, h, bits, ground.HeightMin, ground.HeightMax, values, coverage);
        }

        private static byte[] EncodeRgba(byte[] rgba, int w, int h, bool srgb)
        {
            PngCodec.RowWriter writer = new PngCodec.RowWriter(w, h, srgb);
            for (int y = 0; y < h; y++) writer.WriteRow(rgba, y * w * 4);
            return writer.Finish();
        }

        private static void Write(string path, byte[] bytes, List<string> created)
        {
            if (path == null || bytes == null || File.Exists(path)) return;
            File.WriteAllBytes(path, bytes);
            created.Add(path);
        }

        // Рамка обрезки, сохранённая в плоскости Blender, → рисунок места новой земли (для переимпорта).
        public static RectInt CanvasRectFromPlane(LocationGroundDefinition ground)
        {
            double q = ground.ProjectionQ;
            int height = ground.Rows * ground.TileHeight;
            int x = Mathf.RoundToInt((float)((ground.CropPlane.x - ground.Origin.x) / q));
            int top = height - Mathf.RoundToInt((float)((ground.CropPlane.yMax - ground.Origin.y) / q));
            return new RectInt(x, top, Mathf.RoundToInt((float)(ground.CropPlane.width / q)), Mathf.RoundToInt((float)(ground.CropPlane.height / q)));
        }
    }
}

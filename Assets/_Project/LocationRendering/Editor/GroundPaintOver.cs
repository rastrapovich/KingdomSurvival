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
    // ПР-12О: обрисовка Color всей карты одним файлом. «Сохранить Color всей
    // карты» сшивает участки в один PNG — основу для обрисовки; «Заменить
    // Color обрисовкой» режет большой PNG по сетке manifest (участок
    // (i, j) — снизу слева) и подменяет Color участков. Разрешение может быть
    // выше рендера в k раз — участок получает W·k × H·k пикселей на тот же
    // прямоугольник. Normal и Height остаются из экспорта. Исходный рендер
    // Color сохраняется — к нему можно вернуться. Всё транзакционно, как импорт.
    public static class GroundPaintOver
    {
        public const int MaxTileSize = 16384;

        public sealed class Plan
        {
            public string SourcePath;
            public PngCodec.Header Header;
            public int TileWidth, TileHeight;
            public float Scale = 1;
            public readonly List<string> Errors = new List<string>();
            public readonly List<string> Warnings = new List<string>();
            public bool CanApply => Errors.Count == 0;

            public string Summary => Path.GetFileName(SourcePath) + ": " + Header.Width + "×" + Header.Height + " (" + PngCodec.ColorName(Header.ColorType) + " " +
                                     Header.BitDepth + " бит) → участки " + TileWidth + "×" + TileHeight +
                                     (Math.Abs(Scale - 1) > 1e-6 ? " · разрешение ×" + Scale.ToString("0.###") + " к рендеру" : " · как рендер");
        }

        // Подходит ли большой PNG к земле места: размер — ровно k × вся карта.
        public static Plan Analyze(string path, LocationGroundDefinition ground)
        {
            Plan plan = new Plan { SourcePath = path };
            if (ground == null || !ground.IsTiled || ground.Tiles.Count == 0)
            {
                plan.Errors.Add("Сначала импортируйте экспорт Blender: обрисовка режется по его сетке.");
                return plan;
            }
            byte[] head = new byte[64];
            try
            {
                using (FileStream stream = File.OpenRead(path)) stream.Read(head, 0, head.Length);
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
            {
                plan.Errors.Add("Файл не прочитан: " + error.Message);
                return plan;
            }
            if (!PngCodec.TryReadHeader(head, out PngCodec.Header header, out string problem) || !PngCodec.IsSupported(header, out problem))
            {
                plan.Errors.Add(problem);
                return plan;
            }
            plan.Header = header;
            Vector2Int map = ground.VirtualSize;
            if (header.Width % ground.Columns != 0 || header.Height % ground.Rows != 0 ||
                (long)(header.Width / ground.Columns) * ground.TileHeight != (long)(header.Height / ground.Rows) * ground.TileWidth)
            {
                plan.Errors.Add("Размер " + header.Width + "×" + header.Height + " не кратен карте " + map.x + "×" + map.y + " (" + ground.Columns + "×" + ground.Rows +
                                " участков по " + ground.TileWidth + "×" + ground.TileHeight + "). Нужен тот же кадр в том же или большем разрешении " +
                                "(например " + map.x * 2 + "×" + map.y * 2 + "), без обрезки и полей.");
                return plan;
            }
            plan.TileWidth = header.Width / ground.Columns;
            plan.TileHeight = header.Height / ground.Rows;
            plan.Scale = plan.TileWidth / (float)ground.TileWidth;
            if (plan.Scale < 1 - 1e-6f)
                plan.Errors.Add("Обрисовка меньше рендера (×" + plan.Scale.ToString("0.###") + "): земля потеряет чёткость. Сохраните её в размере не меньше " + map.x + "×" + map.y + ".");
            if (Math.Max(plan.TileWidth, plan.TileHeight) > MaxTileSize)
                plan.Errors.Add("Участок обрисовки " + plan.TileWidth + "×" + plan.TileHeight + " больше " + MaxTileSize + " пикселей — уменьшите разрешение.");
            if (!header.HasAlpha)
                plan.Warnings.Add("В обрисовке нет прозрачности: прозрачные края рендера (если были) станут непрозрачными.");
            if (header.BitDepth == 16)
                plan.Warnings.Add("Обрисовка 16 бит — Color будет сохранён в 8 бит на канал.");
            if (ground.Incomplete)
                plan.Warnings.Add("Земля неполная: обрисовка ляжет только на участки, которые уже есть.");
            return plan;
        }

        // Нарезка: строки большого PNG идут сверху вниз, каждая делится на
        // участки строки сетки; готовый участок сразу сжимается.
        public static Dictionary<string, byte[]> Slice(byte[] png, int columns, int rows)
        {
            Dictionary<string, byte[]> result = new Dictionary<string, byte[]>();
            PngCodec.TryReadHeader(png, out PngCodec.Header header, out _);
            int tileW = header.Width / columns, tileH = header.Height / rows;
            byte[] line = new byte[header.Width * 4];
            PngCodec.RowWriter[] writers = new PngCodec.RowWriter[columns];
            PngCodec.DecodeRows(png, (y, samples) =>
            {
                int bandRow = y % tileH, row = rows - 1 - y / tileH;
                PngCodec.ToRgba8(samples, header, line);
                for (int x = 0; x < columns; x++)
                {
                    if (bandRow == 0) writers[x] = new PngCodec.RowWriter(tileW, tileH);
                    writers[x].WriteRow(line, x * tileW * 4);
                    if (bandRow == tileH - 1) result[LocationGroundTile.KeyOf(x, row)] = writers[x].Finish();
                }
            });
            return result;
        }

        public static bool Apply(Plan plan, LocalLocationDatabaseAsset database, string locationId, out string message)
        {
            message = string.Empty;
            LocationVisualDefinition visual = database.FindVisual(locationId);
            LocationGroundDefinition ground = visual?.Ground;
            if (!plan.CanApply || ground == null) { message = string.Join("\n", plan.Errors.DefaultIfEmpty("Нет земли из участков.")); return false; }
            Dictionary<string, byte[]> tiles;
            try
            {
                EditorUtility.DisplayProgressBar("Обрисовка Color", "Нарезка " + Path.GetFileName(plan.SourcePath) + "…", .3f);
                byte[] png = File.ReadAllBytes(plan.SourcePath);
                tiles = Slice(png, ground.Columns, ground.Rows);
            }
            catch (Exception error) when (error is IOException || error is InvalidDataException || error is UnauthorizedAccessException)
            {
                message = "Обрисовка не применена: " + error.Message + "\nЗемля не изменена.";
                return false;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            string folder = GroundImporter.Root + "/" + GroundImporter.Sanitize(locationId) + "/" + GroundImporter.Sanitize(ground.MapId) + "/Paint";
            List<(LocationGroundTile tile, string path, string sha)> changed = new List<(LocationGroundTile, string, string)>();
            foreach (LocationGroundTile tile in ground.Tiles)
            {
                if (tile?.Color == null || !tiles.TryGetValue(tile.Key, out byte[] bytes)) continue;
                string sha = GroundExportPackage.Sha256Hex(bytes);
                if (sha == tile.PaintSha256) continue;
                changed.Add((tile, folder + "/" + GroundImporter.Sanitize(ground.MapId) + "_" + tile.Key + "_paint__" + sha.Substring(0, 10) + ".png", sha));
            }
            if (changed.Count == 0)
            {
                message = "Обрисовка совпадает с уже загруженной — участки без изменений.";
                return true;
            }

            List<string> created = new List<string>();
            try
            {
                Directory.CreateDirectory(folder);
                foreach ((LocationGroundTile tile, string path, string sha) in changed)
                {
                    if (File.Exists(path)) continue;
                    File.WriteAllBytes(path, tiles[tile.Key]);
                    created.Add(path);
                }
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                int done = 0;
                foreach ((LocationGroundTile tile, string path, string sha) in changed)
                {
                    EditorUtility.DisplayProgressBar("Обрисовка Color", "Настройки импорта " + tile.Key, done++ / (float)changed.Count);
                    GroundImporter.ConfigureColor(path, tile.Normal != null ? AssetDatabase.GetAssetPath(tile.Normal) : null, Math.Max(plan.TileWidth, plan.TileHeight));
                    if (AssetDatabase.LoadAssetAtPath<Sprite>(path) == null) throw new IOException("Unity не импортировал " + path + " как Sprite.");
                }

                Undo.RecordObject(database, "Обрисовка Color");
                foreach ((LocationGroundTile tile, string path, string sha) in changed)
                {
                    if (!tile.IsPainted) tile.RenderColorGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(tile.Color));
                    tile.Color = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                    tile.PaintSha256 = sha;
                }
                ground.ColorScale = plan.Scale;
                ground.PaintSource = Path.GetFileName(plan.SourcePath);
                ground.PaintedUtc = DateTime.UtcNow.ToString("u");
                EditorUtility.SetDirty(database);
                if (AssetDatabase.Contains(database)) AssetDatabase.SaveAssetIfDirty(database);
            }
            catch (Exception error)
            {
                foreach (string path in created) AssetDatabase.DeleteAsset(path);
                message = "Обрисовка не применена: " + error.Message + "\nНовые файлы удалены, земля не изменена.";
                Debug.LogException(error);
                return false;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
            int removed = GroundImporter.CleanupUnreferenced(locationId, ground);
            message = "Обрисовка «" + ground.PaintSource + "»: обновлено участков " + changed.Count + " из " + ground.Tiles.Count +
                      (Math.Abs(plan.Scale - 1) > 1e-6 ? ", разрешение ×" + plan.Scale.ToString("0.###") : "") +
                      (removed > 0 ? ", удалено устаревших файлов " + removed : "") + ".";
            return true;
        }

        // Вернуть Color из рендера (обрисовка удаляется из проекта, исходный файл обрисовки не трогается).
        public static bool RevertToRender(LocalLocationDatabaseAsset database, string locationId, out string message)
        {
            LocationGroundDefinition ground = database.FindVisual(locationId)?.Ground;
            if (ground == null || !ground.IsPainted) { message = "Обрисовки нет."; return false; }
            List<string> missing = ground.Tiles.Where(tile => tile.IsPainted &&
                AssetDatabase.LoadAssetAtPath<Sprite>(AssetDatabase.GUIDToAssetPath(tile.RenderColorGuid)) == null).Select(tile => tile.Key).ToList();
            if (missing.Count > 0) { message = "Нет рендера Color у участков " + string.Join(", ", missing) + " — переимпортируйте экспорт Blender."; return false; }
            Undo.RecordObject(database, "Вернуть Color из рендера");
            foreach (LocationGroundTile tile in ground.Tiles.Where(tile => tile.IsPainted))
            {
                tile.Color = AssetDatabase.LoadAssetAtPath<Sprite>(AssetDatabase.GUIDToAssetPath(tile.RenderColorGuid));
                tile.PaintSha256 = tile.RenderColorGuid = string.Empty;
            }
            ground.ColorScale = 1;
            ground.PaintSource = ground.PaintedUtc = string.Empty;
            EditorUtility.SetDirty(database);
            if (AssetDatabase.Contains(database)) AssetDatabase.SaveAssetIfDirty(database);
            int removed = GroundImporter.CleanupUnreferenced(locationId, ground);
            message = "Color из рендера вернён" + (removed > 0 ? ", файлы обрисовки удалены из проекта: " + removed : "") + ".";
            return true;
        }

        // Сшить Color всех участков (текущий: обрисовка или рендер) в один PNG.
        public static bool ExportWhole(LocationGroundDefinition ground, string path, out string message)
        {
            message = string.Empty;
            if (ground == null || !ground.IsTiled || ground.Tiles.Count == 0) { message = "Нет земли из участков."; return false; }
            LocationGroundTile sample = ground.Tiles.First(tile => tile?.Color != null);
            int tileW = Mathf.RoundToInt(sample.Color.rect.width), tileH = Mathf.RoundToInt(sample.Color.rect.height);
            if (ground.Tiles.Any(tile => tile?.Color != null && (Mathf.RoundToInt(tile.Color.rect.width) != tileW || Mathf.RoundToInt(tile.Color.rect.height) != tileH)))
            {
                message = "У участков разное разрешение Color — сначала верните рендер или загрузите обрисовку целиком.";
                return false;
            }
            try
            {
                PngCodec.RowWriter writer = new PngCodec.RowWriter(tileW * ground.Columns, tileH * ground.Rows);
                byte[] line = new byte[tileW * ground.Columns * 4];
                for (int row = ground.Rows - 1; row >= 0; row--)
                {
                    EditorUtility.DisplayProgressBar("Color всей карты", "Ряд участков " + (ground.Rows - row) + " из " + ground.Rows, (ground.Rows - 1 - row) / (float)ground.Rows);
                    byte[][] band = new byte[ground.Columns][];
                    for (int column = 0; column < ground.Columns; column++)
                    {
                        Sprite color = ground.Find(column, row)?.Color;
                        if (color == null) continue;
                        byte[] png = File.ReadAllBytes(AssetDatabase.GetAssetPath(color.texture));
                        if (!PngCodec.TryReadHeader(png, out PngCodec.Header header, out string problem)) throw new InvalidDataException(problem);
                        if (header.Width != tileW || header.Height != tileH) throw new InvalidDataException(LocationGroundTile.KeyOf(column, row) + ": исходный PNG другого размера.");
                        byte[] tile = new byte[tileW * tileH * 4];
                        PngCodec.DecodeRows(png, (y, samples) => PngCodec.ToRgba8(samples, header, tile, y * tileW * 4));
                        band[column] = tile;
                    }
                    for (int y = 0; y < tileH; y++)
                    {
                        for (int column = 0; column < ground.Columns; column++)
                        {
                            if (band[column] != null) Buffer.BlockCopy(band[column], y * tileW * 4, line, column * tileW * 4, tileW * 4);
                            else Array.Clear(line, column * tileW * 4, tileW * 4);
                        }
                        writer.WriteRow(line, 0);
                    }
                }
                File.WriteAllBytes(path, writer.Finish());
            }
            catch (Exception error) when (error is IOException || error is InvalidDataException || error is UnauthorizedAccessException)
            {
                message = "Не сохранено: " + error.Message;
                return false;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
            message = "Color всей карты сохранён: " + Path.GetFileName(path) + " (" + tileW * ground.Columns + "×" + tileH * ground.Rows +
                      "). Обрисуйте его, не меняя кадр (можно увеличить разрешение в целое число раз), и загрузите кнопкой «Заменить Color обрисовкой».";
            return true;
        }
    }
}

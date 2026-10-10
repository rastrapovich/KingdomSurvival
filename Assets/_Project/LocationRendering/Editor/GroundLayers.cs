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
    // ПР-12О: ручное управление слоями земли места. Color, Normal и Height —
    // по отдельности: заменить картинкой всей карты любого разрешения,
    // сохранить слой целиком, удалить. Новая картинка всегда покрывает тот же
    // кадр земли: если размер другой, она пересчитывается (билинейно) под
    // сетку участков. Color может задать новый размер места («подогнать место
    // под рисунок»): тогда сетка строится заново, а оставшиеся Normal и Height
    // растягиваются на новый кадр. Земли из участков ещё нет — её создаёт
    // Color. Всё транзакционно: ошибка — новые файлы удаляются, место прежнее.
    public static class GroundLayers
    {
        public enum Layer { Color, Normal, Height }

        // Color другого размера: растянуть на прежний кадр места или подогнать
        // место под рисунок (1 пиксель рисунка = 1 пиксель места).
        public enum Fit { KeepPlace, FitPicture }

        // Наибольший участок новой сетки (стороны больше режутся на участки).
        public const int MaxTile = 2048;
        public const int MaxSide = 16384;

        public sealed class Options
        {
            public Fit Fit = Fit.KeepPlace;
            public double HeightMin, HeightMax = 1, MetersPerBlenderUnit = 1;
            public bool InvertGreen;
        }

        public sealed class Plan
        {
            public Layer Layer;
            public string SourcePath;
            public PngCodec.Header Header;
            public Options Options = new Options();
            // Земли из участков нет — Color создаёт её.
            public bool NewGround;
            // Сетка участков меняется: остальные слои пересчитываются под неё.
            public bool Regrid;
            // Итоговая сетка (пиксели виртуальной карты).
            public int Columns, Rows, TileWidth, TileHeight;
            // Рисунок в виртуальной карте (сверху слева); остальное — добивка до целых участков.
            public int ContentWidth, ContentHeight;
            // Во сколько раз участок картинки подробнее участка сетки (Color, Normal).
            public int Scale = 1;
            // Картинка ложится без пересчёта (только нарезка).
            public bool Exact;
            public readonly List<string> Errors = new List<string>();
            public readonly List<string> Warnings = new List<string>();
            public readonly List<string> Changes = new List<string>();
            public bool CanApply => Errors.Count == 0;
            public int VirtualWidth => Columns * TileWidth;
            public int VirtualHeight => Rows * TileHeight;

            public string Summary => Name(Layer) + ": " + Path.GetFileName(SourcePath) + " · " + Header.Width + "×" + Header.Height + " (" +
                                     PngCodec.ColorName(Header.ColorType) + " " + Header.BitDepth + " бит) → " +
                                     (Exact ? "нарезка без пересчёта" : "пересчёт под " + VirtualWidth * Scale + "×" + VirtualHeight * Scale) +
                                     " · участки " + Columns + "×" + Rows + " по " + TileWidth * Scale + "×" + TileHeight * Scale;
        }

        public static string Name(Layer layer) => layer == Layer.Color ? "Color" : layer == Layer.Normal ? "Normal" : "Height";

        // Слой по имени файла: «…_normal», «…_n» — Normal; «…_height» — Height; иначе Color.
        public static Layer Guess(string path)
        {
            string name = Path.GetFileNameWithoutExtension(path ?? string.Empty).ToLowerInvariant();
            if (name.EndsWith("_height") || name.EndsWith("_heights") || name.EndsWith("_высота") || name.EndsWith("_h")) return Layer.Height;
            if (name.EndsWith("_normal") || name.EndsWith("_normals") || name.EndsWith("_нормаль") || name.EndsWith("_n")) return Layer.Normal;
            return Layer.Color;
        }

        // Что сейчас в слое (для строки в окне).
        public static string Describe(LocationGroundDefinition ground, Layer layer)
        {
            if (ground == null || !ground.IsTiled || ground.Tiles.Count == 0) return layer == Layer.Color ? "нет земли из участков" : "—";
            int total = ground.Columns * ground.Rows;
            List<LocationGroundTile> tiles = ground.Tiles.Where(tile => tile != null).ToList();
            switch (layer)
            {
                case Layer.Color:
                {
                    LocationGroundTile sample = tiles.FirstOrDefault(tile => tile.Color != null);
                    int count = tiles.Count(tile => tile.Color != null);
                    if (sample == null) return "нет";
                    return count + " из " + total + " · участок " + Mathf.RoundToInt(sample.Color.rect.width) + "×" + Mathf.RoundToInt(sample.Color.rect.height) +
                           (ground.IsPainted ? " · своя картинка «" + ground.PaintSource + "»" : " · рендер Blender");
                }
                case Layer.Normal:
                {
                    LocationGroundTile sample = tiles.FirstOrDefault(tile => tile.Normal != null);
                    int count = tiles.Count(tile => tile.Normal != null);
                    if (sample == null) return "нет — земля освещается ровно";
                    return count + " из " + total + " · участок " + sample.Normal.width + "×" + sample.Normal.height;
                }
                default:
                {
                    int count = tiles.Count(tile => tile.Height != null);
                    if (count == 0) return "нет — люди стоят на плоской земле";
                    return count + " из " + total + " · " + ground.HeightMin.ToString("0.###") + " … " + ground.HeightMax.ToString("0.###") + " BU · " +
                           ground.HeightBitDepth + " бит" + (ground.HeightEnabled ? "" : " · выключена");
                }
            }
        }

        // ------------------------------------------------------------------
        // Разбор
        // ------------------------------------------------------------------

        public static Plan Analyze(LocalLocationDefinition location, LocationGroundDefinition ground, Layer layer, string path, Options options)
        {
            Plan plan = new Plan { Layer = layer, SourcePath = path, Options = options ?? new Options() };
            if (location == null) { plan.Errors.Add("Место не выбрано."); return plan; }
            if (!GroundExportPackage.ReadHeader(path, out PngCodec.Header header, out string problem) || !PngCodec.IsSupported(header, out problem))
            {
                plan.Errors.Add(problem);
                return plan;
            }
            plan.Header = header;
            int w = header.Width, h = header.Height;
            bool tiled = ground != null && ground.IsTiled && ground.Tiles.Any(tile => tile?.Color != null);
            if (!tiled && layer != Layer.Color)
            {
                plan.Errors.Add("Сначала задайте Color — он создаёт землю места; Normal и Height ложатся на неё.");
                return plan;
            }
            if (Math.Max(w, h) > MaxSide * 4) { plan.Errors.Add("Картинка " + w + "×" + h + " слишком велика."); return plan; }

            if (layer == Layer.Color && (!tiled || plan.Options.Fit == Fit.FitPicture))
            {
                plan.NewGround = !tiled;
                Vector2Int current = tiled ? ground.VirtualSize : Vector2Int.zero;
                plan.Columns = Mathf.Max(1, Mathf.CeilToInt(w / (float)MaxTile));
                plan.Rows = Mathf.Max(1, Mathf.CeilToInt(h / (float)MaxTile));
                plan.TileWidth = Mathf.CeilToInt(w / (float)plan.Columns);
                plan.TileHeight = Mathf.CeilToInt(h / (float)plan.Rows);
                if (tiled && current.x == w && current.y == h)
                {
                    // Тот же размер — прежняя сетка, без перестройки.
                    plan.Columns = ground.Columns; plan.Rows = ground.Rows;
                    plan.TileWidth = ground.TileWidth; plan.TileHeight = ground.TileHeight;
                }
                plan.Regrid = tiled && (plan.Columns != ground.Columns || plan.Rows != ground.Rows || plan.TileWidth != ground.TileWidth || plan.TileHeight != ground.TileHeight);
                plan.ContentWidth = w; plan.ContentHeight = h;
                plan.Exact = true;
                Vector2 canvas = LocationVisualGeometry.CanvasSize(location);
                if (Mathf.Abs(canvas.x - plan.VirtualWidth) > .5f || Mathf.Abs(canvas.y - plan.VirtualHeight) > .5f)
                    plan.Changes.Add("Размер места " + Mathf.RoundToInt(canvas.x) + "×" + Mathf.RoundToInt(canvas.y) + " → " + plan.VirtualWidth + "×" + plan.VirtualHeight +
                                     " (1 пиксель рисунка = 1 пиксель места). Входы, объекты, противники и зоны сохранят долю рисунка.");
                if (plan.VirtualWidth != w || plan.VirtualHeight != h)
                    plan.Changes.Add("Добивка до целых участков: +" + (plan.VirtualWidth - w) + " px справа, +" + (plan.VirtualHeight - h) + " px снизу (прозрачно).");
                if (plan.NewGround)
                    plan.Changes.Add("Место получает землю из участков " + plan.Columns + "×" + plan.Rows +
                                     ". Прежний «Рисунок места» сохраняется, но не показывается.");
                if (plan.Regrid)
                {
                    plan.Changes.Add("Сетка " + ground.Columns + "×" + ground.Rows + " по " + ground.TileWidth + "×" + ground.TileHeight + " → " +
                                     plan.Columns + "×" + plan.Rows + " по " + plan.TileWidth + "×" + plan.TileHeight + ".");
                    if (ground.Tiles.Any(tile => tile?.Normal != null)) plan.Changes.Add("Normal пересчитается на новый кадр (растяжение).");
                    if (ground.HasAnyHeight) plan.Changes.Add("Height пересчитается на новый кадр (растяжение).");
                    if (ground.IsPainted && ground.Tiles.Any(tile => !string.IsNullOrEmpty(tile?.RenderColorGuid)))
                        plan.Warnings.Add("Рендер Blender под прежней картинкой не переносится: «Вернуть Color из рендера» станет недоступно.");
                }
                if (tiled && Mathf.Abs(current.x * (float)h - current.y * (float)w) > Mathf.Max(current.x, current.y))
                    plan.Warnings.Add("Пропорции другие (" + current.x + "×" + current.y + " → " + w + "×" + h + "): Normal и Height растянутся неравномерно.");
                return plan;
            }

            plan.Columns = ground.Columns; plan.Rows = ground.Rows;
            plan.TileWidth = ground.TileWidth; plan.TileHeight = ground.TileHeight;
            Vector2Int map = ground.VirtualSize;
            plan.ContentWidth = map.x; plan.ContentHeight = map.y;
            if (layer == Layer.Height)
            {
                plan.Scale = 1;
                if (!(plan.Options.HeightMax > plan.Options.HeightMin) || double.IsNaN(plan.Options.HeightMin) || double.IsInfinity(plan.Options.HeightMax))
                    plan.Errors.Add("Задайте Height Range: max больше min (абсолютная Z Blender, общий для всей карты).");
                if (!(plan.Options.MetersPerBlenderUnit > 0)) plan.Errors.Add("Метров в Blender Unit должно быть больше нуля.");
                if (!header.HasAlpha) plan.Warnings.Add("В Height нет альфы — высота считается заданной везде (покрытие полное).");
                if (header.BitDepth == 8) plan.Warnings.Add("Height 8 бит: шаг " + ((plan.Options.HeightMax - plan.Options.HeightMin) / 255).ToString("0.####") + " BU — точность ниже 16 бит.");
            }
            else
            {
                // Целое кратное карты — без пересчёта (обрисовка ×2, ×3…).
                int k = Mathf.Max(1, Mathf.RoundToInt(w / (float)map.x));
                plan.Scale = k;
                if (layer == Layer.Normal && k > 1 && (w != map.x * k || h != map.y * k)) plan.Scale = k = 1;
                if (Math.Max(plan.TileWidth, plan.TileHeight) * k > MaxSide)
                    plan.Errors.Add("Участок " + plan.TileWidth * k + "×" + plan.TileHeight * k + " больше " + MaxSide + " пикселей — уменьшите картинку.");
                if (layer == Layer.Color && !header.HasAlpha) plan.Warnings.Add("В картинке нет прозрачности: пустые края станут непрозрачными.");
            }
            plan.Exact = w == map.x * plan.Scale && h == map.y * plan.Scale;
            if (!plan.Exact)
            {
                plan.Changes.Add(Name(layer) + " " + w + "×" + h + " пересчитается под карту " + map.x * plan.Scale + "×" + map.y * plan.Scale + " (тот же кадр земли).");
                if (Mathf.Abs(map.x * (float)h - map.y * (float)w) > Mathf.Max(map.x, map.y))
                    plan.Warnings.Add("Пропорции " + w + "×" + h + " отличаются от земли " + map.x + "×" + map.y + ": картинка растянется неравномерно." +
                                      (layer == Layer.Color ? " Чтобы место подстроилось под рисунок — выберите «Подогнать место под рисунок»." : ""));
            }
            if (layer == Layer.Color)
                plan.Changes.Add("Color участков заменится" + (plan.Scale > 1 ? " (разрешение ×" + plan.Scale + ")" : "") + "; Normal и Height остаются.");
            else if (layer == Layer.Normal)
                plan.Changes.Add((ground.Tiles.Any(tile => tile?.Normal != null) ? "Normal заменится" : "Земля получит Normal") +
                                 (plan.Options.InvertGreen ? " (зелёный перевернётся под Unity)" : "") + ".");
            else
                plan.Changes.Add((ground.HasAnyHeight ? "Height заменится" : "Земля получит Height") + ": " + plan.Options.HeightMin.ToString("0.####") + " … " +
                                 plan.Options.HeightMax.ToString("0.####") + " BU, " + plan.Options.MetersPerBlenderUnit.ToString("0.###") + " м/BU.");
            return plan;
        }

        // ------------------------------------------------------------------
        // Применение
        // ------------------------------------------------------------------

        private sealed class Built
        {
            public Dictionary<string, byte[]> Tiles;
            public Dictionary<string, string> Paths = new Dictionary<string, string>();
            public int Bits = 16;
        }

        public static bool Apply(Plan plan, LocalLocationDatabaseAsset database, string locationId, Action<LocalLocationDefinition, float, float> resizeCanvas, out string message)
        {
            message = string.Empty;
            LocalLocationDefinition location = database.locations.Find(item => item.Id == locationId);
            LocationVisualDefinition visual = database.FindVisual(locationId);
            if (location == null || visual == null) { message = "Место или его художественная сборка не найдены."; return false; }
            if (!plan.CanApply) { message = string.Join("\n", plan.Errors); return false; }
            LocationGroundDefinition ground = visual.Ground;
            string mapId = plan.NewGround || ground == null || string.IsNullOrEmpty(ground.MapId)
                ? GroundImporter.Sanitize(Path.GetFileNameWithoutExtension(plan.SourcePath)) : ground.MapId;
            string folder = GroundImporter.Root + "/" + GroundImporter.Sanitize(locationId) + "/" + GroundImporter.Sanitize(mapId) + "/Layers";

            // 1. Подготовка вне Assets.
            Built main, normal = null, height = null;
            try
            {
                EditorUtility.DisplayProgressBar("Слои земли", Name(plan.Layer) + ": " + Path.GetFileName(plan.SourcePath) + "…", .2f);
                int bits = plan.Layer == Layer.Height && plan.Exact && plan.Header.BitDepth == 8 ? 8 : 16;
                main = Render(new PngSource(plan.SourcePath, plan.Layer, plan.Options.InvertGreen), plan.Layer, plan, plan.Scale,
                    plan.Options.HeightMin, plan.Options.HeightMax, bits);
                if (plan.Regrid)
                {
                    if (ground.Tiles.Any(tile => tile?.Normal != null))
                    {
                        EditorUtility.DisplayProgressBar("Слои земли", "Normal на новый кадр…", .5f);
                        normal = Render(new TiledSource(ground, Layer.Normal), Layer.Normal, plan, 1, 0, 1, 16);
                    }
                    if (ground.HasAnyHeight)
                    {
                        EditorUtility.DisplayProgressBar("Слои земли", "Height на новый кадр…", .7f);
                        height = Render(new TiledSource(ground, Layer.Height), Layer.Height, plan, 1, ground.HeightMin, ground.HeightMax, 16);
                    }
                }
            }
            catch (Exception error) when (error is IOException || error is InvalidDataException || error is UnauthorizedAccessException || error is ArgumentException)
            {
                EditorUtility.ClearProgressBar();
                message = "Слой не применён: " + error.Message + "\nМесто не изменено.";
                return false;
            }

            // 2. Запись в Assets.
            List<string> created = new List<string>();
            string stem = GroundImporter.Sanitize(mapId);
            try
            {
                Directory.CreateDirectory(folder);
                Write(main, plan.Layer, folder, stem, created);
                if (normal != null) Write(normal, Layer.Normal, folder, stem, created);
                if (height != null) Write(height, Layer.Height, folder, stem, created);
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

                Built normals = plan.Layer == Layer.Normal ? main : normal;
                if (normals != null)
                    foreach (string path in normals.Paths.Values) GroundImporter.ConfigureNormal(path, Math.Max(plan.TileWidth, plan.TileHeight) * Math.Max(1, plan.Scale));
                if (plan.Layer == Layer.Color)
                {
                    // Sprite появляется только после настроек импорта; нормаль — сразу второй текстурой.
                    int done = 0;
                    foreach (KeyValuePair<string, string> pair in main.Paths)
                    {
                        EditorUtility.DisplayProgressBar("Слои земли", "Настройки импорта " + pair.Key, done++ / (float)Math.Max(1, main.Paths.Count));
                        Texture2D kept = !plan.NewGround && !plan.Regrid ? ground?.Find(KeyX(pair.Key), KeyY(pair.Key))?.Normal : null;
                        string normalPath = normal != null && normal.Paths.TryGetValue(pair.Key, out string rebuilt) ? rebuilt
                            : kept != null ? AssetDatabase.GetAssetPath(kept) : null;
                        GroundImporter.ConfigureColor(pair.Value, normalPath, Math.Max(plan.TileWidth, plan.TileHeight) * plan.Scale);
                        if (AssetDatabase.LoadAssetAtPath<Sprite>(pair.Value) == null) throw new IOException("Unity не импортировал " + pair.Value + " как Sprite.");
                    }
                }
                Undo.RecordObject(database, "Слой земли: " + Name(plan.Layer));
                if (plan.NewGround || ground == null) ground = visual.Ground = visual.Ground ?? new LocationGroundDefinition();
                Vector2Int oldSize = ground.VirtualSize;
                bool hadTiles = ground.IsTiled && ground.Tiles.Count > 0;

                if (plan.Layer == Layer.Color)
                {
                    if (plan.NewGround || plan.Regrid) RebuildTiles(ground, plan, main, normal, height, mapId);
                    else ReplaceColor(ground, plan, main);
                }
                else if (plan.Layer == Layer.Normal)
                {
                    foreach (LocationGroundTile tile in ground.Tiles)
                    {
                        if (tile == null || !main.Paths.TryGetValue(tile.Key, out string path)) continue;
                        tile.Normal = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                        if (tile.Normal == null) throw new IOException("Unity не импортировал " + path + ".");
                        tile.NormalSha256 = GroundExportPackage.Sha256Hex(main.Tiles[tile.Key]);
                        tile.NormalRevision = "вручную";
                        AttachNormal(tile);
                    }
                    ground.NormalGreenInverted = false;
                    ground.NormalLevelDegrees = 0;
                }
                else
                {
                    foreach (LocationGroundTile tile in ground.Tiles)
                    {
                        if (tile == null || !main.Paths.TryGetValue(tile.Key, out string path)) continue;
                        tile.Height = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
                        if (tile.Height == null) throw new IOException("Unity не импортировал " + path + ".");
                        tile.HeightSha256 = GroundExportPackage.Sha256Hex(main.Tiles[tile.Key]);
                        tile.HeightRevision = "вручную";
                    }
                    ground.HeightMin = plan.Options.HeightMin;
                    ground.HeightMax = plan.Options.HeightMax;
                    ground.MetersPerBlenderUnit = plan.Options.MetersPerBlenderUnit;
                    ground.HeightBitDepth = main.Bits;
                    ground.HeightEnabled = true;
                }
                ground.Incomplete = GroundImporter.IsIncomplete(ground);
                if (plan.Layer == Layer.Color && (plan.NewGround || plan.Regrid || !hadTiles))
                {
                    if (visual.Camera == null || visual.Camera.Version == 0) visual.Camera = LocationCameraSettings.ForLargeMap();
                    FitPlace(location, visual, plan, resizeCanvas, hadTiles ? oldSize : Vector2Int.zero);
                }
                EditorUtility.SetDirty(database);
                if (AssetDatabase.Contains(database)) AssetDatabase.SaveAssetIfDirty(database);
            }
            catch (Exception error)
            {
                foreach (string path in created) AssetDatabase.DeleteAsset(path);
                message = "Слой не применён: " + error.Message + "\nНовые файлы удалены, место не изменено.";
                Debug.LogException(error);
                return false;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
            int removed = GroundImporter.CleanupUnreferenced(locationId, visual.Ground);
            message = Name(plan.Layer) + " «" + Path.GetFileName(plan.SourcePath) + "» применён: участков " + main.Tiles.Count +
                      (plan.Exact ? "" : ", пересчитан под " + plan.VirtualWidth * plan.Scale + "×" + plan.VirtualHeight * plan.Scale) +
                      (normal != null ? ", Normal пересчитан" : "") + (height != null ? ", Height пересчитан" : "") +
                      (removed > 0 ? ", удалено устаревших файлов " + removed : "") + ".";
            return true;
        }

        // Новая сетка: участки заново (Color — новый, Normal и Height — пересчитанные).
        private static void RebuildTiles(LocationGroundDefinition ground, Plan plan, Built color, Built normal, Built height, string mapId)
        {
            bool fresh = plan.NewGround || !ground.IsTiled;
            int padBottom = plan.VirtualHeight - plan.ContentHeight;
            if (fresh)
            {
                ground.Version = LocationGroundDefinition.CurrentVersion;
                ground.MapId = mapId;
                ground.RevisionId = string.Empty;
                ground.ExporterVersion = "вручную";
                ground.ConfigurationHash = string.Empty;
                ground.ExportStatus = "complete";
                ground.SurfaceContract = "matched_ground";
                ground.Manifest = null;
                ground.Warnings = new List<string>();
                ground.ExportPpu = LocationVisualGeometry.PixelsPerUnit;
                ground.ProjectionQ = 0;
                ground.Origin = Vector2.zero;
                ground.HeightEnabled = false;
                ground.Cropped = false;
                ground.NormalGreenInverted = false;
                ground.NormalLevelDegrees = 0;
            }
            else
            {
                // Тот же кадр мира в новом разрешении: плотность проекции и начало сетки.
                Vector2Int old = ground.VirtualSize;
                float k = old.x / (float)Mathf.Max(1, plan.ContentWidth);
                if (ground.ProjectionQ > 0)
                {
                    ground.ProjectionQ *= k;
                    ground.Origin -= new Vector2(0, padBottom * ground.ProjectionQ);
                }
                Rect useful = ground.UsefulPixels;
                if (useful.width > 0)
                {
                    float sx = plan.ContentWidth / (float)Mathf.Max(1, old.x), sy = plan.ContentHeight / (float)Mathf.Max(1, old.y);
                    ground.UsefulPixels = new Rect(useful.x * sx, useful.y * sy + padBottom, useful.width * sx, useful.height * sy);
                }
            }
            if (fresh || ground.UsefulPixels.width <= 0) ground.UsefulPixels = new Rect(0, padBottom, plan.ContentWidth, plan.ContentHeight);
            ground.Mode = LocationGroundMode.Tiles;
            ground.ImportedUtc = DateTime.UtcNow.ToString("u");
            ground.Columns = plan.Columns; ground.Rows = plan.Rows;
            ground.TileWidth = plan.TileWidth; ground.TileHeight = plan.TileHeight;
            List<LocationGroundTile> tiles = new List<LocationGroundTile>();
            for (int y = 0; y < plan.Rows; y++)
            {
                for (int x = 0; x < plan.Columns; x++)
                {
                    string key = LocationGroundTile.KeyOf(x, y);
                    string sha = GroundExportPackage.Sha256Hex(color.Tiles[key]);
                    LocationGroundTile tile = new LocationGroundTile
                    {
                        X = x, Y = y, Color = AssetDatabase.LoadAssetAtPath<Sprite>(color.Paths[key]),
                        ColorSha256 = sha, PaintSha256 = sha, ColorRevision = "вручную"
                    };
                    if (normal != null && normal.Paths.TryGetValue(key, out string normalPath))
                    {
                        tile.Normal = AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath);
                        tile.NormalSha256 = GroundExportPackage.Sha256Hex(normal.Tiles[key]);
                        tile.NormalRevision = "вручную";
                    }
                    if (height != null && height.Paths.TryGetValue(key, out string heightPath))
                    {
                        tile.Height = AssetDatabase.LoadAssetAtPath<TextAsset>(heightPath);
                        tile.HeightSha256 = GroundExportPackage.Sha256Hex(height.Tiles[key]);
                        tile.HeightRevision = "вручную";
                    }
                    tiles.Add(tile);
                }
            }
            ground.Tiles = tiles;
            if (height != null) ground.HeightBitDepth = height.Bits;
            if (!ground.HasAnyHeight) ground.HeightEnabled = false;
            ground.ColorScale = 1;
            ground.PaintSource = Path.GetFileName(plan.SourcePath);
            ground.PaintedUtc = DateTime.UtcNow.ToString("u");
        }

        // Та же сетка: Color участков — новая картинка; рендер под ней остаётся для возврата.
        private static void ReplaceColor(LocationGroundDefinition ground, Plan plan, Built color)
        {
            foreach (LocationGroundTile tile in ground.Tiles)
            {
                if (tile == null || !color.Paths.TryGetValue(tile.Key, out string path)) continue;
                if (!tile.IsPainted && tile.Color != null) tile.RenderColorGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(tile.Color.texture));
                tile.Color = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                tile.PaintSha256 = GroundExportPackage.Sha256Hex(color.Tiles[tile.Key]);
            }
            ground.ColorScale = plan.Scale;
            ground.PaintSource = Path.GetFileName(plan.SourcePath);
            ground.PaintedUtc = DateTime.UtcNow.ToString("u");
        }

        // Место — под новый кадр: рисунок (без добивки) ложится на прежний
        // рисунок места, точки сохраняют долю; добивка достраивает холст.
        private static void FitPlace(LocalLocationDefinition location, LocationVisualDefinition visual, Plan plan,
            Action<LocalLocationDefinition, float, float> resizeCanvas, Vector2Int oldGround)
        {
            Vector2 canvas = LocationVisualGeometry.CanvasSize(location);
            if (Mathf.Abs(canvas.x - plan.ContentWidth) > .5f || Mathf.Abs(canvas.y - plan.ContentHeight) > .5f)
            {
                if (resizeCanvas != null) resizeCanvas(location, plan.ContentWidth, plan.ContentHeight);
                else LocationDatabaseWindow.ResizeLocation(location, plan.ContentWidth, plan.ContentHeight, location.HexesAcross, true);
            }
            if (plan.VirtualWidth != plan.ContentWidth || plan.VirtualHeight != plan.ContentHeight)
                LocationRebase.Translate(location, visual, Vector2.zero, new Vector2(plan.VirtualWidth, plan.VirtualHeight));
        }

        // Нормаль участка — второй текстурой _NormalMap его Color (и рендера под обрисовкой).
        private static void AttachNormal(LocationGroundTile tile)
        {
            if (tile.Color != null && !SpriteNormalMaps.Assign(tile.Color, tile.Normal, out string problem)) throw new IOException(problem);
            if (!string.IsNullOrEmpty(tile.RenderColorGuid))
            {
                Sprite render = AssetDatabase.LoadAssetAtPath<Sprite>(AssetDatabase.GUIDToAssetPath(tile.RenderColorGuid));
                if (render != null) SpriteNormalMaps.Assign(render, tile.Normal, out _);
            }
        }

        private static void Write(Built built, Layer layer, string folder, string stem, List<string> created)
        {
            foreach (KeyValuePair<string, byte[]> pair in built.Tiles)
            {
                string sha = GroundExportPackage.Sha256Hex(pair.Value).Substring(0, 10);
                string suffix = layer == Layer.Color ? "_color" : layer == Layer.Normal ? "_normal" : "_height";
                string path = folder + "/" + stem + "_" + pair.Key + suffix + "__" + sha + (layer == Layer.Height ? ".bytes" : ".png");
                built.Paths[pair.Key] = path;
                if (File.Exists(path)) continue;
                File.WriteAllBytes(path, pair.Value);
                created.Add(path);
            }
        }

        // ------------------------------------------------------------------
        // Удаление
        // ------------------------------------------------------------------

        public static bool Remove(LocalLocationDatabaseAsset database, string locationId, Layer layer, out string message)
        {
            LocationVisualDefinition visual = database.FindVisual(locationId);
            LocationGroundDefinition ground = visual?.Ground;
            if (ground == null || !ground.IsTiled || ground.Tiles.Count == 0) { message = "У места нет земли из участков."; return false; }
            Undo.RecordObject(database, "Удалить слой земли: " + Name(layer));
            switch (layer)
            {
                case Layer.Normal:
                    foreach (LocationGroundTile tile in ground.Tiles.Where(tile => tile?.Normal != null))
                    {
                        tile.Normal = null;
                        tile.NormalSha256 = tile.NormalRevision = string.Empty;
                        AttachNormal(tile);
                    }
                    ground.NormalGreenInverted = false;
                    ground.NormalLevelDegrees = 0;
                    break;
                case Layer.Height:
                    foreach (LocationGroundTile tile in ground.Tiles.Where(tile => tile != null))
                    {
                        tile.Height = null;
                        tile.HeightSha256 = tile.HeightRevision = string.Empty;
                    }
                    ground.HeightEnabled = false;
                    break;
                default:
                    // Без Color земли нет: место возвращается к «Рисунку места».
                    LocationGroundDefinition empty = new LocationGroundDefinition();
                    visual.Ground = empty;
                    ground = empty;
                    break;
            }
            ground.Incomplete = GroundImporter.IsIncomplete(ground);
            EditorUtility.SetDirty(database);
            if (AssetDatabase.Contains(database)) AssetDatabase.SaveAssetIfDirty(database);
            int removed = GroundImporter.CleanupUnreferenced(locationId, ground);
            message = (layer == Layer.Color ? "Земля из участков удалена — место снова на «Рисунке места»" : Name(layer) + " удалён") +
                      (removed > 0 ? ", файлов убрано из проекта: " + removed : "") + ".";
            return true;
        }

        // ------------------------------------------------------------------
        // Слой целиком в один PNG
        // ------------------------------------------------------------------

        public static bool ExportWhole(LocationGroundDefinition ground, Layer layer, string path, out string message)
        {
            if (layer == Layer.Color) return GroundPaintOver.ExportWhole(ground, path, out message);
            message = string.Empty;
            if (ground == null || !ground.IsTiled || ground.Tiles.Count == 0) { message = "Нет земли из участков."; return false; }
            if (layer == Layer.Normal && !ground.Tiles.Any(tile => tile?.Normal != null)) { message = "У земли нет Normal."; return false; }
            if (layer == Layer.Height && !ground.HasAnyHeight) { message = "У земли нет Height."; return false; }
            try
            {
                EditorUtility.DisplayProgressBar("Слой целиком", Name(layer) + "…", .4f);
                TiledSource source = new TiledSource(ground, layer);
                bool height = layer == Layer.Height;
                int channels = height ? 2 : 3, bits = height ? 16 : 8;
                PngCodec.RowWriter writer = new PngCodec.RowWriter(source.Width, source.Height, bits, height ? PngCodec.GrayAlpha : PngCodec.Rgb, false);
                byte[] line = new byte[source.Width * channels * bits / 8];
                source.Run((y, row) =>
                {
                    for (int x = 0; x < source.Width; x++)
                    {
                        if (height)
                        {
                            ushort value = (ushort)Math.Round(Mathf.Clamp01(row[x * 2]) * 65535.0), cover = (ushort)Math.Round(Mathf.Clamp01(row[x * 2 + 1]) * 65535.0);
                            line[x * 4] = (byte)(value >> 8); line[x * 4 + 1] = (byte)value;
                            line[x * 4 + 2] = (byte)(cover >> 8); line[x * 4 + 3] = (byte)cover;
                        }
                        else
                            for (int c = 0; c < 3; c++) line[x * 3 + c] = ToByte(row[x * 3 + c] * .5f + .5f);
                    }
                    writer.WriteRow(line, 0);
                });
                File.WriteAllBytes(path, writer.Finish());
                message = Name(layer) + " всей карты сохранён: " + Path.GetFileName(path) + " (" + source.Width + "×" + source.Height + ")" +
                          (height ? ", 16 бит, альфа — покрытие, диапазон " + ground.HeightMin.ToString("0.####") + " … " + ground.HeightMax.ToString("0.####") + " BU" : "") +
                          ". Правьте, не меняя кадр, и загрузите кнопкой «Заменить…».";
                return true;
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
        }

        // ------------------------------------------------------------------
        // Источники строк и пересчёт
        // ------------------------------------------------------------------

        // Строки картинки сверху вниз, значения каналов — числа:
        // Color — RGBA 0..1 (без умножения на альфу), Normal — XYZ −1..1,
        // Height — значение 0..1 и покрытие 0..1.
        private interface IRowSource
        {
            int Width { get; }
            int Height { get; }
            int Channels { get; }
            void Run(Action<int, float[]> row);
        }

        private sealed class PngSource : IRowSource
        {
            private readonly string path;
            private readonly Layer layer;
            private readonly bool invertGreen;
            private readonly PngCodec.Header header;

            public PngSource(string path, Layer layer, bool invertGreen)
            {
                this.path = path; this.layer = layer; this.invertGreen = invertGreen;
                if (!GroundExportPackage.ReadHeader(path, out header, out string problem)) throw new InvalidDataException(problem);
            }

            public int Width => header.Width;
            public int Height => header.Height;
            public int Channels => ChannelsOf(layer);

            public void Run(Action<int, float[]> row)
            {
                int channels = header.Channels, count = Channels;
                bool alpha = header.HasAlpha;
                float max = header.BitDepth == 16 ? 65535f : 255f;
                float[] values = new float[header.Width * count];
                using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20))
                {
                    PngCodec.DecodeRows(stream, (y, samples) =>
                    {
                        for (int x = 0; x < header.Width; x++)
                        {
                            int s = x * channels, t = x * count;
                            float r = samples[s] / max, g = channels >= 3 ? samples[s + 1] / max : r, b = channels >= 3 ? samples[s + 2] / max : r;
                            float a = alpha ? samples[s + channels - 1] / max : 1;
                            switch (layer)
                            {
                                case Layer.Color:
                                    values[t] = r; values[t + 1] = g; values[t + 2] = b; values[t + 3] = a;
                                    break;
                                case Layer.Normal:
                                    values[t] = r * 2 - 1; values[t + 1] = (g * 2 - 1) * (invertGreen ? -1 : 1); values[t + 2] = b * 2 - 1;
                                    break;
                                default:
                                    values[t] = a > 0 ? r : 0; values[t + 1] = a;
                                    break;
                            }
                        }
                        row(y, values);
                    });
                }
            }
        }

        // Слой участков текущей земли — одной картинкой (строки сверху вниз).
        private sealed class TiledSource : IRowSource
        {
            private readonly LocationGroundDefinition ground;
            private readonly Layer layer;
            private readonly int tileW, tileH;

            public TiledSource(LocationGroundDefinition ground, Layer layer)
            {
                this.ground = ground; this.layer = layer;
                tileW = ground.TileWidth; tileH = ground.TileHeight;
                if (layer == Layer.Normal)
                {
                    LocationGroundTile sample = ground.Tiles.First(tile => tile?.Normal != null);
                    if (!GroundExportPackage.ReadHeader(AssetDatabase.GetAssetPath(sample.Normal), out PngCodec.Header header, out string problem))
                        throw new InvalidDataException(problem);
                    tileW = header.Width; tileH = header.Height;
                }
            }

            public int Width => ground.Columns * tileW;
            public int Height => ground.Rows * tileH;
            public int Channels => ChannelsOf(layer);

            public void Run(Action<int, float[]> row)
            {
                int count = Channels;
                float[] values = new float[Width * count];
                for (int band = ground.Rows - 1; band >= 0; band--)
                {
                    float[][] tiles = new float[ground.Columns][];
                    for (int column = 0; column < ground.Columns; column++) tiles[column] = Tile(ground.Find(column, band));
                    for (int y = 0; y < tileH; y++)
                    {
                        for (int column = 0; column < ground.Columns; column++)
                        {
                            float[] tile = tiles[column];
                            int offset = column * tileW * count;
                            if (tile != null) Array.Copy(tile, y * tileW * count, values, offset, tileW * count);
                            else
                                for (int i = 0; i < tileW * count; i++) values[offset + i] = layer == Layer.Normal && i % 3 == 2 ? 1 : 0;
                        }
                        row((ground.Rows - 1 - band) * tileH + y, values);
                    }
                }
            }

            // Участок — числами, строки сверху вниз; нет данных — null.
            private float[] Tile(LocationGroundTile tile)
            {
                int count = Channels;
                if (layer == Layer.Height)
                {
                    if (tile?.Height == null) return null;
                    LocationHeightTileData data = LocationHeightTileData.Deserialize(tile.Height.bytes);
                    if (data.Width != tileW || data.Height != tileH) throw new InvalidDataException(tile.Key + ": Height " + data.Width + "×" + data.Height + " не совпадает с сеткой.");
                    // Значения — в общем диапазоне земли.
                    double scale = (data.Max - data.Min) / Math.Max(1e-12, ground.HeightMax - ground.HeightMin);
                    double shift = (data.Min - ground.HeightMin) / Math.Max(1e-12, ground.HeightMax - ground.HeightMin);
                    float[] result = new float[tileW * tileH * 2];
                    for (int y = 0; y < tileH; y++)
                        for (int x = 0; x < tileW; x++)
                        {
                            int source = (tileH - 1 - y) * tileW + x, target = (y * tileW + x) * 2;
                            byte cover = data.Coverage[source];
                            result[target] = cover > 0 ? (float)(shift + data.Values[source] / data.MaxRaw * scale) : 0;
                            result[target + 1] = cover / 255f;
                        }
                    return result;
                }
                if (tile?.Normal == null) return null;
                byte[] png = File.ReadAllBytes(AssetDatabase.GetAssetPath(tile.Normal));
                if (!PngCodec.TryReadHeader(png, out PngCodec.Header header, out string problem)) throw new InvalidDataException(problem);
                if (header.Width != tileW || header.Height != tileH) throw new InvalidDataException(tile.Key + ": Normal другого размера, чем у соседних участков.");
                float[] normal = new float[tileW * tileH * count];
                int channels = header.Channels;
                float max = header.BitDepth == 16 ? 65535f : 255f;
                PngCodec.DecodeRows(png, (y, samples) =>
                {
                    for (int x = 0; x < tileW; x++)
                    {
                        int s = x * channels, t = (y * tileW + x) * 3;
                        for (int c = 0; c < 3; c++) normal[t + c] = samples[s + Math.Min(c, channels - 1)] / max * 2 - 1;
                    }
                });
                return normal;
            }
        }

        private static int ChannelsOf(Layer layer) => layer == Layer.Color ? 4 : layer == Layer.Normal ? 3 : 2;

        // Пересчёт источника под сетку плана и нарезка на участки.
        // scale — во сколько раз участок подробнее сетки.
        private static Built Render(IRowSource source, Layer layer, Plan plan, int scale, double heightMin, double heightMax, int heightBits)
        {
            int tileW = plan.TileWidth * scale, tileH = plan.TileHeight * scale;
            int contentW = plan.ContentWidth * scale, contentH = plan.ContentHeight * scale;
            TileSink sink = new TileSink(layer, plan.Columns, plan.Rows, tileW, tileH, heightMin, heightMax, heightBits);
            Resampler resampler = new Resampler(source.Width, source.Height, contentW, contentH, source.Channels, layer, sink.Content);
            source.Run(resampler.Push);
            resampler.Finish();
            sink.Finish();
            return new Built { Tiles = sink.Result, Bits = heightBits == 8 ? 8 : 16 };
        }

        // Билинейный пересчёт строк потоком: в памяти две строки источника.
        // Color — с учётом альфы (без тёмной каймы), Normal — с нормировкой,
        // Height — только покрытые пиксели и без смешивания через разрыв.
        // Тот же размер — строки проходят как есть.
        private sealed class Resampler
        {
            private readonly int srcW, srcH, dstW, dstH, channels;
            private readonly Layer layer;
            private readonly Action<int, float[]> sink;
            private readonly bool identity;
            private float[] previous, current;
            private readonly float[] output;
            private readonly int[] x0, x1;
            private readonly float[] fx;
            private int received, emitted;

            // Разрыв поверхности для Height: доля диапазона высот.
            private const float HeightSeam = .02f;

            public Resampler(int srcW, int srcH, int dstW, int dstH, int channels, Layer layer, Action<int, float[]> sink)
            {
                this.srcW = srcW; this.srcH = srcH; this.dstW = dstW; this.dstH = dstH; this.channels = channels; this.layer = layer; this.sink = sink;
                identity = srcW == dstW && srcH == dstH;
                output = new float[dstW * channels];
                x0 = new int[dstW]; x1 = new int[dstW]; fx = new float[dstW];
                for (int x = 0; x < dstW; x++)
                {
                    double sx = (x + .5) * srcW / dstW - .5;
                    int a = (int)Math.Floor(sx);
                    fx[x] = Mathf.Clamp01((float)(sx - a));
                    x0[x] = Mathf.Clamp(a, 0, srcW - 1);
                    x1[x] = Mathf.Clamp(a + 1, 0, srcW - 1);
                }
            }

            public void Push(int y, float[] row)
            {
                received++;
                if (identity)
                {
                    sink(emitted++, row);
                    return;
                }
                float[] swap = previous ?? new float[row.Length];
                previous = current;
                current = swap;
                Array.Copy(row, current, row.Length);
                int r = received - 1;
                while (emitted < dstH)
                {
                    double sy = (emitted + .5) * srcH / dstH - .5;
                    int a = (int)Math.Floor(sy);
                    int y0 = Mathf.Clamp(a, 0, srcH - 1), y1 = Mathf.Clamp(a + 1, 0, srcH - 1);
                    if (y1 > r) break;
                    float[] rowA = y0 == r || previous == null ? current : previous;
                    float[] rowB = y1 == r || previous == null ? current : previous;
                    Blend(rowA, rowB, Mathf.Clamp01((float)(sy - a)));
                    sink(emitted++, output);
                }
            }

            public void Finish()
            {
                if (received != srcH) throw new InvalidDataException("Картинка прочитана не целиком (" + received + " из " + srcH + " строк).");
                if (emitted != dstH) throw new InvalidDataException("Пересчёт не завершён (" + emitted + " из " + dstH + " строк).");
            }

            private void Blend(float[] a, float[] b, float fy)
            {
                for (int x = 0; x < dstW; x++)
                {
                    int i00 = x0[x] * channels, i10 = x1[x] * channels;
                    float wx = fx[x];
                    float w00 = (1 - wx) * (1 - fy), w10 = wx * (1 - fy), w01 = (1 - wx) * fy, w11 = wx * fy;
                    int o = x * channels;
                    switch (layer)
                    {
                        case Layer.Color:
                        {
                            float alpha = a[i00 + 3] * w00 + a[i10 + 3] * w10 + b[i00 + 3] * w01 + b[i10 + 3] * w11;
                            for (int c = 0; c < 3; c++)
                            {
                                float sum = a[i00 + c] * a[i00 + 3] * w00 + a[i10 + c] * a[i10 + 3] * w10 + b[i00 + c] * b[i00 + 3] * w01 + b[i10 + c] * b[i10 + 3] * w11;
                                output[o + c] = alpha > 1e-6f ? sum / alpha : a[i00 + c] * w00 + a[i10 + c] * w10 + b[i00 + c] * w01 + b[i10 + c] * w11;
                            }
                            output[o + 3] = alpha;
                            break;
                        }
                        case Layer.Normal:
                        {
                            Vector3 n = new Vector3(
                                a[i00] * w00 + a[i10] * w10 + b[i00] * w01 + b[i10] * w11,
                                a[i00 + 1] * w00 + a[i10 + 1] * w10 + b[i00 + 1] * w01 + b[i10 + 1] * w11,
                                a[i00 + 2] * w00 + a[i10 + 2] * w10 + b[i00 + 2] * w01 + b[i10 + 2] * w11);
                            if (n.sqrMagnitude < 1e-8f) n = Vector3.forward;
                            n.Normalize();
                            output[o] = n.x; output[o + 1] = n.y; output[o + 2] = n.z;
                            break;
                        }
                        default:
                        {
                            float sum = 0, weights = 0, cover = 0, low = float.MaxValue, high = float.MinValue, best = 0, bestValue = 0;
                            void Take(float[] row, int i, float w)
                            {
                                cover += row[i + 1] * w;
                                if (row[i + 1] <= 0 || w <= 0) return;
                                sum += row[i] * w; weights += w;
                                low = Math.Min(low, row[i]); high = Math.Max(high, row[i]);
                                if (w > best) { best = w; bestValue = row[i]; }
                            }
                            Take(a, i00, w00); Take(a, i10, w10); Take(b, i00, w01); Take(b, i10, w11);
                            // Разрыв (ступень, край моста): соседей не смешивать — ближайший покрытый.
                            output[o] = weights <= 0 ? 0 : high - low > HeightSeam ? bestValue : sum / weights;
                            output[o + 1] = weights <= 0 ? 0 : Math.Max(cover, 1 / 255f);
                            break;
                        }
                    }
                }
            }
        }

        // Строки карты (рисунок сверху слева, остальное — добивка) → участки.
        private sealed class TileSink
        {
            private readonly Layer layer;
            private readonly int columns, rows, tileW, tileH, bits;
            private readonly double min, max;
            private readonly PngCodec.RowWriter[] writers;
            private readonly ushort[][] values;
            private readonly byte[][] coverage;
            private readonly byte[] line;
            private int next;
            public readonly Dictionary<string, byte[]> Result = new Dictionary<string, byte[]>();

            // bits — разрядность Height (8 — только 8-битная картинка без пересчёта).
            public TileSink(Layer layer, int columns, int rows, int tileW, int tileH, double min, double max, int bits)
            {
                this.layer = layer; this.columns = columns; this.rows = rows; this.tileW = tileW; this.tileH = tileH;
                this.min = min; this.max = max; this.bits = bits == 8 ? 8 : 16;
                writers = new PngCodec.RowWriter[columns];
                values = new ushort[columns][];
                coverage = new byte[columns][];
                line = new byte[columns * tileW * 4];
            }

            // Строка рисунка (y — сверху; ширина — рисунок без добивки).
            public void Content(int y, float[] row) => Emit(row, row.Length / ChannelsOf(layer));

            // Добивка до целых участков — пустые строки.
            public void Finish()
            {
                while (next < rows * tileH) Emit(null, 0);
            }

            private void Emit(float[] row, int width)
            {
                int y = next++;
                if (y >= rows * tileH) throw new InvalidDataException("Строк больше, чем в сетке участков.");
                int band = y / tileH, inBand = y % tileH, tileRow = rows - 1 - band;
                int count = ChannelsOf(layer);
                double rawMax = bits == 8 ? 255.0 : 65535.0;
                for (int column = 0; column < columns; column++)
                {
                    if (inBand == 0)
                    {
                        if (layer == Layer.Height) { values[column] = new ushort[tileW * tileH]; coverage[column] = new byte[tileW * tileH]; }
                        else writers[column] = new PngCodec.RowWriter(tileW, tileH, 8, PngCodec.Rgba, layer == Layer.Color);
                    }
                    for (int x = 0; x < tileW; x++)
                    {
                        int source = column * tileW + x;
                        bool inside = row != null && source < width;
                        int s = source * count;
                        if (layer == Layer.Height)
                        {
                            // Данные высоты — строки снизу вверх.
                            int index = (tileH - 1 - inBand) * tileW + x;
                            float cover = inside ? Mathf.Clamp01(row[s + 1]) : 0;
                            coverage[column][index] = cover > 0 ? (byte)Math.Max(1, Math.Round(cover * 255)) : (byte)0;
                            values[column][index] = cover > 0 ? (ushort)Math.Round(Mathf.Clamp01(row[s]) * rawMax) : (ushort)0;
                            continue;
                        }
                        int t = source * 4;
                        if (layer == Layer.Color)
                        {
                            line[t] = inside ? ToByte(row[s]) : (byte)0; line[t + 1] = inside ? ToByte(row[s + 1]) : (byte)0;
                            line[t + 2] = inside ? ToByte(row[s + 2]) : (byte)0; line[t + 3] = inside ? ToByte(row[s + 3]) : (byte)0;
                        }
                        else
                        {
                            line[t] = inside ? ToByte(row[s] * .5f + .5f) : (byte)128; line[t + 1] = inside ? ToByte(row[s + 1] * .5f + .5f) : (byte)128;
                            line[t + 2] = inside ? ToByte(row[s + 2] * .5f + .5f) : (byte)255; line[t + 3] = 255;
                        }
                    }
                    if (layer != Layer.Height) writers[column].WriteRow(line, column * tileW * 4);
                    if (inBand != tileH - 1) continue;
                    string key = LocationGroundTile.KeyOf(column, tileRow);
                    Result[key] = layer == Layer.Height
                        ? new LocationHeightTileData(tileW, tileH, bits, min, max, values[column], coverage[column]).Serialize()
                        : writers[column].Finish();
                }
            }
        }

        // «X001_Y002» → 1 и 2.
        private static int KeyX(string key) => int.Parse(key.Substring(1, key.IndexOf('_') - 1));
        private static int KeyY(string key) => int.Parse(key.Substring(key.IndexOf('Y') + 1));

        private static byte ToByte(float value) => (byte)Mathf.Clamp(Mathf.RoundToInt(value * 255f), 0, 255);
    }
}

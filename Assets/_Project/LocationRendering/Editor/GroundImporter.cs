using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using KingdomSurvival.ArtAssets.Editor;
using KingdomSurvival.BattlefieldDatabase;
using UnityEditor;
using UnityEngine;

namespace KingdomSurvival.LocationRendering.Editor
{
    // ПР-12О: применение пакета земли к месту Базы локаций — транзакционно.
    // 1) Подготовка вне Assets: повторная проверка sha256, декодирование
    //    высоты в CPU-данные (ushort + покрытие), нормали — переворот
    //    зелёного/выпрямление, если выбрано. Ошибка — ничего не тронуто.
    // 2) Запись в Assets/_Project/Art/Locations/Ground/<место>/<карта>:
    //    имя файла содержит хеш содержимого — новый набор ложится рядом со
    //    старым, тот же manifest повторно ничего не дублирует. Ошибка импорта
    //    — новые файлы удаляются, данные места прежние.
    // 3) Подмена ссылок в данных места (Undo), затем удаление файлов этой
    //    земли, на которые больше никто не ссылается.
    // Объекты, NPC, входы, события и настройки камеры не трогаются.
    public static class GroundImporter
    {
        public const string Root = "Assets/_Project/Art/Locations/Ground";

        public enum TileAction { New, Replace, Unchanged, Skip }

        public sealed class Plan
        {
            public GroundExportPackage Package;
            public string LocationId;
            // Выбранные участки (ключи X000_Y000); пусто — все готовые.
            public HashSet<string> Selected = new HashSet<string>();
            public bool LevelNormals;
            // Размер места ≠ размеру земли: подогнать (точки сохраняют долю рисунка).
            public bool ResizeCanvas = true;
            // Частичный пакет применяется только как предпросмотр.
            public bool AllowPartial;
            // Обрисованные участки сохраняют обрисовку (обновляется только рендер под ней).
            public bool KeepPaint = true;
            public readonly Dictionary<string, TileAction> Actions = new Dictionary<string, TileAction>();
            public readonly List<string> Changes = new List<string>();
            public readonly List<string> Blockers = new List<string>();
            public bool ReplacesWholeGround;
        }

        // ------------------------------------------------------------------
        // Сравнение с текущей землёй места
        // ------------------------------------------------------------------

        public static Plan Analyze(GroundExportPackage package, LocalLocationDefinition location, LocationVisualDefinition visual, IEnumerable<string> selected = null)
        {
            Plan plan = new Plan { Package = package, LocationId = location?.Id };
            if (location == null || visual == null) { plan.Blockers.Add("Выберите место с художественной сборкой (вкладка «Предметы» → «Добавить сборку и свет»)."); return plan; }
            if (!package.CanApply) { plan.Blockers.AddRange(package.Errors.DefaultIfEmpty("В пакете нет ни одного готового Color-участка.")); return plan; }
            plan.Selected = new HashSet<string>(selected ?? package.Tiles.Where(tile => tile.Usable(GroundExportPackage.Color)).Select(tile => tile.Key));
            LocationGroundDefinition ground = visual.Ground ?? new LocationGroundDefinition();
            bool hadTiles = ground.IsTiled && ground.Tiles.Count > 0;
            string incompatible = hadTiles ? Incompatibility(ground, package) : null;
            bool fullSet = package.Tiles.Count == package.Columns * package.Rows &&
                           package.Tiles.All(tile => tile.Usable(GroundExportPackage.Color) && plan.Selected.Contains(tile.Key));
            if (hadTiles && (ground.MapId != package.MapId || incompatible != null))
            {
                if (!fullSet)
                {
                    plan.Blockers.Add("Участки из другого экспорта несовместимы с землёй места «" + ground.MapId + "»" +
                                      (incompatible != null ? " (" + incompatible + ")" : "") +
                                      ". Одинаковая нумерация файлов не доказывает, что участки соседние: экспортируйте их общей сеткой (тот же manifest) " +
                                      "или импортируйте полный набор новой карты, чтобы заменить землю целиком.");
                    return plan;
                }
                plan.ReplacesWholeGround = true;
                plan.Changes.Add("Земля места заменяется целиком: «" + ground.MapId + "» → «" + package.MapId + "»" + (incompatible != null ? " (" + incompatible + ")" : "") + ".");
            }
            else if (!hadTiles)
            {
                plan.ReplacesWholeGround = true;
                plan.Changes.Add(visual.Background != null
                    ? "Прежний «Рисунок места» больше не показывается: земля — из участков (сам рисунок и его ссылка сохраняются)."
                    : "Место получает землю из участков вместо технической заглушки.");
            }

            foreach (GroundExportTile tile in package.Tiles)
            {
                if (!plan.Selected.Contains(tile.Key) || !tile.Usable(GroundExportPackage.Color)) { plan.Actions[tile.Key] = TileAction.Skip; continue; }
                LocationGroundTile current = plan.ReplacesWholeGround ? null : ground.Find(tile.X, tile.Y);
                if (current == null) plan.Actions[tile.Key] = TileAction.New;
                else plan.Actions[tile.Key] = SameContent(current, tile) ? TileAction.Unchanged : TileAction.Replace;
            }
            Vector2 canvas = LocationVisualGeometry.CanvasSize(location);
            if (Mathf.Abs(canvas.x - package.VirtualWidth) > .5f || Mathf.Abs(canvas.y - package.VirtualHeight) > .5f)
                plan.Changes.Add("Размер места " + canvas.x + "×" + canvas.y + " → " + package.VirtualWidth + "×" + package.VirtualHeight +
                                 " (1 пиксель земли = 1 пиксель места). Входы, объекты, противники и зоны сохранят долю рисунка; разметка пересчитается.");
            if (hadTiles && !plan.ReplacesWholeGround && Math.Abs(ground.HeightMax - package.HeightMax) + Math.Abs(ground.HeightMin - package.HeightMin) > 1e-9 && package.Requested(GroundExportPackage.Height))
                plan.Changes.Add("Height Range " + ground.HeightMin + "…" + ground.HeightMax + " → " + package.HeightMin + "…" + package.HeightMax + ".");
            if (Math.Abs(package.UnityPpu - LocationVisualGeometry.PixelsPerUnit) > .01)
                plan.Changes.Add("PPU экспорта " + package.UnityPpu + ", в местах — " + LocationVisualGeometry.PixelsPerUnit +
                                 ": масштаб людей задаёт «Ширина кадра боя», земля не пересчитывается.");
            int count(TileAction action) => plan.Actions.Values.Count(value => value == action);
            plan.Changes.Add("Участки: новых " + count(TileAction.New) + ", заменить " + count(TileAction.Replace) + ", без изменений " +
                             count(TileAction.Unchanged) + ", пропустить " + count(TileAction.Skip) + ".");
            int painted = plan.ReplacesWholeGround ? 0 : ground.Tiles.Count(tile => tile != null && tile.IsPainted &&
                plan.Actions.TryGetValue(tile.Key, out TileAction action) && action == TileAction.Replace);
            if (painted > 0 && !package.ColorPainted)
                plan.Changes.Add("Обрисовка Color сохраняется у " + painted + " заменяемых участков (обновится рендер под ней, Normal и Height). " +
                                 "Снимите «Сохранить обрисовку Color», чтобы вернуть Color из нового рендера.");
            if (package.ColorPainted)
                plan.Changes.Add("Color — обрисовка из файла экспорта «" + package.ColorFile + "»: ляжет на все выбранные участки" +
                                 (Math.Abs(package.ColorScale - 1) > 1e-6 ? " с разрешением ×" + package.ColorScale.ToString("0.###") : "") + ".");
            else if (plan.ReplacesWholeGround && ground.IsPainted)
                plan.Changes.Add("Обрисовка Color прежней земли не переносится на новую карту.");
            return plan;
        }

        // Совместимость с уже собранной землёй: тот же базис, origin, масштаб,
        // размер участка, сетка, высота и нормали. null — совместимо.
        public static string Incompatibility(LocationGroundDefinition ground, GroundExportPackage package)
        {
            if (ground.Columns != package.Columns || ground.Rows != package.Rows) return "сетка " + ground.Columns + "×" + ground.Rows + " ≠ " + package.Columns + "×" + package.Rows;
            if (ground.TileWidth != package.TileWidth || ground.TileHeight != package.TileHeight) return "размер участка";
            if (Math.Abs(ground.ProjectionQ - package.Q) > Math.Max(1e-9, package.Q * 1e-6)) return "масштаб проекции q";
            if (Math.Abs(ground.Origin.x - package.Origin[0]) > package.Q * 1e-3 || Math.Abs(ground.Origin.y - package.Origin[1]) > package.Q * 1e-3) return "начало сетки (origin)";
            if (!Same(ground.BasisRight, package.Basis[0]) || !Same(ground.BasisUp, package.Basis[1]) || !Same(ground.BasisBack, package.Basis[2])) return "базис камеры";
            if (package.Requested(GroundExportPackage.Height) && ground.HasAnyHeight &&
                (Math.Abs(ground.HeightMin - package.HeightMin) > 1e-9 || Math.Abs(ground.HeightMax - package.HeightMax) > 1e-9 || ground.HeightBitDepth != package.HeightBits))
                return "Height Range или разрядность высоты";
            if (package.Requested(GroundExportPackage.Normal) && ground.NormalGreenInverted != package.InvertGreen) return "соглашение зелёного канала нормалей";
            return null;
        }

        private static bool Same(Vector3 axis, double[] value) =>
            value != null && value.Length == 3 && Math.Abs(axis.x - value[0]) < 1e-5 && Math.Abs(axis.y - value[1]) < 1e-5 && Math.Abs(axis.z - value[2]) < 1e-5;

        // Тот же участок: у каждого готового прохода есть ассет с тем же sha256 исходника.
        private static bool SameContent(LocationGroundTile current, GroundExportTile tile)
        {
            bool Same(string have, GroundExportPass pass, UnityEngine.Object asset) =>
                pass == null || !pass.Usable || (asset != null && string.Equals(have, pass.Sha256, StringComparison.OrdinalIgnoreCase));
            return Same(current.ColorSha256, tile.Pass(GroundExportPackage.Color), current.Color) &&
                   Same(current.NormalSha256, tile.Pass(GroundExportPackage.Normal), current.Normal) &&
                   Same(current.HeightSha256, tile.Pass(GroundExportPackage.Height), current.Height);
        }

        // ------------------------------------------------------------------
        // Применение
        // ------------------------------------------------------------------

        private sealed class Staged
        {
            public GroundExportTile Tile;
            public string ColorPath, NormalPath, HeightPath;
            public byte[] Color, Normal, Height;
            public string ColorSha, NormalSha, HeightSha;
        }

        // Возвращает текст результата; при ошибке данные места не меняются.
        public static bool Apply(Plan plan, LocalLocationDatabaseAsset database, Action<LocalLocationDefinition, float, float> resizeCanvas, out string message)
        {
            message = string.Empty;
            GroundExportPackage package = plan.Package;
            LocalLocationDefinition location = database.locations.Find(item => item.Id == plan.LocationId);
            LocationVisualDefinition visual = database.FindVisual(plan.LocationId);
            if (plan.Blockers.Count > 0) { message = string.Join("\n", plan.Blockers); return false; }
            if (location == null || visual == null) { message = "Место не найдено."; return false; }
            List<GroundExportTile> work = package.Tiles.Where(tile => plan.Actions.TryGetValue(tile.Key, out TileAction action) &&
                                                                     (action == TileAction.New || action == TileAction.Replace)).ToList();
            if (WouldBeIncomplete(plan, visual.Ground) && !plan.AllowPartial)
            {
                message = "После импорта земля будет неполной (нет Color или проходов у части участков): применить можно только как частичный предпросмотр.";
                return false;
            }

            string folder = Root + "/" + Sanitize(location.Id) + "/" + Sanitize(package.MapId);
            List<Staged> staged = new List<Staged>();
            // 1. Подготовка вне Assets.
            try
            {
                int index = 0;
                foreach (GroundExportTile tile in work)
                {
                    if (EditorUtility.DisplayCancelableProgressBar("Земля из Blender", "Подготовка участка " + tile.Key, index++ / (float)Math.Max(1, work.Count)))
                    {
                        message = "Импорт отменён. Место не изменено.";
                        return false;
                    }
                    staged.Add(Stage(tile, plan, folder));
                }
            }
            catch (Exception error) when (error is IOException || error is InvalidDataException || error is UnauthorizedAccessException || error is ArgumentException)
            {
                message = "Импорт остановлен: " + error.Message + "\nМесто не изменено.";
                return false;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            // 2. Запись в Assets (новые файлы рядом со старыми).
            List<string> created = new List<string>();
            int paintBehindRender = 0;
            bool sameView = false, recrop = false;
            Rect cropPlane = default;
            try
            {
                Directory.CreateDirectory(folder + "/Color");
                if (staged.Any(item => item.Normal != null)) Directory.CreateDirectory(folder + "/Normal");
                if (staged.Any(item => item.Height != null)) Directory.CreateDirectory(folder + "/Height");
                string manifestPath = package.Manual ? null : folder + "/" + Sanitize(package.MapId) + "_manifest.json";
                AssetDatabase.StartAssetEditing();
                try
                {
                    foreach (Staged item in staged)
                    {
                        Write(item.ColorPath, item.Color, created);
                        Write(item.NormalPath, item.Normal, created);
                        Write(item.HeightPath, item.Height, created);
                    }
                    if (manifestPath != null) WriteText(manifestPath, package.ManifestText, created);
                }
                finally
                {
                    AssetDatabase.StopAssetEditing();
                }
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                int done = 0;
                foreach (Staged item in staged)
                {
                    EditorUtility.DisplayProgressBar("Земля из Blender", "Настройки импорта " + item.Tile.Key, done++ / (float)Math.Max(1, staged.Count));
                    if (item.NormalPath != null) ConfigureNormal(item.NormalPath, Math.Max(package.TileWidth, package.TileHeight));
                    ConfigureColor(item.ColorPath, item.NormalPath, (int)Math.Ceiling(Math.Max(package.TileWidth, package.TileHeight) * package.ColorScale));
                }
                foreach (Staged item in staged)
                {
                    if (AssetDatabase.LoadAssetAtPath<Sprite>(item.ColorPath) == null) throw new IOException("Unity не импортировал " + item.ColorPath + " как Sprite.");
                    if (item.NormalPath != null && AssetDatabase.LoadAssetAtPath<Texture2D>(item.NormalPath) == null) throw new IOException("Unity не импортировал " + item.NormalPath + ".");
                    if (item.HeightPath != null && AssetDatabase.LoadAssetAtPath<TextAsset>(item.HeightPath) == null) throw new IOException("Unity не импортировал " + item.HeightPath + ".");
                }

                // 3. Подмена ссылок.
                paintBehindRender = 0;
                Undo.RecordObject(database, "Импорт земли из Blender");
                LocationGroundDefinition ground = visual.Ground ?? (visual.Ground = new LocationGroundDefinition());
                // Прежняя земля из Blender с тем же ракурсом и масштабом: раскладка
                // места переносится сдвигом по плоскости камеры, обрезка повторяется.
                bool oldTiled = ground.IsTiled && ground.Tiles.Count > 0;
                sameView = oldTiled && SameView(ground, package) && LocationGroundGrid.For(ground, location).IsOneToOne;
                Vector2 oldOrigin = ground.Origin;
                int oldHeight = ground.Rows * ground.TileHeight;
                recrop = sameView && plan.ReplacesWholeGround && ground.Cropped && ground.CropOnReimport;
                cropPlane = ground.CropPlane;
                if (plan.ReplacesWholeGround) { ground.Tiles.Clear(); ground.Cropped = false; }
                WriteHeader(ground, package, plan, manifestPath);
                foreach (Staged item in staged)
                {
                    LocationGroundTile tile = ground.Find(item.Tile.X, item.Tile.Y);
                    if (tile == null) { tile = new LocationGroundTile { X = item.Tile.X, Y = item.Tile.Y }; ground.Tiles.Add(tile); }
                    if (tile.IsPainted && plan.KeepPaint && !package.ColorPainted)
                    {
                        // Обрисовка остаётся Color; новый рендер — её основа для возврата.
                        if (!string.Equals(tile.ColorSha256, item.ColorSha, StringComparison.OrdinalIgnoreCase)) paintBehindRender++;
                        tile.RenderColorGuid = AssetDatabase.AssetPathToGUID(item.ColorPath);
                    }
                    else
                    {
                        tile.Color = AssetDatabase.LoadAssetAtPath<Sprite>(item.ColorPath);
                        // Color, обрисованный прямо в файле экспорта, — сразу обрисовка (рендера под ней нет).
                        tile.PaintSha256 = package.ColorPainted ? item.ColorSha : string.Empty;
                        tile.RenderColorGuid = string.Empty;
                    }
                    tile.ColorSha256 = item.ColorSha; tile.ColorRevision = item.Tile.Pass(GroundExportPackage.Color)?.Revision ?? string.Empty;
                    if (item.NormalPath != null)
                    {
                        tile.Normal = AssetDatabase.LoadAssetAtPath<Texture2D>(item.NormalPath);
                        tile.NormalSha256 = item.NormalSha; tile.NormalRevision = item.Tile.Pass(GroundExportPackage.Normal)?.Revision ?? string.Empty;
                        if (tile.IsPainted) SpriteNormalMaps.Assign(tile.Color, tile.Normal, out _);
                    }
                    if (item.HeightPath != null)
                    {
                        tile.Height = AssetDatabase.LoadAssetAtPath<TextAsset>(item.HeightPath);
                        tile.HeightSha256 = item.HeightSha; tile.HeightRevision = item.Tile.Pass(GroundExportPackage.Height)?.Revision ?? string.Empty;
                    }
                }
                ground.Tiles.Sort((a, b) => a.Y != b.Y ? a.Y.CompareTo(b.Y) : a.X.CompareTo(b.X));
                if (package.ColorPainted) { ground.ColorScale = package.ColorScale; ground.PaintSource = package.ColorFile; ground.PaintedUtc = DateTime.UtcNow.ToString("u"); }
                else if (!ground.IsPainted) { ground.ColorScale = 1; ground.PaintSource = ground.PaintedUtc = string.Empty; }
                ground.HeightEnabled = ground.HasAnyHeight;
                ground.Incomplete = IsIncomplete(ground);
                if (visual.Camera == null || visual.Camera.Version == 0) visual.Camera = LocationCameraSettings.ForLargeMap();
                Vector2 canvas = LocationVisualGeometry.CanvasSize(location);
                bool resized = Mathf.Abs(canvas.x - package.VirtualWidth) > .5f || Mathf.Abs(canvas.y - package.VirtualHeight) > .5f;
                if (plan.ResizeCanvas && sameView)
                {
                    // Та же плоскость: точка земли (x, y) → новая карта сдвигом, без растяжения.
                    Vector2 offset = new Vector2((float)((package.Origin[0] - oldOrigin.x) / package.Q),
                        (float)(oldHeight - package.VirtualHeight + (oldOrigin.y - package.Origin[1]) / package.Q));
                    if (resized || offset.sqrMagnitude > 1e-4f)
                        LocationRebase.Translate(location, visual, offset, new Vector2(package.VirtualWidth, package.VirtualHeight));
                }
                else if (plan.ResizeCanvas && resized)
                    resizeCanvas(location, package.VirtualWidth, package.VirtualHeight);
                EditorUtility.SetDirty(database);
                if (AssetDatabase.Contains(database)) AssetDatabase.SaveAssetIfDirty(database);
            }
            catch (Exception error)
            {
                foreach (string path in created) AssetDatabase.DeleteAsset(path);
                message = "Импорт не применён: " + error.Message + "\nНовые файлы удалены, место не изменено.";
                Debug.LogException(error);
                return false;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            int removed = CleanupUnreferenced(location.Id, visual.Ground);
            int unchanged = plan.Actions.Values.Count(action => action == TileAction.Unchanged);
            message = "Земля «" + package.MapId + "»: обновлено участков " + staged.Count + (unchanged > 0 ? ", без изменений " + unchanged : "") +
                      (removed > 0 ? ", удалено устаревших файлов " + removed : "") +
                      (paintBehindRender > 0 ? ". Рендер Color изменился у " + paintBehindRender + " обрисованных участков — обрисовка сохранена; сохраните Color всей карты и проверьте её" : "") +
                      (visual.Ground.Incomplete ? ". Земля неполная — только предпросмотр, в игру не пойдёт." : ".");
            if (recrop)
            {
                // Прежняя обрезка — по той же рамке плоскости Blender.
                visual.Ground.CropPlane = cropPlane;
                GroundCrop.Plan crop = GroundCrop.Analyze(visual.Ground, location, GroundCrop.CanvasRectFromPlane(visual.Ground));
                if (crop.CanApply && GroundCrop.Apply(crop, database, location.Id, out string cropMessage))
                    message += " Обрезка повторена: " + cropMessage;
                else
                    message += " Обрезка не повторена: " + string.Join(" ", crop.Errors) + " — обрежьте заново во вкладке «Земля».";
            }
            return true;
        }

        private static bool SameView(LocationGroundDefinition ground, GroundExportPackage package) =>
            !package.Manual && ground.ProjectionQ > 0 && package.Q > 0 && Math.Abs(ground.ProjectionQ - package.Q) <= package.Q * 1e-5 &&
            Same(ground.BasisRight, package.Basis[0]) && Same(ground.BasisUp, package.Basis[1]) && Same(ground.BasisBack, package.Basis[2]);

        // Итог импорта: у каждого участка сетки будет Color, а у выбранных —
        // все заказанные проходы? Иначе — только частичный предпросмотр.
        public static bool WouldBeIncomplete(Plan plan, LocationGroundDefinition current)
        {
            GroundExportPackage package = plan.Package;
            for (int y = 0; y < package.Rows; y++)
            {
                for (int x = 0; x < package.Columns; x++)
                {
                    GroundExportTile tile = package.Find(x, y);
                    bool imported = tile != null && plan.Actions.TryGetValue(tile.Key, out TileAction action) && action != TileAction.Skip;
                    if (imported && !package.TileReady(tile)) return true;
                    bool kept = !plan.ReplacesWholeGround && current?.Find(x, y)?.Color != null;
                    if (!imported && !kept) return true;
                }
            }
            return false;
        }

        private static Staged Stage(GroundExportTile tile, Plan plan, string folder)
        {
            GroundExportPackage package = plan.Package;
            Staged staged = new Staged { Tile = tile };
            string name = package.Manual || package.Single ? Sanitize(package.MapId) + (package.Columns * package.Rows > 1 ? "_" + tile.Key : "") : Sanitize(package.MapId) + "_" + tile.Key;
            staged.Color = ReadVerified(tile.Pass(GroundExportPackage.Color));
            staged.ColorSha = tile.Pass(GroundExportPackage.Color).Sha256;
            staged.ColorPath = folder + "/Color/" + name + "__" + staged.ColorSha.Substring(0, 10) + ".png";
            GroundExportPass normal = tile.Pass(GroundExportPackage.Normal);
            if (normal != null && normal.Usable)
            {
                byte[] bytes = ReadVerified(normal);
                staged.NormalSha = normal.Sha256;
                byte[] fixedBytes = FixNormals(bytes, package.InvertGreen, plan.LevelNormals ? package.NormalTiltDegrees : 0);
                staged.Normal = fixedBytes ?? bytes;
                string tag = (fixedBytes != null ? GroundExportPackage.Sha256Hex(staged.Normal) : staged.NormalSha).Substring(0, 10);
                staged.NormalPath = folder + "/Normal/" + name + "_normal__" + tag + ".png";
            }
            GroundExportPass height = tile.Pass(GroundExportPackage.Height);
            if (height != null && height.Usable)
            {
                byte[] bytes = ReadVerified(height);
                LocationHeightTileData data = LocationHeightTileData.FromPng(bytes, package.HeightMin, package.HeightMax);
                if (data.BitDepth != package.HeightBits) throw new InvalidDataException(tile.Key + ": Height " + data.BitDepth + " бит вместо " + package.HeightBits + ".");
                staged.Height = data.Serialize();
                staged.HeightSha = height.Sha256;
                staged.HeightPath = folder + "/Height/" + name + "_height__" + height.Sha256.Substring(0, 10) + ".bytes";
            }
            return staged;
        }

        // Файл перечитывается: между проверкой и применением он мог измениться.
        private static byte[] ReadVerified(GroundExportPass pass)
        {
            byte[] bytes = File.ReadAllBytes(pass.FullPath);
            if (!string.Equals(GroundExportPackage.Sha256Hex(bytes), pass.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new IOException(pass.Path + " изменился после проверки (sha256). Проверьте пакет заново.");
            return bytes;
        }

        // Зелёный инвертирован в экспорте → вернуть Y вверх (один раз);
        // выпрямление — поворот нормалей на наклон камеры из manifest.
        // null — файл без изменений.
        public static byte[] FixNormals(byte[] png, bool flipGreen, float levelDegrees)
        {
            if (!flipGreen && Mathf.Abs(levelDegrees) < GroundNormals.LevelTolerance) return null;
            ushort[] samples = PngCodec.Decode(png, out PngCodec.Header header);
            int channels = header.Channels;
            if (channels < 3) throw new InvalidDataException("Карта нормалей без RGB.");
            double max = header.BitDepth == 16 ? 65535 : 255;
            float angle = Mathf.Abs(levelDegrees) < GroundNormals.LevelTolerance ? 0 : levelDegrees * Mathf.Deg2Rad;
            float cos = Mathf.Cos(angle), sin = Mathf.Sin(angle);
            for (int i = 0; i < samples.Length; i += channels)
            {
                Vector3 n = new Vector3((float)(samples[i] / max * 2 - 1), (float)(samples[i + 1] / max * 2 - 1), (float)(samples[i + 2] / max * 2 - 1));
                if (flipGreen) n.y = -n.y;
                if (angle != 0) n = new Vector3(n.x, n.y * cos - n.z * sin, n.y * sin + n.z * cos);
                if (n.sqrMagnitude < 1e-8f) n = Vector3.forward;
                n.Normalize();
                samples[i] = (ushort)Math.Round((n.x * .5 + .5) * max);
                samples[i + 1] = (ushort)Math.Round((n.y * .5 + .5) * max);
                samples[i + 2] = (ushort)Math.Round((n.z * .5 + .5) * max);
            }
            return PngCodec.Encode(header.Width, header.Height, header.BitDepth, header.ColorType, samples);
        }

        private static void Write(string path, byte[] bytes, List<string> created)
        {
            if (path == null || bytes == null) return;
            if (File.Exists(path))
            {
                // Тот же хеш в имени — то же содержимое: файл и его GUID остаются.
                if (new FileInfo(path).Length == bytes.Length) return;
            }
            else created.Add(path);
            File.WriteAllBytes(path, bytes);
        }

        private static void WriteText(string path, string text, List<string> created)
        {
            if (!File.Exists(path)) created.Add(path);
            else if (File.ReadAllText(path) == text) return;
            File.WriteAllText(path, text);
        }

        // Земля: Sprite на полный прямоугольник (без tight mesh и обрезки по
        // альфе), без mipmaps и уменьшения, Clamp — согласованно у всех участков.
        internal static void ConfigureColor(string path, string normalPath, int largestSide)
        {
            TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = LocationVisualGeometry.PixelsPerUnit;
            importer.sRGBTexture = true;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.maxTextureSize = MaxSize(largestSide);
            importer.npotScale = TextureImporterNPOTScale.None;
            TextureImporterSettings settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect;
            settings.spriteAlignment = (int)SpriteAlignment.Center;
            settings.spriteExtrude = 0;
            settings.spriteGenerateFallbackPhysicsShape = false;
            importer.SetTextureSettings(settings);
            Texture2D normal = normalPath != null ? AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath) : null;
            List<SecondarySpriteTexture> textures = (importer.secondarySpriteTextures ?? Array.Empty<SecondarySpriteTexture>())
                .Where(item => item.name != SpriteNormalMaps.SecondaryName).ToList();
            if (normal != null) textures.Add(new SecondarySpriteTexture { name = SpriteNormalMaps.SecondaryName, texture = normal });
            importer.secondarySpriteTextures = textures.ToArray();
            string guid = normal != null ? AssetDatabase.AssetPathToGUID(normalPath) : null;
            if (!SpriteNormalMaps.SaveVerified(importer, meta => (guid == null || meta.Contains(guid)) && meta.Contains("spriteMeshType: 0")))
                throw new IOException("Не записаны настройки импорта " + path + ".");
        }

        internal static void ConfigureNormal(string path, int largestSide)
        {
            TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.NormalMap;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.maxTextureSize = MaxSize(largestSide);
            importer.npotScale = TextureImporterNPOTScale.None;
            if (!SpriteNormalMaps.SaveVerified(importer, meta => SpriteNormalMaps.MetaValue(meta, "textureType") == ((int)TextureImporterType.NormalMap).ToString()))
                throw new IOException("Не записаны настройки импорта " + path + ".");
        }

        private static int MaxSize(int largestSide)
        {
            int size = 32;
            while (size < largestSide && size < 16384) size *= 2;
            return Math.Max(2048, size);
        }

        private static void WriteHeader(LocationGroundDefinition ground, GroundExportPackage package, Plan plan, string manifestPath)
        {
            ground.Version = LocationGroundDefinition.CurrentVersion;
            ground.Mode = LocationGroundMode.Tiles;
            ground.MapId = package.MapId;
            ground.RevisionId = package.RevisionId;
            ground.ExporterVersion = package.Manual ? "вручную" : package.ExporterVersion;
            ground.ConfigurationHash = package.ConfigurationHash;
            ground.ExportStatus = package.Status;
            ground.SurfaceContract = package.SurfaceContract;
            ground.ImportedUtc = DateTime.UtcNow.ToString("u");
            ground.Manifest = manifestPath != null ? AssetDatabase.LoadAssetAtPath<TextAsset>(manifestPath) : null;
            ground.Warnings = package.Warnings.ToList();
            ground.Columns = package.Columns; ground.Rows = package.Rows;
            ground.TileWidth = package.TileWidth; ground.TileHeight = package.TileHeight;
            ground.ExportPpu = (float)package.UnityPpu;
            ground.Origin = new Vector2((float)package.Origin[0], (float)package.Origin[1]);
            ground.ProjectionQ = (float)package.Q;
            ground.Reference = ToVector(package.Reference);
            ground.BasisRight = ToVector(package.Basis[0]); ground.BasisUp = ToVector(package.Basis[1]); ground.BasisBack = ToVector(package.Basis[2]);
            ground.UsefulPixels = package.UsefulPixels;
            if (package.Requested(GroundExportPackage.Height))
            {
                ground.HeightMin = package.HeightMin; ground.HeightMax = package.HeightMax;
                ground.HeightBitDepth = package.HeightBits;
            }
            ground.MetersPerBlenderUnit = package.MetersPerBlenderUnit;
            ground.NormalGreenInverted = package.InvertGreen;
            ground.NormalLevelDegrees = plan.LevelNormals ? package.NormalTiltDegrees : 0;
            if (package.CharacterHeightPx > 0) ground.ExportCharacterPx = (float)package.CharacterHeightPx;
        }

        private static Vector3 ToVector(double[] value) => value != null && value.Length == 3 ? new Vector3((float)value[0], (float)value[1], (float)value[2]) : Vector3.zero;

        // Неполная земля: нет Color у участка сетки или нормали есть не у всех.
        public static bool IsIncomplete(LocationGroundDefinition ground)
        {
            bool anyNormal = ground.Tiles.Any(tile => tile?.Normal != null);
            for (int y = 0; y < ground.Rows; y++)
                for (int x = 0; x < ground.Columns; x++)
                {
                    LocationGroundTile tile = ground.Find(x, y);
                    if (tile?.Color == null || (anyNormal && tile.Normal == null)) return true;
                }
            return false;
        }

        // Файлы этой земли, на которые больше нет ссылок, — удалить.
        public static int CleanupUnreferenced(string locationId, LocationGroundDefinition ground)
        {
            string folder = Root + "/" + Sanitize(locationId);
            if (!AssetDatabase.IsValidFolder(folder)) return 0;
            HashSet<string> used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (LocationGroundTile tile in ground?.Tiles ?? new List<LocationGroundTile>())
            {
                if (tile == null) continue;
                foreach (UnityEngine.Object asset in new UnityEngine.Object[] { tile.Color != null ? tile.Color.texture : null, tile.Normal, tile.Height })
                    if (asset != null) used.Add(AssetDatabase.GetAssetPath(asset));
            }
            if (ground?.Manifest != null) used.Add(AssetDatabase.GetAssetPath(ground.Manifest));
            // Рендер Color под обрисовкой — основа для возврата.
            foreach (LocationGroundTile tile in ground?.Tiles ?? new List<LocationGroundTile>())
                if (!string.IsNullOrEmpty(tile?.RenderColorGuid)) used.Add(AssetDatabase.GUIDToAssetPath(tile.RenderColorGuid));
            int removed = 0;
            foreach (string guid in AssetDatabase.FindAssets(string.Empty, new[] { folder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (AssetDatabase.IsValidFolder(path) || used.Contains(path)) continue;
                if (AssetDatabase.DeleteAsset(path)) removed++;
            }
            return removed;
        }

        public static string Sanitize(string value)
        {
            string result = Regex.Replace(value ?? string.Empty, @"[^\w\-.]", "_");
            return string.IsNullOrEmpty(result) ? "ground" : result;
        }

        // ------------------------------------------------------------------
        // Ручной комплект без manifest
        // ------------------------------------------------------------------

        public sealed class ManualOptions
        {
            public int Columns, Rows;
            public double HeightMin, HeightMax = 1;
            public double MetersPerBlenderUnit = 1;
            // Строки PNG сверху вниз (как у экспортёра); false — снизу вверх.
            public bool RowsTopToBottom = true;
            // Индексы участков от нижнего левого (как у экспортёра); false — от верхнего левого.
            public bool IndicesFromBottom = true;
            public bool InvertGreen;
            // Автор подтвердил размер, разрядность, диапазон и ориентацию.
            public bool Confirmed;
        }

        private static readonly Regex TileName = new Regex(@"^(?<base>.+?)_X(?<x>\d{3,})_Y(?<y>\d{3,})(?<kind>_normal|_height)?$", RegexOptions.IgnoreCase);

        // Сопоставление PNG по именам: base_X###_Y###[ _normal | _height ].png
        // или один участок base.png / base_normal.png / base_height.png.
        // Неоднозначное (несколько карт, повторы) — ошибка, а не догадка.
        public static GroundExportPackage FromFiles(IEnumerable<string> files, ManualOptions options)
        {
            GroundExportPackage package = new GroundExportPackage { Manual = true, Status = "complete", Schema = 1, SurfaceContract = "matched_ground" };
            List<string> pngs = files.Where(path => path.EndsWith(".png", StringComparison.OrdinalIgnoreCase) && File.Exists(path)).Select(Path.GetFullPath).Distinct().ToList();
            if (pngs.Count == 0) { package.Errors.Add("Нет PNG."); return package; }
            Dictionary<string, Dictionary<string, string>> byKey = new Dictionary<string, Dictionary<string, string>>();
            HashSet<string> bases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string path in pngs)
            {
                string stem = Path.GetFileNameWithoutExtension(path);
                Match match = TileName.Match(stem);
                string kind, key, baseName;
                if (match.Success)
                {
                    baseName = match.Groups["base"].Value;
                    key = int.Parse(match.Groups["x"].Value) + "," + int.Parse(match.Groups["y"].Value);
                    kind = KindOf(match.Groups["kind"].Value);
                }
                else
                {
                    kind = stem.EndsWith("_normal", StringComparison.OrdinalIgnoreCase) ? GroundExportPackage.Normal
                        : stem.EndsWith("_height", StringComparison.OrdinalIgnoreCase) ? GroundExportPackage.Height : GroundExportPackage.Color;
                    baseName = kind == GroundExportPackage.Color ? stem : stem.Substring(0, stem.Length - 7);
                    key = "0,0";
                }
                bases.Add(baseName);
                if (!byKey.TryGetValue(key, out Dictionary<string, string> set)) byKey[key] = set = new Dictionary<string, string>();
                if (set.ContainsKey(kind)) { package.Errors.Add("Неоднозначно: два файла " + kind + " для участка " + key + " (" + Path.GetFileName(set[kind]) + ", " + Path.GetFileName(path) + ")."); continue; }
                set[kind] = path;
            }
            if (bases.Count > 1) package.Errors.Add("Файлы разных карт: " + string.Join(", ", bases) + ". Выберите файлы одной карты.");
            package.MapId = bases.FirstOrDefault() ?? "ground";
            List<int[]> indices = byKey.Keys.Select(key => key.Split(',').Select(int.Parse).ToArray()).ToList();
            int maxX = indices.Max(i => i[0]), maxY = indices.Max(i => i[1]);
            package.Columns = options.Columns > 0 ? options.Columns : maxX + 1;
            package.Rows = options.Rows > 0 ? options.Rows : maxY + 1;
            package.HeightMin = options.HeightMin; package.HeightMax = options.HeightMax; package.HeightBits = 16;
            package.MetersPerBlenderUnit = options.MetersPerBlenderUnit;
            package.UnityPpu = LocationVisualGeometry.PixelsPerUnit;
            package.InvertGreen = options.InvertGreen;
            package.Q = 1;
            package.RequestedPasses.Add(GroundExportPackage.Color);
            if (byKey.Values.Any(set => set.ContainsKey(GroundExportPackage.Normal))) package.RequestedPasses.Add(GroundExportPackage.Normal);
            if (byKey.Values.Any(set => set.ContainsKey(GroundExportPackage.Height))) package.RequestedPasses.Add(GroundExportPackage.Height);
            if (package.Requested(GroundExportPackage.Height) && !(options.HeightMax > options.HeightMin))
                package.Errors.Add("Задайте Height Range: max больше min (абсолютная Z Blender, общий для всех участков).");
            if (!options.Confirmed)
                package.Errors.Add("Подтвердите размер, разрядность, Height Range и ориентацию: без manifest они не проверяются экспортёром.");
            if (!options.RowsTopToBottom)
                package.Errors.Add("Перевёрнутые по вертикали изображения не поддержаны: сохраните PNG в обычном порядке строк (сверху вниз) — " +
                                   "импорт переворачивает строки ровно один раз.");

            foreach (KeyValuePair<string, Dictionary<string, string>> entry in byKey)
            {
                int[] xy = entry.Key.Split(',').Select(int.Parse).ToArray();
                int y = options.IndicesFromBottom ? xy[1] : package.Rows - 1 - xy[1];
                if (xy[0] >= package.Columns || y < 0 || y >= package.Rows) { package.Errors.Add("Участок " + entry.Key + " вне сетки."); continue; }
                GroundExportTile tile = new GroundExportTile { X = xy[0], Y = y, Status = "complete" };
                foreach (string kind in GroundExportPackage.Kinds)
                {
                    GroundExportPass pass = new GroundExportPass { Kind = kind };
                    if (entry.Value.TryGetValue(kind, out string path))
                    {
                        pass.Status = "complete";
                        pass.FullPath = path;
                        pass.Path = Path.GetFileName(path);
                    }
                    else pass.Status = package.Requested(kind) ? "missing" : "not_requested";
                    tile.Passes[kind] = pass;
                }
                package.Tiles.Add(tile);
            }
            if (package.Find(0, 0) == null && package.Tiles.Count == 0) package.Errors.Add("Не найдено ни одного участка.");
            VerifyManual(package, options);
            return package;
        }

        private static string KindOf(string suffix) =>
            suffix.Equals("_normal", StringComparison.OrdinalIgnoreCase) ? GroundExportPackage.Normal
            : suffix.Equals("_height", StringComparison.OrdinalIgnoreCase) ? GroundExportPackage.Height : GroundExportPackage.Color;

        private static void VerifyManual(GroundExportPackage package, ManualOptions options)
        {
            foreach (GroundExportTile tile in package.Tiles)
            {
                foreach (GroundExportPass pass in tile.Passes.Values)
                {
                    if (pass.Status != "complete") { if (pass.Status == "missing") pass.Problem = "нет файла"; continue; }
                    byte[] bytes = File.ReadAllBytes(pass.FullPath);
                    pass.Sha256 = GroundExportPackage.Sha256Hex(bytes);
                    if (!PngCodec.TryReadHeader(bytes, out PngCodec.Header header, out string problem)) { pass.Problem = problem; continue; }
                    pass.Png = header;
                    if (package.TileWidth == 0) { package.TileWidth = header.Width; package.TileHeight = header.Height; }
                    if (header.Width != package.TileWidth || header.Height != package.TileHeight) { pass.Problem = "размер " + header.Width + "×" + header.Height + " отличается от других участков."; continue; }
                    if (pass.Kind != GroundExportPackage.Color && !PngCodec.IsSupported(header, out problem)) { pass.Problem = problem; continue; }
                    if (pass.Kind == GroundExportPackage.Height)
                    {
                        package.HeightBits = header.BitDepth;
                        if (header.BitDepth == 8) package.Warnings.Add("Height " + pass.Path + " — 8 бит: точность " + ((package.HeightMax - package.HeightMin) / 255).ToString("0.####") + " BU.");
                    }
                    pass.Usable = true;
                }
            }
            package.Notes.Add("Ручной комплект: PPU мест " + LocationVisualGeometry.PixelsPerUnit + ", участок " + package.TileWidth + "×" + package.TileHeight +
                              ", индексы " + (options.IndicesFromBottom ? "снизу слева" : "сверху слева") + ".");
        }
    }
}

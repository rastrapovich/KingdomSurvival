using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KingdomSurvival.ArtAssets.Editor;
using KingdomSurvival.BattlefieldDatabase;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace KingdomSurvival.LocationRendering.Editor
{
    // ПР-12О: вкладка «Земля» — слои Color / Normal / Height по отдельности
    // (заменить картинкой любого размера, сохранить целиком, удалить), импорт
    // экспорта Blender (KS Ground Renderer, manifest schema 1 и 2), карта
    // высот, камера места и проверка. Предпросмотр: Color / Normal / Height,
    // сетка участков с индексами, высота под указателем.
    public sealed partial class LocationDatabaseWindow
    {
        private enum GroundView { Color, Normal, Height }

        private GroundExportPackage pendingPackage;
        private bool pendingKeepPaint = true;
        private GroundImporter.Plan pendingPlan;
        private HashSet<string> pendingSelection;
        private bool pendingLevelNormals, pendingResize = true;
        private GroundView groundView;
        private bool showGroundGrid = true, showCameraFrame = true;
        private readonly GroundImporter.ManualOptions manualOptions = new GroundImporter.ManualOptions();
        private List<string> groundCheck = new List<string>();
        private readonly Dictionary<string, Sprite> groundPreview = new Dictionary<string, Sprite>();

        private LocationGroundDefinition Ground => Visual?.Ground;

        private void BuildGroundSettings(LocalLocationDefinition location)
        {
            if (Visual == null)
            {
                Help("Для земли нужна художественная сборка места: вкладка «Предметы» → «Добавить сборку и свет».");
                return;
            }
            if (Visual.Ground == null) Visual.Ground = new LocationGroundDefinition();
            if (Visual.Camera == null) Visual.Camera = new LocationCameraSettings();
            LocationGroundDefinition ground = Visual.Ground;

            // ---------------- Земля ----------------
            Heading("Земля");
            List<string> modes = new List<string> { "Один участок (рисунок места)", "Несколько участков (земля из слоёв)" };
            PopupField<string> mode = new PopupField<string>("Режим земли", modes, ground.IsTiled ? 1 : 0);
            mode.RegisterValueChangedCallback(evt =>
            {
                LocationGroundMode value = mode.index == 1 ? LocationGroundMode.Tiles : LocationGroundMode.Single;
                if (value == LocationGroundMode.Tiles && ground.Tiles.Count == 0) { status.text = "Сначала задайте Color в «Слоях земли» или импортируйте экспорт Blender."; BuildSettings(); return; }
                Change(() => ground.Mode = value, true);
            });
            settings.Add(mode);
            if (ground.IsTiled)
            {
                Label info = new Label("Карта «" + ground.MapId + "» · сетка " + ground.Columns + "×" + ground.Rows + " · участок " + ground.TileWidth + "×" +
                                       ground.TileHeight + " px · вся " + ground.VirtualSize.x + "×" + ground.VirtualSize.y + " px" +
                                       (ground.ExporterVersion.Length > 0 ? " · " + (ground.ExporterVersion == "вручную" ? "собрана вручную" : "Blender " + ground.ExporterVersion) : "") +
                                       (ground.Incomplete ? "\nНЕПОЛНАЯ: только предпросмотр, в игру не пойдёт." : ""));
                info.style.whiteSpace = WhiteSpace.Normal;
                settings.Add(info);
            }
            else if (ground.Tiles.Count > 0)
                Help("Участки «" + ground.MapId + "» сохранены, но показывается прежний рисунок места. Режим выше возвращает их.");

            BuildLayers(location, ground);

            // ---------------- Импорт из Blender ----------------
            Heading("Импорт из Blender");
            VisualElement row = new VisualElement(); row.style.flexDirection = FlexDirection.Row; row.style.flexWrap = Wrap.Wrap;
            AddButton(row, "Импортировать экспорт Blender…", () =>
            {
                string path = EditorUtility.OpenFilePanel("Manifest экспорта (…_manifest.json)", "", "json");
                if (!string.IsNullOrEmpty(path)) LoadPackage(path);
            });
            AddButton(row, "…из папки", () =>
            {
                string path = EditorUtility.OpenFolderPanel("Папка экспорта Blender", "", "");
                if (!string.IsNullOrEmpty(path)) LoadPackage(path);
            });
            settings.Add(row);

            if (pendingPackage != null) BuildPendingImport(location);

            Foldout manual = new Foldout { text = "Ручное добавление участков (без manifest)", value = false };
            manual.Add(new HelpBox("Запасной путь: PNG «имя_X000_Y000.png», «…_normal.png», «…_height.png». Без manifest размеры, разрядность, " +
                                   "Height Range и ориентацию задаёте вы; неоднозначные пары не принимаются. Одну картинку всей карты проще задать в «Слоях земли».",
                HelpBoxMessageType.None));
            DoubleField min = new DoubleField("Height min (BU)") { value = manualOptions.HeightMin };
            min.RegisterValueChangedCallback(evt => manualOptions.HeightMin = evt.newValue); manual.Add(min);
            DoubleField max = new DoubleField("Height max (BU)") { value = manualOptions.HeightMax };
            max.RegisterValueChangedCallback(evt => manualOptions.HeightMax = evt.newValue); manual.Add(max);
            DoubleField meters = new DoubleField("Метров в Blender Unit") { value = manualOptions.MetersPerBlenderUnit };
            meters.RegisterValueChangedCallback(evt => manualOptions.MetersPerBlenderUnit = evt.newValue); manual.Add(meters);
            UnityEngine.UIElements.Toggle fromBottom = new UnityEngine.UIElements.Toggle("Индексы от нижнего левого") { value = manualOptions.IndicesFromBottom };
            fromBottom.RegisterValueChangedCallback(evt => manualOptions.IndicesFromBottom = evt.newValue); manual.Add(fromBottom);
            UnityEngine.UIElements.Toggle rows = new UnityEngine.UIElements.Toggle("Изображения не перевёрнуты") { value = manualOptions.RowsTopToBottom };
            rows.RegisterValueChangedCallback(evt => manualOptions.RowsTopToBottom = evt.newValue); manual.Add(rows);
            UnityEngine.UIElements.Toggle green = new UnityEngine.UIElements.Toggle("Зелёный нормалей инвертирован (DirectX)") { value = manualOptions.InvertGreen };
            green.RegisterValueChangedCallback(evt => manualOptions.InvertGreen = evt.newValue); manual.Add(green);
            UnityEngine.UIElements.Toggle confirm = new UnityEngine.UIElements.Toggle("Размер, разрядность, диапазон и ориентация проверены") { value = manualOptions.Confirmed };
            confirm.RegisterValueChangedCallback(evt => manualOptions.Confirmed = evt.newValue); manual.Add(confirm);
            manual.Add(new Button(() =>
            {
                string folder = EditorUtility.OpenFolderPanel("Папка с PNG участков", "", "");
                if (string.IsNullOrEmpty(folder)) return;
                LoadManual(Directory.GetFiles(folder, "*.png", SearchOption.AllDirectories));
            }) { text = "Выбрать папку с PNG…" });
            settings.Add(manual);

            if (ground.Tiles.Count > 0)
            {
                Foldout list = new Foldout { text = "Участки (" + ground.Tiles.Count + ")", value = false };
                foreach (LocationGroundTile tile in ground.Tiles)
                {
                    Label line = new Label(tile.Key + " · Color " + (tile.IsPainted ? "своя картинка" : Mark(tile.Color)) + " · Normal " + Mark(tile.Normal) + " · Height " + Mark(tile.Height));
                    line.style.fontSize = 11;
                    list.Add(line);
                }
                settings.Add(list);
            }

            if (ground.IsTiled && ground.Tiles.Count > 0) BuildCrop(location, ground);
            if (ground.IsTiled && ground.Tiles.Count > 0) BuildPeopleHeight(location, ground);

            // ---------------- Карта высот ----------------
            Heading("Карта высот");
            if (!ground.HasAnyHeight)
                Help("У земли нет Height. Высота — числовые данные экспорта (абсолютная Z Blender), не рисунок; её не нарисовать вручную.");
            else
            {
                Label range = new Label("Диапазон " + ground.HeightMin.ToString("0.####") + " … " + ground.HeightMax.ToString("0.####") + " BU · " + ground.HeightBitDepth +
                                        " бит · шаг " + ((ground.HeightMax - ground.HeightMin) / (ground.HeightBitDepth == 16 ? 65535.0 : 255.0)).ToString("0.######") +
                                        " BU · " + ground.MetersPerBlenderUnit.ToString("0.###") + " м/BU" +
                                        (renderer?.Height != null ? "\nВ памяти: " + (renderer.Height.MemoryBytes / 1048576.0).ToString("0.0") + " МиБ (" + renderer.Height.LoadedTiles + " участков)" : ""));
                range.style.whiteSpace = WhiteSpace.Normal;
                settings.Add(range);
                Toggle("Высота включена", ground.HeightEnabled, value => ground.HeightEnabled = value, true);
                Toggle("Высота только на части карты", ground.HeightPartialAllowed, value => ground.HeightPartialAllowed = value);
                Number("Разрыв поверхности (м)", ground.SeamlessMeters, .01f, 10, value => ground.SeamlessMeters = value,
                    "Соседние пиксели расходятся сильнее — интерполяция их не смешивает (ступень, край моста).");
                if (ground.SurfaceContract == "separate_explicit")
                    settings.Add(new HelpBox("Раздельные источники: Height описывает землю, а не дополнительные объекты Color (крыши, камни).", HelpBoxMessageType.Warning));
                foreach (string problem in renderer?.HeightProblems ?? Array.Empty<string>())
                    settings.Add(new HelpBox(problem, HelpBoxMessageType.Warning));
            }
            VisualElement views = new VisualElement(); views.style.flexDirection = FlexDirection.Row;
            foreach ((string title, GroundView value) in new[] { ("Color", GroundView.Color), ("Normal", GroundView.Normal), ("Height", GroundView.Height) })
            {
                Button button = new Button(() => { groundView = value; ApplyGroundView(); BuildSettings(); }) { text = title };
                if (groundView == value) button.style.unityFontStyleAndWeight = FontStyle.Bold;
                views.Add(button);
            }
            settings.Add(new Label("Предпросмотр земли"));
            settings.Add(views);
            Help("Высота под указателем — в левом верхнем углу предпросмотра (точный пиксель и билинейно).");

            // ---------------- Камера ----------------
            Heading("Камера");
            LocationCameraSettings camera = Visual.Camera;
            if (camera.Version == 0)
                Help("Прежние настройки: кадр сразу на командире, как до этой версии. Любая правка ниже включает настройки места.");
            void CameraChange(Action action) { action(); camera.Version = 1; }
            Toggle("Следовать за командиром", camera.FollowCommander, value => CameraChange(() => camera.FollowCommander = value));
            Number("Сглаживание (с)", camera.SmoothTime, 0, 1.5f, value => CameraChange(() => camera.SmoothTime = value), "0 — кадр сразу на командире.");
            Vector2Field deadZone = new Vector2Field("Мёртвая зона (доли)") { value = camera.DeadZone };
            deadZone.RegisterValueChangedCallback(evt => Change(() => CameraChange(() => camera.DeadZone = Vector2.Max(Vector2.zero, Vector2.Min(Vector2.one, evt.newValue)))));
            settings.Add(deadZone);
            Vector2Field offset = new Vector2Field("Смещение (пиксели, Y вниз)") { value = camera.Offset };
            offset.RegisterValueChangedCallback(evt => Change(() => CameraChange(() => camera.Offset = evt.newValue)));
            settings.Add(offset);
            Number("Видимая высота (пиксели)", camera.ViewHeight, 0, Mathf.Max(1080, location.CanvasHeight), value => CameraChange(() => camera.ViewHeight = value),
                "0 — как прежде: высота места, но не больше 1080. Колесо в игре приближает от этого значения.");
            List<LocationCameraBounds> bounds = new List<LocationCameraBounds> { LocationCameraBounds.Canvas, LocationCameraBounds.UsefulArea, LocationCameraBounds.Custom };
            Choice("Границы камеры", bounds, value => value == LocationCameraBounds.Canvas ? "Весь рисунок" : value == LocationCameraBounds.UsefulArea ? "Полезная область экспорта" : "Своя рамка",
                camera.Bounds, value => CameraChange(() => camera.Bounds = value));
            if (camera.Bounds == LocationCameraBounds.Custom)
            {
                RectField custom = new RectField("Рамка (доли рисунка)") { value = camera.CustomBounds };
                custom.RegisterValueChangedCallback(evt => Change(() => CameraChange(() => camera.CustomBounds = evt.newValue)));
                settings.Add(custom);
            }
            Number("Телепорт дальше (высот кадра)", camera.SnapDistance, 0, 5, value => CameraChange(() => camera.SnapDistance = value),
                "Скачок командира дальше этого — кадр сразу на нём, без пролёта.");
            UnityEngine.UIElements.Toggle frame = new UnityEngine.UIElements.Toggle("Показывать рамку камеры") { value = showCameraFrame };
            frame.RegisterValueChangedCallback(evt => showCameraFrame = evt.newValue); settings.Add(frame);

            // ---------------- Проверка ----------------
            Heading("Проверка");
            UnityEngine.UIElements.Toggle grid = new UnityEngine.UIElements.Toggle("Сетка участков и индексы") { value = showGroundGrid };
            grid.RegisterValueChangedCallback(evt => showGroundGrid = evt.newValue); settings.Add(grid);
            AddButton(settings, "Проверить землю", () => { groundCheck = CheckGround(location); BuildSettings(); });
            foreach (string line in groundCheck)
                settings.Add(new HelpBox(line, line.StartsWith("✓", StringComparison.Ordinal) ? HelpBoxMessageType.Info : HelpBoxMessageType.Warning));
        }

        private static string Mark(UnityEngine.Object value) => value != null ? "✓" : "—";

        // ------------------------------------------------------------------
        // Слои земли: Color, Normal, Height по отдельности
        // ------------------------------------------------------------------

        private GroundLayers.Plan pendingLayer;
        private GroundLayers.Layer pendingLayerKind;
        private string pendingLayerPath;
        private readonly GroundLayers.Options layerOptions = new GroundLayers.Options();

        private void BuildLayers(LocalLocationDefinition location, LocationGroundDefinition ground)
        {
            Heading("Слои земли");
            bool tiled = ground.IsTiled && ground.Tiles.Any(tile => tile?.Color != null);
            Help(tiled
                ? "Каждый слой меняется отдельно. «Заменить…» берёт картинку всей карты любого размера — она пересчитается под землю. " +
                  "Normal и Height не обязательны: без Normal земля освещается ровно, без Height люди стоят на плоскости."
                : "Земли из участков нет. «Создать землю из рисунка…» — одна картинка Color любого размера: место подстроится под неё. " +
                  "Normal и Height можно добавить потом (или импортировать экспорт Blender ниже).");
            foreach (GroundLayers.Layer layer in new[] { GroundLayers.Layer.Color, GroundLayers.Layer.Normal, GroundLayers.Layer.Height })
            {
                bool has = layer == GroundLayers.Layer.Color ? tiled
                    : layer == GroundLayers.Layer.Normal ? tiled && ground.Tiles.Any(tile => tile?.Normal != null) : tiled && ground.HasAnyHeight;
                VisualElement line = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, marginTop = 4 } };
                Label title = new Label(GroundLayers.Name(layer)) { style = { width = 58, unityFontStyleAndWeight = FontStyle.Bold } };
                Label state = new Label(GroundLayers.Describe(ground, layer)) { style = { flexGrow = 1, flexShrink = 1, whiteSpace = WhiteSpace.Normal, fontSize = 11 } };
                if (!has) state.style.color = new Color(.65f, .68f, .62f);
                line.Add(title);
                line.Add(state);
                settings.Add(line);
                VisualElement buttons = new VisualElement { style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap, marginLeft = 58 } };
                if (layer == GroundLayers.Layer.Color || tiled)
                    AddButton(buttons, layer == GroundLayers.Layer.Color && !tiled ? "Создать землю из рисунка…" : has ? "Заменить…" : "Добавить…", () =>
                    {
                        string path = EditorUtility.OpenFilePanel(GroundLayers.Name(layer) + " всей карты (PNG)", "", "png");
                        if (!string.IsNullOrEmpty(path)) LoadLayer(path, layer);
                    });
                if (has)
                    AddButton(buttons, "Сохранить целиком…", () =>
                    {
                        string suffix = layer == GroundLayers.Layer.Color ? "_color_full" : layer == GroundLayers.Layer.Normal ? "_normal_full" : "_height_full";
                        string path = EditorUtility.SaveFilePanel(GroundLayers.Name(layer) + " всей карты", "", GroundImporter.Sanitize(ground.MapId) + suffix + ".png", "png");
                        if (string.IsNullOrEmpty(path)) return;
                        GroundLayers.ExportWhole(ground, layer, path, out string message);
                        status.text = message;
                        EditorUtility.RevealInFinder(path);
                    });
                if (layer == GroundLayers.Layer.Color && ground.IsPainted && ground.Tiles.Any(tile => !string.IsNullOrEmpty(tile?.RenderColorGuid)))
                    AddButton(buttons, "Вернуть рендер Blender", () =>
                    {
                        if (!EditorUtility.DisplayDialog("Вернуть Color из рендера?", "Своя картинка Color будет убрана из проекта (исходный файл не трогается).", "Вернуть", "Отмена")) return;
                        GroundPaintOver.RevertToRender(database, location.Id, out string message);
                        status.text = message;
                        ClearGroundPreview();
                        BuildSettings(); RebuildPreview();
                    });
                if (has)
                    AddButton(buttons, layer == GroundLayers.Layer.Color ? "Удалить землю…" : "Удалить", () => RemoveLayer(location, layer));
                settings.Add(buttons);
            }
            if (pendingLayer != null) BuildPendingLayer(ground);
            settings.Add(GroundDropZone());
        }

        private void RemoveLayer(LocalLocationDefinition location, GroundLayers.Layer layer)
        {
            string text = layer == GroundLayers.Layer.Color
                ? "Земля из участков будет удалена целиком (Color, Normal, Height) — место вернётся к «Рисунку места». Входы, объекты и разметка останутся."
                : GroundLayers.Name(layer) + " будет удалён у всех участков. " + (layer == GroundLayers.Layer.Normal ? "Земля станет освещаться ровно." : "Люди будут стоять на плоской земле.");
            if (!EditorUtility.DisplayDialog("Удалить " + (layer == GroundLayers.Layer.Color ? "землю" : GroundLayers.Name(layer)) + "?", text + "\nФайлы этого слоя будут убраны из проекта.", "Удалить", "Отмена"))
                return;
            AssetDatabase.SaveAssetIfDirty(database);
            GroundLayers.Remove(database, location.Id, layer, out string message);
            status.text = message;
            pendingLayer = null;
            if (layer == GroundLayers.Layer.Height && groundView == GroundView.Height) groundView = GroundView.Color;
            if (layer == GroundLayers.Layer.Normal && groundView == GroundView.Normal) groundView = GroundView.Color;
            ClearGroundPreview();
            groundCheck = CheckGround(location);
            BuildSettings(); RebuildPreview();
        }

        private void LoadLayer(string path, GroundLayers.Layer layer)
        {
            pendingLayerPath = path;
            pendingLayerKind = layer;
            pendingPackage = null; pendingPlan = null;
            LocationGroundDefinition ground = Ground;
            if (layer == GroundLayers.Layer.Height)
            {
                bool known = ground != null && ground.HasAnyHeight;
                layerOptions.HeightMin = known ? ground.HeightMin : manualOptions.HeightMin;
                layerOptions.HeightMax = known ? ground.HeightMax : manualOptions.HeightMax;
                layerOptions.MetersPerBlenderUnit = known ? ground.MetersPerBlenderUnit : manualOptions.MetersPerBlenderUnit;
            }
            ReplanLayer();
            tab = Tab.Ground;
            status.text = pendingLayer.CanApply ? GroundLayers.Name(layer) + ": проверьте сводку в «Слоях земли» и нажмите «Применить»." : GroundLayers.Name(layer) + " не подходит — см. «Слои земли».";
            BuildSettings();
        }

        private void ReplanLayer() => pendingLayer = GroundLayers.Analyze(Location, Ground, pendingLayerKind, pendingLayerPath, layerOptions);

        private void BuildPendingLayer(LocationGroundDefinition ground)
        {
            GroundLayers.Plan plan = pendingLayer;
            VisualElement box = new VisualElement { style = { marginTop = 6, paddingLeft = 6, paddingRight = 6, paddingTop = 4, paddingBottom = 6 } };
            box.style.borderLeftWidth = 2;
            box.style.borderLeftColor = new Color(.95f, .75f, .3f);
            List<GroundLayers.Layer> kinds = new List<GroundLayers.Layer> { GroundLayers.Layer.Color, GroundLayers.Layer.Normal, GroundLayers.Layer.Height };
            PopupField<string> kind = new PopupField<string>("Это слой", kinds.Select(GroundLayers.Name).ToList(), kinds.IndexOf(pendingLayerKind))
            { tooltip = "Слой угадан по имени файла (…_normal / _n — Normal, …_height — Height). Поправьте, если не так." };
            kind.RegisterValueChangedCallback(evt => LoadLayer(pendingLayerPath, kinds[kind.index]));
            box.Add(kind);
            Label summary = new Label(plan.Header.Width > 0 ? plan.Summary : Path.GetFileName(plan.SourcePath)) { style = { whiteSpace = WhiteSpace.Normal } };
            box.Add(summary);
            if (plan.Layer == GroundLayers.Layer.Color && ground.IsTiled && ground.Tiles.Count > 0)
            {
                PopupField<string> fit = new PopupField<string>("Размер места",
                    new List<string> { "Оставить (рисунок растянется на землю)", "Подогнать место под рисунок" }, (int)layerOptions.Fit)
                { tooltip = "Подогнать — 1 пиксель рисунка = 1 пиксель места: сетка участков строится заново, Normal и Height растягиваются на новый кадр." };
                fit.RegisterValueChangedCallback(evt => { layerOptions.Fit = (GroundLayers.Fit)fit.index; ReplanLayer(); BuildSettings(); });
                box.Add(fit);
            }
            if (plan.Layer == GroundLayers.Layer.Normal)
            {
                UnityEngine.UIElements.Toggle invert = new UnityEngine.UIElements.Toggle("Зелёный инвертирован (DirectX)") { value = layerOptions.InvertGreen };
                invert.RegisterValueChangedCallback(evt => { layerOptions.InvertGreen = evt.newValue; ReplanLayer(); BuildSettings(); });
                box.Add(invert);
            }
            if (plan.Layer == GroundLayers.Layer.Height)
            {
                void Number(string label, double value, Action<double> set)
                {
                    DoubleField field = new DoubleField(label) { value = value, isDelayed = true };
                    field.RegisterValueChangedCallback(evt => { set(evt.newValue); ReplanLayer(); BuildSettings(); });
                    box.Add(field);
                }
                Number("Height min (BU)", layerOptions.HeightMin, value => layerOptions.HeightMin = value);
                Number("Height max (BU)", layerOptions.HeightMax, value => layerOptions.HeightMax = value);
                Number("Метров в Blender Unit", layerOptions.MetersPerBlenderUnit, value => layerOptions.MetersPerBlenderUnit = value);
            }
            foreach (string error in plan.Errors) box.Add(new HelpBox(error, HelpBoxMessageType.Error));
            foreach (string warning in plan.Warnings) box.Add(new HelpBox(warning, HelpBoxMessageType.Warning));
            foreach (string change in plan.Changes) box.Add(new HelpBox(change, HelpBoxMessageType.None));
            VisualElement buttons = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            Button apply = new Button(ApplyLayer) { text = "Применить" };
            apply.SetEnabled(plan.CanApply);
            buttons.Add(apply);
            AddButton(buttons, "Отмена", () => { pendingLayer = null; BuildSettings(); });
            box.Add(buttons);
            settings.Add(box);
        }

        private void ApplyLayer()
        {
            if (pendingLayer == null || Location == null) return;
            AssetDatabase.SaveAssetIfDirty(database);
            bool ok = GroundLayers.Apply(pendingLayer, database, Location.Id, (location, width, height) => ResizeCanvas(location, width, height, location.HexesAcross, true),
                out string message);
            status.text = message;
            if (!ok) { EditorUtility.DisplayDialog("Слои земли", message, "Понятно"); return; }
            GroundLayers.Layer layer = pendingLayer.Layer;
            pendingLayer = null;
            groundView = GroundView.Color;
            ClearGroundPreview();
            groundCheck = CheckGround(Location);
            BuildSettings(); RebuildPreview();
        }

        // ------------------------------------------------------------------
        // Обрезка земли
        // ------------------------------------------------------------------

        private RectInt? cropRect;
        private GroundCrop.Plan cropPlan;
        private bool cropDrawing;
        private Vector2 cropStart;
        private int cropMargin = 16;

        private void BuildCrop(LocalLocationDefinition location, LocationGroundDefinition ground)
        {
            Heading("Обрезка");
            Help("Чёрное — пустота: прозрачные поля экспорта и добивка до целых участков. «Найти рамку по земле» берёт рамку по непрозрачным " +
                 "пикселям Color; рамку можно нарисовать мышью на картинке или поправить числами. Земля перенарезается, входы, объекты, " +
                 "противники, зоны и разметка остаются на своих местах земли.");
            if (ground.Cropped)
            {
                Label state = new Label("Земля обрезана: " + ground.Columns * ground.TileWidth + "×" + ground.Rows * ground.TileHeight + " px.");
                settings.Add(state);
                Toggle("Обрезать так же при переимпорте", ground.CropOnReimport, value => ground.CropOnReimport = value);
            }
            IntegerField margin = new IntegerField("Запас вокруг земли (px)") { value = cropMargin };
            margin.RegisterValueChangedCallback(evt => cropMargin = Mathf.Max(0, evt.newValue));
            settings.Add(margin);
            VisualElement row = new VisualElement(); row.style.flexDirection = FlexDirection.Row; row.style.flexWrap = Wrap.Wrap;
            AddButton(row, "Найти рамку по земле", () =>
            {
                EditorUtility.DisplayProgressBar("Обрезка земли", "Поиск непрозрачных пикселей…", .5f);
                bool found;
                RectInt bounds;
                string problem;
                try { found = GroundCrop.TryContentBounds(ground, location, out bounds, out problem); }
                finally { EditorUtility.ClearProgressBar(); }
                if (!found) { status.text = problem; return; }
                SetCropRect(location, ground, new RectInt(bounds.x - cropMargin, bounds.y - cropMargin, bounds.width + 2 * cropMargin, bounds.height + 2 * cropMargin));
            });
            AddButton(row, cropDrawing ? "● Рисую рамку (ЛКМ)" : "Нарисовать рамку мышью", () =>
            {
                cropDrawing = !cropDrawing;
                status.text = cropDrawing ? "Протяните рамку обрезки ЛКМ по картинке места." : status.text;
                BuildSettings();
            });
            settings.Add(row);
            if (cropRect == null || cropPlan == null) return;
            RectIntField field = new RectIntField("Рамка (px, Y вниз)") { value = cropRect.Value };
            field.RegisterValueChangedCallback(evt => SetCropRect(location, ground, evt.newValue));
            settings.Add(field);
            Label summary = new Label(cropPlan.CanApply ? cropPlan.Summary(LocationVisualGeometry.CanvasSize(location)) : string.Empty);
            summary.style.whiteSpace = WhiteSpace.Normal;
            settings.Add(summary);
            foreach (string error in cropPlan.Errors) settings.Add(new HelpBox(error, HelpBoxMessageType.Error));
            foreach (string warning in cropPlan.Warnings) settings.Add(new HelpBox(warning, HelpBoxMessageType.Warning));
            VisualElement buttons = new VisualElement(); buttons.style.flexDirection = FlexDirection.Row;
            Button apply = new Button(() =>
            {
                AssetDatabase.SaveAssetIfDirty(database);
                bool ok = GroundCrop.Apply(cropPlan, database, location.Id, out string message);
                status.text = message;
                if (!ok) { EditorUtility.DisplayDialog("Обрезка земли", message, "Понятно"); return; }
                cropRect = null; cropPlan = null; cropDrawing = false;
                zoom = 1; viewCenter = CanvasSize / 2;
                ClearGroundPreview();
                groundCheck = CheckGround(location);
                BuildSettings(); RebuildPreview();
            }) { text = "Обрезать" };
            apply.SetEnabled(cropPlan.CanApply);
            buttons.Add(apply);
            AddButton(buttons, "Отмена", () => { cropRect = null; cropPlan = null; cropDrawing = false; BuildSettings(); });
            settings.Add(buttons);
        }

        private void SetCropRect(LocalLocationDefinition location, LocationGroundDefinition ground, RectInt rect)
        {
            cropRect = rect;
            cropPlan = GroundCrop.Analyze(ground, location, rect);
            tab = Tab.Ground;
            BuildSettings();
        }

        // Рамка обрезки мышью: ЛКМ — протянуть.
        private bool HandleCropInput(Event evt, Rect frame, Vector2 pixel)
        {
            if (!cropDrawing || tab != Tab.Ground || Ground == null || !Ground.IsTiled) return false;
            if (evt.type == EventType.MouseDown && evt.button == 0 && frame.Contains(evt.mousePosition))
            {
                cropStart = pixel;
                dragging = true;
                evt.Use();
                return true;
            }
            if (evt.type == EventType.MouseDrag && evt.button == 0 && dragging)
            {
                Vector2 a = Vector2.Min(cropStart, pixel), b = Vector2.Max(cropStart, pixel);
                cropRect = new RectInt(Mathf.RoundToInt(a.x), Mathf.RoundToInt(a.y), Mathf.RoundToInt(b.x - a.x), Mathf.RoundToInt(b.y - a.y));
                evt.Use();
                return true;
            }
            if (evt.type == EventType.MouseUp && evt.button == 0 && dragging)
            {
                dragging = false;
                cropDrawing = false;
                if (cropRect.HasValue) SetCropRect(Location, Ground, cropRect.Value);
                evt.Use();
                return true;
            }
            return false;
        }

        // Рост людей места задаёт «Ширина кадра боя» (клетка боя ∝ ширине кадра);
        // эталон персонажа из Blender даёт нужный рост в пикселях карты.
        private void BuildPeopleHeight(LocalLocationDefinition location, LocationGroundDefinition ground)
        {
            Heading("Рост людей");
            float current = renderer != null ? renderer.HexSizePixels * LocationWorldRenderer.FieldHeightInHexSizes : 0;
            Help("Люди места сейчас ≈ " + current.ToString("0") + " px (от «Ширины кадра боя», вкладка «Место»)." +
                 (ground.ExportCharacterPx > 0
                     ? " Эталон персонажа в экспорте Blender ≈ " + ground.ExportCharacterPx.ToString("0") + " px."
                     : " Укажите «Эталон персонажа» в аддоне — тогда рост подгоняется кнопкой."));
            if (ground.ExportCharacterPx > 0 && current > 1)
                AddButton(settings, "Подогнать рост людей под эталон", () =>
                {
                    float scale = ground.ExportCharacterPx / current;
                    Change(() => location.BattleFrameWidth = Mathf.Max(64, location.BattleFrameWidth * scale), true);
                    status.text = "Ширина кадра боя: " + location.BattleFrameWidth.ToString("0") + " px — люди ≈ " + ground.ExportCharacterPx.ToString("0") + " px.";
                });
        }

        private VisualElement GroundDropZone()
        {
            Label drop = new Label("Перетащите сюда картинку слоя (имя …_normal / _n — Normal, …_height — Height, иначе Color), " +
                                   "manifest или папку экспорта Blender, или PNG участков «имя_X000_Y000»");
            drop.style.whiteSpace = WhiteSpace.Normal;
            drop.style.unityTextAlign = TextAnchor.MiddleCenter;
            drop.style.paddingTop = drop.style.paddingBottom = 12;
            drop.style.marginTop = drop.style.marginBottom = 4;
            drop.style.color = new Color(.7f, .76f, .7f);
            drop.style.borderTopWidth = drop.style.borderBottomWidth = drop.style.borderLeftWidth = drop.style.borderRightWidth = 1;
            drop.style.borderTopColor = drop.style.borderBottomColor = drop.style.borderLeftColor = drop.style.borderRightColor = new Color(.45f, .5f, .45f);
            drop.RegisterCallback<DragUpdatedEvent>(evt =>
            {
                DragAndDrop.visualMode = DragAndDrop.paths != null && DragAndDrop.paths.Length > 0 ? DragAndDropVisualMode.Copy : DragAndDropVisualMode.Rejected;
                drop.style.backgroundColor = new Color(.95f, .75f, .3f, .2f);
                evt.StopPropagation();
            });
            drop.RegisterCallback<DragLeaveEvent>(_ => drop.style.backgroundColor = StyleKeyword.Null);
            drop.RegisterCallback<DragPerformEvent>(evt =>
            {
                DragAndDrop.AcceptDrag();
                evt.StopPropagation();
                string[] paths = DragAndDrop.paths ?? Array.Empty<string>();
                EditorApplication.delayCall += () =>
                {
                    if (paths.Length == 1 && (Directory.Exists(paths[0]) || paths[0].EndsWith(".json", StringComparison.OrdinalIgnoreCase))) LoadPackage(paths[0]);
                    // Одна картинка — слой всей карты (Color, Normal или Height по имени).
                    else if (paths.Length == 1 && paths[0].EndsWith(".png", StringComparison.OrdinalIgnoreCase) && !IsTileFileName(paths[0]))
                        LoadLayer(paths[0], GroundLayers.Guess(paths[0]));
                    else if (paths.Length > 0 && paths.All(path => path.EndsWith(".png", StringComparison.OrdinalIgnoreCase))) LoadManual(paths);
                    else status.text = "Нужна картинка слоя, manifest экспорта, папка экспорта или PNG участков.";
                };
            });
            return drop;
        }

        // «имя_X000_Y000[_normal|_height].png» — участок ручного пути.
        private static bool IsTileFileName(string path) =>
            System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileNameWithoutExtension(path), @"_X\d{3,}_Y\d{3,}(_normal|_height)?$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        private void LoadPackage(string path)
        {
            EditorUtility.DisplayProgressBar("Земля из Blender", "Проверка пакета (sha256, PNG)…", .5f);
            try { pendingPackage = GroundExportPackage.Load(path); }
            finally { EditorUtility.ClearProgressBar(); }
            pendingSelection = null;
            pendingLevelNormals = false;
            pendingKeepPaint = true;
            pendingLayer = null;
            pendingResize = true;
            ReplanPending();
            tab = Tab.Ground;
            status.text = pendingPackage.Errors.Count > 0 ? "Пакет с ошибками — см. «Земля»." : "Пакет проверен — проверьте сводку и нажмите «Применить».";
            BuildSettings();
        }

        private void LoadManual(IEnumerable<string> files)
        {
            pendingPackage = GroundImporter.FromFiles(files, manualOptions);
            pendingSelection = null;
            pendingLevelNormals = false;
            pendingResize = true;
            ReplanPending();
            tab = Tab.Ground;
            BuildSettings();
        }

        private void ReplanPending()
        {
            pendingPlan = pendingPackage != null ? GroundImporter.Analyze(pendingPackage, Location, Visual, pendingSelection) : null;
            if (pendingPlan != null && pendingSelection == null) pendingSelection = new HashSet<string>(pendingPlan.Selected);
        }

        private void BuildPendingImport(LocalLocationDefinition location)
        {
            GroundExportPackage package = pendingPackage;
            Heading("Импорт: проверка пакета");
            Label summary = new Label(package.Summary());
            summary.style.whiteSpace = WhiteSpace.Normal;
            settings.Add(summary);
            foreach (string error in package.Errors) settings.Add(new HelpBox(error, HelpBoxMessageType.Error));
            foreach (string warning in package.Warnings) settings.Add(new HelpBox(warning, HelpBoxMessageType.Warning));
            foreach (string note in package.Notes) settings.Add(new HelpBox(note, HelpBoxMessageType.Info));
            if (pendingPlan != null)
            {
                // Ошибки пакета уже показаны выше — без повтора.
                foreach (string blocker in pendingPlan.Blockers.Where(blocker => !package.Errors.Contains(blocker))) settings.Add(new HelpBox(blocker, HelpBoxMessageType.Error));
                if (pendingPlan.Blockers.Count == 0)
                {
                    settings.Add(new Label("Что изменится:"));
                    foreach (string change in pendingPlan.Changes) settings.Add(new HelpBox(change, HelpBoxMessageType.None));
                }
            }
            Foldout tiles = new Foldout { text = "Участки пакета", value = package.Tiles.Count <= 16 };
            foreach (GroundExportTile tile in package.Tiles.OrderByDescending(item => item.Y).ThenBy(item => item.X))
            {
                GroundImporter.TileAction action = pendingPlan != null && pendingPlan.Actions.TryGetValue(tile.Key, out GroundImporter.TileAction value) ? value : GroundImporter.TileAction.Skip;
                string state = !tile.Usable(GroundExportPackage.Color) ? "нет Color" : !package.TileReady(tile) ? "неполный" :
                    action == GroundImporter.TileAction.New ? "новый" : action == GroundImporter.TileAction.Replace ? "Заменить" :
                    action == GroundImporter.TileAction.Unchanged ? "без изменений" : "пропустить";
                UnityEngine.UIElements.Toggle check = new UnityEngine.UIElements.Toggle(tile.Key + " · " + state)
                {
                    value = pendingSelection != null && pendingSelection.Contains(tile.Key)
                };
                check.SetEnabled(tile.Usable(GroundExportPackage.Color));
                check.tooltip = string.Join("\n", tile.Problems);
                string key = tile.Key;
                check.RegisterValueChangedCallback(evt =>
                {
                    if (evt.newValue) pendingSelection.Add(key); else pendingSelection.Remove(key);
                    ReplanPending(); BuildSettings();
                });
                tiles.Add(check);
                foreach (string problem in tile.Problems) tiles.Add(new HelpBox(tile.Key + " · " + problem, HelpBoxMessageType.Warning));
            }
            settings.Add(tiles);
            // Normal и Height нужны не везде: проход можно не загружать.
            foreach (string kind in new[] { GroundExportPackage.Normal, GroundExportPackage.Height })
            {
                if (!package.Exported(kind)) continue;
                bool unavailable = package.Unavailable.Contains(kind);
                UnityEngine.UIElements.Toggle pass = new UnityEngine.UIElements.Toggle(kind + " из пакета")
                {
                    value = package.Requested(kind),
                    tooltip = unavailable ? kind + " не подходит к карте — добавьте его после импорта в «Слоях земли»."
                        : "Снято — " + kind + " из пакета не загружается: у прежних участков он остаётся, у новых его нет. Удалить или заменить слой можно в «Слоях земли»."
                };
                pass.SetEnabled(!unavailable);
                string passKind = kind;
                pass.RegisterValueChangedCallback(evt =>
                {
                    if (evt.newValue) package.Excluded.Remove(passKind); else package.Excluded.Add(passKind);
                    ReplanPending(); BuildSettings();
                });
                settings.Add(pass);
            }
            if (package.Requested(GroundExportPackage.Normal))
            {
                UnityEngine.UIElements.Toggle level = new UnityEngine.UIElements.Toggle("Выпрямить нормали по наклону камеры (" + package.NormalTiltDegrees.ToString("0.#") + "°)")
                {
                    value = pendingLevelNormals,
                    tooltip = "Ровная земля в нормалях наклонной камеры «смотрит» вверх по экрану, и 2D-свет освещает её только ниже себя. " +
                              "Поворот на угол из manifest — один для всех участков, швы не появляются. Исходные PNG не меняются."
                };
                level.RegisterValueChangedCallback(evt => pendingLevelNormals = evt.newValue);
                settings.Add(level);
            }
            if (Ground != null && Ground.IsPainted)
            {
                UnityEngine.UIElements.Toggle keep = new UnityEngine.UIElements.Toggle("Сохранить обрисовку Color") { value = pendingKeepPaint,
                    tooltip = "Обновятся рендер под обрисовкой, Normal и Height; сама обрисовка останется Color участков." };
                keep.RegisterValueChangedCallback(evt => pendingKeepPaint = evt.newValue);
                settings.Add(keep);
            }
            UnityEngine.UIElements.Toggle resize = new UnityEngine.UIElements.Toggle("Подогнать размер места под землю") { value = pendingResize };
            resize.RegisterValueChangedCallback(evt => pendingResize = evt.newValue);
            settings.Add(resize);
            VisualElement buttons = new VisualElement(); buttons.style.flexDirection = FlexDirection.Row;
            bool incomplete = pendingPlan != null && pendingPlan.Blockers.Count == 0 && GroundImporter.WouldBeIncomplete(pendingPlan, Ground);
            Button apply = new Button(ApplyPending) { text = incomplete ? "Применить как частичный предпросмотр" : "Применить" };
            apply.SetEnabled(pendingPlan != null && pendingPlan.Blockers.Count == 0 && pendingSelection.Count > 0);
            buttons.Add(apply);
            AddButton(buttons, "Отмена", () => { pendingPackage = null; pendingPlan = null; BuildSettings(); });
            settings.Add(buttons);
        }

        private void ApplyPending()
        {
            if (pendingPlan == null) return;
            pendingPlan.LevelNormals = pendingLevelNormals;
            pendingPlan.ResizeCanvas = pendingResize;
            pendingPlan.KeepPaint = pendingKeepPaint;
            if (GroundImporter.WouldBeIncomplete(pendingPlan, Ground))
            {
                if (!EditorUtility.DisplayDialog("Неполный пакет",
                        "Готовых участков " + pendingPackage.ReadyTiles + " из " + pendingPackage.Columns * pendingPackage.Rows +
                        ". Землю можно применить только как частичный предпросмотр: запуск места будет заблокирован, пока не появятся все Color-участки.",
                        "Применить как предпросмотр", "Отмена")) return;
                pendingPlan.AllowPartial = true;
            }
            if (!pendingResize && Location != null &&
                (Mathf.Abs(Location.CanvasWidth - pendingPackage.VirtualWidth) > .5f || Mathf.Abs(Location.CanvasHeight - pendingPackage.VirtualHeight) > .5f) &&
                !EditorUtility.DisplayDialog("Размер места", "Размер места останется прежним — земля растянется на него. Пиксель земли перестанет совпадать с пикселем места.", "Растянуть", "Отмена"))
                return;
            AssetDatabase.SaveAssetIfDirty(database);
            bool ok = GroundImporter.Apply(pendingPlan, database, (location, width, height) => ResizeCanvas(location, width, height, location.HexesAcross, true), out string message);
            status.text = message;
            if (!ok) { EditorUtility.DisplayDialog("Земля из Blender", message, "Понятно"); return; }
            pendingPackage = null; pendingPlan = null; groundView = GroundView.Color;
            ClearGroundPreview();
            groundCheck = CheckGround(Location);
            BuildSettings(); RebuildPreview();
        }

        // ------------------------------------------------------------------
        // Проверка
        // ------------------------------------------------------------------

        private List<string> CheckGround(LocalLocationDefinition location)
        {
            List<string> result = new List<string>();
            LocationGroundDefinition ground = Ground;
            if (ground == null || !ground.IsTiled) { result.Add("✓ Земля — прежний рисунок места (один участок)."); return result; }
            result.AddRange(LocationGroundLayout.RuntimeErrors(Visual, location));
            List<string> heightErrors = new List<string>();
            LocationHeightField height = LocationHeightField.Load(ground, location, heightErrors);
            result.AddRange(heightErrors);
            long gpu = 0;
            foreach (LocationGroundTile tile in ground.Tiles)
            {
                if (tile?.Color == null) continue;
                gpu += UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(tile.Color.texture);
                if (tile.Normal != null) gpu += UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(tile.Normal);
                // Обрисовка может быть в k раз подробнее рендера — на тот же прямоугольник.
                float scale = tile.IsPainted ? ground.ColorScale : 1;
                int expectW = Mathf.RoundToInt(ground.TileWidth * scale), expectH = Mathf.RoundToInt(ground.TileHeight * scale);
                if (AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(tile.Color.texture)) is TextureImporter importer)
                {
                    TextureImporterSettings settings = new TextureImporterSettings();
                    importer.ReadTextureSettings(settings);
                    if (settings.spriteMeshType != SpriteMeshType.FullRect) result.Add(tile.Key + ": сетка спрайта не «Full Rect» — возможны щели на швах.");
                    if (importer.mipmapEnabled) result.Add(tile.Key + ": включены mipmaps — края участков могут различаться.");
                    importer.GetSourceTextureWidthAndHeight(out int w, out int h);
                    if (w != expectW || h != expectH) result.Add(tile.Key + ": исходный PNG " + w + "×" + h + " вместо " + expectW + "×" + expectH + ".");
                    if (importer.maxTextureSize < Mathf.Max(w, h)) result.Add(tile.Key + ": Max Size " + importer.maxTextureSize + " уменьшает участок.");
                    if (tile.Normal != null && SpriteNormalMaps.Find(importer) != tile.Normal) result.Add(tile.Key + ": нормаль не подключена второй текстурой _NormalMap.");
                }
                if (Mathf.RoundToInt(tile.Color.rect.width) != expectW || Mathf.RoundToInt(tile.Color.rect.height) != expectH)
                    result.Add(tile.Key + ": Sprite " + tile.Color.rect.width + "×" + tile.Color.rect.height + " — не весь участок.");
                if (tile.IsPainted && !string.IsNullOrEmpty(tile.RenderColorGuid) && AssetDatabase.LoadAssetAtPath<Sprite>(AssetDatabase.GUIDToAssetPath(tile.RenderColorGuid)) == null)
                    result.Add(tile.Key + ": нет рендера Color под обрисовкой — вернуть рендер нельзя, пока экспорт не переимпортирован.");
            }
            if (result.Count == 0) result.Add("✓ Земля в порядке: " + ground.Tiles.Count + " участков, ориентация и размеры согласованы.");
            result.Add("✓ Память: текстуры Color/Normal ≈ " + (gpu / 1048576.0).ToString("0.0") + " МиБ" +
                       (height != null ? ", высота (CPU) " + (height.MemoryBytes / 1048576.0).ToString("0.0") + " МиБ" : "") + ".");
            return result;
        }

        // ------------------------------------------------------------------
        // Предпросмотр
        // ------------------------------------------------------------------

        // Normal и Height (без света) — только на вкладке «Земля»; на других
        // вкладках и после ухода с неё — Color под светом суток.
        private void ApplyGroundView()
        {
            if (renderer == null || Ground == null || !Ground.IsTiled) return;
            if (tab != Tab.Ground) groundView = GroundView.Color;
            if (groundView == GroundView.Color) { renderer.ShowGroundOverride(null); return; }
            GroundView view = groundView;
            renderer.ShowGroundOverride((column, row) => PreviewSprite(Ground.Find(column, row), view));
        }

        private Sprite PreviewSprite(LocationGroundTile tile, GroundView view)
        {
            UnityEngine.Object source = view == GroundView.Normal ? (UnityEngine.Object)tile?.Normal : tile?.Height;
            if (source == null) return null;
            string key = view + ":" + AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(source));
            if (groundPreview.TryGetValue(key, out Sprite cached) && cached != null) return cached;
            Texture2D texture = null;
            try
            {
                if (view == GroundView.Normal)
                {
                    byte[] png = File.ReadAllBytes(AssetDatabase.GetAssetPath(source));
                    ushort[] samples = PngCodec.Decode(png, out PngCodec.Header header);
                    float max = header.BitDepth == 16 ? 65535 : 255;
                    int channels = header.Channels;
                    texture = Downsampled(header.Width, header.Height, (x, y) =>
                    {
                        int i = ((header.Height - 1 - y) * header.Width + x) * channels;
                        return new Color(samples[i] / max, samples[i + Math.Min(1, channels - 1)] / max, samples[i + Math.Min(2, channels - 1)] / max, 1);
                    });
                }
                else
                {
                    LocationHeightTileData data = LocationHeightTileData.Deserialize(((TextAsset)source).bytes);
                    texture = Downsampled(data.Width, data.Height, (x, y) =>
                    {
                        int i = y * data.Width + x;
                        if (data.Coverage[i] == 0) return new Color(.55f, .1f, .45f, 1);
                        float t = (float)(data.Values[i] / data.MaxRaw);
                        return new Color(t, t, t, 1);
                    });
                }
            }
            catch (Exception error) when (error is IOException || error is InvalidDataException)
            {
                status.text = tile.Key + ": " + error.Message;
                return null;
            }
            Sprite sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(.5f, .5f), 100);
            sprite.hideFlags = HideFlags.HideAndDontSave;
            groundPreview[key] = sprite;
            return sprite;
        }

        // Копия для показа (до 512 пикселей по стороне); строки снизу вверх.
        private static Texture2D Downsampled(int width, int height, Func<int, int, Color> pixel)
        {
            int step = Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(width, height) / 512f));
            int w = Mathf.Max(1, width / step), h = Mathf.Max(1, height / step);
            Texture2D texture = new Texture2D(w, h, TextureFormat.RGBA32, false, true)
            {
                hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp
            };
            Color[] colors = new Color[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    colors[y * w + x] = pixel(Mathf.Min(width - 1, x * step), Mathf.Min(height - 1, y * step));
            texture.SetPixels(colors);
            texture.Apply();
            return texture;
        }

        private void ClearGroundPreview()
        {
            foreach (Sprite sprite in groundPreview.Values)
            {
                if (sprite == null) continue;
                DestroyImmediate(sprite.texture);
                DestroyImmediate(sprite);
            }
            groundPreview.Clear();
        }

        // Сетка участков, рамка камеры и высота под указателем.
        private void DrawGroundOverlay(Rect frame, Vector2 shift, Vector2 mouse)
        {
            LocalLocationDefinition location = Location;
            LocationGroundDefinition ground = Ground;
            Vector2 Gui(Vector2 p) => ToGui(frame, p) + shift;
            Handles.BeginGUI();
            if (showGroundGrid && ground != null && ground.IsTiled && renderer != null)
            {
                LocationGroundGrid grid = LocationGroundGrid.For(ground, location);
                GUIStyle style = new GUIStyle(EditorStyles.miniBoldLabel) { normal = { textColor = new Color(1, .9f, .55f) } };
                for (int row = 0; row < grid.Rows; row++)
                {
                    for (int column = 0; column < grid.Columns; column++)
                    {
                        Rect rect = grid.TileCanvasRect(column, row);
                        Vector2 a = Gui(rect.min), b = Gui(rect.max);
                        bool missing = renderer.IsGroundPieceMissing(column, row);
                        Handles.DrawSolidRectangleWithOutline(Rect.MinMaxRect(a.x, a.y, b.x, b.y), missing ? new Color(1, .1f, .1f, .12f) : Color.clear,
                            new Color(1, .85f, .4f, .55f));
                        GUI.Label(new Rect(a.x + 3, b.y - 16, 140, 16), LocationGroundTile.KeyOf(column, row) + (missing ? " · нет Color" : ""), style);
                    }
                }
                if (ground.UsefulPixels.width > 0)
                {
                    Rect useful = grid.VirtualRectToCanvas(ground.UsefulPixels);
                    Vector2 a = Gui(useful.min), b = Gui(useful.max);
                    Handles.DrawSolidRectangleWithOutline(Rect.MinMaxRect(a.x, a.y, b.x, b.y), Color.clear, new Color(.4f, .9f, 1f, .5f));
                }
            }
            if (cropRect.HasValue && tab == Tab.Ground)
            {
                // Что останется (итоговая рамка с добором до сетки) и что отрежется — затемнено.
                RectInt keep = cropPlan != null && cropPlan.CanApply && !dragging ? cropPlan.Canvas : cropRect.Value;
                Vector2 a = Gui(new Vector2(keep.xMin, keep.yMin)), b = Gui(new Vector2(keep.xMax, keep.yMax));
                Vector2 c0 = Gui(Vector2.zero), c1 = Gui(CanvasSize);
                Color dim = new Color(0, 0, 0, .55f);
                Handles.DrawSolidRectangleWithOutline(Rect.MinMaxRect(c0.x, c0.y, c1.x, a.y), dim, Color.clear);
                Handles.DrawSolidRectangleWithOutline(Rect.MinMaxRect(c0.x, b.y, c1.x, c1.y), dim, Color.clear);
                Handles.DrawSolidRectangleWithOutline(Rect.MinMaxRect(c0.x, a.y, a.x, b.y), dim, Color.clear);
                Handles.DrawSolidRectangleWithOutline(Rect.MinMaxRect(b.x, a.y, c1.x, b.y), dim, Color.clear);
                Handles.DrawSolidRectangleWithOutline(Rect.MinMaxRect(a.x, a.y, b.x, b.y), Color.clear, new Color(1, .55f, .1f, 1));
                GUI.Label(new Rect(a.x + 4, a.y + 2, 260, 16), "обрезка " + keep.width + "×" + keep.height, EditorStyles.whiteMiniLabel);
            }
            if (showCameraFrame && tab == Tab.Ground && Visual != null)
            {
                Rect bounds = LocationCameraFollow.ResolveBounds(Visual, location);
                Vector2 a = Gui(bounds.min), b = Gui(bounds.max);
                Handles.DrawSolidRectangleWithOutline(Rect.MinMaxRect(a.x, a.y, b.x, b.y), Color.clear, new Color(.5f, 1, .5f, .8f));
                float viewHeight = LocationCameraFollow.DefaultViewHeight(Visual, location);
                Vector2 view = new Vector2(viewHeight * 16f / 9f, viewHeight);
                Vector2 start = LocationVisualGeometry.ToPixel(location, Visual.TestStartPoint) + Visual.Camera.Offset;
                Vector2 center = LocationCameraFollow.ClampView(start, view, bounds);
                Vector2 c = Gui(center - view / 2), d = Gui(center + view / 2);
                Handles.DrawSolidRectangleWithOutline(Rect.MinMaxRect(c.x, c.y, d.x, d.y), new Color(.5f, 1, .5f, .04f), new Color(.5f, 1, .5f, .45f));
                GUI.Label(new Rect(c.x + 4, c.y + 2, 260, 16), "кадр 16:9 у старта теста", EditorStyles.miniLabel);
            }
            Handles.EndGUI();
            if (groundView != GroundView.Color && ground != null && ground.IsTiled)
            {
                // Нормали и высота показываются без света — о смене суток напоминает подпись.
                GUIStyle note = new GUIStyle(EditorStyles.helpBox) { fontSize = 11, normal = { textColor = new Color(1, .85f, .45f) } };
                GUI.Label(new Rect(8, frame.height - 30, 420, 22), "Показан " + groundView + " без света — время суток не видно. Вернуть: «Color».", note);
            }

            if (renderer?.Height != null && frame.Contains(mouse) && (tab == Tab.Ground || groundView == GroundView.Height))
            {
                Vector2 pixel = ToPixel(frame, mouse);
                string text;
                if (renderer.TrySampleHeightAtPixel(pixel, out HeightSample exact, true))
                {
                    renderer.TrySampleHeightAtPixel(pixel, out HeightSample smooth);
                    text = "Высота: " + exact.Meters.ToString("0.0000") + " м (" + exact.Blender.ToString("0.0000") + " BU) · " +
                           LocationGroundTile.KeyOf(exact.Column, exact.Row) + " · покрытие " + Mathf.RoundToInt(exact.Coverage * 100) + "%" +
                           (smooth.Valid ? "\nбилинейно " + smooth.Meters.ToString("0.0000") + " м" + (smooth.Discontinuity ? " · разрыв" : "") : "");
                }
                else text = "Высота: " + exact.Reason;
                GUIStyle box = new GUIStyle(EditorStyles.helpBox) { fontSize = 11, normal = { textColor = Color.white } };
                GUI.Label(new Rect(8, 8, 330, 36), text, box);
            }
        }
    }
}

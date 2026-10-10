using System;
using System.Collections.Generic;
using System.Linq;
using KingdomSurvival.ArtAssets;
using KingdomSurvival.ArtAssets.Editor;
using KingdomSurvival.BattlefieldDatabase;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace KingdomSurvival.LocationRendering.Editor
{
    // ПР-12Р: вкладка «Раскидка» — слои травы, камней, мха; кисть с набором
    // ассетов и разбросом размера, формы, поворота, отражения и цвета; ластик,
    // перекраска, заливка всего места. Мазок дополняет предпросмотр сразу,
    // без пересборки места; один мазок — один шаг Undo.
    public sealed partial class LocationDatabaseWindow
    {
        private string scatterLayerId;
        private LocationScatterTool scatterTool = LocationScatterTool.Paint;
        private LocationScatterTool strokeTool;
        private bool scatterStroke;
        private LocationScatterIndex scatterIndex;
        private System.Random scatterRandom;
        private Func<Vector2, bool> scatterMask;
        private Vector2 lastStamp;
        private double carpetRefreshAt;
        private readonly HashSet<int> strokeChanged = new HashSet<int>();
        // Мазок поверх уже стоящих: плотность считается только по его экземплярам.
        private bool strokeOver;
        private readonly HashSet<int> strokeAdded = new HashSet<int>();

        // Больше этого в слое объектами — предложить ковёр.
        private const int HeavyObjectLayer = 3000;

        private LocationScatterLayer ScatterLayer
        {
            get
            {
                List<LocationScatterLayer> layers = Visual?.ScatterLayers;
                if (layers == null || layers.Count == 0) return null;
                LocationScatterLayer layer = layers.Find(item => item != null && item.Id == scatterLayerId);
                if (layer == null) { layer = layers.FirstOrDefault(item => item != null); scatterLayerId = layer?.Id; }
                return layer;
            }
        }

        private static readonly string[] ScatterToolNames = { "Кисть", "Ластик", "Перекрасить" };

        private string ScatterHint() =>
            "Раскидка «" + (ScatterLayer?.Name ?? "—") + "»: ЛКМ — " + ScatterToolNames[(int)scatterTool].ToLowerInvariant() +
            "; Shift — ластик, Ctrl — перекрасить; [ ] — радиус; Esc — закончить.";

        // Правка кисти: Undo и запись, без пересборки предпросмотра.
        private void BrushEdit(Action action)
        {
            Undo.RecordObject(database, "Кисть раскидки");
            action();
            EditorUtility.SetDirty(database);
            saveAt = EditorApplication.timeSinceStartup + .5;
            if (tool == Tool.Scatter) status.text = ScatterHint();
        }

        // ------------------------------------------------------------------
        // Панель
        // ------------------------------------------------------------------

        private void BuildScatterSettings(LocalLocationDefinition location)
        {
            LocationVisualDefinition visual = Visual;
            if (visual == null)
            {
                Heading("Раскидка");
                Help("Для раскидки нужна художественная сборка места (вкладка «Предметы»).");
                return;
            }
            if (visual.ScatterLayers == null) visual.ScatterLayers = new List<LocationScatterLayer>();
            Heading("Слои раскидки");
            Help("Трава, камни, мох, цветы — кистью, сотнями штук. «Объекты» — каждый экземпляр как предмет: прячет героя по глубине, " +
                 "нормали, тени, может мешать проходу. «Ковёр» — тысячи штук одной пачкой, почти бесплатно, одним слоем под людьми (или над ними), " +
                 "без теней и проходимости — для низкой травы и мелочи.");
            VisualElement add = Row();
            AddButton(add, "+ Слой объектами", () => AddScatterLayer(LocationScatterMode.Objects));
            AddButton(add, "+ Слой ковром", () => AddScatterLayer(LocationScatterMode.Carpet));
            LocationScatterLayer selected = ScatterLayer;
            foreach (LocationScatterLayer layer in visual.ScatterLayers.Where(item => item != null))
                BuildScatterLayerRow(visual, layer, layer == selected);
            if (selected == null) return;

            BuildScatterTools(selected);
            BuildScatterLayerSettings(selected);
            BuildScatterAssets(selected);
            BuildScatterBrush(selected);
            BuildScatterLook(selected);
            BuildScatterColor(selected);
            BuildScatterMask(selected);
            BuildScatterActions(location, visual, selected);
        }

        // Строка слоя: выбрать, скрыть, удалить — сразу в списке слоёв.
        private void BuildScatterLayerRow(LocationVisualDefinition visual, LocationScatterLayer layer, bool selected)
        {
            VisualElement row = Row();
            row.style.flexWrap = Wrap.NoWrap;
            row.style.alignItems = Align.Center;
            string id = layer.Id;
            string title = (selected ? "● " : "") + layer.Name + " · " + layer.Instances.Count + " шт. · " +
                           (layer.Mode == LocationScatterMode.Carpet ? "ковёр" : "объекты") + (layer.Locked ? " · закреплён" : "");
            Button select = new Button(() => { scatterLayerId = id; BuildSettings(); if (tool == Tool.Scatter) status.text = ScatterHint(); }) { text = title };
            select.style.flexGrow = 1;
            select.style.flexShrink = 1;
            select.style.unityTextAlign = TextAnchor.MiddleLeft;
            if (selected) { select.style.unityFontStyleAndWeight = FontStyle.Bold; select.style.color = new Color(.95f, .82f, .5f); }
            if (layer.Hidden) select.style.opacity = .55f;
            row.Add(select);
            UnityEngine.UIElements.Toggle hidden = new UnityEngine.UIElements.Toggle("скрыть") { value = layer.Hidden, tooltip = "Скрыть слой в месте (экземпляры остаются)" };
            hidden.labelElement.style.minWidth = 0;
            hidden.labelElement.style.paddingRight = 2;
            hidden.style.flexShrink = 0;
            hidden.style.marginLeft = 6;
            hidden.RegisterValueChangedCallback(evt => Change(() => layer.Hidden = evt.newValue, true));
            row.Add(hidden);
            Button delete = new Button(() =>
            {
                if (!EditorUtility.DisplayDialog("Удалить слой?", "Слой «" + layer.Name + "» и все его экземпляры (" + layer.Instances.Count + " шт.) будут удалены.", "Удалить", "Отмена")) return;
                Change(() => { visual.ScatterLayers.Remove(layer); if (scatterLayerId == id) scatterLayerId = null; }, true);
                if (tool == Tool.Scatter && ScatterLayer == null) SetTool(Tool.Select);
            }) { text = "удалить", tooltip = "Удалить слой и все его экземпляры" };
            delete.style.flexShrink = 0;
            row.Add(delete);
        }

        private void BuildScatterTools(LocationScatterLayer layer)
        {
            Heading("Рисовать");
            VisualElement row = Row();
            for (int i = 0; i < ScatterToolNames.Length; i++)
            {
                LocationScatterTool value = (LocationScatterTool)i;
                bool active = tool == Tool.Scatter && scatterTool == value;
                Button button = new Button(() => { scatterTool = value; SetTool(Tool.Scatter); }) { text = (active ? "● " : "") + ScatterToolNames[i] };
                if (active) { button.style.unityFontStyleAndWeight = FontStyle.Bold; button.style.color = new Color(.95f, .82f, .5f); }
                row.Add(button);
            }
            if (tool == Tool.Scatter) AddButton(row, "Закончить", () => SetTool(Tool.Select));
            Label hint = new Label("Shift — ластик, Ctrl — перекрасить, [ и ] — радиус, Esc — закончить. Один мазок — один шаг Ctrl+Z.");
            hint.style.whiteSpace = WhiteSpace.Normal;
            hint.style.color = new Color(.68f, .74f, .68f);
            settings.Add(hint);
            if (layer.Locked) settings.Add(new HelpBox("Слой закреплён: кисть его не меняет.", HelpBoxMessageType.Warning));
            if (layer.Hidden) settings.Add(new HelpBox("Слой скрыт: мазки не видны, пока его не показать.", HelpBoxMessageType.Warning));
        }

        private void BuildScatterLayerSettings(LocationScatterLayer layer)
        {
            Heading("Слой");
            Label count = new Label("Экземпляров: " + layer.Instances.Count);
            count.style.color = new Color(.75f, .8f, .75f);
            settings.Add(count);
            if (layer.Mode == LocationScatterMode.Objects && layer.Instances.Count > HeavyObjectLayer)
                settings.Add(new HelpBox("В слое объектами больше " + HeavyObjectLayer + " штук — место может тормозить. Низкую траву и мелочь лучше вести ковром.",
                    HelpBoxMessageType.Warning));
            Text("Название", layer.Name, value => layer.Name = string.IsNullOrWhiteSpace(value) ? layer.Name : value.Trim());
            List<LocationScatterMode> modes = new List<LocationScatterMode> { LocationScatterMode.Objects, LocationScatterMode.Carpet };
            Choice("Как рисовать", modes, mode => mode == LocationScatterMode.Carpet ? "Ковёр (тысячи штук пачкой)" : "Объекты", layer.Mode, value => layer.Mode = value);
            List<LocationVisualBand> bands = layer.Mode == LocationScatterMode.Carpet
                ? new List<LocationVisualBand> { LocationVisualBand.GroundDetail, LocationVisualBand.Foreground }
                : new List<LocationVisualBand> { LocationVisualBand.GroundDetail, LocationVisualBand.World, LocationVisualBand.Foreground };
            LocationVisualBand band = layer.Mode == LocationScatterMode.Carpet ? layer.CarpetBand : layer.Band;
            Choice("Слой рисунка", bands, BandTitle, band, value => layer.Band = value);
            Number("Порядок внутри слоя", layer.OrderOffset, -1000, 1000, value => layer.OrderOffset = Mathf.RoundToInt(value));
            Toggle("Закрепить (кисть не меняет)", layer.Locked, value => layer.Locked = value, true);
            if (layer.Mode == LocationScatterMode.Objects)
            {
                Toggle("Тень от солнца (силуэт)", layer.ProjectsShadow, value => layer.ProjectsShadow = value);
                settings[settings.childCount - 1].tooltip = "Силуэт рисунка ложится на землю от солнца и луны — как «Тень от солнца» части в Базе ассетов.";
                Toggle("Тень от огня (по контуру рисунка)", layer.ProjectsFireShadow, value => layer.ProjectsFireShadow = value);
                settings[settings.childCount - 1].tooltip = "Костёр и другие местные источники дают за экземпляром тёмный конус — как «Тень от огня» части в Базе ассетов. " +
                                                            "Сотни экземпляров с этой тенью дороги: траве обычно не нужна.";
                if (layer.Band != LocationVisualBand.World)
                    Help("Тени — только у слоя «Объекты и персонажи»: детали земли и передний план их не отбрасывают.");
                Toggle("Проходимость — как у ассета (камни мешают)", layer.BlocksMovement, value => layer.BlocksMovement = value);
            }
            else Help("Ковёр не сортируется с людьми, не отбрасывает тени и не мешает проходу. Анимация ассетов идёт, фаза разложена по группам.");
        }

        private static string BandTitle(LocationVisualBand band)
        {
            switch (band)
            {
                case LocationVisualBand.GroundDetail: return "Детали земли (всегда под людьми)";
                case LocationVisualBand.Foreground: return "Передний план (всегда над людьми)";
                case LocationVisualBand.Ground: return "Земля";
                default: return "Объекты и персонажи (по глубине)";
            }
        }

        private void BuildScatterAssets(LocationScatterLayer layer)
        {
            Heading("Ассеты кисти");
            Help("Несколько ассетов — кисть выбирает по весу: вес 2 выпадает вдвое чаще, чем 1. «Размер ×» — свой множитель размера ассета.");
            ArtAssetDatabaseAsset catalog = ArtAssetDatabaseAsset.Current;
            for (int i = 0; i < layer.Assets.Count; i++)
            {
                LocationScatterEntry entry = layer.Assets[i];
                if (entry == null) continue;
                ArtAssetDefinition asset = ArtAssetDatabaseAsset.FindCurrent(entry.AssetId);
                VisualElement row = Row();
                row.style.flexWrap = Wrap.NoWrap;
                row.style.alignItems = Align.Center;
                row.style.marginBottom = 2;
                const float size = 34;
                IMGUIContainer thumb = new IMGUIContainer(() =>
                {
                    if (Event.current.type != EventType.Repaint) return;
                    Rect rect = new Rect(0, 0, size, size);
                    EditorGUI.DrawRect(rect, new Color(0, 0, 0, .25f));
                    if (asset != null) ArtAssetDrawing.DrawFitted(catalog, asset, layer.Brush.View, new Rect(2, 2, size - 4, size - 4));
                }) { style = { width = size, height = size, flexShrink = 0 } };
                row.Add(thumb);
                UnityEngine.UIElements.Toggle enabled = new UnityEngine.UIElements.Toggle { value = entry.Enabled, tooltip = "В кисти" };
                enabled.RegisterValueChangedCallback(evt => BrushEdit(() => entry.Enabled = evt.newValue));
                row.Add(enabled);
                Label name = new Label(asset != null ? asset.Name : "нет в Базе: " + entry.AssetId) { style = { flexGrow = 1, flexShrink = 1, overflow = Overflow.Hidden } };
                if (asset == null) name.style.color = new Color(1, .5f, .4f);
                row.Add(name);
                FloatField weight = new FloatField("вес") { value = entry.Weight, isDelayed = true, tooltip = "Как часто выпадает" };
                weight.labelElement.style.minWidth = 24;
                weight.style.width = 78;
                weight.RegisterValueChangedCallback(evt => BrushEdit(() => entry.Weight = Mathf.Max(0, evt.newValue)));
                row.Add(weight);
                FloatField scale = new FloatField("×") { value = entry.ScaleMultiplier, isDelayed = true, tooltip = "Свой множитель размера ассета" };
                scale.labelElement.style.minWidth = 12;
                scale.style.width = 62;
                scale.RegisterValueChangedCallback(evt => BrushEdit(() => entry.ScaleMultiplier = Mathf.Max(.01f, evt.newValue)));
                row.Add(scale);
                int index = i;
                row.Add(new Button(() => { BrushEdit(() => layer.Assets.RemoveAt(index)); BuildSettings(); }) { text = "✕", tooltip = "Убрать из кисти (поставленные остаются)" });
            }
            VisualElement actions = Row();
            AddButton(actions, "+ Ассет из Базы…", () => ArtAssetPicker.Show("Ассет в кисть «" + layer.Name + "»", id =>
            {
                if (layer.Assets.Any(item => item != null && item.AssetId == id)) { status.text = "Этот ассет уже в кисти."; return; }
                BrushEdit(() => layer.Assets.Add(new LocationScatterEntry { AssetId = id }));
                BuildSettings();
            }));
            Label drop = new Label("…или перетащите ассеты из окна «База ассетов» сюда");
            drop.style.whiteSpace = WhiteSpace.Normal;
            drop.style.unityTextAlign = TextAnchor.MiddleCenter;
            drop.style.paddingTop = drop.style.paddingBottom = 8;
            drop.style.color = new Color(.7f, .76f, .7f);
            drop.style.borderTopWidth = drop.style.borderBottomWidth = drop.style.borderLeftWidth = drop.style.borderRightWidth = 1;
            drop.style.borderTopColor = drop.style.borderBottomColor = drop.style.borderLeftColor = drop.style.borderRightColor = new Color(.45f, .5f, .45f);
            drop.RegisterCallback<DragUpdatedEvent>(evt =>
            {
                DragAndDrop.visualMode = string.IsNullOrEmpty(ArtAssetPicker.DraggedAssetId()) ? DragAndDropVisualMode.Rejected : DragAndDropVisualMode.Copy;
                evt.StopPropagation();
            });
            drop.RegisterCallback<DragPerformEvent>(evt =>
            {
                string id = ArtAssetPicker.DraggedAssetId();
                evt.StopPropagation();
                if (string.IsNullOrEmpty(id)) return;
                DragAndDrop.AcceptDrag();
                ArtAssetPicker.EndDrag();
                EditorApplication.delayCall += () =>
                {
                    if (layer.Assets.All(item => item == null || item.AssetId != id)) BrushEdit(() => layer.Assets.Add(new LocationScatterEntry { AssetId = id }));
                    BuildSettings();
                };
            });
            settings.Add(drop);
        }

        private void BuildScatterBrush(LocationScatterLayer layer)
        {
            LocationScatterBrush brush = layer.Brush;
            Heading("Кисть");
            BrushNumber("Радиус (пиксели)", brush.Radius, 4, 1000, value => brush.Radius = Mathf.Max(1, value), "Размер круга кисти на рисунке места. [ и ] — меньше / больше.");
            Label spacing = new Label();
            void Spacing(float density) => spacing.text = density > 0
                ? "В среднем одна штука на квадрат ≈ " + Mathf.RoundToInt(100 / Mathf.Sqrt(density)) + "×" + Mathf.RoundToInt(100 / Mathf.Sqrt(density)) + " пикс."
                : "Плотность 0 — кисть ничего не ставит.";
            BrushLogNumber("Плотность (шт. на 100×100 пикс.)", brush.Density, .01f, 60, value => { brush.Density = Mathf.Max(0, value); Spacing(brush.Density); },
                "Итоговая плотность мазка. Шкала ползунка логарифмическая: 0,01…1 так же точны, как 1…60; точное число — в поле справа.");
            spacing.style.color = new Color(.68f, .74f, .68f);
            spacing.style.marginLeft = 150;
            spacing.style.fontSize = 11;
            Spacing(brush.Density);
            settings.Add(spacing);
            BrushNumber("Нажим (за отпечаток)", brush.Flow, .01f, 1, value => brush.Flow = Mathf.Clamp(value, .01f, 1),
                "Какую долю недостающего до плотности добавляет один отпечаток кисти. 1 — сразу до плотности; 0,1 — мазок набирает постепенно: " +
                "водите по месту, пока не станет достаточно.");
            BrushNumber("Наименьшее расстояние (пикс.)", brush.MinDistance, 0, 400, value => brush.MinDistance = Mathf.Max(0, value),
                "Между опорами, в том числе до уже стоящих экземпляров слоя (с «Рисовать поверх» — только внутри мазка).");
            BrushNumber("Спад к краю", brush.Falloff, 0, 1, value => brush.Falloff = Mathf.Clamp01(value), "0 — ровно до края круга, 1 — к краю редеет до нуля.");
            BrushToggle("Рисовать поверх уже стоящих", brush.PaintOver, value => brush.PaintOver = value);
            settings[settings.childCount - 1].tooltip = "Мазок не считает прежние экземпляры слоя — ни для плотности, ни для наименьшего расстояния: " +
                                                        "каждый новый мазок досыпает ещё столько же поверх. Наименьшее расстояние действует внутри мазка. " +
                                                        "«Залить всё место» тогда добавляет ещё один слой плотности.";
            BrushToggle("Ластик и перекраска — только ассеты кисти", brush.OnlyBrushAssets, value => brush.OnlyBrushAssets = value);
        }

        private void BuildScatterLook(LocationScatterLayer layer)
        {
            LocationScatterBrush brush = layer.Brush;
            Heading("Размер и форма");
            BrushRange("Размер", brush.Scale, .1f, 3, value => brush.Scale = value, "Множитель игрового размера ассета: случайный между «от» и «до».");
            BrushRange("Растяжение по ширине", brush.Stretch, .2f, 3, value => brush.Stretch = value, "Меньше 1 — уже (сжатие), больше — шире.");
            BrushRange("Поворот (°)", brush.Rotation, -180, 180, value => brush.Rotation = value, "Вокруг опоры; плюс — против часовой. Траве хватает ±8°.");
            List<LocationScatterFlip> flips = new List<LocationScatterFlip> { LocationScatterFlip.Never, LocationScatterFlip.Random, LocationScatterFlip.Always };
            BrushChoice("Отражение", flips, flip => flip == LocationScatterFlip.Random ? "Случайно" : flip == LocationScatterFlip.Always ? "Всегда" : "Никогда",
                brush.Flip, value => brush.Flip = value);
            List<LocationScatterViewMode> viewModes = new List<LocationScatterViewMode> { LocationScatterViewMode.Fixed, LocationScatterViewMode.Random };
            BrushChoice("Ракурс", viewModes, mode => mode == LocationScatterViewMode.Random ? "Случайный из имеющихся у ассета" : "Заданный",
                brush.ViewMode, value => brush.ViewMode = value, true);
            if (brush.ViewMode == LocationScatterViewMode.Fixed)
                BrushChoice("Заданный ракурс", ArtAssetLabels.Views.ToList(), ArtAssetLabels.ViewTitle, brush.View, value => brush.View = value, true);
        }

        private void BuildScatterColor(LocationScatterLayer layer)
        {
            LocationScatterBrush brush = layer.Brush;
            Heading("Цвет");
            BrushNumber("Тон: общий сдвиг (°)", brush.HueShift, -180, 180, value => brush.HueShift = value, "Поворот цветового круга: зелень → желтизна → рыжина.");
            BrushNumber("Тон: разброс ± (°)", brush.HueJitter, 0, 180, value => brush.HueJitter = Mathf.Abs(value), "Каждому экземпляру — свой сдвиг тона в этих пределах.");
            BrushRange("Насыщенность", brush.Saturation, 0, 2, value => brush.Saturation = value, "Множитель: 0 — серый, 1 — как есть, 2 — вдвое ярче цвет.");
            BrushRange("Яркость", brush.Brightness, 0, 2, value => brush.Brightness = value, "Множитель светлоты: разброс оживляет заросль.");
            BrushColor("Подкраска: от", brush.TintA, value => brush.TintA = value);
            BrushColor("Подкраска: до", brush.TintB, value => brush.TintB = value);
            Label hint = new Label("Подкраска умножает цвет: белый — без изменений. Каждому экземпляру — случайный цвет между «от» и «до».");
            hint.style.whiteSpace = WhiteSpace.Normal;
            hint.style.color = new Color(.68f, .74f, .68f);
            settings.Add(hint);
        }

        private void BuildScatterMask(LocationScatterLayer layer)
        {
            LocationScatterBrush brush = layer.Brush;
            Heading("Где можно");
            BrushToggle("Только на проходимом", brush.OnlyPassable, value => brush.OnlyPassable = value);
            BrushToggle("Не на основаниях предметов", brush.AvoidObjects, value => brush.AvoidObjects = value);
            Label title = new Label("Только на местности (ничего не отмечено — на любой):");
            title.style.whiteSpace = WhiteSpace.Normal;
            title.style.marginTop = 4;
            settings.Add(title);
            VisualElement grid = Row();
            foreach (WorldMapGameplayTerrainType type in WorldMapTerrainLabels.All)
            {
                WorldMapGameplayTerrainType value = type;
                UnityEngine.UIElements.Toggle toggle = new UnityEngine.UIElements.Toggle(WorldMapTerrainLabels.Name(type)) { value = brush.Terrain.Contains(type) };
                toggle.style.width = 150;
                toggle.labelElement.style.minWidth = 100;
                toggle.RegisterValueChangedCallback(evt => BrushEdit(() =>
                {
                    brush.Terrain.Remove(value);
                    if (evt.newValue) brush.Terrain.Add(value);
                }));
                grid.Add(toggle);
            }
        }

        private void BuildScatterActions(LocalLocationDefinition location, LocationVisualDefinition visual, LocationScatterLayer layer)
        {
            Heading("Действия со слоем");
            VisualElement row = Row();
            AddButton(row, "Залить всё место", () => FillScatter(location, visual, layer));
            AddButton(row, "Перекрасить весь слой", () =>
            {
                if (layer.Locked) { status.text = "Слой закреплён."; return; }
                Change(() => LocationScatterPainter.Recolor(layer, location, null, new System.Random(Environment.TickCount)), true);
                status.text = "Облик всех экземпляров разыгран заново по кисти.";
            });
            AddButton(row, "Очистить слой", () =>
            {
                if (!EditorUtility.DisplayDialog("Очистить слой?", "Убрать все экземпляры слоя «" + layer.Name + "» (" + layer.Instances.Count + " шт.)? Кисть и ассеты останутся.", "Очистить", "Отмена")) return;
                Change(() => layer.Instances.Clear(), true);
            });
            AddButton(row, "Дублировать слой", () => Change(() =>
            {
                LocationScatterLayer copy = JsonUtility.FromJson<LocationScatterLayer>(JsonUtility.ToJson(layer));
                copy.Id = Guid.NewGuid().ToString("N");
                copy.Name = layer.Name + " (копия)";
                copy.Instances.Clear();
                copy.NextKey = 1;
                visual.ScatterLayers.Add(copy);
                scatterLayerId = copy.Id;
            }, true));
        }

        private void AddScatterLayer(LocationScatterMode mode)
        {
            LocationVisualDefinition visual = Visual;
            if (visual == null) return;
            Change(() =>
            {
                LocationScatterLayer layer = new LocationScatterLayer
                {
                    Name = mode == LocationScatterMode.Carpet ? "Низкая трава" : "Трава",
                    Mode = mode, Band = mode == LocationScatterMode.Carpet ? LocationVisualBand.GroundDetail : LocationVisualBand.World
                };
                if (mode == LocationScatterMode.Carpet)
                {
                    layer.Brush.Density = 12; layer.Brush.MinDistance = 6;
                    // Ковёр живее с разбросом облика: ширина, насыщенность, яркость ±15 %.
                    layer.Brush.Stretch = new Vector2(.85f, 1.15f);
                    layer.Brush.Saturation = new Vector2(.85f, 1.15f);
                    layer.Brush.Brightness = new Vector2(.85f, 1.15f);
                }
                // Новый слой — с ассетами кисти выбранного слоя: удобно для «трава ковром + трава объектами».
                LocationScatterLayer current = ScatterLayer;
                if (current != null)
                    foreach (LocationScatterEntry entry in current.Assets.Where(item => item != null))
                        layer.Assets.Add(new LocationScatterEntry { AssetId = entry.AssetId, Weight = entry.Weight, ScaleMultiplier = entry.ScaleMultiplier, Enabled = entry.Enabled });
                visual.ScatterLayers.Add(layer);
                scatterLayerId = layer.Id;
            }, true);
            status.text = "Слой добавлен. Добавьте ассеты в кисть и рисуйте: «Кисть» и ЛКМ по месту.";
        }

        private void FillScatter(LocalLocationDefinition location, LocationVisualDefinition visual, LocationScatterLayer layer)
        {
            if (layer.Locked) { status.text = "Слой закреплён."; return; }
            if (LocationScatterPainter.PickEntry(layer, new System.Random(1)) == null) { status.text = "Добавьте в кисть хотя бы один ассет."; return; }
            Vector2 canvas = LocationVisualGeometry.CanvasSize(location);
            int expected = Mathf.FloorToInt(layer.Brush.Density / LocationScatterPainter.DensityArea * canvas.x * canvas.y) +
                           (layer.Brush.PaintOver ? layer.Instances.Count : 0);
            if (layer.Mode == LocationScatterMode.Objects && expected > HeavyObjectLayer &&
                !EditorUtility.DisplayDialog("Много объектов", "При этой плотности выйдет около " + expected + " объектов — место может тормозить. " +
                                                              "Для низкой травы лучше слой ковром. Всё равно залить?", "Залить", "Отмена"))
                return;
            int added = 0;
            Change(() =>
            {
                bool over = layer.Brush.PaintOver;
                LocationScatterIndex index = over ? new LocationScatterIndex(Mathf.Max(8, layer.Brush.MinDistance)) : LocationScatterIndex.Of(layer, location);
                added = LocationScatterPainter.Fill(layer, location, new System.Random(Environment.TickCount),
                    LocationScatterPainter.Mask(layer.Brush, location, visual), index, null, 20000, over).Count;
            }, true);
            status.text = "Залито: +" + added + " шт. (с учётом «Где можно» и наименьшего расстояния" + (layer.Brush.PaintOver ? ", поверх уже стоящих" : "") + ").";
        }

        // ------------------------------------------------------------------
        // Поля кисти (без пересборки места)
        // ------------------------------------------------------------------

        private void BrushNumber(string label, float value, float min, float max, Action<float> set, string help = null)
        {
            Slider field = new Slider(label, Mathf.Min(min, value), Mathf.Max(max, value)) { value = value, showInputField = true, tooltip = help };
            field.RegisterValueChangedCallback(evt => BrushEdit(() => set(evt.newValue)));
            settings.Add(field);
        }

        // Число в логарифмической шкале (min > 0): малые значения так же
        // точны, как большие. Поле справа — точное число (можно и 0).
        private void BrushLogNumber(string label, float value, float min, float max, Action<float> set, string help = null)
        {
            VisualElement row = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center }, tooltip = help };
            Label title = new Label(label) { tooltip = help };
            title.style.width = 150;
            title.style.flexShrink = 0;
            title.style.whiteSpace = WhiteSpace.Normal;
            row.Add(title);
            float low = Mathf.Log(min), high = Mathf.Log(max);
            float ToSlider(float number) => Mathf.InverseLerp(low, high, Mathf.Log(Mathf.Clamp(number, min, max)));
            float FromSlider(float t) => Mathf.Exp(Mathf.Lerp(low, high, t));
            float Round(float number) => (float)Math.Round(number, number < .1f ? 3 : number < 10 ? 2 : 1);
            Slider slider = new Slider(0, 1) { value = ToSlider(value) };
            slider.style.flexGrow = 1;
            FloatField field = new FloatField { value = value, isDelayed = true };
            field.style.width = 60;
            slider.RegisterValueChangedCallback(evt =>
            {
                float number = Round(FromSlider(evt.newValue));
                field.SetValueWithoutNotify(number);
                BrushEdit(() => set(number));
            });
            field.RegisterValueChangedCallback(evt =>
            {
                float number = Mathf.Max(0, evt.newValue);
                field.SetValueWithoutNotify(number);
                slider.SetValueWithoutNotify(ToSlider(number));
                BrushEdit(() => set(number));
            });
            row.Add(slider);
            row.Add(field);
            settings.Add(row);
        }

        private void BrushToggle(string label, bool value, Action<bool> set)
        {
            UnityEngine.UIElements.Toggle field = new UnityEngine.UIElements.Toggle(label) { value = value };
            field.RegisterValueChangedCallback(evt => BrushEdit(() => set(evt.newValue)));
            settings.Add(field);
        }

        private void BrushColor(string label, Color value, Action<Color> set)
        {
            UnityEditor.UIElements.ColorField field = new UnityEditor.UIElements.ColorField(label) { value = value, showAlpha = false };
            field.RegisterValueChangedCallback(evt => BrushEdit(() => set(evt.newValue)));
            settings.Add(field);
        }

        private void BrushChoice<T>(string label, List<T> options, Func<T, string> name, T current, Action<T> set, bool refresh = false)
        {
            PopupField<string> field = new PopupField<string>(label, options.Select(name).ToList(), Mathf.Max(0, options.IndexOf(current)));
            field.RegisterValueChangedCallback(evt =>
            {
                BrushEdit(() => set(options[field.index]));
                if (refresh) BuildSettings();
            });
            settings.Add(field);
        }

        // Разброс «от — до»: ползунок с двумя ручками и поля чисел.
        private void BrushRange(string label, Vector2 value, float min, float max, Action<Vector2> set, string help = null)
        {
            VisualElement row = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center }, tooltip = help };
            Label title = new Label(label) { tooltip = help };
            title.style.width = 150;
            title.style.flexShrink = 0;
            row.Add(title);
            FloatField from = new FloatField { value = value.x, isDelayed = true };
            from.style.width = 52;
            MinMaxSlider slider = new MinMaxSlider(Mathf.Min(value.x, value.y), Mathf.Max(value.x, value.y), Mathf.Min(min, value.x), Mathf.Max(max, value.y));
            slider.style.flexGrow = 1;
            FloatField to = new FloatField { value = value.y, isDelayed = true };
            to.style.width = 52;
            void Apply(Vector2 range)
            {
                range = new Vector2(Mathf.Min(range.x, range.y), Mathf.Max(range.x, range.y));
                from.SetValueWithoutNotify((float)Math.Round(range.x, 3));
                to.SetValueWithoutNotify((float)Math.Round(range.y, 3));
                slider.lowLimit = Mathf.Min(slider.lowLimit, range.x);
                slider.highLimit = Mathf.Max(slider.highLimit, range.y);
                slider.SetValueWithoutNotify(range);
                BrushEdit(() => set(range));
            }
            slider.RegisterValueChangedCallback(evt => Apply(evt.newValue));
            from.RegisterValueChangedCallback(evt => Apply(new Vector2(evt.newValue, to.value)));
            to.RegisterValueChangedCallback(evt => Apply(new Vector2(from.value, evt.newValue)));
            row.Add(from);
            row.Add(slider);
            row.Add(to);
            settings.Add(row);
        }

        // Облик предмета, поставленного вручную (вкладка «Предметы»).
        private void LookSettings(LocationVisualObject item)
        {
            if (item.ColorAdjust == null) item.ColorAdjust = new LocationColorAdjust();
            LocationColorAdjust color = item.ColorAdjust;
            Number("Растяжение по ширине", item.Stretch, .2f, 3, value => item.Stretch = Mathf.Max(.05f, value), "1 — как есть; меньше — уже, больше — шире.");
            Number("Поворот (°, + против часовой)", item.Rotation, -180, 180, value => item.Rotation = value, "Вокруг опоры.");
            Number("Тон (сдвиг, °)", color.Hue, -180, 180, value => color.Hue = value);
            Number("Насыщенность", color.Saturation, 0, 2, value => color.Saturation = Mathf.Max(0, value));
            Number("Яркость", color.Brightness, 0, 2, value => color.Brightness = Mathf.Max(0, value));
            ColorField("Подкраска", color.Tint, value => color.Tint = value);
        }

        // ------------------------------------------------------------------
        // Рисование мышью
        // ------------------------------------------------------------------

        private bool HandleScatterInput(Event evt, Rect frame, Vector2 pixel)
        {
            LocationScatterLayer layer = ScatterLayer;
            if (evt.type == EventType.KeyDown && evt.keyCode == KeyCode.Escape)
            {
                SetTool(Tool.Select); evt.Use(); return true;
            }
            if (evt.type == EventType.KeyDown && layer != null && (evt.keyCode == KeyCode.LeftBracket || evt.keyCode == KeyCode.RightBracket))
            {
                float factor = evt.keyCode == KeyCode.LeftBracket ? 1 / 1.15f : 1.15f;
                BrushEdit(() => layer.Brush.Radius = Mathf.Clamp(layer.Brush.Radius * factor, 2, 4000));
                BuildSettings();
                evt.Use(); return true;
            }
            if (evt.type == EventType.MouseDown && evt.button == 0 && frame.Contains(evt.mousePosition))
            {
                BeginScatterStroke(layer, evt, pixel);
                evt.Use(); return true;
            }
            if (evt.type == EventType.MouseDrag && evt.button == 0 && scatterStroke)
            {
                float spacing = Mathf.Max(3, (layer?.Brush.Radius ?? 10) * .3f);
                if ((pixel - lastStamp).sqrMagnitude >= spacing * spacing) ApplyScatter(layer, pixel);
                evt.Use(); return true;
            }
            if (evt.type == EventType.MouseUp && evt.button == 0 && scatterStroke)
            {
                EndScatterStroke(layer);
                evt.Use(); return true;
            }
            return false;
        }

        private void BeginScatterStroke(LocationScatterLayer layer, Event evt, Vector2 pixel)
        {
            if (layer == null) { status.text = "Сначала добавьте слой раскидки."; return; }
            if (layer.Locked) { status.text = "Слой «" + layer.Name + "» закреплён."; return; }
            strokeTool = evt.shift ? LocationScatterTool.Erase : (evt.control || evt.command) ? LocationScatterTool.Recolor : scatterTool;
            if (strokeTool == LocationScatterTool.Paint && LocationScatterPainter.PickEntry(layer, new System.Random(1)) == null)
            {
                status.text = "Добавьте в кисть хотя бы один ассет («+ Ассет из Базы…»).";
                return;
            }
            Undo.RecordObject(database, "Раскидка: " + ScatterToolNames[(int)strokeTool].ToLowerInvariant());
            // Поверх уже стоящих: прежние экземпляры не мешают ни плотности, ни расстоянию.
            strokeOver = layer.Brush.PaintOver;
            strokeAdded.Clear();
            scatterIndex = strokeOver ? new LocationScatterIndex(Mathf.Max(8, layer.Brush.MinDistance)) : LocationScatterIndex.Of(layer, Location);
            scatterRandom = new System.Random(Environment.TickCount);
            scatterMask = LocationScatterPainter.Mask(layer.Brush, Location, Visual);
            strokeChanged.Clear();
            scatterStroke = true;
            dragging = true;
            ApplyScatter(layer, pixel);
        }

        private void ApplyScatter(LocationScatterLayer layer, Vector2 pixel)
        {
            if (layer == null) return;
            lastStamp = pixel;
            List<int> recolored = null;
            bool changed;
            switch (strokeTool)
            {
                case LocationScatterTool.Erase:
                    changed = LocationScatterPainter.Erase(layer, Location, pixel, scatterIndex).Count > 0;
                    break;
                case LocationScatterTool.Recolor:
                    recolored = LocationScatterPainter.Recolor(layer, Location, pixel, scatterRandom, null, strokeChanged);
                    strokeChanged.UnionWith(recolored);
                    changed = recolored.Count > 0;
                    break;
                default:
                    List<LocationScatterInstance> added = LocationScatterPainter.Stamp(layer, Location, pixel, scatterRandom, scatterMask, scatterIndex,
                        null, strokeOver ? strokeAdded : null);
                    foreach (LocationScatterInstance instance in added) strokeAdded.Add(instance.Key);
                    changed = added.Count > 0;
                    break;
            }
            if (!changed) return;
            EditorUtility.SetDirty(database);
            if (renderer == null) return;
            if (layer.Mode == LocationScatterMode.Objects) renderer.RefreshScatter(layer, recolored);
            else if (EditorApplication.timeSinceStartup >= carpetRefreshAt)
            {
                renderer.RefreshScatter(layer);
                carpetRefreshAt = EditorApplication.timeSinceStartup + .08;
            }
        }

        private void EndScatterStroke(LocationScatterLayer layer)
        {
            scatterStroke = false;
            dragging = false;
            scatterIndex = null;
            scatterMask = null;
            EditorUtility.SetDirty(database);
            saveAt = EditorApplication.timeSinceStartup + .5;
            // Ковёр — последний мазок; камни, которые мешают, — новая проходимость.
            if (layer != null && layer.BlocksMovement && layer.Mode == LocationScatterMode.Objects) RebuildPreview();
            else if (layer != null) renderer?.RefreshScatter(layer);
            BuildSettings();
            status.text = ScatterHint() + "  Экземпляров: " + (layer?.Instances.Count ?? 0) + ".";
        }

        private void DrawScatterBrush(Vector2 center, float scale, Event evt)
        {
            LocationScatterLayer layer = ScatterLayer;
            if (layer == null) return;
            LocationScatterTool shown = scatterStroke ? strokeTool : evt.shift ? LocationScatterTool.Erase : (evt.control || evt.command) ? LocationScatterTool.Recolor : scatterTool;
            Color color = shown == LocationScatterTool.Erase ? new Color(1, .4f, .35f, .85f)
                : shown == LocationScatterTool.Recolor ? new Color(1, .85f, .35f, .85f) : new Color(.55f, 1, .55f, .85f);
            float radius = layer.Brush.Radius * scale;
            Handles.color = color;
            Handles.DrawWireDisc(center, Vector3.forward, radius);
            if (layer.Brush.Falloff > .01f)
            {
                Handles.color = new Color(color.r, color.g, color.b, .35f);
                Handles.DrawWireDisc(center, Vector3.forward, radius * (1 - layer.Brush.Falloff * .5f));
            }
        }
    }
}

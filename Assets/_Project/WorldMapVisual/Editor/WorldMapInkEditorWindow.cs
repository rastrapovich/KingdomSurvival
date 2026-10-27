using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace KingdomSurvival.WorldMapVisual.Editor
{
    // «Редактор туши»: облик каждого вида линий туши на карте свободной игры —
    // нажим, перо, концы, обрывы, штрихи внахлёст, дрожь руки, сухая кисть.
    // Правит стиль генератора карты (тот же ассет, что «Генератор карты»);
    // предпросмотр рисует той же кистью, что и карта. Окно генератора
    // перерисовывает карту при каждой правке.
    public sealed class WorldMapInkEditorWindow : EditorWindow
    {
        private const string PrefsKey = "KS.WorldMapInkEditor.";

        // true — правка не двигает край заливок: генератору хватит перерисовать
        // одну тушь.
        public static event Action<bool> Changed;

        private WorldMapGeneratorAsset generator;
        private VisualElement lineList;
        private ScrollView settingsScroll;
        private VisualElement parent;
        private InkPreview preview;

        private static WorldMapInkLine Selected
        {
            get => (WorldMapInkLine)Mathf.Clamp(EditorPrefs.GetInt(PrefsKey + "Line", 0), 0, Enum.GetValues(typeof(WorldMapInkLine)).Length - 1);
            set => EditorPrefs.SetInt(PrefsKey + "Line", (int)value);
        }

        private static float PreviewZoom
        {
            get => EditorPrefs.GetFloat(PrefsKey + "Zoom", 1.5f);
            set => EditorPrefs.SetFloat(PrefsKey + "Zoom", value);
        }

        private WorldMapArtStyle A => generator.Art;
        private WorldMapInkStyle Ink => generator.Art.Ink;
        private WorldMapStrokeStyle Stroke => Ink.Get(Selected);

        [MenuItem("Kingdom Survival/Редактор туши")]
        public static void Open()
        {
            WorldMapInkEditorWindow window = GetWindow<WorldMapInkEditorWindow>();
            window.titleContent = new GUIContent("Редактор туши");
            window.minSize = new Vector2(760f, 520f);
            window.Show();
        }

        private void OnEnable() => Undo.undoRedoPerformed += OnUndoRedo;

        private void OnDisable() => Undo.undoRedoPerformed -= OnUndoRedo;

        private void OnUndoRedo()
        {
            if (generator == null)
                return;
            Rebuild();
            Changed?.Invoke(false);
        }

        private void CreateGUI()
        {
            WorldMapDatabaseAsset database = Resources.Load<WorldMapDatabaseAsset>(WorldMapDatabaseAsset.ResourcesPath);
            generator = database != null ? database.Generator : null;
            VisualElement root = rootVisualElement;
            root.Clear();
            if (generator == null)
            {
                root.Add(new HelpBox("Генератор карты не найден. Откройте «Kingdom Survival → Генератор карты» и создайте его.", HelpBoxMessageType.Warning));
                return;
            }
            generator.Art.UpgradeInk();
            root.style.flexDirection = FlexDirection.Row;

            lineList = new VisualElement();
            lineList.style.width = 190;
            lineList.style.paddingLeft = lineList.style.paddingRight = 6;
            lineList.style.paddingTop = 6;
            lineList.style.borderRightWidth = 1;
            lineList.style.borderRightColor = new Color(0f, 0f, 0f, 0.4f);
            root.Add(lineList);

            VisualElement right = new VisualElement();
            right.style.flexGrow = 1;
            preview = new InkPreview(this);
            right.Add(preview);
            VisualElement zoomRow = new VisualElement { style = { flexDirection = FlexDirection.Row, paddingLeft = 6, paddingRight = 6 } };
            UnityEngine.UIElements.Slider zoom = new UnityEngine.UIElements.Slider("Приближение предпросмотра", 0.5f, 6f) { value = PreviewZoom, showInputField = true };
            zoom.style.flexGrow = 1;
            zoom.RegisterValueChangedCallback(evt => { PreviewZoom = evt.newValue; preview.MarkDirtyRepaint(); });
            zoomRow.Add(zoom);
            right.Add(zoomRow);
            settingsScroll = new ScrollView(ScrollViewMode.Vertical);
            settingsScroll.style.flexGrow = 1;
            right.Add(settingsScroll);
            root.Add(right);
            Rebuild();
        }

        private void Rebuild()
        {
            if (lineList == null || generator == null)
                return;
            BuildLineList();
            float scroll = settingsScroll.scrollOffset.y;
            settingsScroll.Clear();
            parent = settingsScroll.contentContainer;
            parent.style.paddingLeft = parent.style.paddingRight = 8;
            parent.style.paddingBottom = 12;
            BuildSettings();
            settingsScroll.schedule.Execute(() => settingsScroll.scrollOffset = new Vector2(0f, scroll));
            preview?.MarkDirtyRepaint();
        }

        private void BuildLineList()
        {
            lineList.Clear();
            lineList.Add(new Label("ЛИНИИ ТУШИ") { style = { unityFontStyleAndWeight = FontStyle.Bold, color = new Color(0.62f, 0.75f, 0.85f), marginBottom = 4 } });
            foreach (WorldMapInkLine line in Enum.GetValues(typeof(WorldMapInkLine)))
            {
                WorldMapInkLine item = line;
                WorldMapStrokeStyle stroke = Ink.Get(item);
                Button button = new Button(() => { Selected = item; Rebuild(); }) { text = (stroke.Enabled ? "● " : "○ ") + WorldMapInkStyle.Name(item) };
                button.style.unityTextAlign = TextAnchor.MiddleLeft;
                button.style.height = 24;
                if (item == Selected)
                {
                    button.style.backgroundColor = new Color(0.27f, 0.38f, 0.49f);
                    button.style.unityFontStyleAndWeight = FontStyle.Bold;
                }
                lineList.Add(button);
            }

            lineList.Add(new Label("ОБЩЕЕ") { style = { unityFontStyleAndWeight = FontStyle.Bold, color = new Color(0.62f, 0.75f, 0.85f), marginTop = 12, marginBottom = 4 } });
            VisualElement keep = parent;
            parent = lineList;
            ColorField("Цвет", A.InkColor, v => A.InkColor = v, 50);
            Slider("Толщина, px", A.InkWidth, 0f, 12f, v => A.InkWidth = v, null, 50);
            Toggle("Заливка по линии туши", Ink.FillsFollowInk, v => Ink.FillsFollowInk = v,
                "Где есть тушь (берег, кромка леса), заливка и узор обрываются ровно по линии туши — не за ней и не перед ней");
            parent = keep;
        }

        private void BuildSettings()
        {
            WorldMapInkLine line = Selected;
            WorldMapStrokeStyle s = Stroke;
            parent.Add(new Label(WorldMapInkStyle.Name(line)) { style = { unityFontStyleAndWeight = FontStyle.Bold, fontSize = 14, marginTop = 6 } });
            parent.Add(new HelpBox(Description(line), HelpBoxMessageType.None));

            Row(("Ровная линия", () => ApplyPreset(Preset.Even)), ("Тонкое перо", () => ApplyPreset(Preset.Pen)), ("Кисть с нажимом", () => ApplyPreset(Preset.Brush)),
                ("Сухая кисть", () => ApplyPreset(Preset.Dry)), ("Штрихи внахлёст", () => ApplyPreset(Preset.Hatch)), ("Дрожащая рука", () => ApplyPreset(Preset.Shaky)));
            Row(("Сбросить", () => Change(() => { bool on = s.Enabled; Ink.Set(line, WorldMapInkStyle.Default(line)); Ink.Get(line).Enabled = on; }, true)),
                ("Применить ко всем линиям", () => Change(() =>
                {
                    foreach (WorldMapInkLine other in Enum.GetValues(typeof(WorldMapInkLine)))
                    {
                        if (other == line)
                            continue;
                        WorldMapStrokeStyle target = Ink.Get(other);
                        float opacity = target.Opacity, width = target.Width;
                        bool ownColor = target.OwnColor;
                        Color color = target.Color;
                        target.CopyFrom(s);
                        target.Opacity = opacity; target.Width = width; target.OwnColor = ownColor; target.Color = color;
                    }
                }, true)));

            Section("Основное");
            Toggle("Рисовать", s.Enabled, v => s.Enabled = v, null, true);
            Slider("Непрозрачность", s.Opacity, 0f, 1f, v => s.Opacity = v);
            Slider(line == WorldMapInkLine.Trail ? "Толщина × ширины тропы" : "Толщина × общей", s.Width, 0f, 4f, v => s.Width = v);
            if (line != WorldMapInkLine.Trail)
            {
                Toggle("Свой цвет", s.OwnColor, v => s.OwnColor = v, "Вместо общего цвета туши", true);
                if (s.OwnColor)
                    ColorField("Цвет", s.Color, v => s.Color = v);
            }
            if (line == WorldMapInkLine.ShoreEcho)
            {
                IntSlider("Линий вдоль берега", Ink.ShoreEchoCount, 0, 5, v => Ink.ShoreEchoCount = v);
                Slider("Шаг между линиями, клеток", Ink.ShoreEchoSpacingHexes, 0.03f, 1f, v => Ink.ShoreEchoSpacingHexes = v);
            }

            Section("Нажим");
            Slider("Разнотолщинность", s.Pressure, 0f, 1f, v => s.Pressure = v, "0 — ровная линия, 1 — от самого тонкого до самого толстого");
            Slider("Самый тонкий, доля", s.Thinnest, 0.05f, 1f, v => s.Thinnest = v);
            Slider("Самый толстый, доля", s.Thickest, 1f, 3f, v => s.Thickest = v);
            Slider("Волна нажима, клеток", s.PressureWaveHexes, 0.1f, 8f, v => s.PressureWaveHexes = v, "Как часто линия тоньшает и толстеет");
            Slider("Дрожь нажима", s.PressureJitter, 0f, 1f, v => s.PressureJitter = v, "Мелкие колебания толщины поверх длинной волны");

            Section("Перо");
            Slider("Широкое перо", s.Nib, 0f, 1f, v => s.Nib = v, "Поперёк пера линия толще, вдоль — тоньше (каллиграфия)");
            Slider("Угол пера, °", s.NibAngle, 0f, 180f, v => s.NibAngle = v);

            Section("Концы");
            PopupField<string> ends = new PopupField<string>("Форма концов", new List<string> { "Острые", "Тупые", "Капля" }, (int)s.Ends);
            ends.RegisterValueChangedCallback(_ => Change(() => s.Ends = (WorldMapStrokeEnd)ends.index));
            parent.Add(ends);
            Slider("Длина сужения, толщин", s.TaperLength, 0f, 8f, v => s.TaperLength = v);

            Section("Обрывы");
            Slider("Обрывы", s.Gaps, 0f, 0.8f, v => s.Gaps = v, "Доля линии, где кисть отрывается от бумаги");
            Slider("Размер обрывов, клеток", s.GapScaleHexes, 0.2f, 10f, v => s.GapScaleHexes = v);

            Section("Штрихи внахлёст");
            Slider("Длина штриха, клеток", s.StrokeLengthHexes, 0f, 6f, v => s.StrokeLengthHexes = v, "0 — сплошная линия");
            Slider("Разброс длины", s.StrokeLengthJitter, 0f, 1f, v => s.StrokeLengthJitter = v);
            Slider("Нахлёст", s.StrokeOverlap, 0f, 0.9f, v => s.StrokeOverlap = v);
            Slider("Сдвиг в сторону, толщин", s.StrokeOffset, 0f, 2f, v => s.StrokeOffset = v);

            Section("Рука");
            Slider("Дрожь руки, клеток", s.WobbleHexes, 0f, 0.4f, v => s.WobbleHexes = v,
                line == WorldMapInkLine.Shore || line == WorldMapInkLine.ForestEdge ? "Линия гуляет в стороны — вместе с краем заливки" : "Линия гуляет в стороны");
            Slider("Волна дрожи, клеток", s.WobbleWaveHexes, 0.1f, 8f, v => s.WobbleWaveHexes = v);

            Section("Сухая кисть");
            IntSlider("Волосков", s.Bristles, 1, 7, v => s.Bristles = v);
            Slider("Ширина веера, толщин", s.BristleSpread, 0f, 2f, v => s.BristleSpread = v);
            Slider("Сухость", s.Dryness, 0f, 1f, v => s.Dryness = v, "Волоски местами не оставляют следа");
        }

        private static string Description(WorldMapInkLine line)
        {
            switch (line)
            {
                case WorldMapInkLine.Shore: return "Обводка озёр и рек. Край воды лежит ровно на линии (если включено «Заливка по линии туши»).";
                case WorldMapInkLine.ShoreEcho: return "Тонкие линии внутри воды вдоль берега, как на старых картах. По умолчанию выключены — задайте число линий.";
                case WorldMapInkLine.ForestEdge: return "Обводка лесных массивов. Заливка и узор леса обрываются по ней.";
                case WorldMapInkLine.RoadEdge: return "Края дорог: мазки по кромке полосы, полоса ложится поверх их внутренней половины.";
                case WorldMapInkLine.Trail: return "Сами тропы — линия цвета тропы. Пунктир задаётся в «Генераторе карты» → «Дороги».";
                default: return "Перила мостов, нарисованных кодом (без своих рисунков моста).";
            }
        }

        private enum Preset { Even, Pen, Brush, Dry, Hatch, Shaky }

        // Готовые наборы меняют только форму мазка; включённость, прозрачность,
        // толщина и цвет линии остаются.
        private void ApplyPreset(Preset preset)
        {
            Change(() =>
            {
                WorldMapStrokeStyle s = Stroke;
                WorldMapStrokeStyle p = new WorldMapStrokeStyle();
                switch (preset)
                {
                    case Preset.Even:
                        p.Pressure = 0f; p.Nib = 0f; p.Gaps = 0f; p.WobbleHexes = 0f; p.Ends = WorldMapStrokeEnd.Blunt;
                        break;
                    case Preset.Pen:
                        p.Pressure = 0.35f; p.Thinnest = 0.5f; p.Thickest = 1.3f; p.PressureWaveHexes = 1.5f; p.PressureJitter = 0.15f;
                        p.Nib = 0.75f; p.Gaps = 0.06f; p.WobbleHexes = 0.015f; p.TaperLength = 4f;
                        break;
                    case Preset.Brush:
                        p.Pressure = 0.9f; p.Thinnest = 0.15f; p.Thickest = 2.2f; p.PressureWaveHexes = 1.1f; p.PressureJitter = 0.35f;
                        p.Nib = 0.35f; p.Gaps = 0.14f; p.WobbleHexes = 0.03f; p.Ends = WorldMapStrokeEnd.Blob; p.TaperLength = 2.5f;
                        break;
                    case Preset.Dry:
                        p.Pressure = 0.6f; p.Thickest = 1.9f; p.Nib = 0.2f; p.Gaps = 0.1f; p.WobbleHexes = 0.03f;
                        p.Bristles = 4; p.BristleSpread = 0.9f; p.Dryness = 0.45f;
                        break;
                    case Preset.Hatch:
                        p.Pressure = 0.5f; p.Nib = 0.4f; p.Gaps = 0f; p.WobbleHexes = 0.02f;
                        p.StrokeLengthHexes = 0.9f; p.StrokeLengthJitter = 0.45f; p.StrokeOverlap = 0.25f; p.StrokeOffset = 0.5f; p.TaperLength = 2f;
                        break;
                    case Preset.Shaky:
                        p.Pressure = 0.55f; p.PressureJitter = 0.7f; p.Nib = 0.3f; p.Gaps = 0.2f; p.GapScaleHexes = 1.2f;
                        p.WobbleHexes = 0.09f; p.WobbleWaveHexes = 0.6f;
                        break;
                }
                float opacity = s.Opacity, width = s.Width;
                bool ownColor = s.OwnColor;
                Color color = s.Color;
                s.CopyFrom(p);
                s.Opacity = opacity; s.Width = width; s.OwnColor = ownColor; s.Color = color;
            }, true);
        }

        private void Change(Action action, bool rebuild = false)
        {
            if (generator == null)
                return;
            Undo.RecordObject(generator, "Редактор туши");
            string edgeBefore = FillEdgeKey();
            action();
            EditorUtility.SetDirty(generator);
            if (rebuild)
                Rebuild();
            else
                preview?.MarkDirtyRepaint();
            Changed?.Invoke(FillEdgeKey() == edgeBefore);
        }

        // Всё, что двигает край заливок или знаки кодом: общая толщина и
        // цвет туши, «Заливка по линии туши», включённость и дрожь руки
        // берега и кромки леса. Остальные правки меняют только линии.
        private string FillEdgeKey()
        {
            string Edge(WorldMapStrokeStyle s) => s.Enabled + " " + (s.Opacity > 0f) + " " + s.WobbleHexes + " " + s.WobbleWaveHexes;
            return Ink.FillsFollowInk + "|" + A.InkWidth + "|" + A.InkColor + "|" + Edge(Ink.Shore) + "|" + Edge(Ink.ForestEdge);
        }

        // ------------------------------------------------------------------
        // Поля
        // ------------------------------------------------------------------

        private void Section(string title)
        {
            parent.Add(new Label(title.ToUpperInvariant())
            {
                style = { marginTop = 10, marginBottom = 2, unityFontStyleAndWeight = FontStyle.Bold, color = new Color(.88f, .78f, .53f) }
            });
        }

        private void Row(params (string text, Action action)[] buttons)
        {
            VisualElement row = new VisualElement { style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap, marginTop = 4 } };
            foreach ((string text, Action action) in buttons)
                row.Add(new Button(action) { text = text });
            parent.Add(row);
        }

        private void Toggle(string label, bool value, Action<bool> set, string tooltip = null, bool rebuild = false)
        {
            UnityEngine.UIElements.Toggle field = new UnityEngine.UIElements.Toggle(label) { value = value, tooltip = tooltip };
            field.RegisterValueChangedCallback(evt => Change(() => set(evt.newValue), rebuild || parent == lineList));
            parent.Add(field);
        }

        private void Slider(string label, float value, float min, float max, Action<float> set, string tooltip = null, float labelWidth = 0f)
        {
            UnityEngine.UIElements.Slider field = new UnityEngine.UIElements.Slider(label, min, max) { value = value, showInputField = true, tooltip = tooltip };
            if (labelWidth > 0f)
                field.labelElement.style.minWidth = field.labelElement.style.width = labelWidth;
            field.RegisterValueChangedCallback(evt => Change(() => set(evt.newValue)));
            parent.Add(field);
        }

        private void IntSlider(string label, int value, int min, int max, Action<int> set)
        {
            SliderInt field = new SliderInt(label, min, max) { value = value, showInputField = true };
            field.RegisterValueChangedCallback(evt => Change(() => set(evt.newValue)));
            parent.Add(field);
        }

        private void ColorField(string label, Color value, Action<Color> set, float labelWidth = 0f)
        {
            UnityEditor.UIElements.ColorField field = new UnityEditor.UIElements.ColorField(label) { value = value };
            if (labelWidth > 0f)
                field.labelElement.style.minWidth = field.labelElement.style.width = labelWidth;
            field.RegisterValueChangedCallback(evt => Change(() => set(evt.newValue)));
            parent.Add(field);
        }

        // ------------------------------------------------------------------
        // Предпросмотр: бумага, пятно-озеро с краем по линии, открытая волна
        // и дорога — той же кистью, что и карта.
        // ------------------------------------------------------------------

        private sealed class InkPreview : VisualElement
        {
            private readonly WorldMapInkEditorWindow window;

            public InkPreview(WorldMapInkEditorWindow window)
            {
                this.window = window;
                style.height = 260;
                style.marginLeft = style.marginRight = 6;
                style.marginTop = 6;
                style.overflow = Overflow.Hidden;
                generateVisualContent += Draw;
            }

            private void Draw(MeshGenerationContext context)
            {
                if (window.generator == null)
                    return;
                Rect rect = contentRect;
                WorldMapArtStyle art = window.A;
                WorldMapInkLine line = Selected;
                WorldMapStrokeStyle stroke = window.Stroke;
                float zoom = PreviewZoom;
                float hexPx = 40f * zoom;
                float inkWidth = art.InkWidth * zoom;
                Painter2D painter = context.painter2D;

                painter.fillColor = art.BaseColor;
                painter.BeginPath();
                painter.MoveTo(Vector2.zero);
                painter.LineTo(new Vector2(rect.width, 0f));
                painter.LineTo(new Vector2(rect.width, rect.height));
                painter.LineTo(new Vector2(0f, rect.height));
                painter.ClosePath();
                painter.Fill();

                WorldMapInkBrush brush = new WorldMapInkBrush(new WorldMapNoise(17), hexPx);
                List<WorldMapVectorShape> shapes = new List<WorldMapVectorShape>();
                Color ink = art.InkColor;

                // Пятно: озеро или лесной массив с краем по линии.
                Vector2 center = new Vector2(rect.width * 0.24f, rect.height * 0.5f);
                float radius = Mathf.Min(rect.width * 0.2f, rect.height * 0.38f);
                List<Vector2> blob = new List<Vector2>();
                for (int i = 0; i < 64; i++)
                {
                    float angle = i / 64f * Mathf.PI * 2f;
                    float r = radius * (1f + 0.18f * Mathf.Sin(angle * 3f + 0.7f) + 0.08f * Mathf.Sin(angle * 7f));
                    blob.Add(center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * r);
                }
                if (stroke.WobbleHexes > 0f && (line == WorldMapInkLine.Shore || line == WorldMapInkLine.ForestEdge))
                    blob = brush.Wobble(WorldMapInkBrush.Resample(blob, true, 2f), true, stroke.WobbleHexes * hexPx, stroke.WobbleWaveHexes * hexPx, 5);
                Color fill = line == WorldMapInkLine.ForestEdge ? art.ConiferColor : art.WaterShallowColor;
                WorldMapVectorShape area = new WorldMapVectorShape { Color = new Color(fill.r, fill.g, fill.b, 1f), OddEven = false };
                area.Contours.Add(blob.ToArray());
                shapes.Add(area);
                if (line == WorldMapInkLine.ShoreEcho)
                {
                    WorldMapStrokeStyle shore = window.Ink.Shore;
                    brush.Stroke(shapes, blob, true, shore, inkWidth, ink, 3, false);
                    int count = Mathf.Max(1, window.Ink.ShoreEchoCount);
                    for (int k = 1; k <= count; k++)
                    {
                        float inset = window.Ink.ShoreEchoSpacingHexes * hexPx * k;
                        List<Vector2> echo = new List<Vector2>();
                        foreach (Vector2 point in blob)
                        {
                            Vector2 toCenter = center - point;
                            echo.Add(point + toCenter.normalized * Mathf.Min(inset, toCenter.magnitude * 0.9f));
                        }
                        brush.Stroke(shapes, echo, true, stroke, inkWidth, ink, 10 + k);
                    }
                }
                else
                    brush.Stroke(shapes, blob, true, stroke, line == WorldMapInkLine.Trail ? art.TrailWidthHexes * hexPx : inkWidth,
                        line == WorldMapInkLine.Trail ? art.TrailColor : ink, 3, !(line == WorldMapInkLine.Shore || line == WorldMapInkLine.ForestEdge));

                // Волна — открытая линия; для дорог — полоса с обводкой.
                List<Vector2> wave = new List<Vector2>();
                for (int i = 0; i <= 80; i++)
                {
                    float t = i / 80f;
                    wave.Add(new Vector2(Mathf.Lerp(rect.width * 0.5f, rect.width * 0.96f, t), rect.height * (0.5f + 0.3f * Mathf.Sin(t * Mathf.PI * 2.2f))));
                }
                if (line == WorldMapInkLine.RoadEdge)
                {
                    float roadWidth = Mathf.Max(3f, art.RoadWidthHexes * hexPx);
                    List<Vector2> path = WorldMapInkBrush.Resample(wave, false, 2f);
                    if (stroke.WobbleHexes > 0f)
                        path = brush.Wobble(path, false, stroke.WobbleHexes * hexPx, stroke.WobbleWaveHexes * hexPx, 9);
                    List<Vector2> left = new List<Vector2>(), right = new List<Vector2>();
                    for (int i = 0; i < path.Count; i++)
                    {
                        Vector2 normal = WorldMapInkBrush.Normal(path, i, false);
                        left.Add(path[i] + normal * roadWidth * 0.5f);
                        right.Add(path[i] - normal * roadWidth * 0.5f);
                    }
                    brush.Stroke(shapes, left, false, stroke, inkWidth, ink, 21, false);
                    brush.Stroke(shapes, right, false, stroke, inkWidth, ink, 22, false);
                    Vector2[] ribbon = new Vector2[left.Count * 2];
                    for (int i = 0; i < left.Count; i++)
                    {
                        ribbon[i] = left[i];
                        ribbon[ribbon.Length - 1 - i] = right[i];
                    }
                    WorldMapVectorShape road = new WorldMapVectorShape { Color = new Color(art.RoadColor.r, art.RoadColor.g, art.RoadColor.b, 1f), OddEven = false };
                    road.Contours.Add(ribbon);
                    shapes.Add(road);
                }
                else
                    brush.Stroke(shapes, wave, false, stroke, line == WorldMapInkLine.Trail ? art.TrailWidthHexes * hexPx : inkWidth,
                        line == WorldMapInkLine.Trail ? art.TrailColor : ink, 7);

                foreach (WorldMapVectorShape shape in shapes)
                {
                    if (shape.Contours.Count == 0)
                        continue;
                    painter.fillColor = shape.Color;
                    painter.BeginPath();
                    foreach (Vector2[] contour in shape.Contours)
                    {
                        if (contour.Length < 3)
                            continue;
                        painter.MoveTo(contour[0]);
                        for (int i = 1; i < contour.Length; i++)
                            painter.LineTo(contour[i]);
                        painter.ClosePath();
                    }
                    painter.Fill(shape.OddEven ? FillRule.OddEven : FillRule.NonZero);
                }
            }
        }
    }
}

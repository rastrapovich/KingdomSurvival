using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace KingdomSurvival.WorldMapVisual.Editor
{
    // 12Ф-2: вода и уступы в Редакторе карты — реки и уступы линиями по
    // точкам, озеро «до уровня», брод, высушить.
    public sealed partial class AuthoredWorldMapWindow
    {
        private enum WaterMode
        {
            River,
            Lake,
            Ford,
            Dry
        }

        private static readonly string[] WaterModeTitles = { "Река", "Озеро", "Брод", "Высушить" };
        private const float PickPixels = 9f;

        private AuthoredWorldMapLayout layout;
        private bool reliefLedgeMode;
        private WaterMode waterMode;
        private float lakeLevel = 0.06f;
        private int selectedLine = -1;
        private bool drawingLine;
        private int draggedPoint = -1;
        private int selectedFord = -1;
        private bool draggingFord;

        private AuthoredWorldMapLayout Layout
        {
            get
            {
                if (layout == null || layout.Terrain == null || layout.Terrain.Length != Data.CellCount)
                    layout = AuthoredWorldMapLayout.Compute(Data);
                return layout;
            }
        }

        private bool IsRiverMode => tool == Tool.Water && waterMode == WaterMode.River;
        private bool IsLedgeMode => tool == Tool.Relief && reliefLedgeMode;
        private bool IsLineTool => IsRiverMode || IsLedgeMode;
        private int LineCount => IsRiverMode ? Data.Rivers.Count : IsLedgeMode ? Data.Ledges.Count : 0;
        private bool HasSelectedLine => selectedLine >= 0 && selectedLine < LineCount;

        private List<AuthoredPoint> LinePoints(int index) =>
            IsRiverMode ? Data.Rivers[index].Points : Data.Ledges[index].Points;

        private void SelectTool(Tool next)
        {
            if (tool == next)
                return;
            FinishLine();
            tool = next;
            selectedVillage = -1;
            selectedLine = -1;
            selectedFord = -1;
        }

        // ---------------- Линии ----------------

        private float CanvasPerScreenPixel => texture == null ? 1f : Data.Grid.CanvasWidth / (zoom * texture.width);

        private void LinePointerDown(double px, double py, Event e)
        {
            double pick = PickPixels * CanvasPerScreenPixel;

            // 1. Точка любой линии (сначала выбранной) — тянуть.
            int hitLine = -1, hitPoint = -1;
            double best = pick;
            for (int l = 0; l < LineCount; l++)
            {
                List<AuthoredPoint> points = LinePoints(l);
                for (int i = 0; i < points.Count; i++)
                {
                    double d = AuthoredWorldMapLines.Dist(px, py, points[i].X, points[i].Y) - (l == selectedLine ? pick * 0.3 : 0.0);
                    if (d < best)
                    {
                        best = d;
                        hitLine = l;
                        hitPoint = i;
                    }
                }
            }
            if (hitLine >= 0 && !(drawingLine && hitLine == selectedLine && hitPoint == LinePoints(hitLine).Count - 1))
            {
                selectedLine = hitLine;
                draggedPoint = hitPoint;
                drawingLine = false;
                Repaint();
                return;
            }

            // 2. Рисуется линия — следующая точка.
            if (drawingLine && HasSelectedLine)
            {
                Undo.RecordObject(asset, IsRiverMode ? "Редактор карты: точка реки" : "Редактор карты: точка уступа");
                if (IsRiverMode)
                    AuthoredWorldMapLines.SnapToRiver(Data, selectedLine, ref px, ref py);
                LinePoints(selectedLine).Add(new AuthoredPoint(px, py));
                draggedPoint = LinePoints(selectedLine).Count - 1;
                MarkChanged(true);
                return;
            }

            // 3. По линии: выбранной — вставить точку, другой — выбрать.
            for (int l = 0; l < LineCount; l++)
            {
                List<AuthoredPoint> points = LinePoints(l);
                double d = AuthoredWorldMapLines.Nearest(points, px, py, out int segment, out double nx, out double ny);
                if (d > pick || segment < 0)
                    continue;
                if (l == selectedLine && points.Count >= 2)
                {
                    Undo.RecordObject(asset, "Редактор карты: вставить точку");
                    points.Insert(segment + 1, new AuthoredPoint(nx, ny));
                    draggedPoint = segment + 1;
                    MarkChanged(true);
                }
                else
                {
                    selectedLine = l;
                    draggedPoint = -1;
                    Repaint();
                }
                return;
            }

            // 4. Пусто — новая линия.
            Undo.RecordObject(asset, IsRiverMode ? "Редактор карты: новая река" : "Редактор карты: новый уступ");
            if (IsRiverMode)
            {
                AuthoredWorldMapLines.SnapToRiver(Data, -1, ref px, ref py);
                Data.Rivers.Add(new AuthoredRiver { Points = new List<AuthoredPoint> { new AuthoredPoint(px, py) } });
            }
            else
                Data.Ledges.Add(new AuthoredLedge { Points = new List<AuthoredPoint> { new AuthoredPoint(px, py) } });
            selectedLine = LineCount - 1;
            drawingLine = true;
            draggedPoint = 0;
            MarkChanged(true);
        }

        private void LinePointerDrag(double px, double py, Event e)
        {
            if (!HasSelectedLine || draggedPoint < 0)
                return;
            List<AuthoredPoint> points = LinePoints(selectedLine);
            if (draggedPoint >= points.Count)
                return;
            bool end = draggedPoint == 0 || draggedPoint == points.Count - 1;
            if (IsRiverMode && end)
                AuthoredWorldMapLines.SnapToRiver(Data, selectedLine, ref px, ref py);
            Undo.RecordObject(asset, "Редактор карты: точка");
            points[draggedPoint].X = (float)px;
            points[draggedPoint].Y = (float)py;
            MarkChanged(true);
        }

        private void FinishLine()
        {
            bool wasDrawing = drawingLine;
            drawingLine = false;
            draggedPoint = -1;
            if (!HasSelectedLine)
                return;
            if (LinePoints(selectedLine).Count < 2)
            {
                Undo.RecordObject(asset, "Редактор карты: убрать незаконченную линию");
                if (IsRiverMode)
                    Data.Rivers.RemoveAt(selectedLine);
                else
                    Data.Ledges.RemoveAt(selectedLine);
                selectedLine = -1;
                MarkChanged(true);
            }
            else if (wasDrawing)
                Repaint();
        }

        private void RemoveLastPoint()
        {
            List<AuthoredPoint> points = LinePoints(selectedLine);
            if (points.Count == 0)
                return;
            Undo.RecordObject(asset, "Редактор карты: убрать точку");
            points.RemoveAt(points.Count - 1);
            if (points.Count == 0)
            {
                drawingLine = false;
                if (IsRiverMode)
                    Data.Rivers.RemoveAt(selectedLine);
                else
                    Data.Ledges.RemoveAt(selectedLine);
                selectedLine = -1;
            }
            MarkChanged(true);
        }

        private void DeleteSelectedLine()
        {
            if (!HasSelectedLine)
                return;
            Undo.RecordObject(asset, IsRiverMode ? "Редактор карты: удалить реку" : "Редактор карты: удалить уступ");
            if (IsRiverMode)
                Data.Rivers.RemoveAt(selectedLine);
            else
                Data.Ledges.RemoveAt(selectedLine);
            selectedLine = -1;
            drawingLine = false;
            MarkChanged(true);
        }

        // ---------------- Озеро, брод, высушить ----------------

        private void WaterPointerDown(double px, double py, int index, Event e)
        {
            switch (waterMode)
            {
                case WaterMode.Lake:
                {
                    Undo.RecordObject(asset, "Редактор карты: озеро");
                    int maxCells = Mathf.Max(200, Data.CellCount / 4);
                    int filled = AuthoredWorldMapLines.FillLake(Data, index, lakeLevel, maxCells);
                    if (filled < 0)
                        ShowNotification(new GUIContent("Вода разольётся по равнине.\nОпустите дно рельефом или уменьшите уровень."), 2.5);
                    else
                        MarkChanged(true);
                    dragging = false;
                    break;
                }
                case WaterMode.Dry:
                    Undo.RecordObject(asset, "Редактор карты: высушить");
                    if (AuthoredWorldMapLines.Dry(Data, index) > 0)
                        MarkChanged(true);
                    else
                        ShowNotification(new GUIContent("Здесь нет нарисованной воды.\nРеку удаляют в режиме «Река» (Del)."), 2.0);
                    dragging = false;
                    break;
                case WaterMode.Ford:
                {
                    double pick = PickPixels * CanvasPerScreenPixel;
                    for (int i = 0; i < Data.Fords.Count; i++)
                    {
                        if (AuthoredWorldMapLines.Dist(px, py, Data.Fords[i].X, Data.Fords[i].Y) <= pick)
                        {
                            selectedFord = i;
                            draggingFord = true;
                            Repaint();
                            return;
                        }
                    }
                    Undo.RecordObject(asset, "Редактор карты: брод");
                    Data.Fords.Add(new AuthoredPoint(px, py));
                    selectedFord = Data.Fords.Count - 1;
                    draggingFord = true;
                    MarkChanged(true);
                    break;
                }
                default:
                    dragging = false;
                    break;
            }
        }

        private void WaterPointerDrag(double px, double py)
        {
            if (waterMode != WaterMode.Ford || !draggingFord || selectedFord < 0 || selectedFord >= Data.Fords.Count)
                return;
            Undo.RecordObject(asset, "Редактор карты: брод");
            Data.Fords[selectedFord].X = (float)px;
            Data.Fords[selectedFord].Y = (float)py;
            MarkChanged(true);
        }

        private void DeleteSelectedFord()
        {
            if (selectedFord < 0 || selectedFord >= Data.Fords.Count)
                return;
            Undo.RecordObject(asset, "Редактор карты: удалить брод");
            Data.Fords.RemoveAt(selectedFord);
            selectedFord = -1;
            MarkChanged(true);
        }

        // ---------------- Инспекторы ----------------

        private void DrawWaterInspector()
        {
            WaterMode next = (WaterMode)GUILayout.Toolbar((int)waterMode, WaterModeTitles);
            if (next != waterMode)
            {
                FinishLine();
                waterMode = next;
                selectedLine = -1;
                selectedFord = -1;
            }
            switch (waterMode)
            {
                case WaterMode.River: DrawRiverInspector(); break;
                case WaterMode.Lake:
                    Header("Озеро до уровня");
                    lakeLevel = EditorGUILayout.Slider(new GUIContent("Уровень над дном", "Насколько вода поднимается над точкой клика"), lakeLevel, 0f, 0.3f);
                    EditorGUILayout.HelpBox("Клик в низину — вода заполняет её до уровня, берега остаются сухими. Сначала опустите дно рельефом. Вода в игре непроходима.", MessageType.None);
                    break;
                case WaterMode.Ford:
                    Header("Броды: " + Data.Fords.Count);
                    EditorGUILayout.HelpBox("Клик по реке — брод: через реку можно перейти пешком. Тянуть — двигать, Del — удалить. Мосты встанут сами, когда дороги пересекут реку (12Ф-3).", MessageType.None);
                    if (selectedFord >= 0 && selectedFord < Data.Fords.Count && GUILayout.Button("Удалить брод (Del)"))
                        DeleteSelectedFord();
                    break;
                case WaterMode.Dry:
                    EditorGUILayout.HelpBox("Клик по нарисованной воде (озеру) — она снова суша.", MessageType.None);
                    break;
            }
        }

        private void DrawRiverInspector()
        {
            Header("Реки: " + Data.Rivers.Count);
            for (int i = 0; i < Data.Rivers.Count; i++)
            {
                AuthoredRiver r = Data.Rivers[i];
                string title = string.IsNullOrWhiteSpace(r.Name) ? "Река " + (i + 1) : r.Name;
                if (GUILayout.Toggle(selectedLine == i, title, "Button") && selectedLine != i)
                {
                    FinishLine();
                    selectedLine = i;
                    if (r.Points.Count > 0)
                        CenterOnCanvas(r.Points[r.Points.Count / 2].X, r.Points[r.Points.Count / 2].Y);
                }
            }
            if (!HasSelectedLine)
            {
                EditorGUILayout.HelpBox("Клик по пустому месту начинает реку у истока; следующие клики — к устью. Enter или ПКМ — закончить. Конец у другой реки прилипает к ней — приток.", MessageType.None);
                return;
            }
            AuthoredRiver river = Data.Rivers[selectedLine];
            Header(drawingLine ? "Река рисуется…" : "Выбранная река");
            EditorGUI.BeginChangeCheck();
            string name = EditorGUILayout.TextField(new GUIContent("Название", "Для себя; в игре название реки пока не показывается"), river.Name);
            float start = EditorGUILayout.Slider(new GUIContent("Ширина у истока, клеток"), river.StartWidth, 0.3f, 4f);
            float end = EditorGUILayout.Slider(new GUIContent("Ширина у устья, клеток"), river.EndWidth, 0.3f, 6f);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(asset, "Редактор карты: река");
                river.Name = name;
                river.StartWidth = start;
                river.EndWidth = end;
                MarkChanged(true);
            }
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(new GUIContent("Исток ⇄ устье", "Развернуть направление течения")))
            {
                Undo.RecordObject(asset, "Редактор карты: развернуть реку");
                river.Points.Reverse();
                (river.StartWidth, river.EndWidth) = (river.EndWidth, river.StartWidth);
                MarkChanged(true);
            }
            if (GUILayout.Button("Удалить (Del)"))
                DeleteSelectedLine();
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.HelpBox("Русло прорезается само и не поднимается вниз по течению. Если земля под рекой идёт в гору — предупреждение в проверке.", MessageType.None);
        }

        private void DrawLedgeInspector()
        {
            Header("Уступы: " + Data.Ledges.Count);
            for (int i = 0; i < Data.Ledges.Count; i++)
            {
                if (GUILayout.Toggle(selectedLine == i, "Уступ " + (i + 1), "Button") && selectedLine != i)
                {
                    FinishLine();
                    selectedLine = i;
                    List<AuthoredPoint> pts = Data.Ledges[i].Points;
                    if (pts.Count > 0)
                        CenterOnCanvas(pts[pts.Count / 2].X, pts[pts.Count / 2].Y);
                }
            }
            if (!HasSelectedLine)
            {
                EditorGUILayout.HelpBox("Обрыв линией: клики по точкам, Enter или ПКМ — закончить. Верх обрыва отмечен засечками. Скалы непроходимы; концы сходят в склон — там можно подняться.", MessageType.None);
                return;
            }
            AuthoredLedge ledge = Data.Ledges[selectedLine];
            Header(drawingLine ? "Уступ рисуется…" : "Выбранный уступ");
            EditorGUI.BeginChangeCheck();
            float height = EditorGUILayout.Slider(new GUIContent("Высота обрыва"), ledge.Height, 0.3f, 2.5f);
            bool pass = EditorGUILayout.Toggle(new GUIContent("Проход на концах", "Концы обрыва сходят в травяной склон"), ledge.PassAtEnds);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(asset, "Редактор карты: уступ");
                ledge.Height = height;
                ledge.PassAtEnds = pass;
                MarkChanged(true);
            }
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(new GUIContent("Верх ⇄", "Перевернуть: какая сторона обрыва выше")))
            {
                Undo.RecordObject(asset, "Редактор карты: сторона уступа");
                ledge.UpperLeft = !ledge.UpperLeft;
                MarkChanged(true);
            }
            if (GUILayout.Button("Удалить (Del)"))
                DeleteSelectedLine();
            EditorGUILayout.EndHorizontal();
        }

        private void CenterOnCanvas(float x, float y)
        {
            if (texture == null)
                return;
            float k = zoom * texture.width / Data.Grid.CanvasWidth;
            pan = new Vector2(canvasRect.width * 0.5f - x * k, canvasRect.height * 0.5f - y * k);
            Repaint();
        }

        // ---------------- Рисунок линий ----------------

        private static Vector2 CanvasToLocal(Rect local, WorldMapHexGrid g, float x, float y) =>
            new Vector2(local.x + x / g.CanvasWidth * local.width, local.y + y / g.CanvasHeight * local.height);

        private static void DrawSegment(Vector2 a, Vector2 b, float width, Color color)
        {
            if (Event.current.type != EventType.Repaint)
                return;
            Vector2 d = b - a;
            float length = d.magnitude;
            if (length < 0.5f)
                return;
            Matrix4x4 matrix = GUI.matrix;
            Color previous = GUI.color;
            GUI.color = color;
            GUIUtility.RotateAroundPivot(Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg, a);
            GUI.DrawTexture(new Rect(a.x, a.y - width * 0.5f, length, width), EditorGUIUtility.whiteTexture);
            GUI.matrix = matrix;
            GUI.color = previous;
        }

        private static void DrawPolyline(Rect local, WorldMapHexGrid g, List<AuthoredPoint> points, float width, Color color)
        {
            for (int i = 0; i + 1 < points.Count; i++)
                DrawSegment(CanvasToLocal(local, g, points[i].X, points[i].Y), CanvasToLocal(local, g, points[i + 1].X, points[i + 1].Y), width, color);
        }

        private void DrawLines(Rect local)
        {
            WorldMapHexGrid g = Data.Grid;
            float hexPx = local.width / Mathf.Max(1, Data.Columns);
            AuthoredWorldMapLayout l = Layout;

            for (int i = 0; i < l.Rivers.Count; i++)
                DrawPolyline(local, g, l.Rivers[i].Smoothed, Mathf.Max(1.5f, hexPx * 0.12f), new Color(0.12f, 0.3f, 0.55f, 0.9f));

            foreach (AuthoredWorldMapLayout.LedgeInfo info in l.Ledges)
            {
                List<AuthoredPoint> pts = info.Smoothed;
                DrawPolyline(local, g, pts, Mathf.Max(2f, hexPx * 0.18f), new Color(0.3f, 0.2f, 0.12f, 0.95f));
                // Засечки — на стороне плато, через каждые ~0,7 клетки.
                float side = info.Source.UpperLeft ? 1f : -1f;
                foreach ((double x, double y, double _, double dx, double dy) in AuthoredWorldMapLines.Sample(pts, g.HexWidth * 0.7))
                {
                    // «Слева по ходу» на экране (y вниз): нормаль (dy, -dx).
                    Vector2 a = CanvasToLocal(local, g, (float)x, (float)y);
                    Vector2 n = new Vector2((float)dy, (float)-dx) * side;
                    DrawSegment(a, a + n * Mathf.Max(4f, hexPx * 0.45f), Mathf.Max(1f, hexPx * 0.08f), new Color(0.3f, 0.2f, 0.12f, 0.8f));
                }
            }

            for (int f = 0; f < l.Fords.Count; f++)
            {
                AuthoredWorldMapLayout.FordInfo ford = l.Fords[f];
                Vector2 p = CanvasToLocal(local, g, ford.Source.X, ford.Source.Y);
                float r = Mathf.Max(5f, hexPx * 0.35f);
                Disc(p, r, ford.Ok ? new Color(0.55f, 0.42f, 0.25f, 0.95f) : new Color(0.85f, 0.2f, 0.2f, 0.95f));
                if (tool == Tool.Water && waterMode == WaterMode.Ford && selectedFord == f)
                    Ring(p, r * 1.5f, new Color(1f, 0.85f, 0.3f));
            }

            // Опорные точки — в режиме своей линии.
            if (!IsLineTool)
                return;
            for (int li = 0; li < LineCount; li++)
            {
                List<AuthoredPoint> points = LinePoints(li);
                bool selected = li == selectedLine;
                if (selected && points.Count >= 1)
                    DrawPolyline(local, g, points, 1f, new Color(1f, 1f, 1f, 0.5f));
                for (int i = 0; i < points.Count; i++)
                {
                    Vector2 p = CanvasToLocal(local, g, points[i].X, points[i].Y);
                    float r = selected ? 5f : 3.5f;
                    Disc(p, r + 1.5f, new Color(0f, 0f, 0f, 0.8f));
                    Color c = selected ? (i == 0 ? new Color(0.4f, 1f, 0.5f) : new Color(1f, 0.85f, 0.3f)) : new Color(0.9f, 0.9f, 0.9f);
                    Disc(p, r, c);
                }
            }
        }
    }
}

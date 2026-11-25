using UnityEditor;
using UnityEngine;

namespace KingdomSurvival.WorldMapVisual.Editor
{
    // 12Ф-3: дороги в Редакторе карты — опорные точки, автопрокладка,
    // прилипание, мосты, «Соединить деревни».
    public sealed partial class AuthoredWorldMapWindow
    {
        private bool newRoadTrail;
        private float newRoadStraightness = 0.3f;
        private bool newRoadAvoidWater;
        private bool roadSnap = true;
        private float roadSnapHexes = AuthoredWorldMapRoads.DefaultSnapHexes;
        private float connectLoops = 0.2f;
        private int connectExits = 2;

        private void DrawRoadsInspector()
        {
            Header("Новая дорога");
            newRoadTrail = GUILayout.Toolbar(newRoadTrail ? 1 : 0, new[] { "Дорога", "Тропа" }) == 1;
            newRoadStraightness = StraightnessSlider(newRoadStraightness);
            newRoadAvoidWater = EditorGUILayout.Toggle(new GUIContent("Не пересекать воду", "Без мостов: обход или брод"), newRoadAvoidWater);
            roadSnap = EditorGUILayout.Toggle(new GUIContent("Прилипать", "Концы прилипают к деревням, лагерю и другим дорогам"), roadSnap);
            if (roadSnap)
                roadSnapHexes = EditorGUILayout.Slider("Радиус прилипания, клеток", roadSnapHexes, 0.5f, 5f);

            Header("Соединить деревни");
            connectLoops = EditorGUILayout.Slider(new GUIContent("Петли", "0 — только самое нужное; больше — объездные пути"), connectLoops, 0f, 1f);
            connectExits = EditorGUILayout.IntSlider(new GUIContent("Выходы к краю", "Дороги от ближайших поселений к краям карты: слева, справа, сверху, снизу"), connectExits, 0, 4);
            if (GUILayout.Button(new GUIContent("Соединить деревни", "Проложить недостающие дороги между лагерем и деревнями. Готовые дороги не трогаются."), GUILayout.Height(26f)))
            {
                FinishLine();
                Undo.RecordObject(asset, "Редактор карты: соединить деревни");
                int added = AuthoredWorldMapRoads.ConnectVillages(Data, connectLoops, connectExits, newRoadTrail, newRoadStraightness);
                MarkChanged(true);
                ShowNotification(new GUIContent(added == 0 ? "Всё уже соединено" : "Добавлено дорог: " + added), 1.5);
            }

            Header("Дороги: " + Data.Roads.Count);
            for (int i = 0; i < Data.Roads.Count; i++)
            {
                AuthoredRoad r = Data.Roads[i];
                bool broken = i < Layout.Roads.Count && Layout.Roads[i].Broken;
                string title = (r.Trail ? "Тропа " : "Дорога ") + (i + 1) + (broken ? "  ✖" : "");
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
                EditorGUILayout.HelpBox("Клики — опорные точки, путь прокладывается сам. Конец у деревни, лагеря или дороги прилипает к ним (перекрёсток). Через реку — мост сам.", MessageType.None);
                return;
            }
            AuthoredRoad road = Data.Roads[selectedLine];
            Header(drawingLine ? "Дорога рисуется…" : "Выбранная дорога");
            EditorGUI.BeginChangeCheck();
            bool trail = GUILayout.Toolbar(road.Trail ? 1 : 0, new[] { "Дорога", "Тропа" }) == 1;
            float straight = StraightnessSlider(road.Straightness);
            bool avoid = EditorGUILayout.Toggle(new GUIContent("Не пересекать воду"), road.AvoidWater);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(asset, "Редактор карты: дорога");
                road.Trail = trail;
                road.Straightness = straight;
                road.AvoidWater = avoid;
                MarkChanged(true);
            }
            if (selectedLine < Layout.Roads.Count)
            {
                AuthoredWorldMapLayout.RoadInfo info = Layout.Roads[selectedLine];
                EditorGUILayout.LabelField($"Клеток пути: {info.Cells.Count}, мостов: {info.Crossings.Count}", EditorStyles.miniLabel);
            }
            if (GUILayout.Button("Удалить (Del)"))
                DeleteSelectedLine();
        }

        private static float StraightnessSlider(float value)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(new GUIContent("По рельефу", "Обходит холмы, лес и болото"), GUILayout.Width(70f));
            value = GUILayout.HorizontalSlider(value, 0f, 1f);
            EditorGUILayout.LabelField(new GUIContent("Прямо", "Почти по линейке"), GUILayout.Width(40f));
            EditorGUILayout.EndHorizontal();
            return value;
        }

        // Дороги, мосты и опорные точки — поверх схемы.
        private void DrawRoads(Rect local)
        {
            WorldMapHexGrid g = Data.Grid;
            float hexPx = local.width / Mathf.Max(1, Data.Columns);
            AuthoredWorldMapLayout l = Layout;
            for (int i = 0; i < l.Roads.Count; i++)
            {
                AuthoredWorldMapLayout.RoadInfo info = l.Roads[i];
                bool trail = info.Source.Trail;
                Color color = info.Broken ? new Color(0.85f, 0.2f, 0.2f, 0.95f)
                    : trail ? new Color(0.55f, 0.42f, 0.28f, 0.9f) : new Color(0.42f, 0.28f, 0.15f, 0.95f);
                float width = Mathf.Max(trail ? 1.2f : 2f, hexPx * (trail ? 0.1f : 0.2f));
                DrawPolyline(local, g, info.Smoothed, width, color);
                foreach (AuthoredWorldMapLayout.CrossingInfo crossing in info.Crossings)
                {
                    g.CellCenter(g.CellAt(crossing.Entry), out double ax, out double ay);
                    g.CellCenter(g.CellAt(crossing.Exit), out double bx, out double by);
                    DrawSegment(CanvasToLocal(local, g, (float)ax, (float)ay), CanvasToLocal(local, g, (float)bx, (float)by),
                        Mathf.Max(3f, hexPx * 0.4f), new Color(0.25f, 0.17f, 0.1f, 0.95f));
                }
            }
        }
    }
}

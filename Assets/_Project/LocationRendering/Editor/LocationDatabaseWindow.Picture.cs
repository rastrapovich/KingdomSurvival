using System.Linq;
using KingdomSurvival.BattlefieldDatabase;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace KingdomSurvival.LocationRendering.Editor
{
    // Рисунок места: подогнать размер места под рисунок и кадрировать
    // рисунок рамкой. Рамку протягивают мышью, потом тянут за края и углы
    // или двигают целиком; точно — числами.
    public sealed partial class LocationDatabaseWindow
    {
        private enum PictureDrag { None, New, Move, Resize }

        private const int EdgeLeft = 1, EdgeRight = 2, EdgeTop = 4, EdgeBottom = 8;
        private const float EdgeGrip = 7;

        private bool pictureCropMode;
        private RectInt? pictureCrop;
        private PictureDrag pictureDrag;
        private int pictureEdges;
        private Vector2 pictureDragStart;
        private RectInt pictureDragRect;

        private bool HasPlainPicture => Visual?.Background != null && (Ground == null || !Ground.IsTiled);
        private bool PictureCropActive => pictureCropMode && tab == Tab.Place && HasPlainPicture && Location != null;

        private void ResetPictureCrop()
        {
            pictureCropMode = false;
            pictureCrop = null;
            pictureDrag = PictureDrag.None;
        }

        private void BuildPictureSettings(LocalLocationDefinition location)
        {
            if (!HasPlainPicture) return;
            Vector2 picture = LocationPictureCrop.SourceSize(Visual.Background);
            Vector2 canvasSize = CanvasSize;
            bool same = Mathf.Abs(picture.x - canvasSize.x) <= .5f && Mathf.Abs(picture.y - canvasSize.y) <= .5f;
            Label info = new Label("Рисунок " + picture.x + "×" + picture.y + " px · место " + Mathf.RoundToInt(canvasSize.x) + "×" +
                                   Mathf.RoundToInt(canvasSize.y) + " px" + (same ? " — совпадают." : " — рисунок растянут на место."));
            info.style.whiteSpace = WhiteSpace.Normal;
            info.style.marginTop = 4;
            settings.Add(info);
            Button fit = new Button(() =>
            {
                Change(() => ResizeCanvas(location, picture.x, picture.y, location.HexesAcross, true), true);
                status.text = "Размер места подогнан под рисунок: " + picture.x + "×" + picture.y + " px. Входы, объекты и противники сохранили свою долю рисунка.";
            }) { text = "Подогнать размер места под рисунок", tooltip = "Место станет ровно по рисунку, 1 пиксель рисунка = 1 пиксель места." };
            fit.SetEnabled(!same);
            settings.Add(fit);

            Heading("Кадрирование рисунка");
            Help("Рамка — какая часть рисунка останется местом. «Рамка мышью»: протяните рамку ЛКМ по рисунку, потом тяните края и углы " +
                 "или двигайте её целиком; ПКМ — панорама, колесо — масштаб, Esc — выйти. Точно — числами ниже. «Кадрировать» сохраняет " +
                 "обрезанную копию рисунка (и нормалей) рядом с исходным файлом, исходный не меняется. Входы, объекты, противники, зоны, " +
                 "предметы, раскидка и разметка остаются на своих местах рисунка. Ctrl+Z возвращает место к прежнему рисунку.");
            VisualElement row = new VisualElement(); row.style.flexDirection = FlexDirection.Row; row.style.flexWrap = Wrap.Wrap;
            AddButton(row, pictureCropMode ? "● Рамка мышью (ЛКМ)" : "Рамка мышью", () =>
            {
                pictureCropMode = !pictureCropMode;
                if (!pictureCropMode) pictureCrop = null;
                status.text = pictureCropMode ? "Протяните рамку ЛКМ по рисунку; края, углы и середина рамки — тянутся." : status.text;
                BuildSettings();
            });
            AddButton(row, "Весь рисунок", () =>
            {
                pictureCropMode = true;
                pictureCrop = new RectInt(0, 0, Mathf.RoundToInt(canvasSize.x), Mathf.RoundToInt(canvasSize.y));
                BuildSettings();
            });
            settings.Add(row);
            if (!pictureCropMode || !pictureCrop.HasValue) return;

            Label summary = new Label();
            summary.style.whiteSpace = WhiteSpace.Normal;
            HelpBox problem = new HelpBox(string.Empty, HelpBoxMessageType.Warning);
            Button apply = new Button(ApplyPictureCrop) { text = "Кадрировать" };
            void Describe()
            {
                RectInt rect = pictureCrop ?? default;
                float k = picture.x / Mathf.Max(1, canvasSize.x);
                summary.text = "Место " + Mathf.RoundToInt(canvasSize.x) + "×" + Mathf.RoundToInt(canvasSize.y) + " → " + rect.width + "×" + rect.height +
                               " px" + (Mathf.Abs(k - 1) > .001f ? " (в файле рисунка ≈ " + Mathf.RoundToInt(rect.width * k) + "×" + Mathf.RoundToInt(rect.height * k) + ")" : "") + ".";
                string text = LocationPictureCrop.Problem(location, Visual, rect);
                problem.text = text ?? string.Empty;
                problem.style.display = text != null ? DisplayStyle.Flex : DisplayStyle.None;
                apply.SetEnabled(text == null);
            }
            RectIntField field = new RectIntField("Рамка (px места, Y вниз)") { value = pictureCrop.Value };
            field.RegisterValueChangedCallback(evt =>
            {
                pictureCrop = LocationPictureCrop.Clamp(evt.newValue, CanvasSize);
                Describe();
            });
            settings.Add(field);
            settings.Add(summary);
            settings.Add(problem);
            VisualElement buttons = new VisualElement(); buttons.style.flexDirection = FlexDirection.Row;
            buttons.Add(apply);
            AddButton(buttons, "Отмена", () => { ResetPictureCrop(); BuildSettings(); });
            settings.Add(buttons);
            Describe();
        }

        private void ApplyPictureCrop()
        {
            LocalLocationDefinition location = Location;
            LocationVisualDefinition visual = Visual;
            if (!pictureCrop.HasValue || location == null || visual == null) return;
            RectInt rect = pictureCrop.Value;
            AssetDatabase.SaveAssetIfDirty(database);
            if (!LocationPictureCrop.CreateAssets(location, visual, rect, out Sprite sprite, out Texture2D normal, out string message))
            {
                status.text = message;
                EditorUtility.DisplayDialog("Кадрирование рисунка", message, "Понятно");
                return;
            }
            LocationRebase.Result moved = null;
            Change(() =>
            {
                moved = LocationRebase.Translate(location, visual, new Vector2(rect.x, rect.y), new Vector2(rect.width, rect.height));
                visual.Background = sprite;
                visual.BackgroundNormalMap = normal;
            });
            ResetPictureCrop();
            zoom = 1;
            viewCenter = CanvasSize / 2;
            string outside = moved.Outside > 0
                ? " За рамкой оказались: " + string.Join(", ", moved.Names.Take(5)) + (moved.Names.Count > 5 ? " и ещё " + (moved.Names.Count - 5) : "") + " — передвиньте их."
                : string.Empty;
            string scatter = moved.ScatterRemoved > 0 ? " Раскидки за рамкой убрано: " + moved.ScatterRemoved + "." : string.Empty;
            status.text = message + " Место " + rect.width + "×" + rect.height + " px." + outside + scatter;
            BuildSettings();
            RebuildPreview();
        }

        // Рамка мышью: вне рамки — новая, у края или угла — тянуть край,
        // внутри — двигать.
        private bool HandlePictureCropInput(Event evt, Rect frame, Vector2 pixel)
        {
            if (!PictureCropActive) return false;
            if (evt.type == EventType.KeyDown && evt.keyCode == KeyCode.Escape)
            {
                ResetPictureCrop();
                BuildSettings();
                evt.Use();
                return true;
            }
            if (evt.type == EventType.MouseDown && evt.button == 0 && frame.Contains(evt.mousePosition))
            {
                pictureDragStart = pixel;
                pictureEdges = pictureCrop.HasValue ? PictureEdgesAt(frame, evt.mousePosition) : 0;
                if (pictureEdges != 0) pictureDrag = PictureDrag.Resize;
                else if (pictureCrop.HasValue && pictureCrop.Value.Contains(Vector2Int.FloorToInt(pixel))) pictureDrag = PictureDrag.Move;
                else pictureDrag = PictureDrag.New;
                pictureDragRect = pictureCrop ?? new RectInt(Mathf.RoundToInt(pixel.x), Mathf.RoundToInt(pixel.y), 0, 0);
                dragging = true;
                evt.Use();
                return true;
            }
            if (evt.type == EventType.MouseDrag && evt.button == 0 && pictureDrag != PictureDrag.None)
            {
                Vector2 canvasSize = CanvasSize;
                int x = Mathf.RoundToInt(pixel.x), y = Mathf.RoundToInt(pixel.y);
                RectInt start = pictureDragRect;
                switch (pictureDrag)
                {
                    case PictureDrag.New:
                        int sx = Mathf.RoundToInt(pictureDragStart.x), sy = Mathf.RoundToInt(pictureDragStart.y);
                        pictureCrop = LocationPictureCrop.Clamp(new RectInt(Mathf.Min(sx, x), Mathf.Min(sy, y), Mathf.Abs(x - sx), Mathf.Abs(y - sy)), canvasSize);
                        break;
                    case PictureDrag.Move:
                        Vector2Int delta = Vector2Int.RoundToInt(pixel - pictureDragStart);
                        int left = Mathf.Clamp(start.x + delta.x, 0, Mathf.Max(0, Mathf.RoundToInt(canvasSize.x) - start.width));
                        int top = Mathf.Clamp(start.y + delta.y, 0, Mathf.Max(0, Mathf.RoundToInt(canvasSize.y) - start.height));
                        pictureCrop = new RectInt(left, top, start.width, start.height);
                        break;
                    case PictureDrag.Resize:
                        int x0 = (pictureEdges & EdgeLeft) != 0 ? x : start.xMin, x1 = (pictureEdges & EdgeRight) != 0 ? x : start.xMax;
                        int y0 = (pictureEdges & EdgeTop) != 0 ? y : start.yMin, y1 = (pictureEdges & EdgeBottom) != 0 ? y : start.yMax;
                        pictureCrop = LocationPictureCrop.Clamp(new RectInt(x0, y0, x1 - x0, y1 - y0), canvasSize);
                        break;
                }
                evt.Use();
                return true;
            }
            if (evt.type == EventType.MouseUp && evt.button == 0 && pictureDrag != PictureDrag.None)
            {
                pictureDrag = PictureDrag.None;
                dragging = false;
                if (pictureCrop.HasValue && (pictureCrop.Value.width < 2 || pictureCrop.Value.height < 2)) pictureCrop = null;
                BuildSettings();
                evt.Use();
                return true;
            }
            return false;
        }

        // Края рамки под указателем (координаты холста).
        private int PictureEdgesAt(Rect frame, Vector2 mouse)
        {
            RectInt rect = pictureCrop.Value;
            Vector2 a = ToGui(frame, new Vector2(rect.xMin, rect.yMin)), b = ToGui(frame, new Vector2(rect.xMax, rect.yMax));
            int edges = 0;
            bool inY = mouse.y >= a.y - EdgeGrip && mouse.y <= b.y + EdgeGrip;
            bool inX = mouse.x >= a.x - EdgeGrip && mouse.x <= b.x + EdgeGrip;
            if (inY && Mathf.Abs(mouse.x - a.x) <= EdgeGrip) edges |= EdgeLeft;
            else if (inY && Mathf.Abs(mouse.x - b.x) <= EdgeGrip) edges |= EdgeRight;
            if (inX && Mathf.Abs(mouse.y - a.y) <= EdgeGrip) edges |= EdgeTop;
            else if (inX && Mathf.Abs(mouse.y - b.y) <= EdgeGrip) edges |= EdgeBottom;
            return edges;
        }

        // Затемнение за рамкой, рамка, ручки и курсоры (внутри клипа холста).
        private void DrawPictureCrop(Rect frame, Vector2 shift)
        {
            if (!PictureCropActive || !pictureCrop.HasValue) return;
            RectInt rect = pictureCrop.Value;
            Vector2 Gui(Vector2 p) => ToGui(frame, p) + shift;
            Vector2 a = Gui(new Vector2(rect.xMin, rect.yMin)), b = Gui(new Vector2(rect.xMax, rect.yMax));
            Vector2 c0 = Gui(Vector2.zero), c1 = Gui(CanvasSize);
            Color dim = new Color(0, 0, 0, .6f);
            Color line = new Color(.35f, .85f, 1f, 1f);
            Handles.BeginGUI();
            Handles.DrawSolidRectangleWithOutline(Rect.MinMaxRect(c0.x, c0.y, c1.x, a.y), dim, Color.clear);
            Handles.DrawSolidRectangleWithOutline(Rect.MinMaxRect(c0.x, b.y, c1.x, c1.y), dim, Color.clear);
            Handles.DrawSolidRectangleWithOutline(Rect.MinMaxRect(c0.x, a.y, a.x, b.y), dim, Color.clear);
            Handles.DrawSolidRectangleWithOutline(Rect.MinMaxRect(b.x, a.y, c1.x, b.y), dim, Color.clear);
            Handles.DrawSolidRectangleWithOutline(Rect.MinMaxRect(a.x, a.y, b.x, b.y), Color.clear, line);
            // Трети — для композиции кадра.
            Handles.color = new Color(line.r, line.g, line.b, .3f);
            for (int i = 1; i < 3; i++)
            {
                float x = Mathf.Lerp(a.x, b.x, i / 3f), y = Mathf.Lerp(a.y, b.y, i / 3f);
                Handles.DrawLine(new Vector3(x, a.y), new Vector3(x, b.y));
                Handles.DrawLine(new Vector3(a.x, y), new Vector3(b.x, y));
            }
            Vector2 middle = (a + b) / 2;
            foreach (Vector2 grip in new[]
                     {
                         a, b, new Vector2(a.x, b.y), new Vector2(b.x, a.y),
                         new Vector2(middle.x, a.y), new Vector2(middle.x, b.y), new Vector2(a.x, middle.y), new Vector2(b.x, middle.y)
                     })
                Handles.DrawSolidRectangleWithOutline(new Rect(grip.x - 4, grip.y - 4, 8, 8), Color.white, line);
            Handles.EndGUI();
            GUI.Label(new Rect(a.x + 4, a.y + 2, 260, 16), "кадр " + rect.width + "×" + rect.height, EditorStyles.whiteMiniLabel);

            float g = EdgeGrip;
            EditorGUIUtility.AddCursorRect(Rect.MinMaxRect(a.x + g, a.y + g, b.x - g, b.y - g), MouseCursor.MoveArrow);
            EditorGUIUtility.AddCursorRect(Rect.MinMaxRect(a.x - g, a.y + g, a.x + g, b.y - g), MouseCursor.ResizeHorizontal);
            EditorGUIUtility.AddCursorRect(Rect.MinMaxRect(b.x - g, a.y + g, b.x + g, b.y - g), MouseCursor.ResizeHorizontal);
            EditorGUIUtility.AddCursorRect(Rect.MinMaxRect(a.x + g, a.y - g, b.x - g, a.y + g), MouseCursor.ResizeVertical);
            EditorGUIUtility.AddCursorRect(Rect.MinMaxRect(a.x + g, b.y - g, b.x - g, b.y + g), MouseCursor.ResizeVertical);
            EditorGUIUtility.AddCursorRect(new Rect(a.x - g, a.y - g, 2 * g, 2 * g), MouseCursor.ResizeUpLeft);
            EditorGUIUtility.AddCursorRect(new Rect(b.x - g, b.y - g, 2 * g, 2 * g), MouseCursor.ResizeUpLeft);
            EditorGUIUtility.AddCursorRect(new Rect(b.x - g, a.y - g, 2 * g, 2 * g), MouseCursor.ResizeUpRight);
            EditorGUIUtility.AddCursorRect(new Rect(a.x - g, b.y - g, 2 * g, 2 * g), MouseCursor.ResizeUpRight);
        }
    }
}

using System.Collections.Generic;
using System.Linq;
using KingdomSurvival.AnimationDatabase;
using KingdomSurvival.BattlefieldDatabase;
using KingdomSurvival.LocationRendering;
using KingdomSurvival.UnitDatabase;
using UnityEditor;
using UnityEngine;

namespace KingdomSurvival.ArtAssets.Editor
{
    // Холст: объекты рядом на линиях земли, в игровом размере (с человеком
    // для сравнения) или вписанные в ячейки. Раскладка — только просмотр.
    // Галерея: крупные карточки с полнотой ракурсов и нормалей.
    public sealed partial class ArtAssetDatabaseWindow
    {
        private sealed class CanvasItem
        {
            public ArtAssetDefinition Asset;
            // Опора на холсте (единицы холста, Y вниз).
            public Vector2 Anchor;
            public float Scale;
            // Рисунок с подписью (единицы холста, Y вниз).
            public Rect Rect;
            public Rect ArtRect;
        }

        private sealed class CanvasRow
        {
            public float Baseline, Left, Right;
            public string Title;
        }

        private const float CanvasGap = .7f, FitCell = 2.6f, LabelPixels = 38;
        private readonly List<CanvasItem> canvasItems = new List<CanvasItem>();
        private readonly List<CanvasRow> canvasRows = new List<CanvasRow>();
        private Rect canvasContent;
        private bool layoutDirty = true;
        private bool pendingFrameAll;
        private (ArtAssetView view, ArtAssetCanvasScale scale, int zoom) layoutKey;
        private Vector2 lastMouse, pressPosition;
        private string pressedId;
        private bool panning;
        private float personHeight = -1;
        private Sprite personSprite;
        private Vector2 personPivot = new Vector2(.5f, .15f);
        private string dropHint;
        private Rect dropRect;

        private void DrawCenter()
        {
            if (catalog == null || state == null) return;
            Rect area = new Rect(0, 0, center.contentRect.width, center.contentRect.height);
            // До первой раскладки размер — NaN: не рисовать и не считать.
            if (!(area.width >= 20) || !(area.height >= 20)) return;
            if (pendingFrameAll && Event.current.type == EventType.Layout) FrameAll();
            Event evt = Event.current;
            if (evt.type == EventType.DragExited) { dropHint = null; center.MarkDirtyRepaint(); }
            switch (state.Mode)
            {
                case ArtAssetCenterMode.Gallery: DrawGallery(area, evt); break;
                case ArtAssetCenterMode.Card: DrawCard(area, evt); break;
                default: DrawCanvas(area, evt); break;
            }
            if (dropHint != null && evt.type == EventType.Repaint)
            {
                Handles.DrawSolidRectangleWithOutline(dropRect, new Color(.95f, .75f, .3f, .12f), new Color(.95f, .75f, .3f, .95f));
                GUIStyle style = new GUIStyle(EditorStyles.boldLabel) { normal = { textColor = new Color(1, .85f, .45f) }, fontSize = 14 };
                GUI.Label(new Rect(dropRect.x + 10, dropRect.y + 8, dropRect.width - 20, 24), dropHint, style);
            }
        }

        private void FillBackground(Rect area)
        {
            if (Event.current.type != EventType.Repaint) return;
            switch (state.Background)
            {
                case ArtAssetBackground.Light: EditorGUI.DrawRect(area, new Color(.82f, .82f, .78f)); break;
                case ArtAssetBackground.Checker: ArtAssetDrawing.Checker(area, 16); break;
                default: EditorGUI.DrawRect(area, new Color(.09f, .1f, .095f)); break;
            }
        }

        private Color TextColor => state.Background == ArtAssetBackground.Dark ? new Color(.86f, .88f, .84f) : new Color(.1f, .1f, .1f);

        // ------------------------------------------------------------------
        // Эталон человека
        // ------------------------------------------------------------------

        // Рост фигуры в месте: размер клетки боя технического лагеря × рост
        // фигуры в клетках (как в LocationWorldRenderer).
        private float PersonHeight()
        {
            if (personHeight > 0) return personHeight;
            personHeight = 1.7f;
            LocalLocationDatabaseAsset locations = AssetDatabase.LoadAssetAtPath<LocalLocationDatabaseAsset>(LocalLocationDatabaseAsset.AssetPath);
            BattlefieldDatabaseAsset fields = Resources.Load<BattlefieldDatabaseAsset>(BattlefieldDatabaseAsset.ResourcesPath);
            LocalLocationDefinition camp = locations?.locations.Find(item => item.Id == "technical_lighting_camp") ?? locations?.locations.FirstOrDefault();
            BattlefieldDefinitionData field = camp != null ? fields?.FindById(camp.BattlefieldId) : null;
            if (camp != null && field != null)
            {
                LocalLocationGeometry geometry = new LocalLocationGeometry(camp, field, new List<Rect>());
                personHeight = geometry.ArenaHexSize / LocationVisualGeometry.PixelsPerUnit * LocationWorldRenderer.FieldHeightInHexSizes;
            }
            UnitDatabaseAsset units = Resources.Load<UnitDatabaseAsset>(UnitDatabaseAsset.ResourcesPath);
            CreatureAnimationDatabaseAsset animations = Resources.Load<CreatureAnimationDatabaseAsset>(CreatureAnimationDatabaseAsset.ResourcesPath);
            UnitDefinitionData unit = units?.Units.FirstOrDefault(item => item != null && item.Category == UnitCategory.Commander) ??
                                      units?.Units.FirstOrDefault(item => item != null);
            CreatureAnimationSetData set = unit != null && animations != null ? animations.FindSet(unit.AnimationSetId) : null;
            personSprite = set?.FindFirstFrame() ?? unit?.BattlefieldSprite;
            if (set != null) personPivot = set.Pivot;
            return personHeight;
        }

        private void DrawPerson(Vector2 anchor, float pixelsPerUnit)
        {
            float height = PersonHeight() * pixelsPerUnit;
            if (personSprite != null)
            {
                float width = height * personSprite.rect.width / personSprite.rect.height;
                ArtAssetDrawing.DrawSprite(new Rect(anchor.x - personPivot.x * width, anchor.y - (1 - personPivot.y) * height, width, height), personSprite,
                    false, new Color(1, 1, 1, .9f));
            }
            else if (Event.current.type == EventType.Repaint)
            {
                Color color = new Color(.55f, .62f, .75f, .9f);
                float w = height * .28f;
                EditorGUI.DrawRect(new Rect(anchor.x - w / 2, anchor.y - height * .82f, w, height * .5f), color);
                EditorGUI.DrawRect(new Rect(anchor.x - w / 2, anchor.y - height * .32f, w * .4f, height * .32f), color);
                EditorGUI.DrawRect(new Rect(anchor.x + w * .1f, anchor.y - height * .32f, w * .4f, height * .32f), color);
                Handles.color = color;
                Handles.DrawSolidDisc(new Vector2(anchor.x, anchor.y - height * .9f), Vector3.forward, height * .1f);
            }
            GUIStyle style = new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = TextColor }, alignment = TextAnchor.UpperCenter };
            GUI.Label(new Rect(anchor.x - 60, anchor.y + 2, 120, 16), "Человек · " + PersonHeight().ToString("0.##") + " ед.", style);
        }

        // ------------------------------------------------------------------
        // Холст
        // ------------------------------------------------------------------

        private void BuildCanvasLayout()
        {
            List<ArtAssetDefinition> items = Visible();
            (ArtAssetView, ArtAssetCanvasScale, int) key = (state.View, state.Scale, Mathf.RoundToInt(state.Zoom));
            if (!layoutDirty && key == layoutKey) return;
            layoutKey = key;
            layoutDirty = false;
            canvasItems.Clear();
            canvasRows.Clear();
            bool game = state.Scale == ArtAssetCanvasScale.GameSize;
            float label = LabelPixels / Mathf.Max(1, state.Zoom);
            float person = game ? PersonHeight() * .45f + CanvasGap : 0;
            float widest = 0;
            List<(ArtAssetDefinition asset, Rect bounds, float scale)> measured = new List<(ArtAssetDefinition, Rect, float)>();
            foreach (ArtAssetDefinition asset in items)
            {
                Rect bounds = ArtAssetDrawing.Resolve(catalog, asset, state.View).Bounds;
                float scale = game ? 1 : FitCell / Mathf.Max(.01f, Mathf.Max(bounds.width, bounds.height));
                measured.Add((asset, bounds, scale));
                widest = Mathf.Max(widest, bounds.width * scale);
            }
            float limit = game ? Mathf.Max(22, widest + person) : (FitCell + CanvasGap) * 8;
            bool custom = state.Order.Count > 0;
            List<List<int>> rows = new List<List<int>>();
            List<string> titles = new List<string>();
            float x = person;
            ArtAssetCategory? category = null;
            for (int i = 0; i < measured.Count; i++)
            {
                float width = game ? measured[i].bounds.width : FitCell;
                bool newCategory = !custom && category != measured[i].asset.Category;
                if (rows.Count == 0 || (rows[rows.Count - 1].Count > 0 && (x + width > limit || newCategory)))
                {
                    rows.Add(new List<int>());
                    titles.Add(!custom && (rows.Count == 1 || newCategory) ? ArtAssetLabels.CategoryTitle(measured[i].asset.Category) : null);
                    x = person;
                }
                category = measured[i].asset.Category;
                rows[rows.Count - 1].Add(i);
                x += width + CanvasGap;
            }
            float baseline = 0, previousBelow = 0;
            canvasContent = Rect.zero;
            bool first = true;
            for (int r = 0; r < rows.Count; r++)
            {
                float above = game ? PersonHeight() : 0, below = 0;
                foreach (int i in rows[r])
                {
                    (ArtAssetDefinition asset, Rect bounds, float scale) = measured[i];
                    above = Mathf.Max(above, game ? bounds.yMax * scale : FitCell);
                    below = Mathf.Max(below, game ? -bounds.yMin * scale : 0);
                }
                baseline += (r == 0 ? 0 : previousBelow + label + CanvasGap * 1.4f) + above + (titles[r] != null ? label * .6f : 0);
                previousBelow = below;
                CanvasRow row = new CanvasRow { Baseline = baseline, Left = 0, Title = titles[r] };
                x = person;
                foreach (int i in rows[r])
                {
                    (ArtAssetDefinition asset, Rect bounds, float scale) = measured[i];
                    float width = game ? bounds.width : FitCell;
                    Vector2 anchor = game ? new Vector2(x - bounds.xMin, baseline)
                        : new Vector2(x + FitCell / 2 - bounds.center.x * scale, baseline - FitCell / 2 + bounds.center.y * scale);
                    Rect art = new Rect(anchor.x + bounds.xMin * scale, anchor.y - bounds.yMax * scale, bounds.width * scale, bounds.height * scale);
                    if (!game) art = new Rect(x, baseline - FitCell, FitCell, FitCell);
                    Rect rect = Rect.MinMaxRect(art.xMin, art.yMin, Mathf.Max(art.xMax, art.xMin + 2.2f * label), Mathf.Max(art.yMax, baseline) + label);
                    canvasItems.Add(new CanvasItem { Asset = asset, Anchor = anchor, Scale = scale, ArtRect = art, Rect = rect });
                    x += width + CanvasGap;
                }
                row.Right = x;
                canvasRows.Add(row);
                Rect rowRect = Rect.MinMaxRect(0, baseline - above - label, Mathf.Max(x, person + 1), baseline + below + label);
                canvasContent = first ? rowRect : Rect.MinMaxRect(Mathf.Min(canvasContent.xMin, rowRect.xMin), Mathf.Min(canvasContent.yMin, rowRect.yMin),
                    Mathf.Max(canvasContent.xMax, rowRect.xMax), Mathf.Max(canvasContent.yMax, rowRect.yMax));
                first = false;
            }
        }

        private Vector2 CanvasToGui(Rect area, Vector2 point) => (point - state.Pan) * state.Zoom + area.center;
        private Vector2 GuiToCanvas(Rect area, Vector2 point) => (point - area.center) / state.Zoom + state.Pan;

        private void FrameAll()
        {
            Rect area = center != null ? new Rect(0, 0, center.contentRect.width, center.contentRect.height) : default;
            // Окно ещё не разложено (размер NaN): «Показать все» — при первой отрисовке.
            if (!(area.width >= 20) || !(area.height >= 20))
            {
                pendingFrameAll = true;
                center?.MarkDirtyRepaint();
                return;
            }
            pendingFrameAll = false;
            layoutDirty = true;
            BuildCanvasLayout();
            if (canvasContent.width <= 0) { state.Pan = Vector2.zero; center?.MarkDirtyRepaint(); return; }
            for (int pass = 0; pass < 2; pass++)
            {
                state.Zoom = Mathf.Clamp(Mathf.Min(area.width / (canvasContent.width + 2), area.height / (canvasContent.height + 2)), 4, 400);
                layoutDirty = true;
                BuildCanvasLayout();
            }
            state.Pan = canvasContent.center;
            ScheduleStateSave();
            center?.MarkDirtyRepaint();
        }

        private void FrameSelected()
        {
            if (state.Mode != ArtAssetCenterMode.Canvas) { SetMode(ArtAssetCenterMode.Canvas); }
            BuildCanvasLayout();
            CanvasItem item = canvasItems.Find(entry => entry.Asset.Id == state.SelectedId);
            if (item == null) { status.text = "Выбранный ассет скрыт фильтром или не выбран."; return; }
            Rect area = new Rect(0, 0, center.contentRect.width, center.contentRect.height);
            if (!(area.width >= 20) || !(area.height >= 20)) return;
            float zoom = Mathf.Min(area.width / Mathf.Max(.01f, item.Rect.width * 3), area.height / Mathf.Max(.01f, item.Rect.height * 1.8f));
            if (!float.IsNaN(zoom)) state.Zoom = Mathf.Clamp(zoom, 4, 400);
            layoutDirty = true;
            BuildCanvasLayout();
            item = canvasItems.Find(entry => entry.Asset.Id == state.SelectedId);
            if (item != null) state.Pan = item.Rect.center;
            ScheduleStateSave();
            center.MarkDirtyRepaint();
        }

        private CanvasItem CanvasHit(Rect area, Vector2 mouse)
        {
            Vector2 point = GuiToCanvas(area, mouse);
            for (int i = canvasItems.Count - 1; i >= 0; i--)
                if (canvasItems[i].Rect.Contains(point)) return canvasItems[i];
            return null;
        }

        private void DrawCanvas(Rect area, Event evt)
        {
            BuildCanvasLayout();
            if (HandleCanvasInput(area, evt)) return;
            if (evt.type != EventType.Repaint) return;
            FillBackground(area);
            if (canvasItems.Count == 0)
            {
                GUIStyle empty = new GUIStyle(EditorStyles.wordWrappedLabel) { alignment = TextAnchor.MiddleCenter, normal = { textColor = TextColor } };
                GUI.Label(area, catalog.assets.Count == 0
                    ? "Каталог пуст. Перетащите сюда папку с рисунками (6 ракурсов и нормали) или нажмите «Ещё → Создать технический набор»."
                    : "Ничего не найдено — измените поиск, категорию или фильтры.", empty);
                return;
            }
            Rect visibleRect = Rect.MinMaxRect(GuiToCanvas(area, area.min).x, GuiToCanvas(area, area.min).y, GuiToCanvas(area, area.max).x, GuiToCanvas(area, area.max).y);
            GUIStyle title = new GUIStyle(EditorStyles.boldLabel) { normal = { textColor = new Color(.95f, .78f, .42f) } };
            bool game = state.Scale == ArtAssetCanvasScale.GameSize;
            foreach (CanvasRow row in canvasRows)
            {
                if (row.Baseline < visibleRect.yMin - 30 || row.Baseline > visibleRect.yMax + 60) continue;
                Vector2 left = CanvasToGui(area, new Vector2(row.Left - .3f, row.Baseline)), right = CanvasToGui(area, new Vector2(row.Right, row.Baseline));
                EditorGUI.DrawRect(new Rect(left.x, left.y, right.x - left.x, 1), new Color(TextColor.r, TextColor.g, TextColor.b, .35f));
                if (game) DrawPerson(CanvasToGui(area, new Vector2(PersonHeight() * .2f, row.Baseline)), state.Zoom);
                if (row.Title != null)
                {
                    float top = row.Baseline;
                    foreach (CanvasItem item in canvasItems) if (Mathf.Approximately(item.Anchor.y, row.Baseline) || (!game && item.Rect.yMax > row.Baseline - .01f && item.Rect.yMin < row.Baseline)) top = Mathf.Min(top, item.ArtRect.yMin);
                    Vector2 at = CanvasToGui(area, new Vector2(row.Left, top));
                    GUI.Label(new Rect(at.x, at.y - 22, 300, 20), row.Title, title);
                }
            }
            GUIStyle name = new GUIStyle(EditorStyles.miniBoldLabel) { normal = { textColor = TextColor }, clipping = TextClipping.Clip };
            GUIStyle small = new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = TextColor }, clipping = TextClipping.Clip };
            GUIStyle warning = new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = new Color(1, .6f, .25f) }, clipping = TextClipping.Clip };
            foreach (CanvasItem item in canvasItems)
            {
                if (!item.Rect.Overlaps(visibleRect)) continue;
                ArtAssetDefinition asset = item.Asset;
                ArtAssetDrawing.Layout layout = ArtAssetDrawing.Resolve(catalog, asset, state.View);
                bool fallback = layout.Resolved.ViewFallback;
                Vector2 anchor = CanvasToGui(area, item.Anchor);
                ArtAssetDrawing.DrawAsset(catalog, asset, state.View, anchor, state.Zoom * item.Scale, ArtAssetDrawMode.Color, fallback ? new Color(1, 1, 1, .45f) : (Color?)null);
                Vector2 artMin = CanvasToGui(area, item.ArtRect.min), artMax = CanvasToGui(area, item.ArtRect.max);
                Rect art = Rect.MinMaxRect(artMin.x, artMin.y, artMax.x, artMax.y);
                if (asset.Id == state.SelectedId)
                    Handles.DrawSolidRectangleWithOutline(new Rect(art.x - 3, art.y - 3, art.width + 6, art.height + 6), new Color(1, .8f, .3f, .06f), new Color(1, .8f, .3f, 1));
                float labelY = Mathf.Max(art.yMax, CanvasToGui(area, new Vector2(0, item.Anchor.y)).y) + 2;
                float width = Mathf.Max(art.width, 190);
                GUI.Label(new Rect(art.x, labelY, width, 14), (asset.Favorite ? "★ " : "") + asset.Name, name);
                string line = ArtAssetDrawing.Completeness(asset);
                GUI.Label(new Rect(art.x, labelY + 12, width, 14), line, small);
                if (fallback) GUI.Label(new Rect(art.x, labelY + 24, width + 60, 14), "нет «" + ArtAssetLabels.ViewTitle(state.View) + "» → показан «" + ArtAssetLabels.ViewTitle(layout.Resolved.ShownView) + "»", warning);
                else if (layout.Resolved.NoArt) GUI.Label(new Rect(art.x, labelY + 24, width, 14), "нет рисунков", warning);
                else if (asset.NormalCount < asset.ViewCount) GUI.Label(new Rect(art.x, labelY + 24, width, 14), "⚠ нормали не у всех ракурсов", warning);
            }
        }

        private bool HandleCanvasInput(Rect area, Event evt)
        {
            Vector2 mouse = evt.mousePosition;
            switch (evt.type)
            {
                case EventType.ScrollWheel:
                    Vector2 before = GuiToCanvas(area, mouse);
                    state.Zoom = Mathf.Clamp(state.Zoom * Mathf.Pow(1.1f, -evt.delta.y), 4, 400);
                    state.Pan += before - GuiToCanvas(area, mouse);
                    ScheduleStateSave();
                    evt.Use();
                    center.MarkDirtyRepaint();
                    return true;
                case EventType.MouseDown when evt.button == 1 || evt.button == 2:
                    panning = true;
                    lastMouse = mouse;
                    evt.Use();
                    return true;
                case EventType.MouseDrag when panning:
                    state.Pan -= (mouse - lastMouse) / state.Zoom;
                    lastMouse = mouse;
                    ScheduleStateSave();
                    evt.Use();
                    center.MarkDirtyRepaint();
                    return true;
                case EventType.MouseUp when panning:
                    panning = false;
                    evt.Use();
                    return true;
                case EventType.MouseDown when evt.button == 0:
                    CanvasItem hit = CanvasHit(area, mouse);
                    center.Focus();
                    pressedId = hit?.Asset.Id;
                    pressPosition = mouse;
                    if (hit != null && evt.clickCount >= 2) { SelectAsset(hit.Asset.Id, true); evt.Use(); return true; }
                    if ((hit?.Asset.Id ?? "") != state.SelectedId) SelectAsset(hit?.Asset.Id);
                    evt.Use();
                    return true;
                case EventType.MouseDrag when evt.button == 0 && pressedId != null && Vector2.Distance(mouse, pressPosition) > 6:
                    ArtAssetDefinition dragged = catalog.Find(pressedId);
                    pressedId = null;
                    if (dragged != null) ArtAssetPicker.StartDrag(dragged);
                    evt.Use();
                    return true;
                case EventType.MouseUp when evt.button == 0:
                    pressedId = null;
                    return false;
            }
            return false;
        }

        // Свой ассет, брошенный на холст или в галерею: новый порядок показа.
        private void ReorderBefore(string id, ArtAssetDefinition before)
        {
            List<string> order = Visible().Select(item => item.Id).ToList();
            order.Remove(id);
            int index = before != null && before.Id != id ? order.IndexOf(before.Id) : order.Count;
            order.Insert(Mathf.Max(0, index), id);
            state.Order = order;
            visibleDirty = true;
            ScheduleStateSave();
            status.text = "Порядок на холсте изменён (только просмотр).";
        }

        // ------------------------------------------------------------------
        // Галерея
        // ------------------------------------------------------------------

        private Rect GalleryCard(Rect area, int index, int columns, float size) =>
            new Rect(area.x + 8 + (index % columns) * (size + 10), area.y + 8 + (index / columns) * (size + 76) - state.GalleryScroll, size, size + 68);

        private int GalleryColumns(Rect area) => Mathf.Max(1, Mathf.FloorToInt((area.width - 16) / (state.CardSize + 10)));

        private ArtAssetDefinition GalleryHit(Rect area, Vector2 mouse)
        {
            List<ArtAssetDefinition> items = Visible();
            int columns = GalleryColumns(area);
            for (int i = 0; i < items.Count; i++)
                if (GalleryCard(area, i, columns, state.CardSize).Contains(mouse)) return items[i];
            return null;
        }

        private void DrawGallery(Rect area, Event evt)
        {
            List<ArtAssetDefinition> items = Visible();
            int columns = GalleryColumns(area);
            float rowHeight = state.CardSize + 76;
            float content = Mathf.CeilToInt(items.Count / (float)columns) * rowHeight + 16;
            state.GalleryScroll = Mathf.Clamp(state.GalleryScroll, 0, Mathf.Max(0, content - area.height));
            Vector2 mouse = evt.mousePosition;
            switch (evt.type)
            {
                case EventType.ScrollWheel:
                    state.GalleryScroll = Mathf.Clamp(state.GalleryScroll + evt.delta.y * 24, 0, Mathf.Max(0, content - area.height));
                    evt.Use(); center.MarkDirtyRepaint(); return;
                case EventType.MouseDown when evt.button == 0:
                    ArtAssetDefinition hit = GalleryHit(area, mouse);
                    pressedId = hit?.Id;
                    pressPosition = mouse;
                    if (hit != null && evt.clickCount >= 2) { SelectAsset(hit.Id, true); evt.Use(); return; }
                    if ((hit?.Id ?? "") != state.SelectedId) SelectAsset(hit?.Id);
                    evt.Use(); return;
                case EventType.MouseDrag when evt.button == 0 && pressedId != null && Vector2.Distance(mouse, pressPosition) > 6:
                    ArtAssetDefinition dragged = catalog.Find(pressedId);
                    pressedId = null;
                    if (dragged != null) ArtAssetPicker.StartDrag(dragged);
                    evt.Use(); return;
            }
            if (evt.type != EventType.Repaint) return;
            FillBackground(area);
            int first = Mathf.Max(0, Mathf.FloorToInt(state.GalleryScroll / rowHeight) * columns);
            int last = Mathf.Min(items.Count - 1, Mathf.CeilToInt((state.GalleryScroll + area.height) / rowHeight) * columns + columns);
            GUIStyle name = new GUIStyle(EditorStyles.miniBoldLabel) { normal = { textColor = TextColor }, clipping = TextClipping.Clip };
            GUIStyle small = new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = TextColor }, clipping = TextClipping.Clip };
            GUIStyle warning = new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = new Color(1, .6f, .25f) }, clipping = TextClipping.Clip };
            for (int i = first; i <= last; i++)
            {
                ArtAssetDefinition asset = items[i];
                Rect card = GalleryCard(area, i, columns, state.CardSize);
                bool selected = asset.Id == state.SelectedId;
                EditorGUI.DrawRect(card, selected ? new Color(.95f, .75f, .3f, .3f) : new Color(0, 0, 0, .18f));
                Rect image = new Rect(card.x + 4, card.y + 4, card.width - 8, state.CardSize - 8);
                ArtAssetDrawing.Layout layout = ArtAssetDrawing.Resolve(catalog, asset, state.View);
                ArtAssetDrawing.DrawFitted(catalog, asset, state.View, image, ArtAssetDrawMode.Color, layout.Resolved.ViewFallback ? new Color(1, 1, 1, .45f) : (Color?)null);
                float y = card.y + state.CardSize - 2;
                GUI.Label(new Rect(card.x + 4, y, card.width - 8, 14), (asset.Favorite ? "★ " : "") + asset.Name, name);
                GUI.Label(new Rect(card.x + 4, y + 13, card.width - 8, 14), "Ракурсы: " + asset.ViewCount + "/6", small);
                GUI.Label(new Rect(card.x + 4, y + 26, card.width - 8, 14), "Нормали: " + asset.NormalCount + "/6", asset.NormalCount < asset.ViewCount ? warning : small);
                string note = layout.Resolved.NoArt ? "нет рисунков"
                    : layout.Resolved.ViewFallback ? "нет «" + ArtAssetLabels.ViewTitle(state.View) + "»"
                    : asset.NormalCount < asset.ViewCount ? "⚠ не все нормали" : UsageCount(asset.Id) > 0 ? "в местах: " + UsageCount(asset.Id) : "не используется";
                GUI.Label(new Rect(card.x + 4, y + 39, card.width - 8, 14), note, note.StartsWith("в местах") || note == "не используется" ? small : warning);
            }
        }
    }
}

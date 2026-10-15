using System;
using System.Collections.Generic;
using System.Linq;
using KingdomSurvival.BattlefieldDatabase;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace KingdomSurvival.LocationRendering.Editor
{
    // Предметы рисунка в окне Базы локаций: выбор мышью по непрозрачным
    // пикселям (выбранный предмет хватается первым, Alt+клик — следующий под
    // курсором), список в порядке отрисовки с перетаскиванием (выше/ниже в
    // кадре), видимость и замок в строке, клавиши холста, рамка выбранного и
    // подсветка того, что возьмёт клик.
    public sealed partial class LocationDatabaseWindow
    {
        private const float DragThreshold = 4;
        private Vector2 pressGui;
        private bool dragMoved;
        private string objectQuery = "";
        // Предмет под строкой списка, на которую наведена мышь (подсветка на холсте).
        private string hoveredListId;

        public const string ObjectKeysHelp =
            "Клик — выбрать; выбранный предмет тащится первым, даже если сверху другой. Alt+клик — следующий предмет под курсором. " +
            "Пустые места рисунка не ловят клик. Стрелки — сдвиг (Shift — ×10), PgUp/PgDn — выше/ниже в кадре (Shift — к краю слоя), " +
            "Tab / Shift+Tab — следующий / предыдущий, Ctrl+D — копия, Delete — удалить, F — показать, Esc — снять выбор. " +
            "Заблокированный предмет кликом на холсте не берётся — только в списке.";

        // ------------------------------------------------------------------
        // Выбор мышью
        // ------------------------------------------------------------------

        // Предметы, чей непрозрачный рисунок под точкой, — сверху вниз.
        private List<LocationVisualObject> ArtHits(LocalLocationDefinition location, Vector2 pixel)
        {
            List<LocationVisualObject> hits = new List<LocationVisualObject>();
            if (Visual == null || renderer == null) return hits;
            Vector3 world = LocationVisualGeometry.PixelToWorld(location, pixel);
            foreach (LocationVisualObject item in Visual.Objects)
                if (item != null && !item.Hidden && !item.LightOnly && OpaqueAt(item, world)) hits.Add(item);
            return hits.OrderByDescending(item => renderer.ObjectSortingOrder(item.Id)).ToList();
        }

        private bool OpaqueAt(LocationVisualObject item, Vector3 world)
        {
            foreach (SpriteRenderer image in renderer.ObjectImages(item.Id))
            {
                if (image == null || !image.enabled || image.sprite == null) continue;
                if (!image.bounds.Contains(new Vector3(world.x, world.y, image.bounds.center.z))) continue;
                Vector2 local = image.transform.InverseTransformPoint(world);
                if (image.flipX) local.x = -local.x;
                if (image.flipY) local.y = -local.y;
                if (LocationAlphaContours.IsOpaqueAt(image.sprite, local)) return true;
            }
            return false;
        }

        // Что возьмёт клик: выбранный (если он под курсором), иначе верхний
        // незаблокированный; next — следующий под курсором после выбранного.
        private LocationVisualObject PickArt(LocalLocationDefinition location, Vector2 pixel, bool next)
        {
            List<LocationVisualObject> hits = ArtHits(location, pixel);
            if (hits.Count == 0) return null;
            LocationVisualObject selected = ArtObject;
            List<LocationVisualObject> free = hits.Where(item => !item.Locked).ToList();
            if (next)
            {
                int at = selected != null ? hits.IndexOf(selected) : -1;
                return hits[(at + 1) % hits.Count];
            }
            if (selected != null && hits.Contains(selected)) return selected;
            return free.Count > 0 ? free[0] : hits[0];
        }

        // ------------------------------------------------------------------
        // Список предметов
        // ------------------------------------------------------------------

        private void BuildObjectList(LocalLocationDefinition location, LocationVisualDefinition visual)
        {
            TextField search = new TextField("Найти") { value = objectQuery };
            search.RegisterValueChangedCallback(evt => { objectQuery = evt.newValue ?? ""; BuildSettings(); });
            settings.Add(search);

            List<LocationVisualObject> all = LocationObjectOrder.TopFirst(location, visual);
            bool filtered = !string.IsNullOrWhiteSpace(objectQuery);
            List<LocationVisualObject> shown = filtered
                ? all.Where(item => (item.Name ?? "").IndexOf(objectQuery.Trim(), StringComparison.OrdinalIgnoreCase) >= 0).ToList()
                : all;
            Label caption = new Label(filtered
                ? "Найдено: " + shown.Count + " из " + all.Count + " (порядок перетаскиванием — без поиска)."
                : "Сверху — то, что рисуется поверх. Перетащите строку, чтобы предмет рисовался выше или ниже.");
            caption.style.whiteSpace = WhiteSpace.Normal;
            caption.style.color = new Color(.65f, .75f, .85f);
            caption.style.fontSize = 11;
            settings.Add(caption);

            ListView view = new ListView(shown, 22, MakeObjectRow, (element, index) => BindObjectRow(element, shown, index))
            {
                reorderable = !filtered,
                reorderMode = ListViewReorderMode.Animated,
                selectionType = SelectionType.Single,
                showBorder = true
            };
            view.style.height = Mathf.Clamp(shown.Count * 22 + 4, 48, 300);
            view.style.marginTop = 2;
            view.style.marginBottom = 4;
            int selectedIndex = shown.IndexOf(ArtObject);
            if (selectedIndex >= 0)
            {
                view.SetSelectionWithoutNotify(new[] { selectedIndex });
                view.schedule.Execute(() => view.ScrollToItem(selectedIndex));
            }
            view.selectionChanged += items =>
            {
                LocationVisualObject item = items.OfType<LocationVisualObject>().FirstOrDefault();
                if (item != null && item != ArtObject) Select(Kind.Art, item.Id);
            };
            view.itemIndexChanged += (from, to) =>
            {
                LocationVisualObject item = to >= 0 && to < shown.Count ? shown[to] : null;
                if (item == null || item.LightOnly) { BuildSettings(); return; }
                Change(() => LocationObjectOrder.PlaceAt(location, shown, to));
                selectedKind = Kind.Art;
                selectedElementId = item.Id;
                status.text = "«" + item.Name + "» — " + (to == 0 ? "поверх всех." : "под «" + shown[to - 1].Name + "».");
                view.schedule.Execute(BuildSettings);
            };
            settings.Add(view);

            LocationVisualObject selected = ArtObject;
            if (selected != null && !selected.LightOnly)
            {
                VisualElement order = new VisualElement();
                order.style.flexDirection = FlexDirection.Row;
                order.style.flexWrap = Wrap.Wrap;
                settings.Add(order);
                OrderButton(order, "▲ Выше", () => StepSelected(1, false), "PgUp");
                OrderButton(order, "▼ Ниже", () => StepSelected(-1, false), "PgDn");
                OrderButton(order, "⤒ Поверх слоя", () => StepSelected(1, true), "Shift+PgUp");
                OrderButton(order, "⤓ Под слоем", () => StepSelected(-1, true), "Shift+PgDn");
                OrderButton(order, "Показать", FrameSelected, "F");
                OrderButton(order, "Копия", DuplicateSelected, "Ctrl+D");
                OrderButton(order, "Удалить", DeleteSelected, "Delete");
            }
            Help(ObjectKeysHelp);
        }

        private static void OrderButton(VisualElement parent, string title, Action action, string keys) =>
            parent.Add(new Button(action) { text = title, tooltip = keys });

        private VisualElement MakeObjectRow()
        {
            VisualElement row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.paddingLeft = 2;
            UnityEngine.UIElements.Toggle shown = new UnityEngine.UIElements.Toggle { name = "shown", tooltip = "Показывать" };
            UnityEngine.UIElements.Toggle locked = new UnityEngine.UIElements.Toggle { name = "locked", tooltip = "Заблокировать: кликом на холсте не берётся и не двигается" };
            Label name = new Label { name = "name" };
            name.style.flexGrow = 1;
            name.style.overflow = Overflow.Hidden;
            name.style.textOverflow = TextOverflow.Ellipsis;
            Label band = new Label { name = "band" };
            band.style.color = new Color(.6f, .65f, .7f);
            band.style.fontSize = 10;
            band.style.marginRight = 4;
            row.Add(shown);
            row.Add(locked);
            row.Add(name);
            row.Add(band);
            row.RegisterCallback<PointerEnterEvent>(_ => hoveredListId = row.userData as string);
            row.RegisterCallback<PointerLeaveEvent>(_ => { if (hoveredListId == row.userData as string) hoveredListId = null; });
            return row;
        }

        private static readonly string[] BandShort = { "земля", "детали", "объекты", "кроны" };

        private void BindObjectRow(VisualElement row, List<LocationVisualObject> items, int index)
        {
            LocationVisualObject item = items[index];
            row.userData = item.Id;
            UnityEngine.UIElements.Toggle shown = row.Q<UnityEngine.UIElements.Toggle>("shown"), locked = row.Q<UnityEngine.UIElements.Toggle>("locked");
            shown.SetValueWithoutNotify(!item.Hidden);
            locked.SetValueWithoutNotify(item.Locked);
            shown.userData = item;
            locked.userData = item;
            // Обработчики — один раз на строку (строки переиспользуются ListView).
            if (shown.ClassListContains("bound") == false)
            {
                shown.AddToClassList("bound");
                shown.RegisterValueChangedCallback(evt =>
                {
                    if (shown.userData is LocationVisualObject target) Change(() => target.Hidden = !evt.newValue, true);
                });
                locked.RegisterValueChangedCallback(evt =>
                {
                    if (locked.userData is LocationVisualObject target) Change(() => target.Locked = evt.newValue, true);
                });
            }
            Label name = row.Q<Label>("name");
            name.text = (string.IsNullOrEmpty(item.Name) ? "без названия" : item.Name) + (item.UsesAsset ? "" : " · PNG");
            name.style.color = item.Hidden ? new Color(.5f, .5f, .5f) : item.Locked ? new Color(.75f, .7f, .6f) : new Color(.86f, .88f, .9f);
            int band = (int)LocationObjectOrder.BandOf(item);
            row.Q<Label>("band").text = item.LightOnly ? "свет" : band >= 0 && band < BandShort.Length ? BandShort[band] : "";
        }

        // ------------------------------------------------------------------
        // Действия с выбранным
        // ------------------------------------------------------------------

        private void StepSelected(int direction, bool toEdge)
        {
            LocationVisualObject selected = ArtObject;
            LocalLocationDefinition location = Location;
            if (selected == null || Visual == null || location == null) return;
            bool moved = false;
            Change(() => moved = toEdge
                ? LocationObjectOrder.ToEdgeOfBand(location, Visual, selected, direction > 0)
                : LocationObjectOrder.Step(location, Visual, selected, direction), true);
            status.text = moved ? "«" + selected.Name + "» " + (direction > 0 ? "выше." : "ниже.") : "Дальше некуда.";
        }

        private void FrameSelected()
        {
            LocationVisualObject selected = ArtObject;
            if (selected == null || Location == null) return;
            if (renderer != null && renderer.TryObjectBounds(selected.Id, out Bounds bounds))
                viewCenter = LocationVisualGeometry.WorldToPixel(Location, bounds.center);
            else viewCenter = LocationVisualGeometry.ToPixel(Location, selected.Position);
            canvas?.MarkDirtyRepaint();
        }

        private void DuplicateSelected()
        {
            LocationVisualObject selected = ArtObject;
            LocationVisualDefinition visual = Visual;
            if (selected == null || visual == null) return;
            Change(() =>
            {
                LocationVisualObject copy = JsonUtility.FromJson<LocationVisualObject>(JsonUtility.ToJson(selected));
                copy.Id = Guid.NewGuid().ToString("N");
                copy.GroupId = "";
                copy.Locked = false;
                copy.Position += new Vector2(.02f, .02f);
                visual.Objects.Insert(visual.Objects.IndexOf(selected) + 1, copy);
                selectedElementId = copy.Id;
            }, true);
            status.text = "Копия «" + selected.Name + "».";
        }

        private void DeleteSelected()
        {
            LocationVisualObject selected = ArtObject;
            LocationVisualDefinition visual = Visual;
            if (selected == null || visual == null) return;
            Change(() => { visual.Objects.Remove(selected); selectedKind = Kind.None; selectedElementId = null; }, true);
            status.text = "Удалён «" + selected.Name + "» (Ctrl+Z — вернуть).";
        }

        private void NudgeSelected(Vector2 pixels)
        {
            LocationVisualObject selected = ArtObject;
            if (selected == null || selected.Locked || Visual == null) return;
            Vector2 normalized = new Vector2(pixels.x / CanvasSize.x, pixels.y / CanvasSize.y);
            Change(() =>
            {
                foreach (LocationVisualObject item in Visual.Objects)
                    if (!item.Locked && (item == selected || (!string.IsNullOrEmpty(selected.GroupId) && item.GroupId == selected.GroupId)))
                    {
                        item.Position += normalized;
                        renderer?.MoveObject(item.Id, item.Position);
                    }
            });
        }

        private void SelectNeighbour(int direction)
        {
            LocalLocationDefinition location = Location;
            if (Visual == null || location == null) return;
            List<LocationVisualObject> list = LocationObjectOrder.TopFirst(location, Visual).Where(item => !item.Hidden).ToList();
            if (list.Count == 0) return;
            int at = list.IndexOf(ArtObject);
            int next = at < 0 ? (direction > 0 ? 0 : list.Count - 1) : (at + direction + list.Count) % list.Count;
            Select(Kind.Art, list[next].Id);
            status.text = "Выбран «" + list[next].Name + "».";
        }

        // Клавиши холста для предметов (инструмент «Выбор»). true — съедено.
        private bool HandleObjectKeys(Event evt)
        {
            if (evt.type != EventType.KeyDown || tool != Tool.Select || Visual == null) return false;
            bool action = evt.control || evt.command;
            if (evt.keyCode == KeyCode.Tab) { SelectNeighbour(evt.shift ? -1 : 1); return true; }
            LocationVisualObject selected = ArtObject;
            if (selected == null) return false;
            float step = evt.shift ? 10 : 1;
            switch (evt.keyCode)
            {
                case KeyCode.LeftArrow: NudgeSelected(new Vector2(-step, 0)); return true;
                case KeyCode.RightArrow: NudgeSelected(new Vector2(step, 0)); return true;
                case KeyCode.UpArrow: NudgeSelected(new Vector2(0, -step)); return true;
                case KeyCode.DownArrow: NudgeSelected(new Vector2(0, step)); return true;
                case KeyCode.PageUp: StepSelected(1, evt.shift); return true;
                case KeyCode.PageDown: StepSelected(-1, evt.shift); return true;
                case KeyCode.Delete:
                case KeyCode.Backspace: DeleteSelected(); return true;
                case KeyCode.D when action: DuplicateSelected(); return true;
                case KeyCode.F when !action: FrameSelected(); return true;
                case KeyCode.Escape: Select(Kind.None, null); return true;
            }
            return false;
        }

        // ------------------------------------------------------------------
        // Подсветка на холсте
        // ------------------------------------------------------------------

        // Рамка выбранного предмета и пунктир того, что возьмёт клик (или
        // строки списка под мышью).
        private void DrawObjectHighlights(LocalLocationDefinition location, Rect frame, Func<float, float, Vector2> gui)
        {
            if (renderer == null || Visual == null) return;
            LocationVisualObject selected = ArtObject;
            if (selected != null && !selected.LightOnly) DrawObjectBox(location, selected.Id, gui, new Color(1, .85f, .2f, .95f), false);
            string hover = hoveredListId;
            if (hover == null && tool == Tool.Select && !dragging && frame.Contains(previewMouse))
                hover = PickArt(location, ToPixel(frame, previewMouse), false)?.Id;
            if (hover != null && hover != selected?.Id) DrawObjectBox(location, hover, gui, new Color(.6f, .85f, 1f, .8f), true);
        }

        private void DrawObjectBox(LocalLocationDefinition location, string id, Func<float, float, Vector2> gui, Color color, bool dotted)
        {
            if (!renderer.TryObjectBounds(id, out Bounds bounds)) return;
            Vector2 a = LocationVisualGeometry.WorldToPixel(location, bounds.min), b = LocationVisualGeometry.WorldToPixel(location, bounds.max);
            Vector2 ga = gui(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y)), gb = gui(Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
            Vector3[] corners = { new Vector3(ga.x, ga.y), new Vector3(gb.x, ga.y), new Vector3(gb.x, gb.y), new Vector3(ga.x, gb.y), new Vector3(ga.x, ga.y) };
            Handles.color = color;
            if (dotted) Handles.DrawDottedLines(new[] { corners[0], corners[1], corners[1], corners[2], corners[2], corners[3], corners[3], corners[0] }, 4);
            else Handles.DrawAAPolyLine(2, corners);
        }
    }
}

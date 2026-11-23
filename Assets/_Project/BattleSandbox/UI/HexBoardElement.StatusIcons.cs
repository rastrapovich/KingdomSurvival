using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace KingdomSurvival.BattleSandbox
{
    // Пакет «Боевые состояния», §10: значки действующих временных состояний
    // над бойцом. Только представление SandboxBattle.GetStatusViews — без
    // своей симуляции. Пул элементов UI Toolkit; раскладка — полуовал с
    // равным шагом X (круги одной дуги не пересекаются), до 6 значков + «+N».
    public sealed partial class HexBoardElement
    {
        public const float StatusIconSize = 30f;
        public const float StatusIconGap = 8f;
        public const int StatusIconMaxVisible = 6;
        private const float StatusBaseOffset = 10f;
        private const float StatusLevelStep = 24f;
        private const int StatusLevels = 3;

        // Уменьшение движения: без масштабирования и пульсации, только смена прозрачности.
        public static bool StatusReducedMotion { get; set; }

        private sealed class StatusSlot
        {
            public VisualElement Root;
            public Label Fallback;
            public Label Duration;
            public Label Charges;
            public string StatusId;
            public string Tooltip;
            public List<SandboxStatusView> TipViews = new List<SandboxStatusView>();
        }

        private sealed class StatusGroup
        {
            public readonly List<StatusSlot> Slots = new List<StatusSlot>();
            public StatusSlot More;
            public List<SandboxStatusView> Views = new List<SandboxStatusView>();
            public int VisibleCount;
            public Rect Bounds;
        }

        private static readonly Dictionary<string, Sprite> statusSprites = new Dictionary<string, Sprite>(StringComparer.Ordinal);
        private static readonly HashSet<string> missingStatusSprites = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, StatusGroup> statusGroups = new Dictionary<string, StatusGroup>(StringComparer.Ordinal);
        private readonly List<(SandboxUnitState Unit, Vector2 Anchor)> statusAnchors = new List<(SandboxUnitState, Vector2)>();
        private readonly List<Rect> placedStatusBounds = new List<Rect>();
        private readonly HashSet<string> seenStatusUnits = new HashSet<string>(StringComparer.Ordinal);
        private VisualElement statusTooltip;
        private StatusSlot pinnedStatusSlot;
        private int statusViewsKey = int.MinValue;

        // Общий загрузчик значков (поле боя и карточка бойца): кэш, без повторной загрузки.
        internal static Sprite LoadStatusSprite(string iconId) => StatusSprite(iconId);

        private static Sprite StatusSprite(string iconId)
        {
            if (string.IsNullOrEmpty(iconId))
                return null;
            if (statusSprites.TryGetValue(iconId, out Sprite sprite))
                return sprite;
            sprite = Resources.Load<Sprite>("StatusIcons/" + iconId.Replace('.', '_'));
            statusSprites[iconId] = sprite;
            if (sprite == null && missingStatusSprites.Add(iconId))
                Debug.LogWarning("Значок состояния не найден: Resources/StatusIcons/" + iconId.Replace('.', '_') + ".png — показан текстовый заменитель.");
            return sprite;
        }

        // Ключ пересборки списков: состояния, ходы, удары, стойки и связи.
        private int StatusViewsKey()
        {
            int key = battle.StatusRevision * 31 + battle.Round * 7919 + battle.CurrentTurnIndex * 131 + battle.HitRecords.Count * 17 + battle.Links.Count * 3;
            foreach (SandboxUnitState unit in battle.Units)
            {
                if (unit.IsGuarding)
                    key += unit.Id.GetHashCode() & 0xff;
            }
            return key;
        }

        // Вызывается из SyncUnitImages: якорь — верх стабильной рамки фигуры
        // (не верхний пиксель кадра), поэтому дыхание не двигает значки.
        private void CollectStatusAnchor(SandboxUnitState unit, Vector2 ground, float figureTop, HexLayout layout)
        {
            float top = Mathf.Max(figureTop, ground.y - layout.Size * 1.6f);
            statusAnchors.Add((unit, new Vector2(ground.x, top)));
        }

        private void SyncStatusIcons()
        {
            if (battle == null)
            {
                ClearStatusIcons();
                return;
            }
            int key = StatusViewsKey();
            bool rebuild = key != statusViewsKey;
            statusViewsKey = key;

            // Сначала текущий боец — ему приоритет места; затем по экранному Y.
            SandboxUnitState current = battle.CurrentUnit;
            statusAnchors.Sort((a, b) =>
            {
                bool ac = current != null && a.Unit.Id == current.Id;
                bool bc = current != null && b.Unit.Id == current.Id;
                if (ac != bc)
                    return ac ? -1 : 1;
                int byY = a.Anchor.y.CompareTo(b.Anchor.y);
                return byY != 0 ? byY : string.CompareOrdinal(a.Unit.Id, b.Unit.Id);
            });

            placedStatusBounds.Clear();
            seenStatusUnits.Clear();
            foreach ((SandboxUnitState unit, Vector2 anchor) in statusAnchors)
            {
                if (!statusGroups.TryGetValue(unit.Id, out StatusGroup group))
                {
                    group = new StatusGroup();
                    statusGroups.Add(unit.Id, group);
                    rebuild = true;
                }
                if (rebuild)
                    group.Views = battle.GetStatusViews(unit.Id, SandboxTeam.Player);
                if (group.Views.Count == 0)
                {
                    HideGroup(group);
                    continue;
                }
                seenStatusUnits.Add(unit.Id);
                LayoutGroup(unit, group, anchor);
            }
            foreach (KeyValuePair<string, StatusGroup> pair in statusGroups)
            {
                if (!seenStatusUnits.Contains(pair.Key))
                    HideGroup(pair.Value);
            }
            statusAnchors.Clear();
        }

        private void LayoutGroup(SandboxUnitState unit, StatusGroup group, Vector2 anchor)
        {
            int total = group.Views.Count;
            int visible = total > StatusIconMaxVisible ? StatusIconMaxVisible : total;
            int level = 0;
            Rect bounds = GroupBounds(anchor, SlotCount(visible, total), level);
            // Конфликт с уже размещённой группой: поднять на ограниченный
            // уровень, затем сократить до одного важного значка и «+N».
            while (Overlaps(bounds) && level < StatusLevels - 1)
            {
                level++;
                bounds = GroupBounds(anchor, SlotCount(visible, total), level);
            }
            if (Overlaps(bounds))
            {
                visible = 1;
                level = 0;
                bounds = GroupBounds(anchor, SlotCount(visible, total), level);
            }
            // Прижать к границам поля общим смещением всей группы.
            float shiftX = 0f;
            if (bounds.xMin < 2f)
                shiftX = 2f - bounds.xMin;
            else if (bounds.xMax > contentRect.width - 2f)
                shiftX = contentRect.width - 2f - bounds.xMax;
            float shiftY = bounds.yMin < 2f ? 2f - bounds.yMin : 0f;
            bounds.position += new Vector2(shiftX, shiftY);
            placedStatusBounds.Add(bounds);
            group.Bounds = bounds;
            group.VisibleCount = visible;

            int slots = SlotCount(visible, total);
            for (int j = 0; j < slots; j++)
            {
                bool more = j == visible && total > visible;
                StatusSlot slot = more ? (group.More ??= CreateSlot()) : SlotAt(group, j);
                Vector2 center = SlotCenter(anchor, slots, j, level) + new Vector2(shiftX, shiftY);
                if (more)
                    FillMore(slot, group, total - visible);
                else
                    FillSlot(slot, group.Views[j], unit);
                Place(slot, center);
            }
            for (int j = visible; j < group.Slots.Count; j++)
                group.Slots[j].Root.style.display = DisplayStyle.None;
            if (group.More != null && total <= visible)
                group.More.Root.style.display = DisplayStyle.None;
        }

        private static int SlotCount(int visible, int total) => visible + (total > visible ? 1 : 0);

        // x(j) = (j − (m−1)/2)·s; y(j) = −baseOffset − Ry·sqrt(1 − (x/Rx)²).
        private static Vector2 SlotCenter(Vector2 anchor, int m, int j, int level)
        {
            float s = StatusIconSize + StatusIconGap;
            float x = (j - (m - 1) * 0.5f) * s;
            float y;
            if (m <= 1)
            {
                y = 0f;
            }
            else
            {
                float rx = Mathf.Max(56f, (m - 1) * s * 0.5f);
                float ry = Mathf.Clamp(18f + 3f * m, 24f, 40f);
                float t = Mathf.Clamp01(1f - (x / rx) * (x / rx));
                // Дуга выгнута вверх: центр выше краёв.
                y = -ry * Mathf.Sqrt(t) + ry * Mathf.Sqrt(Mathf.Clamp01(1f - ((m - 1) * 0.5f * s / rx) * ((m - 1) * 0.5f * s / rx)));
            }
            return new Vector2(anchor.x + x, anchor.y - StatusBaseOffset - StatusIconSize * 0.5f + y - level * StatusLevelStep);
        }

        private static Rect GroupBounds(Vector2 anchor, int m, int level)
        {
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            for (int j = 0; j < m; j++)
            {
                Vector2 c = SlotCenter(anchor, m, j, level);
                minX = Mathf.Min(minX, c.x - StatusIconSize * 0.5f);
                maxX = Mathf.Max(maxX, c.x + StatusIconSize * 0.5f);
                minY = Mathf.Min(minY, c.y - StatusIconSize * 0.5f);
                maxY = Mathf.Max(maxY, c.y + StatusIconSize * 0.5f);
            }
            return Rect.MinMaxRect(minX, minY, maxX, maxY);
        }

        private bool Overlaps(Rect bounds)
        {
            for (int i = 0; i < placedStatusBounds.Count; i++)
            {
                if (placedStatusBounds[i].Overlaps(bounds))
                    return true;
            }
            return false;
        }

        private StatusSlot SlotAt(StatusGroup group, int index)
        {
            while (group.Slots.Count <= index)
                group.Slots.Add(CreateSlot());
            return group.Slots[index];
        }

        private StatusSlot CreateSlot()
        {
            StatusSlot slot = new StatusSlot();
            VisualElement root = new VisualElement { focusable = false, pickingMode = PickingMode.Ignore };
            root.style.position = Position.Absolute;
            root.style.width = StatusIconSize;
            root.style.height = StatusIconSize;
            root.style.opacity = 0f;
            if (!StatusReducedMotion)
            {
                root.style.scale = new Scale(new Vector3(0.9f, 0.9f, 1f));
                root.style.transitionProperty = new List<StylePropertyName> { new StylePropertyName("opacity"), new StylePropertyName("scale"), new StylePropertyName("left"), new StylePropertyName("top") };
                root.style.transitionDuration = new List<TimeValue> { new TimeValue(0.19f), new TimeValue(0.19f), new TimeValue(0.15f), new TimeValue(0.15f) };
            }
            else
            {
                root.style.transitionProperty = new List<StylePropertyName> { new StylePropertyName("opacity") };
                root.style.transitionDuration = new List<TimeValue> { new TimeValue(0.12f) };
            }

            slot.Fallback = new Label { pickingMode = PickingMode.Ignore };
            slot.Fallback.style.position = Position.Absolute;
            slot.Fallback.style.left = 0; slot.Fallback.style.top = 0; slot.Fallback.style.right = 0; slot.Fallback.style.bottom = 0;
            slot.Fallback.style.unityTextAlign = TextAnchor.MiddleCenter;
            slot.Fallback.style.fontSize = 9;
            slot.Fallback.style.whiteSpace = WhiteSpace.Normal;
            slot.Fallback.style.color = new Color(0.98f, 0.94f, 0.86f, 1f);
            SetRadius(slot.Fallback, StatusIconSize * 0.5f);
            root.Add(slot.Fallback);

            slot.Duration = Badge(true);
            slot.Charges = Badge(false);
            root.Add(slot.Duration);
            root.Add(slot.Charges);
            slot.Root = root;

            // Описание — подсказка по наведению (UpdateStatusTooltip); полный список — в карточке бойца.
            Add(root);
            // Появление: прозрачность и масштаб 0,9 → 1 на следующем кадре.
            root.schedule.Execute(() =>
            {
                root.style.opacity = 1f;
                root.style.scale = new Scale(Vector3.one);
            });
            return slot;
        }

        private static Label Badge(bool topRight)
        {
            Label badge = new Label { pickingMode = PickingMode.Ignore };
            badge.style.position = Position.Absolute;
            badge.style.minWidth = 13;
            badge.style.height = 13;
            badge.style.fontSize = 9;
            badge.style.unityFontStyleAndWeight = FontStyle.Bold;
            badge.style.unityTextAlign = TextAnchor.MiddleCenter;
            badge.style.paddingLeft = 2; badge.style.paddingRight = 2;
            badge.style.paddingTop = 0; badge.style.paddingBottom = 0;
            badge.style.color = new Color(0.98f, 0.95f, 0.88f, 1f);
            badge.style.backgroundColor = new Color(0.12f, 0.10f, 0.08f, 0.92f);
            SetRadius(badge, 6f);
            if (topRight)
            {
                badge.style.right = -4;
                badge.style.top = -4;
            }
            else
            {
                badge.style.left = -4;
                badge.style.bottom = -4;
            }
            return badge;
        }

        private static void SetRadius(VisualElement element, float radius)
        {
            element.style.borderTopLeftRadius = radius;
            element.style.borderTopRightRadius = radius;
            element.style.borderBottomLeftRadius = radius;
            element.style.borderBottomRightRadius = radius;
        }

        private void FillSlot(StatusSlot slot, SandboxStatusView view, SandboxUnitState unit)
        {
            slot.Root.style.display = DisplayStyle.Flex;
            if (slot.StatusId != view.StatusId)
            {
                slot.StatusId = view.StatusId;
                Sprite sprite = StatusSprite(view.IconId);
                if (sprite != null)
                {
                    slot.Root.style.backgroundImage = new StyleBackground(sprite);
                    slot.Fallback.text = string.Empty;
                    slot.Fallback.style.backgroundColor = Color.clear;
                }
                else
                {
                    // Явный заменитель с названием: отсутствие ассета видно и в валидаторе.
                    slot.Root.style.backgroundImage = StyleKeyword.None;
                    slot.Fallback.text = view.Name.Length > 8 ? view.Name.Substring(0, 7) + "…" : view.Name;
                    slot.Fallback.style.backgroundColor = view.Polarity == SandboxStatusPolarity.Beneficial
                        ? new Color(0.22f, 0.36f, 0.26f, 0.95f)
                        : new Color(0.42f, 0.16f, 0.14f, 0.95f);
                }
            }
            string duration = view.TriggerRound > 0 ? "р" + view.TriggerRound
                : view.Dependent ? "св"
                : view.TurnsLeft > 0 ? view.TurnsLeft.ToString() : string.Empty;
            slot.Duration.text = duration;
            slot.Duration.style.display = string.IsNullOrEmpty(duration) ? DisplayStyle.None : DisplayStyle.Flex;
            slot.Charges.text = view.Charges > 1 ? "×" + view.Charges : string.Empty;
            slot.Charges.style.display = view.Charges > 1 ? DisplayStyle.Flex : DisplayStyle.None;
            // Форма маркера, а не только цвет: польза — светлая рамка, вред — тёмная.
            Color border = view.Polarity == SandboxStatusPolarity.Beneficial ? new Color(0.86f, 0.92f, 0.74f, 1f) : new Color(0.14f, 0.08f, 0.06f, 1f);
            float width = view.StrongControl ? 2.5f : 1f;
            slot.Root.style.borderTopColor = border; slot.Root.style.borderBottomColor = border;
            slot.Root.style.borderLeftColor = border; slot.Root.style.borderRightColor = border;
            slot.Root.style.borderTopWidth = width; slot.Root.style.borderBottomWidth = width;
            slot.Root.style.borderLeftWidth = width; slot.Root.style.borderRightWidth = width;
            SetRadius(slot.Root, StatusIconSize * 0.5f);
            slot.Tooltip = unit.DisplayLabel + " — " + view.Tooltip;
            slot.TipViews.Clear();
            slot.TipViews.Add(view);
        }

        private void FillMore(StatusSlot slot, StatusGroup group, int hidden)
        {
            slot.Root.style.display = DisplayStyle.Flex;
            slot.StatusId = null;
            slot.Root.style.backgroundImage = StyleKeyword.None;
            slot.Fallback.text = "+" + hidden;
            slot.Fallback.style.fontSize = 12;
            slot.Fallback.style.backgroundColor = new Color(0.16f, 0.13f, 0.10f, 0.95f);
            slot.Duration.style.display = DisplayStyle.None;
            slot.Charges.style.display = DisplayStyle.None;
            // Полный список — по наведению/фокусу/клику на «+N».
            List<string> lines = new List<string>();
            for (int i = 0; i < group.Views.Count; i++)
                lines.Add("• " + group.Views[i].Tooltip);
            slot.Tooltip = string.Join("\n", lines);
            slot.TipViews.Clear();
            slot.TipViews.AddRange(group.Views);
        }

        // Подсказка по наведению на значок: строка «иконка слева, справа название,
        // под названием описание»; для «+N» — строки всех состояний бойца.
        // Значки не принимают указатель, поэтому попадание считается по их рамкам.
        private void UpdateStatusTooltip(Vector2 pointer)
        {
            StatusSlot hit = null;
            foreach (StatusGroup group in statusGroups.Values)
            {
                foreach (StatusSlot slot in group.Slots)
                    if (slot.Root.resolvedStyle.display != DisplayStyle.None && slot.Root.layout.Contains(pointer)) { hit = slot; break; }
                if (hit == null && group.More != null && group.More.Root.resolvedStyle.display != DisplayStyle.None && group.More.Root.layout.Contains(pointer))
                    hit = group.More;
                if (hit != null)
                    break;
            }
            if (hit == null || hit.TipViews.Count == 0)
            {
                HideStatusTooltip();
                return;
            }
            if (statusTooltip == null)
            {
                statusTooltip = new VisualElement { pickingMode = PickingMode.Ignore, focusable = false };
                statusTooltip.style.position = Position.Absolute;
                statusTooltip.style.width = 260f;
                statusTooltip.style.paddingLeft = 8f; statusTooltip.style.paddingRight = 8f;
                statusTooltip.style.paddingTop = 6f; statusTooltip.style.paddingBottom = 2f;
                statusTooltip.style.backgroundColor = new Color(0.10f, 0.09f, 0.08f, 0.96f);
                statusTooltip.style.borderTopColor = statusTooltip.style.borderBottomColor = statusTooltip.style.borderLeftColor = statusTooltip.style.borderRightColor = new Color(0.46f, 0.38f, 0.26f, 1f);
                statusTooltip.style.borderTopWidth = statusTooltip.style.borderBottomWidth = statusTooltip.style.borderLeftWidth = statusTooltip.style.borderRightWidth = 1f;
                SetRadius(statusTooltip, 3f);
                Add(statusTooltip);
            }
            statusTooltip.Clear();
            foreach (SandboxStatusView view in hit.TipViews)
                statusTooltip.Add(BuildStatusTipRow(view));
            statusTooltip.style.display = DisplayStyle.Flex;
            statusTooltip.BringToFront();
            // Над значком; у верхнего края — под ним; внутри поля по X.
            Rect slotRect = hit.Root.layout;
            float height = Mathf.Max(48f, hit.TipViews.Count * 64f);
            float left = Mathf.Clamp(slotRect.center.x - 130f, 2f, Mathf.Max(2f, contentRect.width - 262f));
            float top = slotRect.yMin - height - 6f;
            if (top < 2f)
                top = slotRect.yMax + 6f;
            statusTooltip.style.left = left;
            statusTooltip.style.top = top;
        }

        private void HideStatusTooltip()
        {
            if (statusTooltip != null)
                statusTooltip.style.display = DisplayStyle.None;
        }

        private static VisualElement BuildStatusTipRow(SandboxStatusView view)
        {
            bool good = view.Polarity == SandboxStatusPolarity.Beneficial;
            VisualElement row = new VisualElement { pickingMode = PickingMode.Ignore };
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.FlexStart;
            row.style.marginBottom = 6f;

            VisualElement icon = new VisualElement { pickingMode = PickingMode.Ignore };
            icon.style.width = 32f; icon.style.height = 32f;
            icon.style.flexShrink = 0f; icon.style.marginRight = 8f;
            Sprite sprite = StatusSprite(view.IconId);
            if (sprite != null)
                icon.style.backgroundImage = new StyleBackground(sprite);
            else
            {
                icon.style.backgroundColor = good ? new Color(0.22f, 0.36f, 0.26f, 1f) : new Color(0.42f, 0.16f, 0.14f, 1f);
                SetRadius(icon, 16f);
            }
            row.Add(icon);

            VisualElement text = new VisualElement { pickingMode = PickingMode.Ignore };
            text.style.flexGrow = 1f; text.style.flexShrink = 1f;
            Label name = new Label(view.Name) { pickingMode = PickingMode.Ignore };
            name.style.fontSize = 12;
            name.style.unityFontStyleAndWeight = FontStyle.Bold;
            name.style.whiteSpace = WhiteSpace.Normal;
            name.style.color = good ? new Color(0.72f, 0.86f, 0.62f, 1f) : new Color(0.95f, 0.66f, 0.55f, 1f);
            text.Add(name);
            // Описание — сразу под названием, с переносом строк.
            if (!string.IsNullOrEmpty(view.Effect))
            {
                Label effect = new Label(view.Effect) { pickingMode = PickingMode.Ignore };
                effect.style.fontSize = 10;
                effect.style.whiteSpace = WhiteSpace.Normal;
                effect.style.color = new Color(0.84f, 0.83f, 0.78f, 1f);
                text.Add(effect);
            }
            string time = view.TriggerRound > 0 ? "срабатывает в раунде " + view.TriggerRound
                : view.Dependent ? "пока действует связь"
                : view.TurnsLeft > 0 ? "осталось ходов: " + view.TurnsLeft : string.Empty;
            if (view.Charges > 1)
                time += (time.Length > 0 ? " · " : string.Empty) + "заряды: " + view.Charges;
            if (time.Length > 0)
            {
                Label meta = new Label(time) { pickingMode = PickingMode.Ignore };
                meta.style.fontSize = 9;
                meta.style.whiteSpace = WhiteSpace.Normal;
                meta.style.color = new Color(0.62f, 0.62f, 0.58f, 1f);
                meta.style.marginTop = 2f;
                text.Add(meta);
            }
            row.Add(text);
            return row;
        }

        private static void Place(StatusSlot slot, Vector2 center)
        {
            slot.Root.style.left = center.x - StatusIconSize * 0.5f;
            slot.Root.style.top = center.y - StatusIconSize * 0.5f;
        }

        private static void HideGroup(StatusGroup group)
        {
            foreach (StatusSlot slot in group.Slots)
                slot.Root.style.display = DisplayStyle.None;
            if (group.More != null)
                group.More.Root.style.display = DisplayStyle.None;
        }

        private void ClearStatusIcons()
        {
            foreach (StatusGroup group in statusGroups.Values)
            {
                foreach (StatusSlot slot in group.Slots)
                    slot.Root.RemoveFromHierarchy();
                group.More?.Root.RemoveFromHierarchy();
            }
            statusGroups.Clear();
            statusAnchors.Clear();
            pinnedStatusSlot = null;
            statusTooltip?.RemoveFromHierarchy();
            statusTooltip = null;
            statusViewsKey = int.MinValue;
        }
    }
}

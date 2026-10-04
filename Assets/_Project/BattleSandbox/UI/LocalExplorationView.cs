using System;
using System.Collections.Generic;
using KingdomSurvival.AnimationDatabase;
using KingdomSurvival.BattlefieldDatabase;
using UnityEngine;
using UnityEngine.UIElements;

namespace KingdomSurvival.BattleSandbox
{
    // ПР-12К (канон v1.53 §28.3): поле исследуемого места. Это то же поле
    // Базы полей боя в том же кадре (BattlefieldView, cover), что и в бою на
    // месте, поэтому фон, стены и клетки при начале боя не сдвигаются.
    // Сетка скрыта; фигуры стоят опорой в центре клетки, как в бою (та же
    // высота и опора набора анимаций). Шаг между клетками — доля пути.
    // Окно только показывает и сообщает о кликах; решает контроллер.
    public sealed class LocalExplorationView : VisualElement
    {
        public enum ActorKind
        {
            Fighter,
            Retinue,
            Enemy
        }

        // Как показать одного участника в этом кадре.
        public struct ActorFrame
        {
            public string Id;
            public string UnitTypeId;
            // Буквы жетона, если у типа нет рисунка.
            public string TokenText;
            public ActorKind Kind;
            public HexCoord From;
            public HexCoord To;
            public float Progress;
            public HexFacing Facing;
            public bool Walking;
            public bool Wounded;
        }

        private sealed class Figure
        {
            public VisualElement Root;
            public Image Image;
            public Label Token;
            public CreatureAnimationPlayer Player;
            public string UnitTypeId;
            public Sprite LastSprite;
        }

        private sealed class ObjectMarker
        {
            public VisualElement Root;
            public Label Label;
            public HexCoord Cell;
        }

        private const float FieldHeightInHexSizes = HexBoardElement.FieldHeightInHexSizes;
        private const float StaticAnchorFromBottom = HexBoardElement.StaticAnchorFromBottom;

        private readonly BattlefieldView surface;
        private readonly VisualElement objectLayer;
        private readonly VisualElement actorLayer;
        private readonly VisualElement clickMarker;
        private readonly SandboxUnitContent content;
        private readonly Dictionary<string, Figure> figures = new Dictionary<string, Figure>(StringComparer.Ordinal);
        private readonly Dictionary<string, ObjectMarker> objects = new Dictionary<string, ObjectMarker>(StringComparer.Ordinal);
        private readonly List<ActorFrame> frames = new List<ActorFrame>();
        private float clickMarkerUntil = -1f;
        private HexCoord clickMarkerCell;

        public event Action<HexCoord> CellClicked;
        public event Action<string> ObjectClicked;

        public BattlefieldView Surface => surface;

        public LocalExplorationView(BattlefieldDefinitionData field, BattlefieldHexStyle hexStyle)
        {
            name = "local-exploration-view";
            AddToClassList("local-exploration-view");
            Fill(this);
            style.overflow = Overflow.Hidden;
            style.backgroundColor = new Color(0.035f, 0.043f, 0.050f, 1f);

            content = SandboxUnitDatabaseAdapter.Load();

            surface = new BattlefieldView(true) { name = "local-exploration-surface" };
            Fill(surface);
            surface.Show(field, hexStyle);
            // Скрытая разметка: при исследовании гексы не рисуются.
            surface.Q("battlefield-hex-fill")?.style.SetDisplay(false);
            surface.Q("battlefield-hex-images")?.style.SetDisplay(false);
            surface.Q("battlefield-hex-lines")?.style.SetDisplay(false);
            // Пометку «ждёт рисунка» при исследовании показывает шапка места.
            surface.Q("battlefield-placeholder-label")?.style.SetDisplay(false);
            surface.LayoutChanged += Redraw;
            Add(surface);

            objectLayer = new VisualElement { name = "local-exploration-objects", pickingMode = PickingMode.Ignore };
            Fill(objectLayer);
            Add(objectLayer);

            clickMarker = new VisualElement { name = "local-exploration-click-marker", pickingMode = PickingMode.Ignore };
            clickMarker.AddToClassList("local-exploration-click-marker");
            clickMarker.style.position = Position.Absolute;
            clickMarker.style.display = DisplayStyle.None;
            objectLayer.Add(clickMarker);

            actorLayer = new VisualElement { name = "local-exploration-actors", pickingMode = PickingMode.Ignore };
            Fill(actorLayer);
            Add(actorLayer);

            RegisterCallback<PointerDownEvent>(OnPointerDown);
        }

        private static void Fill(VisualElement element)
        {
            element.style.position = Position.Absolute;
            element.style.left = 0f;
            element.style.right = 0f;
            element.style.top = 0f;
            element.style.bottom = 0f;
        }

        // ------------------------------------------------------------------
        // Объекты места
        // ------------------------------------------------------------------

        public void SetObject(string id, HexCoord cell, string label, bool active, bool shown)
        {
            if (!objects.TryGetValue(id, out ObjectMarker marker))
            {
                marker = new ObjectMarker { Root = new VisualElement { pickingMode = PickingMode.Ignore } };
                marker.Root.AddToClassList("local-object-marker");
                marker.Root.style.position = Position.Absolute;
                marker.Label = new Label { pickingMode = PickingMode.Ignore };
                marker.Label.AddToClassList("local-object-label");
                marker.Root.Add(marker.Label);
                objects.Add(id, marker);
                objectLayer.Add(marker.Root);
            }
            marker.Cell = cell;
            marker.Label.text = label;
            marker.Root.style.display = shown ? DisplayStyle.Flex : DisplayStyle.None;
            marker.Root.EnableInClassList("local-object-marker--active", active);
            marker.Root.EnableInClassList("local-object-marker--done", !active);
            PlaceObject(marker);
        }

        private void PlaceObject(ObjectMarker marker)
        {
            BattlefieldGridLayout layout = surface.Layout;
            if (layout.Size <= 0.01f)
                return;
            Vector2 center = layout.GetCenter(marker.Cell.Q, marker.Cell.R);
            float width = layout.Size * 2.6f;
            marker.Root.style.width = width;
            marker.Root.style.left = center.x - width * 0.5f;
            marker.Root.style.top = center.y - layout.Size * 0.45f;
        }

        public void ShowClickMarker(HexCoord cell)
        {
            clickMarkerCell = cell;
            clickMarkerUntil = Time.realtimeSinceStartup + 0.6f;
        }

        // ------------------------------------------------------------------
        // Участники
        // ------------------------------------------------------------------

        public void Render(IReadOnlyList<ActorFrame> actors)
        {
            frames.Clear();
            frames.AddRange(actors);
            Redraw();
        }

        private void Redraw()
        {
            BattlefieldGridLayout layout = surface.Layout;
            if (layout.Size <= 0.01f)
                return;

            foreach (ObjectMarker marker in objects.Values)
                PlaceObject(marker);

            float now = Time.realtimeSinceStartup;
            if (now < clickMarkerUntil)
            {
                Vector2 center = layout.GetCenter(clickMarkerCell.Q, clickMarkerCell.R);
                float size = layout.Size * 0.9f;
                clickMarker.style.display = DisplayStyle.Flex;
                clickMarker.style.width = size;
                clickMarker.style.height = size * 0.6f;
                clickMarker.style.left = center.x - size * 0.5f;
                clickMarker.style.top = center.y - size * 0.3f;
                clickMarker.style.opacity = Mathf.Clamp01((clickMarkerUntil - now) / 0.6f);
            }
            else
            {
                clickMarker.style.display = DisplayStyle.None;
            }

            HashSet<string> visible = new HashSet<string>(StringComparer.Ordinal);
            List<(float depth, VisualElement element)> depth = new List<(float, VisualElement)>();
            foreach (ActorFrame frame in frames)
            {
                visible.Add(frame.Id);
                Figure figure = GetFigure(frame);
                Vector2 from = layout.GetCenter(frame.From.Q, frame.From.R);
                Vector2 to = layout.GetCenter(frame.To.Q, frame.To.R);
                Vector2 ground = Vector2.Lerp(from, to, Mathf.Clamp01(frame.Progress));
                LayoutFigure(figure, frame, ground, layout, now);
                depth.Add((ground.y, figure.Root));
            }

            List<string> stale = new List<string>();
            foreach (KeyValuePair<string, Figure> entry in figures)
            {
                if (!visible.Contains(entry.Key))
                    stale.Add(entry.Key);
            }
            foreach (string id in stale)
            {
                figures[id].Root.RemoveFromHierarchy();
                figures.Remove(id);
            }

            // Дальние ниже ближних — по земле.
            depth.Sort((a, b) => a.depth.CompareTo(b.depth));
            foreach ((float _, VisualElement element) in depth)
                element.BringToFront();
        }

        private Figure GetFigure(ActorFrame frame)
        {
            if (figures.TryGetValue(frame.Id, out Figure figure) && figure.UnitTypeId == frame.UnitTypeId)
                return figure;
            if (figure != null)
                figure.Root.RemoveFromHierarchy();

            figure = new Figure
            {
                UnitTypeId = frame.UnitTypeId,
                Root = new VisualElement { name = "local-actor-" + frame.Id, pickingMode = PickingMode.Ignore }
            };
            figure.Root.AddToClassList("local-actor");
            figure.Root.style.position = Position.Absolute;
            figure.Image = new Image { pickingMode = PickingMode.Ignore, scaleMode = ScaleMode.StretchToFill };
            figure.Image.style.position = Position.Absolute;
            figure.Root.Add(figure.Image);
            figure.Token = new Label { pickingMode = PickingMode.Ignore };
            figure.Token.AddToClassList("local-actor-token");
            figure.Token.style.position = Position.Absolute;
            figure.Root.Add(figure.Token);

            SandboxUnitVisual visual = content.GetVisual(frame.UnitTypeId);
            if (visual != null && visual.IsAnimated)
            {
                float phase = (frame.Id.GetHashCode() & 0x7fff) / 32767f * 2f;
                figure.Player = new CreatureAnimationPlayer(visual.AnimationSet, DirectionFor(frame), phase);
                figure.Player.Play(CreatureAnimationAction.Idle, Time.realtimeSinceStartup);
            }
            figures[frame.Id] = figure;
            actorLayer.Add(figure.Root);
            return figure;
        }

        private CreatureAnimationDirection DirectionFor(ActorFrame frame)
        {
            HexFacing facing = frame.Kind == ActorKind.Enemy ? HexBoardElement.MirrorFacing(frame.Facing) : frame.Facing;
            CreatureAnimationDatabaseAsset database = content.AnimationDatabase;
            if (database != null)
                return database.GetDirection(facing);
            foreach (CreatureAnimationDirectionMapping entry in CreatureAnimationDatabaseAsset.DefaultDirectionMap())
            {
                if (entry.Facing == facing)
                    return entry.Direction;
            }
            return CreatureAnimationDirection.Front;
        }

        private void LayoutFigure(Figure figure, ActorFrame frame, Vector2 ground, BattlefieldGridLayout layout, float now)
        {
            SandboxUnitVisual visual = content.GetVisual(frame.UnitTypeId);
            bool mirrored = frame.Kind == ActorKind.Enemy;
            figure.Root.EnableInClassList("local-actor--retinue", frame.Kind == ActorKind.Retinue);
            figure.Root.EnableInClassList("local-actor--enemy", frame.Kind == ActorKind.Enemy);
            figure.Root.EnableInClassList("local-actor--wounded", frame.Wounded);
            Color tint = frame.Kind == ActorKind.Retinue ? new Color(0.86f, 0.84f, 0.78f, 0.92f) : Color.white;

            if (visual != null && visual.IsAnimated && figure.Player != null)
            {
                CreatureAnimationSetData set = visual.AnimationSet;
                figure.Player.SetDirection(DirectionFor(frame));
                CreatureAnimationAction action = frame.Walking ? CreatureAnimationAction.Walk : CreatureAnimationAction.Idle;
                if (figure.Player.Action != action)
                    figure.Player.Play(action, now);
                Sprite sprite = figure.Player.Evaluate(now) ?? figure.LastSprite ?? set.FindFirstFrame() ?? visual.BattlefieldSprite;
                figure.LastSprite = sprite;
                figure.Image.sprite = sprite;
                Vector2 canvas = set.CanvasSize.x > 0 && set.CanvasSize.y > 0
                    ? (Vector2)set.CanvasSize
                    : sprite != null ? sprite.rect.size : Vector2.one;
                float height = layout.Size * FieldHeightInHexSizes * set.FieldScale;
                float width = height * canvas.x / Mathf.Max(1f, canvas.y);
                Vector2 cellOffset = figure.Player.Clip != null ? figure.Player.Clip.Offset : Vector2.zero;
                float pivotX = mirrored ? 1f - set.Pivot.x : set.Pivot.x;
                float offsetX = mirrored ? -cellOffset.x : cellOffset.x;
                figure.Image.style.display = DisplayStyle.Flex;
                figure.Token.style.display = DisplayStyle.None;
                figure.Image.style.width = width;
                figure.Image.style.height = height;
                figure.Image.style.scale = new Scale(new Vector3(mirrored ? -1f : 1f, 1f, 1f));
                figure.Image.style.left = ground.x - pivotX * width + offsetX * width;
                figure.Image.style.top = ground.y - (1f - set.Pivot.y) * height - cellOffset.y * height;
                figure.Image.tintColor = tint;
            }
            else if (visual != null && visual.BattlefieldSprite != null)
            {
                // Нет кадров — статичная миниатюра; ходьба от этого не зависит.
                float size = layout.Size * FieldHeightInHexSizes * visual.BattlefieldScale;
                Vector2 offset = visual.BattlefieldOffset;
                if (mirrored)
                    offset.x = -offset.x;
                Vector2 center = ground + offset;
                figure.Image.sprite = visual.BattlefieldSprite;
                figure.Image.scaleMode = ScaleMode.ScaleToFit;
                figure.Image.style.display = DisplayStyle.Flex;
                figure.Token.style.display = DisplayStyle.None;
                figure.Image.style.scale = new Scale(new Vector3(mirrored ? -1f : 1f, 1f, 1f));
                figure.Image.style.width = size;
                figure.Image.style.height = size;
                figure.Image.style.left = center.x - size * 0.5f;
                figure.Image.style.top = center.y - size * (1f - StaticAnchorFromBottom);
                figure.Image.tintColor = tint;
            }
            else
            {
                // Ни кадров, ни миниатюры — жетон с буквами (как в бою).
                string text = !string.IsNullOrEmpty(visual?.TokenText) ? visual.TokenText : frame.TokenText;
                float size = layout.Size * 1.1f;
                figure.Image.style.display = DisplayStyle.None;
                figure.Token.style.display = DisplayStyle.Flex;
                figure.Token.text = string.IsNullOrEmpty(text) ? "?" : text;
                figure.Token.style.width = size;
                figure.Token.style.height = size * 0.8f;
                figure.Token.style.left = ground.x - size * 0.5f;
                figure.Token.style.top = ground.y - size * 0.75f;
                figure.Token.style.fontSize = Mathf.Max(9f, size * 0.32f);
            }
        }

        // ------------------------------------------------------------------
        // Клики
        // ------------------------------------------------------------------

        private void OnPointerDown(PointerDownEvent evt)
        {
            if (evt.button != 0)
                return;
            if (!TryGetCell(evt.localPosition, out HexCoord cell))
                return;
            foreach (KeyValuePair<string, ObjectMarker> entry in objects)
            {
                if (entry.Value.Cell == cell && entry.Value.Root.style.display != DisplayStyle.None)
                {
                    ObjectClicked?.Invoke(entry.Key);
                    evt.StopPropagation();
                    return;
                }
            }
            CellClicked?.Invoke(cell);
            evt.StopPropagation();
        }

        public bool TryGetCell(Vector2 localPoint, out HexCoord cell)
        {
            cell = default;
            BattlefieldGridLayout layout = surface.Layout;
            if (layout.Size <= 0.01f || !layout.TryGetCell(localPoint, out int q, out int r))
                return false;
            cell = new HexCoord(q, r);
            return true;
        }

        // Экранная точка центра клетки (для автотестов и подсказок).
        public Vector2 CellCenter(HexCoord cell) => surface.Layout.GetCenter(cell.Q, cell.R);
    }

    internal static class LocalExplorationStyleExtensions
    {
        public static void SetDisplay(this IStyle style, bool visible)
        {
            style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }
}

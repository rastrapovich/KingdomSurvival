using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace KingdomSurvival.BattleSandbox
{
    public sealed class HexBoardElement : VisualElement
    {
        private static readonly Color NormalColor = new Color(0.16f, 0.20f, 0.22f, 1f);
        private static readonly Color DifficultColor = new Color(0.29f, 0.25f, 0.17f, 1f);
        private static readonly Color ImpassableColor = new Color(0.07f, 0.08f, 0.09f, 1f);
        private static readonly Color ReachableColor = new Color(0.16f, 0.33f, 0.40f, 1f);
        private static readonly Color AttackableColor = new Color(0.42f, 0.16f, 0.15f, 1f);

        private SandboxBattle battle;
        private string selectedTargetId;

        public event Action<HexCoord> HexClicked;
        public event Action<string, Vector2> UnitDetailsRequested;

        public HexBoardElement()
        {
            name = "battle-sandbox-board";
            style.flexGrow = 1f;
            style.minWidth = 620f;
            style.minHeight = 520f;
            style.backgroundColor = new Color(0.055f, 0.065f, 0.075f, 1f);
            style.borderLeftWidth = 1f;
            style.borderRightWidth = 1f;
            style.borderTopWidth = 1f;
            style.borderBottomWidth = 1f;
            Color borderColor = new Color(0.30f, 0.32f, 0.32f, 1f);
            style.borderLeftColor = borderColor;
            style.borderRightColor = borderColor;
            style.borderTopColor = borderColor;
            style.borderBottomColor = borderColor;
            pickingMode = PickingMode.Position;

            generateVisualContent += DrawBoard;
            RegisterCallback<PointerDownEvent>(OnPointerDown);
            RegisterCallback<GeometryChangedEvent>(_ => MarkDirtyRepaint());
        }

        public void SetBattle(SandboxBattle value, string targetId)
        {
            battle = value;
            selectedTargetId = targetId;
            MarkDirtyRepaint();
        }

        private void OnPointerDown(PointerDownEvent evt)
        {
            if (battle == null || (evt.button != 0 && evt.button != 1))
                return;

            HexLayout layout = CalculateLayout();
            Vector2 pointerPosition = new Vector2(evt.localPosition.x, evt.localPosition.y);
            float bestDistance = float.MaxValue;
            HexCoord best = default;
            bool found = false;

            for (int r = 0; r < battle.Height; r++)
            {
                for (int q = 0; q < battle.Width; q++)
                {
                    HexCoord coord = new HexCoord(q, r);
                    Vector2 center = layout.GetCenter(coord);
                    float distance = (center - pointerPosition).sqrMagnitude;
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        best = coord;
                        found = true;
                    }
                }
            }

            if (found && bestDistance <= layout.Size * layout.Size)
            {
                if (evt.button == 1)
                {
                    SandboxUnitState occupant = battle.GetUnitAt(best);
                    if (occupant == null)
                        return;

                    Vector2 panelPosition = new Vector2(evt.position.x, evt.position.y);
                    UnitDetailsRequested?.Invoke(occupant.Id, panelPosition);
                    evt.StopPropagation();
                    return;
                }

                HexClicked?.Invoke(best);
                evt.StopPropagation();
            }
        }

        private void DrawBoard(MeshGenerationContext context)
        {
            if (battle == null || contentRect.width <= 1f || contentRect.height <= 1f)
                return;

            Painter2D painter = context.painter2D;
            HexLayout layout = CalculateLayout();
            SandboxUnitState current = battle.CurrentUnit;
            IReadOnlyDictionary<HexCoord, int> reachable =
                current != null && current.Team == SandboxTeam.Player
                    ? battle.GetReachable(current.Id)
                    : new Dictionary<HexCoord, int>();

            for (int r = 0; r < battle.Height; r++)
            {
                for (int q = 0; q < battle.Width; q++)
                {
                    HexCoord coord = new HexCoord(q, r);
                    SandboxTerrain terrain = battle.GetTerrain(coord);
                    SandboxUnitState occupant = battle.GetUnitAt(coord);
                    Color fill = GetTerrainColor(terrain);

                    if (reachable.ContainsKey(coord))
                        fill = ReachableColor;

                    if (current != null && current.Team == SandboxTeam.Player &&
                        occupant != null && occupant.Team == SandboxTeam.Enemy &&
                        battle.PreviewAttack(current.Id, occupant.Id).IsValid)
                    {
                        fill = AttackableColor;
                    }

                    if (occupant != null && occupant.Id == selectedTargetId)
                        fill = new Color(0.58f, 0.20f, 0.17f, 1f);

                    DrawHex(
                        painter,
                        layout.GetCenter(coord),
                        layout.Size - 1.5f,
                        fill,
                        new Color(0.36f, 0.38f, 0.38f, 1f),
                        1.2f);
                }
            }

            foreach (SandboxUnitState unit in battle.Units)
            {
                if (unit.IsDefeated)
                    continue;
                DrawUnit(painter, layout, unit, current);
            }
        }

        private void DrawUnit(
            Painter2D painter,
            HexLayout layout,
            SandboxUnitState unit,
            SandboxUnitState current)
        {
            Vector2 center = layout.GetCenter(unit.Position);
            float radius = layout.Size * 0.55f;
            Color fill = unit.Team == SandboxTeam.Player
                ? new Color(0.78f, 0.62f, 0.27f, 1f)
                : new Color(0.70f, 0.25f, 0.22f, 1f);

            Color outline = current != null && current.Id == unit.Id
                ? Color.white
                : new Color(0.05f, 0.05f, 0.05f, 1f);
            DrawCircle(
                painter,
                center,
                radius,
                fill,
                outline,
                current != null && current.Id == unit.Id ? 4f : 2f);

            if (unit.IsGuarding)
            {
                DrawCircle(
                    painter,
                    center,
                    radius + 5f,
                    new Color(0f, 0f, 0f, 0f),
                    new Color(0.35f, 0.82f, 0.90f, 1f),
                    3f,
                    false);
            }

            float barWidth = layout.Size * 1.25f;
            float barHeight = 6f;
            Vector2 barOrigin = center + new Vector2(-barWidth * 0.5f, -radius - 12f);
            DrawRectangle(painter, barOrigin, new Vector2(barWidth, barHeight), new Color(0.05f, 0.05f, 0.05f, 1f));
            DrawRectangle(
                painter,
                barOrigin,
                new Vector2(barWidth * unit.HitPoints / unit.MaxHitPoints, barHeight),
                unit.HitPoints > unit.MaxHitPoints * 0.35f
                    ? new Color(0.32f, 0.72f, 0.38f, 1f)
                    : new Color(0.84f, 0.31f, 0.26f, 1f));

            DrawRoleMark(painter, center, layout.Size, unit.Role);
        }

        private static void DrawRoleMark(
            Painter2D painter,
            Vector2 center,
            float size,
            SandboxUnitRole role)
        {
            painter.strokeColor = new Color(0.10f, 0.09f, 0.07f, 0.9f);
            painter.lineWidth = 3f;
            float half = size * 0.22f;
            painter.BeginPath();

            if (role == SandboxUnitRole.Archer)
            {
                painter.MoveTo(center + new Vector2(-half, half));
                painter.LineTo(center + new Vector2(half, -half));
                painter.MoveTo(center + new Vector2(half * 0.2f, -half));
                painter.LineTo(center + new Vector2(half, -half));
                painter.LineTo(center + new Vector2(half, -half * 0.2f));
            }
            else if (role == SandboxUnitRole.Guard)
            {
                painter.MoveTo(center + new Vector2(-half, -half));
                painter.LineTo(center + new Vector2(-half, half));
                painter.LineTo(center + new Vector2(half, half));
                painter.LineTo(center + new Vector2(half, -half));
                painter.ClosePath();
            }
            else if (role == SandboxUnitRole.Healer)
            {
                painter.MoveTo(center + new Vector2(-half, 0f));
                painter.LineTo(center + new Vector2(half, 0f));
                painter.MoveTo(center + new Vector2(0f, -half));
                painter.LineTo(center + new Vector2(0f, half));
            }
            else if (role == SandboxUnitRole.Spearman)
            {
                painter.MoveTo(center + new Vector2(-half, half));
                painter.LineTo(center + new Vector2(half, -half));
            }
            else if (role == SandboxUnitRole.Scout)
            {
                painter.MoveTo(center + new Vector2(0f, -half));
                painter.LineTo(center + new Vector2(half, 0f));
                painter.LineTo(center + new Vector2(0f, half));
                painter.LineTo(center + new Vector2(-half, 0f));
                painter.ClosePath();
            }
            else
            {
                painter.MoveTo(center + new Vector2(-half, 0f));
                painter.LineTo(center + new Vector2(half, 0f));
            }

            painter.Stroke();
        }

        private static Color GetTerrainColor(SandboxTerrain terrain)
        {
            switch (terrain)
            {
                case SandboxTerrain.Difficult:
                    return DifficultColor;
                case SandboxTerrain.Impassable:
                    return ImpassableColor;
                default:
                    return NormalColor;
            }
        }

        private static void DrawHex(
            Painter2D painter,
            Vector2 center,
            float radius,
            Color fill,
            Color stroke,
            float lineWidth)
        {
            painter.fillColor = fill;
            painter.strokeColor = stroke;
            painter.lineWidth = lineWidth;
            painter.BeginPath();

            for (int i = 0; i < 6; i++)
            {
                float angle = Mathf.Deg2Rad * (60f * i - 30f);
                Vector2 point = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                if (i == 0)
                    painter.MoveTo(point);
                else
                    painter.LineTo(point);
            }

            painter.ClosePath();
            painter.Fill();
            painter.Stroke();
        }

        private static void DrawRectangle(
            Painter2D painter,
            Vector2 origin,
            Vector2 size,
            Color fill)
        {
            painter.fillColor = fill;
            painter.BeginPath();
            painter.MoveTo(origin);
            painter.LineTo(origin + new Vector2(size.x, 0f));
            painter.LineTo(origin + size);
            painter.LineTo(origin + new Vector2(0f, size.y));
            painter.ClosePath();
            painter.Fill();
        }

        private static void DrawCircle(
            Painter2D painter,
            Vector2 center,
            float radius,
            Color fill,
            Color stroke,
            float lineWidth,
            bool fillShape = true)
        {
            const int segments = 20;
            painter.fillColor = fill;
            painter.strokeColor = stroke;
            painter.lineWidth = lineWidth;
            painter.BeginPath();
            for (int i = 0; i < segments; i++)
            {
                float angle = Mathf.PI * 2f * i / segments;
                Vector2 point = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                if (i == 0)
                    painter.MoveTo(point);
                else
                    painter.LineTo(point);
            }
            painter.ClosePath();
            if (fillShape)
                painter.Fill();
            painter.Stroke();
        }

        private HexLayout CalculateLayout()
        {
            float availableWidth = Mathf.Max(100f, contentRect.width - 36f);
            float availableHeight = Mathf.Max(100f, contentRect.height - 36f);
            float widthUnits = Mathf.Sqrt(3f) * (battle.Width + (battle.Height - 1) * 0.5f);
            float heightUnits = 1.5f * (battle.Height - 1) + 2f;
            float size = Mathf.Min(availableWidth / widthUnits, availableHeight / heightUnits);
            size = Mathf.Max(18f, size);

            float boardWidth = widthUnits * size;
            float boardHeight = heightUnits * size;
            Vector2 origin = new Vector2(
                (contentRect.width - boardWidth) * 0.5f + Mathf.Sqrt(3f) * size * 0.5f,
                (contentRect.height - boardHeight) * 0.5f + size);
            return new HexLayout(size, origin);
        }

        private readonly struct HexLayout
        {
            public float Size { get; }
            private Vector2 Origin { get; }

            public HexLayout(float size, Vector2 origin)
            {
                Size = size;
                Origin = origin;
            }

            public Vector2 GetCenter(HexCoord coord)
            {
                float x = Size * Mathf.Sqrt(3f) * (coord.Q + coord.R * 0.5f);
                float y = Size * 1.5f * coord.R;
                return Origin + new Vector2(x, y);
            }
        }
    }
}

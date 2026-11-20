using System;
using System.Collections.Generic;
using System.Linq;
using KingdomSurvival.MapLabBridge;
using UnityEngine;
using UnityEngine.UIElements;

namespace KingdomSurvival.BattleSandbox
{
    // ПР-16: режим выбора цели способности. Клетки-кандидаты, предпросмотр
    // по наведению. Истина — в модели боя; доска только показывает.
    public sealed class BoardAbilityTargeting
    {
        public HashSet<HexCoord> Cells { get; } = new HashSet<HexCoord>();
        public HexCoord? FirstAnchor { get; set; }
        public Func<HexCoord, SandboxAbilityPreview> Preview { get; set; }
    }

    // ПР-16: программный рисунок состояний способностей — вода, разрыхлённая
    // земля, ямы, нити, лозы, жгуты, яйца, объявленные угрозы, статусы.
    // Без PNG: всё строится по данным клеток и объектов боя.
    public sealed partial class HexBoardElement
    {
        private BoardAbilityTargeting abilityTargeting;
        private HexCoord? abilityHoverCell;
        private SandboxAbilityPreview abilityHoverPreview;

        public event Action<HexCoord> AbilityCellClicked;
        public event Action AbilityTargetingCancelled;

        private static readonly SandboxUnitVisual HiddenStumpVisual =
            new SandboxUnitVisual(null, null, 1f, Vector2.zero, "?", 1f);

        public BoardAbilityTargeting AbilityTargeting
        {
            get => abilityTargeting;
            set
            {
                abilityTargeting = value;
                abilityHoverCell = null;
                abilityHoverPreview = null;
                MarkDirtyRepaint();
            }
        }

        // true — событие обработано режимом способности.
        private bool HandleAbilityPointerDown(PointerDownEvent evt, HexCoord cell)
        {
            if (abilityTargeting == null)
                return false;
            if (evt.button == 1)
                AbilityTargetingCancelled?.Invoke();
            else
                AbilityCellClicked?.Invoke(cell);
            evt.StopPropagation();
            return true;
        }

        private bool UpdateAbilityHover(HexCoord? cell)
        {
            if (abilityTargeting == null)
                return false;
            abilityHoverCell = cell;
            abilityHoverPreview = cell.HasValue && abilityTargeting.Preview != null &&
                                  (abilityTargeting.Cells.Contains(cell.Value) || abilityTargeting.FirstAnchor.HasValue)
                ? abilityTargeting.Preview(cell.Value)
                : null;
            tooltip = abilityHoverPreview == null
                ? "Выберите подсвеченную клетку · ПКМ — отмена"
                : abilityHoverPreview.IsValid
                    ? abilityHoverPreview.Summary + " · ЛКМ — применить"
                    : abilityHoverPreview.Reason;
            MarkDirtyRepaint();
            return true;
        }

        // Подпись наведения на клетку с объектом или угрозой (вне режима цели).
        private string DescribeCell(HexCoord cell)
        {
            if (battle == null)
                return string.Empty;
            List<string> parts = new List<string>();
            SandboxCellState state = battle.GetCellState(cell);
            if ((state & SandboxCellState.ShallowWater) != 0) parts.Add("мелкая вода: вход 2");
            if ((state & SandboxCellState.LooseEarth) != 0) parts.Add("разрыхлённая земля: вход 2");
            if ((state & SandboxCellState.Pit) != 0) parts.Add("яма: непроходима");
            if ((state & SandboxCellState.Subsided) != 0) parts.Add("осевшая земля: вход 2");
            foreach (SandboxBattleObject item in battle.Objects.Where(item => !item.IsDestroyed && item.Cells.Contains(cell)))
            {
                string extra = item.Kind == SandboxObjectKind.Thread ? ", до раунда " + item.ExpiresAtRound + " — вход останавливает движение"
                    : item.Kind == SandboxObjectKind.Egg ? ", вылупится через " + item.Counter + " ход(а) матки" : string.Empty;
                parts.Add(item.Label + " " + item.HitPoints + "/" + item.MaxHitPoints + " HP" + extra);
            }
            foreach (SandboxTelegraph telegraph in battle.Telegraphs.Where(item => item.Cells.Contains(cell) || item.Center == cell))
            {
                if (telegraph.Kind == SandboxTelegraphKind.BurrowExit && telegraph.Center != cell)
                    continue;
                parts.Add("⚠ " + telegraph.Note);
            }
            SandboxUnitState stuck = battle.Units.FirstOrDefault(unit => !unit.IsDefeated && unit.IsStuckInTunnel && unit.Position == cell);
            if (stuck != null)
                parts.Add(stuck.DisplayLabel + " зажат в тоннеле: бейте по этому маркеру (" + stuck.HitPoints + " HP)");
            return string.Join(" · ", parts);
        }

        private void PaintSegment(Painter2D painter, Vector2 a, Vector2 b, Color color, float width)
        {
            if (marksDrawnBelow)
            {
                marks.Add(new BoardMark { Kind = BoardMarkKind.Segment, A = a, B = b, Line = color, LineWidth = width });
                return;
            }
            painter.strokeColor = color;
            painter.lineWidth = width;
            painter.BeginPath();
            painter.MoveTo(a);
            painter.LineTo(b);
            painter.Stroke();
        }

        private static float StableNoise(HexCoord cell, int salt)
        {
            unchecked
            {
                int h = cell.Q * 73856093 ^ cell.R * 19349663 ^ salt * 83492791;
                h = (h ^ (h >> 13)) * 1274126177;
                return ((h & 0x7fffffff) % 1000) / 1000f;
            }
        }

        // Подложка: клетки состояний — под фигурами.
        private void DrawAbilityGround(Painter2D painter, HexLayout layout)
        {
            if (battle == null)
                return;
            float radius = layout.Size * (1f - hexStyle.Gap);
            foreach (KeyValuePair<HexCoord, SandboxCellState> pair in battle.CellStates)
            {
                if (!IsBoardCell(pair.Key))
                    continue;
                Vector2 center = layout.GetCenter(pair.Key);
                if ((pair.Value & SandboxCellState.ShallowWater) != 0 && !Painted(pair.Key, MapLabArenaSurface.ShallowWater))
                {
                    // Спокойная сине-серая заливка чуть шире гекса: соседние
                    // водные клетки сливаются без внутренних швов.
                    PaintHex(painter, center, layout.Size * 1.04f, new Color(0.33f, 0.47f, 0.55f, 0.58f), Color.clear, 0f, true, layout.VerticalScale);
                    // Редкие направленные следы течения, стабильные по координатам.
                    if (StableNoise(pair.Key, 1) < 0.45f)
                    {
                        float angle = -0.35f + StableNoise(pair.Key, 2) * 0.3f;
                        Vector2 dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle) * layout.VerticalScale) * layout.Size * 0.32f;
                        Vector2 shift = new Vector2(StableNoise(pair.Key, 3) - 0.5f, StableNoise(pair.Key, 4) - 0.5f) * layout.Size * 0.4f;
                        PaintSegment(painter, center + shift - dir, center + shift + dir, new Color(0.78f, 0.86f, 0.88f, 0.45f), 1.4f);
                    }
                }
                if ((pair.Value & SandboxCellState.LooseEarth) != 0 && !Painted(pair.Key, MapLabArenaSurface.LooseEarth))
                {
                    PaintHex(painter, center, radius * 0.92f, new Color(0.47f, 0.34f, 0.20f, 0.32f), Color.clear, 0f, true, layout.VerticalScale);
                    for (int i = 0; i < 3; i++)
                    {
                        Vector2 dot = center + new Vector2(StableNoise(pair.Key, 10 + i) - 0.5f, (StableNoise(pair.Key, 20 + i) - 0.5f) * layout.VerticalScale) * layout.Size;
                        PaintCircle(painter, dot, 2.2f, new Color(0.30f, 0.21f, 0.12f, 0.75f), Color.clear, 0f);
                    }
                }
                if ((pair.Value & SandboxCellState.Subsided) != 0 && !Painted(pair.Key, MapLabArenaSurface.Subsided))
                    PaintHex(painter, center, radius * 0.9f, new Color(0.34f, 0.26f, 0.17f, 0.36f), new Color(0.22f, 0.16f, 0.10f, 0.6f), 1.2f, true, layout.VerticalScale);
                if ((pair.Value & SandboxCellState.Pit) != 0 && !Painted(pair.Key, MapLabArenaSurface.Pit))
                {
                    PaintHex(painter, center, radius * 0.9f, new Color(0.05f, 0.04f, 0.035f, 0.86f), new Color(0.32f, 0.22f, 0.13f, 0.9f), 2f, true, layout.VerticalScale);
                    PaintCircle(painter, center, layout.Size * 0.28f, new Color(0f, 0f, 0f, 0.9f), Color.clear, 0f);
                }
            }

            // Объявленные угрозы: форма и значок, не только красный цвет.
            foreach (SandboxTelegraph telegraph in battle.Telegraphs)
            {
                switch (telegraph.Kind)
                {
                    case SandboxTelegraphKind.Toll:
                        foreach (HexCoord cell in telegraph.Cells.Where(IsBoardCell))
                            PaintHex(painter, layout.GetCenter(cell), radius * 0.86f, new Color(0.95f, 0.66f, 0.26f, 0.08f), new Color(0.95f, 0.66f, 0.26f, 0.55f), 1.4f, true, layout.VerticalScale);
                        break;
                    case SandboxTelegraphKind.Flood:
                        foreach (HexCoord cell in telegraph.Cells.Where(IsBoardCell))
                            PaintHex(painter, layout.GetCenter(cell), radius * 0.8f, new Color(0.33f, 0.47f, 0.55f, 0.18f), new Color(0.55f, 0.75f, 0.86f, 0.9f), 2f, true, layout.VerticalScale);
                        break;
                    case SandboxTelegraphKind.Subsidence:
                        foreach (HexCoord cell in telegraph.Cells.Where(IsBoardCell))
                        {
                            Vector2 c = layout.GetCenter(cell);
                            PaintHex(painter, c, radius * 0.84f, new Color(0.55f, 0.18f, 0.10f, 0.28f), new Color(0.85f, 0.42f, 0.20f, 0.95f), 2.2f, true, layout.VerticalScale);
                            float s = layout.Size * 0.3f;
                            PaintSegment(painter, c + new Vector2(-s, -s * 0.6f), c + new Vector2(s, s * 0.6f), new Color(0.20f, 0.10f, 0.05f, 0.9f), 2f);
                            PaintSegment(painter, c + new Vector2(-s, s * 0.6f), c + new Vector2(s, -s * 0.6f), new Color(0.20f, 0.10f, 0.05f, 0.9f), 2f);
                        }
                        break;
                    case SandboxTelegraphKind.BurrowExit:
                    {
                        Vector2 c = layout.GetCenter(telegraph.Center);
                        PaintCircle(painter, c, layout.Size * 0.48f, new Color(0.40f, 0.27f, 0.14f, 0.35f), new Color(0.95f, 0.56f, 0.22f, 1f), 3f);
                        for (int i = 1; i < telegraph.Cells.Count; i++)
                            PaintSegment(painter, layout.GetCenter(telegraph.Cells[i - 1]), layout.GetCenter(telegraph.Cells[i]), new Color(0.55f, 0.38f, 0.20f, 0.65f), 2f);
                        break;
                    }
                }
            }
        }

        // Поверх фигур: нити, лозы, яйца, статусы, режим выбора цели.
        private void DrawAbilityOverlay(Painter2D painter, HexLayout layout)
        {
            if (battle == null)
                return;
            float radius = layout.Size * (1f - hexStyle.Gap);
            foreach (SandboxBattleObject item in battle.Objects.Where(item => !item.IsDestroyed))
            {
                switch (item.Kind)
                {
                    case SandboxObjectKind.Thread:
                        foreach (HexCoord cell in item.Cells.Where(IsBoardCell))
                            PaintHex(painter, layout.GetCenter(cell), radius * 0.9f, new Color(0.92f, 0.90f, 0.78f, 0.10f), new Color(0.94f, 0.92f, 0.80f, 0.85f), 1.6f, true, layout.VerticalScale);
                        for (int i = 1; i < item.Cells.Count; i++)
                            PaintSegment(painter, layout.GetCenter(item.Cells[i - 1]), layout.GetCenter(item.Cells[i]), new Color(0.96f, 0.95f, 0.86f, 0.95f), 1.6f);
                        if (item.Cells.Count > 0)
                        {
                            PaintCircle(painter, layout.GetCenter(item.Cells[0]), 3.5f, new Color(0.45f, 0.33f, 0.20f, 1f), Color.clear, 0f);
                            PaintCircle(painter, layout.GetCenter(item.Cells[item.Cells.Count - 1]), 3.5f, new Color(0.45f, 0.33f, 0.20f, 1f), Color.clear, 0f);
                        }
                        break;
                    case SandboxObjectKind.Vine:
                    case SandboxObjectKind.Tendril:
                        if (item.Cells.Count >= 2)
                        {
                            Color color = item.Kind == SandboxObjectKind.Vine ? new Color(0.42f, 0.62f, 0.28f, 0.95f) : new Color(0.30f, 0.52f, 0.58f, 0.95f);
                            for (int i = 1; i < item.Cells.Count; i++)
                                PaintSegment(painter, layout.GetCenter(item.Cells[i - 1]), layout.GetCenter(item.Cells[i]), color, 4f);
                            Vector2 mid = layout.GetCenter(item.Cells[item.Cells.Count / 2]);
                            PaintCircle(painter, mid, 5f, color, new Color(0.08f, 0.10f, 0.06f, 1f), 1.5f);
                        }
                        break;
                    case SandboxObjectKind.Egg:
                        if (item.Cells.Count > 0 && IsBoardCell(item.Cells[0]))
                        {
                            Vector2 c = layout.GetCenter(item.Cells[0]);
                            PaintCircle(painter, c, layout.Size * 0.3f, new Color(0.86f, 0.80f, 0.64f, 1f), new Color(0.40f, 0.26f, 0.20f, 1f), 2f);
                            for (int i = 0; i < item.Counter; i++)
                                PaintCircle(painter, c + new Vector2((i - (item.Counter - 1) * 0.5f) * 7f, layout.Size * 0.42f), 2.4f, new Color(0.70f, 0.22f, 0.18f, 1f), Color.clear, 0f);
                        }
                        break;
                }
            }

            HashSet<string> tollMarked = new HashSet<string>(battle.Telegraphs.Where(item => item.Kind == SandboxTelegraphKind.Toll).SelectMany(item => item.TargetIds));
            foreach (SandboxUnitState unit in battle.Units)
            {
                if (unit.IsDefeated)
                    continue;
                Vector2 c = layout.GetCenter(unit.Position);
                if (unit.IsStuckInTunnel)
                {
                    PaintCircle(painter, c, layout.Size * 0.4f, new Color(0.28f, 0.18f, 0.10f, 0.9f), new Color(0.95f, 0.56f, 0.22f, 1f), 3f);
                    continue;
                }
                if (unit.IsOffBoard)
                    continue;
                float r = layout.Size * 0.62f;
                if (unit.IsStunned)
                    PaintCircle(painter, c, r + 8f, Color.clear, new Color(0.96f, 0.86f, 0.30f, 0.95f), 2f, false);
                if (unit.IsNumbed)
                    PaintCircle(painter, c, r + 11f, Color.clear, new Color(0.66f, 0.46f, 0.86f, 0.95f), 3f, false);
                if (battle.IsHeld(unit))
                    PaintCircle(painter, c, r + 4f, Color.clear, new Color(0.42f, 0.66f, 0.34f, 0.95f), 3f, false);
                if (tollMarked.Contains(unit.Id))
                    PaintCircle(painter, c + new Vector2(0f, -layout.Size * 0.95f), 6f, new Color(0.95f, 0.66f, 0.26f, 1f), new Color(0.2f, 0.12f, 0.04f, 1f), 1.5f);
                SandboxUnitState parasite = battle.GetAttachedParasite(unit);
                if (parasite != null)
                    PaintCircle(painter, c + new Vector2(layout.Size * 0.42f, -layout.Size * 0.42f), layout.Size * 0.18f,
                        new Color(0.62f, 0.12f, 0.12f, 1f), new Color(0.98f, 0.90f, 0.80f, 1f), 1.5f);
            }

            if (abilityTargeting == null)
                return;
            foreach (HexCoord cell in abilityTargeting.Cells.Where(IsBoardCell))
                PaintHex(painter, layout.GetCenter(cell), radius * 0.94f, new Color(0.95f, 0.80f, 0.40f, 0.12f), new Color(0.98f, 0.82f, 0.42f, 0.95f), 2.4f, true, layout.VerticalScale);
            if (abilityTargeting.FirstAnchor.HasValue)
                PaintCircle(painter, layout.GetCenter(abilityTargeting.FirstAnchor.Value), 7f, new Color(0.95f, 0.92f, 0.80f, 1f), new Color(0.3f, 0.2f, 0.1f, 1f), 2f);
            SandboxAbilityPreview preview = abilityHoverPreview;
            if (preview == null || !preview.IsValid)
                return;
            for (int i = 1; i < preview.Path.Count; i++)
                PaintSegment(painter, layout.GetCenter(preview.Path[i - 1]), layout.GetCenter(preview.Path[i]), new Color(0.98f, 0.86f, 0.52f, 0.95f), 3f);
            foreach (HexCoord cell in preview.DangerCells.Where(IsBoardCell))
                PaintHex(painter, layout.GetCenter(cell), radius * 0.8f, new Color(0.90f, 0.40f, 0.22f, 0.16f), new Color(0.92f, 0.46f, 0.24f, 0.9f), 1.6f, true, layout.VerticalScale);
            if (preview.LandingCell.HasValue)
                PaintCircle(painter, layout.GetCenter(preview.LandingCell.Value), layout.Size * 0.36f, Color.clear, new Color(0.98f, 0.86f, 0.52f, 1f), 3f, false);
            if (preview.ForcedCell.HasValue)
            {
                Vector2 to = layout.GetCenter(preview.ForcedCell.Value);
                PaintCircle(painter, to, layout.Size * 0.3f, new Color(0.60f, 0.80f, 0.95f, 0.25f), new Color(0.60f, 0.80f, 0.95f, 1f), 2.5f);
            }
        }
    }
}

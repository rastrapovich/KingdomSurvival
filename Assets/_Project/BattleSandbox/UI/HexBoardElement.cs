using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace KingdomSurvival.BattleSandbox
{
    public sealed class HexBoardElement : VisualElement
    {
        private const float AttackLungeDuration = 0.12f;
        private const float AttackReturnDuration = 0.10f;
        private const float DamageFloatDuration = 0.58f;

        private static readonly Color NormalColor = new Color(0.16f, 0.20f, 0.22f, 1f);
        private static readonly Color DifficultColor = new Color(0.29f, 0.25f, 0.17f, 1f);
        private static readonly Color ImpassableColor = new Color(0.07f, 0.08f, 0.09f, 1f);
        private static readonly Color ReachableColor = new Color(0.16f, 0.33f, 0.40f, 1f);

        private SandboxBattle battle;
        private string selectedTargetId;
        private readonly Label damageLabel;
        private IVisualElementScheduledItem attackAnimationItem;
        private string animationAttackerId;
        private string animationTargetId;
        private Vector2 attackerOffset;
        private Vector2 targetOffset;
        private float targetFlash;
        private float attackAnimationStartedAt;
        private bool impactApplied;
        private Action impactCallback;
        private Action completionCallback;
        private string hoverAttackTargetId;
        private HexCoord? hoverAttackPosition;
        private Vector2 hoverCursorPosition;
        private Vector2 hoverAttackDirection;
        private bool hoverRangedAttack;
        private bool attackCursorActive;
        private bool nativeCursorHidden;

        public event Action<HexCoord> HexClicked;
        public event Action<string, Vector2> UnitDetailsRequested;
        public event Action<string, HexCoord?> AttackRequested;
        public bool IsAnimating { get; private set; }

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
            RegisterCallback<PointerMoveEvent>(OnPointerMove);
            RegisterCallback<PointerLeaveEvent>(_ => ClearAttackCursor());
            RegisterCallback<DetachFromPanelEvent>(_ => ClearAttackCursor(false));
            RegisterCallback<GeometryChangedEvent>(_ => MarkDirtyRepaint());

            damageLabel = new Label();
            damageLabel.name = "sandbox-floating-damage";
            damageLabel.pickingMode = PickingMode.Ignore;
            damageLabel.style.display = DisplayStyle.None;
            damageLabel.style.position = Position.Absolute;
            damageLabel.style.width = 120f;
            damageLabel.style.height = 36f;
            damageLabel.style.fontSize = 23f;
            damageLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            damageLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            damageLabel.style.color = new Color(0.98f, 0.33f, 0.25f, 1f);
            Add(damageLabel);
        }

        public void SetBattle(SandboxBattle value, string targetId)
        {
            battle = value;
            selectedTargetId = targetId;
            ClearAttackCursor(false);
            MarkDirtyRepaint();
        }

        public bool PlayAttackAnimation(
            string attackerId,
            string targetId,
            int damage,
            Action onImpact,
            Action onComplete)
        {
            if (IsAnimating || battle == null)
                return false;

            ClearAttackCursor(false);

            SandboxUnitState attacker = battle.GetUnit(attackerId);
            SandboxUnitState target = battle.GetUnit(targetId);
            if (attacker == null || target == null || attacker.IsDefeated || target.IsDefeated)
                return false;

            IsAnimating = true;
            animationAttackerId = attackerId;
            animationTargetId = targetId;
            attackerOffset = Vector2.zero;
            targetOffset = Vector2.zero;
            targetFlash = 0f;
            impactApplied = false;
            impactCallback = onImpact;
            completionCallback = onComplete;
            attackAnimationStartedAt = Time.realtimeSinceStartup;

            damageLabel.text = "−" + Mathf.Max(0, damage);
            damageLabel.style.display = DisplayStyle.None;
            damageLabel.style.opacity = 1f;

            attackAnimationItem = schedule.Execute(UpdateAttackAnimation).Every(16);
            MarkDirtyRepaint();
            return true;
        }

        private void OnPointerDown(PointerDownEvent evt)
        {
            if (battle == null || IsAnimating || (evt.button != 0 && evt.button != 1))
                return;

            Vector2 pointerPosition = new Vector2(evt.localPosition.x, evt.localPosition.y);
            HexCoord best;
            if (TryGetHexAt(pointerPosition, out best))
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

                UpdateAttackCursor(pointerPosition);
                SandboxUnitState target = battle.GetUnitAt(best);
                if (attackCursorActive && target != null && target.Id == hoverAttackTargetId)
                {
                    string targetId = hoverAttackTargetId;
                    HexCoord? attackPosition = hoverAttackPosition;
                    ClearAttackCursor();
                    AttackRequested?.Invoke(targetId, attackPosition);
                    evt.StopPropagation();
                    return;
                }

                HexClicked?.Invoke(best);
                evt.StopPropagation();
            }
        }

        private void OnPointerMove(PointerMoveEvent evt)
        {
            Vector2 pointerPosition = new Vector2(evt.localPosition.x, evt.localPosition.y);
            UpdateAttackCursor(pointerPosition);
        }

        private void UpdateAttackCursor(Vector2 pointerPosition)
        {
            ClearAttackCursor(false);
            if (battle == null || IsAnimating)
                return;

            HexCoord coord;
            if (!TryGetHexAt(pointerPosition, out coord))
                return;

            SandboxUnitState attacker = battle.CurrentUnit;
            SandboxUnitState target = battle.GetUnitAt(coord);
            if (attacker == null || target == null ||
                attacker.Team != SandboxTeam.Player || target.Team == attacker.Team)
            {
                return;
            }

            HexLayout layout = CalculateLayout();
            HexCoord attackPosition;
            int movementCost;
            bool valid;
            if (attacker.AttackRange > 1)
            {
                valid = battle.TryFindAttackPosition(
                    attacker.Id,
                    target.Id,
                    out attackPosition,
                    out movementCost);
                hoverRangedAttack = true;
                hoverCursorPosition = pointerPosition;
                hoverAttackDirection = Vector2.right;
            }
            else
            {
                attackPosition = SelectMeleeAttackPosition(
                    attacker,
                    target,
                    pointerPosition,
                    layout);
                valid = battle.TryGetMeleeAttackPosition(
                    attacker.Id,
                    target.Id,
                    attackPosition,
                    out movementCost);
                hoverRangedAttack = false;
                Vector2 targetCenter = layout.GetCenter(target.Position);
                Vector2 attackCenter = layout.GetCenter(attackPosition);
                Vector2 fromTarget = attackCenter - targetCenter;
                fromTarget = fromTarget.sqrMagnitude > 0.001f
                    ? fromTarget.normalized
                    : Vector2.left;
                hoverCursorPosition = targetCenter + fromTarget * layout.Size * 0.82f;
                hoverAttackDirection = -fromTarget;
            }

            if (!valid)
            {
                tooltip = attacker.AttackRange > 1
                    ? "Цель находится вне доступной зоны выстрела."
                    : "С выбранной грани подойти и ударить нельзя.";
                MarkDirtyRepaint();
                return;
            }

            SandboxAttackPreview preview = battle.PreviewReachableAttack(attacker.Id, target.Id);
            if (!preview.IsValid)
                return;

            hoverAttackTargetId = target.Id;
            hoverAttackPosition = attackPosition;
            attackCursorActive = true;
            nativeCursorHidden = true;
            Cursor.visible = false;
            tooltip = (hoverRangedAttack ? "Выстрел" : "Удар мечом") +
                      (movementCost > 0 ? " · движение " + movementCost : string.Empty) +
                      " · " + preview.Damage + " урона · ЛКМ для атаки";
            MarkDirtyRepaint();
        }

        private static HexCoord SelectMeleeAttackPosition(
            SandboxUnitState attacker,
            SandboxUnitState target,
            Vector2 pointerPosition,
            HexLayout layout)
        {
            Vector2 targetCenter = layout.GetCenter(target.Position);
            Vector2 pointerDirection = pointerPosition - targetCenter;
            if (pointerDirection.sqrMagnitude <= 4f)
                pointerDirection = layout.GetCenter(attacker.Position) - targetCenter;
            pointerDirection = pointerDirection.sqrMagnitude > 0.001f
                ? pointerDirection.normalized
                : Vector2.left;

            HexCoord selected = target.Position;
            float bestDot = float.MinValue;
            foreach (HexCoord neighbor in target.Position.Neighbors())
            {
                Vector2 direction = layout.GetCenter(neighbor) - targetCenter;
                float dot = Vector2.Dot(pointerDirection, direction.normalized);
                if (dot > bestDot)
                {
                    bestDot = dot;
                    selected = neighbor;
                }
            }

            return selected;
        }

        private void ClearAttackCursor(bool repaint = true)
        {
            hoverAttackTargetId = null;
            hoverAttackPosition = null;
            hoverCursorPosition = Vector2.zero;
            hoverAttackDirection = Vector2.right;
            hoverRangedAttack = false;
            attackCursorActive = false;
            tooltip = string.Empty;
            if (nativeCursorHidden)
            {
                Cursor.visible = true;
                nativeCursorHidden = false;
            }

            if (repaint)
                MarkDirtyRepaint();
        }

        private bool TryGetHexAt(Vector2 pointerPosition, out HexCoord best)
        {
            best = default;
            if (battle == null)
                return false;

            HexLayout layout = CalculateLayout();
            float bestDistance = float.MaxValue;
            bool found = false;
            for (int r = 0; r < battle.Height; r++)
            {
                for (int q = 0; q < battle.Width; q++)
                {
                    HexCoord coord = new HexCoord(q, r);
                    float distance = (layout.GetCenter(coord) - pointerPosition).sqrMagnitude;
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        best = coord;
                        found = true;
                    }
                }
            }

            return found && bestDistance <= layout.Size * layout.Size;
        }

        private void DrawBoard(MeshGenerationContext context)
        {
            if (battle == null || contentRect.width <= 1f || contentRect.height <= 1f)
                return;

            Painter2D painter = context.painter2D;
            HexLayout layout = CalculateLayout();
            SandboxUnitState current = battle.CurrentUnit;
            IReadOnlyDictionary<HexCoord, int> reachable =
                !IsAnimating && current != null && current.Team == SandboxTeam.Player
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
                bool animatedDefeatedTarget = IsAnimating && unit.Id == animationTargetId;
                if (unit.IsDefeated && !animatedDefeatedTarget)
                    continue;
                DrawUnit(painter, layout, unit, current);
            }

            if (attackCursorActive && !IsAnimating)
            {
                if (hoverAttackPosition.HasValue && current != null &&
                    hoverAttackPosition.Value != current.Position)
                {
                    DrawHex(
                        painter,
                        layout.GetCenter(hoverAttackPosition.Value),
                        layout.Size - 4f,
                        new Color(0.82f, 0.64f, 0.18f, 0.16f),
                        new Color(0.98f, 0.80f, 0.32f, 0.95f),
                        3f);
                }

                DrawAttackCursor(
                    painter,
                    hoverCursorPosition,
                    hoverRangedAttack,
                    hoverAttackDirection);
            }
        }

        private void DrawUnit(
            Painter2D painter,
            HexLayout layout,
            SandboxUnitState unit,
            SandboxUnitState current)
        {
            Vector2 center = layout.GetCenter(unit.Position);
            if (IsAnimating && unit.Id == animationAttackerId)
                center += attackerOffset;
            else if (IsAnimating && unit.Id == animationTargetId)
                center += targetOffset;

            float radius = layout.Size * 0.55f;
            Color fill = unit.Team == SandboxTeam.Player
                ? new Color(0.78f, 0.62f, 0.27f, 1f)
                : new Color(0.70f, 0.25f, 0.22f, 1f);
            if (IsAnimating && unit.Id == animationTargetId && targetFlash > 0f)
                fill = Color.Lerp(fill, Color.white, targetFlash * 0.72f);

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

        private static void DrawAttackCursor(
            Painter2D painter,
            Vector2 center,
            bool ranged,
            Vector2 direction)
        {
            const float radius = 13f;
            DrawCircle(
                painter,
                center,
                radius,
                new Color(0.10f, 0.09f, 0.06f, 0.92f),
                new Color(0.98f, 0.86f, 0.61f, 1f),
                2f);

            painter.strokeColor = new Color(0.98f, 0.93f, 0.82f, 1f);
            painter.lineWidth = 2.6f;
            painter.BeginPath();
            float half = radius * 0.58f;

            if (ranged)
            {
                painter.MoveTo(center + new Vector2(-half, 0f));
                painter.LineTo(center + new Vector2(half, 0f));
                painter.MoveTo(center + new Vector2(half * 0.25f, -half * 0.55f));
                painter.LineTo(center + new Vector2(half, 0f));
                painter.LineTo(center + new Vector2(half * 0.25f, half * 0.55f));
            }
            else
            {
                Vector2 blade = direction.sqrMagnitude > 0.001f
                    ? direction.normalized
                    : Vector2.right;
                Vector2 cross = new Vector2(-blade.y, blade.x);
                Vector2 pommel = center - blade * half * 0.68f;
                Vector2 tip = center + blade * half * 0.90f;
                Vector2 guard = center - blade * half * 0.28f;
                painter.MoveTo(pommel);
                painter.LineTo(tip);
                painter.MoveTo(guard - cross * half * 0.42f);
                painter.LineTo(guard + cross * half * 0.42f);
            }

            painter.Stroke();
        }

        private void UpdateAttackAnimation()
        {
            if (!IsAnimating || battle == null)
            {
                FinishAttackAnimation();
                return;
            }

            SandboxUnitState attacker = battle.GetUnit(animationAttackerId);
            SandboxUnitState target = battle.GetUnit(animationTargetId);
            if (attacker == null || target == null)
            {
                FinishAttackAnimation();
                return;
            }

            float elapsed = Mathf.Max(0f, Time.realtimeSinceStartup - attackAnimationStartedAt);
            HexLayout layout = CalculateLayout();
            Vector2 direction = layout.GetCenter(target.Position) - layout.GetCenter(attacker.Position);
            direction = direction.sqrMagnitude > 0.001f ? direction.normalized : Vector2.right;
            float lungeDistance = layout.Size * 0.34f;

            if (elapsed < AttackLungeDuration)
            {
                float progress = Mathf.Clamp01(elapsed / AttackLungeDuration);
                float eased = 1f - Mathf.Pow(1f - progress, 3f);
                attackerOffset = direction * lungeDistance * eased;
            }
            else
            {
                if (!impactApplied)
                {
                    impactApplied = true;
                    damageLabel.style.display = DisplayStyle.Flex;
                    impactCallback?.Invoke();
                }

                float sinceImpact = elapsed - AttackLungeDuration;
                float returnProgress = Mathf.Clamp01(sinceImpact / AttackReturnDuration);
                attackerOffset = direction * lungeDistance * (1f - Mathf.SmoothStep(0f, 1f, returnProgress));

                float reactionProgress = Mathf.Clamp01(sinceImpact / 0.24f);
                float reactionStrength = 1f - reactionProgress;
                Vector2 perpendicular = new Vector2(-direction.y, direction.x);
                targetOffset = perpendicular * Mathf.Sin(sinceImpact * 72f) * 5f * reactionStrength;
                targetFlash = reactionStrength;

                float floatProgress = Mathf.Clamp01(sinceImpact / DamageFloatDuration);
                Vector2 targetCenter = layout.GetCenter(target.Position) + targetOffset;
                damageLabel.style.left = targetCenter.x - 60f;
                damageLabel.style.top = targetCenter.y - layout.Size * 0.88f - floatProgress * 38f;
                damageLabel.style.opacity = 1f - floatProgress;

                if (sinceImpact >= DamageFloatDuration)
                {
                    FinishAttackAnimation();
                    return;
                }
            }

            MarkDirtyRepaint();
        }

        private void FinishAttackAnimation()
        {
            if (!IsAnimating)
                return;

            attackAnimationItem?.Pause();
            attackAnimationItem = null;
            IsAnimating = false;
            animationAttackerId = null;
            animationTargetId = null;
            attackerOffset = Vector2.zero;
            targetOffset = Vector2.zero;
            targetFlash = 0f;
            damageLabel.style.display = DisplayStyle.None;
            damageLabel.style.opacity = 1f;
            impactCallback = null;

            Action callback = completionCallback;
            completionCallback = null;
            MarkDirtyRepaint();
            callback?.Invoke();
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

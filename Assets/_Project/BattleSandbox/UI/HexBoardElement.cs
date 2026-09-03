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
        private const float MovementSegmentDuration = 0.14f;
        private const float HealthBarWidthScale = 0.875f;
        private const float HealthBarHeight = 4.2f;
        private const float HealthBarBottomInset = 8f;
        private const float MeleeAutoSelectRadiusScale = 0.48f;
        private const float GridVerticalScale = 0.75f;

        private static readonly Color NormalColor = new Color(0.16f, 0.20f, 0.22f, 0.80f);
        private static readonly Color DifficultColor = new Color(0.29f, 0.25f, 0.17f, 0.80f);
        private static readonly Color ImpassableColor = new Color(0.07f, 0.08f, 0.09f, 0.80f);
        private static readonly Color ReachableColor = new Color(0.28f, 0.75f, 0.90f, 0.40f);

        private SandboxBattle battle;
        private string selectedTargetId;
        private IReadOnlyDictionary<string, SandboxUnitVisual> unitVisuals =
            new Dictionary<string, SandboxUnitVisual>();
        private readonly Dictionary<string, Image> unitImages =
            new Dictionary<string, Image>();
        private readonly Dictionary<string, VisualElement> unitHealthBars =
            new Dictionary<string, VisualElement>();
        private readonly Dictionary<string, VisualElement> unitHealthFills =
            new Dictionary<string, VisualElement>();
        private readonly VisualElement attackCursorOverlay;
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

        private IVisualElementScheduledItem movementAnimationItem;
        private string movementUnitId;
        private IReadOnlyList<HexCoord> movementPath;
        private int movementSegmentIndex;
        private float movementSegmentStartedAt;
        private Vector2 movementVisualPosition;
        private Action movementCompletionCallback;

        private string hoverAttackTargetId;
        private HexCoord? hoverAttackPosition;
        private Vector2 hoverCursorPosition;
        private Vector2 hoverAttackDirection;
        private bool hoverRangedAttack;
        private bool attackCursorActive;
        private bool nativeCursorHidden;
        private readonly List<HexCoord> hoverMovePath = new List<HexCoord>();

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

            attackCursorOverlay = new VisualElement
            {
                name = "sandbox-attack-cursor-overlay",
                pickingMode = PickingMode.Ignore
            };
            attackCursorOverlay.style.position = Position.Absolute;
            attackCursorOverlay.style.left = 0f;
            attackCursorOverlay.style.top = 0f;
            attackCursorOverlay.style.right = 0f;
            attackCursorOverlay.style.bottom = 0f;
            attackCursorOverlay.generateVisualContent += DrawAttackCursorOverlay;
            Add(attackCursorOverlay);

            generateVisualContent += DrawBoard;
            RegisterCallback<PointerDownEvent>(OnPointerDown);
            RegisterCallback<PointerMoveEvent>(OnPointerMove);
            RegisterCallback<PointerLeaveEvent>(_ => ClearPointerPreview());
            RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                ClearPointerPreview(false);
                ClearUnitImages();
            });
            RegisterCallback<GeometryChangedEvent>(_ =>
            {
                SyncUnitImages();
                MarkDirtyRepaint();
                attackCursorOverlay.MarkDirtyRepaint();
            });

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

        internal void SetUnitVisuals(
            IReadOnlyDictionary<string, SandboxUnitVisual> visuals)
        {
            unitVisuals = visuals ?? new Dictionary<string, SandboxUnitVisual>();
            SyncUnitImages();
            MarkDirtyRepaint();
        }

        public void SetBattle(SandboxBattle value, string targetId)
        {
            battle = value;
            selectedTargetId = targetId;
            ClearPointerPreview(false);
            SyncUnitImages();
            MarkDirtyRepaint();
        }

        public bool PlayMoveAnimation(
            string unitId,
            IReadOnlyList<HexCoord> path,
            Action onComplete)
        {
            if (IsAnimating || battle == null || path == null || path.Count < 2)
                return false;

            SandboxUnitState unit = battle.GetUnit(unitId);
            if (unit == null || unit.IsDefeated || path[0] != unit.Position)
                return false;

            ClearPointerPreview(false);
            IsAnimating = true;
            movementUnitId = unitId;
            movementPath = new List<HexCoord>(path);
            movementSegmentIndex = 0;
            movementSegmentStartedAt = Time.realtimeSinceStartup;
            movementVisualPosition = CalculateLayout().GetCenter(path[0]);
            movementCompletionCallback = onComplete;
            movementAnimationItem = schedule.Execute(UpdateMovementAnimation).Every(16);
            SyncUnitImages();
            MarkDirtyRepaint();
            return true;
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

            ClearPointerPreview(false);

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
            SyncUnitImages();
            MarkDirtyRepaint();
            return true;
        }

        private void OnPointerDown(PointerDownEvent evt)
        {
            if (battle == null || IsAnimating || (evt.button != 0 && evt.button != 1))
                return;

            Vector2 pointerPosition = new Vector2(evt.localPosition.x, evt.localPosition.y);
            HexCoord best;
            if (!TryGetHexAt(pointerPosition, out best))
                return;

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

            UpdatePointerPreview(pointerPosition);
            SandboxUnitState target = battle.GetUnitAt(best);
            if (attackCursorActive && target != null && target.Id == hoverAttackTargetId)
            {
                string targetId = hoverAttackTargetId;
                HexCoord? attackPosition = hoverAttackPosition;
                ClearPointerPreview();
                AttackRequested?.Invoke(targetId, attackPosition);
                evt.StopPropagation();
                return;
            }

            HexClicked?.Invoke(best);
            evt.StopPropagation();
        }

        private void OnPointerMove(PointerMoveEvent evt)
        {
            Vector2 pointerPosition = new Vector2(evt.localPosition.x, evt.localPosition.y);
            UpdatePointerPreview(pointerPosition);
        }

        private void UpdatePointerPreview(Vector2 pointerPosition)
        {
            ClearPointerPreview(false);
            if (battle == null || IsAnimating)
                return;

            HexCoord coord;
            if (!TryGetHexAt(pointerPosition, out coord))
                return;

            SandboxUnitState attacker = battle.CurrentUnit;
            if (attacker == null || attacker.Team != SandboxTeam.Player)
                return;

            SandboxUnitState target = battle.GetUnitAt(coord);
            if (target != null && target.Team != attacker.Team)
            {
                UpdateAttackCursor(pointerPosition, attacker, target);
                return;
            }

            if (target != null)
                return;

            IReadOnlyList<HexCoord> path;
            int movementCost;
            if (SandboxMovementPath.TryBuild(
                    battle,
                    attacker.Id,
                    coord,
                    out path,
                    out movementCost) &&
                path.Count > 1)
            {
                hoverMovePath.AddRange(path);
                tooltip = "Маршрут · " + movementCost + " движения · ЛКМ для перемещения";
                MarkDirtyRepaint();
            }
        }

        private void UpdateAttackCursor(
            Vector2 pointerPosition,
            SandboxUnitState attacker,
            SandboxUnitState target)
        {
            HexLayout layout = CalculateLayout();
            HexCoord attackPosition;
            int movementCost;
            bool valid;
            bool autoSelectMeleeSide = false;
            if (attacker.AttackRange > 1)
            {
                valid = battle.TryFindAttackPosition(
                    attacker.Id,
                    target.Id,
                    out attackPosition,
                    out movementCost);
                hoverRangedAttack = true;
                hoverCursorPosition = layout.GetCenter(target.Position);
                hoverAttackDirection = Vector2.right;
            }
            else
            {
                hoverRangedAttack = false;
                Vector2 targetCenter = layout.GetCenter(target.Position);
                float autoSelectRadius = layout.Size * MeleeAutoSelectRadiusScale;
                autoSelectMeleeSide =
                    layout.GetSquaredGridDistance(pointerPosition, targetCenter) <=
                    autoSelectRadius * autoSelectRadius;

                if (autoSelectMeleeSide)
                {
                    valid = battle.TryFindAttackPosition(
                        attacker.Id,
                        target.Id,
                        out attackPosition,
                        out movementCost);
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
                }

                if (valid)
                {
                    Vector2 attackCenter = layout.GetCenter(attackPosition);
                    Vector2 fromTarget = attackCenter - targetCenter;
                    fromTarget = fromTarget.sqrMagnitude > 0.001f
                        ? fromTarget.normalized
                        : Vector2.left;
                    hoverCursorPosition = targetCenter + fromTarget * layout.Size * 0.82f;
                    hoverAttackDirection = -fromTarget;
                }
            }

            if (!valid)
            {
                tooltip = attacker.AttackRange > 1
                    ? "Цель находится вне доступной зоны выстрела."
                    : autoSelectMeleeSide
                        ? "К цели нельзя подойти для удара в пределах оставшегося движения."
                        : "С выбранной грани подойти и ударить нельзя.";
                MarkDirtyRepaint();
                return;
            }

            SandboxAttackPreview preview = battle.PreviewReachableAttack(attacker.Id, target.Id);
            if (!preview.IsValid)
                return;

            if (movementCost > 0 && attackPosition != attacker.Position)
            {
                IReadOnlyList<HexCoord> path;
                int pathCost;
                if (SandboxMovementPath.TryBuild(
                        battle,
                        attacker.Id,
                        attackPosition,
                        out path,
                        out pathCost))
                {
                    hoverMovePath.AddRange(path);
                }
            }

            hoverAttackTargetId = target.Id;
            hoverAttackPosition = attackPosition;
            attackCursorActive = true;
            nativeCursorHidden = true;
            UnityEngine.Cursor.visible = false;
            tooltip = (hoverRangedAttack ? "Выстрел" : "Удар мечом") +
                      (movementCost > 0 ? " · движение " + movementCost : string.Empty) +
                      " · " + preview.Damage + " урона · ЛКМ для атаки";
            MarkDirtyRepaint();
            attackCursorOverlay.MarkDirtyRepaint();
        }

        private static HexCoord SelectMeleeAttackPosition(
            SandboxUnitState attacker,
            SandboxUnitState target,
            Vector2 pointerPosition,
            HexLayout layout)
        {
            Vector2 targetCenter = layout.GetCenter(target.Position);
            Vector2 pointerDirection = layout.ToGridSpaceVector(pointerPosition - targetCenter);
            if (pointerDirection.sqrMagnitude <= 4f)
            {
                pointerDirection = layout.ToGridSpaceVector(
                    layout.GetCenter(attacker.Position) - targetCenter);
            }
            pointerDirection = pointerDirection.sqrMagnitude > 0.001f
                ? pointerDirection.normalized
                : Vector2.left;

            HexCoord selected = target.Position;
            float bestDot = float.MinValue;
            foreach (HexCoord neighbor in target.Position.Neighbors())
            {
                Vector2 direction = layout.ToGridSpaceVector(
                    layout.GetCenter(neighbor) - targetCenter);
                float dot = Vector2.Dot(pointerDirection, direction.normalized);
                if (dot > bestDot)
                {
                    bestDot = dot;
                    selected = neighbor;
                }
            }

            return selected;
        }

        private void ClearPointerPreview(bool repaint = true)
        {
            hoverMovePath.Clear();
            ClearAttackCursor(false);
            if (repaint)
                MarkDirtyRepaint();
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
                UnityEngine.Cursor.visible = true;
                nativeCursorHidden = false;
            }

            attackCursorOverlay.MarkDirtyRepaint();
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
                    if (!IsBoardCell(coord))
                        continue;

                    float distance = layout.GetSquaredGridDistance(
                        layout.GetCenter(coord),
                        pointerPosition);
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
                    if (!IsBoardCell(coord))
                        continue;

                    SandboxTerrain terrain = battle.GetTerrain(coord);
                    SandboxUnitState occupant = battle.GetUnitAt(coord);
                    Color fill = GetTerrainColor(terrain);

                    if (occupant != null && occupant.Id == selectedTargetId)
                        fill = new Color(0.58f, 0.20f, 0.17f, 0.80f);

                    DrawHex(
                        painter,
                        layout.GetCenter(coord),
                        layout.Size - 1.5f,
                        fill,
                        new Color(0.36f, 0.38f, 0.38f, 0.80f),
                        1.2f,
                        true,
                        layout.VerticalScale);
                }
            }

            if (!IsAnimating && reachable.Count > 0)
            {
                foreach (KeyValuePair<HexCoord, int> pair in reachable)
                {
                    if (current != null && pair.Key == current.Position)
                        continue;
                    if (!IsBoardCell(pair.Key))
                        continue;

                    DrawHex(
                        painter,
                        layout.GetCenter(pair.Key),
                        layout.Size - 2.6f,
                        new Color(0f, 0f, 0f, 0f),
                        ReachableColor,
                        2.2f,
                        false,
                        layout.VerticalScale);
                }
            }

            if (hoverMovePath.Count > 1 && !IsAnimating)
                DrawMovePath(painter, layout, hoverMovePath);

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
                        3f,
                        true,
                        layout.VerticalScale);
                }
            }
        }

        private void DrawAttackCursorOverlay(MeshGenerationContext context)
        {
            if (!attackCursorActive || IsAnimating || battle == null)
                return;

            DrawAttackCursor(
                context.painter2D,
                hoverCursorPosition,
                hoverRangedAttack,
                hoverAttackDirection);
        }

        private static void DrawMovePath(
            Painter2D painter,
            HexLayout layout,
            IReadOnlyList<HexCoord> path)
        {
            if (path == null || path.Count < 2)
                return;

            Color routeColor = new Color(0.62f, 0.86f, 0.92f, 0.86f);
            painter.strokeColor = routeColor;
            painter.lineWidth = 3f;
            painter.BeginPath();
            painter.MoveTo(layout.GetCenter(path[0]));
            for (int i = 1; i < path.Count; i++)
                painter.LineTo(layout.GetCenter(path[i]));
            painter.Stroke();

            for (int i = 1; i < path.Count; i++)
            {
                DrawCircle(
                    painter,
                    layout.GetCenter(path[i]),
                    4.5f,
                    routeColor,
                    new Color(0.90f, 0.97f, 0.98f, 0.95f),
                    1.2f);
            }
        }

        private void DrawUnit(
            Painter2D painter,
            HexLayout layout,
            SandboxUnitState unit,
            SandboxUnitState current)
        {
            Vector2 center = layout.GetCenter(unit.Position);
            if (IsAnimating && unit.Id == movementUnitId && movementPath != null)
            {
                center = movementVisualPosition;
            }
            else if (IsAnimating && unit.Id == animationAttackerId)
            {
                center += attackerOffset;
            }
            else if (IsAnimating && unit.Id == animationTargetId)
            {
                center += targetOffset;
            }

            float radius = layout.Size * 0.55f;
            bool hasBattlefieldSprite = HasBattlefieldSprite(unit.TypeId);
            if (!hasBattlefieldSprite)
            {
                Color fill = unit.Team == SandboxTeam.Player
                    ? new Color(0.78f, 0.62f, 0.27f, 1f)
                    : new Color(0.70f, 0.25f, 0.22f, 1f);
                if (IsAnimating && unit.Id == animationTargetId && targetFlash > 0f)
                    fill = Color.Lerp(fill, Color.white, targetFlash * 0.72f);

                Color outline = current != null && current.Id == unit.Id
                    ? new Color(1f, 1f, 1f, 0.5f)
                    : new Color(0.05f, 0.05f, 0.05f, 1f);
                DrawCircle(
                    painter,
                    center,
                    radius,
                    fill,
                    outline,
                    current != null && current.Id == unit.Id ? 4f : 2f);
            }
            else if (current != null && current.Id == unit.Id)
            {
                DrawCircle(
                    painter,
                    center,
                    radius,
                    new Color(0f, 0f, 0f, 0f),
                    new Color(1f, 1f, 1f, 0.5f),
                    4f,
                    false);
            }

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

            if (!hasBattlefieldSprite)
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

        private void UpdateMovementAnimation()
        {
            if (!IsAnimating || battle == null || movementPath == null || movementPath.Count < 2)
            {
                FinishMovementAnimation();
                return;
            }

            if (movementSegmentIndex >= movementPath.Count - 1)
            {
                FinishMovementAnimation();
                return;
            }

            float duration = MovementSegmentDuration;
            if (battle.GetTerrain(movementPath[movementSegmentIndex + 1]) == SandboxTerrain.Difficult)
                duration *= 1.35f;

            float elapsed = Mathf.Max(0f, Time.realtimeSinceStartup - movementSegmentStartedAt);
            float progress = Mathf.Clamp01(elapsed / duration);
            float eased = Mathf.SmoothStep(0f, 1f, progress);
            HexLayout layout = CalculateLayout();
            Vector2 start = layout.GetCenter(movementPath[movementSegmentIndex]);
            Vector2 end = layout.GetCenter(movementPath[movementSegmentIndex + 1]);
            movementVisualPosition = Vector2.Lerp(start, end, eased);

            if (progress >= 1f)
            {
                movementSegmentIndex++;
                movementSegmentStartedAt = Time.realtimeSinceStartup;
                movementVisualPosition = end;
                if (movementSegmentIndex >= movementPath.Count - 1)
                {
                    FinishMovementAnimation();
                    return;
                }
            }

            SyncUnitImages();
            MarkDirtyRepaint();
        }

        private void FinishMovementAnimation()
        {
            if (movementAnimationItem == null && movementPath == null)
                return;

            movementAnimationItem?.Pause();
            movementAnimationItem = null;
            IsAnimating = false;
            movementUnitId = null;
            movementPath = null;
            movementSegmentIndex = 0;
            movementVisualPosition = Vector2.zero;

            Action callback = movementCompletionCallback;
            movementCompletionCallback = null;
            SyncUnitImages();
            MarkDirtyRepaint();
            callback?.Invoke();
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

            SyncUnitImages();
            MarkDirtyRepaint();
        }

        private void FinishAttackAnimation()
        {
            if (attackAnimationItem == null && string.IsNullOrEmpty(animationAttackerId))
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
            SyncUnitImages();
            MarkDirtyRepaint();
            callback?.Invoke();
        }

        private bool HasBattlefieldSprite(string typeId)
        {
            SandboxUnitVisual visual;
            return !string.IsNullOrWhiteSpace(typeId) &&
                   unitVisuals.TryGetValue(typeId, out visual) &&
                   visual != null &&
                   visual.BattlefieldSprite != null;
        }

        private void SyncUnitImages()
        {
            if (battle == null || contentRect.width <= 1f || contentRect.height <= 1f)
            {
                ClearUnitImages();
                return;
            }

            HexLayout layout = CalculateLayout();
            HashSet<string> visibleUnitIds = new HashSet<string>();
            HashSet<string> visibleImageIds = new HashSet<string>();
            foreach (SandboxUnitState unit in battle.Units)
            {
                bool animatedDefeatedTarget = IsAnimating && unit.Id == animationTargetId;
                if (unit.IsDefeated && !animatedDefeatedTarget)
                    continue;

                visibleUnitIds.Add(unit.Id);
                Vector2 unitCenter = GetUnitVisualCenter(layout, unit);
                Vector2 healthCenter = unitCenter;
                float healthTop = unitCenter.y + layout.Size * 0.34f;

                SandboxUnitVisual visual;
                bool hasBattlefieldSprite =
                    unitVisuals.TryGetValue(unit.TypeId, out visual) &&
                    visual != null &&
                    visual.BattlefieldSprite != null;
                if (hasBattlefieldSprite)
                {
                    Image image;
                    if (!unitImages.TryGetValue(unit.Id, out image))
                    {
                        image = new Image
                        {
                            pickingMode = PickingMode.Ignore,
                            scaleMode = ScaleMode.ScaleToFit
                        };
                        image.style.position = Position.Absolute;
                        unitImages.Add(unit.Id, image);
                        Add(image);
                    }

                    visibleImageIds.Add(unit.Id);
                    image.sprite = visual.BattlefieldSprite;
                    float size = layout.Size * 1.35f * visual.BattlefieldScale;
                    Vector2 center = unitCenter + visual.BattlefieldOffset;
                    image.style.width = size;
                    image.style.height = size;
                    image.style.left = center.x - size * 0.5f;
                    image.style.top = center.y - size * 0.5f;
                    image.tintColor = IsAnimating && unit.Id == animationTargetId && targetFlash > 0f
                        ? Color.Lerp(Color.white, new Color(1f, 0.58f, 0.52f, 1f), targetFlash)
                        : Color.white;
                    image.style.display = DisplayStyle.Flex;

                    healthCenter = center;
                    healthTop = center.y + size * 0.5f - HealthBarBottomInset - HealthBarHeight;
                }

                SyncHealthBar(unit, healthCenter, healthTop, layout.Size * HealthBarWidthScale);
            }

            List<string> removedImageIds = null;
            foreach (KeyValuePair<string, Image> pair in unitImages)
            {
                if (visibleImageIds.Contains(pair.Key))
                    continue;
                if (removedImageIds == null)
                    removedImageIds = new List<string>();
                removedImageIds.Add(pair.Key);
                pair.Value.RemoveFromHierarchy();
            }

            if (removedImageIds != null)
            {
                for (int i = 0; i < removedImageIds.Count; i++)
                    unitImages.Remove(removedImageIds[i]);
            }

            List<string> removedHealthIds = null;
            foreach (KeyValuePair<string, VisualElement> pair in unitHealthBars)
            {
                if (visibleUnitIds.Contains(pair.Key))
                    continue;
                if (removedHealthIds == null)
                    removedHealthIds = new List<string>();
                removedHealthIds.Add(pair.Key);
                pair.Value.RemoveFromHierarchy();
            }

            if (removedHealthIds != null)
            {
                for (int i = 0; i < removedHealthIds.Count; i++)
                {
                    unitHealthBars.Remove(removedHealthIds[i]);
                    unitHealthFills.Remove(removedHealthIds[i]);
                }
            }

            attackCursorOverlay.BringToFront();
            damageLabel.BringToFront();
        }

        private void SyncHealthBar(
            SandboxUnitState unit,
            Vector2 center,
            float top,
            float width)
        {
            VisualElement healthBar;
            VisualElement healthFill;
            if (!unitHealthBars.TryGetValue(unit.Id, out healthBar))
            {
                healthBar = new VisualElement
                {
                    pickingMode = PickingMode.Ignore
                };
                healthBar.style.position = Position.Absolute;
                healthBar.style.backgroundColor = new Color(0.04f, 0.04f, 0.04f, 0.92f);
                healthBar.style.overflow = Overflow.Hidden;
                healthBar.style.borderTopLeftRadius = 2f;
                healthBar.style.borderTopRightRadius = 2f;
                healthBar.style.borderBottomLeftRadius = 2f;
                healthBar.style.borderBottomRightRadius = 2f;

                healthFill = new VisualElement
                {
                    pickingMode = PickingMode.Ignore
                };
                healthFill.style.position = Position.Absolute;
                healthFill.style.left = 0f;
                healthFill.style.top = 0f;
                healthFill.style.bottom = 0f;
                healthBar.Add(healthFill);

                unitHealthBars.Add(unit.Id, healthBar);
                unitHealthFills.Add(unit.Id, healthFill);
                Add(healthBar);
            }
            else
            {
                healthFill = unitHealthFills[unit.Id];
            }

            float ratio = Mathf.Clamp01((float)unit.HitPoints / Mathf.Max(1, unit.MaxHitPoints));
            healthBar.style.width = width;
            healthBar.style.height = HealthBarHeight;
            healthBar.style.left = center.x - width * 0.5f;
            healthBar.style.top = top;
            healthBar.style.display = DisplayStyle.Flex;
            healthFill.style.width = Length.Percent(ratio * 100f);
            healthFill.style.backgroundColor = unit.HitPoints > unit.MaxHitPoints * 0.35f
                ? new Color(0.32f, 0.72f, 0.38f, 1f)
                : new Color(0.84f, 0.31f, 0.26f, 1f);
            healthBar.BringToFront();
        }

        private Vector2 GetUnitVisualCenter(HexLayout layout, SandboxUnitState unit)
        {
            Vector2 center = layout.GetCenter(unit.Position);
            if (IsAnimating && unit.Id == movementUnitId && movementPath != null)
                return movementVisualPosition;
            if (IsAnimating && unit.Id == animationAttackerId)
                return center + attackerOffset;
            if (IsAnimating && unit.Id == animationTargetId)
                return center + targetOffset;
            return center;
        }

        private void ClearUnitImages()
        {
            foreach (Image image in unitImages.Values)
                image.RemoveFromHierarchy();
            unitImages.Clear();

            foreach (VisualElement healthBar in unitHealthBars.Values)
                healthBar.RemoveFromHierarchy();
            unitHealthBars.Clear();
            unitHealthFills.Clear();
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
            float lineWidth,
            bool fillShape = true,
            float verticalScale = 1f)
        {
            painter.fillColor = fill;
            painter.strokeColor = stroke;
            painter.lineWidth = lineWidth;
            painter.BeginPath();

            for (int i = 0; i < 6; i++)
            {
                float angle = Mathf.Deg2Rad * (60f * i - 30f);
                Vector2 point = center + new Vector2(
                    Mathf.Cos(angle) * radius,
                    Mathf.Sin(angle) * radius * verticalScale);
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

        private bool IsBoardCell(HexCoord coord)
        {
            if (battle == null || !battle.IsInside(coord))
                return false;

            if (!SandboxArenaShape.MatchesDimensions(battle.Width, battle.Height))
                return true;

            return SandboxArenaShape.Contains(coord);
        }

        private HexLayout CalculateLayout()
        {
            float availableWidth = Mathf.Max(100f, contentRect.width - 36f);
            float availableHeight = Mathf.Max(100f, contentRect.height - 36f);
            bool compactArena = SandboxArenaShape.MatchesDimensions(battle.Width, battle.Height);
            float stagger = compactArena ? 0f : (battle.Height > 1 ? 0.5f : 0f);
            float widthUnits = Mathf.Sqrt(3f) * (battle.Width + stagger);
            float unscaledHeightUnits = 1.5f * (battle.Height - 1) + 2f;
            float heightUnits = unscaledHeightUnits * GridVerticalScale;
            float size = Mathf.Min(availableWidth / widthUnits, availableHeight / heightUnits);
            size = Mathf.Max(18f, size);

            float boardWidth = widthUnits * size;
            float boardHeight = heightUnits * size;
            Vector2 origin = new Vector2(
                (contentRect.width - boardWidth) * 0.5f + Mathf.Sqrt(3f) * size * 0.5f,
                (contentRect.height - boardHeight) * 0.5f + size * GridVerticalScale);
            return new HexLayout(size, origin, GridVerticalScale);
        }

        private readonly struct HexLayout
        {
            public float Size { get; }
            public float VerticalScale { get; }
            private Vector2 Origin { get; }

            public HexLayout(float size, Vector2 origin, float verticalScale)
            {
                Size = size;
                Origin = origin;
                VerticalScale = Mathf.Max(0.01f, verticalScale);
            }

            public Vector2 GetCenter(HexCoord coord)
            {
                float rowOffset = (coord.R & 1) == 0 ? 0f : 0.5f;
                float x = Size * Mathf.Sqrt(3f) * (coord.Q + rowOffset);
                float y = Size * 1.5f * coord.R * VerticalScale;
                return Origin + new Vector2(x, y);
            }

            public Vector2 ToGridSpaceVector(Vector2 vector)
            {
                return new Vector2(vector.x, vector.y / VerticalScale);
            }

            public float GetSquaredGridDistance(Vector2 first, Vector2 second)
            {
                return ToGridSpaceVector(first - second).sqrMagnitude;
            }
        }
    }
}

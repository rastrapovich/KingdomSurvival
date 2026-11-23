using System;
using System.Collections.Generic;
using KingdomSurvival.AnimationDatabase;
using KingdomSurvival.BattlefieldDatabase;
using UnityEngine;
using UnityEngine.UIElements;

namespace KingdomSurvival.BattleSandbox
{
    // ПР-12З: представление действий на поле. Модель уже посчитала исход;
    // здесь он только показывается по записям ударов, по порядку и один раз.
    public sealed partial class HexBoardElement
    {
        // Высота холста набора на поле при масштабе 1 — прежняя рамка миниатюры.
        internal const float FieldHeightInHexSizes = 1.35f;
        // Прежнее правило миниатюры: центр гекса на 15% выше нижнего края рамки.
        internal const float StaticAnchorFromBottom = 0.15f;
        private const float LegacyReactionDuration = 0.24f;
        private const float HitReactionCap = 1.2f;
        private const float DeathWaitCap = 0.9f;
        private const float StepSafetyCap = 4f;
        private static readonly Color CorpseTint = new Color(0.55f, 0.55f, 0.58f, 1f);

        // Состояние конкретного бойца на поле: у каждого свои часы и ракурс,
        // даже если тип и набор одинаковые.
        private sealed class UnitPresentation
        {
            public CreatureAnimationPlayer Player;
            public HexFacing Facing;
            // Противник: зеркальный ракурс и отражённая картинка.
            public bool Mirrored;
            // Здоровье, которое очередь уже показала; null — как в модели.
            public int? PresentedHitPoints;
            // Павший остаётся на поле последним кадром смерти.
            public bool Corpse;
            // Смерти в наборе нет: последний кадр с затемнением.
            public bool CorpseDarkened;
            public Sprite LastSprite;
        }

        private CreatureAnimationDatabaseAsset animationDatabase;
        private readonly Dictionary<string, UnitPresentation> presentations =
            new Dictionary<string, UnitPresentation>(StringComparer.Ordinal);
        private SandboxBattle presentedBattle;
        private IVisualElementScheduledItem presentationTicker;
        private readonly List<string> depthOrder = new List<string>();

        private List<SandboxHitRecord> hitSequence;
        private int hitStepIndex;
        private float hitStepStartedAt;
        private float hitStepImpactAt;
        private float hitStepEndAt;
        private bool hitStepImpactShown;
        private bool hitStepLunge;
        private bool hitStepShake;
        // Выстрел снарядом: попадание показывается только по прилёте. Набор
        // стрелка и время выпуска (момент удара клипа); длительность полёта
        // пересчитывается по текущей раскладке каждый кадр.
        private CreatureAnimationSetData hitStepProjectileSet;
        private bool hitStepStrikerMirrored;
        private float hitStepReleaseAt;
        private float hitStepFlightSeconds;
        private Image projectileImage;
        private IVisualElementScheduledItem hitSequenceItem;
        private Action hitSequenceCompleted;
        private readonly HashSet<string> vanishAfterStep = new HashSet<string>(StringComparer.Ordinal);

        private float presentationClock;
        private float presentationClockReal = -1f;

        // Часы показа боя: реальное время × общая скорость боя. На них идут
        // ходьба, удары и анимации; смена скорости действует сразу.
        private float PresentationTime
        {
            get
            {
                float now = Time.realtimeSinceStartup;
                if (presentationClockReal < 0f)
                    presentationClockReal = now;
                presentationClock += Mathf.Max(0f, now - presentationClockReal) * BattlePresentationSpeed.Multiplier;
                presentationClockReal = now;
                return presentationClock;
            }
        }

        private void ResetPresentationsIfBattleChanged()
        {
            if (ReferenceEquals(presentedBattle, battle))
                return;
            presentedBattle = battle;
            presentations.Clear();
            ClearStatusIcons();
            vanishAfterStep.Clear();
            depthOrder.Clear();
            hitStepProjectileSet = null;
            HideProjectile();
        }

        private SandboxUnitVisual GetVisual(string typeId)
        {
            return !string.IsNullOrWhiteSpace(typeId) && unitVisuals.TryGetValue(typeId, out SandboxUnitVisual visual)
                ? visual
                : null;
        }

        private UnitPresentation GetPresentation(SandboxUnitState unit)
        {
            if (unit == null)
                return null;
            if (presentations.TryGetValue(unit.Id, out UnitPresentation presentation))
                return presentation;

            presentation = new UnitPresentation
            {
                Facing = InitialFacing(unit),
                Mirrored = unit.Team == SandboxTeam.Enemy
            };
            SandboxUnitVisual visual = GetVisual(unit.TypeId);
            if (visual != null && visual.IsAnimated)
            {
                // Сдвиг фазы по ID: одинаковые существа не дышат в такт.
                float phase = (unit.Id.GetHashCode() & 0x7fff) / 32767f * 2f;
                presentation.Player = new CreatureAnimationPlayer(visual.AnimationSet, DirectionFor(presentation, presentation.Facing), phase);
                presentation.Player.Play(CreatureAnimationAction.Idle, PresentationTime);
            }
            presentations.Add(unit.Id, presentation);
            EnsurePresentationTicker();
            return presentation;
        }

        private CreatureAnimationDirection ToDirection(HexFacing facing)
        {
            return animationDatabase != null
                ? animationDatabase.GetDirection(facing)
                : DefaultDirectionFor(facing);
        }

        private static CreatureAnimationDirection DefaultDirectionFor(HexFacing facing)
        {
            foreach (CreatureAnimationDirectionMapping entry in CreatureAnimationDatabaseAsset.DefaultDirectionMap())
            {
                if (entry.Facing == facing)
                    return entry.Direction;
            }
            return CreatureAnimationDirection.Front;
        }

        private void SetFacing(UnitPresentation presentation, HexFacing facing)
        {
            if (presentation == null)
                return;
            presentation.Facing = facing;
            presentation.Player?.SetDirection(DirectionFor(presentation, facing));
        }

        // Противник показан зеркально: ракурс отражённого направления
        // (влево ↔ вправо) и картинка, отражённая по горизонтали. Обе стороны
        // симметричны: свои лицом к камере, глядя вправо, противники — влево.
        private CreatureAnimationDirection DirectionFor(UnitPresentation presentation, HexFacing facing)
        {
            return ToDirection(presentation != null && presentation.Mirrored ? MirrorFacing(facing) : facing);
        }

        internal static HexFacing MirrorFacing(HexFacing facing)
        {
            switch (facing)
            {
                case HexFacing.East: return HexFacing.West;
                case HexFacing.West: return HexFacing.East;
                case HexFacing.NorthEast: return HexFacing.NorthWest;
                case HexFacing.NorthWest: return HexFacing.NorthEast;
                case HexFacing.SouthEast: return HexFacing.SouthWest;
                default: return HexFacing.SouthEast;
            }
        }

        // В начале боя каждый смотрит в сторону противника.
        private HexFacing InitialFacing(SandboxUnitState unit)
        {
            HexFacing fallback = unit.Team == SandboxTeam.Player ? HexFacing.East : HexFacing.West;
            if (battle == null)
                return fallback;
            HexLayout layout = CalculateLayout();
            Vector2 sum = Vector2.zero;
            int count = 0;
            foreach (SandboxUnitState other in battle.Units)
            {
                if (other.Team == unit.Team || other.IsDefeated)
                    continue;
                sum += layout.GetCenter(other.Position);
                count++;
            }
            if (count == 0)
                return fallback;
            Vector2 toward = sum / count - layout.GetCenter(unit.Position);
            // На поле стороны стоят слева и справа: взгляд — строго вбок.
            return Mathf.Abs(toward.x) >= 0.001f
                ? (toward.x > 0f ? HexFacing.East : HexFacing.West)
                : fallback;
        }

        private HexFacing FacingBetween(HexCoord from, HexCoord to, HexFacing fallback)
        {
            int neighbor = from.GetNeighborIndex(to);
            if (neighbor >= 0)
                return (HexFacing)neighbor;
            HexLayout layout = CalculateLayout();
            return HexFacingMath.Nearest(layout.GetCenter(to) - layout.GetCenter(from), NeighborScreenVectors(layout), fallback);
        }

        // Экранные смещения к шести соседям в текущей проекции поля.
        private static Vector2[] NeighborScreenVectors(HexLayout layout)
        {
            HexCoord reference = new HexCoord(2, 2);
            Vector2 center = layout.GetCenter(reference);
            Vector2[] vectors = new Vector2[6];
            int index = 0;
            foreach (HexCoord neighbor in reference.Neighbors())
                vectors[index++] = layout.GetCenter(neighbor) - center;
            return vectors;
        }

        private void EnsurePresentationTicker()
        {
            if (panel == null)
                return;
            bool anyAnimated = false;
            foreach (UnitPresentation presentation in presentations.Values)
            {
                if (presentation.Player != null)
                {
                    anyAnimated = true;
                    break;
                }
            }
            if (!anyAnimated)
                return;
            if (presentationTicker == null)
                presentationTicker = schedule.Execute(SyncUnitImages).Every(33);
            else
                presentationTicker.Resume();
        }

        private void PausePresentation()
        {
            presentationTicker?.Pause();
            hitSequenceItem?.Pause();
            HideProjectile();
        }

        // Виден ли боец живым (здоровье, круги, стойка). Павший, чей удар
        // ещё не показан, остаётся живым до своего маркера.
        private bool IsShownAlive(SandboxUnitState unit)
        {
            if (unit == null)
                return false;
            // Бриф завершения, этап 15: лежащий «При смерти» остаётся на поле со
            // значком состояния и полосой HP — его можно спасти или добить.
            if (unit.IsDying)
                return true;
            // ПР-16: сидящий на бойце, ушедший под землю и ждущий места не
            // занимают гекс — на поле их показывают метки, а не фигура.
            if (!unit.IsDefeated && unit.IsOffBoard)
                return false;
            if (vanishAfterStep.Contains(unit.Id))
                return true;
            if (presentations.TryGetValue(unit.Id, out UnitPresentation presentation) &&
                presentation.PresentedHitPoints.HasValue)
            {
                return presentation.PresentedHitPoints.Value > 0;
            }
            return !unit.IsDefeated;
        }

        private bool IsCorpse(SandboxUnitState unit)
        {
            return unit != null && presentations.TryGetValue(unit.Id, out UnitPresentation presentation) && presentation.Corpse;
        }

        private int GetShownHitPoints(SandboxUnitState unit)
        {
            if (presentations.TryGetValue(unit.Id, out UnitPresentation presentation) &&
                presentation.PresentedHitPoints.HasValue)
            {
                return presentation.PresentedHitPoints.Value;
            }
            return unit.HitPoints;
        }

        // ------------------------------------------------------------------
        // Ходьба
        // ------------------------------------------------------------------

        // Скорость — общая скорость ходьбы по гексам, одна на бой и исследование
        // места (База полей боя); темп шага набора подстраивается под неё —
        // ноги не скользят (SyncWalkRate).
        private static float GetWalkHexesPerSecond(string unitId) => BattlefieldDatabaseAsset.SharedWalkHexesPerSecond;

        private void SyncWalkRate(UnitPresentation presentation)
        {
            CreatureAnimationPlayer player = presentation?.Player;
            if (player?.Set == null || player.Action != CreatureAnimationAction.Walk)
                return;
            player.SetRate(player.Set.GetWalkRate(player.Direction, BattlefieldDatabaseAsset.SharedWalkHexesPerSecond), PresentationTime);
        }

        private void BeginWalkPresentation(string unitId, IReadOnlyList<HexCoord> path)
        {
            UnitPresentation presentation = GetPresentation(battle?.GetUnit(unitId));
            if (presentation == null)
                return;
            if (path.Count > 1)
                SetFacing(presentation, FacingBetween(path[0], path[1], presentation.Facing));
            presentation.Player?.Play(CreatureAnimationAction.Walk, PresentationTime);
            SyncWalkRate(presentation);
        }

        // Направление обновляется на поворотах маршрута; клип не перезапускается.
        private void UpdateWalkFacing(string unitId, IReadOnlyList<HexCoord> path, int segmentIndex)
        {
            if (path == null || segmentIndex < 0 || segmentIndex >= path.Count - 1)
                return;
            UnitPresentation presentation = GetPresentation(battle?.GetUnit(unitId));
            SetFacing(presentation, FacingBetween(path[segmentIndex], path[segmentIndex + 1], presentation != null ? presentation.Facing : HexFacing.East));
            SyncWalkRate(presentation);
        }

        private void EndWalkPresentation(string unitId)
        {
            if (string.IsNullOrEmpty(unitId) || !presentations.TryGetValue(unitId, out UnitPresentation presentation))
                return;
            presentation.Player?.Play(CreatureAnimationAction.Idle, PresentationTime);
        }

        // ------------------------------------------------------------------
        // Очередь ударов
        // ------------------------------------------------------------------

        // Удары одного действия: упреждающий, основной, ответный. Каждый шаг —
        // поворот, атака или выстрел, маркер (здоровье, реакция, смерть), конец.
        internal bool PlayHitSequence(IReadOnlyList<SandboxHitRecord> records, Action onComplete)
        {
            if (IsAnimating || battle == null || records == null || records.Count == 0)
                return false;

            ClearPointerPreview(false);
            IsAnimating = true;
            hitSequence = new List<SandboxHitRecord>(records);
            // До своих маркеров все участники показывают прежнее здоровье.
            foreach (SandboxHitRecord record in hitSequence)
            {
                UnitPresentation target = GetPresentation(battle.GetUnit(record.TargetId));
                if (target != null && !target.PresentedHitPoints.HasValue)
                    target.PresentedHitPoints = record.TargetHitPointsBefore;
            }
            hitSequenceCompleted = onComplete;
            hitStepIndex = -1;
            BeginNextHitStep();
            if (hitSequence == null)
                return true;
            hitSequenceItem = schedule.Execute(UpdateHitSequence).Every(16);
            SyncUnitImages();
            MarkDirtyRepaint();
            return true;
        }

        private void BeginNextHitStep()
        {
            EndHitStep();
            hitStepIndex++;
            if (hitSequence == null || hitStepIndex >= hitSequence.Count)
            {
                FinishHitSequence();
                return;
            }

            SandboxHitRecord record = hitSequence[hitStepIndex];
            float now = PresentationTime;
            animationAttackerId = record.StrikerId;
            animationTargetId = record.TargetId;
            attackerOffset = Vector2.zero;
            targetOffset = Vector2.zero;
            targetFlash = 0f;
            hitStepImpactShown = false;
            hitStepShake = false;
            hitStepProjectileSet = null;
            hitStepFlightSeconds = 0f;

            UnitPresentation striker = GetPresentation(battle.GetUnit(record.StrikerId));
            SetFacing(striker, FacingBetween(record.StrikerPosition, record.TargetPosition, striker != null ? striker.Facing : HexFacing.East));

            float impactDelay = AttackLungeDuration;
            float strikerDuration = AttackLungeDuration + AttackReturnDuration;
            hitStepLunge = true;
            CreatureAnimationPlayer player = striker?.Player;
            if (player != null)
            {
                player.Play(record.IsRanged ? CreatureAnimationAction.Shoot : CreatureAnimationAction.Attack, now);
                CreatureAnimationClip clip = player.Clip;
                if (clip != null && (clip.Action == CreatureAnimationAction.Attack || clip.Action == CreatureAnimationAction.Shoot))
                {
                    impactDelay = clip.ImpactSeconds;
                    strikerDuration = clip.Duration;
                    hitStepLunge = false;
                    if (player.Set != null && player.Set.FiresProjectile(clip.Action))
                    {
                        hitStepProjectileSet = player.Set;
                        hitStepStrikerMirrored = striker.Mirrored;
                    }
                }
                else
                {
                    // Атаки в наборе нет: короткий выпад без ожидания клипа.
                    player.Play(CreatureAnimationAction.Idle, now);
                }
            }

            hitStepStartedAt = now;
            hitStepImpactAt = now + Mathf.Max(0f, impactDelay);
            if (hitStepProjectileSet != null)
            {
                // Стрела вылетает в момент удара клипа, попадание — по прилёте.
                hitStepReleaseAt = hitStepImpactAt;
                hitStepFlightSeconds = ProjectileFlightSeconds(record, CalculateLayout());
                hitStepImpactAt = hitStepReleaseAt + hitStepFlightSeconds;
            }
            hitStepEndAt = Mathf.Max(now + strikerDuration, hitStepImpactAt + DamageFloatDuration);
            damageLabel.text = "−" + Mathf.Max(0, record.Damage);
            damageLabel.style.display = DisplayStyle.None;
            damageLabel.style.opacity = 1f;
        }

        private void UpdateHitSequence()
        {
            if (!IsAnimating || battle == null || hitSequence == null || hitStepIndex >= hitSequence.Count)
            {
                FinishHitSequence();
                return;
            }

            SandboxHitRecord record = hitSequence[hitStepIndex];
            float now = PresentationTime;
            HexLayout layout = CalculateLayout();
            if (hitStepProjectileSet != null && !hitStepImpactShown)
            {
                // Окно могли растянуть: полёт считается по текущей раскладке.
                hitStepFlightSeconds = ProjectileFlightSeconds(record, layout);
                hitStepImpactAt = hitStepReleaseAt + hitStepFlightSeconds;
                hitStepEndAt = Mathf.Max(hitStepEndAt, hitStepImpactAt + DamageFloatDuration);
            }
            if (!hitStepImpactShown && now >= hitStepImpactAt)
                ShowImpact(record, now);
            UpdateProjectile(record, layout, now);

            Vector2 direction = layout.GetCenter(record.TargetPosition) - layout.GetCenter(record.StrikerPosition);
            direction = direction.sqrMagnitude > 0.001f ? direction.normalized : Vector2.right;
            float lungeDistance = layout.Size * 0.34f;
            if (hitStepLunge)
            {
                if (!hitStepImpactShown)
                {
                    float progress = Mathf.Clamp01((now - hitStepStartedAt) / Mathf.Max(0.001f, hitStepImpactAt - hitStepStartedAt));
                    attackerOffset = direction * lungeDistance * (1f - Mathf.Pow(1f - progress, 3f));
                }
                else
                {
                    float returnProgress = Mathf.Clamp01((now - hitStepImpactAt) / AttackReturnDuration);
                    attackerOffset = direction * lungeDistance * (1f - Mathf.SmoothStep(0f, 1f, returnProgress));
                }
            }

            if (hitStepImpactShown)
            {
                float sinceImpact = now - hitStepImpactAt;
                float reactionStrength = 1f - Mathf.Clamp01(sinceImpact / LegacyReactionDuration);
                if (hitStepShake)
                {
                    Vector2 perpendicular = new Vector2(-direction.y, direction.x);
                    targetOffset = perpendicular * Mathf.Sin(sinceImpact * 72f) * 5f * reactionStrength;
                }
                targetFlash = reactionStrength;

                float floatProgress = Mathf.Clamp01(sinceImpact / DamageFloatDuration);
                Vector2 targetCenter = layout.GetCenter(record.TargetPosition) + targetOffset;
                damageLabel.style.left = targetCenter.x - 60f;
                damageLabel.style.top = targetCenter.y - layout.Size * 0.88f - floatProgress * 38f;
                damageLabel.style.opacity = 1f - floatProgress;
            }

            if (now >= hitStepEndAt || now - hitStepStartedAt > StepSafetyCap + hitStepFlightSeconds)
                BeginNextHitStep();

            SyncUnitImages();
            MarkDirtyRepaint();
        }

        // Маркер: исход уже определён моделью, здесь он только становится видимым.
        private void ShowImpact(SandboxHitRecord record, float now)
        {
            hitStepImpactShown = true;
            HideProjectile();
            damageLabel.style.display = DisplayStyle.Flex;
            SandboxUnitState targetUnit = battle.GetUnit(record.TargetId);
            ImpactShown?.Invoke(record, targetUnit != null && targetUnit.Team == SandboxTeam.Player);
            UnitPresentation target = GetPresentation(targetUnit);
            if (target == null)
                return;
            target.PresentedHitPoints = record.TargetHitPointsAfter;
            SetFacing(target, FacingBetween(record.TargetPosition, record.StrikerPosition, target.Facing));

            float reaction = LegacyReactionDuration;
            if (record.TargetDefeated)
            {
                if (target.Player != null)
                {
                    target.Corpse = true;
                    target.Player.Play(CreatureAnimationAction.Death, now);
                    CreatureAnimationClip death = target.Player.Clip;
                    target.CorpseDarkened = death == null;
                    reaction = death != null ? Mathf.Min(death.Duration, DeathWaitCap) : LegacyReactionDuration;
                    hitStepShake = death == null;
                }
                else
                {
                    // Статичная миниатюра и жетон: как раньше, исчезают после удара.
                    vanishAfterStep.Add(record.TargetId);
                    hitStepShake = true;
                }
            }
            else if (target.Player != null)
            {
                target.Player.Play(CreatureAnimationAction.Hit, now);
                CreatureAnimationClip hit = target.Player.Clip;
                if (hit != null && hit.Action == CreatureAnimationAction.Hit)
                {
                    reaction = Mathf.Min(hit.Duration, HitReactionCap);
                }
                else
                {
                    // Удара в наборе нет: живой стоит в ожидании, реакция — встряска.
                    target.Player.Play(CreatureAnimationAction.Idle, now);
                    hitStepShake = true;
                }
            }
            else
            {
                hitStepShake = true;
            }
            hitStepEndAt = Mathf.Max(hitStepEndAt, now + reaction);
        }

        private void EndHitStep()
        {
            vanishAfterStep.Clear();
            attackerOffset = Vector2.zero;
            targetOffset = Vector2.zero;
            targetFlash = 0f;
            damageLabel.style.display = DisplayStyle.None;
            damageLabel.style.opacity = 1f;
            hitStepProjectileSet = null;
            HideProjectile();
        }

        // ------------------------------------------------------------------
        // Снаряд (стрела лучника)
        // ------------------------------------------------------------------

        // Показанный размер холста набора — тот же, что в LayoutAnimatedImage.
        private static Vector2 FigureSize(CreatureAnimationSetData set, HexLayout layout)
        {
            Vector2 canvas = set.CanvasSize.x > 0 && set.CanvasSize.y > 0 ? (Vector2)set.CanvasSize : Vector2.one;
            float height = layout.Size * FieldHeightInHexSizes * set.FieldScale;
            return new Vector2(height * canvas.x / Mathf.Max(1f, canvas.y), height);
        }

        private void ProjectilePoints(SandboxHitRecord record, HexLayout layout, out Vector2 from, out Vector2 to)
        {
            CreatureAnimationProjectileData data = hitStepProjectileSet.Projectile;
            Vector2 ground = layout.GetCenter(record.StrikerPosition);
            Vector2 targetGround = layout.GetCenter(record.TargetPosition);
            // Противник показан отражённым и смотрит влево.
            from = CreatureAnimationProjectileMath.LaunchPosition(
                ground, FigureSize(hitStepProjectileSet, layout), targetGround, data, hitStepStrikerMirrored ? -1f : 1f);
            to = CreatureAnimationProjectileMath.HitPosition(targetGround, layout.Size, data);
        }

        private float ProjectileFlightSeconds(SandboxHitRecord record, HexLayout layout)
        {
            ProjectilePoints(record, layout, out Vector2 from, out Vector2 to);
            float hexStep = NeighborScreenVectors(layout)[0].magnitude;
            return Mathf.Max(0.01f, CreatureAnimationProjectileMath.FlightSeconds(from, to, hexStep, hitStepProjectileSet.Projectile));
        }

        private void UpdateProjectile(SandboxHitRecord record, HexLayout layout, float now)
        {
            CreatureAnimationProjectileData data = hitStepProjectileSet != null ? hitStepProjectileSet.Projectile : null;
            if (data == null || !data.HasSprite || hitStepImpactShown || now < hitStepReleaseAt)
            {
                HideProjectile();
                return;
            }

            if (projectileImage == null)
            {
                projectileImage = new Image();
                projectileImage.name = "sandbox-projectile";
                projectileImage.pickingMode = PickingMode.Ignore;
                projectileImage.style.position = Position.Absolute;
                projectileImage.scaleMode = ScaleMode.StretchToFill;
                projectileImage.style.transformOrigin = new TransformOrigin(Length.Percent(50f), Length.Percent(50f));
                Add(projectileImage);
            }

            ProjectilePoints(record, layout, out Vector2 from, out Vector2 to);
            float t = Mathf.Clamp01((now - hitStepReleaseAt) / Mathf.Max(0.01f, hitStepFlightSeconds));
            Vector2 position = CreatureAnimationProjectileMath.Evaluate(from, to, data.ArcHeight, t, out float angle);

            Sprite sprite = data.Sprite;
            if (projectileImage.sprite != sprite)
                projectileImage.sprite = sprite;
            float width = data.LengthHexes * layout.Size;
            float height = width * sprite.rect.height / Mathf.Max(1f, sprite.rect.width);
            projectileImage.style.width = width;
            projectileImage.style.height = height;
            projectileImage.style.left = position.x - width * 0.5f;
            projectileImage.style.top = position.y - height * 0.5f;
            projectileImage.style.rotate = new Rotate(angle);
            projectileImage.style.display = DisplayStyle.Flex;
            projectileImage.BringToFront();
        }

        private void HideProjectile()
        {
            if (projectileImage != null)
                projectileImage.style.display = DisplayStyle.None;
        }

        // Вызывается после перестановки слоёв: стрела поверх фигур.
        private void BringProjectileToFront()
        {
            if (projectileImage != null && projectileImage.style.display == DisplayStyle.Flex)
                projectileImage.BringToFront();
        }

        private void FinishHitSequence()
        {
            if (hitSequence == null && hitSequenceItem == null)
                return;
            hitSequenceItem?.Pause();
            hitSequenceItem = null;
            hitSequence = null;
            EndHitStep();
            // Очередь показала всё: дальше здоровье снова читается из модели.
            foreach (UnitPresentation presentation in presentations.Values)
                presentation.PresentedHitPoints = null;
            IsAnimating = false;
            animationAttackerId = null;
            animationTargetId = null;

            Action callback = hitSequenceCompleted;
            hitSequenceCompleted = null;
            SyncUnitImages();
            MarkDirtyRepaint();
            callback?.Invoke();
        }

        // ------------------------------------------------------------------
        // Картинка бойца с набором анимаций
        // ------------------------------------------------------------------

        // Опора набора стоит в точке земли; размер — от гекса, а не от границ
        // текущего кадра, поэтому ноги не прыгают при смене кадров и падении.
        private void LayoutAnimatedImage(
            Image image,
            SandboxUnitVisual visual,
            UnitPresentation presentation,
            Vector2 ground,
            HexLayout layout)
        {
            CreatureAnimationSetData set = visual.AnimationSet;
            Sprite sprite = presentation.Player != null ? presentation.Player.Evaluate(PresentationTime) : null;
            if (sprite == null)
                sprite = presentation.LastSprite != null ? presentation.LastSprite : set.FindFirstFrame();
            if (sprite == null)
                sprite = visual.BattlefieldSprite;
            presentation.LastSprite = sprite;
            if (image.sprite != sprite)
                image.sprite = sprite;

            Vector2 canvas = set.CanvasSize.x > 0 && set.CanvasSize.y > 0
                ? (Vector2)set.CanvasSize
                : sprite != null ? sprite.rect.size : Vector2.one;
            float height = layout.Size * FieldHeightInHexSizes * set.FieldScale;
            float width = height * canvas.x / Mathf.Max(1f, canvas.y);
            Vector2 cellOffset = presentation.Player?.Clip != null ? presentation.Player.Clip.Offset : Vector2.zero;
            image.scaleMode = ScaleMode.StretchToFill;
            image.style.width = width;
            image.style.height = height;
            // Отражение — вокруг центра картинки, поэтому опора X тоже отражается:
            // ноги остаются в центре гекса. Противник отражён целиком; клип
            // набора «один ракурс + отражение» отражается ещё раз (два
            // отражения дают прямую картинку).
            bool flip = presentation.Mirrored != (presentation.Player != null && presentation.Player.IsMirrored);
            float pivotX = flip ? 1f - set.Pivot.x : set.Pivot.x;
            float offsetX = flip ? -cellOffset.x : cellOffset.x;
            image.style.scale = new Scale(new Vector3(flip ? -1f : 1f, 1f, 1f));
            image.style.left = ground.x - pivotX * width + offsetX * width;
            image.style.top = ground.y - (1f - set.Pivot.y) * height - cellOffset.y * height;
        }

        // Порядок слоёв — по месту на земле: павшие ниже живых, дальние ниже
        // ближних. Иерархия меняется, только когда порядок действительно сменился.
        private void ApplyDepthOrder(List<(string id, float key, VisualElement element)> entries)
        {
            entries.Sort((a, b) =>
            {
                int byKey = a.key.CompareTo(b.key);
                return byKey != 0 ? byKey : string.CompareOrdinal(a.id, b.id);
            });
            bool changed = entries.Count != depthOrder.Count;
            for (int i = 0; !changed && i < entries.Count; i++)
                changed = entries[i].id != depthOrder[i];
            if (!changed)
                return;
            depthOrder.Clear();
            foreach ((string id, float _, VisualElement element) in entries)
            {
                element.BringToFront();
                depthOrder.Add(id);
            }
        }
    }
}

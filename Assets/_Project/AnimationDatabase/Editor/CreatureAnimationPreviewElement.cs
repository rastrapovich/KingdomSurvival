using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace KingdomSurvival.AnimationDatabase.Editor
{
    // Предпросмотр набора без Play Mode. Порядок кадров и время — те же
    // CreatureAnimationClip, что в бою. Таймер работает, только пока элемент
    // на экране, и останавливается при закрытии окна или смене существа.
    public sealed class CreatureAnimationPreviewElement : VisualElement
    {
        // Высота холста на поле в размерах гекса при масштабе 1 (прежняя рамка миниатюры).
        public const float FieldHeightInHexSizes = 1.35f;
        private const float GridVerticalScale = 0.75f;
        private const float OnceClipRestartDelay = 0.6f;

        private readonly PopupField<CreatureAnimationAction> actionField;
        private readonly PopupField<CreatureAnimationDirection> directionField;
        private readonly VisualElement viewport;
        private readonly VisualElement overlay;
        private readonly Image spriteImage;
        private readonly VisualElement pivotMarker;
        private readonly Button playButton;
        private readonly SliderInt frameSlider;
        private readonly Label frameLabel;
        private readonly Label zoomLabel;
        private readonly Label noticeLabel;
        private readonly Toggle groundToggle;
        private readonly Toggle pivotToggle;

        private CreatureAnimationDatabaseAsset database;
        private CreatureAnimationSetData set;
        private Sprite staticSprite;
        private CreatureAnimationClip clip;
        private CreatureAnimationAction action = CreatureAnimationAction.Idle;
        private CreatureAnimationDirection direction = CreatureAnimationDirection.Front;
        private IVisualElementScheduledItem timer;
        private double lastTick;
        private float clipTime;
        private float finishedFor;
        private bool playing = true;
        private float zoom = 1f;
        private bool fitRequested = true;
        private bool draggingPivot;
        private int dragPointerId = -1;

        // Выбор действия и ракурса пользователем.
        public event Action<CreatureAnimationAction, CreatureAnimationDirection> SelectionChanged;
        // Перетаскивание точки опоры: новое значение и признак конца жеста.
        public event Action<Vector2, bool> PivotDragged;

        public bool AllowPivotEditing { get; set; }
        public CreatureAnimationAction SelectedAction => action;
        public CreatureAnimationDirection SelectedDirection => direction;
        public CreatureAnimationClip CurrentClip => clip;

        public CreatureAnimationPreviewElement(float viewportHeight = 300f)
        {
            style.flexGrow = 1f;
            style.minWidth = 260f;

            VisualElement selectors = Row();
            actionField = new PopupField<CreatureAnimationAction>(
                new List<CreatureAnimationAction>(CreatureAnimationLabels.Actions),
                CreatureAnimationAction.Idle,
                ActionChoiceLabel,
                ActionChoiceLabel);
            actionField.style.flexGrow = 1f;
            actionField.tooltip = "Действие";
            actionField.RegisterValueChangedCallback(evt => Select(evt.newValue, direction, true));
            directionField = new PopupField<CreatureAnimationDirection>(
                new List<CreatureAnimationDirection>(CreatureAnimationLabels.Directions),
                CreatureAnimationDirection.Front,
                DirectionChoiceLabel,
                DirectionChoiceLabel);
            directionField.style.flexGrow = 1f;
            directionField.tooltip = "Ракурс";
            directionField.RegisterValueChangedCallback(evt => Select(action, evt.newValue, true));
            selectors.Add(actionField);
            selectors.Add(directionField);
            Add(selectors);

            viewport = new VisualElement { name = "animation-preview-viewport" };
            viewport.style.height = viewportHeight;
            viewport.style.marginTop = 4f;
            viewport.style.overflow = Overflow.Hidden;
            viewport.style.position = Position.Relative;
            viewport.generateVisualContent += DrawCheckerboard;
            viewport.RegisterCallback<WheelEvent>(OnWheel);
            viewport.RegisterCallback<GeometryChangedEvent>(_ => Layout());
            Add(viewport);

            spriteImage = new Image { scaleMode = ScaleMode.StretchToFill, pickingMode = PickingMode.Ignore };
            spriteImage.style.position = Position.Absolute;
            viewport.Add(spriteImage);

            overlay = new VisualElement { pickingMode = PickingMode.Ignore };
            overlay.style.position = Position.Absolute;
            overlay.style.left = 0f;
            overlay.style.top = 0f;
            overlay.style.right = 0f;
            overlay.style.bottom = 0f;
            overlay.generateVisualContent += DrawOverlay;
            viewport.Add(overlay);

            pivotMarker = new VisualElement
            {
                tooltip = "Точка опоры: место кадра, которое стоит в центре гекса. Перетащите мышью."
            };
            pivotMarker.style.position = Position.Absolute;
            pivotMarker.style.width = 12f;
            pivotMarker.style.height = 12f;
            SetRadius(pivotMarker, 6f);
            pivotMarker.style.backgroundColor = new Color(0.95f, 0.10f, 0.08f, 1f);
            SetBorder(pivotMarker, new Color(1f, 1f, 1f, 0.9f), 1.5f);
            pivotMarker.RegisterCallback<PointerDownEvent>(BeginPivotDrag);
            pivotMarker.RegisterCallback<PointerMoveEvent>(ContinuePivotDrag);
            pivotMarker.RegisterCallback<PointerUpEvent>(EndPivotDrag);
            pivotMarker.RegisterCallback<PointerCaptureOutEvent>(_ => FinishPivotDrag());
            viewport.Add(pivotMarker);

            noticeLabel = new Label { pickingMode = PickingMode.Ignore };
            noticeLabel.style.position = Position.Absolute;
            noticeLabel.style.left = 6f;
            noticeLabel.style.right = 6f;
            noticeLabel.style.bottom = 4f;
            noticeLabel.style.whiteSpace = WhiteSpace.Normal;
            noticeLabel.style.fontSize = 10f;
            noticeLabel.style.color = new Color(1f, 0.72f, 0.35f, 1f);
            viewport.Add(noticeLabel);

            VisualElement transport = Row();
            Button previous = new Button(() => Step(-1)) { text = "◀", tooltip = "Предыдущий кадр" };
            playButton = new Button(TogglePlay) { tooltip = "Воспроизведение / пауза" };
            Button next = new Button(() => Step(1)) { text = "▶|", tooltip = "Следующий кадр" };
            frameSlider = new SliderInt(0, 0) { tooltip = "Текущий кадр" };
            frameSlider.style.flexGrow = 1f;
            frameSlider.RegisterValueChangedCallback(evt =>
            {
                if (clip == null)
                    return;
                playing = false;
                clipTime = clip.GetFrameStart(evt.newValue);
                RefreshTransport();
                Render();
            });
            transport.Add(previous);
            transport.Add(playButton);
            transport.Add(next);
            transport.Add(frameSlider);
            Add(transport);

            frameLabel = new Label();
            frameLabel.style.fontSize = 10f;
            frameLabel.style.color = new Color(0.72f, 0.72f, 0.72f, 1f);
            Add(frameLabel);

            VisualElement viewRow = Row();
            viewRow.Add(new Button(() => { fitRequested = true; Layout(); }) { text = "Вписать", tooltip = "Вписать кадр в окно просмотра" });
            viewRow.Add(new Button(() => SetZoom(1f)) { text = "100%", tooltip = "Один пиксель кадра — один пиксель экрана" });
            zoomLabel = new Label();
            zoomLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
            zoomLabel.style.minWidth = 110f;
            zoomLabel.tooltip = "Масштаб просмотра (колесо мыши). Не меняет размер существа в игре.";
            viewRow.Add(zoomLabel);
            groundToggle = new Toggle("Гекс") { value = true, tooltip = "Линия земли и гекс поля в игровом масштабе" };
            groundToggle.RegisterValueChangedCallback(_ => overlay.MarkDirtyRepaint());
            pivotToggle = new Toggle("Опора") { value = true, tooltip = "Маркер точки опоры" };
            pivotToggle.RegisterValueChangedCallback(_ => Layout());
            ShrinkToggle(groundToggle);
            ShrinkToggle(pivotToggle);
            viewRow.Add(groundToggle);
            viewRow.Add(pivotToggle);
            Add(viewRow);

            RegisterCallback<AttachToPanelEvent>(_ => StartTimer());
            RegisterCallback<DetachFromPanelEvent>(_ => StopTimer());
            RefreshTransport();
        }

        public void SetData(CreatureAnimationDatabaseAsset value, CreatureAnimationSetData animationSet, Sprite fallbackSprite)
        {
            bool setChanged = !ReferenceEquals(set, animationSet);
            database = value;
            set = animationSet;
            staticSprite = fallbackSprite;
            if (setChanged)
            {
                fitRequested = true;
                clipTime = 0f;
                playing = true;
            }
            Rebuild();
        }

        // Внешний выбор (ячейка таблицы) — без повторного события.
        public void Select(CreatureAnimationAction newAction, CreatureAnimationDirection newDirection, bool notify = false)
        {
            bool changed = newAction != action || newDirection != direction;
            action = newAction;
            direction = newDirection;
            actionField.SetValueWithoutNotify(action);
            directionField.SetValueWithoutNotify(direction);
            if (changed)
            {
                clipTime = 0f;
                finishedFor = 0f;
            }
            Rebuild();
            if (notify && changed)
                SelectionChanged?.Invoke(action, direction);
        }

        // Данные набора изменились (скорость, кадры, опора): кадр и время пересчитываются.
        public void Rebuild()
        {
            clip = CreatureAnimationResolver.Resolve(set, action, direction);
            if (clip != null && clipTime > clip.Duration && clip.Playback != CreatureAnimationPlayback.Loop)
                clipTime = 0f;
            RefreshNotice();
            RefreshTransport();
            Layout();
        }

        private void StartTimer()
        {
            StopTimer();
            lastTick = EditorApplication.timeSinceStartup;
            timer = schedule.Execute(Tick).Every(16);
        }

        private void StopTimer()
        {
            timer?.Pause();
            timer = null;
            FinishPivotDrag();
        }

        private void Tick()
        {
            double now = EditorApplication.timeSinceStartup;
            float delta = Mathf.Clamp((float)(now - lastTick), 0f, 0.25f);
            lastTick = now;
            if (!playing || clip == null || clip.FrameCount == 0)
                return;

            clipTime += delta;
            if (clip.IsFinished(clipTime))
            {
                // Однократный клип в просмотре повторяется после короткой паузы.
                finishedFor += delta;
                if (finishedFor >= OnceClipRestartDelay)
                {
                    clipTime = 0f;
                    finishedFor = 0f;
                }
            }
            Render();
            RefreshTransport();
        }

        private void TogglePlay()
        {
            playing = !playing;
            lastTick = EditorApplication.timeSinceStartup;
            RefreshTransport();
        }

        private void Step(int delta)
        {
            if (clip == null || clip.FrameCount == 0)
                return;
            playing = false;
            int index = (clip.GetFrameIndex(clipTime) + delta + clip.FrameCount) % clip.FrameCount;
            clipTime = clip.GetFrameStart(index);
            RefreshTransport();
            Render();
        }

        private void RefreshTransport()
        {
            playButton.text = playing ? "❚❚" : "▶";
            if (clip == null || clip.FrameCount == 0)
            {
                frameSlider.lowValue = 0;
                frameSlider.highValue = 0;
                frameSlider.SetValueWithoutNotify(0);
                frameSlider.SetEnabled(false);
                frameLabel.text = staticSprite != null
                    ? "Анимаций нет — показана статичная миниатюра поля."
                    : "Анимаций нет.";
                return;
            }

            frameSlider.SetEnabled(true);
            int index = clip.GetFrameIndex(clipTime);
            frameSlider.lowValue = 0;
            frameSlider.highValue = Mathf.Max(0, clip.FrameCount - 1);
            frameSlider.SetValueWithoutNotify(index);
            float shown = clip.Playback == CreatureAnimationPlayback.Loop && clip.Duration > 0f
                ? clipTime % clip.Duration
                : Mathf.Min(clipTime, clip.Duration);
            string marker = CreatureAnimationLabels.HasImpactMarker(clip.Action)
                ? " · удар на " + clip.ImpactSeconds.ToString("0.00") + " с"
                : string.Empty;
            frameLabel.text = "Кадр " + (index + 1) + " / " + clip.FrameCount +
                              " · " + shown.ToString("0.00") + " / " + clip.Duration.ToString("0.00") + " с · " +
                              CreatureAnimationLabels.PlaybackTitle(clip.Playback) + marker;
        }

        private void RefreshNotice()
        {
            string text = string.Empty;
            if (set != null && clip == null)
            {
                text = action == CreatureAnimationAction.Death
                    ? "Нет «Смерти»: в бою павший останется последним кадром с затемнением."
                    : "Нет кадров для «" + CreatureAnimationLabels.ActionTitle(action) + "».";
            }
            else if (clip != null && clip.IsActionSubstitute)
            {
                text = "Нет «" + CreatureAnimationLabels.ActionTitle(action) + "» — в бою подставляется «" +
                       CreatureAnimationLabels.ActionTitle(clip.Action) + "».";
            }
            else if (clip != null && clip.IsDirectionSubstitute)
            {
                text = "Ракурса «" + CreatureAnimationLabels.DirectionTitle(direction) + "» нет — показан ближайший: «" +
                       CreatureAnimationLabels.DirectionTitle(clip.Direction) + "».";
            }
            noticeLabel.text = text;
            noticeLabel.style.display = string.IsNullOrEmpty(text) ? DisplayStyle.None : DisplayStyle.Flex;
        }

        private Vector2 CanvasSize
        {
            get
            {
                if (set != null && set.CanvasSize.x > 0 && set.CanvasSize.y > 0)
                    return set.CanvasSize;
                Sprite sprite = clip != null && clip.FrameCount > 0 ? clip.Frames[0] : staticSprite;
                return sprite != null ? sprite.rect.size : new Vector2(128f, 128f);
            }
        }

        private Vector2 Pivot => set != null ? set.Pivot : CreatureAnimationSetData.DefaultPivot;

        private void SetZoom(float value)
        {
            zoom = Mathf.Clamp(value, 0.05f, 16f);
            fitRequested = false;
            Layout();
        }

        private void OnWheel(WheelEvent evt)
        {
            float factor = evt.delta.y > 0f ? 1f / 1.12f : 1.12f;
            SetZoom(zoom * factor);
            evt.StopPropagation();
        }

        // Холст по центру окна; опора и гекс — поверх.
        private void Layout()
        {
            float width = viewport.resolvedStyle.width;
            float height = viewport.resolvedStyle.height;
            if (float.IsNaN(width) || width <= 1f || height <= 1f)
                return;

            Vector2 canvas = CanvasSize;
            if (fitRequested)
                zoom = Mathf.Clamp(Mathf.Min(width / canvas.x, height / canvas.y) * 0.85f, 0.05f, 16f);
            zoomLabel.text = "Просмотр " + Mathf.RoundToInt(zoom * 100f) + "%";

            Vector2 size = canvas * zoom;
            Vector2 offset = clip != null ? clip.Offset : Vector2.zero;
            float left = (width - size.x) * 0.5f + offset.x * size.x;
            float top = (height - size.y) * 0.5f - offset.y * size.y;
            spriteImage.style.left = left;
            spriteImage.style.top = top;
            spriteImage.style.width = size.x;
            spriteImage.style.height = size.y;

            Vector2 pivotPoint = PivotScreenPoint(width, height, size);
            pivotMarker.style.left = pivotPoint.x - 6f;
            pivotMarker.style.top = pivotPoint.y - 6f;
            pivotMarker.style.display = pivotToggle.value && (set != null || staticSprite != null) ? DisplayStyle.Flex : DisplayStyle.None;
            pivotMarker.pickingMode = AllowPivotEditing && set != null ? PickingMode.Position : PickingMode.Ignore;
            overlay.MarkDirtyRepaint();
            Render();
        }

        private Vector2 PivotScreenPoint(float width, float height, Vector2 size)
        {
            Vector2 pivot = Pivot;
            float left = (width - size.x) * 0.5f;
            float top = (height - size.y) * 0.5f;
            return new Vector2(left + pivot.x * size.x, top + (1f - pivot.y) * size.y);
        }

        private void Render()
        {
            Sprite sprite = null;
            if (clip != null && clip.FrameCount > 0)
                sprite = clip.Frames[clip.GetFrameIndex(clipTime)];
            else if (set == null || !set.HasAnyFrames)
                sprite = staticSprite;
            else
                sprite = set.FindFirstFrame();
            if (spriteImage.sprite != sprite)
                spriteImage.sprite = sprite;
            spriteImage.style.display = sprite != null ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void DrawCheckerboard(MeshGenerationContext context)
        {
            Rect rect = viewport.contentRect;
            Painter2D painter = context.painter2D;
            const float cell = 12f;
            Color dark = new Color(0.16f, 0.16f, 0.17f, 1f);
            Color light = new Color(0.22f, 0.22f, 0.23f, 1f);
            FillRect(painter, new Rect(0f, 0f, rect.width, rect.height), dark);
            painter.fillColor = light;
            for (float y = 0f, row = 0; y < rect.height; y += cell, row++)
            {
                for (float x = ((int)row % 2) * cell; x < rect.width; x += cell * 2f)
                {
                    painter.BeginPath();
                    painter.MoveTo(new Vector2(x, y));
                    painter.LineTo(new Vector2(Mathf.Min(x + cell, rect.width), y));
                    painter.LineTo(new Vector2(Mathf.Min(x + cell, rect.width), Mathf.Min(y + cell, rect.height)));
                    painter.LineTo(new Vector2(x, Mathf.Min(y + cell, rect.height)));
                    painter.ClosePath();
                    painter.Fill();
                }
            }
        }

        // Линия земли и гекс в игровом масштабе: высота холста на поле —
        // 1,35 размера гекса × масштаб на поле.
        private void DrawOverlay(MeshGenerationContext context)
        {
            if (!groundToggle.value)
                return;
            float width = viewport.resolvedStyle.width;
            float height = viewport.resolvedStyle.height;
            if (float.IsNaN(width) || width <= 1f)
                return;

            Vector2 size = CanvasSize * zoom;
            Vector2 center = PivotScreenPoint(width, height, size);
            float fieldScale = set != null ? set.FieldScale : 1f;
            float hexSize = size.y / (FieldHeightInHexSizes * fieldScale);
            Painter2D painter = context.painter2D;

            painter.strokeColor = new Color(0.45f, 0.75f, 0.85f, 0.55f);
            painter.lineWidth = 1f;
            painter.BeginPath();
            painter.MoveTo(new Vector2(0f, center.y));
            painter.LineTo(new Vector2(width, center.y));
            painter.Stroke();

            painter.strokeColor = new Color(0.95f, 0.85f, 0.45f, 0.85f);
            painter.lineWidth = 1.5f;
            painter.BeginPath();
            for (int i = 0; i < 6; i++)
            {
                float angle = Mathf.Deg2Rad * (60f * i - 30f);
                Vector2 point = center + new Vector2(Mathf.Cos(angle) * hexSize, Mathf.Sin(angle) * hexSize * GridVerticalScale);
                if (i == 0)
                    painter.MoveTo(point);
                else
                    painter.LineTo(point);
            }
            painter.ClosePath();
            painter.Stroke();

            // Стрелка направления ракурса на поле: проверка таблицы ракурсов.
            if (database != null)
            {
                HexFacing facing = database.GetFacing(clip != null ? clip.Direction : direction);
                Vector2 arrow = FacingScreenVector(facing).normalized * hexSize * 0.8f;
                painter.strokeColor = new Color(1f, 1f, 1f, 0.85f);
                painter.lineWidth = 2f;
                painter.BeginPath();
                painter.MoveTo(center);
                painter.LineTo(center + arrow);
                Vector2 side = new Vector2(-arrow.y, arrow.x).normalized * 5f;
                Vector2 back = -arrow.normalized * 8f;
                painter.MoveTo(center + arrow);
                painter.LineTo(center + arrow + back + side);
                painter.MoveTo(center + arrow);
                painter.LineTo(center + arrow + back - side);
                painter.Stroke();
            }
        }

        // Экранный вектор к соседнему гексу (ось Y вниз), как на поле боя.
        public static Vector2 FacingScreenVector(HexFacing facing)
        {
            float dx = Mathf.Sqrt(3f);
            float dy = 1.5f * GridVerticalScale;
            switch (facing)
            {
                case HexFacing.East: return new Vector2(dx, 0f);
                case HexFacing.NorthEast: return new Vector2(dx * 0.5f, -dy);
                case HexFacing.NorthWest: return new Vector2(-dx * 0.5f, -dy);
                case HexFacing.West: return new Vector2(-dx, 0f);
                case HexFacing.SouthWest: return new Vector2(-dx * 0.5f, dy);
                default: return new Vector2(dx * 0.5f, dy);
            }
        }

        private void BeginPivotDrag(PointerDownEvent evt)
        {
            if (!AllowPivotEditing || set == null || evt.button != 0)
                return;
            draggingPivot = true;
            dragPointerId = evt.pointerId;
            pivotMarker.CapturePointer(evt.pointerId);
            evt.StopPropagation();
        }

        private void ContinuePivotDrag(PointerMoveEvent evt)
        {
            if (!draggingPivot || evt.pointerId != dragPointerId)
                return;
            Vector2 local = viewport.WorldToLocal(evt.position);
            PivotDragged?.Invoke(ScreenToPivot(local), false);
            evt.StopPropagation();
        }

        private void EndPivotDrag(PointerUpEvent evt)
        {
            if (!draggingPivot || evt.pointerId != dragPointerId)
                return;
            Vector2 local = viewport.WorldToLocal(evt.position);
            if (pivotMarker.HasPointerCapture(evt.pointerId))
                pivotMarker.ReleasePointer(evt.pointerId);
            draggingPivot = false;
            dragPointerId = -1;
            PivotDragged?.Invoke(ScreenToPivot(local), true);
            evt.StopPropagation();
        }

        private void FinishPivotDrag()
        {
            draggingPivot = false;
            dragPointerId = -1;
        }

        private Vector2 ScreenToPivot(Vector2 local)
        {
            float width = viewport.resolvedStyle.width;
            float height = viewport.resolvedStyle.height;
            Vector2 size = CanvasSize * zoom;
            float left = (width - size.x) * 0.5f;
            float top = (height - size.y) * 0.5f;
            return new Vector2(
                Mathf.Clamp01((local.x - left) / Mathf.Max(1f, size.x)),
                Mathf.Clamp01(1f - (local.y - top) / Mathf.Max(1f, size.y)));
        }

        private string ActionChoiceLabel(CreatureAnimationAction value)
        {
            CreatureAnimationClipData data = set?.FindClip(value);
            string suffix = data != null && data.HasAnyFrames ? string.Empty : " (нет)";
            return CreatureAnimationLabels.ActionTitle(value) + suffix;
        }

        private string DirectionChoiceLabel(CreatureAnimationDirection value)
        {
            string arrow = database != null ? CreatureAnimationLabels.FacingArrow(database.GetFacing(value)) + " " : string.Empty;
            return arrow + CreatureAnimationLabels.DirectionTitle(value);
        }

        private static VisualElement Row()
        {
            VisualElement row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginTop = 3f;
            return row;
        }

        private static void ShrinkToggle(Toggle toggle)
        {
            toggle.style.marginLeft = 8f;
            toggle.labelElement.style.minWidth = 30f;
            toggle.labelElement.style.width = StyleKeyword.Auto;
        }

        private static void FillRect(Painter2D painter, Rect rect, Color color)
        {
            painter.fillColor = color;
            painter.BeginPath();
            painter.MoveTo(rect.min);
            painter.LineTo(new Vector2(rect.xMax, rect.yMin));
            painter.LineTo(rect.max);
            painter.LineTo(new Vector2(rect.xMin, rect.yMax));
            painter.ClosePath();
            painter.Fill();
        }

        private static void SetRadius(VisualElement element, float radius)
        {
            element.style.borderTopLeftRadius = radius;
            element.style.borderTopRightRadius = radius;
            element.style.borderBottomLeftRadius = radius;
            element.style.borderBottomRightRadius = radius;
        }

        private static void SetBorder(VisualElement element, Color color, float width)
        {
            element.style.borderLeftWidth = width;
            element.style.borderRightWidth = width;
            element.style.borderTopWidth = width;
            element.style.borderBottomWidth = width;
            element.style.borderLeftColor = color;
            element.style.borderRightColor = color;
            element.style.borderTopColor = color;
            element.style.borderBottomColor = color;
        }
    }
}

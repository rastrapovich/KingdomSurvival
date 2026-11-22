using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace KingdomSurvival.AnimationDatabase.Editor
{
    // Предпросмотр набора фигуры глобальной карты без Play Mode. Кадры и время —
    // те же CreatureAnimationClip, что в игре. Вместо гекса боя — шкала «ширина
    // одной клетки карты» и стрелка сектора карты, куда смотрит ракурс.
    // Таймер работает, только пока элемент на экране.
    public sealed class WorldMapAnimationPreviewElement : VisualElement
    {
        private readonly PopupField<CreatureAnimationAction> actionField;
        private readonly PopupField<CreatureAnimationDirection> directionField;
        private readonly VisualElement viewport;
        private readonly VisualElement overlay;
        private readonly Image spriteImage;
        private readonly VisualElement pivotMarker;
        private readonly Label cellLabel;
        private readonly Button playButton;
        private readonly SliderInt frameSlider;
        private readonly Label frameLabel;
        private readonly Label zoomLabel;
        private readonly Label noticeLabel;
        private readonly Toggle groundToggle;
        private readonly Toggle pivotToggle;

        private WorldMapAnimationDatabaseAsset database;
        private WorldMapAnimationSetData mapSet;
        private CreatureAnimationClip clip;
        // Кадр, который игра покажет на стоянке без «Ожидания» (первый кадр ходьбы).
        private Sprite standingFrame;
        private CreatureAnimationAction action = CreatureAnimationAction.Walk;
        private CreatureAnimationDirection direction = CreatureAnimationDirection.Front;
        private IVisualElementScheduledItem timer;
        private double lastTick;
        private float clipTime;
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

        public WorldMapAnimationPreviewElement(float viewportHeight = 340f)
        {
            style.flexGrow = 1f;
            style.minWidth = 260f;

            VisualElement selectors = Row();
            actionField = new PopupField<CreatureAnimationAction>(
                new List<CreatureAnimationAction>(WorldMapAnimationEditorData.Actions),
                CreatureAnimationAction.Walk,
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
            directionField.tooltip = "Ракурс (стрелка — сектор карты, куда он смотрит)";
            directionField.RegisterValueChangedCallback(evt => Select(action, evt.newValue, true));
            selectors.Add(actionField);
            selectors.Add(directionField);
            Add(selectors);

            viewport = new VisualElement { name = "world-map-preview-viewport" };
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

            cellLabel = new Label("ширина клетки карты") { pickingMode = PickingMode.Ignore };
            cellLabel.style.position = Position.Absolute;
            cellLabel.style.fontSize = 9f;
            cellLabel.style.color = new Color(0.95f, 0.85f, 0.45f, 0.95f);
            viewport.Add(cellLabel);

            pivotMarker = new VisualElement
            {
                tooltip = "Точка опоры: место кадра, которое стоит в центре клетки карты (под ногами). Перетащите мышью."
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
            zoomLabel.tooltip = "Масштаб просмотра (колесо мыши). Не меняет размер фигуры на карте.";
            viewRow.Add(zoomLabel);
            groundToggle = new Toggle("Клетка") { value = true, tooltip = "Шкала ширины клетки карты и стрелка сектора" };
            groundToggle.RegisterValueChangedCallback(_ => Layout());
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

        public void SetData(WorldMapAnimationDatabaseAsset value, WorldMapAnimationSetData set)
        {
            bool setChanged = !ReferenceEquals(mapSet, set);
            database = value;
            mapSet = set;
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
            if (!WorldMapAnimationEditorData.IsSupportedAction(newAction))
                newAction = CreatureAnimationAction.Walk;
            bool changed = newAction != action || newDirection != direction;
            action = newAction;
            direction = newDirection;
            if (changed)
                clipTime = 0f;
            Rebuild();
            if (notify && changed)
                SelectionChanged?.Invoke(action, direction);
        }

        // Данные набора изменились (скорость, кадры, опора, рост): пересчёт.
        public void Rebuild()
        {
            actionField.SetValueWithoutNotify(action);
            directionField.SetValueWithoutNotify(direction);
            CreatureAnimationSetData animation = mapSet?.Animation;
            clip = CreatureAnimationResolver.Resolve(animation, action, direction);
            // Нет «Ожидания»: на стоянке игра показывает первый кадр ходьбы.
            standingFrame = null;
            if (animation != null && clip == null && action == CreatureAnimationAction.Idle)
            {
                CreatureAnimationClip walk = CreatureAnimationResolver.Resolve(animation, CreatureAnimationAction.Walk, direction);
                if (walk != null && walk.FrameCount > 0)
                    standingFrame = walk.Frames[0];
            }
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
            // Однократный клип в просмотре повторяется сразу после конца.
            if (clip.IsFinished(clipTime))
                clipTime = 0f;
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
                frameLabel.text = standingFrame != null ? "Ожидания нет — на стоянке игра покажет первый кадр ходьбы." : "Анимаций нет.";
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
            WorldMapFacing facing = database != null ? database.GetFacing(clip.Direction) : WorldMapFacing.Down;
            frameLabel.text = "Кадр " + (index + 1) + " / " + clip.FrameCount +
                              " · " + shown.ToString("0.00") + " / " + clip.Duration.ToString("0.00") + " с · " +
                              CreatureAnimationLabels.PlaybackTitle(clip.Playback) +
                              " · смотрит " + WorldMapFacingLabels.Arrow(facing) + " " + WorldMapFacingLabels.Title(facing);
        }

        private void RefreshNotice()
        {
            string text = string.Empty;
            if (mapSet != null && clip == null && standingFrame == null)
            {
                text = "Нет кадров для «" + CreatureAnimationLabels.ActionTitle(action) + "».";
            }
            else if (standingFrame != null)
            {
                text = "Нет «Ожидания» — на стоянке игра покажет первый кадр «Ходьбы».";
            }
            else if (clip != null && clip.IsActionSubstitute)
            {
                text = "Нет «" + CreatureAnimationLabels.ActionTitle(action) + "» — игра подставит «" +
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
                CreatureAnimationSetData animation = mapSet?.Animation;
                if (animation != null && animation.CanvasSize.x > 0 && animation.CanvasSize.y > 0)
                    return animation.CanvasSize;
                Sprite sprite = clip != null && clip.FrameCount > 0 ? clip.Frames[0] : standingFrame;
                return sprite != null ? sprite.rect.size : new Vector2(128f, 128f);
            }
        }

        private Vector2 Pivot => mapSet != null ? mapSet.Animation.Pivot : WorldMapAnimationSetData.DefaultPivot;

        // Ширина одной клетки карты в пикселях просмотра: высота холста =
        // «Рост на карте» клеток.
        private float CellWidthPixels
        {
            get
            {
                float height = mapSet != null ? mapSet.HeightInHexes : WorldMapAnimationSetData.DefaultHeightInHexes;
                return CanvasSize.y * zoom / Mathf.Max(0.01f, height);
            }
        }

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

        // Холст по центру окна; опора и шкала — поверх.
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
            spriteImage.style.left = (width - size.x) * 0.5f + offset.x * size.x;
            spriteImage.style.top = (height - size.y) * 0.5f - offset.y * size.y;
            spriteImage.style.width = size.x;
            spriteImage.style.height = size.y;

            Vector2 pivotPoint = PivotScreenPoint(width, height, size);
            pivotMarker.style.left = pivotPoint.x - 6f;
            pivotMarker.style.top = pivotPoint.y - 6f;
            pivotMarker.style.display = pivotToggle.value && mapSet != null ? DisplayStyle.Flex : DisplayStyle.None;
            pivotMarker.pickingMode = AllowPivotEditing && mapSet != null ? PickingMode.Position : PickingMode.Ignore;

            float cell = CellWidthPixels;
            cellLabel.style.left = pivotPoint.x - cell * 0.5f;
            cellLabel.style.top = pivotPoint.y + 8f;
            cellLabel.style.display = groundToggle.value && mapSet != null ? DisplayStyle.Flex : DisplayStyle.None;
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
            else if (standingFrame != null)
                sprite = standingFrame;
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

        // Линия земли, шкала в одну ширину клетки карты и стрелка сектора:
        // стрелка должна совпадать со взглядом фигуры (проверка таблицы ракурсов).
        private void DrawOverlay(MeshGenerationContext context)
        {
            if (!groundToggle.value || mapSet == null)
                return;
            float width = viewport.resolvedStyle.width;
            float height = viewport.resolvedStyle.height;
            if (float.IsNaN(width) || width <= 1f)
                return;

            Vector2 size = CanvasSize * zoom;
            Vector2 center = PivotScreenPoint(width, height, size);
            float cell = CellWidthPixels;
            Painter2D painter = context.painter2D;

            painter.strokeColor = new Color(0.45f, 0.75f, 0.85f, 0.55f);
            painter.lineWidth = 1f;
            painter.BeginPath();
            painter.MoveTo(new Vector2(0f, center.y));
            painter.LineTo(new Vector2(width, center.y));
            painter.Stroke();

            // Шкала: ширина одной клетки карты под опорой.
            painter.strokeColor = new Color(0.95f, 0.85f, 0.45f, 0.95f);
            painter.lineWidth = 2f;
            painter.BeginPath();
            painter.MoveTo(new Vector2(center.x - cell * 0.5f, center.y + 4f));
            painter.LineTo(new Vector2(center.x + cell * 0.5f, center.y + 4f));
            painter.MoveTo(new Vector2(center.x - cell * 0.5f, center.y));
            painter.LineTo(new Vector2(center.x - cell * 0.5f, center.y + 8f));
            painter.MoveTo(new Vector2(center.x + cell * 0.5f, center.y));
            painter.LineTo(new Vector2(center.x + cell * 0.5f, center.y + 8f));
            painter.Stroke();

            if (database == null)
                return;
            WorldMapFacing facing = database.GetFacing(clip != null ? clip.Direction : direction);
            Vector2 arrow = FacingScreenVector(facing) * cell * 0.5f;
            painter.strokeColor = new Color(1f, 1f, 1f, 0.9f);
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

        // Экранный единичный вектор сектора карты (ось Y вниз): сектор 0 —
        // вниз, дальше через 60° против часовой стрелки — по тому же правилу,
        // что WorldMapFacingLabels.FromScreenDirection.
        public static Vector2 FacingScreenVector(WorldMapFacing facing)
        {
            float heading = (180f - 60f * (int)facing) * Mathf.Deg2Rad;
            return new Vector2(Mathf.Sin(heading), -Mathf.Cos(heading));
        }

        private void BeginPivotDrag(PointerDownEvent evt)
        {
            if (!AllowPivotEditing || mapSet == null || evt.button != 0)
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
            CreatureAnimationClipData data = mapSet?.Animation.FindClip(value);
            string suffix = data != null && data.HasAnyFrames ? string.Empty : " (нет)";
            return CreatureAnimationLabels.ActionTitle(value) + suffix;
        }

        private string DirectionChoiceLabel(CreatureAnimationDirection value)
        {
            string arrow = database != null ? WorldMapFacingLabels.Arrow(database.GetFacing(value)) + " " : string.Empty;
            return arrow + CreatureAnimationLabels.DirectionFolder(value);
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

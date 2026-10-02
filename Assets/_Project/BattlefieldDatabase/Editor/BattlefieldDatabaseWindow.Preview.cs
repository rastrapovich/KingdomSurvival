using System.Collections.Generic;
using KingdomSurvival.BattleSandbox;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace KingdomSurvival.BattlefieldDatabase.Editor
{
    // Предпросмотр поля в кадре боя 16:9 и включение/отключение гексов.
    public sealed partial class BattlefieldDatabaseWindow
    {
        private static readonly Color DisabledFill = new Color(0.45f, 0.06f, 0.06f, 0.45f);
        private static readonly Color DisabledLine = new Color(0.95f, 0.36f, 0.30f, 0.90f);
        private static readonly Color HoverLine = new Color(1f, 1f, 1f, 0.85f);

        // Образцы состояний для предпросмотра — как в бою.
        private static readonly Vector2Int[] SampleDifficult = { new Vector2Int(3, 2), new Vector2Int(4, 2) };
        private static readonly Vector2Int[] SampleImpassable = { new Vector2Int(6, 4) };
        private static readonly Vector2Int[] SampleReachable = { new Vector2Int(2, 3), new Vector2Int(3, 3), new Vector2Int(2, 4) };
        private static readonly Vector2Int SampleTarget = new Vector2Int(7, 3);
        private static readonly Vector2Int SampleHover = new Vector2Int(6, 2);

        private BattlefieldView previewView;
        private CellOverlay overlay;
        private Label cellsInfo;
        private Label dropHint;

        private VisualElement BuildPreviewColumn()
        {
            VisualElement column = new VisualElement();
            column.style.flexGrow = 1f;
            column.style.flexShrink = 1f;
            column.style.paddingLeft = 8f;
            column.style.paddingRight = 4f;
            column.style.paddingTop = 8f;
            column.style.paddingBottom = 8f;

            cellsInfo = new Label();
            cellsInfo.style.marginBottom = 4f;
            cellsInfo.style.unityFontStyleAndWeight = FontStyle.Bold;
            column.Add(cellsInfo);

            VisualElement host = new VisualElement();
            host.style.flexGrow = 1f;
            host.style.minHeight = 240f;
            host.style.backgroundColor = new Color(0.07f, 0.075f, 0.085f, 1f);
            host.style.borderTopLeftRadius = 4f;
            host.style.borderTopRightRadius = 4f;
            host.style.borderBottomLeftRadius = 4f;
            host.style.borderBottomRightRadius = 4f;
            host.style.overflow = Overflow.Hidden;

            previewView = new BattlefieldView(false);
            SetAbsoluteFill(previewView);
            host.Add(previewView);

            overlay = new CellOverlay(this);
            SetAbsoluteFill(overlay);
            host.Add(overlay);
            previewView.LayoutChanged += overlay.MarkDirtyRepaint;

            dropHint = new Label("Перетащите сюда картинку поля");
            SetAbsoluteFill(dropHint);
            dropHint.pickingMode = PickingMode.Ignore;
            dropHint.style.unityTextAlign = TextAnchor.MiddleCenter;
            dropHint.style.fontSize = 16f;
            dropHint.style.color = new Color(0.75f, 0.75f, 0.75f, 0.8f);
            host.Add(dropHint);

            RegisterImageDrop(host, BackgroundFolder, false, sprites =>
            {
                if (!HasSelection)
                    return;
                serializedDatabase.Update();
                SelectedProperty.FindPropertyRelative("background").objectReferenceValue = sprites[0];
                serializedDatabase.ApplyModifiedProperties();
                ShowSelected();
            });
            column.Add(host);

            Label hint = new Label("Кадр 16:9 — так поле выглядит в бою при любом размере окна. " +
                                   "Перетащите картинку на кадр, чтобы заменить фон.");
            hint.style.whiteSpace = WhiteSpace.Normal;
            hint.style.fontSize = 10f;
            hint.style.color = MutedText;
            hint.style.marginTop = 4f;
            column.Add(hint);
            return column;
        }

        private static void SetAbsoluteFill(VisualElement element)
        {
            element.style.position = Position.Absolute;
            element.style.left = 0f;
            element.style.right = 0f;
            element.style.top = 0f;
            element.style.bottom = 0f;
        }

        private void RefreshPreview()
        {
            if (previewView == null || !HasSelection)
                return;
            BattlefieldDefinitionData field = SelectedField;
            previewView.Show(field, database.GetHexStyle(field));
            overlay.MarkDirtyRepaint();
            dropHint.style.display = field.Background == null ? DisplayStyle.Flex : DisplayStyle.None;

            int active = BattlefieldFrame.CountActiveCells(field);
            bool enough = active >= BattlefieldFrame.MinActiveCells;
            bool connected = enough && BattlefieldFrame.AreActiveCellsConnected(field);
            cellsInfo.text = "Доступно гексов: " + active + " из " + SandboxArenaShape.CellCount +
                             (!enough ? " — мало, нужно не меньше " + BattlefieldFrame.MinActiveCells
                                 : !connected ? " — поле разорвано, пешие бойцы не дойдут друг до друга" : string.Empty);
            cellsInfo.style.color = enough && connected ? (Color)new Color(0.85f, 0.85f, 0.85f, 1f) : WarningColor;
        }

        // ── Отключённые гексы ──────────────────────────────────────────────

        private void SetCellDisabled(int q, int r, bool disabled)
        {
            if (!HasSelection || !BattlefieldFrame.IsArenaCell(q, r))
                return;
            serializedDatabase.Update();
            SerializedProperty cells = SelectedProperty.FindPropertyRelative("disabledCells");
            int index = -1;
            for (int i = 0; i < cells.arraySize; i++)
            {
                Vector2Int cell = cells.GetArrayElementAtIndex(i).vector2IntValue;
                if (cell.x == q && cell.y == r)
                    index = i;
            }

            if (disabled && index < 0)
            {
                cells.arraySize++;
                cells.GetArrayElementAtIndex(cells.arraySize - 1).vector2IntValue = new Vector2Int(q, r);
            }
            else if (!disabled && index >= 0)
            {
                cells.DeleteArrayElementAtIndex(index);
            }
            else
            {
                return;
            }

            serializedDatabase.ApplyModifiedProperties();
            RefreshPreview();
        }

        private void EnableAllCells()
        {
            if (!HasSelection)
                return;
            serializedDatabase.Update();
            SelectedProperty.FindPropertyRelative("disabledCells").ClearArray();
            serializedDatabase.ApplyModifiedProperties();
            ScheduleRefresh();
        }

        private void InvertCells()
        {
            if (!HasSelection)
                return;
            BattlefieldDefinitionData field = SelectedField;
            List<Vector2Int> inverted = new List<Vector2Int>();
            foreach (HexCoord cell in SandboxArenaShape.Cells())
            {
                if (!field.IsCellDisabled(cell.Q, cell.R))
                    inverted.Add(new Vector2Int(cell.Q, cell.R));
            }

            serializedDatabase.Update();
            SerializedProperty cells = SelectedProperty.FindPropertyRelative("disabledCells");
            cells.ClearArray();
            for (int i = 0; i < inverted.Count; i++)
            {
                cells.arraySize++;
                cells.GetArrayElementAtIndex(i).vector2IntValue = inverted[i];
            }
            serializedDatabase.ApplyModifiedProperties();
            ScheduleRefresh();
        }

        // Слой правки над предпросмотром: отключённые гексы, образцы
        // состояний, наведение и кисть включения/отключения.
        private sealed class CellOverlay : VisualElement
        {
            private readonly BattlefieldDatabaseWindow window;
            private Vector2Int? hover;
            private Vector2Int? lastPainted;
            private bool painting;
            private bool paintDisabled;
            private int undoGroup;

            public CellOverlay(BattlefieldDatabaseWindow window)
            {
                this.window = window;
                pickingMode = PickingMode.Position;
                generateVisualContent += Draw;
                RegisterCallback<PointerDownEvent>(OnPointerDown);
                RegisterCallback<PointerMoveEvent>(OnPointerMove);
                RegisterCallback<PointerUpEvent>(OnPointerUp);
                RegisterCallback<PointerLeaveEvent>(_ =>
                {
                    hover = null;
                    MarkDirtyRepaint();
                });
            }

            private BattlefieldGridLayout Layout => window.previewView.Layout;

            private bool TryGetCell(Vector2 point, out Vector2Int cell)
            {
                cell = default;
                if (Layout.Size <= 0.01f || !Layout.TryGetCell(point, out int q, out int r))
                    return false;
                cell = new Vector2Int(q, r);
                return true;
            }

            private void OnPointerDown(PointerDownEvent evt)
            {
                if (evt.button != 0 || !window.HasSelection || !TryGetCell(evt.localPosition, out Vector2Int cell))
                    return;
                Undo.IncrementCurrentGroup();
                Undo.SetCurrentGroupName("Гексы поля боя");
                undoGroup = Undo.GetCurrentGroup();
                paintDisabled = !window.SelectedField.IsCellDisabled(cell.x, cell.y);
                painting = true;
                lastPainted = cell;
                window.SetCellDisabled(cell.x, cell.y, paintDisabled);
                this.CapturePointer(evt.pointerId);
                evt.StopPropagation();
            }

            private void OnPointerMove(PointerMoveEvent evt)
            {
                Vector2Int? current = TryGetCell(evt.localPosition, out Vector2Int cell) ? cell : (Vector2Int?)null;
                if (current != hover)
                {
                    hover = current;
                    MarkDirtyRepaint();
                }

                if (painting && current.HasValue && current != lastPainted)
                {
                    lastPainted = current;
                    window.SetCellDisabled(cell.x, cell.y, paintDisabled);
                }
            }

            private void OnPointerUp(PointerUpEvent evt)
            {
                if (!painting)
                    return;
                painting = false;
                lastPainted = null;
                this.ReleasePointer(evt.pointerId);
                Undo.CollapseUndoOperations(undoGroup);
                window.list?.RefreshItems();
            }

            private void Draw(MeshGenerationContext context)
            {
                if (!window.HasSelection || Layout.Size <= 0.01f)
                    return;

                BattlefieldDefinitionData field = window.SelectedField;
                BattlefieldHexStyle style = window.database.GetHexStyle(field);
                Painter2D painter = context.painter2D;
                float radius = Layout.Size * (1f - style.Gap);

                if (window.showStateSamples)
                {
                    foreach (Vector2Int cell in SampleDifficult)
                        FillCell(painter, field, cell, radius, style.DifficultColor, style);
                    foreach (Vector2Int cell in SampleImpassable)
                        FillCell(painter, field, cell, radius, style.ImpassableColor, style);
                    FillCell(painter, field, SampleTarget, radius, style.TargetColor, style);
                    foreach (Vector2Int cell in SampleReachable)
                    {
                        if (field.IsCellDisabled(cell.x, cell.y))
                            continue;
                        painter.strokeColor = style.ReachableColor;
                        painter.lineWidth = style.ReachableLineWidth;
                        BattlefieldFrame.FillHexPath(painter, Layout.GetCenter(cell.x, cell.y),
                            Mathf.Max(1f, radius - style.ReachableLineWidth * 0.5f), Layout.VerticalScale);
                        painter.Stroke();
                    }
                    if (!field.IsCellDisabled(SampleHover.x, SampleHover.y))
                    {
                        painter.fillColor = style.AttackHoverFill;
                        painter.strokeColor = style.AttackHoverLine;
                        painter.lineWidth = 3f;
                        BattlefieldFrame.FillHexPath(painter, Layout.GetCenter(SampleHover.x, SampleHover.y),
                            Mathf.Max(1f, radius - 2.5f), Layout.VerticalScale);
                        painter.Fill();
                        painter.Stroke();
                    }
                }

                // Отключённые гексы: красная штриховка с крестом.
                foreach (HexCoord cell in SandboxArenaShape.Cells())
                {
                    if (!field.IsCellDisabled(cell.Q, cell.R))
                        continue;
                    Vector2 center = Layout.GetCenter(cell.Q, cell.R);
                    painter.fillColor = DisabledFill;
                    painter.strokeColor = DisabledLine;
                    painter.lineWidth = 1.5f;
                    BattlefieldFrame.FillHexPath(painter, center, radius, Layout.VerticalScale);
                    painter.Fill();
                    painter.Stroke();
                    float arm = radius * 0.32f;
                    painter.BeginPath();
                    painter.MoveTo(center + new Vector2(-arm, -arm * Layout.VerticalScale));
                    painter.LineTo(center + new Vector2(arm, arm * Layout.VerticalScale));
                    painter.MoveTo(center + new Vector2(arm, -arm * Layout.VerticalScale));
                    painter.LineTo(center + new Vector2(-arm, arm * Layout.VerticalScale));
                    painter.Stroke();
                }

                if (hover.HasValue)
                {
                    painter.strokeColor = HoverLine;
                    painter.lineWidth = 2f;
                    BattlefieldFrame.FillHexPath(painter, Layout.GetCenter(hover.Value.x, hover.Value.y), radius,
                        Layout.VerticalScale);
                    painter.Stroke();
                }
            }

            private void FillCell(
                Painter2D painter,
                BattlefieldDefinitionData field,
                Vector2Int cell,
                float radius,
                Color fill,
                BattlefieldHexStyle style)
            {
                if (field.IsCellDisabled(cell.x, cell.y))
                    return;
                Vector2 center = Layout.GetCenter(cell.x, cell.y);
                painter.fillColor = fill;
                BattlefieldFrame.FillHexPath(painter, center, radius, Layout.VerticalScale);
                painter.Fill();
                Color line = style.Fade(style.LineColor);
                if (style.LineWidth > 0.01f && line.a > 0.001f)
                {
                    painter.strokeColor = line;
                    painter.lineWidth = style.LineWidth;
                    painter.Stroke();
                }
            }
        }
    }
}

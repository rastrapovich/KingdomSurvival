using UnityEditor;
using UnityEngine;

namespace KingdomSurvival.DialogueDatabase.Editor
{
    // "Читаемые ноды и удобные Свойства узла" — presentation-логика правой
    // панели Inspector'а: изменяемая ширина (сплиттер + EditorPrefs),
    // отказ от горизонтального ScrollView, вертикальный layout для длинных
    // ID-полей и правила видимости нерелевантных полей. Чистые
    // classification-функции (Resolve.../ShouldShow...) не зависят от
    // SerializedProperty/IMGUI и проверяемы в EditMode; ниже них —
    // Editor-only рисование, использующее их.
    public sealed partial class DialogueDatabaseWindow
    {
        public enum DialogueEditorLayoutMode
        {
            Normal,
            NarrowInspector
        }

        private const string GraphInspectorWidthPrefKey = "KingdomSurvival.DialogueDatabase.GraphInspectorWidth";
        private const float GraphInspectorMinWidth = 360f;
        private const float GraphInspectorDefaultWidth = 480f;
        private const float GraphInspectorMaxWidthFraction = 0.6f;
        private const float GraphInspectorSplitterWidth = 6f;
        private const float GraphInspectorContentPadding = 18f;

        // §41: ниже этой ширины длинные ID-поля переходят в вертикальный
        // layout (подпись сверху, поле на всю ширину снизу). В «Таблице»
        // (много места) остаётся прежний горизонтальный PropertyField —
        // регрессии там не будет, так как Normal-режим просто вызывает
        // тот же API, что и раньше.
        private const float NarrowInspectorWidthThreshold = 560f;

        private float graphInspectorWidth = GraphInspectorDefaultWidth;
        private bool graphInspectorResizing;

        // Общее для DrawNode/DrawChoice/DrawTextBlock/DrawConditionGroup/
        // DrawCheckSpec/DrawEffectsList состояние текущего прохода отрисовки
        // (аналогично уже существующим graphZoom/graphSelectedNodeIndex) —
        // так не пришлось протаскивать параметр через десяток сигнатур,
        // используемых и «Таблицей», и панелью «Граф».
        private DialogueEditorLayoutMode inspectorLayoutMode = DialogueEditorLayoutMode.Normal;

        private void LoadGraphInspectorWidth()
        {
            graphInspectorWidth = EditorPrefs.GetFloat(GraphInspectorWidthPrefKey, GraphInspectorDefaultWidth);
        }

        private void SaveGraphInspectorWidth()
        {
            EditorPrefs.SetFloat(GraphInspectorWidthPrefKey, graphInspectorWidth);
        }

        // §17: минимум 360, максимум ~50-60% окна, иначе на узких окнах
        // Inspector мог бы съесть весь canvas.
        public static float ClampInspectorWidth(float width, float windowWidth)
        {
            float max = Mathf.Max(GraphInspectorMinWidth, windowWidth * GraphInspectorMaxWidthFraction);
            return Mathf.Clamp(width, GraphInspectorMinWidth, max);
        }

        public static DialogueEditorLayoutMode ResolveLayoutMode(float availableWidth)
        {
            return availableWidth < NarrowInspectorWidthThreshold
                ? DialogueEditorLayoutMode.NarrowInspector
                : DialogueEditorLayoutMode.Normal;
        }

        public static bool ShouldStackLongField(DialogueEditorLayoutMode mode)
        {
            return mode == DialogueEditorLayoutMode.NarrowInspector;
        }

        // §28: какие поля ответа релевантны его виду — вырезает лишние
        // Success/Failure/Check у обычного ответа и обычный Target у
        // активной проверки/EXIT.
        public static bool ShouldShowActiveCheckFields(DialogueChoiceKind kind)
        {
            return kind == DialogueChoiceKind.ActiveReturnable || kind == DialogueChoiceKind.ActiveDecisive;
        }

        public static bool ShouldShowNormalTargetField(DialogueChoiceKind kind)
        {
            return kind != DialogueChoiceKind.Exit && !ShouldShowActiveCheckFields(kind);
        }

        // ------------------------------------------------------------------
        // Рисование (Editor-only)
        // ------------------------------------------------------------------

        private void DrawGraphInspectorSplitter()
        {
            Rect splitterRect = GUILayoutUtility.GetRect(
                GraphInspectorSplitterWidth, GraphInspectorSplitterWidth, 10f, 10000f, GUILayout.ExpandHeight(true));

            EditorGUI.DrawRect(
                splitterRect,
                EditorGUIUtility.isProSkin ? new Color(0f, 0f, 0f, 0.45f) : new Color(0f, 0f, 0f, 0.18f));
            EditorGUIUtility.AddCursorRect(splitterRect, MouseCursor.ResizeHorizontal);

            Event current = Event.current;
            if (current.type == EventType.MouseDown && splitterRect.Contains(current.mousePosition))
            {
                graphInspectorResizing = true;
                GUI.FocusControl(null);
                current.Use();
            }

            if (current.type == EventType.MouseDrag && graphInspectorResizing)
            {
                // Сплиттер левее Inspector'а: смещение мыши влево (delta.x
                // отрицателен) должно РАСШИРЯТЬ панель.
                graphInspectorWidth = ClampInspectorWidth(graphInspectorWidth - current.delta.x, position.width);
                current.Use();
                Repaint();
            }

            if (current.type == EventType.MouseUp && graphInspectorResizing)
            {
                graphInspectorResizing = false;
                SaveGraphInspectorWidth();
                current.Use();
            }
        }

        private static void DrawSectionHeader(string title)
        {
            GUILayout.Space(10f);
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
            GUILayout.Space(2f);
        }

        // §19-21: длинный ID — в узком Inspector'е подпись сверху и поле на
        // всю ширину снизу (без горизонтального "label | ооочень длинное
        // значение"); в «Таблице»/широком Inspector'е — прежний
        // PropertyField, поведение не меняется.
        private void DrawLongIdField(SerializedProperty stringProperty, string label)
        {
            if (ShouldStackLongField(inspectorLayoutMode))
            {
                EditorGUILayout.LabelField(label, EditorStyles.miniBoldLabel);
                stringProperty.stringValue = EditorGUILayout.TextField(stringProperty.stringValue);
                GUILayout.Space(2f);
            }
            else
            {
                EditorGUILayout.PropertyField(stringProperty, new GUIContent(label));
            }
        }
    }
}

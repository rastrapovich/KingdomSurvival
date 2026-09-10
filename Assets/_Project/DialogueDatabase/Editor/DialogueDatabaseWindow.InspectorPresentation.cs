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

        // §28 инструкции "полноценное редактирование нод": приблизительная
        // доступная ширина содержимого — нужна, чтобы посчитать
        // CalcHeight авто-высокого TextArea. В узком Inspector'е известна
        // точно (graphInspectorWidth); в «Таблице» — консервативная
        // константа (там нет горизонтальной проблемы, точность не критична).
        private float inspectorContentWidth = 700f;

        // Свёрнутость технической секции "УСТАРЕВШИЕ ДАННЫЕ" — сессионное
        // состояние окна, не часть Dialogue asset (§34).
        private bool legacyTextFoldout;

        // §26 инструкции "полноценное редактирование нод": колесо мыши над
        // Inspector-панелью прокручивает eё независимо от того, какая нода
        // выбрана и что происходит на canvas.
        private const float GraphInspectorScrollWheelSpeed = 20f;

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

        // §28: полноценный авто-высокий TextArea вместо PropertyField с
        // атрибутом TextArea(min,max) — тот атрибут ограничивает видимую
        // высоту и заводит СОБСТВЕННЫЙ внутренний scrollbar при превышении
        // (именно это и мешало прокрутке всей панели колёсиком). Здесь
        // высота считается по факту содержимого (CalcHeight) и всегда
        // вмещает весь текст — прокручивается сама панель, а не поле.
        private void DrawAutoHeightNarrativeText(SerializedProperty textProperty, string label, float minHeight = 40f)
        {
            if (!string.IsNullOrEmpty(label))
                EditorGUILayout.LabelField(label, EditorStyles.miniBoldLabel);

            GUIStyle style = new GUIStyle(EditorStyles.textArea) { wordWrap = true };
            string content = textProperty.stringValue;
            float measuredWidth = Mathf.Max(60f, inspectorContentWidth);
            float height = Mathf.Max(
                minHeight,
                style.CalcHeight(new GUIContent(string.IsNullOrEmpty(content) ? " " : content), measuredWidth));

            EditorGUI.BeginChangeCheck();
            string newValue = EditorGUILayout.TextArea(content, style, GUILayout.Height(height));
            if (EditorGUI.EndChangeCheck())
            {
                textProperty.stringValue = newValue;
                EditorUtility.SetDirty(database);
            }
        }

        // §15-16: крупная карточка выбранного узла — портрет говорящего
        // (тот же источник, что и у игрового диалога: FindSpeaker →
        // Portrait) слева, имя/роль/ID справа. Если у говорящего нет
        // портрета — аккуратный текстовый placeholder вместо пустого поля.
        private void DrawGraphInspectorPortraitHeader(SerializedProperty node)
        {
            string speakerId = node.FindPropertyRelative("speakerId").stringValue;
            DialogueSpeakerData speaker = database.FindSpeaker(speakerId);
            string nodeId = node.FindPropertyRelative("id").stringValue;

            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);

            const float portraitSize = 64f;
            Rect portraitRect = GUILayoutUtility.GetRect(
                portraitSize, portraitSize, GUILayout.Width(portraitSize), GUILayout.Height(portraitSize));

            if (speaker != null && speaker.Portrait != null)
            {
                Texture2D preview = AssetPreview.GetAssetPreview(speaker.Portrait);
                if (preview == null)
                    preview = speaker.Portrait.texture;
                GUI.DrawTexture(portraitRect, preview, ScaleMode.ScaleToFit, true);
            }
            else
            {
                EditorGUI.DrawRect(
                    portraitRect,
                    EditorGUIUtility.isProSkin ? new Color(1f, 1f, 1f, 0.08f) : new Color(0f, 0f, 0f, 0.08f));
                string placeholder = speaker != null ? speaker.DisplayName.ToUpperInvariant() : "?";
                GUIStyle placeholderStyle = new GUIStyle(EditorStyles.miniBoldLabel)
                {
                    alignment = TextAnchor.MiddleCenter,
                    wordWrap = true
                };
                GUI.Label(portraitRect, placeholder, placeholderStyle);
            }

            GUILayout.Space(8f);
            EditorGUILayout.BeginVertical();
            GUILayout.FlexibleSpace();

            GUIStyle mutedStyle = new GUIStyle(EditorStyles.miniLabel);
            mutedStyle.normal.textColor = EditorGUIUtility.isProSkin
                ? new Color(0.6f, 0.6f, 0.58f, 1f)
                : new Color(0.42f, 0.42f, 0.4f, 1f);

            if (speaker != null)
            {
                EditorGUILayout.LabelField(speaker.DisplayName, EditorStyles.boldLabel);
                if (!string.IsNullOrWhiteSpace(speaker.Role))
                    EditorGUILayout.LabelField(speaker.Role, EditorStyles.miniLabel);
                EditorGUILayout.LabelField(speaker.Id, mutedStyle);
            }
            else
            {
                EditorGUILayout.LabelField(
                    string.IsNullOrWhiteSpace(speakerId) ? "<нет говорящего>" : "? " + speakerId,
                    EditorStyles.boldLabel);
            }

            EditorGUILayout.LabelField(string.IsNullOrWhiteSpace(nodeId) ? "<без ID>" : nodeId, mutedStyle);

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();
        }

        // §26/§29-30: явная маршрутизация ScrollWheel — Inspector всегда
        // забирает колесо мыши над своим прямоугольником, независимо от
        // выбранного узла и от того, что показывает graph.HandleGraphInput
        // для canvas. panelRect передаётся вызывающей стороной (результат
        // EditorGUILayout.BeginVertical(GUIStyle, ...) для всей панели).
        private void HandleGraphInspectorScrollWheel(Rect panelRect)
        {
            Event current = Event.current;
            if (current.type != EventType.ScrollWheel)
                return;
            if (!panelRect.Contains(current.mousePosition))
                return;

            graphInspectorScroll.y = Mathf.Max(0f, graphInspectorScroll.y + current.delta.y * GraphInspectorScrollWheelSpeed);
            current.Use();
            Repaint();
        }
    }
}

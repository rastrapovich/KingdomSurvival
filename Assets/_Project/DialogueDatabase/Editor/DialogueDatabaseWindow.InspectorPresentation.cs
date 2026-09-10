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
        // §38 инструкции "свободный граф": семантические карточки (заголовок
        // + акцентная полоса + текст условий/эффектов) требуют больше места,
        // чем голый PropertyField — подняли минимум и ширину по умолчанию,
        // чтобы они не переносились через строку на типичном окне.
        private const float GraphInspectorMinWidth = 420f;
        private const float GraphInspectorDefaultWidth = 520f;
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

        // §18-25 инструкции "свободный граф": семь смысловых категорий
        // Inspector'а, каждая — свой акцентный цвет. Это не фон карточки
        // (полная заливка резала бы читаемость длинного текста), а тонкая
        // полоса слева (см. EndSemanticCard) и цвет заголовка секции —
        // "акцентная линия, а не сплошная заливка".
        public enum SemanticCategory
        {
            Character,
            Text,
            Check,
            Condition,
            Effect,
            Choice,
            Error
        }

        // Тема-зависимая палитра (§18): на тёмной теме цвета чуть светлее и
        // менее насыщенные, чтобы не резать глаз на тёмном фоне карточки; на
        // светлой — темнее и насыщеннее, чтобы не терялись на белом.
        // Condition (охра) и Effect (зелёный) — сознательно "визуально
        // противоположные" категории (§25: условия — то, что ПРОВЕРЯЕТСЯ,
        // эффекты — то, что МЕНЯЕТСЯ).
        public static Color GetSemanticAccentColor(SemanticCategory category)
        {
            bool dark = EditorGUIUtility.isProSkin;
            switch (category)
            {
                case SemanticCategory.Character:
                    return dark ? new Color(0.55f, 0.68f, 0.92f) : new Color(0.20f, 0.36f, 0.68f);
                case SemanticCategory.Text:
                    return dark ? new Color(0.80f, 0.80f, 0.78f) : new Color(0.30f, 0.30f, 0.28f);
                case SemanticCategory.Check:
                    return dark ? new Color(0.78f, 0.62f, 0.92f) : new Color(0.46f, 0.28f, 0.62f);
                case SemanticCategory.Condition:
                    return dark ? new Color(0.85f, 0.72f, 0.30f) : new Color(0.62f, 0.48f, 0.06f);
                case SemanticCategory.Effect:
                    return dark ? new Color(0.52f, 0.82f, 0.52f) : new Color(0.18f, 0.52f, 0.20f);
                case SemanticCategory.Choice:
                    return dark ? new Color(0.48f, 0.70f, 0.90f) : new Color(0.14f, 0.42f, 0.68f);
                case SemanticCategory.Error:
                    return dark ? new Color(0.92f, 0.55f, 0.40f) : new Color(0.70f, 0.24f, 0.10f);
                default:
                    return dark ? Color.white : Color.black;
            }
        }

        private static void DrawSectionHeader(string title)
        {
            GUILayout.Space(10f);
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
            GUILayout.Space(2f);
        }

        // Цветной вариант — для секций с явной смысловой категорией (условия,
        // эффекты, проверки, ответы), а не для общих технических блоков.
        private static void DrawSectionHeader(string title, SemanticCategory category)
        {
            GUILayout.Space(10f);
            GUIStyle style = new GUIStyle(EditorStyles.boldLabel);
            style.normal.textColor = GetSemanticAccentColor(category);
            EditorGUILayout.LabelField(title, style);
            GUILayout.Space(2f);
        }

        // §18-19: карточка с акцентной полосой слева вместо сплошной
        // заливки фона. Полоса рисуется ПОСЛЕ EndVertical — тот же
        // проверенный приём "GetLastRect() сразу после EndVertical",
        // которым уже пользуется ScrollWheel-роутинг Inspector'а — без
        // риска положиться на непроверенный Rect-возвращающий overload
        // BeginVertical(GUIStyle, ...).
        private static void BeginSemanticCard(string title, SemanticCategory category)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Space(1f);
            if (!string.IsNullOrEmpty(title))
            {
                GUIStyle headerStyle = new GUIStyle(EditorStyles.miniBoldLabel);
                headerStyle.normal.textColor = GetSemanticAccentColor(category);
                EditorGUILayout.LabelField(title, headerStyle);
            }
        }

        private static void EndSemanticCard(SemanticCategory category)
        {
            EditorGUILayout.EndVertical();
            Rect cardRect = GUILayoutUtility.GetLastRect();
            Rect accentRect = new Rect(cardRect.x, cardRect.y, 3f, cardRect.height);
            EditorGUI.DrawRect(accentRect, GetSemanticAccentColor(category));
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

using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEngine;

namespace KingdomSurvival.DialogueDatabase.Editor
{
    public sealed partial class DialogueDatabaseWindow : EditorWindow
    {
        private const float GraphPadding = 10f;
        private const float GraphGrid = 64f;

        // Активная проверка имеет два порта вместо одного (§13): зелёный
        // успех и красный провал. graphConnectingPortKind запоминает, какой
        // порт сейчас перетаскивается, чтобы завершение перетаскивания
        // записало результат в нужное поле (successNodeId/failureNodeId
        // вместо nextNodeId).
        private enum GraphChoicePortKind
        {
            Normal,
            Success,
            Failure
        }

        private Vector2 graphPan = new Vector2(40f, 40f);
        private Vector2 graphCanvasSize = new Vector2(700f, 500f);
        private float graphZoom = 1f;
        private int graphSelectedNodeIndex = -1;
        private int graphDraggedNodeIndex = -1;
        private bool graphPanning;
        private int graphConnectingNodeIndex = -1;
        private int graphConnectingChoiceIndex = -1;
        private GraphChoicePortKind graphConnectingPortKind = GraphChoicePortKind.Normal;
        private bool graphNeedsCenter = true;
        private Vector2 graphInspectorScroll;
        private int graphInspectorLastNodeIndex = -1;

        // §3 инструкции по информативным нодам: единственный источник
        // истины по геометрии узла для ЭТОГО кадра отрисовки. И отрисовка
        // (DrawGraphNode/DrawGraphChoice), и hit-test/центры портов
        // (GetNodeScreenRect/GetChoicePortCenter/FindNodeAt) читают эти же
        // словари — раскладка не пересчитывается дважды по разным формулам.
        private readonly Dictionary<int, GraphNodeInfo> graphNodeInfoByIndex = new Dictionary<int, GraphNodeInfo>();
        private readonly Dictionary<int, GraphNodeLayoutMetrics> graphNodeMetricsByIndex = new Dictionary<int, GraphNodeLayoutMetrics>();

        private void ResetGraphViewState()
        {
            graphPan = new Vector2(40f, 40f);
            graphZoom = 1f;
            graphSelectedNodeIndex = -1;
            graphDraggedNodeIndex = -1;
            graphPanning = false;
            graphConnectingNodeIndex = -1;
            graphConnectingChoiceIndex = -1;
            graphConnectingPortKind = GraphChoicePortKind.Normal;
            graphNeedsCenter = true;
            graphInspectorScroll = Vector2.zero;
            graphInspectorLastNodeIndex = -1;
        }

        private void DrawDialogueGraph(SerializedProperty dialogue)
        {
            SerializedProperty nodes = dialogue.FindPropertyRelative("nodes");

            HashSet<string> reachable = CollectReachableNodeIds(dialogue);
            RefreshGraphNodeMetrics(dialogue, nodes, reachable);

            EnsureGraphPositions(dialogue);
            DrawGraphToolbar(dialogue, nodes);

            EditorGUILayout.BeginHorizontal();

            Rect canvasRect = GUILayoutUtility.GetRect(
                180f,
                10000f,
                320f,
                10000f,
                GUILayout.ExpandWidth(true),
                GUILayout.ExpandHeight(true));
            graphCanvasSize = canvasRect.size;

            EditorGUI.DrawRect(
                canvasRect,
                EditorGUIUtility.isProSkin
                    ? new Color(0.105f, 0.11f, 0.12f, 1f)
                    : new Color(0.84f, 0.85f, 0.86f, 1f));

            GUI.BeginGroup(canvasRect);
            Rect localCanvas = new Rect(Vector2.zero, canvasRect.size);

            if (graphNeedsCenter && nodes.arraySize > 0)
            {
                CenterGraph(dialogue, localCanvas);
                graphNeedsCenter = false;
            }

            DrawGraphGrid(localCanvas);
            HandleGraphInput(dialogue, nodes, localCanvas);
            DrawGraphConnections(nodes);
            DrawGraphNodes(dialogue, nodes, reachable);
            DrawPendingConnection(nodes);

            GUI.EndGroup();

            DrawGraphInspectorSplitter();
            DrawGraphInspectorPanel(dialogue, nodes);

            EditorGUILayout.EndHorizontal();
        }

        // Раскладка/бейджи/предупреждения считаются один раз в начале кадра
        // (DialogueDatabaseWindow.GraphPresentation.cs — чистая арифметика,
        // без GUIStyle), и дальше и отрисовка, и геометрия портов/hit-test
        // читают готовый результат по индексу узла.
        private void RefreshGraphNodeMetrics(SerializedProperty dialogue, SerializedProperty nodes, HashSet<string> reachable)
        {
            graphNodeInfoByIndex.Clear();
            graphNodeMetricsByIndex.Clear();

            string startNodeId = dialogue.FindPropertyRelative("startNodeId").stringValue;
            HashSet<string> allNodeIds = new HashSet<string>(BuildNodeIndexMap(nodes).Keys, StringComparer.Ordinal);

            for (int i = 0; i < nodes.arraySize; i++)
            {
                SerializedProperty node = nodes.GetArrayElementAtIndex(i);
                string nodeId = node.FindPropertyRelative("id").stringValue;
                bool isStart = string.Equals(nodeId, startNodeId, StringComparison.Ordinal);
                bool isNodeReachable = string.IsNullOrWhiteSpace(nodeId) || reachable.Contains(nodeId);

                GraphNodeInfo info = BuildGraphNodeInfoFromProperty(node, isStart, isNodeReachable);
                bool isSelected = graphSelectedNodeIndex == i;
                GraphNodeLayoutMetrics metrics = ComputeNodeLayoutMetrics(info, allNodeIds, graphDetailMode, isSelected);

                graphNodeInfoByIndex[i] = info;
                graphNodeMetricsByIndex[i] = metrics;
            }
        }

        private GraphNodeLayoutMetrics GetNodeMetrics(int nodeIndex)
        {
            if (graphNodeMetricsByIndex.TryGetValue(nodeIndex, out GraphNodeLayoutMetrics metrics))
                return metrics;

            // Рассинхронизация массива nodes и кэша в пределах одного кадра
            // (например, узел только что удалён) — безопасный фолбэк вместо
            // исключения.
            return new GraphNodeLayoutMetrics
            {
                Width = GetGraphNodeWidth(graphDetailMode),
                TotalHeight = 200f
            };
        }

        // Панель свойств выбранного узла в режиме «Граф» (§18 доработки,
        // §27 инструкции по информативным нодам, §16-31 инструкции по
        // читаемым нодам): текстовые блоки, условия, проверки, эффекты и
        // переходы редактируются ТОЛЬКО здесь — тот же DrawNode/DrawChoice/
        // DrawTextBlock, что и в режиме «Таблица». Карточки на холсте —
        // только чтение (см. DrawGraphNode/DrawGraphChoice).
        //
        // Ширина панели — изменяемая (DrawGraphInspectorSplitter,
        // graphInspectorWidth, EditorPrefs), горизонтального ScrollView нет
        // вообще (GUIStyle.none для horizontalScrollbar, §18) — длинные поля
        // адаптируются через inspectorLayoutMode/DrawLongIdField, а не через
        // прокрутку вбок.
        private void DrawGraphInspectorPanel(SerializedProperty dialogue, SerializedProperty nodes)
        {
            inspectorLayoutMode = ResolveLayoutMode(graphInspectorWidth);

            EditorGUILayout.BeginVertical(GUILayout.Width(graphInspectorWidth), GUILayout.ExpandHeight(true));
            EditorGUILayout.LabelField("СВОЙСТВА УЗЛА", EditorStyles.boldLabel);

            if (graphSelectedNodeIndex < 0 || graphSelectedNodeIndex >= nodes.arraySize)
            {
                EditorGUILayout.HelpBox(
                    "Выберите узел на холсте, чтобы отредактировать его текстовые блоки, ответы, условия, проверки и эффекты — так же, как в «Таблице».",
                    MessageType.Info);
                EditorGUILayout.EndVertical();
                return;
            }

            SerializedProperty node = nodes.GetArrayElementAtIndex(graphSelectedNodeIndex);

            // §30: заголовок панели дополнен ID узла и именем говорящего —
            // видно, что именно редактируется, не открывая сам узел на холсте.
            string nodeId = node.FindPropertyRelative("id").stringValue;
            string speakerId = node.FindPropertyRelative("speakerId").stringValue;
            DialogueSpeakerData speaker = database.FindSpeaker(speakerId);
            string headerLine = string.IsNullOrWhiteSpace(nodeId) ? "<без ID>" : nodeId;
            if (speaker != null)
                headerLine += "  ·  " + speaker.DisplayName;
            EditorGUILayout.LabelField(headerLine, EditorStyles.miniLabel);

            if (graphInspectorLastNodeIndex != graphSelectedNodeIndex)
            {
                node.isExpanded = true;
                graphInspectorLastNodeIndex = graphSelectedNodeIndex;
            }

            graphInspectorScroll = EditorGUILayout.BeginScrollView(
                graphInspectorScroll, GUIStyle.none, GUI.skin.verticalScrollbar);
            EditorGUILayout.BeginVertical(GUILayout.Width(graphInspectorWidth - GraphInspectorContentPadding));
            DrawNode(dialogue, nodes, graphSelectedNodeIndex);
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndScrollView();

            EditorGUILayout.EndVertical();
        }

        private void DrawGraphToolbar(SerializedProperty dialogue, SerializedProperty nodes)
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            if (GUILayout.Button("+ Узел", EditorStyles.toolbarButton, GUILayout.Width(70f)))
            {
                Undo.RecordObject(database, "Add Dialogue Node");
                Vector2 centerWorld = ScreenToGraph(graphCanvasSize * 0.5f);
                AddNodeAtPosition(dialogue, centerWorld);
                graphSelectedNodeIndex = nodes.arraySize - 1;
                EditorUtility.SetDirty(database);
            }

            if (GUILayout.Button("Автораскладка", EditorStyles.toolbarButton, GUILayout.Width(105f)))
            {
                AutoLayoutDialogue(dialogue, true);
                graphNeedsCenter = true;
            }

            if (GUILayout.Button("Центр", EditorStyles.toolbarButton, GUILayout.Width(60f)))
                graphNeedsCenter = true;

            GUILayout.Space(8f);

            DrawGraphDetailModeSelector();

            GUILayout.Space(8f);
            GUILayout.Label(
                "Перетаскивай ноды · тяни жёлтый порт ответа, зелёный порт успеха или красный порт провала на нужный нод · выбери узел, чтобы открыть его свойства справа · колесо = масштаб · Alt+ЛКМ/СКМ = поле",
                EditorStyles.miniLabel);

            GUILayout.FlexibleSpace();

            if (GUILayout.Button("−", EditorStyles.toolbarButton, GUILayout.Width(24f)))
                SetGraphZoom(graphZoom / 1.15f, graphCanvasSize * 0.5f);
            GUILayout.Label(Mathf.RoundToInt(graphZoom * 100f) + "%", EditorStyles.miniLabel, GUILayout.Width(40f));
            if (GUILayout.Button("+", EditorStyles.toolbarButton, GUILayout.Width(24f)))
                SetGraphZoom(graphZoom * 1.15f, graphCanvasSize * 0.5f);

            EditorGUILayout.EndHorizontal();
        }

        // §4/§36: три режима детализации, хранятся в EditorPrefs (не в
        // ассете) — это личная настройка автора графа, а не данные диалога.
        private static readonly string[] GraphDetailModeLabels = { "Компактно", "Стандарт", "Полно" };

        private void DrawGraphDetailModeSelector()
        {
            int current = (int)graphDetailMode;
            int next = GUILayout.Toolbar(current, GraphDetailModeLabels, EditorStyles.toolbarButton, GUILayout.Width(210f));
            if (next != current)
                SetGraphDetailMode((GraphDetailMode)next);
        }

        private void EnsureGraphPositions(SerializedProperty dialogue)
        {
            SerializedProperty nodes = dialogue.FindPropertyRelative("nodes");
            bool needsLayout = false;
            for (int i = 0; i < nodes.arraySize; i++)
            {
                SerializedProperty hasPosition = nodes.GetArrayElementAtIndex(i).FindPropertyRelative("hasEditorPosition");
                if (hasPosition == null || !hasPosition.boolValue)
                {
                    needsLayout = true;
                    break;
                }
            }

            if (!needsLayout)
                return;

            AutoLayoutDialogue(dialogue, false);
            graphNeedsCenter = true;
        }

        private void DrawGraphGrid(Rect canvas)
        {
            float spacing = Mathf.Max(18f, GraphGrid * graphZoom);
            Color minor = EditorGUIUtility.isProSkin
                ? new Color(1f, 1f, 1f, 0.035f)
                : new Color(0f, 0f, 0f, 0.05f);

            float startX = Mathf.Repeat(graphPan.x, spacing);
            for (float x = startX; x < canvas.width; x += spacing)
                EditorGUI.DrawRect(new Rect(x, 0f, 1f, canvas.height), minor);

            float startY = Mathf.Repeat(graphPan.y, spacing);
            for (float y = startY; y < canvas.height; y += spacing)
                EditorGUI.DrawRect(new Rect(0f, y, canvas.width, 1f), minor);
        }

        private void HandleGraphInput(
            SerializedProperty dialogue,
            SerializedProperty nodes,
            Rect canvas)
        {
            Event current = Event.current;
            Vector2 mouse = current.mousePosition;

            if (current.type == EventType.ScrollWheel && canvas.Contains(mouse))
            {
                float factor = Mathf.Pow(1.08f, -current.delta.y);
                SetGraphZoom(graphZoom * factor, mouse);
                current.Use();
                Repaint();
                return;
            }

            if (current.type == EventType.MouseDown && canvas.Contains(mouse))
            {
                if (current.button == 2 || (current.button == 0 && current.alt))
                {
                    graphPanning = true;
                    graphDraggedNodeIndex = -1;
                    GUI.FocusControl(null);
                    current.Use();
                    return;
                }

                if (current.button == 0 && FindNodeAt(nodes, mouse) < 0)
                {
                    graphSelectedNodeIndex = -1;
                    GUI.FocusControl(null);
                }
            }

            if (current.type == EventType.MouseDrag)
            {
                if (graphPanning)
                {
                    graphPan += current.delta;
                    current.Use();
                    Repaint();
                    return;
                }

                if (graphDraggedNodeIndex >= 0 &&
                    graphDraggedNodeIndex < nodes.arraySize)
                {
                    SerializedProperty node = nodes.GetArrayElementAtIndex(graphDraggedNodeIndex);
                    SerializedProperty position = node.FindPropertyRelative("editorPosition");
                    if (position != null)
                    {
                        position.vector2Value += current.delta / Mathf.Max(0.01f, graphZoom);
                        SerializedProperty hasPosition = node.FindPropertyRelative("hasEditorPosition");
                        if (hasPosition != null)
                            hasPosition.boolValue = true;
                        EditorUtility.SetDirty(database);
                    }

                    current.Use();
                    Repaint();
                    return;
                }

                if (graphConnectingNodeIndex >= 0)
                {
                    current.Use();
                    Repaint();
                }
            }

            if (current.type != EventType.MouseUp)
                return;

            if (graphPanning)
            {
                graphPanning = false;
                current.Use();
                return;
            }

            if (graphDraggedNodeIndex >= 0)
            {
                graphDraggedNodeIndex = -1;
                current.Use();
                return;
            }

            if (graphConnectingNodeIndex < 0)
                return;

            int sourceNodeIndex = graphConnectingNodeIndex;
            int sourceChoiceIndex = graphConnectingChoiceIndex;
            GraphChoicePortKind portKind = graphConnectingPortKind;
            int targetNodeIndex = FindNodeAt(nodes, mouse);
            graphConnectingNodeIndex = -1;
            graphConnectingChoiceIndex = -1;
            graphConnectingPortKind = GraphChoicePortKind.Normal;

            if (targetNodeIndex >= 0 &&
                sourceNodeIndex >= 0 &&
                sourceNodeIndex < nodes.arraySize)
            {
                SerializedProperty sourceNode = nodes.GetArrayElementAtIndex(sourceNodeIndex);
                SerializedProperty choices = sourceNode.FindPropertyRelative("choices");
                if (sourceChoiceIndex >= 0 && sourceChoiceIndex < choices.arraySize)
                {
                    string targetId = nodes.GetArrayElementAtIndex(targetNodeIndex)
                        .FindPropertyRelative("id").stringValue;
                    Undo.RecordObject(database, "Connect Dialogue Nodes");
                    SerializedProperty choice = choices.GetArrayElementAtIndex(sourceChoiceIndex);

                    switch (portKind)
                    {
                        case GraphChoicePortKind.Success:
                            choice.FindPropertyRelative("successNodeId").stringValue = targetId;
                            break;
                        case GraphChoicePortKind.Failure:
                            choice.FindPropertyRelative("failureNodeId").stringValue = targetId;
                            break;
                        default:
                            choice.FindPropertyRelative("endsDialogue").boolValue = false;
                            choice.FindPropertyRelative("nextNodeId").stringValue = targetId;
                            break;
                    }

                    EditorUtility.SetDirty(database);
                    ResetPreview();
                }
            }

            current.Use();
            Repaint();
        }

        private static readonly Color GraphNormalEdgeColorDark = new Color(0.55f, 0.78f, 1f, 0.9f);
        private static readonly Color GraphNormalEdgeColorLight = new Color(0.12f, 0.35f, 0.62f, 0.9f);
        private static readonly Color GraphSuccessEdgeColor = new Color(0.42f, 0.78f, 0.42f, 0.95f);
        private static readonly Color GraphFailureEdgeColor = new Color(0.86f, 0.36f, 0.34f, 0.95f);
        private static readonly Color GraphWarningColor = new Color(0.92f, 0.74f, 0.28f, 1f);

        // §7/§32 инструкции "читаемые ноды": проверка — отдельный спокойный
        // accent, не совпадающий ни с текстом, ни с эффектом/условием.
        private static readonly Color GraphCheckAccentColor = new Color(0.62f, 0.66f, 0.95f, 1f);
        private static readonly Color GraphConditionAccentColor = new Color(0.78f, 0.78f, 0.74f, 0.9f);
        private static readonly Color GraphEffectAccentColor = new Color(0.7f, 0.86f, 0.78f, 0.95f);

        private void DrawGraphConnections(SerializedProperty nodes)
        {
            Dictionary<string, int> indicesById = BuildNodeIndexMap(nodes);

            Handles.BeginGUI();
            for (int nodeIndex = 0; nodeIndex < nodes.arraySize; nodeIndex++)
            {
                SerializedProperty node = nodes.GetArrayElementAtIndex(nodeIndex);
                SerializedProperty choices = node.FindPropertyRelative("choices");
                Rect sourceRect = GetNodeScreenRect(nodeIndex, node);

                for (int choiceIndex = 0; choiceIndex < choices.arraySize; choiceIndex++)
                {
                    SerializedProperty choice = choices.GetArrayElementAtIndex(choiceIndex);
                    DialogueChoiceKind kind = (DialogueChoiceKind)choice.FindPropertyRelative("kind").enumValueIndex;

                    if (kind == DialogueChoiceKind.Exit || choice.FindPropertyRelative("endsDialogue").boolValue)
                        continue;

                    if (kind == DialogueChoiceKind.ActiveReturnable || kind == DialogueChoiceKind.ActiveDecisive)
                    {
                        DrawGraphChoiceEdge(
                            nodes, indicesById, GetChoicePortCenter(nodeIndex, sourceRect, choiceIndex, -12f * graphZoom),
                            choice.FindPropertyRelative("successNodeId").stringValue, GraphSuccessEdgeColor);
                        DrawGraphChoiceEdge(
                            nodes, indicesById, GetChoicePortCenter(nodeIndex, sourceRect, choiceIndex, 12f * graphZoom),
                            choice.FindPropertyRelative("failureNodeId").stringValue, GraphFailureEdgeColor);
                        continue;
                    }

                    Color normalColor = EditorGUIUtility.isProSkin ? GraphNormalEdgeColorDark : GraphNormalEdgeColorLight;
                    DrawGraphChoiceEdge(
                        nodes, indicesById, GetChoicePortCenter(nodeIndex, sourceRect, choiceIndex),
                        choice.FindPropertyRelative("nextNodeId").stringValue, normalColor);
                }
            }
            Handles.EndGUI();
        }

        private void DrawGraphChoiceEdge(
            SerializedProperty nodes,
            Dictionary<string, int> indicesById,
            Vector2 from,
            string targetId,
            Color color)
        {
            int targetIndex;
            if (string.IsNullOrWhiteSpace(targetId) || !indicesById.TryGetValue(targetId, out targetIndex))
            {
                Vector2 invalidEnd = from + Vector2.right * (55f * graphZoom);
                Handles.DrawBezier(
                    from,
                    invalidEnd,
                    from + Vector2.right * (30f * graphZoom),
                    invalidEnd + Vector2.left * (10f * graphZoom),
                    new Color(0.95f, 0.35f, 0.3f, 0.9f),
                    null,
                    2f);
                return;
            }

            Rect targetRect = GetNodeScreenRect(targetIndex, nodes.GetArrayElementAtIndex(targetIndex));
            Vector2 to = GetInputPortCenter(targetIndex, targetRect);
            float tangent = Mathf.Max(45f, Mathf.Abs(to.x - from.x) * 0.35f);

            Handles.DrawBezier(
                from,
                to,
                from + Vector2.right * tangent,
                to + Vector2.left * tangent,
                color,
                null,
                Mathf.Max(1.5f, 2.1f * graphZoom));
        }

        private void DrawPendingConnection(SerializedProperty nodes)
        {
            if (graphConnectingNodeIndex < 0 ||
                graphConnectingNodeIndex >= nodes.arraySize)
                return;

            Rect sourceRect = GetNodeScreenRect(graphConnectingNodeIndex, nodes.GetArrayElementAtIndex(graphConnectingNodeIndex));
            float portOffset = graphConnectingPortKind == GraphChoicePortKind.Success
                ? -12f * graphZoom
                : graphConnectingPortKind == GraphChoicePortKind.Failure
                    ? 12f * graphZoom
                    : 0f;
            Vector2 from = GetChoicePortCenter(graphConnectingNodeIndex, sourceRect, graphConnectingChoiceIndex, portOffset);
            Vector2 to = Event.current.mousePosition;
            float tangent = Mathf.Max(40f, Mathf.Abs(to.x - from.x) * 0.35f);

            Color pendingColor = graphConnectingPortKind == GraphChoicePortKind.Success
                ? GraphSuccessEdgeColor
                : graphConnectingPortKind == GraphChoicePortKind.Failure
                    ? GraphFailureEdgeColor
                    : new Color(0.95f, 0.72f, 0.25f, 0.95f);

            Handles.BeginGUI();
            Handles.DrawBezier(
                from,
                to,
                from + Vector2.right * tangent,
                to + Vector2.left * tangent,
                pendingColor,
                null,
                2f);
            Handles.EndGUI();
        }

        private void DrawGraphNodes(
            SerializedProperty dialogue,
            SerializedProperty nodes,
            HashSet<string> reachable)
        {
            string startNodeId = dialogue.FindPropertyRelative("startNodeId").stringValue;

            for (int nodeIndex = 0; nodeIndex < nodes.arraySize; nodeIndex++)
            {
                SerializedProperty node = nodes.GetArrayElementAtIndex(nodeIndex);
                string nodeId = node.FindPropertyRelative("id").stringValue;
                bool isStart = string.Equals(nodeId, startNodeId, StringComparison.Ordinal);
                bool isReachable = string.IsNullOrWhiteSpace(nodeId) || reachable.Contains(nodeId);
                DrawGraphNode(nodes, nodeIndex, isStart, isReachable);
            }
        }

        // Карточка узла — только чтение (§27): весь ввод текста/спикера/
        // целей переходов происходит в правом Inspector'е (DrawNode).
        // Единственные интерактивные элементы холста — заголовок для
        // перетаскивания, порты для связей и кнопка "+ Ответ"/"+ Узел"
        // (структурное добавление, не редактирование содержимого).
        private void DrawGraphNode(
            SerializedProperty nodes,
            int nodeIndex,
            bool isStart,
            bool reachable)
        {
            SerializedProperty node = nodes.GetArrayElementAtIndex(nodeIndex);
            GraphNodeInfo info = graphNodeInfoByIndex.TryGetValue(nodeIndex, out GraphNodeInfo cachedInfo)
                ? cachedInfo
                : BuildGraphNodeInfoFromProperty(node, isStart, reachable);
            GraphNodeLayoutMetrics metrics = GetNodeMetrics(nodeIndex);
            Rect nodeRect = GetNodeScreenRect(nodeIndex, node);

            Rect viewport = new Rect(
                -nodeRect.width,
                -nodeRect.height,
                graphCanvasSize.x + nodeRect.width * 2f,
                graphCanvasSize.y + nodeRect.height * 2f);
            if (!viewport.Overlaps(nodeRect))
                return;

            bool selected = graphSelectedNodeIndex == nodeIndex;
            // §14: рамка остаётся главным индикатором выбора (не толще), но
            // секции выбранного узла дополнительно чуть светлее — второй,
            // более тонкий сигнал, полезный при беглом взгляде на весь граф.
            Color background = EditorGUIUtility.isProSkin
                ? (selected ? new Color(0.20f, 0.21f, 0.23f, 0.98f) : new Color(0.17f, 0.18f, 0.2f, 0.98f))
                : (selected ? new Color(0.99f, 0.99f, 0.98f, 0.98f) : new Color(0.96f, 0.96f, 0.96f, 0.98f));
            Color header = isStart
                ? new Color(0.22f, 0.42f, 0.28f, 1f)
                : (EditorGUIUtility.isProSkin
                    ? new Color(0.22f, 0.24f, 0.27f, 1f)
                    : new Color(0.70f, 0.72f, 0.75f, 1f));
            Color border = selected
                ? new Color(0.95f, 0.72f, 0.24f, 1f)
                : (reachable
                    ? new Color(0f, 0f, 0f, EditorGUIUtility.isProSkin ? 0.55f : 0.25f)
                    : new Color(0.95f, 0.48f, 0.14f, 1f));

            EditorGUI.DrawRect(nodeRect, background);
            DrawGraphBorder(nodeRect, border, selected ? 3f : 1f);

            float titleRowHeight = GraphNodeLayoutConstants.HeaderBaseHeight * graphZoom;
            Rect headerRect = new Rect(nodeRect.x, nodeRect.y, nodeRect.width, metrics.HeaderHeight * graphZoom);
            EditorGUI.DrawRect(headerRect, header);

            Rect inputPort = RectAround(GetInputPortCenter(nodeIndex, nodeRect), Mathf.Max(5f, 6f * graphZoom));
            EditorGUI.DrawRect(inputPort, new Color(0.70f, 0.82f, 0.95f, 1f));

            float margin = GraphPadding * graphZoom;
            Rect dragRect = new Rect(
                headerRect.x + margin,
                headerRect.y + 4f * graphZoom,
                22f * graphZoom,
                titleRowHeight - 8f * graphZoom);
            GUI.Label(dragRect, "⋮", ScaledStyle(EditorStyles.boldLabel, 12, TextAnchor.MiddleCenter));

            string nodeId = node.FindPropertyRelative("id").stringValue;
            Rect idRect = new Rect(
                dragRect.xMax + 3f * graphZoom,
                headerRect.y + 4f * graphZoom,
                headerRect.width - dragRect.width - margin * 2f - (isStart ? 58f : 8f) * graphZoom,
                titleRowHeight - 8f * graphZoom);
            GUI.Label(
                idRect,
                string.IsNullOrWhiteSpace(nodeId) ? "<без ID узла>" : nodeId,
                ScaledStyle(EditorStyles.boldLabel, 11, TextAnchor.MiddleLeft));

            if (isStart)
            {
                Rect startRect = new Rect(
                    headerRect.xMax - 54f * graphZoom,
                    headerRect.y + 5f * graphZoom,
                    48f * graphZoom,
                    titleRowHeight - 10f * graphZoom);
                GUI.Label(startRect, "СТАРТ", ScaledStyle(EditorStyles.miniBoldLabel, 9, TextAnchor.MiddleCenter));
            }

            if (metrics.HeaderHeight * graphZoom > titleRowHeight + 0.5f)
            {
                Rect badgeRowRect = new Rect(
                    headerRect.x + margin,
                    headerRect.y + titleRowHeight,
                    headerRect.width - margin * 2f,
                    metrics.HeaderHeight * graphZoom - titleRowHeight);
                DrawGraphBadgeRow(badgeRowRect, metrics);
            }

            float y = headerRect.yMax + 6f * graphZoom;
            DrawGraphSpeaker(node, nodeRect, metrics, ref y);
            DrawGraphNodeTextSection(nodeRect, info, metrics, ref y);

            Rect titleRect = new Rect(
                nodeRect.x + margin,
                y,
                nodeRect.width - margin * 2f,
                metrics.ChoicesTitleHeight * graphZoom);
            GUI.Label(titleRect, "Ответы игрока", ScaledStyle(EditorStyles.miniBoldLabel, 10, TextAnchor.MiddleLeft));
            y += metrics.ChoicesTitleHeight * graphZoom;

            SerializedProperty choices = node.FindPropertyRelative("choices");
            if (choices.arraySize == 0)
            {
                Rect emptyRect = new Rect(
                    nodeRect.x + margin,
                    y,
                    nodeRect.width - margin * 2f,
                    GraphNodeLayoutConstants.EmptySectionHeight * graphZoom);
                GUI.Label(emptyRect, "Нет вариантов ответа", ScaledStyle(EditorStyles.miniLabel, 10, TextAnchor.MiddleLeft));
                y += GraphNodeLayoutConstants.EmptySectionHeight * graphZoom;
            }
            else
            {
                for (int choiceIndex = 0; choiceIndex < choices.arraySize && choiceIndex < metrics.Choices.Count; choiceIndex++)
                {
                    float choiceHeight = metrics.Choices[choiceIndex].Height * graphZoom;
                    GraphChoiceInfo choiceInfo = choiceIndex < info.Choices.Count ? info.Choices[choiceIndex] : null;
                    DrawGraphChoice(
                        nodeIndex, choiceIndex, nodeRect, y, choiceHeight,
                        choiceInfo, metrics.EffectiveMode, metrics.Warnings);
                    y += choiceHeight;
                }
            }

            // §13 инструкции "читаемые ноды": служебная кнопка добавления не
            // должна выглядеть частью игрового содержания — компактнее и
            // видна только у выбранного узла, а не постоянно у всех.
            if (selected)
            {
                Rect addChoiceRect = new Rect(
                    nodeRect.x + margin,
                    nodeRect.yMax - metrics.FooterHeight * graphZoom + 8f * graphZoom,
                    nodeRect.width - margin * 2f,
                    18f * graphZoom);
                GUIStyle addButtonStyle = ScaledStyle(EditorStyles.miniButton, 9, TextAnchor.MiddleCenter);
                if (GUI.Button(addChoiceRect, "＋ Добавить ответ", addButtonStyle))
                {
                    Undo.RecordObject(database, "Add Dialogue Choice");
                    AddChoice(choices);
                    EditorUtility.SetDirty(database);
                }
            }

            Event current = Event.current;
            if (current.type == EventType.MouseDown &&
                current.button == 0 &&
                nodeRect.Contains(current.mousePosition))
            {
                graphSelectedNodeIndex = nodeIndex;
            }

            if (current.type == EventType.MouseDown &&
                current.button == 0 &&
                dragRect.Contains(current.mousePosition))
            {
                Undo.RecordObject(database, "Move Dialogue Node");
                graphSelectedNodeIndex = nodeIndex;
                graphDraggedNodeIndex = nodeIndex;
                GUI.FocusControl(null);
                current.Use();
            }
        }

        // §5/§24-25: бейджи узла (EXIT/CHECK/COND/FX/KNOW/FLAG/ITEM/
        // UNREACHABLE/"!") плюс тултип со списком конкретных предупреждений
        // на "!"/UNREACHABLE.
        // §3 инструкции "читаемые ноды": обычные бейджи (CHECK/FX/KNOW/FLAG/
        // COND/EXIT/ITEM) — не отдельные крупные прямоугольники, а одна
        // компактная строка "CHECK · FX · KNOW · FLAG". "!"/UNREACHABLE
        // остаются отдельными яркими капсулами — им положено выделяться.
        private void DrawGraphBadgeRow(Rect rect, GraphNodeLayoutMetrics metrics)
        {
            string tooltip = BuildGraphWarningsTooltip(metrics.Warnings);
            List<string> neutralBadges = new List<string>();
            bool hasWarning = false;
            bool hasUnreachable = false;

            foreach (string badge in metrics.Badges)
            {
                if (badge == "СТАРТ") continue;
                if (badge == "!") { hasWarning = true; continue; }
                if (badge == "UNREACHABLE") { hasUnreachable = true; continue; }
                neutralBadges.Add(badge);
            }

            float x = rect.x;

            if (neutralBadges.Count > 0)
            {
                GUIStyle neutralStyle = ScaledStyle(EditorStyles.miniLabel, 8, TextAnchor.MiddleLeft);
                neutralStyle.normal.textColor = EditorGUIUtility.isProSkin
                    ? new Color(0.66f, 0.66f, 0.64f, 1f)
                    : new Color(0.35f, 0.35f, 0.33f, 1f);
                GUIContent content = new GUIContent(string.Join(" · ", neutralBadges));
                Vector2 size = neutralStyle.CalcSize(content);
                Rect textRect = new Rect(x, rect.y, Mathf.Min(size.x, Mathf.Max(0f, rect.xMax - x)), rect.height);
                GUI.Label(textRect, content, neutralStyle);
                x = textRect.xMax + 8f * graphZoom;
            }

            if (hasWarning)
                x = DrawGraphBadgeChip(x, rect, "!", GraphWarningColor, tooltip);
            if (hasUnreachable)
                x = DrawGraphBadgeChip(x, rect, "UNREACHABLE", new Color(0.95f, 0.48f, 0.14f, 1f), tooltip);
        }

        private float DrawGraphBadgeChip(float x, Rect rect, string label, Color color, string tooltip)
        {
            GUIStyle chipStyle = ScaledStyle(EditorStyles.miniBoldLabel, 8, TextAnchor.MiddleCenter);
            GUIContent content = new GUIContent(label, tooltip);
            Vector2 size = chipStyle.CalcSize(content);
            float chipWidth = size.x + 8f * graphZoom;
            if (x + chipWidth > rect.xMax)
                return x;

            Rect chipRect = new Rect(x, rect.y, chipWidth, rect.height);
            EditorGUI.DrawRect(chipRect, new Color(color.r, color.g, color.b, 0.85f));
            GUI.Label(chipRect, content, chipStyle);
            return chipRect.xMax + 4f * graphZoom;
        }

        // §4 инструкции "читаемые ноды": крупнее портрет, имя ярче основного
        // текста, технический [id] заметно слабее (rich text вместо второго
        // Label — не нужно вручную мерить ширину имени), роль — маленькой
        // второй строкой в Full.
        private void DrawGraphSpeaker(SerializedProperty node, Rect nodeRect, GraphNodeLayoutMetrics metrics, ref float y)
        {
            float margin = GraphPadding * graphZoom;
            float sectionHeight = metrics.SpeakerHeight * graphZoom;
            float basePortraitSize = metrics.EffectiveMode == GraphDetailMode.Compact
                ? 28f
                : metrics.EffectiveMode == GraphDetailMode.Full ? 46f : 38f;
            float portraitSize = Mathf.Min(basePortraitSize * graphZoom, sectionHeight - 8f * graphZoom);
            Rect portraitRect = new Rect(
                nodeRect.x + margin,
                y + 4f * graphZoom,
                portraitSize,
                portraitSize);

            SerializedProperty speakerId = node.FindPropertyRelative("speakerId");
            DialogueSpeakerData speaker = database.FindSpeaker(speakerId.stringValue);
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
                    EditorGUIUtility.isProSkin
                        ? new Color(1f, 1f, 1f, 0.08f)
                        : new Color(0f, 0f, 0f, 0.08f));
            }

            float textX = portraitRect.xMax + 6f * graphZoom;
            float textWidth = nodeRect.width - portraitRect.width - margin * 2f - 6f * graphZoom;
            float nameLineHeight = 16f * graphZoom;

            Rect nameRect = new Rect(textX, y + 6f * graphZoom, textWidth, nameLineHeight);
            string mutedHex = EditorGUIUtility.isProSkin ? "9a9a92" : "6b6b64";

            string nameLabel;
            if (string.IsNullOrWhiteSpace(speakerId.stringValue))
                nameLabel = "<нет говорящего>";
            else if (speaker != null)
                nameLabel = "<b>" + speaker.DisplayName + "</b>  <color=#" + mutedHex + ">[" + speaker.Id + "]</color>";
            else
                nameLabel = "<color=#" + mutedHex + ">? " + speakerId.stringValue + "</color>";

            GUIStyle nameStyle = ScaledStyle(EditorStyles.label, 12, TextAnchor.UpperLeft);
            nameStyle.richText = true;
            GUI.Label(nameRect, nameLabel, nameStyle);

            if (metrics.EffectiveMode == GraphDetailMode.Full && speaker != null && !string.IsNullOrWhiteSpace(speaker.Role))
            {
                Rect roleRect = new Rect(textX, nameRect.yMax + 1f * graphZoom, textWidth, GraphNodeLayoutConstants.DetailLineHeight * graphZoom);
                GUIStyle roleStyle = ScaledStyle(EditorStyles.miniLabel, 9, TextAnchor.UpperLeft);
                roleStyle.normal.textColor = EditorGUIUtility.isProSkin
                    ? new Color(0.6f, 0.6f, 0.58f, 1f)
                    : new Color(0.42f, 0.42f, 0.4f, 1f);
                GUI.Label(roleRect, speaker.Role, roleStyle);
            }

            y += sectionHeight;
        }

        // §7-8: текстовые блоки узла — вид блока (никогда числом enum),
        // обрезанное превью, и (Standard/Full) сводки проверки/условий/
        // эффектов. Данные берутся из того же GraphNodeInfo, что и раскладка
        // высоты (metrics), поэтому расхождений между "что нарисовано" и
        // "сколько места выделено" не возникает.
        private void DrawGraphNodeTextSection(Rect nodeRect, GraphNodeInfo info, GraphNodeLayoutMetrics metrics, ref float y)
        {
            float margin = GraphPadding * graphZoom;
            GraphDetailMode mode = metrics.EffectiveMode;

            if (metrics.TextBlocksShown == 0)
            {
                Rect emptyRect = new Rect(nodeRect.x + margin, y, nodeRect.width - margin * 2f, GraphNodeLayoutConstants.EmptySectionHeight * graphZoom);
                GUI.Label(emptyRect, "Нет текстовых блоков", ScaledStyle(EditorStyles.miniLabel, 10, TextAnchor.MiddleLeft));
                y += GraphNodeLayoutConstants.EmptySectionHeight * graphZoom;
                return;
            }

            int maxChars = GetTextPreviewMaxChars(mode);
            List<string> detailLines = new List<string>();
            float cardInset = 4f * graphZoom;

            for (int i = 0; i < metrics.TextBlocksShown; i++)
            {
                GraphTextBlockInfo block = info.TextBlocks[i];
                float cardHeight = metrics.TextBlocks[i].Height * graphZoom;

                // §5: каждый TextBlock — собственная карточка (лёгкий фон),
                // а не непрерывная простыня "заголовок/текст/эффекты".
                Rect cardRect = new Rect(nodeRect.x + margin * 0.5f, y, nodeRect.width - margin, cardHeight - 3f * graphZoom);
                EditorGUI.DrawRect(
                    cardRect,
                    EditorGUIUtility.isProSkin
                        ? new Color(1f, 1f, 1f, 0.035f)
                        : new Color(0f, 0f, 0f, 0.03f));

                float blockY = y + cardInset;
                float contentX = nodeRect.x + margin;
                float contentWidth = nodeRect.width - margin * 2f;
                float lineHeight = GraphNodeLayoutConstants.PreviewLineHeight * graphZoom;

                Rect kindRect = new Rect(contentX, blockY, contentWidth, lineHeight);
                GUI.Label(kindRect, TextBlockKindLabel(block.Kind).ToUpperInvariant(), ScaledStyle(EditorStyles.miniBoldLabel, 9, TextAnchor.MiddleLeft));
                blockY += lineHeight;

                int previewLines = mode == GraphDetailMode.Compact ? 1 : mode == GraphDetailMode.Full ? 3 : 2;
                Rect previewRect = new Rect(contentX, blockY, contentWidth, lineHeight * previewLines);
                GUIStyle previewStyle = ScaledStyle(EditorStyles.label, 10, TextAnchor.UpperLeft);
                previewStyle.wordWrap = true;
                previewStyle.richText = false;
                string previewText = TruncateForGraphPreview(block.Text, maxChars);
                GUI.Label(previewRect, string.IsNullOrEmpty(previewText) ? "<пусто>" : previewText, previewStyle);
                blockY += lineHeight * previewLines;

                if (mode != GraphDetailMode.Compact)
                {
                    if (block.PassiveCheck != null)
                    {
                        Rect checkRect = new Rect(contentX, blockY, contentWidth, GraphNodeLayoutConstants.DetailLineHeight * graphZoom);
                        GUIStyle checkStyle = ScaledStyle(EditorStyles.miniBoldLabel, 9, TextAnchor.MiddleLeft);
                        checkStyle.normal.textColor = GraphCheckAccentColor;
                        GUI.Label(checkRect, "◈ " + BuildGraphPassiveCheckSummary(block.PassiveCheck), checkStyle);
                        blockY += GraphNodeLayoutConstants.DetailLineHeight * graphZoom;
                    }

                    int maxLines = GetMaxDetailLinesPerSection(mode);

                    BuildGraphConditionLines(block.Conditions, maxLines, detailLines, out int condOverflow);
                    blockY = DrawGraphLabeledDetailSection(
                        contentX, contentWidth, blockY, "ПОКАЗАТЬ ЕСЛИ", detailLines, condOverflow, GraphConditionAccentColor);

                    BuildGraphEffectLines(block.OnRevealEffects, maxLines, detailLines, out int fxOverflow);
                    string effectsHeader = block.PassiveCheck != null ? "ПОСЛЕ УСПЕХА" : "ПОСЛЕ ПОКАЗА";
                    blockY = DrawGraphLabeledDetailSection(
                        contentX, contentWidth, blockY, effectsHeader, detailLines, fxOverflow, GraphEffectAccentColor);
                }

                y += cardHeight;
            }

            if (metrics.TextBlocksOverflow > 0)
            {
                Rect overflowRect = new Rect(nodeRect.x + margin, y, nodeRect.width - margin * 2f, GraphNodeLayoutConstants.PreviewLineHeight * graphZoom);
                GUI.Label(overflowRect, BuildOverflowLabel(metrics.TextBlocksOverflow) + " блок(ов)", ScaledStyle(EditorStyles.miniLabel, 9, TextAnchor.MiddleLeft));
                y += GraphNodeLayoutConstants.PreviewLineHeight * graphZoom;
            }
        }

        // §8/§10 инструкции "читаемые ноды": Conditions ("ПОКАЗАТЬ ЕСЛИ") и
        // Effects ("ПОСЛЕ ПОКАЗА"/"ПОСЛЕ УСПЕХА"/"ПОСЛЕ ВЫБОРА") — не общий
        // список строк, а подписанная мини-секция со своим акцентным цветом,
        // чтобы условие "до" и последствие "после" не путались друг с другом
        // или с текстом. Ничего не рисует, если lines пуст (§12 — секция без
        // содержимого просто не появляется, а не превращается в пустую рамку).
        private float DrawGraphLabeledDetailSection(
            float contentX, float contentWidth, float y, string header, List<string> lines, int overflowCount, Color accent)
        {
            if (lines.Count == 0 && overflowCount <= 0)
                return y;

            float headerHeight = GraphNodeLayoutConstants.PreviewLineHeight * graphZoom;
            Rect headerRect = new Rect(contentX, y, contentWidth, headerHeight);
            GUIStyle headerStyle = ScaledStyle(EditorStyles.miniLabel, 8, TextAnchor.MiddleLeft);
            headerStyle.normal.textColor = accent;
            GUI.Label(headerRect, header, headerStyle);
            y += headerHeight;

            float lineHeight = GraphNodeLayoutConstants.DetailLineHeight * graphZoom;
            foreach (string line in lines)
            {
                Rect lineRect = new Rect(contentX, y, contentWidth, lineHeight);
                GUI.Label(lineRect, line, ScaledStyle(EditorStyles.miniLabel, 9, TextAnchor.MiddleLeft));
                y += lineHeight;
            }

            if (overflowCount > 0)
            {
                Rect overflowRect = new Rect(contentX, y, contentWidth, lineHeight);
                GUI.Label(overflowRect, BuildOverflowLabel(overflowCount), ScaledStyle(EditorStyles.miniLabel, 9, TextAnchor.MiddleLeft));
                y += lineHeight;
            }

            return y;
        }

        // Карточка ответа — только чтение (§10 инструкции по читаемым нодам,
        // §13-20/§27 инструкции по информативным нодам): тип, превью текста,
        // цель(и) перехода (вторично, приглушённо — §11), сводка проверки,
        // условия/эффекты отдельными подписанными секциями. Порты (кроме
        // "+ Ответ") остаются интерактивными для перетаскивания связей.
        private void DrawGraphChoice(
            int nodeIndex,
            int choiceIndex,
            Rect nodeRect,
            float y,
            float choiceHeight,
            GraphChoiceInfo choiceInfo,
            GraphDetailMode mode,
            List<string> nodeWarnings)
        {
            float margin = GraphPadding * graphZoom;
            Rect rowRect = new Rect(
                nodeRect.x + margin,
                y + 3f * graphZoom,
                nodeRect.width - margin * 2f,
                choiceHeight - 6f * graphZoom);

            // §10: каждый ответ — отдельная карточка (фон + тонкая рамка),
            // а не продолжение общей колонки.
            EditorGUI.DrawRect(
                rowRect,
                EditorGUIUtility.isProSkin
                    ? new Color(1f, 1f, 1f, 0.045f)
                    : new Color(0f, 0f, 0f, 0.045f));
            DrawGraphBorder(
                rowRect,
                EditorGUIUtility.isProSkin ? new Color(1f, 1f, 1f, 0.07f) : new Color(0f, 0f, 0f, 0.09f),
                1f);

            if (choiceInfo == null)
                return;

            string choiceWarningLabel = "Ответ #" + (choiceIndex + 1).ToString(CultureInfo.InvariantCulture);
            bool hasWarning = ChoiceHasWarning(nodeWarnings, choiceIndex);

            float lineHeight = GraphNodeLayoutConstants.PreviewLineHeight * graphZoom;
            float detailLineHeight = GraphNodeLayoutConstants.DetailLineHeight * graphZoom;
            float cy = rowRect.y + 3f * graphZoom;
            float contentX = rowRect.x + 5f * graphZoom;
            float contentWidth = rowRect.width - 10f * graphZoom;

            Rect kindRect = new Rect(contentX, cy, contentWidth, lineHeight);
            GUI.Label(kindRect, BuildGraphChoiceKindLabel(choiceInfo.Kind), ScaledStyle(EditorStyles.miniBoldLabel, 9, TextAnchor.MiddleLeft));
            cy += lineHeight;

            int previewLines = mode == GraphDetailMode.Full ? 2 : 1;
            Rect textRect = new Rect(contentX, cy, contentWidth, lineHeight * previewLines);
            GUIStyle previewStyle = ScaledStyle(EditorStyles.label, 10, TextAnchor.UpperLeft);
            previewStyle.wordWrap = true;
            string previewText = TruncateForGraphPreview(choiceInfo.Text, GetChoiceTextPreviewMaxChars(mode));
            GUI.Label(textRect, string.IsNullOrEmpty(previewText) ? "<пусто>" : previewText, previewStyle);
            cy += lineHeight * previewLines;

            GUIStyle targetStyle = ScaledStyle(EditorStyles.miniLabel, 8, TextAnchor.MiddleLeft);
            targetStyle.normal.textColor = EditorGUIUtility.isProSkin
                ? new Color(0.62f, 0.62f, 0.6f, 1f)
                : new Color(0.4f, 0.4f, 0.38f, 1f);

            if (choiceInfo.IsExit)
            {
                Rect exitRect = new Rect(contentX, cy, contentWidth, detailLineHeight);
                GUI.Label(exitRect, "ВЫХОД", targetStyle);
                if (hasWarning)
                    cy = DrawGraphChoiceWarningLine(contentX, contentWidth, cy + detailLineHeight, nodeWarnings, choiceWarningLabel);
                return;
            }

            if (choiceInfo.IsActiveCheck)
            {
                if (mode != GraphDetailMode.Compact)
                {
                    Rect mechRect = new Rect(contentX, cy, contentWidth, detailLineHeight);
                    GUIStyle mechStyle = ScaledStyle(EditorStyles.miniLabel, 9, TextAnchor.MiddleLeft);
                    mechStyle.normal.textColor = GraphCheckAccentColor;
                    GUI.Label(mechRect, "◆ " + BuildGraphActiveCheckSummary(choiceInfo.Check), mechStyle);
                    cy += detailLineHeight;
                }

                GUIStyle successStyle = ScaledStyle(EditorStyles.miniLabel, 8, TextAnchor.MiddleLeft);
                successStyle.normal.textColor = GraphSuccessEdgeColor;
                GUIStyle failureStyle = ScaledStyle(EditorStyles.miniLabel, 8, TextAnchor.MiddleLeft);
                failureStyle.normal.textColor = GraphFailureEdgeColor;

                Rect successRect = new Rect(contentX, cy, contentWidth, detailLineHeight);
                GUI.Label(successRect, "✓ УСП → " + GraphShortNodeLabel(choiceInfo.SuccessNodeId), successStyle);
                cy += detailLineHeight;

                Rect failureRect = new Rect(contentX, cy, contentWidth, detailLineHeight);
                GUI.Label(failureRect, "✕ ПРОВ → " + GraphShortNodeLabel(choiceInfo.FailureNodeId), failureStyle);
                cy += detailLineHeight;

                DrawGraphActiveChoicePorts(nodeIndex, choiceIndex, nodeRect);
            }
            else
            {
                string targetLabel = choiceInfo.EndsDialogue ? "→ ВЫХОД" : "→ " + GraphShortNodeLabel(choiceInfo.NextNodeId);
                Rect targetRect = new Rect(contentX, cy, contentWidth, detailLineHeight);
                GUI.Label(targetRect, targetLabel, targetStyle);
                cy += detailLineHeight;

                if (!choiceInfo.EndsDialogue)
                {
                    Vector2 portCenter = GetChoicePortCenter(nodeIndex, nodeRect, choiceIndex);
                    Rect portRect = RectAround(portCenter, Mathf.Max(5f, 6f * graphZoom));
                    EditorGUI.DrawRect(portRect, new Color(0.92f, 0.68f, 0.28f, 1f));

                    Event current = Event.current;
                    if (current.type == EventType.MouseDown &&
                        current.button == 0 &&
                        portRect.Contains(current.mousePosition))
                    {
                        graphConnectingNodeIndex = nodeIndex;
                        graphConnectingChoiceIndex = choiceIndex;
                        graphConnectingPortKind = GraphChoicePortKind.Normal;
                        graphSelectedNodeIndex = nodeIndex;
                        GUI.FocusControl(null);
                        current.Use();
                    }
                }
            }

            // §12 инструкции по читаемым нодам: технический ID обычно
            // вторичен, но при реальной ошибке (несуществующая цель, нет
            // цели) становится ярким предупреждением — контраст важнее
            // приглушённости.
            if (hasWarning)
                cy = DrawGraphChoiceWarningLine(contentX, contentWidth, cy, nodeWarnings, choiceWarningLabel);

            if (mode != GraphDetailMode.Compact)
            {
                int maxLines = GetMaxDetailLinesPerSection(mode);
                List<string> lines = new List<string>();

                BuildGraphConditionLines(choiceInfo.Conditions, maxLines, lines, out int condOverflow);
                cy = DrawGraphLabeledDetailSection(contentX, contentWidth, cy, "УСЛОВИЯ ДОСТУПНОСТИ", lines, condOverflow, GraphConditionAccentColor);

                List<string> successLines = new List<string>();
                BuildGraphEffectLines(choiceInfo.SuccessEffects, maxLines, successLines, out int successOverflow);
                cy = DrawGraphLabeledDetailSection(contentX, contentWidth, cy, "ПОСЛЕ УСПЕХА", successLines, successOverflow, GraphEffectAccentColor);

                List<string> failureLines = new List<string>();
                BuildGraphEffectLines(choiceInfo.FailureEffects, maxLines, failureLines, out int failureOverflow);
                DrawGraphLabeledDetailSection(contentX, contentWidth, cy, "ПОСЛЕ ПРОВАЛА", failureLines, failureOverflow, GraphEffectAccentColor);
            }
        }

        private float DrawGraphChoiceWarningLine(float contentX, float contentWidth, float y, List<string> nodeWarnings, string choiceWarningLabel)
        {
            string message = choiceWarningLabel;
            foreach (string warning in nodeWarnings)
            {
                if (warning.StartsWith(choiceWarningLabel, StringComparison.Ordinal))
                {
                    message = "⚠ " + warning;
                    break;
                }
            }

            float lineHeight = GraphNodeLayoutConstants.DetailLineHeight * graphZoom;
            Rect warningRect = new Rect(contentX, y, contentWidth, lineHeight);
            GUIStyle warningStyle = ScaledStyle(EditorStyles.miniBoldLabel, 9, TextAnchor.MiddleLeft);
            warningStyle.normal.textColor = GraphWarningColor;
            GUIContent content = new GUIContent(message, message);
            GUI.Label(warningRect, content, warningStyle);
            return y + lineHeight;
        }

        // Активная проверка (§13,§15): вместо одного жёлтого порта — два
        // (зелёный успех / красный провал). Точный выбор узла для
        // success/failure делается в режиме «Таблица»/правом Inspector'е —
        // здесь можно только перетащить связь.
        private void DrawGraphActiveChoicePorts(
            int nodeIndex,
            int choiceIndex,
            Rect nodeRect)
        {
            DrawGraphChoicePort(nodeIndex, choiceIndex, nodeRect, -12f * graphZoom, GraphChoicePortKind.Success, GraphSuccessEdgeColor);
            DrawGraphChoicePort(nodeIndex, choiceIndex, nodeRect, 12f * graphZoom, GraphChoicePortKind.Failure, GraphFailureEdgeColor);
        }

        private void DrawGraphChoicePort(
            int nodeIndex,
            int choiceIndex,
            Rect nodeRect,
            float verticalOffset,
            GraphChoicePortKind portKind,
            Color color)
        {
            Vector2 portCenter = GetChoicePortCenter(nodeIndex, nodeRect, choiceIndex, verticalOffset);
            Rect portRect = RectAround(portCenter, Mathf.Max(5f, 6f * graphZoom));
            EditorGUI.DrawRect(portRect, color);

            Event current = Event.current;
            if (current.type == EventType.MouseDown &&
                current.button == 0 &&
                portRect.Contains(current.mousePosition))
            {
                graphConnectingNodeIndex = nodeIndex;
                graphConnectingChoiceIndex = choiceIndex;
                graphConnectingPortKind = portKind;
                graphSelectedNodeIndex = nodeIndex;
                GUI.FocusControl(null);
                current.Use();
            }
        }

        private static string GraphShortNodeLabel(string nodeId)
        {
            if (string.IsNullOrWhiteSpace(nodeId))
                return "<нет>";
            return nodeId.Length > 12 ? nodeId.Substring(0, 11) + "…" : nodeId;
        }

        private void AutoLayoutDialogue(SerializedProperty dialogue, bool recordUndo)
        {
            SerializedProperty nodes = dialogue.FindPropertyRelative("nodes");
            if (nodes.arraySize == 0)
                return;

            if (recordUndo)
                Undo.RecordObject(database, "Auto Layout Dialogue Graph");

            Dictionary<string, int> indicesById = BuildNodeIndexMap(nodes);
            Dictionary<int, int> depthByIndex = new Dictionary<int, int>();
            Queue<int> queue = new Queue<int>();

            string startId = dialogue.FindPropertyRelative("startNodeId").stringValue;
            int startIndex;
            if (!indicesById.TryGetValue(startId, out startIndex))
                startIndex = 0;

            depthByIndex[startIndex] = 0;
            queue.Enqueue(startIndex);

            while (queue.Count > 0)
            {
                int currentIndex = queue.Dequeue();
                int currentDepth = depthByIndex[currentIndex];
                SerializedProperty choices = nodes.GetArrayElementAtIndex(currentIndex).FindPropertyRelative("choices");

                for (int choiceIndex = 0; choiceIndex < choices.arraySize; choiceIndex++)
                {
                    SerializedProperty choice = choices.GetArrayElementAtIndex(choiceIndex);
                    foreach (string targetId in GetGraphChoiceTargets(choice))
                    {
                        int targetIndex;
                        if (!indicesById.TryGetValue(targetId, out targetIndex) ||
                            depthByIndex.ContainsKey(targetIndex))
                            continue;

                        depthByIndex[targetIndex] = currentDepth + 1;
                        queue.Enqueue(targetIndex);
                    }
                }
            }

            int maxDepth = 0;
            foreach (KeyValuePair<int, int> pair in depthByIndex)
                maxDepth = Mathf.Max(maxDepth, pair.Value);

            int orphanDepth = maxDepth + 1;
            for (int i = 0; i < nodes.arraySize; i++)
            {
                if (!depthByIndex.ContainsKey(i))
                    depthByIndex[i] = orphanDepth;
            }

            Dictionary<int, float> nextYByDepth = new Dictionary<int, float>();
            for (int i = 0; i < nodes.arraySize; i++)
            {
                int depth = depthByIndex[i];
                float y;
                if (!nextYByDepth.TryGetValue(depth, out y))
                    y = 60f;

                SerializedProperty node = nodes.GetArrayElementAtIndex(i);
                SetNodeEditorPosition(node, new Vector2(60f + depth * 430f, y));
                y += GetNodeWorldHeight(i) + 60f;
                nextYByDepth[depth] = y;
            }

            EditorUtility.SetDirty(database);
        }

        private void CenterGraph(SerializedProperty dialogue, Rect canvas)
        {
            SerializedProperty nodes = dialogue.FindPropertyRelative("nodes");
            if (nodes.arraySize == 0)
                return;

            Rect bounds = GetNodeWorldRect(0, nodes.GetArrayElementAtIndex(0));
            for (int i = 1; i < nodes.arraySize; i++)
                bounds = Union(bounds, GetNodeWorldRect(i, nodes.GetArrayElementAtIndex(i)));

            graphPan = canvas.center - bounds.center * graphZoom;
        }

        private void SetGraphZoom(float newZoom, Vector2 pivot)
        {
            newZoom = Mathf.Clamp(newZoom, 0.5f, 1.5f);
            if (Mathf.Approximately(newZoom, graphZoom))
                return;

            Vector2 graphPoint = ScreenToGraph(pivot);
            graphZoom = newZoom;
            graphPan = pivot - graphPoint * graphZoom;
            Repaint();
        }

        private Vector2 ScreenToGraph(Vector2 screenPosition)
        {
            return (screenPosition - graphPan) / Mathf.Max(0.01f, graphZoom);
        }

        // §3/§30-34: единственная формула экранного прямоугольника узла —
        // позиция из SerializedProperty, размер из GraphNodeLayoutMetrics
        // (посчитан в RefreshGraphNodeMetrics в начале кадра).
        private Rect GetNodeScreenRect(int nodeIndex, SerializedProperty node)
        {
            GraphNodeLayoutMetrics metrics = GetNodeMetrics(nodeIndex);
            Vector2 worldPosition = node.FindPropertyRelative("editorPosition").vector2Value;
            return new Rect(
                graphPan.x + worldPosition.x * graphZoom,
                graphPan.y + worldPosition.y * graphZoom,
                metrics.Width * graphZoom,
                metrics.TotalHeight * graphZoom);
        }

        private Rect GetNodeWorldRect(int nodeIndex, SerializedProperty node)
        {
            GraphNodeLayoutMetrics metrics = GetNodeMetrics(nodeIndex);
            Vector2 position = node.FindPropertyRelative("editorPosition").vector2Value;
            return new Rect(position.x, position.y, metrics.Width, metrics.TotalHeight);
        }

        private float GetNodeWorldHeight(int nodeIndex)
        {
            return GetNodeMetrics(nodeIndex).TotalHeight;
        }

        private Vector2 GetInputPortCenter(int nodeIndex, Rect nodeRect)
        {
            GraphNodeLayoutMetrics metrics = GetNodeMetrics(nodeIndex);
            return new Vector2(
                nodeRect.xMin,
                nodeRect.yMin + metrics.HeaderHeight * 0.5f * graphZoom);
        }

        // Центр порта варианта ответа — читает Y/Height, посчитанные для
        // ЭТОГО конкретного ответа в ComputeNodeLayoutMetrics, а не по
        // единой формуле фиксированной высоты (§34): высота карточек
        // ответов теперь разная (проверки/условия/эффекты занимают больше
        // места), и порт обязан оставаться на реальном месте кнопки.
        private Vector2 GetChoicePortCenter(int nodeIndex, Rect nodeRect, int choiceIndex, float verticalOffset = 0f)
        {
            GraphNodeLayoutMetrics metrics = GetNodeMetrics(nodeIndex);
            float y;
            if (choiceIndex >= 0 && choiceIndex < metrics.Choices.Count)
            {
                GraphChoiceLayout layout = metrics.Choices[choiceIndex];
                y = nodeRect.yMin + (layout.Y + layout.Height * 0.5f) * graphZoom + verticalOffset;
            }
            else
            {
                y = nodeRect.yMin + verticalOffset;
            }

            return new Vector2(nodeRect.xMax, y);
        }

        private int FindNodeAt(SerializedProperty nodes, Vector2 mousePosition)
        {
            for (int i = nodes.arraySize - 1; i >= 0; i--)
            {
                if (GetNodeScreenRect(i, nodes.GetArrayElementAtIndex(i)).Contains(mousePosition))
                    return i;
            }

            return -1;
        }

        private static Dictionary<string, int> BuildNodeIndexMap(SerializedProperty nodes)
        {
            Dictionary<string, int> result = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < nodes.arraySize; i++)
            {
                string id = nodes.GetArrayElementAtIndex(i).FindPropertyRelative("id").stringValue;
                if (!string.IsNullOrWhiteSpace(id) && !result.ContainsKey(id))
                    result.Add(id, i);
            }

            return result;
        }

        private HashSet<string> CollectReachableNodeIds(SerializedProperty dialogue)
        {
            SerializedProperty nodes = dialogue.FindPropertyRelative("nodes");
            Dictionary<string, int> indicesById = BuildNodeIndexMap(nodes);
            HashSet<string> reachable = new HashSet<string>(StringComparer.Ordinal);
            Queue<string> queue = new Queue<string>();

            string start = dialogue.FindPropertyRelative("startNodeId").stringValue;
            if (!string.IsNullOrWhiteSpace(start) && indicesById.ContainsKey(start))
                queue.Enqueue(start);

            while (queue.Count > 0)
            {
                string id = queue.Dequeue();
                if (!reachable.Add(id))
                    continue;

                int nodeIndex;
                if (!indicesById.TryGetValue(id, out nodeIndex))
                    continue;

                SerializedProperty choices = nodes.GetArrayElementAtIndex(nodeIndex).FindPropertyRelative("choices");
                for (int choiceIndex = 0; choiceIndex < choices.arraySize; choiceIndex++)
                {
                    SerializedProperty choice = choices.GetArrayElementAtIndex(choiceIndex);
                    foreach (string target in GetGraphChoiceTargets(choice))
                    {
                        if (indicesById.ContainsKey(target))
                            queue.Enqueue(target);
                    }
                }
            }

            return reachable;
        }

        // Единая точка вычисления целей одного варианта ответа для
        // автораскладки и подсветки достижимости в Графе: обычный переход —
        // один узел, активная проверка — узлы успеха и провала, EXIT — ни
        // одного. Совпадает по смыслу с DialogueDatabaseAsset.GetChoiceTargets,
        // но работает через SerializedProperty, а не готовые данные.
        private static IEnumerable<string> GetGraphChoiceTargets(SerializedProperty choice)
        {
            DialogueChoiceKind kind = (DialogueChoiceKind)choice.FindPropertyRelative("kind").enumValueIndex;
            bool endsDialogue = choice.FindPropertyRelative("endsDialogue").boolValue;

            if (kind == DialogueChoiceKind.Exit || endsDialogue)
                yield break;

            if (kind == DialogueChoiceKind.ActiveReturnable || kind == DialogueChoiceKind.ActiveDecisive)
            {
                string successId = choice.FindPropertyRelative("successNodeId").stringValue;
                string failureId = choice.FindPropertyRelative("failureNodeId").stringValue;
                if (!string.IsNullOrWhiteSpace(successId))
                    yield return successId;
                if (!string.IsNullOrWhiteSpace(failureId))
                    yield return failureId;
                yield break;
            }

            string nextId = choice.FindPropertyRelative("nextNodeId").stringValue;
            if (!string.IsNullOrWhiteSpace(nextId))
                yield return nextId;
        }

        private GUIStyle ScaledStyle(GUIStyle source, int baseFontSize, TextAnchor alignment)
        {
            GUIStyle style = new GUIStyle(source)
            {
                fontSize = Mathf.Clamp(Mathf.RoundToInt(baseFontSize * graphZoom), 7, 16),
                alignment = alignment
            };
            return style;
        }

        private static Rect RectAround(Vector2 center, float radius)
        {
            return new Rect(
                center.x - radius,
                center.y - radius,
                radius * 2f,
                radius * 2f);
        }

        private static Rect Union(Rect a, Rect b)
        {
            return Rect.MinMaxRect(
                Mathf.Min(a.xMin, b.xMin),
                Mathf.Min(a.yMin, b.yMin),
                Mathf.Max(a.xMax, b.xMax),
                Mathf.Max(a.yMax, b.yMax));
        }

        private static void DrawGraphBorder(Rect rect, Color color, float thickness)
        {
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, thickness), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, thickness, rect.height), color);
            EditorGUI.DrawRect(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), color);
        }
    }
}

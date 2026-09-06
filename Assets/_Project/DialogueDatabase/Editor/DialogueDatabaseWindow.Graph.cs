using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace KingdomSurvival.DialogueDatabase.Editor
{
    public sealed partial class DialogueDatabaseWindow : EditorWindow
    {
        private const float GraphNodeWidth = 320f;
        private const float GraphHeaderHeight = 30f;
        private const float GraphSpeakerHeight = 44f;
        private const float GraphTextHeight = 72f;
        private const float GraphChoiceTitleHeight = 20f;
        private const float GraphChoiceHeight = 54f;
        private const float GraphFooterHeight = 34f;
        private const float GraphPadding = 10f;
        private const float GraphGrid = 64f;

        private Vector2 graphPan = new Vector2(40f, 40f);
        private Vector2 graphCanvasSize = new Vector2(700f, 500f);
        private float graphZoom = 1f;
        private int graphSelectedNodeIndex = -1;
        private int graphDraggedNodeIndex = -1;
        private bool graphPanning;
        private int graphConnectingNodeIndex = -1;
        private int graphConnectingChoiceIndex = -1;
        private bool graphNeedsCenter = true;

        private void ResetGraphViewState()
        {
            graphPan = new Vector2(40f, 40f);
            graphZoom = 1f;
            graphSelectedNodeIndex = -1;
            graphDraggedNodeIndex = -1;
            graphPanning = false;
            graphConnectingNodeIndex = -1;
            graphConnectingChoiceIndex = -1;
            graphNeedsCenter = true;
        }

        private void DrawDialogueGraph(SerializedProperty dialogue)
        {
            SerializedProperty nodes = dialogue.FindPropertyRelative("nodes");
            EnsureGraphPositions(dialogue);
            DrawGraphToolbar(dialogue, nodes);

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
            HashSet<string> reachable = CollectReachableNodeIds(dialogue);
            DrawGraphConnections(nodes);
            DrawGraphNodes(dialogue, nodes, reachable);
            DrawPendingConnection(nodes);

            GUI.EndGroup();
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
            GUILayout.Label(
                "Перетаскивай ноды · тяни жёлтый порт ответа на нужный нод · колесо = масштаб · Alt+ЛКМ/СКМ = поле",
                EditorStyles.miniLabel);

            GUILayout.FlexibleSpace();

            if (GUILayout.Button("−", EditorStyles.toolbarButton, GUILayout.Width(24f)))
                SetGraphZoom(graphZoom / 1.15f, graphCanvasSize * 0.5f);
            GUILayout.Label(Mathf.RoundToInt(graphZoom * 100f) + "%", EditorStyles.miniLabel, GUILayout.Width(40f));
            if (GUILayout.Button("+", EditorStyles.toolbarButton, GUILayout.Width(24f)))
                SetGraphZoom(graphZoom * 1.15f, graphCanvasSize * 0.5f);

            EditorGUILayout.EndHorizontal();
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
            int targetNodeIndex = FindNodeAt(nodes, mouse);
            graphConnectingNodeIndex = -1;
            graphConnectingChoiceIndex = -1;

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
                    choice.FindPropertyRelative("endsDialogue").boolValue = false;
                    choice.FindPropertyRelative("nextNodeId").stringValue = targetId;
                    EditorUtility.SetDirty(database);
                    ResetPreview();
                }
            }

            current.Use();
            Repaint();
        }

        private void DrawGraphConnections(SerializedProperty nodes)
        {
            Dictionary<string, int> indicesById = BuildNodeIndexMap(nodes);

            Handles.BeginGUI();
            for (int nodeIndex = 0; nodeIndex < nodes.arraySize; nodeIndex++)
            {
                SerializedProperty node = nodes.GetArrayElementAtIndex(nodeIndex);
                SerializedProperty choices = node.FindPropertyRelative("choices");
                Rect sourceRect = GetNodeScreenRect(node);

                for (int choiceIndex = 0; choiceIndex < choices.arraySize; choiceIndex++)
                {
                    SerializedProperty choice = choices.GetArrayElementAtIndex(choiceIndex);
                    if (choice.FindPropertyRelative("endsDialogue").boolValue)
                        continue;

                    Vector2 from = GetChoicePortCenter(sourceRect, choiceIndex);
                    string targetId = choice.FindPropertyRelative("nextNodeId").stringValue;
                    int targetIndex;

                    if (string.IsNullOrWhiteSpace(targetId) ||
                        !indicesById.TryGetValue(targetId, out targetIndex))
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
                        continue;
                    }

                    Rect targetRect = GetNodeScreenRect(nodes.GetArrayElementAtIndex(targetIndex));
                    Vector2 to = GetInputPortCenter(targetRect);
                    float tangent = Mathf.Max(45f, Mathf.Abs(to.x - from.x) * 0.35f);

                    Handles.DrawBezier(
                        from,
                        to,
                        from + Vector2.right * tangent,
                        to + Vector2.left * tangent,
                        EditorGUIUtility.isProSkin
                            ? new Color(0.55f, 0.78f, 1f, 0.9f)
                            : new Color(0.12f, 0.35f, 0.62f, 0.9f),
                        null,
                        Mathf.Max(1.5f, 2.1f * graphZoom));
                }
            }
            Handles.EndGUI();
        }

        private void DrawPendingConnection(SerializedProperty nodes)
        {
            if (graphConnectingNodeIndex < 0 ||
                graphConnectingNodeIndex >= nodes.arraySize)
                return;

            Rect sourceRect = GetNodeScreenRect(nodes.GetArrayElementAtIndex(graphConnectingNodeIndex));
            Vector2 from = GetChoicePortCenter(sourceRect, graphConnectingChoiceIndex);
            Vector2 to = Event.current.mousePosition;
            float tangent = Mathf.Max(40f, Mathf.Abs(to.x - from.x) * 0.35f);

            Handles.BeginGUI();
            Handles.DrawBezier(
                from,
                to,
                from + Vector2.right * tangent,
                to + Vector2.left * tangent,
                new Color(0.95f, 0.72f, 0.25f, 0.95f),
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

        private void DrawGraphNode(
            SerializedProperty nodes,
            int nodeIndex,
            bool isStart,
            bool reachable)
        {
            SerializedProperty node = nodes.GetArrayElementAtIndex(nodeIndex);
            Rect nodeRect = GetNodeScreenRect(node);

            Rect viewport = new Rect(
                -nodeRect.width,
                -nodeRect.height,
                graphCanvasSize.x + nodeRect.width * 2f,
                graphCanvasSize.y + nodeRect.height * 2f);
            if (!viewport.Overlaps(nodeRect))
                return;

            bool selected = graphSelectedNodeIndex == nodeIndex;
            Color background = EditorGUIUtility.isProSkin
                ? new Color(0.17f, 0.18f, 0.2f, 0.98f)
                : new Color(0.96f, 0.96f, 0.96f, 0.98f);
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

            Rect headerRect = new Rect(
                nodeRect.x,
                nodeRect.y,
                nodeRect.width,
                GraphHeaderHeight * graphZoom);
            EditorGUI.DrawRect(headerRect, header);

            Rect inputPort = RectAround(GetInputPortCenter(nodeRect), Mathf.Max(5f, 6f * graphZoom));
            EditorGUI.DrawRect(inputPort, new Color(0.70f, 0.82f, 0.95f, 1f));

            float margin = GraphPadding * graphZoom;
            Rect dragRect = new Rect(
                headerRect.x + margin,
                headerRect.y + 4f * graphZoom,
                22f * graphZoom,
                headerRect.height - 8f * graphZoom);
            GUI.Label(dragRect, "⋮", ScaledStyle(EditorStyles.boldLabel, 12, TextAnchor.MiddleCenter));

            string nodeId = node.FindPropertyRelative("id").stringValue;
            Rect idRect = new Rect(
                dragRect.xMax + 3f * graphZoom,
                headerRect.y + 4f * graphZoom,
                headerRect.width - dragRect.width - margin * 2f - (isStart ? 58f : 8f) * graphZoom,
                headerRect.height - 8f * graphZoom);
            GUI.Label(
                idRect,
                string.IsNullOrWhiteSpace(nodeId) ? "<без Node ID>" : nodeId,
                ScaledStyle(EditorStyles.boldLabel, 11, TextAnchor.MiddleLeft));

            if (isStart)
            {
                Rect startRect = new Rect(
                    headerRect.xMax - 54f * graphZoom,
                    headerRect.y + 5f * graphZoom,
                    48f * graphZoom,
                    headerRect.height - 10f * graphZoom);
                GUI.Label(startRect, "START", ScaledStyle(EditorStyles.miniBoldLabel, 9, TextAnchor.MiddleCenter));
            }

            float y = headerRect.yMax + 6f * graphZoom;
            DrawGraphSpeaker(node, nodeRect, ref y);
            DrawGraphNodeText(node, nodeRect, ref y);

            Rect titleRect = new Rect(
                nodeRect.x + margin,
                y,
                nodeRect.width - margin * 2f,
                GraphChoiceTitleHeight * graphZoom);
            GUI.Label(titleRect, "Ответы игрока", ScaledStyle(EditorStyles.miniBoldLabel, 10, TextAnchor.MiddleLeft));
            y += GraphChoiceTitleHeight * graphZoom;

            SerializedProperty choices = node.FindPropertyRelative("choices");
            for (int choiceIndex = 0; choiceIndex < choices.arraySize; choiceIndex++)
            {
                DrawGraphChoice(nodes, nodeIndex, choiceIndex, nodeRect, y, choices.GetArrayElementAtIndex(choiceIndex));
                y += GraphChoiceHeight * graphZoom;
            }

            Rect addChoiceRect = new Rect(
                nodeRect.x + margin,
                nodeRect.yMax - GraphFooterHeight * graphZoom + 5f * graphZoom,
                nodeRect.width - margin * 2f,
                24f * graphZoom);
            if (GUI.Button(addChoiceRect, "+ Ответ", ScaledStyle(GUI.skin.button, 10, TextAnchor.MiddleCenter)))
            {
                Undo.RecordObject(database, "Add Dialogue Choice");
                AddChoice(choices);
                EditorUtility.SetDirty(database);
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

        private void DrawGraphSpeaker(SerializedProperty node, Rect nodeRect, ref float y)
        {
            float margin = GraphPadding * graphZoom;
            Rect portraitRect = new Rect(
                nodeRect.x + margin,
                y + 4f * graphZoom,
                34f * graphZoom,
                34f * graphZoom);

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

            Rect speakerRect = new Rect(
                portraitRect.xMax + 6f * graphZoom,
                y + 7f * graphZoom,
                nodeRect.width - portraitRect.width - margin * 2f - 6f * graphZoom,
                24f * graphZoom);

            IReadOnlyList<DialogueSpeakerData> speakers = database.Speakers;
            if (speakers.Count == 0)
            {
                speakerId.stringValue = EditorGUI.TextField(speakerRect, speakerId.stringValue);
            }
            else
            {
                string[] labels = new string[speakers.Count];
                int selected = 0;
                for (int i = 0; i < speakers.Count; i++)
                {
                    labels[i] = speakers[i].DisplayName + " [" + speakers[i].Id + "]";
                    if (string.Equals(speakers[i].Id, speakerId.stringValue, StringComparison.Ordinal))
                        selected = i;
                }

                int next = EditorGUI.Popup(speakerRect, selected, labels);
                if (next >= 0 && next < speakers.Count)
                    speakerId.stringValue = speakers[next].Id;
            }

            y += GraphSpeakerHeight * graphZoom;
        }

        private void DrawGraphNodeText(SerializedProperty node, Rect nodeRect, ref float y)
        {
            float margin = GraphPadding * graphZoom;
            Rect textRect = new Rect(
                nodeRect.x + margin,
                y,
                nodeRect.width - margin * 2f,
                GraphTextHeight * graphZoom - 4f * graphZoom);

            GUIStyle textStyle = ScaledStyle(EditorStyles.textArea, 11, TextAnchor.UpperLeft);
            textStyle.wordWrap = true;
            SerializedProperty text = node.FindPropertyRelative("text");
            text.stringValue = EditorGUI.TextArea(textRect, text.stringValue, textStyle);
            y += GraphTextHeight * graphZoom;
        }

        private void DrawGraphChoice(
            SerializedProperty nodes,
            int nodeIndex,
            int choiceIndex,
            Rect nodeRect,
            float y,
            SerializedProperty choice)
        {
            float margin = GraphPadding * graphZoom;
            Rect rowRect = new Rect(
                nodeRect.x + margin,
                y + 2f * graphZoom,
                nodeRect.width - margin * 2f,
                GraphChoiceHeight * graphZoom - 4f * graphZoom);

            EditorGUI.DrawRect(
                rowRect,
                EditorGUIUtility.isProSkin
                    ? new Color(1f, 1f, 1f, 0.035f)
                    : new Color(0f, 0f, 0f, 0.035f));

            float targetWidth = 100f * graphZoom;
            float gap = 5f * graphZoom;
            Rect targetRect = new Rect(
                rowRect.xMax - targetWidth - 4f * graphZoom,
                rowRect.y + 5f * graphZoom,
                targetWidth,
                rowRect.height - 10f * graphZoom);
            Rect textRect = new Rect(
                rowRect.x + 5f * graphZoom,
                rowRect.y + 4f * graphZoom,
                targetRect.x - rowRect.x - gap - 5f * graphZoom,
                rowRect.height - 8f * graphZoom);

            SerializedProperty text = choice.FindPropertyRelative("text");
            GUIStyle textStyle = ScaledStyle(EditorStyles.textArea, 10, TextAnchor.UpperLeft);
            textStyle.wordWrap = true;
            text.stringValue = EditorGUI.TextArea(textRect, text.stringValue, textStyle);

            SerializedProperty nextNodeId = choice.FindPropertyRelative("nextNodeId");
            SerializedProperty endsDialogue = choice.FindPropertyRelative("endsDialogue");

            List<string> labels = new List<string>();
            labels.Add("EXIT");
            for (int i = 0; i < nodes.arraySize; i++)
            {
                string id = nodes.GetArrayElementAtIndex(i).FindPropertyRelative("id").stringValue;
                labels.Add(string.IsNullOrWhiteSpace(id) ? "<без ID>" : id);
            }

            int selectedTarget = 0;
            if (!endsDialogue.boolValue)
            {
                for (int i = 0; i < nodes.arraySize; i++)
                {
                    string id = nodes.GetArrayElementAtIndex(i).FindPropertyRelative("id").stringValue;
                    if (string.Equals(id, nextNodeId.stringValue, StringComparison.Ordinal))
                    {
                        selectedTarget = i + 1;
                        break;
                    }
                }
            }

            int nextTarget = EditorGUI.Popup(targetRect, selectedTarget, labels.ToArray());
            if (nextTarget != selectedTarget)
            {
                Undo.RecordObject(database, "Change Dialogue Branch");
                if (nextTarget <= 0)
                {
                    endsDialogue.boolValue = true;
                    nextNodeId.stringValue = string.Empty;
                }
                else
                {
                    endsDialogue.boolValue = false;
                    nextNodeId.stringValue = nodes.GetArrayElementAtIndex(nextTarget - 1)
                        .FindPropertyRelative("id").stringValue;
                }

                EditorUtility.SetDirty(database);
                ResetPreview();
            }

            if (!endsDialogue.boolValue)
            {
                Vector2 portCenter = GetChoicePortCenter(nodeRect, choiceIndex);
                Rect portRect = RectAround(portCenter, Mathf.Max(5f, 6f * graphZoom));
                EditorGUI.DrawRect(portRect, new Color(0.92f, 0.68f, 0.28f, 1f));

                Event current = Event.current;
                if (current.type == EventType.MouseDown &&
                    current.button == 0 &&
                    portRect.Contains(current.mousePosition))
                {
                    graphConnectingNodeIndex = nodeIndex;
                    graphConnectingChoiceIndex = choiceIndex;
                    graphSelectedNodeIndex = nodeIndex;
                    GUI.FocusControl(null);
                    current.Use();
                }
            }
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
                    if (choice.FindPropertyRelative("endsDialogue").boolValue)
                        continue;

                    string targetId = choice.FindPropertyRelative("nextNodeId").stringValue;
                    int targetIndex;
                    if (!indicesById.TryGetValue(targetId, out targetIndex) ||
                        depthByIndex.ContainsKey(targetIndex))
                        continue;

                    depthByIndex[targetIndex] = currentDepth + 1;
                    queue.Enqueue(targetIndex);
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
                y += GetNodeWorldHeight(node) + 60f;
                nextYByDepth[depth] = y;
            }

            EditorUtility.SetDirty(database);
        }

        private void CenterGraph(SerializedProperty dialogue, Rect canvas)
        {
            SerializedProperty nodes = dialogue.FindPropertyRelative("nodes");
            if (nodes.arraySize == 0)
                return;

            Rect bounds = GetNodeWorldRect(nodes.GetArrayElementAtIndex(0));
            for (int i = 1; i < nodes.arraySize; i++)
                bounds = Union(bounds, GetNodeWorldRect(nodes.GetArrayElementAtIndex(i)));

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

        private Rect GetNodeScreenRect(SerializedProperty node)
        {
            Vector2 worldPosition = node.FindPropertyRelative("editorPosition").vector2Value;
            return new Rect(
                graphPan.x + worldPosition.x * graphZoom,
                graphPan.y + worldPosition.y * graphZoom,
                GraphNodeWidth * graphZoom,
                GetNodeWorldHeight(node) * graphZoom);
        }

        private Rect GetNodeWorldRect(SerializedProperty node)
        {
            Vector2 position = node.FindPropertyRelative("editorPosition").vector2Value;
            return new Rect(position.x, position.y, GraphNodeWidth, GetNodeWorldHeight(node));
        }

        private static float GetNodeWorldHeight(SerializedProperty node)
        {
            SerializedProperty choices = node.FindPropertyRelative("choices");
            int choiceCount = choices == null ? 0 : choices.arraySize;
            return GraphHeaderHeight +
                   GraphSpeakerHeight +
                   GraphTextHeight +
                   GraphChoiceTitleHeight +
                   choiceCount * GraphChoiceHeight +
                   GraphFooterHeight +
                   16f;
        }

        private Vector2 GetInputPortCenter(Rect nodeRect)
        {
            return new Vector2(
                nodeRect.xMin,
                nodeRect.yMin + GraphHeaderHeight * 0.5f * graphZoom);
        }

        private Vector2 GetChoicePortCenter(Rect nodeRect, int choiceIndex)
        {
            float y =
                nodeRect.yMin +
                (GraphHeaderHeight +
                 GraphSpeakerHeight +
                 GraphTextHeight +
                 GraphChoiceTitleHeight +
                 GraphChoiceHeight * choiceIndex +
                 GraphChoiceHeight * 0.5f +
                 6f) * graphZoom;
            return new Vector2(nodeRect.xMax, y);
        }

        private int FindNodeAt(SerializedProperty nodes, Vector2 mousePosition)
        {
            for (int i = nodes.arraySize - 1; i >= 0; i--)
            {
                if (GetNodeScreenRect(nodes.GetArrayElementAtIndex(i)).Contains(mousePosition))
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
                    if (choice.FindPropertyRelative("endsDialogue").boolValue)
                        continue;

                    string target = choice.FindPropertyRelative("nextNodeId").stringValue;
                    if (!string.IsNullOrWhiteSpace(target) && indicesById.ContainsKey(target))
                        queue.Enqueue(target);
                }
            }

            return reachable;
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

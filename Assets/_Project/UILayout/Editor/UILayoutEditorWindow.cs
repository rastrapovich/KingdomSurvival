using System.Collections.Generic;
using KingdomSurvival.DialogueDatabase;
using UnityEditor;
using UnityEngine;

namespace KingdomSurvival.UILayout.Editor
{
    public sealed class UILayoutEditorWindow : EditorWindow
    {
        private const float LeftWidth = 220f;
        private const float RightWidth = 300f;
        private const float CanvasAspect = 16f / 9f;

        private UILayoutDatabaseAsset database;
        private DialogueDatabaseAsset dialogueDatabase;
        private int screenIndex;
        private int elementIndex;
        private int previewDialogueIndex;
        private Vector2 leftScroll;
        private Vector2 rightScroll;
        private float previewScale = 0.7f;
        private bool dragging;
        private bool resizing;
        private Vector2 dragStart;
        private Rect rectStart;

        [MenuItem("Kingdom Survival/UI Конструктор")]
        private static void Open()
        {
            GetWindow<UILayoutEditorWindow>("UI Конструктор");
        }

        private void OnEnable()
        {
            if (database == null)
                database = Resources.Load<UILayoutDatabaseAsset>(UILayoutDatabaseAsset.ResourcesPath);
            if (dialogueDatabase == null)
                dialogueDatabase = Resources.Load<DialogueDatabaseAsset>(DialogueDatabaseAsset.ResourcesPath);
        }

        private void OnGUI()
        {
            EditorGUILayout.BeginHorizontal();
            DrawLeftPanel();
            DrawCanvas();
            DrawRightPanel();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawLeftPanel()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(LeftWidth));
            database = (UILayoutDatabaseAsset)EditorGUILayout.ObjectField(
                "База",
                database,
                typeof(UILayoutDatabaseAsset),
                false);

            if (database == null)
            {
                EditorGUILayout.HelpBox("Назначьте UILayoutDatabaseAsset.", MessageType.Info);
                EditorGUILayout.EndVertical();
                return;
            }

            leftScroll = EditorGUILayout.BeginScrollView(leftScroll);
            IReadOnlyList<UILayoutScreenDefinition> screens = database.Screens;
            EditorGUILayout.LabelField("ЭКРАНЫ", EditorStyles.boldLabel);
            for (int i = 0; i < screens.Count; i++)
            {
                if (GUILayout.Toggle(screenIndex == i, screens[i].DisplayName, "Button"))
                {
                    if (screenIndex != i)
                    {
                        screenIndex = i;
                        elementIndex = 0;
                    }
                }
            }

            UILayoutScreenDefinition screen = CurrentScreen;
            if (screen != null)
            {
                GUILayout.Space(10f);
                EditorGUILayout.LabelField("ЭЛЕМЕНТЫ", EditorStyles.boldLabel);
                for (int i = 0; i < screen.Elements.Count; i++)
                {
                    UILayoutElementDefinition element = screen.Elements[i];
                    string indent = string.IsNullOrWhiteSpace(element.ParentId) ? string.Empty : "   ↳ ";
                    if (GUILayout.Toggle(elementIndex == i, indent + element.DisplayName, "Button"))
                        elementIndex = i;
                }
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawCanvas()
        {
            EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            previewScale = EditorGUILayout.Slider("Масштаб preview", previewScale, 0.25f, 1f);
            Vector2Int reference = database != null ? database.ReferenceResolution : new Vector2Int(1920, 1080);
            EditorGUILayout.LabelField(reference.x + " × " + reference.y, EditorStyles.miniLabel);

            UILayoutScreenDefinition screen = CurrentScreen;
            if (screen != null && screen.Id == "narrative-dialogue")
                DrawNarrativePreviewToolbar();

            Rect host = GUILayoutUtility.GetRect(
                100f,
                10000f,
                100f,
                10000f,
                GUILayout.ExpandWidth(true),
                GUILayout.ExpandHeight(true));
            float maxWidth = Mathf.Max(100f, host.width - 20f);
            float maxHeight = Mathf.Max(100f, host.height - 20f);
            float width = Mathf.Min(maxWidth, maxHeight * CanvasAspect) * previewScale;
            float height = width / CanvasAspect;
            Rect canvas = new Rect(
                host.x + (host.width - width) * 0.5f,
                host.y + (host.height - height) * 0.5f,
                width,
                height);

            EditorGUI.DrawRect(canvas, new Color(0.12f, 0.13f, 0.14f, 1f));
            if (screen != null && screen.Id == "narrative-dialogue")
            {
                EditorGUI.DrawRect(
                    canvas,
                    new Color(5f / 255f, 7f / 255f, 8f / 255f, screen.DimmingOpacity));
            }
            GUI.Box(canvas, GUIContent.none);

            if (screen != null)
            {
                float sx = canvas.width / Mathf.Max(1f, reference.x);
                float sy = canvas.height / Mathf.Max(1f, reference.y);
                bool narrativePreview = screen.Id == "narrative-dialogue" && CurrentPreviewDialogue != null;

                for (int i = 0; i < screen.Elements.Count; i++)
                {
                    UILayoutElementDefinition element = screen.Elements[i];
                    Rect draw = ToCanvasRect(canvas, element.Rect, sx, sy);
                    DrawElementImage(draw, element, sx, sy);
                    EditorGUI.DrawRect(
                        draw,
                        i == elementIndex
                            ? new Color(0.75f, 0.54f, 0.18f, 0.20f)
                            : new Color(0.4f, 0.4f, 0.4f, 0.08f));
                    GUI.Box(draw, narrativePreview ? GUIContent.none : new GUIContent(element.DisplayName));

                    if (narrativePreview)
                    {
                        Rect tag = new Rect(draw.x + 2f, draw.y + 2f, Mathf.Min(Mathf.Max(0f, draw.width - 4f), 120f), 16f);
                        GUI.Label(tag, element.DisplayName, EditorStyles.miniLabel);
                    }

                    if (i == elementIndex)
                        HandleSelectedElement(draw, sx, sy, element);
                }

                if (narrativePreview)
                    DrawNarrativeContentPreview(canvas, screen, sx, sy);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawNarrativePreviewToolbar()
        {
            dialogueDatabase = (DialogueDatabaseAsset)EditorGUILayout.ObjectField(
                "База диалогов",
                dialogueDatabase,
                typeof(DialogueDatabaseAsset),
                false);

            if (dialogueDatabase == null || dialogueDatabase.Dialogues.Count == 0)
            {
                EditorGUILayout.HelpBox("Нет доступных диалогов для preview.", MessageType.Info);
                return;
            }

            string[] labels = new string[dialogueDatabase.Dialogues.Count];
            for (int i = 0; i < labels.Length; i++)
            {
                DialogueDefinitionData dialogue = dialogueDatabase.Dialogues[i];
                labels[i] = dialogue.Title + "  [" + dialogue.Id + "]";
            }

            previewDialogueIndex = Mathf.Clamp(previewDialogueIndex, 0, labels.Length - 1);
            previewDialogueIndex = EditorGUILayout.Popup("Диалог preview", previewDialogueIndex, labels);
        }

        private void DrawNarrativeContentPreview(
            Rect canvas,
            UILayoutScreenDefinition screen,
            float sx,
            float sy)
        {
            DialogueDefinitionData dialogue = CurrentPreviewDialogue;
            DialogueNodeData node = FindPreviewNode(dialogue);
            if (dialogue == null || node == null)
                return;

            DialogueSpeakerData speaker = dialogueDatabase != null
                ? dialogueDatabase.FindSpeaker(node.SpeakerId)
                : null;

            UILayoutElementDefinition portrait = screen.FindElement("portrait");
            if (portrait != null && speaker != null && speaker.Portrait != null)
            {
                Rect portraitRect = ToCanvasRect(canvas, portrait.Rect, sx, sy);
                GUI.DrawTexture(portraitRect, speaker.Portrait.texture, ScaleMode.ScaleAndCrop, true);
            }

            if (speaker != null)
            {
                DrawPreviewText(
                    canvas,
                    screen.FindElement("speaker"),
                    speaker.DisplayName,
                    sx,
                    sy);
                DrawPreviewText(
                    canvas,
                    screen.FindElement("role"),
                    speaker.Role,
                    sx,
                    sy);
            }

            DrawPreviewText(
                canvas,
                screen.FindElement("text"),
                node.Text,
                sx,
                sy);

            string choices = string.Empty;
            for (int i = 0; i < node.Choices.Count; i++)
            {
                if (i > 0)
                    choices += "\n\n";
                choices += "› " + node.Choices[i].Text;
            }

            DrawPreviewText(
                canvas,
                screen.FindElement("choices"),
                choices,
                sx,
                sy);
        }

        private static void DrawPreviewText(
            Rect canvas,
            UILayoutElementDefinition element,
            string text,
            float sx,
            float sy)
        {
            if (element == null || string.IsNullOrEmpty(text))
                return;

            Rect rect = ToCanvasRect(canvas, element.Rect, sx, sy);
            rect.x += 5f;
            rect.y += 5f;
            rect.width = Mathf.Max(0f, rect.width - 10f);
            rect.height = Mathf.Max(0f, rect.height - 10f);
            float textScale = Mathf.Max(0.01f, Mathf.Min(sx, sy));
            GUIStyle style = new GUIStyle(EditorStyles.label)
            {
                wordWrap = true,
                fontSize = Mathf.Max(7, Mathf.RoundToInt(element.FontSize * textScale)),
                fontStyle = element.FontStyle,
                alignment = UILayoutRuntimeApplier.ResolveTextAnchor(
                    element.HorizontalAlignment,
                    element.VerticalAlignment)
            };
            if (element.Font != null)
                style.font = element.Font;
            style.normal.textColor = element.TextColor;
            GUI.Label(rect, text, style);
        }

        private DialogueNodeData FindPreviewNode(DialogueDefinitionData dialogue)
        {
            if (dialogue == null)
                return null;

            for (int i = 0; i < dialogue.Nodes.Count; i++)
            {
                DialogueNodeData node = dialogue.Nodes[i];
                if (node != null && node.Id == dialogue.StartNodeId)
                    return node;
            }

            return dialogue.Nodes.Count > 0 ? dialogue.Nodes[0] : null;
        }

        private static Rect ToCanvasRect(Rect canvas, Rect referenceRect, float sx, float sy)
        {
            return new Rect(
                canvas.x + referenceRect.x * sx,
                canvas.y + referenceRect.y * sy,
                referenceRect.width * sx,
                referenceRect.height * sy);
        }

        private static void DrawElementImage(
            Rect draw,
            UILayoutElementDefinition element,
            float sx,
            float sy)
        {
            Texture texture = element.Sprite != null
                ? element.Sprite.texture
                : element.Texture;
            if (texture == null)
                return;

            GUI.BeginGroup(draw);
            Vector2 center = new Vector2(draw.width * 0.5f, draw.height * 0.5f);
            Vector2 size = new Vector2(draw.width, draw.height) * element.ImageScale;
            Vector2 offset = new Vector2(element.ImageOffset.x * sx, element.ImageOffset.y * sy);
            Rect imageRect = new Rect(
                center.x - size.x * 0.5f + offset.x,
                center.y - size.y * 0.5f + offset.y,
                size.x,
                size.y);
            ScaleMode scaleMode = element.ImageMode == UILayoutImageMode.Stretch
                ? ScaleMode.StretchToFill
                : element.ImageMode == UILayoutImageMode.Contain
                    ? ScaleMode.ScaleToFit
                    : ScaleMode.ScaleAndCrop;
            Color previous = GUI.color;
            Color tint = element.Tint;
            tint.a *= element.Opacity;
            GUI.color = tint;
            GUI.DrawTexture(imageRect, texture, scaleMode, true);
            GUI.color = previous;
            GUI.EndGroup();
        }

        private void HandleSelectedElement(
            Rect draw,
            float sx,
            float sy,
            UILayoutElementDefinition element)
        {
            Rect handle = new Rect(draw.xMax - 10f, draw.yMax - 10f, 20f, 20f);
            EditorGUI.DrawRect(handle, new Color(0.95f, 0.7f, 0.2f, 1f));
            Event e = Event.current;

            if (e.type == EventType.MouseDown && e.button == 0)
            {
                if (handle.Contains(e.mousePosition))
                {
                    resizing = true;
                    dragStart = e.mousePosition;
                    rectStart = element.Rect;
                    e.Use();
                }
                else if (draw.Contains(e.mousePosition))
                {
                    dragging = true;
                    dragStart = e.mousePosition;
                    rectStart = element.Rect;
                    e.Use();
                }
            }

            if (e.type == EventType.MouseDrag && (dragging || resizing))
            {
                Vector2 delta = e.mousePosition - dragStart;
                Undo.RecordObject(database, "Edit UI Layout");
                Rect r = rectStart;
                if (dragging)
                {
                    r.x += delta.x / Mathf.Max(0.001f, sx);
                    r.y += delta.y / Mathf.Max(0.001f, sy);
                }
                else
                {
                    r.width = Mathf.Max(8f, r.width + delta.x / Mathf.Max(0.001f, sx));
                    r.height = Mathf.Max(8f, r.height + delta.y / Mathf.Max(0.001f, sy));
                }

                element.SetRect(r);
                EditorUtility.SetDirty(database);
                Repaint();
                e.Use();
            }

            if (e.type == EventType.MouseUp)
            {
                dragging = false;
                resizing = false;
            }

            if (e.type == EventType.KeyDown && !EditorGUIUtility.editingTextField)
            {
                Vector2 delta = Vector2.zero;
                float step = e.shift ? 10f : 1f;
                if (e.keyCode == KeyCode.LeftArrow) delta.x = -step;
                if (e.keyCode == KeyCode.RightArrow) delta.x = step;
                if (e.keyCode == KeyCode.UpArrow) delta.y = -step;
                if (e.keyCode == KeyCode.DownArrow) delta.y = step;
                if (delta != Vector2.zero)
                {
                    Undo.RecordObject(database, "Nudge UI Layout");
                    Rect r = element.Rect;
                    r.position += delta;
                    element.SetRect(r);
                    EditorUtility.SetDirty(database);
                    Repaint();
                    e.Use();
                }
            }
        }

        private void DrawRightPanel()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(RightWidth));
            rightScroll = EditorGUILayout.BeginScrollView(rightScroll);
            UILayoutElementDefinition element = CurrentElement;
            if (database == null || element == null)
            {
                EditorGUILayout.HelpBox("Выберите элемент.", MessageType.Info);
            }
            else
            {
                EditorGUILayout.LabelField(element.DisplayName, EditorStyles.boldLabel);
                SerializedObject so = new SerializedObject(database);
                SerializedProperty screens = so.FindProperty("screens");
                SerializedProperty screen = screens.GetArrayElementAtIndex(screenIndex);
                SerializedProperty elements = screen.FindPropertyRelative("elements");
                SerializedProperty selected = elements.GetArrayElementAtIndex(elementIndex);

                EditorGUI.BeginChangeCheck();

                if (CurrentScreen != null && CurrentScreen.Id == "narrative-dialogue")
                {
                    EditorGUILayout.LabelField("ЭКРАН", EditorStyles.boldLabel);
                    EditorGUILayout.PropertyField(
                        screen.FindPropertyRelative("dimmingOpacity"),
                        new GUIContent("Затемнение фона"));
                    EditorGUILayout.Space(8f);
                }

                EditorGUILayout.PropertyField(
                    selected.FindPropertyRelative("parentId"),
                    new GUIContent("Родитель ID"));
                EditorGUILayout.PropertyField(selected.FindPropertyRelative("rect"));

                if (IsTextElement(element.Id))
                {
                    EditorGUILayout.Space(8f);
                    EditorGUILayout.LabelField("ТЕКСТ", EditorStyles.boldLabel);
                    EditorGUILayout.PropertyField(selected.FindPropertyRelative("font"), new GUIContent("Шрифт"));
                    EditorGUILayout.PropertyField(selected.FindPropertyRelative("fontSize"), new GUIContent("Размер"));
                    EditorGUILayout.PropertyField(selected.FindPropertyRelative("textColor"), new GUIContent("Цвет"));
                    EditorGUILayout.PropertyField(selected.FindPropertyRelative("fontStyle"), new GUIContent("Начертание"));
                    EditorGUILayout.PropertyField(selected.FindPropertyRelative("horizontalAlignment"), new GUIContent("По горизонтали"));
                    EditorGUILayout.PropertyField(selected.FindPropertyRelative("verticalAlignment"), new GUIContent("По вертикали"));
                }

                EditorGUILayout.Space(8f);
                EditorGUILayout.LabelField("ИЗОБРАЖЕНИЕ", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(selected.FindPropertyRelative("sprite"));
                EditorGUILayout.PropertyField(selected.FindPropertyRelative("texture"));
                EditorGUILayout.PropertyField(selected.FindPropertyRelative("imageMode"));
                EditorGUILayout.PropertyField(selected.FindPropertyRelative("imageScale"));
                EditorGUILayout.PropertyField(selected.FindPropertyRelative("imageOffset"));
                EditorGUILayout.PropertyField(selected.FindPropertyRelative("tint"));
                EditorGUILayout.PropertyField(selected.FindPropertyRelative("opacity"));
                if (EditorGUI.EndChangeCheck())
                {
                    so.ApplyModifiedProperties();
                    EditorUtility.SetDirty(database);
                    Repaint();
                }

                GUILayout.Space(8f);
                if (GUILayout.Button("Сбросить положение изображения"))
                {
                    Undo.RecordObject(database, "Reset UI Image Transform");
                    element.ResetImageTransform();
                    EditorUtility.SetDirty(database);
                    Repaint();
                }

                if (GUILayout.Button("Сохранить Asset"))
                {
                    EditorUtility.SetDirty(database);
                    AssetDatabase.SaveAssets();
                }

                if (GUILayout.Button("Проверить базу"))
                {
                    List<string> issues = new List<string>();
                    database.CollectValidationIssues(issues);
                    if (issues.Count == 0)
                        Debug.Log("UI Layout Database: ошибок не найдено.");
                    else
                        Debug.LogWarning("UI Layout Database:\n" + string.Join("\n", issues));
                }
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private static bool IsTextElement(string elementId)
        {
            return elementId == "speaker" ||
                   elementId == "role" ||
                   elementId == "text" ||
                   elementId == "choices";
        }

        private DialogueDefinitionData CurrentPreviewDialogue
        {
            get
            {
                if (dialogueDatabase == null || dialogueDatabase.Dialogues.Count == 0)
                    return null;
                previewDialogueIndex = Mathf.Clamp(previewDialogueIndex, 0, dialogueDatabase.Dialogues.Count - 1);
                return dialogueDatabase.Dialogues[previewDialogueIndex];
            }
        }

        private UILayoutScreenDefinition CurrentScreen
        {
            get
            {
                if (database == null || database.Screens.Count == 0)
                    return null;
                screenIndex = Mathf.Clamp(screenIndex, 0, database.Screens.Count - 1);
                return database.Screens[screenIndex];
            }
        }

        private UILayoutElementDefinition CurrentElement
        {
            get
            {
                UILayoutScreenDefinition screen = CurrentScreen;
                if (screen == null || screen.Elements.Count == 0)
                    return null;
                elementIndex = Mathf.Clamp(elementIndex, 0, screen.Elements.Count - 1);
                return screen.Elements[elementIndex];
            }
        }
    }
}

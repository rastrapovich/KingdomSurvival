using System;
using System.Collections.Generic;
using KingdomSurvival.DialogueDatabase;
using UnityEditor;
using UnityEngine;

namespace KingdomSurvival.UILayout.Editor
{
    public sealed class UILayoutEditorWindow : EditorWindow
    {
        private const float LeftWidth = 240f;
        private const float RightWidth = 320f;
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
        private bool showPreviewContent = true;

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

        // ------------------------------------------------------------------
        // Левая панель: экраны и элементы
        // ------------------------------------------------------------------

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

            EditorGUILayout.LabelField("ЭКРАНЫ", EditorStyles.boldLabel);
            IReadOnlyList<UILayoutScreenDefinition> screens = database.Screens;
            for (int i = 0; i < screens.Count; i++)
            {
                UILayoutScreenDefinition entry = screens[i];
                string label = entry.DisplayName;
                if (entry.AutoApply)
                    label += "  ●";
                if (GUILayout.Toggle(screenIndex == i, label, "Button"))
                {
                    if (screenIndex != i)
                    {
                        screenIndex = i;
                        elementIndex = 0;
                    }
                }
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("+ Экран"))
                AddScreen();
            using (new EditorGUI.DisabledScope(CurrentScreen == null))
            {
                if (GUILayout.Button("Дублировать"))
                    DuplicateScreen();
                if (GUILayout.Button("−"))
                    DeleteScreen();
            }

            EditorGUILayout.EndHorizontal();

            UILayoutScreenDefinition screen = CurrentScreen;
            if (screen != null)
            {
                GUILayout.Space(10f);
                EditorGUILayout.LabelField("ЭЛЕМЕНТЫ", EditorStyles.boldLabel);
                for (int i = 0; i < screen.Elements.Count; i++)
                {
                    UILayoutElementDefinition element = screen.Elements[i];
                    string indent = string.IsNullOrWhiteSpace(element.ParentId) ? string.Empty : "   ↳ ";
                    string mark = element.IsPortrait ||
                                  element.OverrideRect ||
                                  element.OverrideBackground ||
                                  element.OverrideText
                        ? " ●"
                        : string.Empty;
                    if (GUILayout.Toggle(elementIndex == i, indent + element.DisplayName + mark, "Button"))
                        elementIndex = i;
                }

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("+ Элемент"))
                    AddElement();
                using (new EditorGUI.DisabledScope(CurrentElement == null))
                {
                    if (GUILayout.Button("↑"))
                        MoveElement(-1);
                    if (GUILayout.Button("↓"))
                        MoveElement(1);
                    if (GUILayout.Button("−"))
                        DeleteElement();
                }

                EditorGUILayout.EndHorizontal();
                EditorGUILayout.LabelField(
                    "● — элемент переопределяет вёрстку в игре",
                    EditorStyles.miniLabel);
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        // ------------------------------------------------------------------
        // Центр: холст
        // ------------------------------------------------------------------

        private void DrawCanvas()
        {
            EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            previewScale = EditorGUILayout.Slider("Масштаб preview", previewScale, 0.25f, 1f);
            Vector2Int reference = database != null ? database.ReferenceResolution : new Vector2Int(1920, 1080);
            EditorGUILayout.LabelField(reference.x + " × " + reference.y, EditorStyles.miniLabel);

            UILayoutScreenDefinition screen = CurrentScreen;
            showPreviewContent = EditorGUILayout.ToggleLeft("Показывать содержимое preview", showPreviewContent);
            if (screen != null && IsNarrativeDialogue(screen) && showPreviewContent)
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
            if (screen != null && screen.UsesDimming)
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
                bool narrativePreview = showPreviewContent &&
                                        IsNarrativeDialogue(screen) &&
                                        CurrentPreviewDialogue != null;
                DialogueSpeakerData narrativePreviewSpeaker = narrativePreview
                    ? FindPreviewSpeaker(CurrentPreviewDialogue)
                    : null;

                for (int i = 0; i < screen.Elements.Count; i++)
                {
                    UILayoutElementDefinition element = screen.Elements[i];
                    Rect draw = ToCanvasRect(canvas, element.Rect, sx, sy);
                    bool dynamicPortraitWillRender = narrativePreview &&
                                                     element.Id == "portrait" &&
                                                     narrativePreviewSpeaker != null &&
                                                     narrativePreviewSpeaker.Portrait != null;
                    if (!dynamicPortraitWillRender)
                        DrawElementImage(draw, element, sx, sy);
                    EditorGUI.DrawRect(
                        draw,
                        i == elementIndex
                            ? new Color(0.75f, 0.54f, 0.18f, 0.20f)
                            : new Color(0.4f, 0.4f, 0.4f, 0.08f));

                    bool hasOwnContent = narrativePreview ||
                                         (showPreviewContent && !string.IsNullOrEmpty(element.PreviewText));
                    GUI.Box(draw, hasOwnContent ? GUIContent.none : new GUIContent(element.DisplayName));

                    if (hasOwnContent)
                    {
                        Rect tag = new Rect(
                            draw.x + 2f,
                            draw.y + 2f,
                            Mathf.Min(Mathf.Max(0f, draw.width - 4f), 140f),
                            16f);
                        GUI.Label(tag, element.DisplayName, EditorStyles.miniLabel);
                    }

                    if (i == elementIndex)
                        HandleSelectedElement(draw, sx, sy, element);
                }

                if (narrativePreview)
                    DrawNarrativeContentPreview(canvas, screen, sx, sy);
                else if (showPreviewContent)
                    DrawGenericContentPreview(canvas, screen, sx, sy);
            }

            EditorGUILayout.EndVertical();
        }

        /// <summary>
        /// Preview для любого экрана: рисует текст-заглушку каждого текстового
        /// элемента его собственным шрифтом, размером, цветом и выравниванием.
        /// </summary>
        private static void DrawGenericContentPreview(
            Rect canvas,
            UILayoutScreenDefinition screen,
            float sx,
            float sy)
        {
            for (int i = 0; i < screen.Elements.Count; i++)
            {
                UILayoutElementDefinition element = screen.Elements[i];
                if (element == null || string.IsNullOrEmpty(element.PreviewText))
                    continue;
                DrawPreviewText(canvas, element, element.PreviewText, sx, sy);
            }
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

            DialogueSpeakerData speaker = FindPreviewSpeaker(dialogue);

            UILayoutElementDefinition portrait = screen.FindElement("portrait");
            if (portrait != null && speaker != null && speaker.Portrait != null)
            {
                Rect portraitRect = ToCanvasRect(canvas, portrait.Rect, sx, sy);
                DrawNarrativePortraitImage(
                    portraitRect,
                    portrait,
                    speaker,
                    database != null ? (Vector2)database.ReferenceResolution : new Vector2(1920f, 1080f),
                    new Vector2(canvas.width, canvas.height));
            }

            if (speaker != null)
            {
                DrawPreviewText(canvas, screen.FindElement("speaker"), speaker.DisplayName, sx, sy);
                DrawPreviewText(canvas, screen.FindElement("role"), speaker.Role, sx, sy);
            }

            DrawPreviewText(canvas, screen.FindElement("text"), node.Text, sx, sy);

            string choices = string.Empty;
            for (int i = 0; i < node.Choices.Count; i++)
            {
                if (i > 0)
                    choices += "\n\n";
                choices += "› " + node.Choices[i].Text;
            }

            DrawPreviewText(canvas, screen.FindElement("choices"), choices, sx, sy);
        }

        private DialogueSpeakerData FindPreviewSpeaker(DialogueDefinitionData dialogue)
        {
            DialogueNodeData node = FindPreviewNode(dialogue);
            if (node == null || dialogueDatabase == null)
                return null;
            return dialogueDatabase.FindSpeaker(node.SpeakerId);
        }

        /// <summary>
        /// Narrative preview использует ту же математику, что runtime:
        /// UILayout задаёт общий mode/scale/offset/tint/opacity, а Speaker
        /// при включённой индивидуальной кадрировке добавляет zoom/pan/flip.
        /// </summary>
        // Инструкция "свободное кадрирование полного портрета": та же
        // математика, что и preview Базы диалогов и runtime
        // (UILayoutRuntimeApplier.ResolveImageRect) — полный прямоугольник
        // изображения, а не заранее обрезанный ScaleAndCrop. Рамка (frame)
        // только отсекает то, что физически оказалось за её пределами
        // через GUI.BeginGroup, поэтому три места (это превью, превью Базы
        // диалогов, Play Mode) больше не могут разойтись по кадрированию.
        private static void DrawNarrativePortraitImage(
            Rect frame,
            UILayoutElementDefinition definition,
            DialogueSpeakerData speaker,
            Vector2 reference,
            Vector2 actual)
        {
            if (definition == null || speaker == null || speaker.Portrait == null)
                return;

            Sprite sprite = speaker.Portrait;
            Texture texture = sprite.texture;

            bool individual = speaker.OverridePortraitFraming;
            float additionalScale = individual ? speaker.PortraitScale : 1f;
            Vector2 normalizedOffset = individual ? speaker.PortraitOffsetNormalized : Vector2.zero;
            bool flipX = individual && speaker.PortraitFlipX;

            Vector2 offset = UILayoutRuntimeApplier.ResolveImageOffset(
                definition,
                reference,
                actual,
                normalizedOffset);
            Vector3 resolvedScale = UILayoutRuntimeApplier.ResolveImageScale(
                definition,
                additionalScale,
                flipX);
            float magnitude = Mathf.Abs(resolvedScale.y);
            bool flip = resolvedScale.x < 0f;

            // §7: пропорции — по sprite.rect, а не по всей Texture (Sprite
            // Atlas: Texture тогда — весь атлас, а не конкретный портрет).
            Rect imageRect = UILayoutRuntimeApplier.ResolveImageRect(
                UILayoutRuntimeApplier.ResolveSpriteSize(sprite),
                frame.size,
                definition.ImageMode,
                magnitude,
                offset);

            GUI.BeginGroup(frame);

            Matrix4x4 previousMatrix = GUI.matrix;
            Color previousColor = GUI.color;
            Color tint = definition.Tint;
            tint.a *= definition.Opacity;
            GUI.color = tint;
            if (flip)
                GUIUtility.ScaleAroundPivot(new Vector2(-1f, 1f), imageRect.center);

            Rect spriteRect = sprite.rect;
            Rect uv = new Rect(
                spriteRect.x / texture.width,
                spriteRect.y / texture.height,
                spriteRect.width / texture.width,
                spriteRect.height / texture.height);
            GUI.DrawTextureWithTexCoords(imageRect, texture, uv, true);

            GUI.matrix = previousMatrix;
            GUI.color = previousColor;
            GUI.EndGroup();
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
            Sprite sprite = element.Sprite;
            Texture texture = sprite != null
                ? sprite.texture
                : element.Texture;
            if (texture == null)
                return;

            if (!element.IsPortrait)
            {
                GUI.BeginGroup(draw);
                Vector2 center = new Vector2(draw.width * 0.5f, draw.height * 0.5f);
                Vector2 size = new Vector2(draw.width, draw.height) * element.ImageScale;
                Vector2 legacyOffset = new Vector2(element.ImageOffset.x * sx, element.ImageOffset.y * sy);
                Rect legacyRect = new Rect(
                    center.x - size.x * 0.5f + legacyOffset.x,
                    center.y - size.y * 0.5f + legacyOffset.y,
                    size.x,
                    size.y);
                ScaleMode scaleMode = UILayoutRuntimeApplier.ResolveImageScaleMode(element.ImageMode);
                Color legacyColor = GUI.color;
                Color legacyTint = element.Tint;
                legacyTint.a *= element.Opacity;
                GUI.color = legacyTint;
                GUI.DrawTexture(legacyRect, texture, scaleMode, true);
                GUI.color = legacyColor;
                GUI.EndGroup();
                return;
            }

            Vector2 sourceSize = sprite != null
                ? UILayoutRuntimeApplier.ResolveSpriteSize(sprite)
                : new Vector2(texture.width, texture.height);
            Vector2 offset = new Vector2(element.ImageOffset.x * sx, element.ImageOffset.y * sy);
            Rect imageRect = UILayoutRuntimeApplier.ResolveImageRect(
                sourceSize,
                draw.size,
                element.ImageMode,
                element.ImageScale,
                offset);

            GUI.BeginGroup(draw);
            Color previous = GUI.color;
            Color tint = element.Tint;
            tint.a *= element.Opacity;
            GUI.color = tint;

            if (sprite != null)
            {
                Rect spriteRect = sprite.rect;
                Rect uv = new Rect(
                    spriteRect.x / texture.width,
                    spriteRect.y / texture.height,
                    spriteRect.width / texture.width,
                    spriteRect.height / texture.height);
                GUI.DrawTextureWithTexCoords(imageRect, texture, uv, true);
            }
            else
            {
                GUI.DrawTexture(imageRect, texture, ScaleMode.StretchToFill, true);
            }

            GUI.color = previous;
            GUI.EndGroup();
        }

        private void HandleSelectedElement(
            Rect draw,
            float sx,
            float sy,
            UILayoutElementDefinition element)
        {
            bool canResize = element != null && element.SupportsFreeResize;
            Rect handle = new Rect(draw.xMax - 10f, draw.yMax - 10f, 20f, 20f);
            if (canResize)
                EditorGUI.DrawRect(handle, new Color(0.95f, 0.7f, 0.2f, 1f));
            else
                resizing = false;
            Event e = Event.current;

            if (e.type == EventType.MouseDown && e.button == 0)
            {
                if (canResize && handle.Contains(e.mousePosition))
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

        // ------------------------------------------------------------------
        // Правая панель: свойства
        // ------------------------------------------------------------------

        private void DrawRightPanel()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(RightWidth));
            rightScroll = EditorGUILayout.BeginScrollView(rightScroll);

            UILayoutScreenDefinition currentScreen = CurrentScreen;
            UILayoutElementDefinition element = CurrentElement;
            if (database == null || currentScreen == null)
            {
                EditorGUILayout.HelpBox("Выберите экран.", MessageType.Info);
                EditorGUILayout.EndScrollView();
                EditorGUILayout.EndVertical();
                return;
            }

            SerializedObject so = new SerializedObject(database);
            SerializedProperty screens = so.FindProperty("screens");
            SerializedProperty screen = screens.GetArrayElementAtIndex(screenIndex);

            EditorGUI.BeginChangeCheck();

            EditorGUILayout.LabelField("ЭКРАН", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(screen.FindPropertyRelative("id"), new GUIContent("ID"));
            EditorGUILayout.PropertyField(screen.FindPropertyRelative("displayName"), new GUIContent("Название"));
            EditorGUILayout.PropertyField(screen.FindPropertyRelative("description"), new GUIContent("Описание"));
            EditorGUILayout.PropertyField(screen.FindPropertyRelative("rootName"), new GUIContent("Корень UXML"));
            EditorGUILayout.PropertyField(screen.FindPropertyRelative("usesDimming"), new GUIContent("Затемнять фон"));
            using (new EditorGUI.DisabledScope(!currentScreen.UsesDimming))
            {
                EditorGUILayout.PropertyField(
                    screen.FindPropertyRelative("dimmingOpacity"),
                    new GUIContent("Сила затемнения"));
            }

            EditorGUILayout.PropertyField(
                screen.FindPropertyRelative("autoApply"),
                new GUIContent("Применять в игре"));

            EditorGUILayout.PropertyField(
                screen.FindPropertyRelative("requiredElements"),
                new GUIContent("Обязательные элементы"),
                true);

            if (element == null)
            {
                if (EditorGUI.EndChangeCheck())
                    ApplyChanges(so);
                EditorGUILayout.Space(8f);
                EditorGUILayout.HelpBox("В экране нет элементов.", MessageType.Info);
                DrawDatabaseButtons();
                EditorGUILayout.EndScrollView();
                EditorGUILayout.EndVertical();
                return;
            }

            SerializedProperty elements = screen.FindPropertyRelative("elements");
            SerializedProperty selected = elements.GetArrayElementAtIndex(elementIndex);

            EditorGUILayout.Space(10f);
            EditorGUILayout.LabelField("ЭЛЕМЕНТ — " + element.DisplayName, EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(selected.FindPropertyRelative("id"), new GUIContent("ID"));
            EditorGUILayout.PropertyField(selected.FindPropertyRelative("displayName"), new GUIContent("Название"));
            SerializedProperty kindProperty = selected.FindPropertyRelative("kind");
            UILayoutElementKind kindBeforeEdit = element.Kind;
            DrawElementKindPopup(kindProperty);
            UILayoutElementKind editedKind = (UILayoutElementKind)kindProperty.enumValueIndex;
            bool isPortrait = editedKind == UILayoutElementKind.Portrait;
            bool isTextual = editedKind == UILayoutElementKind.Text || editedKind == UILayoutElementKind.Button;
            if (kindBeforeEdit != UILayoutElementKind.Portrait && isPortrait)
                ConvertFreeRectToPortrait(selected);
            DrawParentField(selected, element);
            EditorGUILayout.PropertyField(
                selected.FindPropertyRelative("targetName"),
                new GUIContent("Имя в UXML"));
            if (string.IsNullOrWhiteSpace(element.TargetName))
                EditorGUILayout.HelpBox("Не задано имя элемента UXML — привязка невозможна.", MessageType.Warning);

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("ПРИМЕНЕНИЕ В ИГРЕ", EditorStyles.boldLabel);
            if (isPortrait)
            {
                EditorGUILayout.HelpBox(
                    "Portrait всегда применяет preset-рамку и настройки изображения. " +
                    "Свободный размер для этого типа отключён.",
                    MessageType.None);
            }
            else
            {
                EditorGUILayout.PropertyField(
                    selected.FindPropertyRelative("overrideRect"),
                    new GUIContent("Переопределять прямоугольник"));
                EditorGUILayout.PropertyField(
                    selected.FindPropertyRelative("overrideBackground"),
                    new GUIContent("Переопределять фон"));
            }
            using (new EditorGUI.DisabledScope(!isTextual))
            {
                EditorGUILayout.PropertyField(
                    selected.FindPropertyRelative("overrideText"),
                    new GUIContent("Переопределять текст"));
            }

            bool hasRuntimeOverride = isPortrait ||
                                      selected.FindPropertyRelative("overrideRect").boolValue ||
                                      selected.FindPropertyRelative("overrideBackground").boolValue ||
                                      selected.FindPropertyRelative("overrideText").boolValue;
            if (!currentScreen.AutoApply && hasRuntimeOverride)
            {
                EditorGUILayout.HelpBox(
                    "Переопределения включены, но у экрана выключено «Применять в игре».",
                    MessageType.Warning);
            }

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("ГЕОМЕТРИЯ", EditorStyles.boldLabel);
            if (isPortrait)
                DrawPortraitGeometry(selected);
            else
                EditorGUILayout.PropertyField(selected.FindPropertyRelative("rect"), new GUIContent("Прямоугольник"));

            if (isTextual)
            {
                EditorGUILayout.Space(8f);
                EditorGUILayout.LabelField("ТЕКСТ", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(selected.FindPropertyRelative("font"), new GUIContent("Шрифт"));
                EditorGUILayout.PropertyField(selected.FindPropertyRelative("fontSize"), new GUIContent("Размер"));
                EditorGUILayout.PropertyField(selected.FindPropertyRelative("textColor"), new GUIContent("Цвет"));
                DrawFontStylePopup(selected.FindPropertyRelative("fontStyle"));
                DrawHorizontalAlignmentPopup(selected.FindPropertyRelative("horizontalAlignment"));
                DrawVerticalAlignmentPopup(selected.FindPropertyRelative("verticalAlignment"));
                EditorGUILayout.PropertyField(selected.FindPropertyRelative("previewText"), new GUIContent("Текст предпросмотра"));
            }

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("ИЗОБРАЖЕНИЕ", EditorStyles.boldLabel);
            if (IsNarrativeDialogue(currentScreen) && element.Id == "portrait")
            {
                EditorGUILayout.HelpBox(
                    "Для реального диалога Sprite берётся из Базы диалогов. Здесь задаются рамка, режим, общий масштаб/смещение, оттенок и fallback изображения.",
                    MessageType.None);
            }
            EditorGUILayout.PropertyField(selected.FindPropertyRelative("sprite"), new GUIContent("Спрайт"));
            EditorGUILayout.PropertyField(selected.FindPropertyRelative("texture"), new GUIContent("Текстура"));
            if (isPortrait)
                DrawPortraitImageModePopup(selected.FindPropertyRelative("imageMode"));
            else
                DrawImageModePopup(selected.FindPropertyRelative("imageMode"));
            EditorGUILayout.PropertyField(selected.FindPropertyRelative("imageScale"), new GUIContent("Масштаб"));
            EditorGUILayout.PropertyField(selected.FindPropertyRelative("imageOffset"), new GUIContent("Смещение"));
            EditorGUILayout.PropertyField(selected.FindPropertyRelative("tint"), new GUIContent("Оттенок"));
            EditorGUILayout.PropertyField(selected.FindPropertyRelative("opacity"), new GUIContent("Непрозрачность"));

            if (EditorGUI.EndChangeCheck() || so.hasModifiedProperties)
                ApplyChanges(so);

            GUILayout.Space(8f);
            if (GUILayout.Button("Сбросить положение изображения"))
            {
                Undo.RecordObject(database, "Reset UI Image Transform");
                element.ResetImageTransform();
                EditorUtility.SetDirty(database);
                Repaint();
            }

            DrawDatabaseButtons();
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        /// <summary>
        /// Выпадающий выбор родителя из элементов того же экрана вместо
        /// ручного ввода строки.
        /// </summary>
        private void DrawParentField(
            SerializedProperty selected,
            UILayoutElementDefinition element)
        {
            UILayoutScreenDefinition current = CurrentScreen;
            if (current == null)
                return;

            List<string> options = new List<string> { "— корень экрана —" };
            List<string> ids = new List<string> { string.Empty };
            for (int i = 0; i < current.Elements.Count; i++)
            {
                UILayoutElementDefinition candidate = current.Elements[i];
                if (candidate == null || candidate == element)
                    continue;
                options.Add(candidate.DisplayName);
                ids.Add(candidate.Id);
            }

            int index = ids.IndexOf(element.ParentId);
            if (index < 0)
                index = 0;
            int picked = EditorGUILayout.Popup("Родитель", index, options.ToArray());
            if (picked != index)
                selected.FindPropertyRelative("parentId").stringValue = ids[picked];
        }

        // Русские подписи для перечислений (§19 русификации): обычный
        // PropertyField на enum-поле рисует английские имена констант
        // C#, поэтому здесь — свои Popup с переводом, как ChoiceKindLabel
        // в DialogueDatabaseWindow.

        private static readonly UILayoutElementKind[] ElementKindValues =
            (UILayoutElementKind[])Enum.GetValues(typeof(UILayoutElementKind));

        private static string ElementKindLabel(UILayoutElementKind kind)
        {
            switch (kind)
            {
                case UILayoutElementKind.Panel: return "Панель";
                case UILayoutElementKind.Text: return "Текст";
                case UILayoutElementKind.Image: return "Изображение";
                case UILayoutElementKind.Button: return "Кнопка";
                case UILayoutElementKind.Container: return "Контейнер";
                case UILayoutElementKind.Portrait: return "Портрет";
                default: return kind.ToString();
            }
        }

        private static void DrawElementKindPopup(SerializedProperty kindProperty)
        {
            string[] labels = new string[ElementKindValues.Length];
            int selected = 0;
            for (int i = 0; i < ElementKindValues.Length; i++)
            {
                labels[i] = ElementKindLabel(ElementKindValues[i]);
                if (kindProperty.enumValueIndex == (int)ElementKindValues[i])
                    selected = i;
            }

            int next = EditorGUILayout.Popup("Тип", selected, labels);
            if (next >= 0 && next < ElementKindValues.Length)
                kindProperty.enumValueIndex = (int)ElementKindValues[next];
        }

        private static readonly PortraitSize[] PortraitSizeValues =
            (PortraitSize[])Enum.GetValues(typeof(PortraitSize));

        private static void ConvertFreeRectToPortrait(SerializedProperty selected)
        {
            SerializedProperty rectProperty = selected.FindPropertyRelative("rect");
            SerializedProperty sizeProperty = selected.FindPropertyRelative("portraitSize");
            Rect current = rectProperty.rectValue;
            PortraitSize nearest = PortraitSizeTable.FindNearest(current.width, current.height);
            sizeProperty.enumValueIndex = (int)nearest;
            rectProperty.rectValue = UILayoutPortraitRect.ResizeKeepingCenter(current, nearest);
            selected.FindPropertyRelative("overrideRect").boolValue = false;
            selected.FindPropertyRelative("overrideBackground").boolValue = false;
            selected.FindPropertyRelative("overrideText").boolValue = false;

            SerializedProperty imageMode = selected.FindPropertyRelative("imageMode");
            if (imageMode.enumValueIndex == (int)UILayoutImageMode.Stretch)
                imageMode.enumValueIndex = (int)UILayoutImageMode.Cover;
        }

        private static void DrawPortraitGeometry(SerializedProperty selected)
        {
            SerializedProperty rectProperty = selected.FindPropertyRelative("rect");
            SerializedProperty sizeProperty = selected.FindPropertyRelative("portraitSize");
            Rect current = rectProperty.rectValue;

            int selectedIndex = Mathf.Clamp(sizeProperty.enumValueIndex, 0, PortraitSizeValues.Length - 1);
            string[] labels = new string[PortraitSizeValues.Length];
            for (int i = 0; i < PortraitSizeValues.Length; i++)
            {
                PortraitSizeDefinition definition = PortraitSizeTable.Get(PortraitSizeValues[i]);
                labels[i] = definition.Size + " — " + definition.Width + " × " + definition.Height + " px";
            }

            int nextIndex = EditorGUILayout.Popup("Размер портрета", selectedIndex, labels);
            PortraitSize nextSize = PortraitSizeValues[Mathf.Clamp(nextIndex, 0, PortraitSizeValues.Length - 1)];
            if (nextIndex != selectedIndex)
            {
                sizeProperty.enumValueIndex = (int)nextSize;
                current = UILayoutPortraitRect.ResizeKeepingCenter(current, nextSize);
                rectProperty.rectValue = current;
            }
            else
            {
                Rect normalized = UILayoutPortraitRect.ResizeKeepingCenter(current, nextSize);
                if (!RectsApproximatelyEqual(normalized, current))
                {
                    current = normalized;
                    rectProperty.rectValue = current;
                }
            }

            EditorGUI.BeginChangeCheck();
            float x = EditorGUILayout.FloatField("X", current.x);
            float y = EditorGUILayout.FloatField("Y", current.y);
            if (EditorGUI.EndChangeCheck())
            {
                current.x = x;
                current.y = y;
                rectProperty.rectValue = current;
            }

            PortraitSizeDefinition resolved = PortraitSizeTable.Get(nextSize);
            EditorGUILayout.LabelField("Размер рамки", resolved.Width + " × " + resolved.Height + " px (5:7)");
            EditorGUILayout.HelpBox(
                "Рамку можно перемещать по X/Y. Изменение Width/Height и resize мышью отключены.",
                MessageType.None);
        }

        private static bool RectsApproximatelyEqual(Rect left, Rect right)
        {
            return Mathf.Approximately(left.x, right.x) &&
                   Mathf.Approximately(left.y, right.y) &&
                   Mathf.Approximately(left.width, right.width) &&
                   Mathf.Approximately(left.height, right.height);
        }

        private static readonly FontStyle[] FontStyleValues =
            (FontStyle[])Enum.GetValues(typeof(FontStyle));

        private static string FontStyleLabel(FontStyle style)
        {
            switch (style)
            {
                case FontStyle.Normal: return "Обычное";
                case FontStyle.Bold: return "Жирное";
                case FontStyle.Italic: return "Курсив";
                case FontStyle.BoldAndItalic: return "Жирный курсив";
                default: return style.ToString();
            }
        }

        private static void DrawFontStylePopup(SerializedProperty fontStyleProperty)
        {
            string[] labels = new string[FontStyleValues.Length];
            int selected = 0;
            for (int i = 0; i < FontStyleValues.Length; i++)
            {
                labels[i] = FontStyleLabel(FontStyleValues[i]);
                if (fontStyleProperty.enumValueIndex == (int)FontStyleValues[i])
                    selected = i;
            }

            int next = EditorGUILayout.Popup("Начертание", selected, labels);
            if (next >= 0 && next < FontStyleValues.Length)
                fontStyleProperty.enumValueIndex = (int)FontStyleValues[next];
        }

        private static readonly UILayoutImageMode[] ImageModeValues =
            (UILayoutImageMode[])Enum.GetValues(typeof(UILayoutImageMode));

        private static string ImageModeLabel(UILayoutImageMode mode)
        {
            switch (mode)
            {
                case UILayoutImageMode.Cover: return "Заполнить (с обрезкой)";
                case UILayoutImageMode.Contain: return "Вписать целиком";
                case UILayoutImageMode.Stretch: return "Растянуть";
                default: return mode.ToString();
            }
        }

        private static void DrawImageModePopup(SerializedProperty imageModeProperty)
        {
            string[] labels = new string[ImageModeValues.Length];
            int selected = 0;
            for (int i = 0; i < ImageModeValues.Length; i++)
            {
                labels[i] = ImageModeLabel(ImageModeValues[i]);
                if (imageModeProperty.enumValueIndex == (int)ImageModeValues[i])
                    selected = i;
            }

            int next = EditorGUILayout.Popup("Режим изображения", selected, labels);
            if (next >= 0 && next < ImageModeValues.Length)
                imageModeProperty.enumValueIndex = (int)ImageModeValues[next];
        }

        private static readonly UILayoutImageMode[] PortraitImageModeValues =
        {
            UILayoutImageMode.Cover,
            UILayoutImageMode.Contain
        };

        private static void DrawPortraitImageModePopup(SerializedProperty imageModeProperty)
        {
            int selected = 0;
            for (int i = 0; i < PortraitImageModeValues.Length; i++)
            {
                if (imageModeProperty.enumValueIndex == (int)PortraitImageModeValues[i])
                    selected = i;
            }

            if (imageModeProperty.enumValueIndex == (int)UILayoutImageMode.Stretch)
                imageModeProperty.enumValueIndex = (int)UILayoutImageMode.Cover;

            string[] labels = new string[PortraitImageModeValues.Length];
            for (int i = 0; i < PortraitImageModeValues.Length; i++)
                labels[i] = ImageModeLabel(PortraitImageModeValues[i]);

            int next = EditorGUILayout.Popup("Режим изображения", selected, labels);
            if (next >= 0 && next < PortraitImageModeValues.Length)
                imageModeProperty.enumValueIndex = (int)PortraitImageModeValues[next];
        }

        private static readonly UILayoutTextHorizontalAlignment[] HorizontalAlignmentValues =
            (UILayoutTextHorizontalAlignment[])Enum.GetValues(typeof(UILayoutTextHorizontalAlignment));

        private static string HorizontalAlignmentLabel(UILayoutTextHorizontalAlignment alignment)
        {
            switch (alignment)
            {
                case UILayoutTextHorizontalAlignment.Left: return "Слева";
                case UILayoutTextHorizontalAlignment.Center: return "По центру";
                case UILayoutTextHorizontalAlignment.Right: return "Справа";
                default: return alignment.ToString();
            }
        }

        private static void DrawHorizontalAlignmentPopup(SerializedProperty alignmentProperty)
        {
            string[] labels = new string[HorizontalAlignmentValues.Length];
            int selected = 0;
            for (int i = 0; i < HorizontalAlignmentValues.Length; i++)
            {
                labels[i] = HorizontalAlignmentLabel(HorizontalAlignmentValues[i]);
                if (alignmentProperty.enumValueIndex == (int)HorizontalAlignmentValues[i])
                    selected = i;
            }

            int next = EditorGUILayout.Popup("По горизонтали", selected, labels);
            if (next >= 0 && next < HorizontalAlignmentValues.Length)
                alignmentProperty.enumValueIndex = (int)HorizontalAlignmentValues[next];
        }

        private static readonly UILayoutTextVerticalAlignment[] VerticalAlignmentValues =
            (UILayoutTextVerticalAlignment[])Enum.GetValues(typeof(UILayoutTextVerticalAlignment));

        private static string VerticalAlignmentLabel(UILayoutTextVerticalAlignment alignment)
        {
            switch (alignment)
            {
                case UILayoutTextVerticalAlignment.Top: return "Сверху";
                case UILayoutTextVerticalAlignment.Middle: return "По центру";
                case UILayoutTextVerticalAlignment.Bottom: return "Снизу";
                default: return alignment.ToString();
            }
        }

        private static void DrawVerticalAlignmentPopup(SerializedProperty alignmentProperty)
        {
            string[] labels = new string[VerticalAlignmentValues.Length];
            int selected = 0;
            for (int i = 0; i < VerticalAlignmentValues.Length; i++)
            {
                labels[i] = VerticalAlignmentLabel(VerticalAlignmentValues[i]);
                if (alignmentProperty.enumValueIndex == (int)VerticalAlignmentValues[i])
                    selected = i;
            }

            int next = EditorGUILayout.Popup("По вертикали", selected, labels);
            if (next >= 0 && next < VerticalAlignmentValues.Length)
                alignmentProperty.enumValueIndex = (int)VerticalAlignmentValues[next];
        }

        private void DrawDatabaseButtons()
        {
            GUILayout.Space(8f);
            if (GUILayout.Button("Сохранить Asset"))
            {
                database.NormalizePortraitFrames();
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

        private void ApplyChanges(SerializedObject so)
        {
            so.ApplyModifiedProperties();
            database.NormalizePortraitFrames();
            EditorUtility.SetDirty(database);
            Repaint();
        }

        // ------------------------------------------------------------------
        // Операции над экранами и элементами
        // ------------------------------------------------------------------

        private void AddScreen()
        {
            Undo.RecordObject(database, "Add UI Screen");
            SerializedObject so = new SerializedObject(database);
            SerializedProperty screens = so.FindProperty("screens");
            int index = screens.arraySize;
            screens.InsertArrayElementAtIndex(index);
            SerializedProperty screen = screens.GetArrayElementAtIndex(index);
            screen.FindPropertyRelative("id").stringValue = "new-screen-" + (index + 1);
            screen.FindPropertyRelative("displayName").stringValue = "Новый экран";
            screen.FindPropertyRelative("description").stringValue = string.Empty;
            screen.FindPropertyRelative("rootName").stringValue = string.Empty;
            screen.FindPropertyRelative("usesDimming").boolValue = false;
            screen.FindPropertyRelative("autoApply").boolValue = false;
            screen.FindPropertyRelative("requiredElements").ClearArray();
            screen.FindPropertyRelative("elements").ClearArray();
            ApplyChanges(so);
            screenIndex = index;
            elementIndex = 0;
        }

        private void DuplicateScreen()
        {
            UILayoutScreenDefinition source = CurrentScreen;
            if (source == null)
                return;

            Undo.RecordObject(database, "Duplicate UI Screen");
            SerializedObject so = new SerializedObject(database);
            SerializedProperty screens = so.FindProperty("screens");
            screens.InsertArrayElementAtIndex(screenIndex);
            SerializedProperty copy = screens.GetArrayElementAtIndex(screenIndex + 1);
            copy.FindPropertyRelative("id").stringValue = source.Id + "-copy";
            copy.FindPropertyRelative("displayName").stringValue = source.DisplayName + " (копия)";
            copy.FindPropertyRelative("autoApply").boolValue = false;
            ApplyChanges(so);
            screenIndex += 1;
            elementIndex = 0;
        }

        private void DeleteScreen()
        {
            UILayoutScreenDefinition screen = CurrentScreen;
            if (screen == null)
                return;

            if (!EditorUtility.DisplayDialog(
                    "Удалить экран",
                    "Удалить экран «" + screen.DisplayName + "» со всеми элементами?",
                    "Удалить",
                    "Отмена"))
                return;

            Undo.RecordObject(database, "Delete UI Screen");
            SerializedObject so = new SerializedObject(database);
            SerializedProperty screens = so.FindProperty("screens");
            screens.DeleteArrayElementAtIndex(screenIndex);
            ApplyChanges(so);
            screenIndex = Mathf.Max(0, screenIndex - 1);
            elementIndex = 0;
        }

        private void AddElement()
        {
            UILayoutScreenDefinition screen = CurrentScreen;
            if (screen == null)
                return;

            Undo.RecordObject(database, "Add UI Element");
            SerializedObject so = new SerializedObject(database);
            SerializedProperty screens = so.FindProperty("screens");
            SerializedProperty screenProperty = screens.GetArrayElementAtIndex(screenIndex);
            SerializedProperty elements = screenProperty.FindPropertyRelative("elements");
            int index = elements.arraySize;
            elements.InsertArrayElementAtIndex(index);
            SerializedProperty element = elements.GetArrayElementAtIndex(index);
            element.FindPropertyRelative("id").stringValue = "element-" + (index + 1);
            element.FindPropertyRelative("displayName").stringValue = "Новый элемент";
            element.FindPropertyRelative("parentId").stringValue = string.Empty;
            element.FindPropertyRelative("kind").enumValueIndex = (int)UILayoutElementKind.Panel;
            element.FindPropertyRelative("targetName").stringValue = string.Empty;
            element.FindPropertyRelative("overrideRect").boolValue = false;
            element.FindPropertyRelative("overrideBackground").boolValue = false;
            element.FindPropertyRelative("overrideText").boolValue = false;
            element.FindPropertyRelative("previewText").stringValue = string.Empty;
            element.FindPropertyRelative("rect").rectValue = new Rect(80f, 80f, 320f, 180f);
            element.FindPropertyRelative("portraitSize").enumValueIndex = (int)PortraitSize.M;
            ApplyChanges(so);
            elementIndex = index;
        }

        private void DeleteElement()
        {
            UILayoutScreenDefinition screen = CurrentScreen;
            UILayoutElementDefinition element = CurrentElement;
            if (screen == null || element == null)
                return;

            if (!EditorUtility.DisplayDialog(
                    "Удалить элемент",
                    "Удалить элемент «" + element.DisplayName + "»?",
                    "Удалить",
                    "Отмена"))
                return;

            Undo.RecordObject(database, "Delete UI Element");
            SerializedObject so = new SerializedObject(database);
            SerializedProperty screens = so.FindProperty("screens");
            SerializedProperty screenProperty = screens.GetArrayElementAtIndex(screenIndex);
            SerializedProperty elements = screenProperty.FindPropertyRelative("elements");
            elements.DeleteArrayElementAtIndex(elementIndex);
            ApplyChanges(so);
            elementIndex = Mathf.Max(0, elementIndex - 1);
        }

        private void MoveElement(int offset)
        {
            UILayoutScreenDefinition screen = CurrentScreen;
            if (screen == null)
                return;

            int target = elementIndex + offset;
            if (target < 0 || target >= screen.Elements.Count)
                return;

            Undo.RecordObject(database, "Move UI Element");
            SerializedObject so = new SerializedObject(database);
            SerializedProperty screens = so.FindProperty("screens");
            SerializedProperty screenProperty = screens.GetArrayElementAtIndex(screenIndex);
            SerializedProperty elements = screenProperty.FindPropertyRelative("elements");
            elements.MoveArrayElement(elementIndex, target);
            ApplyChanges(so);
            elementIndex = target;
        }

        private static bool IsNarrativeDialogue(UILayoutScreenDefinition screen)
        {
            return screen != null &&
                   screen.Id == UILayoutDatabaseAsset.NarrativeDialogueScreenId;
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

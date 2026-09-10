using KingdomSurvival.UILayout;
using UnityEditor;
using UnityEngine;

namespace KingdomSurvival.DialogueDatabase.Editor
{
    public sealed partial class DialogueDatabaseWindow : EditorWindow
    {
        private bool speakerPortraitDragging;
        private Vector2 speakerPortraitDragStartMouse;
        private Vector2 speakerPortraitDragStartOffset;

        // Верхняя граница чисто визуального размера preview-панели — не
        // участвует в расчёте кадрирования (см. ResolvePreviewDisplaySize).
        private const float PreviewMaxWidth = 340f;
        private const float PreviewMaxHeight = 460f;

        /// <summary>
        /// Редактор индивидуальной кадрировки говорящего. Общая рамка, режим
        /// Cover/Contain/Stretch, общий scale/offset, tint и opacity всегда
        /// остаются в UI Конструкторе; здесь хранится только добавка конкретного NPC.
        /// </summary>
        private void DrawSpeakerPortraitFraming(SerializedProperty speaker)
        {
            if (speaker == null)
                return;

            SerializedProperty portrait = speaker.FindPropertyRelative("portrait");
            SerializedProperty overrideFraming = speaker.FindPropertyRelative("overridePortraitFraming");
            SerializedProperty scale = speaker.FindPropertyRelative("portraitScale");
            SerializedProperty offset = speaker.FindPropertyRelative("portraitOffsetNormalized");
            SerializedProperty flipX = speaker.FindPropertyRelative("portraitFlipX");

            EditorGUILayout.Space(10f);
            EditorGUILayout.LabelField("КАДРИРОВАНИЕ ПОРТРЕТА", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Рамка и общая композиция берутся из UI Конструктора → Диалог → Портрет. " +
                "Настройки ниже добавляются только для выбранного говорящего.",
                MessageType.None);

            EditorGUILayout.PropertyField(
                overrideFraming,
                new GUIContent("Индивидуальная настройка"));

            if (overrideFraming.boolValue && scale.floatValue <= 0f)
                scale.floatValue = 1f;

            using (new EditorGUI.DisabledScope(!overrideFraming.boolValue))
            {
                scale.floatValue = EditorGUILayout.Slider(
                    "Масштаб",
                    Mathf.Max(0.05f, scale.floatValue),
                    0.05f,
                    4f);
                EditorGUILayout.PropertyField(
                    offset,
                    new GUIContent("Смещение (доля рамки)"));
                EditorGUILayout.PropertyField(
                    flipX,
                    new GUIContent("Отразить по X"));

                if (GUILayout.Button("Сбросить индивидуальную кадрировку"))
                {
                    scale.floatValue = 1f;
                    offset.vector2Value = Vector2.zero;
                    flipX.boolValue = false;
                }
            }

            Sprite sprite = portrait != null ? portrait.objectReferenceValue as Sprite : null;
            UILayoutDatabaseAsset layoutDatabase = UILayoutRuntimeApplier.LoadDefaultDatabase();
            UILayoutScreenDefinition screen = layoutDatabase != null
                ? layoutDatabase.FindScreen(UILayoutDatabaseAsset.NarrativeDialogueScreenId)
                : null;
            UILayoutElementDefinition portraitDefinition = screen != null
                ? screen.FindElement("portrait")
                : null;

            if (layoutDatabase == null || portraitDefinition == null)
            {
                EditorGUILayout.HelpBox(
                    "Не найден UI Layout экрана диалога или элемент portrait. Точный preview недоступен.",
                    MessageType.Warning);
                return;
            }

            // Инструкция "редактор и runtime должны совпадать пиксель-в-
            // пиксель": раньше preview придумывал "actual" из отношения
            // ТОЛЬКО ширины preview-панели к ширине layout-рамки, а высоту
            // preview дополнительно клэмпил в диапазон [180, 430] — из-за
            // этого рамка preview могла иметь ДРУГОЕ соотношение сторон,
            // чем настоящая рамка в игре, и Cover/Contain кадрировали
            // иначе. Теперь реальная (true) рамка считается ТЕМ ЖЕ
            // UILayoutRuntimeApplier.ResolveImageFrameSize, что и runtime
            // (§1-2), а под preview-панель она лишь равномерно уменьшается
            // БЕЗ искажения пропорций (§3) — это чисто визуальный zoom,
            // не участвующий в математике кадрирования.
            Vector2 reference = layoutDatabase.ReferenceResolution;
            Vector2 actual = ResolveEditorPreviewActualResolution(reference);
            Vector2 trueFrameSize = UILayoutRuntimeApplier.ResolveImageFrameSize(portraitDefinition, reference, actual);
            if (trueFrameSize.x <= 0f || trueFrameSize.y <= 0f)
                trueFrameSize = portraitDefinition.Rect.size;

            Vector2 previewSize = ResolvePreviewDisplaySize(trueFrameSize, PreviewMaxWidth, PreviewMaxHeight);
            Rect previewRect = GUILayoutUtility.GetRect(
                previewSize.x,
                previewSize.y,
                GUILayout.Width(previewSize.x),
                GUILayout.Height(previewSize.y));

            EditorGUI.DrawRect(previewRect, new Color(0.07f, 0.08f, 0.09f, 1f));

            bool hasSpeakerPortrait = sprite != null;
            Texture fallbackTexture = portraitDefinition.Sprite != null
                ? portraitDefinition.Sprite.texture
                : portraitDefinition.Texture;
            Texture texture = hasSpeakerPortrait ? sprite.texture : fallbackTexture;

            if (texture != null)
            {
                float individualScale = overrideFraming.boolValue
                    ? Mathf.Max(0.05f, scale.floatValue)
                    : 1f;
                Vector2 individualOffset = overrideFraming.boolValue
                    ? offset.vector2Value
                    : Vector2.zero;
                bool individualFlip = overrideFraming.boolValue && flipX.boolValue;

                DrawPortraitTexture(
                    previewRect,
                    trueFrameSize,
                    hasSpeakerPortrait ? sprite : null,
                    fallbackTexture,
                    portraitDefinition,
                    reference,
                    actual,
                    hasSpeakerPortrait ? individualScale : 1f,
                    hasSpeakerPortrait ? individualOffset : Vector2.zero,
                    hasSpeakerPortrait && individualFlip);
            }

            if (!hasSpeakerPortrait)
            {
                GUIStyle missingStyle = new GUIStyle(EditorStyles.boldLabel)
                {
                    alignment = TextAnchor.MiddleCenter
                };
                missingStyle.normal.textColor = new Color(1f, 1f, 1f, 0.65f);
                GUI.Label(previewRect, "ПОРТРЕТ НЕ НАЗНАЧЕН", missingStyle);
            }

            Color border = new Color(0.8f, 0.62f, 0.28f, 0.55f);
            EditorGUI.DrawRect(new Rect(previewRect.x, previewRect.y, previewRect.width, 1f), border);
            EditorGUI.DrawRect(new Rect(previewRect.x, previewRect.yMax - 1f, previewRect.width, 1f), border);
            EditorGUI.DrawRect(new Rect(previewRect.x, previewRect.y, 1f, previewRect.height), border);
            EditorGUI.DrawRect(new Rect(previewRect.xMax - 1f, previewRect.y, 1f, previewRect.height), border);

            if (hasSpeakerPortrait && overrideFraming.boolValue)
            {
                EditorGUIUtility.AddCursorRect(previewRect, MouseCursor.Pan);
                HandleSpeakerPortraitDrag(previewRect, offset);
                EditorGUILayout.LabelField(
                    "Перетаскивай портрет мышью прямо в preview; масштаб регулируется выше.",
                    EditorStyles.wordWrappedMiniLabel);
            }
            else if (hasSpeakerPortrait)
            {
                EditorGUILayout.LabelField(
                    "Сейчас показана только общая кадрировка из UI Конструктора.",
                    EditorStyles.wordWrappedMiniLabel);
            }
        }

        private void HandleSpeakerPortraitDrag(Rect previewRect, SerializedProperty offset)
        {
            Event e = Event.current;
            if (e == null || offset == null)
                return;

            if (e.type == EventType.MouseDown && e.button == 0 && previewRect.Contains(e.mousePosition))
            {
                Undo.RecordObject(database, "Pan Dialogue Speaker Portrait");
                speakerPortraitDragging = true;
                speakerPortraitDragStartMouse = e.mousePosition;
                speakerPortraitDragStartOffset = offset.vector2Value;
                e.Use();
                return;
            }

            if (e.type == EventType.MouseDrag && e.button == 0 && speakerPortraitDragging)
            {
                Vector2 delta = e.mousePosition - speakerPortraitDragStartMouse;
                offset.vector2Value = speakerPortraitDragStartOffset + new Vector2(
                    delta.x / Mathf.Max(1f, previewRect.width),
                    delta.y / Mathf.Max(1f, previewRect.height));
                Repaint();
                e.Use();
                return;
            }

            if (e.type == EventType.MouseUp && speakerPortraitDragging)
            {
                speakerPortraitDragging = false;
                e.Use();
            }
        }

        // Реальное разрешение текущего Game View — то же самое разрешение,
        // которое видит игрок, и по которому runtime считает актуальную
        // рамку портрета (UILayoutRuntimeApplier.ResolveImageFrameSize).
        // GetMainGameViewSize — стандартный (хоть и приватный) приём Unity
        // Editor для этой задачи. Если отражение недоступно (другая версия
        // редактора, batch mode), используем referenceResolution — тогда
        // preview просто совпадает со случаем "игра запущена ровно в
        // референсном разрешении", что почти всегда и есть основной
        // проверяемый случай.
        private static Vector2 ResolveEditorPreviewActualResolution(Vector2 referenceResolution)
        {
            try
            {
                System.Type gameViewType = System.Type.GetType("UnityEditor.GameView,UnityEditor");
                if (gameViewType != null)
                {
                    System.Reflection.MethodInfo method = gameViewType.GetMethod(
                        "GetSizeOfMainGameView",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                    if (method != null)
                    {
                        Vector2 size = (Vector2)method.Invoke(null, null);
                        if (size.x > 0f && size.y > 0f)
                            return size;
                    }
                }
            }
            catch
            {
                // Отражение во внутренний API Unity намеренно не должно
                // ронять окно — просто используем запасное разрешение ниже.
            }

            return referenceResolution;
        }

        // Чисто визуальное вписывание реальной (true) рамки в разумный
        // размер preview-панели редактора — БЕЗ искажения соотношения
        // сторон (в отличие от старого Mathf.Clamp(180, 430) по высоте,
        // который и приводил к расхождению с runtime). Величина здесь
        // никогда не участвует в математике кадрирования — только в том,
        // во сколько раз итоговый imageRect домножается перед отрисовкой.
        public static Vector2 ResolvePreviewDisplaySize(Vector2 trueFrameSize, float maxWidth, float maxHeight)
        {
            if (trueFrameSize.x <= 0f || trueFrameSize.y <= 0f)
                return new Vector2(maxWidth, maxHeight);

            float scale = Mathf.Min(maxWidth / trueFrameSize.x, maxHeight / trueFrameSize.y);
            return trueFrameSize * scale;
        }

        // Инструкция "свободное кадрирование полного портрета" +
        // "редактор и runtime совпадают пиксель-в-пиксель": вся математика
        // кадрирования (offset/scale/imageRect) считается в РЕАЛЬНОМ
        // масштабе рамки (trueFrameSize, тот же, что использует runtime) —
        // как и раньше, без ScaleAndCrop. Единственное, что здесь чисто
        // визуальное, — displayScale, которым результат домножается перед
        // отрисовкой, чтобы уместиться в preview-панель (previewRect); он
        // выводится из уже готового previewRect.width/trueFrameSize.x, а не
        // передаётся отдельным параметром, поэтому не может разойтись с
        // ResolvePreviewDisplaySize, которым построен сам previewRect.
        private static void DrawPortraitTexture(
            Rect previewRect,
            Vector2 trueFrameSize,
            Sprite sprite,
            Texture fallbackTexture,
            UILayoutElementDefinition definition,
            Vector2 reference,
            Vector2 actual,
            float additionalScale,
            Vector2 normalizedOffset,
            bool flipX)
        {
            if (definition == null)
                return;

            Texture texture = sprite != null ? sprite.texture : fallbackTexture;
            if (texture == null)
                return;

            // §7: пропорции считаются по sprite.rect, а не по всей Texture —
            // иначе портрет из Sprite Atlas измерялся бы по размеру всего
            // атласа, а не своей области.
            Vector2 sourceSize = sprite != null
                ? UILayoutRuntimeApplier.ResolveSpriteSize(sprite)
                : new Vector2(texture.width, texture.height);

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

            Rect trueImageRect = UILayoutRuntimeApplier.ResolveImageRect(
                sourceSize,
                trueFrameSize,
                definition.ImageMode,
                magnitude,
                offset);

            float displayScale = trueFrameSize.x > 0f ? previewRect.width / trueFrameSize.x : 1f;
            Rect imageRect = new Rect(
                trueImageRect.x * displayScale,
                trueImageRect.y * displayScale,
                trueImageRect.width * displayScale,
                trueImageRect.height * displayScale);

            GUI.BeginGroup(previewRect);

            Matrix4x4 previousMatrix = GUI.matrix;
            Color previousColor = GUI.color;
            Color tint = definition.Tint;
            tint.a *= definition.Opacity;
            GUI.color = tint;

            if (flip)
                GUIUtility.ScaleAroundPivot(new Vector2(-1f, 1f), imageRect.center);

            if (sprite != null)
            {
                // Область конкретного Sprite внутри (возможно, атласной)
                // Texture, в UV-координатах — рисуем именно её, растянутую
                // на уже правильно вычисленный imageRect, а не всю Texture.
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

            GUI.matrix = previousMatrix;
            GUI.color = previousColor;
            GUI.EndGroup();
        }
    }
}

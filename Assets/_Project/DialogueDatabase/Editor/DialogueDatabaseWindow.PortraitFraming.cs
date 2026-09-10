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

            float aspect = portraitDefinition.Rect.height > 0f
                ? portraitDefinition.Rect.width / portraitDefinition.Rect.height
                : 0.7f;
            float previewWidth = 300f;
            float previewHeight = Mathf.Clamp(previewWidth / Mathf.Max(0.1f, aspect), 180f, 430f);
            Rect previewRect = GUILayoutUtility.GetRect(
                previewWidth,
                previewHeight,
                GUILayout.Width(previewWidth),
                GUILayout.Height(previewHeight));

            EditorGUI.DrawRect(previewRect, new Color(0.07f, 0.08f, 0.09f, 1f));

            bool hasSpeakerPortrait = sprite != null;
            Texture fallbackTexture = portraitDefinition.Sprite != null
                ? portraitDefinition.Sprite.texture
                : portraitDefinition.Texture;
            Texture texture = hasSpeakerPortrait ? sprite.texture : fallbackTexture;

            if (texture != null)
            {
                float previewScreenScale = portraitDefinition.Rect.width > 0f
                    ? previewRect.width / portraitDefinition.Rect.width
                    : 1f;
                Vector2 reference = layoutDatabase.ReferenceResolution;
                Vector2 actual = reference * previewScreenScale;
                float individualScale = overrideFraming.boolValue
                    ? Mathf.Max(0.05f, scale.floatValue)
                    : 1f;
                Vector2 individualOffset = overrideFraming.boolValue
                    ? offset.vector2Value
                    : Vector2.zero;
                bool individualFlip = overrideFraming.boolValue && flipX.boolValue;

                DrawPortraitTexture(
                    previewRect,
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

        // Инструкция "свободное кадрирование полного портрета": preview
        // больше не обрезает Sprite заранее через ScaleAndCrop. Вместо
        // этого считается прямоугольник ПОЛНОГО изображения (тем же
        // UILayoutRuntimeApplier.ResolveImageRect, что и runtime/UI
        // Конструктор), а рамка лишь визуально отсекает то, что оказалось
        // за её пределами (GUI.BeginGroup + clip самой группой) — то, что
        // раньше вырезал ScaleAndCrop, теперь просто не видно за краем
        // frame, но физически остаётся в imageRect и доступно перетаскиванию.
        private static void DrawPortraitTexture(
            Rect frame,
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

            Rect imageRect = UILayoutRuntimeApplier.ResolveImageRect(
                sourceSize,
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

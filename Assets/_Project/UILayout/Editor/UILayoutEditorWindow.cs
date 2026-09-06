using System.Collections.Generic;
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
        private int screenIndex;
        private int elementIndex;
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
                    if (GUILayout.Toggle(elementIndex == i, screen.Elements[i].DisplayName, "Button"))
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
            GUI.Box(canvas, GUIContent.none);

            UILayoutScreenDefinition screen = CurrentScreen;
            if (screen != null)
            {
                float sx = canvas.width / Mathf.Max(1f, reference.x);
                float sy = canvas.height / Mathf.Max(1f, reference.y);
                for (int i = 0; i < screen.Elements.Count; i++)
                {
                    UILayoutElementDefinition element = screen.Elements[i];
                    Rect r = element.Rect;
                    Rect draw = new Rect(
                        canvas.x + r.x * sx,
                        canvas.y + r.y * sy,
                        r.width * sx,
                        r.height * sy);
                    DrawElementImage(draw, element, sx, sy);
                    EditorGUI.DrawRect(
                        draw,
                        i == elementIndex
                            ? new Color(0.75f, 0.54f, 0.18f, 0.20f)
                            : new Color(0.4f, 0.4f, 0.4f, 0.08f));
                    GUI.Box(draw, element.DisplayName);
                    if (i == elementIndex)
                        HandleSelectedElement(draw, sx, sy, element);
                }
            }

            EditorGUILayout.EndVertical();
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
                EditorGUILayout.PropertyField(selected.FindPropertyRelative("rect"));
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

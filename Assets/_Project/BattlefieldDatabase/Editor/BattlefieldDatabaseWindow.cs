using System;
using System.Collections.Generic;
using KingdomSurvival.BattlefieldDatabase;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace KingdomSurvival.BattlefieldDatabase.Editor
{
    public sealed class BattlefieldDatabaseWindow : EditorWindow
    {
        private const string AssetPath = "Assets/_Project/BattlefieldDatabase/Resources/BattlefieldDatabase/KingdomSurvivalBattlefields.asset";
        [SerializeField] private int selectedIndex = -1;

        private BattlefieldDatabaseAsset database;
        private SerializedObject serializedDatabase;
        private SerializedProperty fieldsProperty;
        private SerializedProperty tagsProperty;
        private SerializedProperty sandboxIdProperty;
        private readonly List<int> visibleIndices = new List<int>();
        private TextField search;
        private ListView list;
        private ScrollView details;
        private Label emptyHint;
        private Label validation;
        private Image previewImage;
        private VisualElement previewViewport;

        [MenuItem("Kingdom Survival/База полей боя")]
        public static void OpenWindow()
        {
            BattlefieldDatabaseWindow window = GetWindow<BattlefieldDatabaseWindow>();
            window.titleContent = new GUIContent("База полей боя");
            window.minSize = new Vector2(960f, 620f);
        }

        public void CreateGUI()
        {
            database = AssetDatabase.LoadAssetAtPath<BattlefieldDatabaseAsset>(AssetPath);
            rootVisualElement.Clear();
            if (database == null)
            {
                rootVisualElement.Add(new HelpBox("Не найдена база полей: " + AssetPath, HelpBoxMessageType.Error));
                return;
            }

            serializedDatabase = new SerializedObject(database);
            fieldsProperty = serializedDatabase.FindProperty("battlefields");
            tagsProperty = serializedDatabase.FindProperty("tags");
            sandboxIdProperty = serializedDatabase.FindProperty("sandboxBattlefieldId");
            BuildToolbar();

            TwoPaneSplitView split = new TwoPaneSplitView(0, 300f, TwoPaneSplitViewOrientation.Horizontal);
            split.style.flexGrow = 1f;
            split.Add(BuildListPane());
            split.Add(BuildDetailPane());
            rootVisualElement.Add(split);
            RefreshList();
            RestoreSelection();
        }

        private void BuildToolbar()
        {
            VisualElement toolbar = new VisualElement();
            toolbar.style.height = 42f;
            toolbar.style.flexDirection = FlexDirection.Row;
            toolbar.style.alignItems = Align.Center;
            toolbar.style.paddingLeft = 8f;
            toolbar.style.paddingRight = 8f;
            AddToolbarButton(toolbar, "+ ДОБАВИТЬ", AddField);
            AddToolbarButton(toolbar, "ДУБЛИРОВАТЬ", DuplicateField);
            AddToolbarButton(toolbar, "УДАЛИТЬ", DeleteField);
            AddToolbarButton(toolbar, "+ ТЕГ", AddTag);
            AddToolbarButton(toolbar, "ПРОВЕРИТЬ БАЗУ", ValidateDatabase);
            validation = new Label();
            validation.style.flexGrow = 1f;
            validation.style.unityTextAlign = TextAnchor.MiddleRight;
            toolbar.Add(validation);
            rootVisualElement.Add(toolbar);
        }

        private static void AddToolbarButton(VisualElement parent, string text, Action action)
        {
            Button button = new Button(action) { text = text };
            button.style.height = 26f;
            button.style.marginRight = 6f;
            parent.Add(button);
        }

        private VisualElement BuildListPane()
        {
            VisualElement pane = new VisualElement();
            pane.style.paddingLeft = 8f;
            pane.style.paddingRight = 8f;
            pane.style.paddingTop = 8f;
            pane.style.paddingBottom = 8f;

            search = new TextField("Поиск");
            search.RegisterValueChangedCallback(_ => RefreshList());
            pane.Add(search);

            list = new ListView();
            list.style.flexGrow = 1f;
            list.style.marginTop = 8f;
            list.fixedItemHeight = 64f;
            list.selectionType = SelectionType.Single;
            list.makeItem = MakeListItem;
            list.bindItem = BindListItem;
            list.selectionChanged += _ => SelectVisible(list.selectedIndex);
            pane.Add(list);

            Label hint = new Label("Фоновое изображение не содержит гексов: сетка, подсветки и юниты накладываются отдельно.");
            hint.style.whiteSpace = WhiteSpace.Normal;
            hint.style.fontSize = 10f;
            hint.style.color = new Color(0.62f, 0.62f, 0.62f, 1f);
            pane.Add(hint);
            return pane;
        }

        private VisualElement BuildDetailPane()
        {
            VisualElement pane = new VisualElement();
            pane.style.flexGrow = 1f;
            emptyHint = new Label("Выберите поле боя слева.");
            emptyHint.style.flexGrow = 1f;
            emptyHint.style.unityTextAlign = TextAnchor.MiddleCenter;
            pane.Add(emptyHint);

            details = new ScrollView();
            details.style.display = DisplayStyle.None;
            details.style.flexGrow = 1f;
            details.style.paddingLeft = 16f;
            details.style.paddingRight = 16f;
            details.style.paddingTop = 12f;
            details.style.paddingBottom = 16f;
            details.RegisterCallback<SerializedPropertyChangeEvent>(_ =>
            {
                serializedDatabase.ApplyModifiedProperties();
                EditorUtility.SetDirty(database);
                list.RefreshItems();
                RefreshPreview();
            });
            pane.Add(details);
            return pane;
        }

        private static VisualElement MakeListItem()
        {
            VisualElement row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            Image image = new Image { name = "image", scaleMode = ScaleMode.ScaleAndCrop, pickingMode = PickingMode.Ignore };
            image.style.width = 82f;
            image.style.height = 48f;
            image.style.marginRight = 8f;
            row.Add(image);
            VisualElement labels = new VisualElement();
            labels.style.flexGrow = 1f;
            Label title = new Label { name = "title" };
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            Label id = new Label { name = "id" };
            id.style.fontSize = 10f;
            id.style.color = new Color(0.62f, 0.62f, 0.62f, 1f);
            labels.Add(title);
            labels.Add(id);
            row.Add(labels);
            return row;
        }

        private void BindListItem(VisualElement row, int visibleIndex)
        {
            if (visibleIndex < 0 || visibleIndex >= visibleIndices.Count)
                return;
            BattlefieldDefinitionData field = database.Battlefields[visibleIndices[visibleIndex]];
            bool active = field.Id == database.SandboxBattlefieldId;
            row.Q<Label>("title").text = (active ? "● " : string.Empty) + field.DisplayLabel;
            row.Q<Label>("id").text = field.Id;
            Image image = row.Q<Image>("image");
            image.sprite = field.Background;
            ApplyFraming(image, field.BackgroundScale, field.BackgroundOffset, 82f, 48f);
        }

        private void RefreshList()
        {
            if (database == null || list == null)
                return;
            serializedDatabase.Update();
            visibleIndices.Clear();
            string query = search != null ? search.value.Trim() : string.Empty;
            for (int i = 0; i < database.Battlefields.Count; i++)
            {
                BattlefieldDefinitionData field = database.Battlefields[i];
                if (field == null)
                    continue;
                if (!string.IsNullOrEmpty(query) &&
                    (field.Id == null || field.Id.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0) &&
                    (field.DisplayLabel == null || field.DisplayLabel.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0))
                    continue;
                visibleIndices.Add(i);
            }
            list.itemsSource = visibleIndices;
            list.Rebuild();
            int visible = visibleIndices.IndexOf(selectedIndex);
            if (visible >= 0)
                list.SetSelectionWithoutNotify(new[] { visible });
        }

        private void RestoreSelection()
        {
            if (selectedIndex < 0 || selectedIndex >= database.Battlefields.Count)
                selectedIndex = database.Battlefields.Count > 0 ? 0 : -1;
            ShowSelected();
        }

        private void SelectVisible(int visibleIndex)
        {
            if (visibleIndex < 0 || visibleIndex >= visibleIndices.Count)
                return;
            selectedIndex = visibleIndices[visibleIndex];
            ShowSelected();
        }

        private void ShowSelected()
        {
            if (selectedIndex < 0 || selectedIndex >= fieldsProperty.arraySize)
            {
                emptyHint.style.display = DisplayStyle.Flex;
                details.style.display = DisplayStyle.None;
                return;
            }
            emptyHint.style.display = DisplayStyle.None;
            details.style.display = DisplayStyle.Flex;
            details.Clear();
            SerializedProperty field = fieldsProperty.GetArrayElementAtIndex(selectedIndex);

            AddHeader("ПОЛЕ БОЯ");
            details.Add(new PropertyField(field.FindPropertyRelative("id"), "ID"));
            details.Add(new PropertyField(field.FindPropertyRelative("displayLabel"), "Название"));
            Button active = new Button(MakeSelectedSandboxDefault) { text = "ИСПОЛЬЗОВАТЬ В BATTLESANDBOX" };
            active.style.marginTop = 6f;
            active.style.marginBottom = 8f;
            details.Add(active);

            AddHeader("ФОН");
            details.Add(new PropertyField(field.FindPropertyRelative("background"), "Изображение поля"));
            details.Add(new PropertyField(field.FindPropertyRelative("backgroundScale"), "Масштаб"));
            details.Add(new PropertyField(field.FindPropertyRelative("backgroundOffset"), "Смещение X / Y"));
            Button reset = new Button(ResetFraming) { text = "СБРОСИТЬ КАДРИРОВАНИЕ" };
            details.Add(reset);
            Label offsetHint = new Label("Смещение задаётся долей размера поля: 0,1 по X = 10% ширины.");
            offsetHint.style.fontSize = 10f;
            offsetHint.style.color = new Color(0.62f, 0.62f, 0.62f, 1f);
            details.Add(offsetHint);

            AddHeader("ТЕГИ");
            details.Add(new PropertyField(field.FindPropertyRelative("tagIds"), "ID тегов"));
            Foldout tagBook = new Foldout { text = "Справочник тегов" };
            tagBook.Add(new PropertyField(tagsProperty, "Теги базы"));
            details.Add(tagBook);

            AddHeader("ПРЕДПРОСМОТР 56 ГЕКСОВ · 7 / 8 / 9 / 8 / 9 / 8 / 7");
            previewViewport = new VisualElement();
            previewViewport.style.height = 360f;
            previewViewport.style.position = Position.Relative;
            previewViewport.style.overflow = Overflow.Hidden;
            previewViewport.style.backgroundColor = new Color(0.05f, 0.06f, 0.07f, 1f);
            previewImage = new Image { scaleMode = ScaleMode.ScaleAndCrop, pickingMode = PickingMode.Ignore };
            SetAbsoluteFill(previewImage);
            previewViewport.Add(previewImage);
            GridPreview grid = new GridPreview();
            SetAbsoluteFill(grid);
            previewViewport.Add(grid);
            details.Add(previewViewport);
            details.Bind(serializedDatabase);
            RefreshPreview();
        }

        private void AddHeader(string text)
        {
            Label label = new Label(text);
            label.style.marginTop = 12f;
            label.style.marginBottom = 5f;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.color = new Color(0.80f, 0.66f, 0.34f, 1f);
            details.Add(label);
        }

        private static void SetAbsoluteFill(VisualElement element)
        {
            element.style.position = Position.Absolute;
            element.style.left = 0f;
            element.style.right = 0f;
            element.style.top = 0f;
            element.style.bottom = 0f;
        }

        private void RefreshPreview()
        {
            if (previewImage == null || selectedIndex < 0 || selectedIndex >= database.Battlefields.Count)
                return;
            BattlefieldDefinitionData field = database.Battlefields[selectedIndex];
            previewImage.sprite = field.Background;
            float width = previewViewport != null ? previewViewport.resolvedStyle.width : 600f;
            float height = previewViewport != null ? previewViewport.resolvedStyle.height : 360f;
            ApplyFraming(previewImage, field.BackgroundScale, field.BackgroundOffset, width, height);
        }

        private static void ApplyFraming(Image image, float scale, Vector2 offset, float width, float height)
        {
            image.style.scale = new Scale(Vector3.one * Mathf.Max(0.1f, scale));
            image.transform.position = new Vector3(offset.x * width, offset.y * height, 0f);
        }

        private void MakeSelectedSandboxDefault()
        {
            if (selectedIndex < 0 || selectedIndex >= database.Battlefields.Count)
                return;
            serializedDatabase.Update();
            sandboxIdProperty.stringValue = database.Battlefields[selectedIndex].Id;
            serializedDatabase.ApplyModifiedProperties();
            EditorUtility.SetDirty(database);
            RefreshList();
        }

        private void ResetFraming()
        {
            if (selectedIndex < 0 || selectedIndex >= fieldsProperty.arraySize)
                return;
            serializedDatabase.Update();
            SerializedProperty field = fieldsProperty.GetArrayElementAtIndex(selectedIndex);
            field.FindPropertyRelative("backgroundScale").floatValue = 1f;
            field.FindPropertyRelative("backgroundOffset").vector2Value = Vector2.zero;
            serializedDatabase.ApplyModifiedProperties();
            EditorUtility.SetDirty(database);
            ShowSelected();
            list.RefreshItems();
        }

        private void AddField()
        {
            serializedDatabase.Update();
            int index = fieldsProperty.arraySize++;
            SerializedProperty field = fieldsProperty.GetArrayElementAtIndex(index);
            field.FindPropertyRelative("id").stringValue = MakeUniqueFieldId("new_battlefield");
            field.FindPropertyRelative("displayLabel").stringValue = "Новое поле";
            field.FindPropertyRelative("background").objectReferenceValue = null;
            field.FindPropertyRelative("backgroundScale").floatValue = 1f;
            field.FindPropertyRelative("backgroundOffset").vector2Value = Vector2.zero;
            field.FindPropertyRelative("tagIds").ClearArray();
            serializedDatabase.ApplyModifiedProperties();
            EditorUtility.SetDirty(database);
            selectedIndex = index;
            RefreshList();
            ShowSelected();
        }

        private void DuplicateField()
        {
            if (selectedIndex < 0 || selectedIndex >= fieldsProperty.arraySize)
                return;
            serializedDatabase.Update();
            fieldsProperty.InsertArrayElementAtIndex(selectedIndex);
            selectedIndex++;
            SerializedProperty field = fieldsProperty.GetArrayElementAtIndex(selectedIndex);
            string sourceId = field.FindPropertyRelative("id").stringValue;
            field.FindPropertyRelative("id").stringValue = MakeUniqueFieldId(sourceId + "_copy");
            field.FindPropertyRelative("displayLabel").stringValue += " — копия";
            serializedDatabase.ApplyModifiedProperties();
            EditorUtility.SetDirty(database);
            RefreshList();
            ShowSelected();
        }

        private void DeleteField()
        {
            if (selectedIndex < 0 || selectedIndex >= fieldsProperty.arraySize)
                return;
            string id = database.Battlefields[selectedIndex].Id;
            if (!EditorUtility.DisplayDialog("Удалить поле боя", "Удалить «" + id + "»?", "Удалить", "Отмена"))
                return;
            serializedDatabase.Update();
            fieldsProperty.DeleteArrayElementAtIndex(selectedIndex);
            if (sandboxIdProperty.stringValue == id)
                sandboxIdProperty.stringValue = fieldsProperty.arraySize > 0
                    ? fieldsProperty.GetArrayElementAtIndex(0).FindPropertyRelative("id").stringValue
                    : string.Empty;
            serializedDatabase.ApplyModifiedProperties();
            EditorUtility.SetDirty(database);
            selectedIndex = Mathf.Clamp(selectedIndex - 1, -1, database.Battlefields.Count - 1);
            RefreshList();
            ShowSelected();
        }

        private void AddTag()
        {
            serializedDatabase.Update();
            int index = tagsProperty.arraySize++;
            SerializedProperty tag = tagsProperty.GetArrayElementAtIndex(index);
            tag.FindPropertyRelative("id").stringValue = MakeUniqueTagId("new_tag");
            tag.FindPropertyRelative("displayLabel").stringValue = "Новый тег";
            tag.FindPropertyRelative("category").stringValue = "Прочее";
            tag.FindPropertyRelative("color").colorValue = Color.gray;
            tag.FindPropertyRelative("description").stringValue = string.Empty;
            serializedDatabase.ApplyModifiedProperties();
            EditorUtility.SetDirty(database);
            ShowSelected();
        }

        private void ValidateDatabase()
        {
            serializedDatabase.ApplyModifiedProperties();
            List<string> issues = new List<string>();
            database.CollectValidationIssues(issues);
            validation.text = issues.Count == 0 ? "Ошибок не найдено" : "Ошибок: " + issues.Count;
            validation.style.color = issues.Count == 0
                ? new Color(0.42f, 0.72f, 0.45f, 1f)
                : new Color(0.90f, 0.48f, 0.38f, 1f);
            if (issues.Count > 0)
                Debug.LogWarning("Battlefield database:\n- " + string.Join("\n- ", issues));
        }

        private string MakeUniqueFieldId(string baseId)
        {
            string candidate = baseId;
            int suffix = 2;
            while (database.FindById(candidate) != null)
                candidate = baseId + "_" + suffix++;
            return candidate;
        }

        private string MakeUniqueTagId(string baseId)
        {
            string candidate = baseId;
            int suffix = 2;
            while (database.FindTag(candidate) != null)
                candidate = baseId + "_" + suffix++;
            return candidate;
        }

        private sealed class GridPreview : VisualElement
        {
            private static readonly int[] RowStarts = { 1, 0, 0, 0, 0, 0, 1 };
            private static readonly int[] RowLengths = { 7, 8, 9, 8, 9, 8, 7 };

            public GridPreview()
            {
                pickingMode = PickingMode.Ignore;
                generateVisualContent += Draw;
            }

            private void Draw(MeshGenerationContext context)
            {
                const int width = 9;
                const int height = 7;
                if (contentRect.width <= 1f || contentRect.height <= 1f)
                    return;
                float aw = contentRect.width - 28f;
                float ah = contentRect.height - 28f;
                float wu = Mathf.Sqrt(3f) * width;
                float hu = 1.5f * (height - 1) + 2f;
                float size = Mathf.Min(aw / wu, ah / hu);
                float bw = wu * size;
                float bh = hu * size;
                Vector2 origin = new Vector2(
                    (contentRect.width - bw) * 0.5f + Mathf.Sqrt(3f) * size * 0.5f,
                    (contentRect.height - bh) * 0.5f + size);
                Painter2D painter = context.painter2D;
                painter.strokeColor = new Color(0.90f, 0.92f, 0.88f, 0.78f);
                painter.lineWidth = 1.3f;
                for (int r = 0; r < height; r++)
                {
                    float rowOffset = (r & 1) == 0 ? 0f : 0.5f;
                    int start = RowStarts[r];
                    int end = start + RowLengths[r];
                    for (int q = start; q < end; q++)
                    {
                        Vector2 center = origin + new Vector2(
                            size * Mathf.Sqrt(3f) * (q + rowOffset),
                            size * 1.5f * r);
                        DrawHex(painter, center, size - 1.5f);
                    }
                }
            }

            private static void DrawHex(Painter2D painter, Vector2 center, float radius)
            {
                painter.BeginPath();
                for (int i = 0; i < 6; i++)
                {
                    float angle = Mathf.Deg2Rad * (60f * i - 30f);
                    Vector2 point = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                    if (i == 0) painter.MoveTo(point); else painter.LineTo(point);
                }
                painter.ClosePath();
                painter.Stroke();
            }
        }
    }
}

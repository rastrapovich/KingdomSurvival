using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace KingdomSurvival.ArtAssets.Editor
{
    // ПР-12Н: единый выбор ассета для других баз: поиск по названию, ID и
    // тегам, категория, крупные миниатюры. Двойной клик или «Выбрать».
    public sealed class ArtAssetPicker : EditorWindow
    {
        public const string DragKey = "KingdomSurvival.ArtAssetId";

        private Action<string> onPick;
        private string query = "", selectedId;
        private int category = -1;
        private Vector2 scroll;
        private float cardSize = 110;
        private ArtAssetView view = ArtAssetView.Front;

        public static void Show(string title, Action<string> pick, string currentId = null)
        {
            ArtAssetPicker window = CreateInstance<ArtAssetPicker>();
            window.titleContent = new GUIContent(title);
            window.onPick = pick;
            window.selectedId = currentId;
            window.minSize = new Vector2(520, 420);
            window.ShowUtility();
        }

        // Начать перетаскивание ассета (в Базу локаций или внутри холста).
        public static void StartDrag(ArtAssetDefinition asset)
        {
            DragAndDrop.PrepareStartDrag();
            DragAndDrop.SetGenericData(DragKey, asset.Id);
            DragAndDrop.objectReferences = Array.Empty<UnityEngine.Object>();
            DragAndDrop.paths = Array.Empty<string>();
            DragAndDrop.StartDrag(asset.Name);
        }

        // Свой ассет тащится, только если вместе с ним не тащат файлы: метка
        // прошлого перетаскивания не должна перехватывать PNG из Проводника.
        public static string DraggedAssetId()
        {
            if ((DragAndDrop.paths != null && DragAndDrop.paths.Length > 0) ||
                (DragAndDrop.objectReferences != null && DragAndDrop.objectReferences.Length > 0))
                return null;
            return DragAndDrop.GetGenericData(DragKey) as string;
        }

        public static void EndDrag() => DragAndDrop.SetGenericData(DragKey, null);

        private void OnGUI()
        {
            ArtAssetDatabaseAsset catalog = ArtAssetDatabaseAsset.Current;
            if (catalog == null)
            {
                EditorGUILayout.HelpBox("Каталог Базы ассетов ещё не создан. Откройте «Kingdom Survival/База ассетов».", MessageType.Info);
                return;
            }
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                query = EditorGUILayout.TextField(query, EditorStyles.toolbarSearchField, GUILayout.MinWidth(160));
                List<string> names = new List<string> { "Все категории" };
                names.AddRange(ArtAssetLabels.Categories.Select(ArtAssetLabels.CategoryTitle));
                category = EditorGUILayout.Popup(category + 1, names.ToArray(), EditorStyles.toolbarPopup, GUILayout.Width(170)) - 1;
                view = (ArtAssetView)EditorGUILayout.Popup((int)view, ArtAssetLabels.Views.Select(ArtAssetLabels.ViewTitle).ToArray(), EditorStyles.toolbarPopup, GUILayout.Width(130));
                cardSize = GUILayout.HorizontalSlider(cardSize, 70, 200, GUILayout.Width(80));
            }
            List<ArtAssetDefinition> items = catalog.assets.Where(item => item != null && item.MatchesQuery(query) &&
                (category < 0 || item.Category == ArtAssetLabels.Categories[category])).OrderBy(item => item.Name).ToList();
            Rect area = GUILayoutUtility.GetRect(10, 10, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            int columns = Mathf.Max(1, Mathf.FloorToInt((area.width - 16) / (cardSize + 8)));
            int rows = Mathf.CeilToInt(items.Count / (float)columns);
            float rowHeight = cardSize + 34;
            Rect content = new Rect(0, 0, area.width - 16, rows * rowHeight + 8);
            scroll = GUI.BeginScrollView(area, scroll, content);
            int firstRow = Mathf.Max(0, Mathf.FloorToInt(scroll.y / rowHeight) - 1);
            int lastRow = Mathf.Min(rows - 1, Mathf.CeilToInt((scroll.y + area.height) / rowHeight) + 1);
            for (int row = firstRow; row <= lastRow; row++)
                for (int column = 0; column < columns; column++)
                {
                    int index = row * columns + column;
                    if (index >= items.Count) break;
                    ArtAssetDefinition asset = items[index];
                    Rect card = new Rect(4 + column * (cardSize + 8), 4 + row * rowHeight, cardSize, rowHeight - 6);
                    if (asset.Id == selectedId) EditorGUI.DrawRect(card, new Color(.95f, .75f, .3f, .35f));
                    Rect image = new Rect(card.x + 4, card.y + 4, card.width - 8, cardSize - 8);
                    ArtAssetDrawing.Checker(image, 10);
                    ArtAssetDrawing.DrawFitted(catalog, asset, view, image);
                    GUI.Label(new Rect(card.x, card.y + cardSize - 2, card.width, 18), asset.Name, EditorStyles.miniBoldLabel);
                    GUI.Label(new Rect(card.x, card.y + cardSize + 12, card.width, 16), ArtAssetDrawing.Completeness(asset), EditorStyles.miniLabel);
                    Event evt = Event.current;
                    if (evt.type == EventType.MouseDown && card.Contains(evt.mousePosition))
                    {
                        selectedId = asset.Id;
                        if (evt.clickCount >= 2) { Pick(); GUIUtility.ExitGUI(); }
                        evt.Use();
                    }
                }
            GUI.EndScrollView();
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(items.Count + " из " + catalog.assets.Count, EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Открыть Базу ассетов", GUILayout.Width(160))) ArtAssetDatabaseWindow.Open(selectedId);
                GUI.enabled = selectedId != null && catalog.Find(selectedId) != null;
                if (GUILayout.Button("Выбрать", GUILayout.Width(110))) Pick();
                GUI.enabled = true;
                if (GUILayout.Button("Отмена", GUILayout.Width(90))) Close();
            }
        }

        private void Pick()
        {
            string id = selectedId;
            Action<string> callback = onPick;
            Close();
            if (id != null) callback?.Invoke(id);
        }
    }
}

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace KingdomSurvival.ItemDatabase.Editor
{
    // 12Т: выбор ID из базы с поиском (вещи, диалоги, говорящие, торговцы) —
    // автору не нужно печатать известные ID вручную. Значение пишется через
    // SerializedProperty: Undo/Redo и отметка изменений работают как обычно.
    public sealed class IdPickerDropdown : AdvancedDropdown
    {
        private readonly string title;
        private readonly List<(string Id, string Label)> options;
        private readonly Action<string> onPicked;

        private IdPickerDropdown(string title, List<(string Id, string Label)> options, Action<string> onPicked)
            : base(new AdvancedDropdownState())
        {
            this.title = title;
            this.options = options;
            this.onPicked = onPicked;
            minimumSize = new Vector2(360f, 320f);
        }

        protected override AdvancedDropdownItem BuildRoot()
        {
            AdvancedDropdownItem root = new AdvancedDropdownItem(title);
            root.AddChild(new IdItem("— пусто —", string.Empty));
            foreach ((string id, string label) in options)
                root.AddChild(new IdItem(string.IsNullOrEmpty(label) ? id : label + "   · " + id, id));
            return root;
        }

        protected override void ItemSelected(AdvancedDropdownItem item)
        {
            if (item is IdItem picked)
                onPicked?.Invoke(picked.Value);
        }

        private sealed class IdItem : AdvancedDropdownItem
        {
            public readonly string Value;

            public IdItem(string name, string value) : base(name)
            {
                Value = value;
            }
        }

        // Строка «подпись: [значение ▾]»; красная подпись — ID не найден.
        public static void Field(string label, SerializedProperty property, string pickerTitle,
            Func<List<(string Id, string Label)>> optionsProvider, Func<string, bool> exists = null, Action<string> jump = null)
        {
            Rect rect = EditorGUILayout.GetControlRect();
            Rect labelRect = new Rect(rect.x, rect.y, EditorGUIUtility.labelWidth, rect.height);
            float jumpWidth = jump != null ? 64f : 0f;
            Rect buttonRect = new Rect(labelRect.xMax, rect.y, rect.width - EditorGUIUtility.labelWidth - jumpWidth, rect.height);
            string value = property.stringValue ?? string.Empty;
            bool broken = !string.IsNullOrEmpty(value) && exists != null && !exists(value);
            Color previous = GUI.color;
            if (broken)
                GUI.color = new Color(1f, 0.55f, 0.5f);
            EditorGUI.LabelField(labelRect, new GUIContent(label, broken ? "Нет в базе: " + value : null));
            string shown = string.IsNullOrEmpty(value) ? "— выбрать —" : value + (broken ? "  (нет в базе!)" : string.Empty);
            if (EditorGUI.DropdownButton(buttonRect, new GUIContent(shown), FocusType.Keyboard))
            {
                SerializedObject owner = property.serializedObject;
                string path = property.propertyPath;
                new IdPickerDropdown(pickerTitle, optionsProvider(), id =>
                {
                    owner.Update();
                    SerializedProperty target = owner.FindProperty(path);
                    if (target == null)
                        return;
                    target.stringValue = id ?? string.Empty;
                    owner.ApplyModifiedProperties();
                }).Show(buttonRect);
            }
            GUI.color = previous;
            if (jump != null)
            {
                Rect jumpRect = new Rect(buttonRect.xMax + 2f, rect.y, jumpWidth - 2f, rect.height);
                using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(value) || broken))
                {
                    if (GUI.Button(jumpRect, "Открыть"))
                        jump(value);
                }
            }
        }

        public static List<(string Id, string Label)> ItemOptions()
        {
            List<(string, string)> result = new List<(string, string)>();
            ItemDatabaseAsset database = AssetDatabase.LoadAssetAtPath<ItemDatabaseAsset>(ItemDatabaseAsset.AssetPath);
            if (database?.items == null)
                return result;
            foreach (ItemDatabaseEntry entry in database.items)
            {
                if (entry?.Data != null && !string.IsNullOrEmpty(entry.Data.Id))
                    result.Add((entry.Data.Id, entry.Data.Name));
            }
            result.Sort((a, b) => string.Compare(a.Item2, b.Item2, StringComparison.CurrentCulture));
            return result;
        }

        public static bool ItemExists(string itemId)
        {
            ItemDatabaseAsset database = AssetDatabase.LoadAssetAtPath<ItemDatabaseAsset>(ItemDatabaseAsset.AssetPath);
            return database != null && database.Find(itemId) != null;
        }
    }
}

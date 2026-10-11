using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace KingdomSurvival.LocationRendering.Editor
{
    // Боковая панель Базы локаций — блоками. Разделы вкладок по-прежнему
    // строятся подряд с заголовками (Heading); после построения всё, что
    // под заголовком, собирается в сворачиваемый блок с рамкой. Что открыто —
    // помнится отдельно для каждой вкладки и раздела. Название места и
    // вкладки остаются сверху.
    public sealed partial class LocationDatabaseWindow
    {
        private const string HeadingClass = "ks-settings-heading";
        private const string BlockPrefsKey = "KS.LocationDatabase.Block.";
        // Открыты по умолчанию — первые разделы вкладки, остальные свёрнуты.
        private const int OpenBlocksByDefault = 2;

        private void GroupSettingsIntoBlocks()
        {
            if (settings == null) return;
            List<VisualElement> items = settings.Children().ToList();
            int first = items.FindIndex(item => item.ClassListContains(HeadingClass));
            if (first < 0) return;
            // Название места и строка вкладок — над блоками.
            int start = Mathf.Min(items.Count, first + 2);
            for (int i = start; i < items.Count; i++) items[i].RemoveFromHierarchy();
            Foldout block = null;
            int count = 0;
            for (int i = start; i < items.Count; i++)
            {
                VisualElement item = items[i];
                if (item.ClassListContains(HeadingClass) && item is Label heading)
                {
                    block = Block(heading.text, count++);
                    continue;
                }
                block ??= Block("Основное", count++);
                block.Add(item);
            }
            // Пустой раздел (только заголовок) не нужен.
            foreach (Foldout empty in settings.Children().OfType<Foldout>().Where(item => item.contentContainer.childCount == 0).ToList())
                empty.RemoveFromHierarchy();
        }

        private Foldout Block(string title, int index)
        {
            string key = BlockPrefsKey + tab + "." + title;
            Foldout block = new Foldout { text = title, value = EditorPrefs.GetBool(key, index < OpenBlocksByDefault) };
            block.RegisterValueChangedCallback(evt =>
            {
                if (evt.target == block) EditorPrefs.SetBool(key, evt.newValue);
            });
            block.style.marginTop = 6;
            block.style.paddingLeft = block.style.paddingRight = 6;
            block.style.paddingTop = 2;
            block.style.paddingBottom = 4;
            block.style.backgroundColor = new Color(1, 1, 1, .035f);
            block.style.borderTopWidth = block.style.borderBottomWidth = block.style.borderLeftWidth = block.style.borderRightWidth = 1;
            block.style.borderTopColor = block.style.borderBottomColor = block.style.borderLeftColor = block.style.borderRightColor = new Color(1, 1, 1, .09f);
            block.style.borderTopLeftRadius = block.style.borderTopRightRadius = block.style.borderBottomLeftRadius = block.style.borderBottomRightRadius = 4;
            UnityEngine.UIElements.Toggle toggle = block.Q<UnityEngine.UIElements.Toggle>();
            if (toggle != null)
            {
                toggle.style.marginTop = 2;
                Label label = toggle.Q<Label>();
                if (label != null)
                {
                    label.style.unityFontStyleAndWeight = FontStyle.Bold;
                    label.style.fontSize = 13;
                    label.style.color = new Color(.88f, .78f, .53f);
                }
            }
            block.contentContainer.style.marginLeft = 4;
            settings.Add(block);
            return block;
        }
    }
}

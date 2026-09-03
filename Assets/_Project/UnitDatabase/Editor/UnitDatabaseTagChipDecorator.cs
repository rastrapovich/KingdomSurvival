using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace KingdomSurvival.UnitDatabase.Editor
{
    [InitializeOnLoad]
    internal static class UnitDatabaseTagChipDecorator
    {
        private const string AssetPath =
            "Assets/_Project/UnitDatabase/Resources/UnitDatabase/KingdomSurvivalUnits.asset";
        private const string WrapperName = "unit-database-tag-chip-row";
        private const double RefreshIntervalSeconds = 0.35d;

        private static double nextRefreshTime;

        static UnitDatabaseTagChipDecorator()
        {
            EditorApplication.update += RefreshOpenWindows;
        }

        private static void RefreshOpenWindows()
        {
            if (EditorApplication.timeSinceStartup < nextRefreshTime)
                return;

            nextRefreshTime = EditorApplication.timeSinceStartup + RefreshIntervalSeconds;

            UnitDatabaseAsset database = AssetDatabase.LoadAssetAtPath<UnitDatabaseAsset>(AssetPath);
            if (database == null || database.Tags == null || database.Tags.Count == 0)
                return;

            Dictionary<string, UnitTagDefinition> tagsById = BuildTagLookup(database);
            UnitDatabaseWindow[] windows = Resources.FindObjectsOfTypeAll<UnitDatabaseWindow>();
            for (int i = 0; i < windows.Length; i++)
            {
                UnitDatabaseWindow window = windows[i];
                if (window == null || window.rootVisualElement == null)
                    continue;

                DecorateWindow(window.rootVisualElement, tagsById);
            }
        }

        private static Dictionary<string, UnitTagDefinition> BuildTagLookup(UnitDatabaseAsset database)
        {
            Dictionary<string, UnitTagDefinition> result =
                new Dictionary<string, UnitTagDefinition>(StringComparer.Ordinal);
            for (int i = 0; i < database.Tags.Count; i++)
            {
                UnitTagDefinition tag = database.Tags[i];
                if (tag == null || string.IsNullOrWhiteSpace(tag.Id))
                    continue;

                result[tag.Id] = tag;
            }

            return result;
        }

        private static void DecorateWindow(
            VisualElement root,
            IReadOnlyDictionary<string, UnitTagDefinition> tagsById)
        {
            List<Toggle> toggles = root.Query<Toggle>().ToList();
            for (int i = 0; i < toggles.Count; i++)
            {
                Toggle toggle = toggles[i];
                if (toggle == null || toggle.parent == null ||
                    toggle.parent.name == WrapperName)
                {
                    continue;
                }

                string tagId = ExtractTagId(toggle.tooltip);
                if (string.IsNullOrEmpty(tagId) ||
                    !tagsById.TryGetValue(tagId, out UnitTagDefinition tag))
                {
                    continue;
                }

                WrapTagToggle(toggle, tag);
            }
        }

        private static string ExtractTagId(string tooltip)
        {
            if (string.IsNullOrWhiteSpace(tooltip))
                return null;

            int lineBreak = tooltip.IndexOf('\n');
            return (lineBreak >= 0 ? tooltip.Substring(0, lineBreak) : tooltip).Trim();
        }

        private static void WrapTagToggle(Toggle toggle, UnitTagDefinition tag)
        {
            VisualElement parent = toggle.parent;
            int index = parent.IndexOf(toggle);
            string originalTooltip = toggle.tooltip;
            string displayLabel = string.IsNullOrWhiteSpace(tag.DisplayLabel)
                ? tag.Id
                : tag.DisplayLabel;

            VisualElement wrapper = new VisualElement
            {
                name = WrapperName,
                tooltip = originalTooltip
            };
            wrapper.style.flexDirection = FlexDirection.Row;
            wrapper.style.alignItems = Align.Center;
            wrapper.style.marginRight = 10f;
            wrapper.style.marginBottom = 6f;

            toggle.RemoveFromHierarchy();
            toggle.label = string.Empty;
            toggle.tooltip = originalTooltip;
            toggle.style.width = 18f;
            toggle.style.minWidth = 18f;
            toggle.style.height = 24f;
            toggle.style.marginLeft = 0f;
            toggle.style.marginRight = 6f;
            toggle.style.marginTop = 0f;
            toggle.style.marginBottom = 0f;
            if (toggle.labelElement != null)
                toggle.labelElement.style.display = DisplayStyle.None;
            wrapper.Add(toggle);

            Label chip = new Label(displayLabel.ToUpperInvariant())
            {
                tooltip = originalTooltip,
                pickingMode = PickingMode.Position
            };
            chip.style.fontSize = 10f;
            chip.style.unityFontStyleAndWeight = FontStyle.Bold;
            chip.style.color = new Color(0.92f, 0.91f, 0.86f, 1f);
            chip.style.paddingLeft = 7f;
            chip.style.paddingRight = 7f;
            chip.style.paddingTop = 4f;
            chip.style.paddingBottom = 4f;
            chip.style.backgroundColor = WithAlpha(tag.Color, 0.28f);
            SetBorder(chip, tag.Color, 1f);
            SetRadius(chip, 8f);

            chip.RegisterCallback<PointerEnterEvent>(_ =>
                chip.style.backgroundColor = WithAlpha(tag.Color, 0.45f));
            chip.RegisterCallback<PointerLeaveEvent>(_ =>
                chip.style.backgroundColor = WithAlpha(tag.Color, 0.28f));
            chip.RegisterCallback<ClickEvent>(_ => toggle.value = !toggle.value);
            wrapper.Add(chip);

            parent.Insert(Mathf.Clamp(index, 0, parent.hierarchy.childCount), wrapper);
        }

        private static Color WithAlpha(Color color, float alpha)
        {
            return new Color(color.r, color.g, color.b, alpha);
        }

        private static void SetBorder(VisualElement element, Color color, float width)
        {
            element.style.borderLeftWidth = width;
            element.style.borderRightWidth = width;
            element.style.borderTopWidth = width;
            element.style.borderBottomWidth = width;
            element.style.borderLeftColor = color;
            element.style.borderRightColor = color;
            element.style.borderTopColor = color;
            element.style.borderBottomColor = color;
        }

        private static void SetRadius(VisualElement element, float radius)
        {
            element.style.borderTopLeftRadius = radius;
            element.style.borderTopRightRadius = radius;
            element.style.borderBottomLeftRadius = radius;
            element.style.borderBottomRightRadius = radius;
        }
    }
}

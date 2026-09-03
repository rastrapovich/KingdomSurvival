using System;
using KingdomSurvival.UnitDatabase;
using UnityEngine;
using UnityEngine.UIElements;

namespace KingdomSurvival.BattleSandbox
{
    internal static class SandboxFighterDetailsRefinerBootstrap
    {
        private static GameObject runnerObject;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Initialize()
        {
            if (runnerObject != null)
                return;

            runnerObject = new GameObject("Sandbox Fighter Details Refiner");
            runnerObject.hideFlags = HideFlags.HideInHierarchy;
            UnityEngine.Object.DontDestroyOnLoad(runnerObject);
            runnerObject.AddComponent<SandboxFighterDetailsRefiner>();
        }
    }

    internal sealed class SandboxFighterDetailsRefiner : MonoBehaviour
    {
        private const string WindowName = "sandbox-fighter-details-window";
        private const string TooltipName = "sandbox-stat-tooltip";
        private const string TagRowName = "sandbox-fighter-tag-row";

        private UnitDatabaseAsset database;
        private VisualElement window;
        private VisualElement tagRow;
        private Label titleLabel;
        private string renderedTypeLabel;

        private void Awake()
        {
            database = Resources.Load<UnitDatabaseAsset>(UnitDatabaseAsset.ResourcesPath);
        }

        private void Update()
        {
            if (window == null || window.panel == null)
            {
                FindAndPrepareWindow();
                return;
            }

            RefreshTags();
        }

        private void FindAndPrepareWindow()
        {
            UIDocument[] documents = UnityEngine.Object.FindObjectsByType<UIDocument>(
                FindObjectsSortMode.None);
            for (int i = 0; i < documents.Length; i++)
            {
                VisualElement root = documents[i] != null
                    ? documents[i].rootVisualElement
                    : null;
                if (root == null)
                    continue;

                VisualElement candidate = root.Q<VisualElement>(WindowName);
                if (candidate == null)
                    continue;

                window = candidate;
                ApplyStaticLayout(root);
                RefreshTags(true);
                return;
            }
        }

        private void ApplyStaticLayout(VisualElement root)
        {
            if (window == null || window.hierarchy.childCount < 2)
                return;

            VisualElement header = window.hierarchy.ElementAt(0);
            VisualElement body = window.hierarchy.ElementAt(1);
            if (header != null && header.hierarchy.childCount > 0)
            {
                VisualElement identity = header.hierarchy.ElementAt(0);
                if (identity != null && identity.hierarchy.childCount > 0)
                    titleLabel = identity.hierarchy.ElementAt(0) as Label;
            }

            if (body == null || body.hierarchy.childCount < 2)
                return;

            VisualElement portraitPanel = body.hierarchy.ElementAt(0);
            VisualElement info = body.hierarchy.ElementAt(1);

            if (portraitPanel != null)
            {
                // Portrait child layout: viewport, fallback label, duplicate role,
                // team label, HP bar. Keep only the image/fallback and HP bar.
                if (portraitPanel.hierarchy.childCount > 2)
                    portraitPanel.hierarchy.ElementAt(2).style.display = DisplayStyle.None;
                if (portraitPanel.hierarchy.childCount > 3)
                    portraitPanel.hierarchy.ElementAt(3).style.display = DisplayStyle.None;
                if (portraitPanel.hierarchy.childCount > 0)
                    portraitPanel.hierarchy.ElementAt(0).style.bottom = 36f;
            }

            if (info != null)
            {
                // Original layout: heading, HP, ATK, DEF, DMG, MOVE, INIT,
                // RANGE, ACTIONS, state, hover hint.
                HideChild(info, 0);
                HideChild(info, 7);
                HideChild(info, 8);
                HideChild(info, 9);
                HideChild(info, 10);

                for (int index = 1; index <= 6; index++)
                    MakeStatRowReadable(info, index);

                tagRow = info.Q<VisualElement>(TagRowName);
                if (tagRow == null)
                {
                    tagRow = new VisualElement
                    {
                        name = TagRowName
                    };
                    tagRow.style.flexDirection = FlexDirection.Row;
                    tagRow.style.flexWrap = Wrap.Wrap;
                    tagRow.style.alignItems = Align.FlexStart;
                    tagRow.style.marginTop = 12f;
                    info.Add(tagRow);
                }
            }

            VisualElement tooltip = root.Q<VisualElement>(TooltipName);
            if (tooltip != null)
            {
                if (tooltip.hierarchy.childCount > 0 &&
                    tooltip.hierarchy.ElementAt(0) is Label tooltipTitle)
                {
                    tooltipTitle.style.fontSize = 16f;
                }

                if (tooltip.hierarchy.childCount > 1 &&
                    tooltip.hierarchy.ElementAt(1) is Label tooltipText)
                {
                    tooltipText.style.fontSize = 15f;
                }
            }
        }

        private static void HideChild(VisualElement parent, int index)
        {
            if (parent != null && index >= 0 && index < parent.hierarchy.childCount)
                parent.hierarchy.ElementAt(index).style.display = DisplayStyle.None;
        }

        private static void MakeStatRowReadable(VisualElement info, int index)
        {
            if (info == null || index < 0 || index >= info.hierarchy.childCount)
                return;

            VisualElement row = info.hierarchy.ElementAt(index);
            row.style.height = 42f;
            row.style.marginBottom = 5f;

            if (row.hierarchy.childCount > 0 && row.hierarchy.ElementAt(0) is Label title)
                title.style.fontSize = 16f;
            if (row.hierarchy.childCount > 1 && row.hierarchy.ElementAt(1) is Label value)
                value.style.fontSize = 18f;
        }

        private void RefreshTags(bool force = false)
        {
            if (database == null || tagRow == null || titleLabel == null)
                return;

            string currentLabel = titleLabel.text ?? string.Empty;
            if (!force && string.Equals(renderedTypeLabel, currentLabel, StringComparison.Ordinal))
                return;

            renderedTypeLabel = currentLabel;
            tagRow.Clear();

            UnitDefinitionData unit = FindUnitByDisplayLabel(currentLabel);
            if (unit == null || unit.TagIds == null || unit.TagIds.Count == 0)
            {
                tagRow.style.display = DisplayStyle.None;
                return;
            }

            for (int i = 0; i < unit.TagIds.Count; i++)
            {
                UnitTagDefinition tag = database.FindTag(unit.TagIds[i]);
                if (tag == null)
                    continue;

                string labelText = string.IsNullOrWhiteSpace(tag.DisplayLabel)
                    ? tag.Id
                    : tag.DisplayLabel;
                Label chip = new Label(labelText.ToUpperInvariant());
                chip.style.fontSize = 13f;
                chip.style.unityFontStyleAndWeight = FontStyle.Bold;
                chip.style.color = new Color(0.92f, 0.91f, 0.86f, 1f);
                chip.style.paddingLeft = 9f;
                chip.style.paddingRight = 9f;
                chip.style.paddingTop = 5f;
                chip.style.paddingBottom = 5f;
                chip.style.marginRight = 6f;
                chip.style.marginBottom = 6f;
                chip.style.backgroundColor = new Color(
                    tag.Color.r,
                    tag.Color.g,
                    tag.Color.b,
                    0.28f);
                SetBorder(chip, tag.Color, 1f);
                SetRadius(chip, 10f);
                chip.tooltip = string.IsNullOrWhiteSpace(tag.Description)
                    ? labelText
                    : tag.Description;
                tagRow.Add(chip);
            }

            tagRow.style.display = tagRow.hierarchy.childCount > 0
                ? DisplayStyle.Flex
                : DisplayStyle.None;
        }

        private UnitDefinitionData FindUnitByDisplayLabel(string displayedTitle)
        {
            if (database == null || database.Units == null)
                return null;

            string normalized = (displayedTitle ?? string.Empty).Trim();
            for (int i = 0; i < database.Units.Count; i++)
            {
                UnitDefinitionData unit = database.Units[i];
                if (unit == null)
                    continue;

                if (string.Equals(
                        unit.DisplayLabel,
                        normalized,
                        StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(
                        unit.DisplayLabel != null ? unit.DisplayLabel.ToUpperInvariant() : string.Empty,
                        normalized,
                        StringComparison.Ordinal))
                {
                    return unit;
                }
            }

            return null;
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

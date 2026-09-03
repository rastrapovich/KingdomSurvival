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
        private const string TagTooltipName = "sandbox-fighter-tag-tooltip";
        private const float TagTooltipWidth = 330f;

        private UnitDatabaseAsset database;
        private VisualElement root;
        private VisualElement window;
        private VisualElement tagRow;
        private VisualElement tagTooltip;
        private Label tagTooltipTitle;
        private Label tagTooltipText;
        private Label titleLabel;
        private Image portraitImage;
        private VisualElement statTooltip;
        private Label statTooltipTitle;
        private Label statTooltipText;
        private string renderedUnitKey;

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
                VisualElement documentRoot = documents[i] != null
                    ? documents[i].rootVisualElement
                    : null;
                if (documentRoot == null)
                    continue;

                VisualElement candidate = documentRoot.Q<VisualElement>(WindowName);
                if (candidate == null)
                    continue;

                root = documentRoot;
                window = candidate;
                ApplyStaticLayout();
                RefreshTags(true);
                return;
            }
        }

        private void ApplyStaticLayout()
        {
            if (root == null || window == null || window.hierarchy.childCount < 2)
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
                if (portraitPanel.hierarchy.childCount > 2)
                    portraitPanel.hierarchy.ElementAt(2).style.display = DisplayStyle.None;
                if (portraitPanel.hierarchy.childCount > 3)
                    portraitPanel.hierarchy.ElementAt(3).style.display = DisplayStyle.None;
                if (portraitPanel.hierarchy.childCount > 0)
                {
                    VisualElement viewport = portraitPanel.hierarchy.ElementAt(0);
                    viewport.style.bottom = 36f;
                    if (viewport.hierarchy.childCount > 0)
                        portraitImage = viewport.hierarchy.ElementAt(0) as Image;
                }
            }

            statTooltip = root.Q<VisualElement>(TooltipName);
            if (statTooltip != null)
            {
                if (statTooltip.hierarchy.childCount > 0 &&
                    statTooltip.hierarchy.ElementAt(0) is Label tooltipTitle)
                {
                    statTooltipTitle = tooltipTitle;
                    statTooltipTitle.style.fontSize = 16f;
                }

                if (statTooltip.hierarchy.childCount > 1 &&
                    statTooltip.hierarchy.ElementAt(1) is Label tooltipText)
                {
                    statTooltipText = tooltipText;
                    statTooltipText.style.fontSize = 15f;
                }
            }

            if (info != null)
            {
                HideChild(info, 0);
                HideChild(info, 8);
                HideChild(info, 9);
                HideChild(info, 10);

                ConfigureStatRow(
                    info,
                    1,
                    "ЖИЗНИ",
                    "Текущий запас здоровья бойца. При 0 жизней боец выбывает из боя.");
                ConfigureStatRow(
                    info,
                    2,
                    "АТАКА",
                    "Сравнивается с Защитой цели и определяет множитель наносимого урона.");
                ConfigureStatRow(
                    info,
                    3,
                    "ЗАЩИТА",
                    "Сравнивается с Атакой противника и влияет на количество получаемого урона.");
                ConfigureStatRow(
                    info,
                    4,
                    "УРОН",
                    "Базовое количество урона до применения результата сравнения Атаки и Защиты.");
                ConfigureStatRow(
                    info,
                    5,
                    "ХОД",
                    "Запас движения бойца на активацию. Стоимость перемещения зависит от пройденных гексов и местности.");
                ConfigureStatRow(
                    info,
                    6,
                    "ИНИЦИАТИВА",
                    "Определяет порядок активации бойцов в начале каждого раунда.");
                ConfigureStatRow(
                    info,
                    7,
                    "ДАЛЬНОСТЬ АТАКИ",
                    "Максимальное расстояние в гексах, с которого боец может атаковать цель.");

                tagRow = info.Q<VisualElement>(TagRowName);
                if (tagRow == null)
                {
                    tagRow = new VisualElement { name = TagRowName };
                    tagRow.style.flexDirection = FlexDirection.Row;
                    tagRow.style.flexWrap = Wrap.Wrap;
                    tagRow.style.alignItems = Align.FlexStart;
                    tagRow.style.marginTop = 10f;
                    info.Add(tagRow);
                }
            }

            EnsureTagTooltip();
        }

        private void ConfigureStatRow(
            VisualElement info,
            int index,
            string titleText,
            string description)
        {
            if (info == null || index < 0 || index >= info.hierarchy.childCount)
                return;

            VisualElement row = info.hierarchy.ElementAt(index);
            row.style.display = DisplayStyle.Flex;
            MakeStatRowCompact(info, index);

            if (row.hierarchy.childCount > 0 && row.hierarchy.ElementAt(0) is Label title)
                title.text = titleText;

            row.RegisterCallback<PointerEnterEvent>(_ =>
            {
                row.schedule.Execute(() => OverrideStatTooltip(titleText, description)).ExecuteLater(1);
            });
        }

        private void OverrideStatTooltip(string title, string description)
        {
            if (statTooltip == null || statTooltipTitle == null || statTooltipText == null ||
                statTooltip.resolvedStyle.display == DisplayStyle.None)
            {
                return;
            }

            statTooltipTitle.text = title;
            statTooltipText.text = description;
        }

        private void EnsureTagTooltip()
        {
            if (root == null)
                return;

            tagTooltip = root.Q<VisualElement>(TagTooltipName);
            if (tagTooltip != null)
            {
                tagTooltipTitle = tagTooltip.hierarchy.childCount > 0
                    ? tagTooltip.hierarchy.ElementAt(0) as Label
                    : null;
                tagTooltipText = tagTooltip.hierarchy.childCount > 1
                    ? tagTooltip.hierarchy.ElementAt(1) as Label
                    : null;
                return;
            }

            tagTooltip = new VisualElement
            {
                name = TagTooltipName,
                pickingMode = PickingMode.Ignore
            };
            tagTooltip.style.display = DisplayStyle.None;
            tagTooltip.style.position = Position.Absolute;
            tagTooltip.style.width = TagTooltipWidth;
            tagTooltip.style.paddingLeft = 12f;
            tagTooltip.style.paddingRight = 12f;
            tagTooltip.style.paddingTop = 10f;
            tagTooltip.style.paddingBottom = 10f;
            tagTooltip.style.backgroundColor = new Color(0.055f, 0.065f, 0.075f, 0.995f);
            SetBorder(tagTooltip, new Color(0.58f, 0.47f, 0.26f, 1f), 1f);
            SetRadius(tagTooltip, 4f);

            tagTooltipTitle = new Label();
            tagTooltipTitle.style.fontSize = 14f;
            tagTooltipTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
            tagTooltipTitle.style.color = new Color(0.91f, 0.76f, 0.43f, 1f);
            tagTooltip.Add(tagTooltipTitle);

            tagTooltipText = new Label();
            tagTooltipText.style.fontSize = 13f;
            tagTooltipText.style.marginTop = 5f;
            tagTooltipText.style.whiteSpace = WhiteSpace.Normal;
            tagTooltipText.style.color = new Color(0.76f, 0.76f, 0.72f, 1f);
            tagTooltip.Add(tagTooltipText);

            root.Add(tagTooltip);
        }

        private static void HideChild(VisualElement parent, int index)
        {
            if (parent != null && index >= 0 && index < parent.hierarchy.childCount)
                parent.hierarchy.ElementAt(index).style.display = DisplayStyle.None;
        }

        private static void MakeStatRowCompact(VisualElement info, int index)
        {
            if (info == null || index < 0 || index >= info.hierarchy.childCount)
                return;

            VisualElement row = info.hierarchy.ElementAt(index);
            row.style.height = 34f;
            row.style.marginBottom = 4f;
            row.style.paddingLeft = 8f;
            row.style.paddingRight = 8f;

            if (row.hierarchy.childCount > 0 && row.hierarchy.ElementAt(0) is Label title)
                title.style.fontSize = 13f;
            if (row.hierarchy.childCount > 1 && row.hierarchy.ElementAt(1) is Label value)
                value.style.fontSize = 14f;
        }

        private void RefreshTags(bool force = false)
        {
            if (database == null || tagRow == null || titleLabel == null)
                return;

            UnitDefinitionData unit = FindCurrentUnit();
            string currentKey = unit != null ? unit.Id : (titleLabel.text ?? string.Empty);
            if (!force && string.Equals(renderedUnitKey, currentKey, StringComparison.Ordinal))
                return;

            renderedUnitKey = currentKey;
            HideTagTooltip();
            tagRow.Clear();

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
                string description = string.IsNullOrWhiteSpace(tag.Description)
                    ? "Описание для этого тега пока не задано."
                    : tag.Description;

                Label chip = new Label(labelText.ToUpperInvariant());
                chip.style.fontSize = 10f;
                chip.style.unityFontStyleAndWeight = FontStyle.Bold;
                chip.style.color = new Color(0.92f, 0.91f, 0.86f, 1f);
                chip.style.paddingLeft = 7f;
                chip.style.paddingRight = 7f;
                chip.style.paddingTop = 4f;
                chip.style.paddingBottom = 4f;
                chip.style.marginRight = 5f;
                chip.style.marginBottom = 5f;
                chip.style.backgroundColor = new Color(
                    tag.Color.r,
                    tag.Color.g,
                    tag.Color.b,
                    0.28f);
                SetBorder(chip, tag.Color, 1f);
                SetRadius(chip, 8f);

                UnitTagDefinition capturedTag = tag;
                string capturedLabel = labelText;
                string capturedDescription = description;
                chip.RegisterCallback<PointerEnterEvent>(_ =>
                {
                    chip.style.backgroundColor = new Color(
                        capturedTag.Color.r,
                        capturedTag.Color.g,
                        capturedTag.Color.b,
                        0.45f);
                    ShowTagTooltip(
                        chip,
                        capturedLabel,
                        capturedDescription,
                        capturedTag.Color);
                });
                chip.RegisterCallback<PointerLeaveEvent>(_ =>
                {
                    chip.style.backgroundColor = new Color(
                        capturedTag.Color.r,
                        capturedTag.Color.g,
                        capturedTag.Color.b,
                        0.28f);
                    HideTagTooltip();
                });
                tagRow.Add(chip);
            }

            tagRow.style.display = tagRow.hierarchy.childCount > 0
                ? DisplayStyle.Flex
                : DisplayStyle.None;
        }

        private UnitDefinitionData FindCurrentUnit()
        {
            if (database == null || database.Units == null || titleLabel == null)
                return null;

            string normalized = (titleLabel.text ?? string.Empty).Trim();
            UnitDefinitionData labelFallback = null;
            Sprite currentPortrait = portraitImage != null ? portraitImage.sprite : null;

            for (int i = 0; i < database.Units.Count; i++)
            {
                UnitDefinitionData unit = database.Units[i];
                if (unit == null)
                    continue;

                bool labelMatches =
                    string.Equals(unit.DisplayLabel, normalized, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(
                        unit.DisplayLabel != null ? unit.DisplayLabel.ToUpperInvariant() : string.Empty,
                        normalized,
                        StringComparison.Ordinal);
                if (!labelMatches)
                    continue;

                if (labelFallback == null)
                    labelFallback = unit;
                if (currentPortrait != null && unit.Portrait == currentPortrait)
                    return unit;
            }

            return labelFallback;
        }

        private void ShowTagTooltip(
            VisualElement anchor,
            string title,
            string description,
            Color accent)
        {
            if (root == null || tagTooltip == null || tagTooltipTitle == null || tagTooltipText == null)
                return;

            tagTooltipTitle.text = title.ToUpperInvariant();
            tagTooltipText.text = description;
            tagTooltipTitle.style.color = new Color(
                Mathf.Max(0.55f, accent.r),
                Mathf.Max(0.55f, accent.g),
                Mathf.Max(0.55f, accent.b),
                1f);
            SetBorder(tagTooltip, accent, 1f);

            tagTooltip.style.display = DisplayStyle.Flex;
            tagTooltip.BringToFront();

            Rect bounds = anchor.worldBound;
            Vector2 topRight = root.WorldToLocal(new Vector2(bounds.xMax, bounds.yMin));
            Vector2 topLeft = root.WorldToLocal(new Vector2(bounds.xMin, bounds.yMin));
            float rootWidth = root.resolvedStyle.width;
            float rootHeight = root.resolvedStyle.height;
            if (float.IsNaN(rootWidth) || rootWidth < 400f)
                rootWidth = 1280f;
            if (float.IsNaN(rootHeight) || rootHeight < 300f)
                rootHeight = 720f;

            float left = topRight.x + 10f;
            if (left + TagTooltipWidth > rootWidth - 12f)
                left = topLeft.x - TagTooltipWidth - 10f;

            tagTooltip.style.left = Mathf.Clamp(
                left,
                12f,
                Mathf.Max(12f, rootWidth - TagTooltipWidth - 12f));
            tagTooltip.style.top = Mathf.Clamp(
                topRight.y,
                12f,
                Mathf.Max(12f, rootHeight - 150f));
        }

        private void HideTagTooltip()
        {
            if (tagTooltip != null)
                tagTooltip.style.display = DisplayStyle.None;
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

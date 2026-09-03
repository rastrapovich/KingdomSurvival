using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace KingdomSurvival.BattleSandbox
{
    internal static class BattleSandboxCombatLogPresentationBootstrap
    {
        private const string BattleSandboxSceneName = "BattleSandbox";
        private static GameObject runnerObject;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Initialize()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            EnsureRunnerFor(SceneManager.GetActiveScene());
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            EnsureRunnerFor(scene);
        }

        private static void EnsureRunnerFor(Scene scene)
        {
            if (!string.Equals(scene.name, BattleSandboxSceneName, StringComparison.Ordinal))
            {
                if (runnerObject != null)
                    UnityEngine.Object.Destroy(runnerObject);
                runnerObject = null;
                return;
            }

            if (runnerObject != null)
                return;

            runnerObject = new GameObject("Battle Sandbox Combat Log Presentation");
            runnerObject.hideFlags = HideFlags.HideInHierarchy;
            runnerObject.AddComponent<BattleSandboxCombatLogPresentation>();
        }
    }

    [DefaultExecutionOrder(12000)]
    internal sealed class BattleSandboxCombatLogPresentation : MonoBehaviour
    {
        private const string BoardName = "battle-sandbox-board";
        private const string SurfaceName = "battlefield-surface";
        private const string SourceScrollName = "battle-sandbox-log-scroll";
        private const string DisplayName = "battle-sandbox-meaningful-log";
        private const int PageSize = 6;
        private const float PanelWidth = 584f;

        private static readonly Color DamageColor = new Color(0.78f, 0.38f, 0.36f, 1f);
        private static readonly Color TextColor = new Color(0.74f, 0.75f, 0.72f, 1f);
        private static readonly Color MutedTextColor = new Color(0.55f, 0.56f, 0.54f, 1f);
        private static readonly Color ButtonBackground = new Color(0.10f, 0.115f, 0.12f, 0.45f);
        private static readonly Color ButtonHoverBackground = new Color(0.16f, 0.17f, 0.17f, 0.58f);
        private static readonly Color ButtonBorder = new Color(0.42f, 0.40f, 0.34f, 0.34f);

        private readonly List<string> meaningfulEntries = new List<string>();
        private List<string> previousSourceLines = new List<string>();

        private VisualElement sidebar;
        private Label sourceLabel;
        private VisualElement entriesContainer;
        private Button newerButton;
        private Button olderButton;
        private Label pageLabel;
        private int pageIndex;

        private void Update()
        {
            if (sidebar == null || sidebar.panel == null || sourceLabel == null || sourceLabel.panel == null)
            {
                TryAttach();
                return;
            }

            CaptureNewEntries();
        }

        private void TryAttach()
        {
            UIDocument[] documents = UnityEngine.Object.FindObjectsByType<UIDocument>(FindObjectsSortMode.None);
            for (int i = 0; i < documents.Length; i++)
            {
                VisualElement root = documents[i] != null ? documents[i].rootVisualElement : null;
                if (root == null)
                    continue;

                VisualElement board = root.Q<VisualElement>(BoardName);
                if (board == null)
                    continue;

                VisualElement surface = board.parent != null && board.parent.name == SurfaceName
                    ? board.parent
                    : null;
                VisualElement body = surface != null ? surface.parent : board.parent;
                if (body == null)
                    continue;

                VisualElement candidateSidebar = FindSidebar(body, board, surface);
                ScrollView sourceScroll = candidateSidebar?.Q<ScrollView>(SourceScrollName);
                Label candidateSource = sourceScroll?.Q<Label>();
                if (candidateSidebar == null || sourceScroll == null || candidateSource == null)
                    continue;

                Attach(candidateSidebar, sourceScroll, candidateSource);
                return;
            }
        }

        private void Attach(VisualElement candidateSidebar, ScrollView sourceScroll, Label candidateSource)
        {
            sidebar = candidateSidebar;
            sourceLabel = candidateSource;
            meaningfulEntries.Clear();
            previousSourceLines.Clear();
            pageIndex = 0;

            StyleSidebar(sidebar);
            sourceScroll.style.display = DisplayStyle.None;

            Label logTitle = FindLogTitle(sidebar);
            if (logTitle != null)
            {
                logTitle.style.fontSize = 14f;
                logTitle.style.color = new Color(0.66f, 0.63f, 0.56f, 0.92f);
                logTitle.style.marginBottom = 4f;
            }

            VisualElement oldDisplay = sidebar.Q<VisualElement>(DisplayName);
            oldDisplay?.RemoveFromHierarchy();

            VisualElement display = new VisualElement { name = DisplayName };
            display.style.flexDirection = FlexDirection.Column;
            display.style.marginTop = 2f;
            sidebar.Add(display);

            entriesContainer = new VisualElement();
            entriesContainer.style.height = 148f;
            entriesContainer.style.flexShrink = 0f;
            entriesContainer.style.justifyContent = Justify.FlexStart;
            display.Add(entriesContainer);

            VisualElement navigation = new VisualElement();
            navigation.style.height = 28f;
            navigation.style.marginTop = 5f;
            navigation.style.flexDirection = FlexDirection.Row;
            navigation.style.alignItems = Align.Center;
            navigation.style.justifyContent = Justify.Center;
            display.Add(navigation);

            newerButton = CreateNavigationButton("‹", ShowNewerPage);
            olderButton = CreateNavigationButton("›", ShowOlderPage);
            pageLabel = new Label();
            pageLabel.style.width = 72f;
            pageLabel.style.fontSize = 12f;
            pageLabel.style.color = MutedTextColor;
            pageLabel.style.unityTextAlign = TextAnchor.MiddleCenter;

            navigation.Add(newerButton);
            navigation.Add(pageLabel);
            navigation.Add(olderButton);

            CaptureNewEntries();
            RefreshDisplay();
        }

        private static VisualElement FindSidebar(
            VisualElement body,
            VisualElement board,
            VisualElement surface)
        {
            for (int i = 0; i < body.childCount; i++)
            {
                VisualElement child = body[i];
                if (child != board && child != surface)
                    return child;
            }

            return null;
        }

        private static Label FindLogTitle(VisualElement container)
        {
            if (container == null)
                return null;

            return container.Query<Label>().ToList()
                .FirstOrDefault(label => string.Equals(
                    (label.text ?? string.Empty).Trim(),
                    "ХОД БОЯ",
                    StringComparison.OrdinalIgnoreCase));
        }

        private static void StyleSidebar(VisualElement panel)
        {
            panel.style.position = Position.Absolute;
            panel.style.left = Length.Percent(50f);
            panel.style.right = StyleKeyword.Auto;
            panel.style.bottom = 14f;
            panel.style.width = PanelWidth;
            panel.style.maxWidth = PanelWidth;
            panel.style.maxHeight = 226f;
            panel.style.marginLeft = -PanelWidth * 0.5f;
            panel.style.marginRight = 0f;
            panel.style.paddingLeft = 14f;
            panel.style.paddingRight = 14f;
            panel.style.paddingTop = 10f;
            panel.style.paddingBottom = 9f;
            panel.style.backgroundColor = new Color(0.045f, 0.052f, 0.055f, 0.624f);
            SetBorder(panel, new Color(0.52f, 0.47f, 0.35f, 0.34f), 1f);
        }

        private void CaptureNewEntries()
        {
            if (sourceLabel == null)
                return;

            List<string> current = SplitSourceLines(sourceLabel.text);
            if (SequenceEqual(previousSourceLines, current))
                return;

            int overlap = FindSuffixPrefixOverlap(previousSourceLines, current);
            for (int i = overlap; i < current.Count; i++)
            {
                string entry = NormalizeEntry(current[i]);
                if (!IsMeaningful(entry))
                    continue;

                meaningfulEntries.Add(entry);
                pageIndex = 0;
            }

            previousSourceLines = current;
            RefreshDisplay();
        }

        private static List<string> SplitSourceLines(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return new List<string>();

            return text
                .Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim())
                .Where(line => line.Length > 0)
                .ToList();
        }

        private static int FindSuffixPrefixOverlap(IReadOnlyList<string> previous, IReadOnlyList<string> current)
        {
            int max = Math.Min(previous.Count, current.Count);
            for (int length = max; length > 0; length--)
            {
                bool matches = true;
                for (int i = 0; i < length; i++)
                {
                    if (!string.Equals(
                            previous[previous.Count - length + i],
                            current[i],
                            StringComparison.Ordinal))
                    {
                        matches = false;
                        break;
                    }
                }

                if (matches)
                    return length;
            }

            return 0;
        }

        private static bool SequenceEqual(IReadOnlyList<string> left, IReadOnlyList<string> right)
        {
            if (left.Count != right.Count)
                return false;

            for (int i = 0; i < left.Count; i++)
            {
                if (!string.Equals(left[i], right[i], StringComparison.Ordinal))
                    return false;
            }

            return true;
        }

        private static string NormalizeEntry(string entry)
        {
            string normalized = (entry ?? string.Empty).Trim();
            if (normalized.StartsWith("•", StringComparison.Ordinal))
                normalized = normalized.Substring(1).TrimStart();
            return normalized;
        }

        private static bool IsMeaningful(string entry)
        {
            if (string.IsNullOrWhiteSpace(entry))
                return false;

            return entry.IndexOf("урона", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   entry.IndexOf("защитную стойку", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   entry.StartsWith("Бой начался", StringComparison.OrdinalIgnoreCase);
        }

        private void ShowNewerPage()
        {
            if (pageIndex <= 0)
                return;

            pageIndex--;
            RefreshDisplay();
        }

        private void ShowOlderPage()
        {
            int pageCount = GetPageCount();
            if (pageIndex >= pageCount - 1)
                return;

            pageIndex++;
            RefreshDisplay();
        }

        private int GetPageCount()
        {
            return Math.Max(1, (meaningfulEntries.Count + PageSize - 1) / PageSize);
        }

        private void RefreshDisplay()
        {
            if (entriesContainer == null || pageLabel == null || newerButton == null || olderButton == null)
                return;

            int pageCount = GetPageCount();
            pageIndex = Mathf.Clamp(pageIndex, 0, pageCount - 1);
            entriesContainer.Clear();

            int newestIndex = meaningfulEntries.Count - 1 - pageIndex * PageSize;
            int oldestIndex = Math.Max(0, newestIndex - PageSize + 1);
            for (int i = newestIndex; i >= oldestIndex && i >= 0; i--)
                entriesContainer.Add(CreateEntryLabel(meaningfulEntries[i]));

            pageLabel.text = (pageIndex + 1) + " / " + pageCount;
            newerButton.SetEnabled(pageIndex > 0);
            olderButton.SetEnabled(pageIndex < pageCount - 1);
        }

        private static Label CreateEntryLabel(string entry)
        {
            Label label = new Label("• " + HighlightDamage(entry));
            label.enableRichText = true;
            label.style.fontSize = 20f;
            label.style.color = TextColor;
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.marginBottom = 2f;
            label.style.unityTextAlign = TextAnchor.UpperLeft;
            return label;
        }

        private static string HighlightDamage(string entry)
        {
            if (string.IsNullOrEmpty(entry))
                return string.Empty;

            int damageWord = entry.IndexOf(" урона", StringComparison.OrdinalIgnoreCase);
            if (damageWord <= 0)
                return entry;

            int start = damageWord - 1;
            while (start >= 0 && char.IsDigit(entry[start]))
                start--;
            start++;
            if (start >= damageWord)
                return entry;

            string value = entry.Substring(start, damageWord - start);
            string hex = ColorUtility.ToHtmlStringRGB(DamageColor);
            return entry.Substring(0, start) +
                   "<color=#" + hex + ">" + value + "</color>" +
                   entry.Substring(damageWord);
        }

        private static Button CreateNavigationButton(string text, Action clicked)
        {
            Button button = new Button(clicked) { text = text };
            button.style.width = 34f;
            button.style.height = 24f;
            button.style.marginLeft = 4f;
            button.style.marginRight = 4f;
            button.style.paddingLeft = 0f;
            button.style.paddingRight = 0f;
            button.style.fontSize = 15f;
            button.style.color = MutedTextColor;
            button.style.backgroundColor = ButtonBackground;
            SetBorder(button, ButtonBorder, 1f);
            SetRadius(button, 4f);

            button.RegisterCallback<PointerEnterEvent>(_ =>
            {
                if (button.enabledSelf)
                    button.style.backgroundColor = ButtonHoverBackground;
            });
            button.RegisterCallback<PointerLeaveEvent>(_ => button.style.backgroundColor = ButtonBackground);
            return button;
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

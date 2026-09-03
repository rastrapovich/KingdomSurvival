using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace KingdomSurvival.BattleSandbox
{
    internal static class BattleSandboxCombatLogCompactRefinerBootstrap
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

            runnerObject = new GameObject("Battle Sandbox Compact Combat Log");
            runnerObject.hideFlags = HideFlags.HideInHierarchy;
            runnerObject.AddComponent<BattleSandboxCombatLogCompactRefiner>();
        }
    }

    [DefaultExecutionOrder(13000)]
    internal sealed class BattleSandboxCombatLogCompactRefiner : MonoBehaviour
    {
        private const string SourceScrollName = "battle-sandbox-log-scroll";
        private const string LegacyDisplayName = "battle-sandbox-meaningful-log";
        private const string CompactDisplayName = "battle-sandbox-compact-log";
        private const int VisibleEntryCount = 3;
        private const float PanelWidth = 584f;

        private static readonly Color DamageColor = new Color(0.78f, 0.38f, 0.36f, 1f);
        private static readonly Color TextColor = new Color(0.74f, 0.75f, 0.72f, 1f);
        private static readonly Color MutedTextColor = new Color(0.55f, 0.56f, 0.54f, 1f);
        private static readonly Color ButtonBackground = new Color(0.10f, 0.115f, 0.12f, 0.40f);
        private static readonly Color ButtonHoverBackground = new Color(0.16f, 0.17f, 0.17f, 0.54f);
        private static readonly Color ButtonBorder = new Color(0.42f, 0.40f, 0.34f, 0.30f);

        private readonly List<string> meaningfulEntries = new List<string>();
        private List<string> previousSourceLines = new List<string>();

        private VisualElement sidebar;
        private Label sourceLabel;
        private VisualElement entriesContainer;
        private Button newerButton;
        private Button olderButton;
        private int scrollOffset;

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
                ScrollView sourceScroll = root?.Q<ScrollView>(SourceScrollName);
                Label candidateSource = sourceScroll?.Q<Label>();
                VisualElement candidateSidebar = sourceScroll?.parent;
                if (candidateSidebar == null || candidateSource == null)
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
            scrollOffset = 0;

            StyleSidebar(sidebar);
            sourceScroll.style.display = DisplayStyle.None;

            VisualElement legacyDisplay = sidebar.Q<VisualElement>(LegacyDisplayName);
            if (legacyDisplay != null)
                legacyDisplay.style.display = DisplayStyle.None;

            Label logTitle = sidebar.Query<Label>().ToList().FirstOrDefault(label =>
                string.Equals((label.text ?? string.Empty).Trim(), "ХОД БОЯ", StringComparison.OrdinalIgnoreCase));
            if (logTitle != null)
            {
                logTitle.style.fontSize = 12f;
                logTitle.style.marginTop = 0f;
                logTitle.style.marginBottom = 1f;
            }

            VisualElement oldCompact = sidebar.Q<VisualElement>(CompactDisplayName);
            oldCompact?.RemoveFromHierarchy();

            VisualElement display = new VisualElement { name = CompactDisplayName };
            display.style.flexDirection = FlexDirection.Row;
            display.style.alignItems = Align.Stretch;
            display.style.marginTop = 0f;
            display.RegisterCallback<WheelEvent>(OnCombatLogWheel, TrickleDown.TrickleDown);
            sidebar.Add(display);

            entriesContainer = new VisualElement();
            entriesContainer.style.height = 66f;
            entriesContainer.style.flexGrow = 1f;
            entriesContainer.style.flexShrink = 1f;
            entriesContainer.style.minWidth = 0f;
            entriesContainer.style.overflow = Overflow.Hidden;
            entriesContainer.style.justifyContent = Justify.FlexStart;
            display.Add(entriesContainer);

            VisualElement navigation = new VisualElement();
            navigation.style.width = 28f;
            navigation.style.height = 66f;
            navigation.style.marginLeft = 4f;
            navigation.style.flexShrink = 0f;
            navigation.style.flexDirection = FlexDirection.Column;
            navigation.style.alignItems = Align.Center;
            navigation.style.justifyContent = Justify.Center;
            display.Add(navigation);

            newerButton = CreateNavigationButton("▲", ShowNewerEntry);
            olderButton = CreateNavigationButton("▼", ShowOlderEntry);
            navigation.Add(newerButton);
            navigation.Add(olderButton);

            CaptureNewEntries();
            RefreshDisplay();
        }

        private static void StyleSidebar(VisualElement panel)
        {
            panel.style.position = Position.Absolute;
            panel.style.left = Length.Percent(50f);
            panel.style.right = StyleKeyword.Auto;
            panel.style.bottom = 14f;
            panel.style.width = PanelWidth;
            panel.style.maxWidth = PanelWidth;
            panel.style.height = 113f;
            panel.style.maxHeight = 113f;
            panel.style.marginLeft = -PanelWidth * 0.5f;
            panel.style.marginRight = 0f;
            panel.style.paddingLeft = 12f;
            panel.style.paddingRight = 8f;
            panel.style.paddingTop = 5f;
            panel.style.paddingBottom = 4f;
            panel.style.backgroundColor = new Color(0.045f, 0.052f, 0.055f, 0.624f);
            SetBorder(panel, new Color(0.52f, 0.47f, 0.35f, 0.34f), 1f);
        }

        private void CaptureNewEntries()
        {
            List<string> current = SplitSourceLines(sourceLabel != null ? sourceLabel.text : string.Empty);
            if (SequenceEqual(previousSourceLines, current))
                return;

            int overlap = FindSuffixPrefixOverlap(previousSourceLines, current);
            for (int i = overlap; i < current.Count; i++)
            {
                string entry = NormalizeEntry(current[i]);
                if (!IsMeaningful(entry))
                    continue;

                meaningfulEntries.Add(entry);
                scrollOffset = 0;
            }

            previousSourceLines = current;
            RefreshDisplay();
        }

        private static List<string> SplitSourceLines(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return new List<string>();

            return text.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries)
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
                    if (!string.Equals(previous[previous.Count - length + i], current[i], StringComparison.Ordinal))
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

        private void OnCombatLogWheel(WheelEvent evt)
        {
            if (evt == null || Mathf.Approximately(evt.delta.y, 0f))
                return;

            int previousOffset = scrollOffset;
            if (evt.delta.y > 0f)
                ShowOlderEntry();
            else
                ShowNewerEntry();

            if (scrollOffset != previousOffset)
                evt.StopPropagation();
        }

        private void ShowNewerEntry()
        {
            if (scrollOffset <= 0)
                return;

            scrollOffset--;
            RefreshDisplay();
        }

        private void ShowOlderEntry()
        {
            int maxOffset = Math.Max(0, meaningfulEntries.Count - VisibleEntryCount);
            if (scrollOffset >= maxOffset)
                return;

            scrollOffset++;
            RefreshDisplay();
        }

        private void RefreshDisplay()
        {
            if (entriesContainer == null || newerButton == null || olderButton == null)
                return;

            int maxOffset = Math.Max(0, meaningfulEntries.Count - VisibleEntryCount);
            scrollOffset = Mathf.Clamp(scrollOffset, 0, maxOffset);
            entriesContainer.Clear();

            int newestIndex = meaningfulEntries.Count - 1 - scrollOffset;
            int oldestIndex = Math.Max(0, newestIndex - VisibleEntryCount + 1);
            for (int i = newestIndex; i >= oldestIndex && i >= 0; i--)
                entriesContainer.Add(CreateEntryLabel(meaningfulEntries[i]));

            StyleNavigationAvailability(newerButton, scrollOffset > 0);
            StyleNavigationAvailability(olderButton, scrollOffset < maxOffset);
        }

        private static Label CreateEntryLabel(string entry)
        {
            Label label = new Label("• " + HighlightDamage(entry));
            label.enableRichText = true;
            label.style.fontSize = 14f;
            label.style.color = TextColor;
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.marginTop = 0f;
            label.style.marginBottom = 0f;
            label.style.paddingTop = 0f;
            label.style.paddingBottom = 0f;
            return label;
        }

        private static string HighlightDamage(string entry)
        {
            int damageWord = (entry ?? string.Empty).IndexOf(" урона", StringComparison.OrdinalIgnoreCase);
            if (damageWord <= 0)
                return entry ?? string.Empty;

            int start = damageWord - 1;
            while (start >= 0 && char.IsDigit(entry[start]))
                start--;
            start++;
            if (start >= damageWord)
                return entry;

            string hex = ColorUtility.ToHtmlStringRGB(DamageColor);
            return entry.Substring(0, start) + "<color=#" + hex + ">" +
                   entry.Substring(start, damageWord - start) + "</color>" + entry.Substring(damageWord);
        }

        private static Button CreateNavigationButton(string text, Action clicked)
        {
            Button button = new Button(clicked) { text = text };
            button.style.width = 24f;
            button.style.height = 24f;
            button.style.marginLeft = 0f;
            button.style.marginRight = 0f;
            button.style.marginTop = 1f;
            button.style.marginBottom = 1f;
            button.style.paddingLeft = 0f;
            button.style.paddingRight = 0f;
            button.style.paddingTop = 0f;
            button.style.paddingBottom = 0f;
            button.style.fontSize = 11f;
            button.style.color = MutedTextColor;
            button.style.backgroundColor = ButtonBackground;
            SetBorder(button, ButtonBorder, 1f);
            SetRadius(button, 4f);
            button.RegisterCallback<PointerEnterEvent>(_ => button.style.backgroundColor = ButtonHoverBackground);
            button.RegisterCallback<PointerLeaveEvent>(_ => button.style.backgroundColor = ButtonBackground);
            return button;
        }

        private static void StyleNavigationAvailability(Button button, bool available)
        {
            if (button == null)
                return;

            button.style.display = DisplayStyle.Flex;
            button.style.opacity = available ? 0.82f : 0.30f;
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

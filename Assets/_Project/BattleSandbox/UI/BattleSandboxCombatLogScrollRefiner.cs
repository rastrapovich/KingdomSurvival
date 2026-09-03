using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace KingdomSurvival.BattleSandbox
{
    [DefaultExecutionOrder(13500)]
    internal sealed class BattleSandboxCombatLogScrollRefiner : MonoBehaviour
    {
        private const string ScrollName = "battle-sandbox-combat-log-scroll";
        private const float WheelStep = 24f;

        private static readonly BindingFlags InstanceFlags =
            BindingFlags.Instance | BindingFlags.NonPublic;

        private static readonly FieldInfo LogLabelField =
            typeof(BattleSandboxController).GetField("logLabel", InstanceFlags);

        private BattleSandboxController controller;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (SceneManager.GetActiveScene().name != "BattleSandbox")
                return;

            if (FindFirstObjectByType<BattleSandboxCombatLogScrollRefiner>() != null)
                return;

            GameObject host = new GameObject("BattleSandboxCombatLogScrollRefiner");
            host.hideFlags = HideFlags.HideAndDontSave;
            DontDestroyOnLoad(host);
            host.AddComponent<BattleSandboxCombatLogScrollRefiner>();
        }

        private void LateUpdate()
        {
            if (SceneManager.GetActiveScene().name != "BattleSandbox")
            {
                Destroy(gameObject);
                return;
            }

            if (controller == null)
                controller = FindFirstObjectByType<BattleSandboxController>();
            if (controller == null || LogLabelField == null)
                return;

            Label logLabel = LogLabelField.GetValue(controller) as Label;
            if (logLabel == null || logLabel.parent == null)
                return;

            if (logLabel.parent is ScrollView existing && existing.name == ScrollName)
                return;

            WrapLogLabel(logLabel);
        }

        private static void WrapLogLabel(Label logLabel)
        {
            VisualElement parent = logLabel.parent;
            if (parent == null)
                return;

            int index = parent.IndexOf(logLabel);
            StyleLength originalMarginTop = logLabel.style.marginTop;

            ScrollView scroll = new ScrollView(ScrollViewMode.Vertical)
            {
                name = ScrollName,
                verticalScrollerVisibility = ScrollerVisibility.Hidden,
                horizontalScrollerVisibility = ScrollerVisibility.Hidden
            };

            scroll.style.flexGrow = 1f;
            scroll.style.flexShrink = 1f;
            scroll.style.minHeight = 0f;
            scroll.style.marginTop = originalMarginTop;
            scroll.style.paddingLeft = 0f;
            scroll.style.paddingRight = 0f;
            scroll.style.paddingTop = 0f;
            scroll.style.paddingBottom = 0f;
            scroll.style.backgroundColor = Color.clear;
            scroll.style.borderLeftWidth = 0f;
            scroll.style.borderRightWidth = 0f;
            scroll.style.borderTopWidth = 0f;
            scroll.style.borderBottomWidth = 0f;

            logLabel.RemoveFromHierarchy();
            logLabel.style.marginTop = 0f;
            logLabel.style.flexGrow = 0f;
            logLabel.style.flexShrink = 0f;
            logLabel.style.width = Length.Percent(100f);

            scroll.Add(logLabel);
            RegisterWheelScrolling(scroll);
            parent.Insert(index, scroll);
        }

        private static void RegisterWheelScrolling(ScrollView scroll)
        {
            scroll.RegisterCallback<WheelEvent>(evt =>
            {
                if (Mathf.Approximately(evt.delta.y, 0f))
                    return;

                float contentHeight = scroll.contentContainer.layout.height;
                float viewportHeight = scroll.contentViewport.layout.height;
                if (float.IsNaN(contentHeight) || float.IsNaN(viewportHeight))
                    return;

                float maxOffset = Mathf.Max(0f, contentHeight - viewportHeight);
                if (maxOffset <= 0f)
                    return;

                float direction = Mathf.Sign(evt.delta.y);
                float nextOffset = Mathf.Clamp(
                    scroll.scrollOffset.y + direction * WheelStep,
                    0f,
                    maxOffset);

                if (Mathf.Approximately(nextOffset, scroll.scrollOffset.y))
                    return;

                scroll.scrollOffset = new Vector2(0f, nextOffset);
                evt.StopPropagation();
            }, TrickleDown.TrickleDown);
        }
    }
}

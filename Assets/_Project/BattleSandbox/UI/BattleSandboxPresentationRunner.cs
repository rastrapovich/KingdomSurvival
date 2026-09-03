using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace KingdomSurvival.BattleSandbox
{
    internal static class BattleSandboxPresentationBootstrap
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
            if (!string.Equals(scene.name, BattleSandboxSceneName, System.StringComparison.Ordinal))
            {
                if (runnerObject != null)
                    Object.Destroy(runnerObject);
                runnerObject = null;
                return;
            }

            if (runnerObject != null)
                return;

            runnerObject = new GameObject("Battle Sandbox Presentation");
            runnerObject.hideFlags = HideFlags.HideInHierarchy;
            runnerObject.AddComponent<BattleSandboxPresentationRunner>();
        }
    }

    internal sealed class BattleSandboxPresentationRunner : MonoBehaviour
    {
        private const string BoardName = "battle-sandbox-board";
        private const string SurfaceName = "battlefield-surface";
        private const string ActionsName = "battle-sandbox-actions";
        private const string ResultOverlayName = "battle-sandbox-result-overlay";
        private const string LogScrollName = "battle-sandbox-log-scroll";

        private VisualElement styledScreen;
        private VisualElement styledSurface;

        private void Update()
        {
            UIDocument[] documents = Object.FindObjectsByType<UIDocument>(FindObjectsSortMode.None);
            for (int i = 0; i < documents.Length; i++)
            {
                VisualElement root = documents[i] != null ? documents[i].rootVisualElement : null;
                if (root == null)
                    continue;

                VisualElement board = root.Q<VisualElement>(BoardName);
                if (board == null)
                {
                    styledScreen = null;
                    styledSurface = null;
                    continue;
                }

                VisualElement surface = board.parent != null && board.parent.name == SurfaceName
                    ? board.parent
                    : null;
                VisualElement body = surface != null ? surface.parent : board.parent;
                VisualElement screen = body != null ? body.parent : null;
                if (screen == null)
                    continue;

                if (styledScreen != screen || styledScreen.panel == null)
                {
                    ApplyLightBattleLayout(screen, body, board, surface);
                    styledScreen = screen;
                }

                if (surface != null && (styledSurface != surface || styledSurface.panel == null))
                {
                    StyleBattlefieldSurface(surface, board);
                    styledSurface = surface;
                }

                TrimRoundLabel(FindRoundLabel(screen));
                return;
            }
        }

        private static void ApplyLightBattleLayout(
            VisualElement screen,
            VisualElement body,
            VisualElement board,
            VisualElement surface)
        {
            screen.style.position = Position.Relative;
            screen.style.paddingLeft = 0f;
            screen.style.paddingRight = 0f;
            screen.style.paddingTop = 0f;
            screen.style.paddingBottom = 0f;
            screen.style.overflow = Overflow.Hidden;

            body.style.flexGrow = 1f;
            body.style.position = Position.Relative;
            body.style.marginLeft = 0f;
            body.style.marginRight = 0f;
            body.style.marginTop = 0f;
            body.style.marginBottom = 0f;

            StyleBoardViewport(board);
            if (surface != null)
                StyleBattlefieldSurface(surface, board);

            VisualElement header = screen.childCount > 0 ? screen[0] : null;
            VisualElement initiative = screen.childCount > 1 ? screen[1] : null;
            VisualElement sidebar = FindSidebar(body, board, surface);

            StyleHeader(header);
            StyleInitiative(initiative);
            StyleSidebarAndActions(screen, sidebar);

            header?.BringToFront();
            initiative?.BringToFront();
            sidebar?.BringToFront();
            screen.Q<VisualElement>(ActionsName)?.BringToFront();
            screen.Q<VisualElement>(ResultOverlayName)?.BringToFront();
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

        private static void StyleBattlefieldSurface(VisualElement surface, VisualElement board)
        {
            surface.style.flexGrow = 1f;
            surface.style.flexShrink = 1f;
            surface.style.minWidth = 0f;
            surface.style.minHeight = 0f;
            surface.style.marginLeft = 0f;
            surface.style.marginRight = 0f;
            surface.style.marginTop = 0f;
            surface.style.marginBottom = 0f;
            surface.style.position = Position.Relative;

            StyleBoardViewport(board);
        }

        private static void StyleBoardViewport(VisualElement board)
        {
            board.style.position = Position.Absolute;
            board.style.left = Length.Percent(10f);
            board.style.right = Length.Percent(10f);
            board.style.top = Length.Percent(10f);
            board.style.bottom = Length.Percent(8f);
            board.style.marginLeft = 0f;
            board.style.marginRight = 0f;
            board.style.marginTop = 0f;
            board.style.marginBottom = 0f;
            board.style.minWidth = 0f;
            board.style.minHeight = 0f;
            board.style.backgroundColor = Color.clear;
            board.style.borderLeftWidth = 0f;
            board.style.borderRightWidth = 0f;
            board.style.borderTopWidth = 0f;
            board.style.borderBottomWidth = 0f;
        }

        private static void StyleHeader(VisualElement header)
        {
            if (header == null)
                return;

            header.style.position = Position.Absolute;
            header.style.left = 16f;
            header.style.right = 16f;
            header.style.top = 12f;
            header.style.height = 40f;
            header.style.alignItems = Align.FlexStart;
            header.style.backgroundColor = Color.clear;

            if (header.childCount > 0)
            {
                VisualElement heading = header[0];
                if (heading.childCount > 0 && heading[0] is Label title)
                    title.style.display = DisplayStyle.None;

                if (heading.childCount > 1 && heading[1] is Label round)
                {
                    round.style.fontSize = 14f;
                    round.style.marginTop = 0f;
                    round.style.unityFontStyleAndWeight = FontStyle.Bold;
                    round.style.color = new Color(0.92f, 0.84f, 0.68f, 1f);
                    TrimRoundLabel(round);
                    round.schedule.Execute(() => TrimRoundLabel(round)).Every(16);
                }
            }

            if (header.childCount > 1 && header[1] is Button setupButton)
            {
                setupButton.style.width = 136f;
                setupButton.style.height = 32f;
                setupButton.style.opacity = 1f;
                StyleInteractiveButton(setupButton);
            }
        }

        private static Label FindRoundLabel(VisualElement screen)
        {
            if (screen == null || screen.childCount == 0)
                return null;

            VisualElement header = screen[0];
            if (header.childCount == 0)
                return null;

            VisualElement heading = header[0];
            return heading.childCount > 1 ? heading[1] as Label : null;
        }

        private static void TrimRoundLabel(Label round)
        {
            if (round == null)
                return;

            string text = round.text ?? string.Empty;
            int separator = text.IndexOf('·');
            if (separator >= 0)
                round.text = text.Substring(0, separator).TrimEnd();
        }

        private static void StyleInitiative(VisualElement initiative)
        {
            if (initiative == null)
                return;

            initiative.style.position = Position.Absolute;
            initiative.style.left = 0f;
            initiative.style.right = 0f;
            initiative.style.top = 4f;
            initiative.style.height = 92f;
            initiative.style.marginLeft = 0f;
            initiative.style.marginRight = 0f;
            initiative.style.marginTop = 0f;
            initiative.style.marginBottom = 0f;
            initiative.style.paddingLeft = 0f;
            initiative.style.paddingRight = 0f;
            initiative.style.paddingTop = 0f;
            initiative.style.paddingBottom = 0f;
            initiative.style.flexDirection = FlexDirection.Row;
            initiative.style.alignItems = Align.Center;
            initiative.style.justifyContent = Justify.Center;
            initiative.style.backgroundColor = Color.clear;
            initiative.style.borderLeftWidth = 0f;
            initiative.style.borderRightWidth = 0f;
            initiative.style.borderTopWidth = 0f;
            initiative.style.borderBottomWidth = 0f;
            initiative.pickingMode = PickingMode.Ignore;
        }

        private static void StyleSidebarAndActions(VisualElement screen, VisualElement sidebar)
        {
            if (sidebar == null)
                return;

            sidebar.style.position = Position.Absolute;
            sidebar.style.left = 14f;
            sidebar.style.bottom = 14f;
            sidebar.style.width = 292f;
            sidebar.style.maxHeight = 124f;
            sidebar.style.paddingLeft = 11f;
            sidebar.style.paddingRight = 11f;
            sidebar.style.paddingTop = 10f;
            sidebar.style.paddingBottom = 10f;
            sidebar.style.backgroundColor = new Color(0.045f, 0.052f, 0.055f, 0.78f);
            sidebar.style.borderLeftColor = new Color(0.52f, 0.47f, 0.35f, 0.42f);
            sidebar.style.borderRightColor = new Color(0.52f, 0.47f, 0.35f, 0.42f);
            sidebar.style.borderTopColor = new Color(0.52f, 0.47f, 0.35f, 0.42f);
            sidebar.style.borderBottomColor = new Color(0.52f, 0.47f, 0.35f, 0.42f);

            if (sidebar.childCount < 9)
                return;

            VisualElement currentUnit = sidebar[0];
            VisualElement currentStats = sidebar[1];
            VisualElement instruction = sidebar[2];
            VisualElement target = sidebar[3];
            VisualElement guard = sidebar[4];
            VisualElement endTurn = sidebar[5];
            VisualElement logTitle = sidebar[6];
            VisualElement log = sidebar[7];
            VisualElement result = sidebar[8];

            currentUnit.style.display = DisplayStyle.None;
            currentStats.style.display = DisplayStyle.None;
            instruction.style.display = DisplayStyle.None;
            target.style.display = DisplayStyle.None;
            logTitle.style.marginTop = 0f;

            ScrollView logScroll = new ScrollView(ScrollViewMode.Vertical)
            {
                name = LogScrollName,
                verticalScrollerVisibility = ScrollerVisibility.Auto,
                horizontalScrollerVisibility = ScrollerVisibility.Hidden
            };
            logScroll.style.height = 78f;
            logScroll.style.marginTop = 3f;
            logScroll.style.backgroundColor = new Color(0f, 0f, 0f, 0.08f);
            logScroll.style.overflow = Overflow.Hidden;
            log.style.marginTop = 0f;
            log.style.maxHeight = StyleKeyword.None;
            log.style.overflow = Overflow.Visible;
            log.RemoveFromHierarchy();
            logScroll.Add(log);
            sidebar.Insert(7, logScroll);

            VisualElement actions = new VisualElement { name = ActionsName };
            actions.style.position = Position.Absolute;
            actions.style.right = 14f;
            actions.style.bottom = 14f;
            actions.style.width = 176f;
            actions.style.flexDirection = FlexDirection.Column;
            actions.style.backgroundColor = Color.clear;
            screen.Add(actions);

            guard.RemoveFromHierarchy();
            endTurn.RemoveFromHierarchy();
            guard.style.marginTop = 0f;
            guard.style.marginBottom = 6f;
            endTurn.style.marginTop = 0f;
            actions.Add(guard);
            actions.Add(endTurn);
            if (guard is Button guardButton)
                StyleInteractiveButton(guardButton);
            if (endTurn is Button endTurnButton)
                StyleInteractiveButton(endTurnButton);

            VisualElement resultOverlay = new VisualElement { name = ResultOverlayName };
            resultOverlay.style.position = Position.Absolute;
            resultOverlay.style.left = 0f;
            resultOverlay.style.right = 0f;
            resultOverlay.style.top = 0f;
            resultOverlay.style.bottom = 0f;
            resultOverlay.style.alignItems = Align.Center;
            resultOverlay.style.justifyContent = Justify.Center;
            resultOverlay.style.backgroundColor = Color.clear;
            resultOverlay.pickingMode = PickingMode.Ignore;
            screen.Add(resultOverlay);

            result.RemoveFromHierarchy();
            result.style.width = 330f;
            result.style.marginTop = 0f;
            result.pickingMode = PickingMode.Position;
            resultOverlay.Add(result);
        }

        private static void StyleInteractiveButton(Button button)
        {
            if (button == null)
                return;

            Color normalBackground = new Color(0.14f, 0.16f, 0.17f, 0.96f);
            Color hoverBackground = new Color(0.23f, 0.25f, 0.24f, 1f);
            Color pressedBackground = new Color(0.31f, 0.27f, 0.17f, 1f);
            Color normalBorder = new Color(0.31f, 0.33f, 0.33f, 0.95f);
            Color hoverBorder = new Color(0.78f, 0.64f, 0.34f, 1f);

            void Apply(Color background, Color border)
            {
                button.style.backgroundColor = background;
                button.style.borderLeftColor = border;
                button.style.borderRightColor = border;
                button.style.borderTopColor = border;
                button.style.borderBottomColor = border;
            }

            Apply(normalBackground, normalBorder);
            button.pickingMode = PickingMode.Position;
            button.RegisterCallback<PointerEnterEvent>(_ =>
            {
                if (button.enabledSelf)
                    Apply(hoverBackground, hoverBorder);
            });
            button.RegisterCallback<PointerLeaveEvent>(_ => Apply(normalBackground, normalBorder));
            button.RegisterCallback<PointerDownEvent>(_ =>
            {
                if (button.enabledSelf)
                    Apply(pressedBackground, hoverBorder);
            });
            button.RegisterCallback<PointerUpEvent>(_ =>
            {
                if (button.enabledSelf)
                    Apply(hoverBackground, hoverBorder);
            });
        }
    }
}

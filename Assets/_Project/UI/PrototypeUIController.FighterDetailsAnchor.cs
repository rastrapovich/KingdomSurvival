using System;
using UnityEngine;
using UnityEngine.UIElements;

public partial class PrototypeUIController
{
    private bool fighterDetailsAnchorInitialized;
    private VisualElement fighterDetailsDimmer;
    private VisualElement pendingFighterDetailsAnchor;
    private string lastAnchoredFighterId;
    private IVisualElementScheduledItem fighterDetailsAnchorPoll;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void InitializeFighterDetailsAnchorRuntime()
    {
        PrototypeUIController controller =
            UnityEngine.Object.FindAnyObjectByType<PrototypeUIController>();
        if (controller == null)
            return;

        UIDocument document = controller.GetComponent<UIDocument>();
        if (document == null)
            return;

        document.rootVisualElement.schedule
            .Execute(controller.TryInitializeFighterDetailsAnchor)
            .ExecuteLater(210);
    }

    private void TryInitializeFighterDetailsAnchor()
    {
        if (fighterDetailsAnchorInitialized)
            return;

        if (interfaceRoot == null || fighterDetailsWindow == null)
        {
            UIDocument document = GetComponent<UIDocument>();
            if (document != null)
            {
                document.rootVisualElement.schedule
                    .Execute(TryInitializeFighterDetailsAnchor)
                    .ExecuteLater(60);
            }
            return;
        }

        fighterDetailsDimmer = new VisualElement
        {
            name = "fighter-details-dimmer",
            focusable = true
        };
        fighterDetailsDimmer.style.display = DisplayStyle.None;
        fighterDetailsDimmer.style.position = Position.Absolute;
        fighterDetailsDimmer.style.left = 0f;
        fighterDetailsDimmer.style.right = 0f;
        fighterDetailsDimmer.style.top = 0f;
        fighterDetailsDimmer.style.bottom = 0f;
        fighterDetailsDimmer.style.backgroundColor =
            new Color(0.01f, 0.015f, 0.02f, 0.72f);

        fighterDetailsDimmer.RegisterCallback<PointerDownEvent>(evt =>
        {
            if (evt.button != 0 || evt.target != fighterDetailsDimmer)
                return;
            CloseAnchoredFighterDetails();
            evt.StopPropagation();
        });
        fighterDetailsDimmer.RegisterCallback<KeyDownEvent>(evt =>
        {
            if (evt.keyCode != KeyCode.Escape)
                return;
            CloseAnchoredFighterDetails();
            evt.StopPropagation();
        });

        interfaceRoot.Add(fighterDetailsDimmer);

        // Один делегированный обработчик отвечает за все карточки бойцов.
        // Карточки гарнизонов регулярно пересоздаются, поэтому нельзя все от того, успел ли конкретный экземпляр получить локальный callback.
        interfaceRoot.RegisterCallback<PointerDownEvent>(
            CaptureFighterDetailsAnchor,
            TrickleDown.TrickleDown);

        fighterDetailsAnchorPoll = interfaceRoot.schedule
            .Execute(TickFighterDetailsAnchor)
            .Every(30);
        fighterDetailsAnchorInitialized = true;
    }

    private void CaptureFighterDetailsAnchor(PointerDownEvent evt)
    {
        if (evt.button != 1 || isGameOver || gameState == null)
            return;

        VisualElement anchor = FindFighterCard(evt.target as VisualElement);

        // Disabled Button может не стать обычной целью события. Panel.Pick
        // позволяет всё равно найти фактическую карточку под курсором.
        if (anchor == null && interfaceRoot != null && interfaceRoot.panel != null)
        {
            VisualElement picked = interfaceRoot.panel.Pick(evt.position);
            anchor = FindFighterCard(picked);
        }

        if (anchor == null)
            return;

        string fighterId = ResolveFighterIdFromCard(anchor);
        if (string.IsNullOrEmpty(fighterId))
            return;

        pendingFighterDetailsAnchor = anchor;
        OpenFighterDetails(fighterId);

        // Не отдаём ПКМ дальше локальным обработчикам карточки: открытие уже
        // выполнено здесь один раз и больше не зависит от polling-декоратора.
        evt.StopPropagation();
    }

    private VisualElement FindFighterCard(VisualElement start)
    {
        VisualElement current = start;
        while (current != null && current != interfaceRoot)
        {
            if (current.ClassListContains("fighter-card") ||
                current.ClassListContains("battle-fighter-card") ||
                current.ClassListContains("battle-result-fighter-card"))
            {
                return current;
            }

            current = current.parent;
        }

        return null;
    }

    private string ResolveFighterIdFromCard(VisualElement card)
    {
        if (card == null || gameState == null || gameState.Fighters == null)
            return null;

        foreach (FighterData fighter in gameState.Fighters)
        {
            if (CardContainsExactLabel(card, fighter.Name))
                return fighter.Id;
        }

        return null;
    }

    private static bool CardContainsExactLabel(
        VisualElement element,
        string expectedText)
    {
        Label label = element as Label;
        if (label != null && label.text == expectedText)
            return true;

        foreach (VisualElement child in element.Children())
        {
            if (CardContainsExactLabel(child, expectedText))
                return true;
        }

        return false;
    }

    private void TickFighterDetailsAnchor()
    {
        if (fighterDetailsWindow == null || fighterDetailsDimmer == null)
            return;

        bool open = !string.IsNullOrEmpty(openedFighterDetailsId) &&
                    fighterDetailsWindow.resolvedStyle.display != DisplayStyle.None;
        if (!open)
        {
            fighterDetailsDimmer.style.display = DisplayStyle.None;
            lastAnchoredFighterId = null;
            return;
        }

        fighterDetailsDimmer.style.display = DisplayStyle.Flex;
        fighterDetailsDimmer.BringToFront();
        fighterDetailsWindow.BringToFront();

        if (pendingFighterDetailsAnchor != null ||
            lastAnchoredFighterId != openedFighterDetailsId)
        {
            PositionFighterDetailsAboveAnchor(pendingFighterDetailsAnchor);
            pendingFighterDetailsAnchor = null;
            lastAnchoredFighterId = openedFighterDetailsId;
        }

        fighterDetailsDimmer.Focus();
        fighterDetailsWindow.BringToFront();
    }

    private void PositionFighterDetailsAboveAnchor(VisualElement anchor)
    {
        float windowWidth = fighterDetailsWindow.resolvedStyle.width;
        float windowHeight = fighterDetailsWindow.resolvedStyle.height;
        if (float.IsNaN(windowWidth) || windowWidth < 100f)
            windowWidth = 440f;
        if (float.IsNaN(windowHeight) || windowHeight < 100f)
            windowHeight = 360f;

        float rootWidth = Math.Max(windowWidth + 24f, interfaceRoot.resolvedStyle.width);
        float rootHeight = Math.Max(windowHeight + 24f, interfaceRoot.resolvedStyle.height);

        float left = (rootWidth - windowWidth) * 0.5f;
        float top = (rootHeight - windowHeight) * 0.5f;

        if (anchor != null && anchor.panel != null)
        {
            Rect bounds = anchor.worldBound;
            Vector2 anchorTopLeft = interfaceRoot.WorldToLocal(
                new Vector2(bounds.xMin, bounds.yMin));
            Vector2 anchorBottomRight = interfaceRoot.WorldToLocal(
                new Vector2(bounds.xMax, bounds.yMax));

            float centerX = (anchorTopLeft.x + anchorBottomRight.x) * 0.5f;
            left = centerX - windowWidth * 0.5f;
            top = anchorTopLeft.y - windowHeight - 10f;

            if (top < 12f)
                top = anchorBottomRight.y + 10f;
        }

        left = Mathf.Clamp(left, 12f, Math.Max(12f, rootWidth - windowWidth - 12f));
        top = Mathf.Clamp(top, 12f, Math.Max(12f, rootHeight - windowHeight - 12f));

        fighterDetailsWindow.style.right = StyleKeyword.Auto;
        fighterDetailsWindow.style.left = left;
        fighterDetailsWindow.style.top = top;
    }

    private void CloseAnchoredFighterDetails()
    {
        CloseFighterDetails();
        pendingFighterDetailsAnchor = null;
        lastAnchoredFighterId = null;
        if (fighterDetailsDimmer != null)
            fighterDetailsDimmer.style.display = DisplayStyle.None;
    }
}

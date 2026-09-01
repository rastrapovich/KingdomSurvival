using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

public partial class PrototypeUIController
{
    private bool worldMapPolishInitialized;
    private List<string> cancelledExpeditionRosterSnapshot;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void InitializeWorldMapPolishRuntime()
    {
        PrototypeUIController controller =
            UnityEngine.Object.FindAnyObjectByType<PrototypeUIController>();

        if (controller == null)
            return;

        UIDocument document = controller.GetComponent<UIDocument>();

        if (document == null)
            return;

        document.rootVisualElement.schedule
            .Execute(controller.TryInitializeWorldMapPolish)
            .ExecuteLater(20);
    }

    private void TryInitializeWorldMapPolish()
    {
        if (worldMapPolishInitialized)
            return;

        if (interfaceRoot == null || gameState == null)
        {
            ScheduleWorldMapPolishRetry();
            return;
        }

        VisualElement map =
            interfaceRoot.Q<VisualElement>("world-map");
        Button capitalButton =
            interfaceRoot.Q<Button>("world-map-capital-button");
        Button cancelButton =
            interfaceRoot.Q<Button>("return-expedition-button");

        if (map == null || capitalButton == null || cancelButton == null)
        {
            ScheduleWorldMapPolishRetry();
            return;
        }

        HideLegacyWorldMapDecoration(map);
        EnsureWorldMapGrid(map);
        StyleCapitalMarker(capitalButton);
        RegisterCancelledRosterPreservation(cancelButton, capitalButton);

        worldMapPolishInitialized = true;
    }

    private void ScheduleWorldMapPolishRetry()
    {
        UIDocument document = GetComponent<UIDocument>();

        if (document == null)
            return;

        document.rootVisualElement.schedule
            .Execute(TryInitializeWorldMapPolish)
            .ExecuteLater(20);
    }

    private static void HideLegacyWorldMapDecoration(VisualElement map)
    {
        map.Query<VisualElement>(className: "world-map-land")
            .ForEach(element =>
                element.style.display = DisplayStyle.None);

        Label compass =
            map.Q<Label>(className: "world-map-compass");

        if (compass != null)
            compass.style.display = DisplayStyle.None;
    }

    private static void EnsureWorldMapGrid(VisualElement map)
    {
        if (map.Q<VisualElement>("world-map-grid-overlay") != null)
            return;

        VisualElement overlay = new VisualElement
        {
            name = "world-map-grid-overlay",
            pickingMode = PickingMode.Ignore
        };

        overlay.style.position = Position.Absolute;
        overlay.style.left = 0f;
        overlay.style.right = 0f;
        overlay.style.top = 0f;
        overlay.style.bottom = 0f;

        Color gridColor = new Color32(95, 99, 92, 72);

        for (int x = 0; x < WorldMapNavigation.GridWidth; x++)
        {
            VisualElement line = new VisualElement
            {
                pickingMode = PickingMode.Ignore
            };

            line.style.position = Position.Absolute;
            line.style.left = new Length(
                x * 100f / (WorldMapNavigation.GridWidth - 1),
                LengthUnit.Percent);
            line.style.top = 0f;
            line.style.bottom = 0f;
            line.style.width = 1f;
            line.style.backgroundColor = gridColor;
            overlay.Add(line);
        }

        for (int y = 0; y < WorldMapNavigation.GridHeight; y++)
        {
            VisualElement line = new VisualElement
            {
                pickingMode = PickingMode.Ignore
            };

            line.style.position = Position.Absolute;
            line.style.left = 0f;
            line.style.right = 0f;
            line.style.top = new Length(
                y * 100f / (WorldMapNavigation.GridHeight - 1),
                LengthUnit.Percent);
            line.style.height = 1f;
            line.style.backgroundColor = gridColor;
            overlay.Add(line);
        }

        map.Add(overlay);
        overlay.SendToBack();
    }

    private static void StyleCapitalMarker(Button capitalButton)
    {
        const float diameter = 68f;
        const float radius = diameter / 2f;

        capitalButton.style.left = new Length(
            WorldMapNavigation.CapitalXPercent,
            LengthUnit.Percent);
        capitalButton.style.top = new Length(
            WorldMapNavigation.CapitalYPercent,
            LengthUnit.Percent);
        capitalButton.style.width = diameter;
        capitalButton.style.minWidth = diameter;
        capitalButton.style.maxWidth = diameter;
        capitalButton.style.height = diameter;
        capitalButton.style.minHeight = diameter;
        capitalButton.style.maxHeight = diameter;
        capitalButton.style.marginLeft = -radius;
        capitalButton.style.marginTop = -radius;
        capitalButton.style.paddingLeft = 4f;
        capitalButton.style.paddingRight = 4f;
        capitalButton.style.paddingTop = 4f;
        capitalButton.style.paddingBottom = 4f;
        capitalButton.style.borderTopLeftRadius = radius;
        capitalButton.style.borderTopRightRadius = radius;
        capitalButton.style.borderBottomLeftRadius = radius;
        capitalButton.style.borderBottomRightRadius = radius;
        capitalButton.style.fontSize = 9f;
    }

    private void RegisterCancelledRosterPreservation(
        Button cancelButton,
        Button capitalButton)
    {
        cancelButton.RegisterCallback<PointerDownEvent>(
            CaptureRosterBeforePotentialCancellation,
            TrickleDown.TrickleDown);
        capitalButton.RegisterCallback<PointerDownEvent>(
            CaptureRosterBeforePotentialCancellation,
            TrickleDown.TrickleDown);

        cancelButton.clicked += ScheduleCancelledRosterRestore;
        capitalButton.clicked += ScheduleCancelledRosterRestore;
    }

    private void CaptureRosterBeforePotentialCancellation(
        PointerDownEvent pointerEvent)
    {
        cancelledExpeditionRosterSnapshot = null;

        if (pointerEvent.button != 0 ||
            gameState == null ||
            !gameState.HasActiveExpedition ||
            !gameState.CanCancelPreparedExpedition)
        {
            return;
        }

        cancelledExpeditionRosterSnapshot =
            new List<string>(gameState.ActiveExpedition.FighterIds);
    }

    private void ScheduleCancelledRosterRestore()
    {
        if (cancelledExpeditionRosterSnapshot == null ||
            cancelledExpeditionRosterSnapshot.Count == 0 ||
            interfaceRoot == null)
        {
            return;
        }

        List<string> rosterToRestore =
            new List<string>(cancelledExpeditionRosterSnapshot);
        cancelledExpeditionRosterSnapshot = null;

        interfaceRoot.schedule
            .Execute(() => RestoreCancelledRoster(rosterToRestore))
            .ExecuteLater(1);
    }

    private void RestoreCancelledRoster(List<string> rosterToRestore)
    {
        if (gameState == null || gameState.HasActiveExpedition)
            return;

        selectedFighterIds.Clear();

        foreach (string fighterId in rosterToRestore)
        {
            if (gameState.FindFighter(fighterId) != null)
                selectedFighterIds.Add(fighterId);
        }

        RefreshStableUiAfterStateChange();
    }
}

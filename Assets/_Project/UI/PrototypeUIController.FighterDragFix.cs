using UnityEngine;
using UnityEngine.UIElements;

public partial class PrototypeUIController
{
    private bool fighterDragRootTrackingInstalled;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void InstallFighterDragRootTrackingAfterSceneLoad()
    {
        PrototypeUIController controller =
            UnityEngine.Object.FindAnyObjectByType<PrototypeUIController>();
        if (controller == null)
            return;

        UIDocument document = controller.GetComponent<UIDocument>();
        if (document == null)
            return;

        document.rootVisualElement.schedule
            .Execute(controller.TryInstallFighterDragRootTracking)
            .ExecuteLater(120);
    }

    private void TryInstallFighterDragRootTracking()
    {
        if (fighterDragRootTrackingInstalled)
            return;

        if (interfaceRoot == null)
        {
            UIDocument document = GetComponent<UIDocument>();
            if (document != null)
            {
                document.rootVisualElement.schedule
                    .Execute(TryInstallFighterDragRootTracking)
                    .ExecuteLater(60);
            }
            return;
        }

        interfaceRoot.RegisterCallback<PointerMoveEvent>(
            OnRootFighterPointerMove,
            TrickleDown.TrickleDown);
        interfaceRoot.RegisterCallback<PointerUpEvent>(
            OnRootFighterPointerUp,
            TrickleDown.TrickleDown);

        fighterDragRootTrackingInstalled = true;
    }

    private void OnRootFighterPointerMove(PointerMoveEvent pointerEvent)
    {
        if (!CanTrackFighterPointer(pointerEvent.pointerId))
            return;

        if (!fighterDragStarted &&
            Vector2.Distance(fighterDragStartPosition, pointerEvent.position) >=
            FighterDragThreshold)
        {
            BeginFighterDrag(pointerEvent.position);
        }

        if (fighterDragStarted)
        {
            UpdateFighterDragGhost(pointerEvent.position);
            UpdateFighterDropHighlights(pointerEvent.position);
        }

        // Button/Clickable may own pointer capture internally. Once a fighter drag
        // is active, the root becomes the authoritative tracker so child handlers
        // cannot interrupt or duplicate the drag lifecycle.
        pointerEvent.StopPropagation();
    }

    private void OnRootFighterPointerUp(PointerUpEvent pointerEvent)
    {
        if (!CanTrackFighterPointer(pointerEvent.pointerId))
            return;

        string fighterId = draggedFighterId;
        bool wasDragging = fighterDragStarted;
        bool droppedToCommander =
            commanderGarrisonDropZone != null &&
            commanderGarrisonDropZone.worldBound.Contains(pointerEvent.position);
        bool droppedToCapital =
            capitalGarrisonDropZone != null &&
            capitalGarrisonDropZone.worldBound.Contains(pointerEvent.position);

        if (draggedFighterCard != null &&
            draggedFighterCard.HasPointerCapture(pointerEvent.pointerId))
        {
            draggedFighterCard.ReleasePointer(pointerEvent.pointerId);
        }

        CleanupFighterDrag();

        if (!wasDragging)
        {
            ToggleFighterAssignment(fighterId);
        }
        else if (droppedToCommander)
        {
            MoveFighterToCommander(fighterId, true);
        }
        else if (droppedToCapital)
        {
            MoveFighterToCommander(fighterId, false);
        }

        pointerEvent.StopPropagation();
    }

    private bool CanTrackFighterPointer(int pointerId)
    {
        return
            draggedFighterCard != null &&
            !string.IsNullOrEmpty(draggedFighterId) &&
            draggedFighterPointerId == pointerId &&
            !isGameOver &&
            gameState != null &&
            !gameState.HasActiveExpedition;
    }
}

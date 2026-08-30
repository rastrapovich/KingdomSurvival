using UnityEngine;
using UnityEngine.UIElements;

public partial class PrototypeUIController
{
    private bool persistentCommanderVisibilityFixApplied;
    private IVisualElementScheduledItem persistentCommanderVisibilityFixItem;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void InitializePersistentCommanderVisibilityFix()
    {
        PrototypeUIController controller =
            UnityEngine.Object.FindAnyObjectByType<PrototypeUIController>();

        if (controller == null)
            return;

        UIDocument document = controller.GetComponent<UIDocument>();

        if (document == null)
            return;

        controller.persistentCommanderVisibilityFixItem =
            document.rootVisualElement.schedule
                .Execute(controller.TryApplyPersistentCommanderVisibilityFix)
                .Every(100);
    }

    private void TryApplyPersistentCommanderVisibilityFix()
    {
        if (persistentCommanderVisibilityFixApplied)
            return;

        if (!persistentCommanderShellInitialized ||
            interfaceRoot == null ||
            persistentCommanderPanel == null)
        {
            return;
        }

        VisualElement sidebar =
            interfaceRoot.Q<VisualElement>(className: "shell-sidebar");
        VisualElement reportsDock =
            interfaceRoot.Q<VisualElement>(className: "shell-reports-dock");

        if (sidebar == null || reportsDock == null)
            return;

        // Панель ранее создавалась в памяти, но не добавлялась в иерархию UI.
        if (persistentCommanderPanel.parent != sidebar)
        {
            persistentCommanderPanel.RemoveFromHierarchy();
            sidebar.Add(persistentCommanderPanel);
        }

        // Донесения занимают только оставшуюся высоту над фиксированной
        // плашкой командира, а не вытесняют её за пределы сайдбара.
        sidebar.style.flexDirection = FlexDirection.Column;
        sidebar.style.alignItems = Align.Stretch;

        reportsDock.style.position = Position.Relative;
        reportsDock.style.width = Length.Percent(100);
        reportsDock.style.height = 0;
        reportsDock.style.minHeight = 120;
        reportsDock.style.flexGrow = 1;
        reportsDock.style.flexShrink = 1;
        reportsDock.style.marginBottom = 0;

        persistentCommanderPanel.style.position = Position.Relative;
        persistentCommanderPanel.style.width = Length.Percent(100);
        persistentCommanderPanel.style.height = 231;
        persistentCommanderPanel.style.minHeight = 231;
        persistentCommanderPanel.style.maxHeight = 231;
        persistentCommanderPanel.style.flexGrow = 0;
        persistentCommanderPanel.style.flexShrink = 0;
        persistentCommanderPanel.style.marginTop = 7;
        persistentCommanderPanel.style.marginBottom = 0;

        persistentCommanderVisibilityFixApplied = true;
        persistentCommanderVisibilityFixItem?.Pause();
    }
}

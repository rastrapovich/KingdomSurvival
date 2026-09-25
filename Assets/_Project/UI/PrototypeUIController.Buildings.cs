using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

// ПР-07А-2: каталог построек удалён с экрана Дома (PR07_HOME_SPEC §9.2).
// От прежнего экрана построек остался только перенос однократных уведомлений
// BuildingSystem в донесения — например, о возврате денег за отменённую
// стройку старого сохранения.
public partial class PrototypeUIController
{
    private IVisualElementScheduledItem buildingNoticeSchedule;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void InstallBuildingNoticesAfterSceneLoad()
    {
        PrototypeUIController controller =
            UnityEngine.Object.FindFirstObjectByType<PrototypeUIController>();
        if (controller == null)
            return;

        UIDocument document = controller.GetComponent<UIDocument>();
        if (document == null)
            return;

        controller.buildingNoticeSchedule?.Pause();
        controller.buildingNoticeSchedule = document.rootVisualElement.schedule
            .Execute(controller.DeliverBuildingNotices)
            .Every(500);
    }

    private void DeliverBuildingNotices()
    {
        if (gameState == null)
            return;

        List<string> notices = BuildingSystem.ConsumeNotices(gameState);
        foreach (string notice in notices)
            AddReport(notice);
    }
}

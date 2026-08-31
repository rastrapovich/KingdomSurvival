using System.Collections.Generic;
using UnityEngine.UIElements;

public partial class PrototypeUIController
{
    private sealed class QueuedModal
    {
        public string Title;
        public string Description;
        public string Consequence;
        public ExpeditionDecisionOccurrence Decision;
        public int ReportIndex = -1;
    }

    private sealed class ExpeditionReturnSnapshot
    {
        public string CommanderName;
        public int FighterCount;
        public int ArmyGold;
        public int ArmySupply;
    }

    private readonly Queue<QueuedModal> queuedModals = new Queue<QueuedModal>();
    private readonly Dictionary<int, int> incidentReportIndexes =
        new Dictionary<int, int>();
    private QueuedModal activeQueuedModal;

    private void QueueDayResolutionModals(
        DayResolutionResult result,
        int reportIndex)
    {
        if (result == null)
            return;

        if (gameState.HasPendingExpeditionDecision)
        {
            queuedModals.Enqueue(new QueuedModal
            {
                Decision = gameState.ActiveExpedition.PendingDecision,
                ReportIndex = reportIndex
            });
        }

        QueueNotice(result.ResearchNotice, reportIndex);
        QueueNotice(result.ExpeditionReturnNotice, reportIndex);

        if (queuedModals.Count > 0)
            MarkReportUnread(reportIndex);

        RefreshEndDayAvailability();
    }

    private void QueueNotice(DayModalNotice notice, int reportIndex)
    {
        if (notice == null)
            return;

        queuedModals.Enqueue(new QueuedModal
        {
            Title = notice.Title,
            Description = notice.Description,
            Consequence = notice.Consequence,
            ReportIndex = reportIndex
        });
    }

    private ExpeditionReturnSnapshot CaptureExpeditionReturnSnapshot()
    {
        if (gameState == null || !gameState.HasActiveExpedition)
            return null;

        CommanderData commander =
            gameState.FindCommander(gameState.ActiveExpedition.CommanderId);

        return new ExpeditionReturnSnapshot
        {
            CommanderName = commander != null ? commander.Name : "Командир",
            FighterCount = gameState.ActiveExpedition.FighterIds.Count,
            ArmyGold = gameState.ArmyGold,
            ArmySupply = gameState.ArmySupply
        };
    }

    private void AddReturnNoticeIfCompleted(
        DayResolutionResult result,
        ExpeditionReturnSnapshot snapshot)
    {
        if (result == null || snapshot == null ||
            gameState.HasActiveExpedition ||
            result.ExpeditionReturnNotice != null)
        {
            return;
        }

        result.ExpeditionReturnNotice = new DayModalNotice
        {
            Title = "ЭКСПЕДИЦИЯ ВЕРНУЛАСЬ",
            Description =
                snapshot.CommanderName + " и " + snapshot.FighterCount +
                " воинов прибыли в столицу. Отряд не расформирован: " +
                "перемещение бойцов между гарнизонами выполняет только игрок.",
            Consequence =
                "В столицу передано: золото +" + snapshot.ArmyGold +
                ", пища +" + snapshot.ArmySupply + ".\n" +
                "Потери: нет.\nОпыт: система пока не реализована.\n" +
                "Состояние бойцов: без изменений."
        };
    }

    private void TryShowNextQueuedModal()
    {
        if (isGameOver || activeQueuedModal != null || queuedModals.Count == 0)
            return;

        activeQueuedModal = queuedModals.Dequeue();

        if (activeQueuedModal.Decision != null)
        {
            OpenDecision(activeQueuedModal.Decision);
        }
        else
        {
            openedIncident = null;
            openedDecision = null;
            incidentModalTitle.text = activeQueuedModal.Title;
            incidentModalDescription.text = activeQueuedModal.Description;
            incidentModalConsequence.text = activeQueuedModal.Consequence;
            incidentUnderstoodButton.text = "ПОНЯТНО";
            incidentUnderstoodButton.style.display = DisplayStyle.Flex;
            decisionOptionAButton.style.display = DisplayStyle.None;
            decisionOptionBButton.style.display = DisplayStyle.None;
            incidentModalOverlay.style.display = DisplayStyle.Flex;
        }

        RefreshEndDayAvailability();
    }

    private void FinishActiveQueuedModal()
    {
        if (activeQueuedModal == null)
            return;

        int reportIndex = activeQueuedModal.ReportIndex;
        activeQueuedModal = null;
        HideIncidentModal();
        MarkReportReadIfFullyAcknowledged(reportIndex);
        ScheduleRoyalReportsRefresh();
        TryShowNextQueuedModal();
        RefreshEndDayAvailability();
    }

    private void ClearQueuedModals()
    {
        queuedModals.Clear();
        incidentReportIndexes.Clear();
        activeQueuedModal = null;
    }

    private void RegisterIncidentReports(
        List<ExpeditionIncidentOccurrence> incidents,
        int reportIndex)
    {
        if (incidents == null || incidents.Count == 0 || reportIndex < 0)
            return;

        foreach (ExpeditionIncidentOccurrence incident in incidents)
            incidentReportIndexes[incident.Id] = reportIndex;

        MarkReportUnread(reportIndex);
    }

    private void AcknowledgeIncidentReport(int incidentId)
    {
        int reportIndex;
        if (!incidentReportIndexes.TryGetValue(incidentId, out reportIndex))
            return;

        incidentReportIndexes.Remove(incidentId);
        MarkReportReadIfFullyAcknowledged(reportIndex);
    }

    private void MarkReportReadIfFullyAcknowledged(int reportIndex)
    {
        if (reportIndex < 0)
            return;

        if (activeQueuedModal != null &&
            activeQueuedModal.ReportIndex == reportIndex)
            return;

        foreach (QueuedModal queued in queuedModals)
        {
            if (queued.ReportIndex == reportIndex)
                return;
        }

        foreach (int pendingReportIndex in incidentReportIndexes.Values)
        {
            if (pendingReportIndex == reportIndex)
                return;
        }

        MarkReportRead(reportIndex);
    }

    private bool HasBlockingModalWork()
    {
        return gameState != null &&
               (gameState.HasPendingExpeditionDecision ||
                activeQueuedModal != null ||
                queuedModals.Count > 0);
    }

    private void RefreshEndDayAvailability()
    {
        if (endDayButton == null || gameState == null)
            return;

        bool blocked = HasBlockingModalWork();
        endDayButton.SetEnabled(!isGameOver && !blocked);
        endDayButton.tooltip = blocked
            ? "Сначала примите обязательное решение или закройте важное донесение"
            : "Завершить день";
    }

    private void MarkReportUnread(int reportIndex)
    {
        if (reportIndex < 0 || reportIndex >= reportHistory.Count)
            return;

        reportRequiresAcknowledgement[reportIndex] = true;
        reportReadStates[reportIndex] = false;
        renderedReportHash = int.MinValue;
        ScheduleRoyalReportsRefresh();
    }

    private void MarkReportRead(int reportIndex)
    {
        if (reportIndex < 0 || reportIndex >= reportHistory.Count)
            return;

        reportRequiresAcknowledgement[reportIndex] = true;
        reportReadStates[reportIndex] = true;
        renderedReportHash = int.MinValue;
    }

    private void RegisterSupplyHoldCallbacks(Button button, int delta)
    {
        button.RegisterCallback<PointerDownEvent>(evt =>
        {
            if (evt.button != 0 || isGameOver)
                return;

            StopSupplyHold();
            supplyHoldDelta = delta;
            supplyHoldRepeated = false;
            supplyHoldSchedule = button.schedule
                .Execute(RepeatSupplyWhileHeld)
                .StartingIn(350)
                .Every(75);
        });

        button.RegisterCallback<PointerUpEvent>(_ => StopSupplyHold());
        button.RegisterCallback<PointerCancelEvent>(_ => StopSupplyHold());
        button.RegisterCallback<PointerCaptureOutEvent>(_ => StopSupplyHold());
    }

    private void RepeatSupplyWhileHeld()
    {
        if (isGameOver || !gameState.CanAdjustArmySupply)
        {
            StopSupplyHold();
            return;
        }

        bool changed = supplyHoldDelta > 0
            ? gameState.TryAddArmySupply()
            : gameState.TryRemoveArmySupply();

        if (!changed)
        {
            StopSupplyHold();
            return;
        }

        supplyHoldRepeated = true;
        RefreshStableResourceUi();
    }

    private void StopSupplyHold()
    {
        if (supplyHoldSchedule != null)
            supplyHoldSchedule.Pause();
        supplyHoldSchedule = null;
    }

    private bool ConsumeRepeatedSupplyClick()
    {
        if (!supplyHoldRepeated)
            return false;

        supplyHoldRepeated = false;
        return true;
    }
}

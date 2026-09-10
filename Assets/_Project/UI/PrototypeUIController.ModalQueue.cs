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
    private bool resumeAfterBlockingModal;

    private void QueueStrategicResultModals(
        StrategicSimulationResult result,
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

        RefreshTimeControlAvailability();
    }

    private void QueueNotice(StrategicModalNotice notice, int reportIndex)
    {
        if (notice == null)
            return;

        // Прибытие в известную локацию является обязательным выбором.
        // Старый PendingBattle удалён; фабрика всегда создаёт обычное
        // сюжетно-исследовательское решение, пока не появится мост BattleSandbox.
        if (notice.Title == "АРМИЯ ПРИБЫЛА" || notice.Title == "ОТРЯД ПРИБЫЛ")
        {
            ExpeditionDecisionOccurrence arrivalDecision;
            if (LocationArrivalDecisionFactory.TryCreate(
                    gameState,
                    out arrivalDecision))
            {
                return;
            }
        }

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
        StrategicSimulationResult result,
        ExpeditionReturnSnapshot snapshot)
    {
        if (result == null || snapshot == null ||
            gameState.HasActiveExpedition ||
            result.ExpeditionReturnNotice != null)
        {
            return;
        }

        result.ExpeditionReturnNotice = new StrategicModalNotice
        {
            Title = "ЭКСПЕДИЦИЯ ВЕРНУЛАСЬ",
            Description =
                snapshot.CommanderName + " и " + snapshot.FighterCount +
                " бойцов прибыли в поселение.",
            Consequence =
                "В поселение передано: золото +" + snapshot.ArmyGold +
                ", пища +" + snapshot.ArmySupply + "."
        };
    }

    private void TryShowNextQueuedModal()
    {
        if (isGameOver || activeQueuedModal != null || queuedModals.Count == 0)
            return;

        activeQueuedModal = queuedModals.Dequeue();
        PauseForBlockingModal();

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

        RefreshTimeControlAvailability();
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
        RefreshTimeControlAvailability();
        ResumeAfterBlockingModalIfReady();
    }

    private void ClearQueuedModals()
    {
        queuedModals.Clear();
        incidentReportIndexes.Clear();
        activeQueuedModal = null;
        resumeAfterBlockingModal = false;
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

        if (activeQueuedModal != null && activeQueuedModal.ReportIndex == reportIndex)
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

    // P09-T05: экран Лагеря должен останавливать стратегическое время как
    // любая другая блокирующая модалка (раздел "Открытие лагеря не тратит
    // время"), но, в отличие от них, обязан пропускать поверх себя D11C
    // ("Затем автоматически запускается D11C... поверх лагерного экрана") —
    // поэтому вынесено отдельным условием, а не в общий список ниже:
    // HasBlockingModalWork используется для паузы времени (включает Camp),
    // HasBlockingModalWorkExceptCamp — только в TryOpenNarrativeDialogueById,
    // чтобы диалог мог открыться, пока камп уже открыт.
    private bool HasBlockingModalWorkExceptCamp()
    {
        return gameState != null &&
               (IsNarrativeDialogueActive ||
                gameState.HasPendingExpeditionDecision ||
                openedIncident != null ||
                openedDecision != null ||
                activeQueuedModal != null ||
                queuedModals.Count > 0);
    }

    private bool HasBlockingModalWork()
    {
        return HasBlockingModalWorkExceptCamp() || IsCampScreenOpen;
    }

    private void PauseForBlockingModal(bool autoPauseRequested = false)
    {
        if (gameState == null || isGameOver)
            return;

        bool wasPaused = ContinuousSimulationSystem.IsPaused(gameState);
        if (autoPauseRequested || !wasPaused)
            resumeAfterBlockingModal = true;

        ContinuousSimulationSystem.SetPaused(gameState, true);
    }

    private void ResumeAfterBlockingModalIfReady()
    {
        if (gameState == null || isGameOver)
            return;

        if (!resumeAfterBlockingModal || HasBlockingModalWork())
        {
            RefreshTimeControlAvailability();
            return;
        }

        resumeAfterBlockingModal = false;
        ContinuousSimulationSystem.SetPaused(gameState, false);
        RefreshContinuousClockOnly();
        RefreshTimeControlAvailability();
    }

    private void RefreshTimeControlAvailability()
    {
        if (timeToggleButton == null || gameState == null)
            return;

        bool blocked = HasBlockingModalWork();
        timeToggleButton.SetEnabled(!isGameOver && !blocked);
        timeToggleButton.tooltip = blocked
            ? IsNarrativeDialogueActive
                ? "Сначала завершите разговор"
                : "Сначала примите обязательное решение или закройте важное донесение"
            : ContinuousSimulationSystem.IsPaused(gameState)
                ? "Продолжить течение времени"
                : "Поставить время на паузу";
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

}

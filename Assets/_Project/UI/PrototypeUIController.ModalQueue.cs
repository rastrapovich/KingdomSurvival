using System.Collections.Generic;
using KingdomSurvival.Chapter01;
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

        // P10-LocInt: обычное прибытие в известную локацию больше не создаёт
        // decision-модал через LocationArrivalDecisionFactory/PendingDecision
        // — вместо него сразу открывается Location Interaction (тот же
        // блокирующий оверлей, что Narrative Dialogue). LocationArrivalDecisionFactory.cs
        // не удалён и не изменён: он остаётся для похожей, но другой
        // механики (находка локации прямо по дороге), которую эта правка
        // не трогает. Если открыть Location Interaction не удалось (гонка
        // с другим блокирующим окном), уведомление проваливается в обычную
        // очередь ниже — игрок всё равно узнает о прибытии и сможет войти
        // в локацию позже кнопкой "ВОЙТИ В ЛОКАЦИЮ".
        if (notice.Title == "АРМИЯ ПРИБЫЛА" || notice.Title == "ОТРЯД ПРИБЫЛ")
        {
            if (gameState != null &&
                gameState.HasActiveExpedition &&
                gameState.ActiveExpedition.Phase == CommanderState.AtLocation)
            {
                string arrivedLocationId = gameState.ActiveExpedition.LocationId;
                LocationData arrivedLocation = gameState.FindLocation(arrivedLocationId);
                if (arrivedLocation != null &&
                    !arrivedLocation.IsWaypoint &&
                    TryOpenLocationInteraction(arrivedLocationId))
                {
                    return;
                }
            }
        }

        // P10-LocInt: если сюжетное Location Research для этой локации уже
        // завершилось и Chapter01StoryDirector готов открыть N12, техническое
        // "ИССЛЕДОВАНИЕ ЗАВЕРШЕНО" не показываем — сюжетная сцена сама
        // является результатом исследования (раздел "Приоритет N12 над
        // техническим окном" инструкции). Для обычных локаций без сюжетного
        // продолжения GetPendingLocationNarrativeDialogueId вернёт null, и
        // уведомление показывается как раньше.
        if (notice.Title == "ИССЛЕДОВАНИЕ ЗАВЕРШЕНО" &&
            !string.IsNullOrEmpty(Chapter01StoryDirector.GetPendingLocationNarrativeDialogueId(gameState)))
        {
            return;
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

    // Fullscreen-экраны Journal/Hero/Camp останавливают стратегическое время
    // так же, как блокирующая модалка. Camp, в отличие от остальных, обязан
    // пропускать поверх себя D11C
    // ("Затем автоматически запускается D11C... поверх лагерного экрана") —
    // поэтому вынесено отдельным условием, а не в общий список ниже:
    // HasBlockingModalWork используется для паузы времени (включает Camp),
    // HasBlockingModalWorkExceptCamp — только в TryOpenNarrativeDialogueById,
    // чтобы диалог мог открыться, пока камп уже открыт.
    private bool HasBlockingModalWorkExceptCamp()
    {
        return gameState != null &&
               (IsNarrativeDialogueActive ||
                IsLocationInteractionActive ||
                IsJournalOpen ||
                IsHeroScreenOpen ||
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
                : IsLocationInteractionActive
                    ? "Сначала закройте окно локации"
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

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

public partial class PrototypeUIController
{
    private sealed class JourneySummaryEntry
    {
        public int Day;
        public string Title;
        public string Description;
        public string Result;
        public ExpeditionIncidentTone Tone;
    }

    private readonly List<JourneySummaryEntry> journeySummaryEntries =
        new List<JourneySummaryEntry>();
    private readonly HashSet<int> journeyProcessedIncidentIds =
        new HashSet<int>();

    private VisualElement journeySummaryBlock;
    private VisualElement journeySummaryList;
    private ScrollView journeySummaryScroll;
    private Label journeySummaryEmptyLabel;

    private GameState journeyTrackedGameState;
    private ExpeditionData journeyTrackedExpedition;
    private ExpeditionDecisionOccurrence journeyTrackedPendingDecision;
    private int journeyPendingReportStartIndex;
    private bool journeySummaryInitialized;

    private void InitializeJourneySummaryUi()
    {
        journeySummaryBlock =
            interfaceRoot.Q<VisualElement>("journey-summary-block");
        journeySummaryScroll =
            interfaceRoot.Q<ScrollView>("journey-summary-scroll");
        journeySummaryList =
            interfaceRoot.Q<VisualElement>("journey-summary-list");

        if (journeySummaryBlock == null ||
            journeySummaryScroll == null ||
            journeySummaryList == null)
        {
            Debug.LogError(
                "Journey summary: статический блок не найден в Prototype_Main.uxml.");
            return;
        }

        journeySummaryInitialized = true;
        journeyTrackedGameState = gameState;
        journeyTrackedExpedition =
            gameState != null ? gameState.ActiveExpedition : null;
        journeyTrackedPendingDecision = null;
        journeyPendingReportStartIndex = reportHistory.Count;
        journeyProcessedIncidentIds.Clear();
        journeySummaryEntries.Clear();

        RenderJourneySummary();
        RefreshJourneySummaryFromState();
    }

    private void RefreshJourneySummaryFromState()
    {
        if (!journeySummaryInitialized || gameState == null)
            return;

        TrackJourneyGameState();
        TrackJourneyExpedition();
        CaptureJourneyBackgroundIncidents();
        TrackResolvedJourneyDecision();
        RefreshJourneyEmptyState();
    }

    private void TrackJourneyGameState()
    {
        if (ReferenceEquals(journeyTrackedGameState, gameState))
            return;

        journeyTrackedGameState = gameState;
        journeyTrackedExpedition = gameState.ActiveExpedition;
        journeyTrackedPendingDecision = null;
        journeyPendingReportStartIndex = reportHistory.Count;
        journeyProcessedIncidentIds.Clear();
        journeySummaryEntries.Clear();
        RenderJourneySummary();
    }

    private void TrackJourneyExpedition()
    {
        ExpeditionData current = gameState.ActiveExpedition;

        if (ReferenceEquals(current, journeyTrackedExpedition))
            return;

        if (current == null)
        {
            journeyTrackedExpedition = null;
            journeyTrackedPendingDecision = null;
            journeyPendingReportStartIndex = reportHistory.Count;
            RefreshJourneyEmptyState();
            return;
        }

        // Новая экспедиция начинает новую локальную сводку.
        journeyTrackedExpedition = current;
        journeyTrackedPendingDecision = null;
        journeyPendingReportStartIndex = reportHistory.Count;
        journeyProcessedIncidentIds.Clear();
        journeySummaryEntries.Clear();
        RenderJourneySummary();
    }

    private void CaptureJourneyBackgroundIncidents()
    {
        if (unreadIncidents.Count == 0)
            return;

        List<ExpeditionIncidentOccurrence> captured =
            new List<ExpeditionIncidentOccurrence>();

        foreach (ExpeditionIncidentOccurrence occurrence in unreadIncidents)
        {
            if (!IsJourneyBackgroundIncident(occurrence))
                continue;

            if (!journeyProcessedIncidentIds.Add(occurrence.Id))
                continue;

            captured.Add(occurrence);

            AddJourneySummaryEntry(
                occurrence.Day,
                occurrence.Title,
                occurrence.Description,
                occurrence.ConsequenceText,
                occurrence.Tone);
        }

        if (captured.Count == 0)
            return;

        unreadIncidents.RemoveAll(IsJourneyBackgroundIncident);
        AppendJourneyArtTextToRoyalReport(captured);
    }

    private bool IsJourneyBackgroundIncident(
        ExpeditionIncidentOccurrence occurrence)
    {
        return occurrence != null && occurrence.Id > 0;
    }

    private void AppendJourneyArtTextToRoyalReport(
        List<ExpeditionIncidentOccurrence> occurrences)
    {
        if (occurrences == null || occurrences.Count == 0)
            return;

        Dictionary<int, List<ExpeditionIncidentOccurrence>> byDay =
            new Dictionary<int, List<ExpeditionIncidentOccurrence>>();

        foreach (ExpeditionIncidentOccurrence occurrence in occurrences)
        {
            List<ExpeditionIncidentOccurrence> dayEntries;

            if (!byDay.TryGetValue(occurrence.Day, out dayEntries))
            {
                dayEntries = new List<ExpeditionIncidentOccurrence>();
                byDay.Add(occurrence.Day, dayEntries);
            }

            dayEntries.Add(occurrence);
        }

        foreach (KeyValuePair<int, List<ExpeditionIncidentOccurrence>> pair in byDay)
        {
            int reportIndex = FindLatestReportIndexForDay(pair.Key);

            if (reportIndex < 0)
                continue;

            List<string> details = new List<string>();

            foreach (ExpeditionIncidentOccurrence occurrence in pair.Value)
            {
                details.Add(
                    occurrence.Title + "\n" +
                    occurrence.Description + "\n" +
                    "Результат: " + occurrence.ConsequenceText);
            }

            reportHistory[reportIndex] +=
                "\n\nПодробности походных происшествий:\n" +
                string.Join("\n\n", details);
        }

        ScheduleRoyalReportsRefresh();
    }

    private int FindLatestReportIndexForDay(int day)
    {
        string prefix = "День " + day + "\n";

        for (int i = reportHistory.Count - 1; i >= 0; i--)
        {
            if (reportHistory[i].StartsWith(prefix, StringComparison.Ordinal))
                return i;
        }

        return -1;
    }

    private void TrackResolvedJourneyDecision()
    {
        if (gameState.HasPendingExpeditionDecision)
        {
            ExpeditionDecisionOccurrence current =
                gameState.ActiveExpedition.PendingDecision;

            if (journeyTrackedPendingDecision == null ||
                journeyTrackedPendingDecision.Id != current.Id)
            {
                journeyTrackedPendingDecision = current;
                journeyPendingReportStartIndex = reportHistory.Count;
            }

            return;
        }

        if (journeyTrackedPendingDecision == null)
            return;

        string resultText =
            FindResolvedDecisionReport(
                journeyTrackedPendingDecision.Title,
                journeyPendingReportStartIndex);

        if (!string.IsNullOrWhiteSpace(resultText))
        {
            AddJourneySummaryEntry(
                journeyTrackedPendingDecision.Day,
                journeyTrackedPendingDecision.Title,
                journeyTrackedPendingDecision.Description,
                resultText,
                ExpeditionIncidentTone.Mixed);
        }

        journeyTrackedPendingDecision = null;
        journeyPendingReportStartIndex = reportHistory.Count;
    }

    private string FindResolvedDecisionReport(
        string title,
        int firstAllowedReportIndex)
    {
        string marker = "Приказ по событию «" + title + "»:";

        for (int i = reportHistory.Count - 1; i >= firstAllowedReportIndex; i--)
        {
            string report = reportHistory[i];
            int markerIndex =
                report.IndexOf(marker, StringComparison.Ordinal);

            if (markerIndex >= 0)
                return report.Substring(markerIndex).Trim();
        }

        return string.Empty;
    }

    private void AddJourneySummaryEntry(
        int day,
        string title,
        string description,
        string result,
        ExpeditionIncidentTone tone)
    {
        journeySummaryEntries.Insert(
            0,
            new JourneySummaryEntry
            {
                Day = day,
                Title = title,
                Description = description,
                Result = result,
                Tone = tone
            });

        RenderJourneySummary();
    }

    private void RenderJourneySummary()
    {
        if (journeySummaryList == null)
            return;

        journeySummaryList.Clear();

        if (journeySummaryEntries.Count == 0)
        {
            VisualTreeAsset hintTemplate = LoadJourneySummaryHintTemplate();
            if (hintTemplate == null)
                return;

            TemplateContainer hintInstance = hintTemplate.Instantiate();
            journeySummaryEmptyLabel = hintInstance.Q<Label>("hero-screen-hint");
            journeySummaryList.Add(hintInstance);
            RefreshJourneyEmptyState();
            return;
        }

        journeySummaryEmptyLabel = null;

        foreach (JourneySummaryEntry entry in journeySummaryEntries)
        {
            VisualElement view = CreateJourneySummaryEntryView(entry);
            if (view != null)
                journeySummaryList.Add(view);
        }
    }

    private VisualTreeAsset journeySummaryEntryTemplate;
    private VisualTreeAsset journeySummaryHintTemplate;

    // Динамическая карточка похода (число записей заранее не известно) —
    // клонируется из шаблона вместо new VisualElement/new Label
    // (ProjectDocs/UI_ARCHITECTURE.md, раздел 19).
    private VisualElement CreateJourneySummaryEntryView(JourneySummaryEntry entry)
    {
        VisualTreeAsset template = LoadJourneySummaryEntryTemplate();
        if (template == null)
            return null;

        TemplateContainer instance = template.Instantiate();

        VisualElement card = instance.Q<VisualElement>("journey-summary-entry");
        if (card != null)
            card.style.borderLeftColor = GetJourneyToneColor(entry.Tone);

        Label header = instance.Q<Label>("journey-summary-entry-header");
        if (header != null)
            header.text = "ДЕНЬ " + entry.Day + " · " + entry.Title.ToUpper();

        Label description = instance.Q<Label>("journey-summary-entry-description");
        if (description != null)
            description.text = entry.Description;

        Label result = instance.Q<Label>("journey-summary-entry-result");
        if (result != null)
        {
            result.text = "Результат: " + entry.Result;
            result.style.color = GetJourneyToneColor(entry.Tone);
        }

        return instance;
    }

    private VisualTreeAsset LoadJourneySummaryEntryTemplate()
    {
        if (journeySummaryEntryTemplate == null)
            journeySummaryEntryTemplate = Resources.Load<VisualTreeAsset>("Templates/JourneySummaryEntry");
        return journeySummaryEntryTemplate;
    }

    // Пустое состояние переиспользует общий шаблон подсказки Hero Screen
    // (тот же визуальный язык §hero-screen-hint) — отдельного шаблона не
    // заводим ради одной строки.
    private VisualTreeAsset LoadJourneySummaryHintTemplate()
    {
        if (journeySummaryHintTemplate == null)
            journeySummaryHintTemplate = Resources.Load<VisualTreeAsset>("Templates/HeroHint");
        return journeySummaryHintTemplate;
    }

    private Color GetJourneyToneColor(ExpeditionIncidentTone tone)
    {
        switch (tone)
        {
            case ExpeditionIncidentTone.Positive:
                return new Color(0.51f, 0.72f, 0.54f);
            case ExpeditionIncidentTone.Negative:
                return new Color(0.84f, 0.49f, 0.45f);
            default:
                return new Color(0.90f, 0.74f, 0.39f);
        }
    }

    private void RefreshJourneyEmptyState()
    {
        if (journeySummaryEmptyLabel == null || gameState == null)
            return;

        journeySummaryEmptyLabel.text = gameState.HasActiveExpedition
            ? "Новых происшествий пока нет."
            : "Армия находится в столице. Новых происшествий нет.";
    }

}

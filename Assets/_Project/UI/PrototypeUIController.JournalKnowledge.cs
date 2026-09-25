using System.Collections.Generic;
using KingdomSurvival.Chapter01;
using KingdomSurvival.DialogueDatabase;
using UnityEngine.UIElements;

// ПР-11 (ProjectDocs/PR11_CHRONICLE_SPEC.md): вкладки журнала «Сведения»
// и «История». Сведения — открытые знания главы: текст берётся из той
// реплики, которая дала знание (база диалогов), с источником и степенью
// уверенности. История — сохраняемые записи Chronicle. «НОВОЕ» снимается
// кликом и сохраняется (Chronicle.SeenIds).
public partial class PrototypeUIController
{
    private enum JournalTab
    {
        Goals,
        Knowledge,
        History
    }

    private JournalTab journalTab = JournalTab.Goals;
    private Button journalKnowledgeTabButton;
    private VisualElement journalEntriesSection;
    private Label journalEntriesTitle;
    private VisualElement journalEntriesList;
    private Label journalDetailStepCaption;
    private Button journalDetailMapButton;
    private string journalDetailLocationId;
    private string selectedJournalKnowledgeId;
    private string selectedJournalHistoryId;
    private Dictionary<string, DialogueKnowledgeSource> knowledgeSources;

    private bool BindJournalKnowledgeUi()
    {
        journalKnowledgeTabButton = BindRequiredElement<Button>(interfaceRoot, JournalScreenName, "journal-tab-knowledge");
        journalEntriesSection = BindRequiredElement<VisualElement>(interfaceRoot, JournalScreenName, "journal-entries-section");
        journalEntriesTitle = BindRequiredElement<Label>(interfaceRoot, JournalScreenName, "journal-entries-section-title");
        journalEntriesList = BindRequiredElement<VisualElement>(interfaceRoot, JournalScreenName, "journal-entries-list");
        journalDetailStepCaption = BindRequiredElement<Label>(interfaceRoot, JournalScreenName, "journal-detail-step-caption");
        journalDetailMapButton = BindRequiredElement<Button>(interfaceRoot, JournalScreenName, "journal-detail-map-button");

        bool ok = journalKnowledgeTabButton != null && journalEntriesSection != null && journalEntriesTitle != null &&
                  journalEntriesList != null && journalDetailStepCaption != null && journalDetailMapButton != null;
        if (!ok)
            return false;

        journalGoalsTabButton.clicked += () => SwitchJournalTab(JournalTab.Goals);
        journalKnowledgeTabButton.clicked += () => SwitchJournalTab(JournalTab.Knowledge);
        journalChronicleTabButton.clicked += () => SwitchJournalTab(JournalTab.History);
        journalDetailMapButton.clicked += OnJournalShowOnMap;
        return true;
    }

    private void SwitchJournalTab(JournalTab tab)
    {
        journalTab = tab;
        journalGoalsTabButton.EnableInClassList("nav-button-active", tab == JournalTab.Goals);
        journalKnowledgeTabButton.EnableInClassList("nav-button-active", tab == JournalTab.Knowledge);
        journalChronicleTabButton.EnableInClassList("nav-button-active", tab == JournalTab.History);
        RefreshJournal();
    }

    // Вкладки «Сведения» и «История»: общий список слева, подробности справа.
    private void RefreshJournalEntriesTab()
    {
        journalMainSection.style.display = DisplayStyle.None;
        journalOptionalSection.style.display = DisplayStyle.None;
        journalCompletedSection.style.display = DisplayStyle.None;
        journalEntriesSection.style.display = DisplayStyle.Flex;
        journalEntriesList.Clear();

        if (journalTab == JournalTab.Knowledge)
            RefreshKnowledgeTab();
        else
            RefreshHistoryTab();
    }

    private void RefreshKnowledgeTab()
    {
        journalEntriesTitle.text = "СВЕДЕНИЯ";
        List<KnowledgeEntry> known = Chapter01KnowledgeCatalog.Known(gameState.Narrative);
        KnowledgeEntry selected = null;
        foreach (KnowledgeEntry entry in known)
        {
            string seenId = "knowledge:" + entry.Id;
            bool isSelected = entry.Id == selectedJournalKnowledgeId;
            if (isSelected)
                selected = entry;
            KnowledgeEntry captured = entry;
            AddJournalEntryRow(entry.Title, Chapter01KnowledgeCatalog.CertaintyLabel(gameState.Narrative, entry),
                !Chronicle.IsSeen(gameState, seenId), isSelected, () =>
                {
                    selectedJournalKnowledgeId = captured.Id;
                    Chronicle.MarkSeen(gameState, seenId);
                    RefreshJournal();
                    RefreshJournalNotificationState();
                });
        }

        if (known.Count == 0)
            AddJournalEntryHint("Пока ничего не известно наверняка — даже слухов.");

        if (selected == null)
        {
            ShowJournalDetail("ВЫБЕРИТЕ СВЕДЕНИЕ", string.Empty, string.Empty, string.Empty, string.Empty, null);
            return;
        }

        DialogueKnowledgeSource source = FindKnowledgeSource(selected.Id);
        ShowJournalDetail("СВЕДЕНИЕ · " + Chapter01KnowledgeCatalog.CertaintyLabel(gameState.Narrative, selected).ToUpperInvariant(),
            selected.Title, source.Text, "ОТКУДА", source.Source, selected.LocationId);
    }

    private void RefreshHistoryTab()
    {
        journalEntriesTitle.text = "ИСТОРИЯ";
        List<ChronicleEntryData> entries = new List<ChronicleEntryData>(Chronicle.Get(gameState).Entries);
        entries.Reverse(); // свежие сверху
        ChronicleEntryData selected = null;
        foreach (ChronicleEntryData entry in entries)
        {
            bool isSelected = entry.Id == selectedJournalHistoryId;
            if (isSelected)
                selected = entry;
            ChronicleEntryData captured = entry;
            AddJournalEntryRow(entry.Title, Chronicle.FormatTime(entry), !Chronicle.IsSeen(gameState, entry.Id), isSelected, () =>
            {
                selectedJournalHistoryId = captured.Id;
                Chronicle.MarkSeen(gameState, captured.Id);
                RefreshJournal();
                RefreshJournalNotificationState();
            });
        }

        if (entries.Count == 0)
            AddJournalEntryHint("История только начинается.");

        if (selected == null)
        {
            ShowJournalDetail("ВЫБЕРИТЕ ЗАПИСЬ", string.Empty, string.Empty, string.Empty, string.Empty, null);
            return;
        }

        ChronicleEntryData cause = string.IsNullOrEmpty(selected.CauseId) ? null : Chronicle.Find(gameState, selected.CauseId);
        ShowJournalDetail("ИСТОРИЯ · " + Chronicle.FormatTime(selected).ToUpperInvariant(), selected.Title, selected.Text,
            cause != null ? "ПОТОМУ ЧТО" : string.Empty,
            cause != null ? Chronicle.FormatTime(cause) + " — " + cause.Title + ": " + cause.Text : string.Empty,
            selected.LocationId);
    }

    private void AddJournalEntryRow(string title, string subtitle, bool unseen, bool selected, System.Action onClick)
    {
        VisualTreeAsset template = LoadJournalGoalRowTemplate();
        if (template == null)
            return;

        TemplateContainer instance = template.Instantiate();
        VisualElement row = instance.Q<VisualElement>("journal-goal-row");
        row?.EnableInClassList("journal-goal-row-selected", selected);
        Label titleLabel = instance.Q<Label>("journal-goal-row-title");
        if (titleLabel != null)
            titleLabel.text = (selected ? "▶ " : "") + title;
        Label badge = instance.Q<Label>("journal-goal-row-badge");
        if (badge != null)
            badge.style.display = unseen ? DisplayStyle.Flex : DisplayStyle.None;
        Label subtitleLabel = instance.Q<Label>("journal-goal-row-subtitle");
        if (subtitleLabel != null)
            subtitleLabel.text = subtitle;

        instance.RegisterCallback<PointerDownEvent>(evt =>
        {
            if (evt.button != 0)
                return;
            onClick();
            evt.StopPropagation();
        });
        journalEntriesList.Add(instance);
    }

    private void AddJournalEntryHint(string text)
    {
        Label hint = new Label(text);
        hint.AddToClassList("ks-muted-text");
        journalEntriesList.Add(hint);
    }

    private void ShowJournalDetail(string category, string title, string description, string caption, string step, string locationId)
    {
        journalDetailCategory.text = category;
        journalDetailTitle.text = title;
        journalDetailDescription.text = description;
        journalDetailStepCaption.text = caption;
        journalDetailStepCaption.style.display = string.IsNullOrEmpty(caption) ? DisplayStyle.None : DisplayStyle.Flex;
        journalDetailCurrentStep.text = step;

        // «Показать на карте» — только если место действительно известно.
        LocationData location = string.IsNullOrEmpty(locationId) ? null : gameState.FindLocation(locationId);
        bool canShow = location != null && location.IsVisibleOnMap;
        journalDetailLocationId = canShow ? locationId : null;
        journalDetailMapButton.style.display = canShow ? DisplayStyle.Flex : DisplayStyle.None;
    }

    private void OnJournalShowOnMap()
    {
        LocationData location = string.IsNullOrEmpty(journalDetailLocationId) ? null : gameState?.FindLocation(journalDetailLocationId);
        if (location == null)
            return;
        CloseJournal();
        OnExpeditionsNavigationClicked();
        float x = location.MapXPercent;
        float y = location.MapYPercent;
        interfaceRoot.schedule.Execute(() => CenterWorldMapOn(x, y)).ExecuteLater(1);
    }

    // Текст сведения — реплика, которая его дала; источник — сцена и говорящий.
    private DialogueKnowledgeSource FindKnowledgeSource(string knowledgeId)
    {
        if (knowledgeSources == null)
            knowledgeSources = DialogueKnowledgeSources.Build(narrativeDialogueDatabase ?? DialogueDatabaseRuntime.LoadDefaultDatabase());
        return knowledgeSources.TryGetValue(knowledgeId, out DialogueKnowledgeSource source)
            ? source
            : new DialogueKnowledgeSource { KnowledgeId = knowledgeId, Text = "Узнано по ходу главы.", Source = "из пережитого в походе" };
    }

    // «Журнал •» — есть непрочитанные сведения или записи истории.
    private bool HasUnseenKnowledgeOrHistory()
    {
        if (gameState == null)
            return false;
        foreach (KnowledgeEntry entry in Chapter01KnowledgeCatalog.Known(gameState.Narrative))
        {
            if (!Chronicle.IsSeen(gameState, "knowledge:" + entry.Id))
                return true;
        }
        foreach (ChronicleEntryData entry in Chronicle.Get(gameState).Entries)
        {
            if (!Chronicle.IsSeen(gameState, entry.Id))
                return true;
        }
        return false;
    }
}

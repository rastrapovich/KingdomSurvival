using System;
using System.Collections;
using System.Collections.Generic;
using KingdomSurvival.Chapter01;
using KingdomSurvival.DialogueDatabase;
using KingdomSurvival.UILayout;
using UnityEngine;
using UnityEngine.UIElements;

public partial class PrototypeUIController
{
    private enum NarrativeUiHistoryItemKind
    {
        PlayerChoice,
        ResponseGroup
    }

    // Один элемент UI-истории. "Новый текст всегда появляется цельным"
    // (дополнение к инструкции presentation пассивных проверок): одна
    // ResponseGroup соответствует ровно одному вызову DisplayNarrativeView()
    // — то есть одному NarrativeDialogueView, целиком раскрывшемуся одним
    // действием игрока. Несколько TextBlock/наблюдение/проверка внутри
    // одной группы никогда не становятся отдельными "сообщениями" — сама
    // сессия v2 истории не хранит (§12 инструкции по визуализации
    // проверок), это чисто presentation-состояние UI.
    private sealed class NarrativeUiHistoryItem
    {
        public NarrativeUiHistoryItemKind Kind;

        // Только для PlayerChoice.
        public string PlayerChoiceText;

        // Только для ResponseGroup — тот же набор данных, что вернула
        // NarrativeDialogueRuntimeSession для одного view; сегменты строятся
        // из них по требованию через NarrativeUiHistoryGrouping.BuildSegments,
        // а не хранятся здесь заранее готовыми.
        public NarrativeCheckPresentationData LeadingActiveCheck;
        public IReadOnlyList<NarrativeDialogueVisibleBlock> Blocks;

        public static NarrativeUiHistoryItem ForPlayerChoice(string text)
        {
            return new NarrativeUiHistoryItem { Kind = NarrativeUiHistoryItemKind.PlayerChoice, PlayerChoiceText = text };
        }

        public static NarrativeUiHistoryItem ForResponseGroup(
            NarrativeCheckPresentationData leadingActiveCheck,
            IReadOnlyList<NarrativeDialogueVisibleBlock> blocks)
        {
            return new NarrativeUiHistoryItem
            {
                Kind = NarrativeUiHistoryItemKind.ResponseGroup,
                LeadingActiveCheck = leadingActiveCheck,
                Blocks = blocks
            };
        }
    }

    private NarrativeDialogueRuntimeSession narrativeDialogueSession;
    private DialogueDatabaseAsset narrativeDialogueDatabase;
    private readonly List<NarrativeUiHistoryItem> narrativeHistory = new List<NarrativeUiHistoryItem>();
    private VisualElement narrativeDialogueOverlay;
    private VisualElement narrativePortrait;
    private VisualElement narrativePanel;
    private VisualElement narrativeDivider;
    private Label narrativePortraitPlaceholder;
    private Label narrativeSpeakerLabel;
    private Label narrativeRoleLabel;
    private ScrollView narrativeTextScroll;
    private VisualElement narrativeHistoryContainer;
    private VisualElement narrativeChoicesContainer;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    private DropdownField narrativeDebugDialogueDropdown;
    private Label narrativeDebugDialogueInfo;
    private Button narrativeDebugOpenButton;
    private readonly List<string> narrativeDebugDialogueIds = new List<string>();
#endif

    private bool IsNarrativeDialogueActive => narrativeDialogueSession != null && narrativeDialogueSession.IsActive;

    private void Awake()
    {
        narrativeDialogueSession = new NarrativeDialogueRuntimeSession();
        narrativeDialogueDatabase = DialogueDatabaseRuntime.LoadDefaultDatabase();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        StartCoroutine(InstallNarrativeDebugTrigger());
#endif
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    private IEnumerator InstallNarrativeDebugTrigger()
    {
        yield return null;
        if (!DebugToolsAvailable || debugPanel == null)
            yield break;
        ScrollView scroll = debugPanel.Q<ScrollView>(className: "debug-scroll");
        if (scroll == null)
            yield break;

        AddDebugSectionTitle(scroll, "НАРРАТИВ");

        narrativeDebugDialogueDropdown = new DropdownField("Диалог");
        narrativeDebugDialogueDropdown.AddToClassList("debug-dialogue-dropdown");
        narrativeDebugDialogueDropdown.RegisterValueChangedCallback(_ => RefreshNarrativeDebugDialogueInfo());
        scroll.Add(narrativeDebugDialogueDropdown);

        narrativeDebugDialogueInfo = new Label();
        narrativeDebugDialogueInfo.AddToClassList("debug-state-label");
        scroll.Add(narrativeDebugDialogueInfo);

        narrativeDebugOpenButton = CreateDebugActionButton(
            "ЗАПУСТИТЬ ДИАЛОГ",
            DebugOpenSelectedNarrativeDialogue);
        scroll.Add(narrativeDebugOpenButton);

        scroll.Add(CreateDebugActionButton(
            "ОБНОВИТЬ СПИСОК ДИАЛОГОВ",
            RefreshNarrativeDebugDialogues));

        RefreshNarrativeDebugDialogues();
    }

    private void RefreshNarrativeDebugDialogues()
    {
        narrativeDialogueDatabase = DialogueDatabaseRuntime.LoadDefaultDatabase();
        narrativeDebugDialogueIds.Clear();
        List<string> labels = new List<string>();

        if (narrativeDialogueDatabase != null)
        {
            for (int i = 0; i < narrativeDialogueDatabase.Dialogues.Count; i++)
            {
                DialogueDefinitionData dialogue = narrativeDialogueDatabase.Dialogues[i];
                if (dialogue == null)
                    continue;

                narrativeDebugDialogueIds.Add(dialogue.Id);
                labels.Add(dialogue.Title + "  [" + dialogue.Id + "]");
            }
        }

        if (narrativeDebugDialogueDropdown != null)
        {
            narrativeDebugDialogueDropdown.choices = labels;
            if (labels.Count > 0)
                narrativeDebugDialogueDropdown.value = labels[0];
            else
                narrativeDebugDialogueDropdown.value = string.Empty;
        }

        if (narrativeDebugOpenButton != null)
            narrativeDebugOpenButton.SetEnabled(labels.Count > 0);

        RefreshNarrativeDebugDialogueInfo();
    }

    private void RefreshNarrativeDebugDialogueInfo()
    {
        if (narrativeDebugDialogueInfo == null)
            return;

        DialogueDefinitionData dialogue = GetSelectedDebugDialogue();
        if (dialogue == null)
        {
            narrativeDebugDialogueInfo.text = "Диалоги в базе не найдены.";
            return;
        }

        narrativeDebugDialogueInfo.text =
            "ID: " + dialogue.Id + "\n" +
            "Категория: " + dialogue.Category + "\n" +
            "Статус: " + dialogue.Status;
    }

    private DialogueDefinitionData GetSelectedDebugDialogue()
    {
        if (narrativeDialogueDatabase == null ||
            narrativeDebugDialogueDropdown == null ||
            narrativeDebugDialogueDropdown.choices == null)
        {
            return null;
        }

        int index = narrativeDebugDialogueDropdown.choices.IndexOf(narrativeDebugDialogueDropdown.value);
        if (index < 0 || index >= narrativeDebugDialogueIds.Count)
            return null;

        return narrativeDialogueDatabase.FindDialogue(narrativeDebugDialogueIds[index]);
    }

    private void DebugOpenSelectedNarrativeDialogue()
    {
        DialogueDefinitionData dialogue = GetSelectedDebugDialogue();
        if (dialogue == null)
        {
            AddReport("[DEBUG] Не выбран диалог для запуска.");
            return;
        }

        if (debugPanel != null)
            debugPanel.style.display = DisplayStyle.None;

        if (!TryOpenNarrativeDialogueById(dialogue.Id))
            AddReport("[DEBUG] Диалог '" + dialogue.Id + "' сейчас нельзя открыть.");
    }
#endif

    // Единственная точка входа игрового кода в диалог. Активные проверки
    // разрешаются через общий NarrativeCheckResolver (§7); принудительного
    // исхода здесь нет и быть не может — это привилегия только Preview
    // в редакторе (§13/§20).
    public bool TryOpenNarrativeDialogueById(string dialogueId)
    {
        if (narrativeDialogueDatabase == null)
            narrativeDialogueDatabase = DialogueDatabaseRuntime.LoadDefaultDatabase();
        if (narrativeDialogueDatabase == null)
        {
            Debug.LogError("Narrative UI: не найдена база диалогов Resources/" + DialogueDatabaseAsset.ResourcesPath + ".asset");
            return false;
        }

        if (gameState == null || isGameOver || IsNarrativeDialogueActive || HasBlockingModalWork())
            return false;

        CommanderData commander = gameState.GetSelectedCommander();
        if (commander == null)
            return false;
        if (commander.HeroProfile == null)
            commander.HeroProfile = new HeroProfileData();
        if (gameState.Narrative == null)
            gameState.Narrative = new NarrativeStateData();

        List<string> presentCompanionIds = new List<string>();
        if (gameState.HasActiveExpedition)
            presentCompanionIds.AddRange(gameState.ActiveExpedition.FighterIds);

        List<string> presentItemIds = new List<string>();
        if (gameState.Narrative.Items != null)
            presentItemIds.AddRange(gameState.Narrative.Items);

        bool started = narrativeDialogueSession.Start(
            narrativeDialogueDatabase,
            dialogueId,
            commander.HeroProfile,
            gameState.Narrative,
            out NarrativeDialogueView view,
            out string error,
            presentCompanionIds,
            presentItemIds,
            gameState.WorldSeed);

        if (!started)
        {
            Debug.LogError("Narrative UI: не удалось открыть диалог '" + dialogueId + "'.\n" + error);
            return false;
        }

        if (!EnsureNarrativeDialogueUi())
            return false;

        narrativeHistory.Clear();
        PauseForBlockingModal();
        narrativeDialogueOverlay.style.display = DisplayStyle.Flex;
        DisplayNarrativeView(view, null);
        if (timeToggleButton != null)
        {
            timeToggleButton.SetEnabled(false);
            timeToggleButton.tooltip = "Сначала завершите разговор";
        }
        return true;
    }

    private bool EnsureNarrativeDialogueUi()
    {
        if (narrativeDialogueOverlay != null && narrativeDialogueOverlay.panel != null)
            return true;
        if (interfaceRoot == null)
            return false;
        VisualElement screen = interfaceRoot.Q<VisualElement>("screen");
        if (screen == null)
            return false;
        StyleSheet narrativeStyle = Resources.Load<StyleSheet>("Prototype_Narrative");
        if (narrativeStyle == null)
        {
            Debug.LogError("Narrative UI: не найден Resources/Prototype_Narrative.uss.");
            return false;
        }
        interfaceRoot.styleSheets.Add(narrativeStyle);

        narrativeDialogueOverlay = new VisualElement { name = "narrative-dialogue-overlay", pickingMode = PickingMode.Position };
        narrativeDialogueOverlay.AddToClassList("narrative-dialogue-overlay");

        narrativePortrait = new VisualElement { name = "narrative-dialogue-portrait" };
        narrativePortrait.AddToClassList("narrative-dialogue-portrait");
        narrativePortraitPlaceholder = new Label("ПОРТРЕТ\nПЕРСОНАЖА");
        narrativePortraitPlaceholder.AddToClassList("narrative-dialogue-portrait-placeholder");
        narrativePortrait.Add(narrativePortraitPlaceholder);

        narrativePanel = new VisualElement { name = "narrative-dialogue-panel" };
        narrativePanel.AddToClassList("narrative-dialogue-panel");

        narrativeSpeakerLabel = new Label { name = "narrative-dialogue-speaker" };
        narrativeSpeakerLabel.AddToClassList("narrative-dialogue-speaker");
        narrativePanel.Add(narrativeSpeakerLabel);

        narrativeRoleLabel = new Label { name = "narrative-dialogue-role" };
        narrativeRoleLabel.AddToClassList("narrative-dialogue-role");
        narrativePanel.Add(narrativeRoleLabel);

        narrativeDivider = new VisualElement();
        narrativeDivider.AddToClassList("narrative-dialogue-divider");
        narrativePanel.Add(narrativeDivider);

        narrativeTextScroll = new ScrollView(ScrollViewMode.Vertical) { name = "narrative-dialogue-text" };
        narrativeTextScroll.AddToClassList("narrative-dialogue-text-scroll");
        narrativeHistoryContainer = new VisualElement();
        narrativeHistoryContainer.AddToClassList("narrative-dialogue-history");
        narrativeTextScroll.Add(narrativeHistoryContainer);
        narrativePanel.Add(narrativeTextScroll);

        narrativeChoicesContainer = new VisualElement { name = "narrative-dialogue-choices" };
        narrativeChoicesContainer.AddToClassList("narrative-dialogue-choices");
        narrativePanel.Add(narrativeChoicesContainer);

        narrativeDialogueOverlay.Add(narrativePanel);
        narrativeDialogueOverlay.Add(narrativePortrait);
        screen.Add(narrativeDialogueOverlay);
        ApplyNarrativeLayout(screen);
        narrativeDialogueOverlay.style.display = DisplayStyle.None;
        return true;
    }

    private void ApplyNarrativeLayout(VisualElement screen)
    {
        UILayoutDatabaseAsset database = UILayoutRuntimeApplier.LoadDefaultDatabase();
        UILayoutScreenDefinition layout = database != null ? database.FindScreen("narrative-dialogue") : null;
        if (layout == null)
            return;

        ReparentNarrativeElement(narrativeSpeakerLabel, layout.FindElement("speaker"));
        ReparentNarrativeElement(narrativeRoleLabel, layout.FindElement("role"));
        ReparentNarrativeElement(narrativeTextScroll, layout.FindElement("text"));
        ReparentNarrativeElement(narrativeChoicesContainer, layout.FindElement("choices"));

        narrativePanel.style.paddingLeft = 0f;
        narrativePanel.style.paddingRight = 0f;
        narrativePanel.style.paddingTop = 0f;
        narrativePanel.style.paddingBottom = 0f;
        if (narrativeDivider != null)
            narrativeDivider.style.display = DisplayStyle.None;

        float actualWidth = screen.resolvedStyle.width;
        float actualHeight = screen.resolvedStyle.height;
        if (actualWidth <= 0f || actualHeight <= 0f)
        {
            actualWidth = database.ReferenceResolution.x;
            actualHeight = database.ReferenceResolution.y;
        }

        Vector2 reference = database.ReferenceResolution;
        Vector2 actual = new Vector2(actualWidth, actualHeight);
        ApplyLayoutElement(narrativeDialogueOverlay, layout.FindElement("overlay"), layout, reference, actual);
        ApplyLayoutElement(narrativePanel, layout.FindElement("panel"), layout, reference, actual);
        ApplyLayoutElement(narrativePortrait, layout.FindElement("portrait"), layout, reference, actual);
        ApplyLayoutElement(narrativeSpeakerLabel, layout.FindElement("speaker"), layout, reference, actual);
        ApplyLayoutElement(narrativeRoleLabel, layout.FindElement("role"), layout, reference, actual);
        ApplyLayoutElement(narrativeTextScroll, layout.FindElement("text"), layout, reference, actual);
        ApplyLayoutElement(narrativeChoicesContainer, layout.FindElement("choices"), layout, reference, actual);
    }

    private void ReparentNarrativeElement(
        VisualElement target,
        UILayoutElementDefinition definition)
    {
        if (target == null || definition == null || narrativeDialogueOverlay == null || narrativePanel == null)
            return;

        VisualElement desiredParent = definition.ParentId == "panel"
            ? narrativePanel
            : narrativeDialogueOverlay;
        if (target.parent == desiredParent)
            return;

        target.RemoveFromHierarchy();
        desiredParent.Add(target);
    }

    private static void ApplyLayoutElement(
        VisualElement target,
        UILayoutElementDefinition definition,
        UILayoutScreenDefinition screen,
        Vector2 reference,
        Vector2 actual)
    {
        if (target == null || definition == null)
            return;
        UILayoutRuntimeApplier.ApplyRect(target, definition, screen, reference, actual);
        UILayoutRuntimeApplier.ApplyBackground(target, definition, reference, actual);
    }

    // Единая точка показа нового представления узла. Одно раскрытие view
    // (причинный результат активной проверки + все видимые блоки) — одна
    // неделимая группа истории (дополнение к инструкции "новое отображение
    // пассивных наблюдений и проверок" — "новый текст всегда появляется
    // цельным"). Порядок остаётся прежним: сначала причинный текст, затем
    // механика (§14).
    private void DisplayNarrativeView(NarrativeDialogueView view, NarrativeCheckPresentationData checkPresentation)
    {
        narrativeHistory.Add(NarrativeUiHistoryItem.ForResponseGroup(checkPresentation, view.VisibleTextBlocks));

        NarrativeDialogueVisibleBlock latestBlock = view.VisibleTextBlocks.Count > 0
            ? view.VisibleTextBlocks[view.VisibleTextBlocks.Count - 1]
            : null;
        narrativeSpeakerLabel.text = latestBlock != null ? latestBlock.SpeakerDisplayName : string.Empty;
        narrativeRoleLabel.text = latestBlock != null ? latestBlock.SpeakerRole : string.Empty;
        ApplyNarrativeSpeakerPortrait(latestBlock != null ? latestBlock.SpeakerId : string.Empty);

        RenderNarrativeDialogueHistory();
        RenderNarrativeDialogueChoices(view);
    }

    private void RenderNarrativeDialogueChoices(NarrativeDialogueView view)
    {
        narrativeChoicesContainer.Clear();

        for (int i = 0; i < view.AvailableChoices.Count; i++)
        {
            NarrativeDialogueChoiceView choiceView = view.AvailableChoices[i];
            string choiceId = choiceView.ChoiceId;
            string choiceText = choiceView.Text;

            Button button = new Button(() => OnNarrativeDialogueChoiceSelected(choiceId, choiceText)) { text = choiceText };
            button.AddToClassList("narrative-dialogue-choice");
            if (choiceView.Kind == DialogueChoiceKind.Exit)
                button.AddToClassList("narrative-dialogue-choice-exit");
            narrativeChoicesContainer.Add(button);

            if (!string.IsNullOrWhiteSpace(choiceView.MechanicalSummary))
            {
                Label mechanic = new Label(choiceView.MechanicalSummary);
                mechanic.AddToClassList("narrative-dialogue-choice-mechanic");
                narrativeChoicesContainer.Add(mechanic);
            }
        }

        for (int i = 0; i < view.DisabledChoices.Count; i++)
        {
            NarrativeDialogueChoiceView disabledView = view.DisabledChoices[i];

            Button button = new Button { text = disabledView.Text };
            button.AddToClassList("narrative-dialogue-choice");
            button.AddToClassList("narrative-dialogue-choice-disabled");
            button.SetEnabled(false);
            narrativeChoicesContainer.Add(button);

            if (!string.IsNullOrWhiteSpace(disabledView.DisabledHint))
            {
                Label hint = new Label(disabledView.DisabledHint);
                hint.AddToClassList("narrative-dialogue-choice-hint");
                narrativeChoicesContainer.Add(hint);
            }
        }
    }

    // Один top-level child на элемент истории: PlayerChoice — одна строка,
    // ResponseGroup — один контейнер на всю группу. Это принципиально для
    // дополнения к инструкции presentation пассивных проверок ("новый текст
    // всегда появляется цельным") — PrototypeUIController.NarrativePresentation.cs
    // гасит "прочитанные" записи и вычисляет scroll именно по top-level
    // детям narrativeHistoryContainer, поэтому одна группа не может
    // оказаться наполовину "историей", наполовину "текущей" (§2/§10), а
    // scroll всегда целится в начало последней группы, а не в последний
    // абзац внутри нее (§3/§4). Сам scroll здесь не планируется — им
    // занимается NarrativePresentation.cs по изменению narrativeHistory.Count.
    private void RenderNarrativeDialogueHistory()
    {
        if (narrativeHistoryContainer == null)
            return;

        narrativeHistoryContainer.Clear();
        for (int i = 0; i < narrativeHistory.Count; i++)
        {
            NarrativeUiHistoryItem item = narrativeHistory[i];

            if (item.Kind == NarrativeUiHistoryItemKind.PlayerChoice)
            {
                Label playerLine = new Label("Вы: " + item.PlayerChoiceText);
                playerLine.AddToClassList("narrative-dialogue-history-player");
                narrativeHistoryContainer.Add(playerLine);
                continue;
            }

            narrativeHistoryContainer.Add(BuildNarrativeHistoryGroupElement(item));
        }
    }

    // Строит один визуальный контейнер для целой ResponseGroup — все
    // сегменты одного view (§1/§7 дополнения к инструкции). Сегменты
    // считает NarrativeUiHistoryGrouping (чистая, тестируемая логика, не
    // зависящая от UI Toolkit); здесь только раскладка по VisualElement.
    private VisualElement BuildNarrativeHistoryGroupElement(NarrativeUiHistoryItem item)
    {
        VisualElement group = new VisualElement();
        group.AddToClassList("narrative-dialogue-history-group");

        List<NarrativeUiHistorySegment> segments =
            NarrativeUiHistoryGrouping.BuildSegments(item.LeadingActiveCheck, item.Blocks);

        for (int i = 0; i < segments.Count; i++)
        {
            NarrativeUiHistorySegment segment = segments[i];

            if (segment.Kind == NarrativeUiSegmentKind.CheckResult)
            {
                VisualElement checkLine = new VisualElement();
                checkLine.AddToClassList("narrative-dialogue-history-check-result");
                checkLine.Add(BuildNarrativeCheckHeaderElement(segment.CheckPresentation));
                group.Add(checkLine);
                continue;
            }

            // Checked-observation (§3 инструкции "новое отображение
            // пассивных наблюдений и проверок"): одна инлайн-строка
            // "ИСТОЧНИК: РЕЗУЛЬТАТ — текст" без подписи говорящего — это
            // наблюдение героя, а не реплика NPC рядом с которым оно возникло.
            if (segment.Kind == NarrativeUiSegmentKind.CheckedObservation)
            {
                VisualElement observationBlock = new VisualElement();
                observationBlock.AddToClassList("narrative-dialogue-history-entry");
                string revealedText = segment.Paragraphs.Count > 0 ? segment.Paragraphs[0] : string.Empty;
                observationBlock.Add(BuildNarrativePassiveCheckLine(segment.CheckPresentation, revealedText));
                group.Add(observationBlock);
                continue;
            }

            // Обычная реплика (MainLine/CompanionLine) — подпись говорящего
            // один раз, затем все слитые подряд абзацы (§5/§6 дополнения).
            VisualElement block = new VisualElement();
            block.AddToClassList("narrative-dialogue-history-entry");

            if (segment.CheckPresentation != null)
                block.Add(BuildNarrativeCheckHeaderElement(segment.CheckPresentation));

            Label speaker = new Label(segment.SpeakerDisplayName);
            speaker.AddToClassList("narrative-dialogue-history-speaker");
            block.Add(speaker);

            for (int p = 0; p < segment.Paragraphs.Count; p++)
            {
                Label text = new Label(segment.Paragraphs[p]);
                text.AddToClassList("narrative-dialogue-history-text");
                block.Add(text);
            }

            group.Add(block);
        }

        return group;
    }

    private void ApplyNarrativeSpeakerPortrait(string speakerId)
    {
        if (narrativePortrait == null)
            return;

        DialogueSpeakerData speaker = narrativeDialogueDatabase != null
            ? narrativeDialogueDatabase.FindSpeaker(speakerId)
            : null;
        Sprite portrait = speaker != null ? speaker.Portrait : null;
        if (portrait == null)
        {
            UILayoutRuntimeApplier.ClearDynamicImage(narrativePortrait);
            if (narrativePortraitPlaceholder != null)
                narrativePortraitPlaceholder.style.display = DisplayStyle.Flex;
            return;
        }

        UILayoutDatabaseAsset layoutDatabase = UILayoutRuntimeApplier.LoadDefaultDatabase();
        UILayoutScreenDefinition dialogueLayout = layoutDatabase != null
            ? layoutDatabase.FindScreen(UILayoutDatabaseAsset.NarrativeDialogueScreenId)
            : null;
        UILayoutElementDefinition portraitDefinition = dialogueLayout != null
            ? dialogueLayout.FindElement("portrait")
            : null;

        Vector2 reference = layoutDatabase != null
            ? (Vector2)layoutDatabase.ReferenceResolution
            : new Vector2(1920f, 1080f);
        Vector2 actual = reference;
        VisualElement screen = interfaceRoot != null
            ? interfaceRoot.Q<VisualElement>("screen")
            : null;
        if (screen != null && screen.resolvedStyle.width > 0f && screen.resolvedStyle.height > 0f)
            actual = new Vector2(screen.resolvedStyle.width, screen.resolvedStyle.height);

        bool useIndividualFraming = speaker != null && speaker.OverridePortraitFraming;
        float speakerScale = useIndividualFraming ? speaker.PortraitScale : 1f;
        Vector2 speakerOffset = useIndividualFraming ? speaker.PortraitOffsetNormalized : Vector2.zero;
        bool flipX = useIndividualFraming && speaker.PortraitFlipX;

        UILayoutRuntimeApplier.ApplyDynamicImage(
            narrativePortrait,
            portrait,
            portraitDefinition,
            reference,
            actual,
            speakerScale,
            speakerOffset,
            flipX);

        if (narrativePortraitPlaceholder != null)
            narrativePortraitPlaceholder.style.display = DisplayStyle.None;
    }

    private void OnNarrativeDialogueChoiceSelected(string choiceId, string choiceText)
    {
        if (!IsNarrativeDialogueActive)
            return;

        narrativeHistory.Add(NarrativeUiHistoryItem.ForPlayerChoice(choiceText));

        NarrativeDialogueSelectionResult result;
        try
        {
            result = narrativeDialogueSession.SelectChoice(choiceId);
        }
        catch (InvalidOperationException exception)
        {
            Debug.LogError("Narrative UI: не удалось выбрать ответ '" + choiceId + "'.\n" + exception.Message);
            return;
        }

        if (result.DialogueEnded)
        {
            string completedDialogueId = narrativeDialogueSession.DialogueId;
            Chapter01StoryDirector.HandleDialogueCompleted(gameState, completedDialogueId);
            CloseNarrativeDialogue();
            return;
        }

        DisplayNarrativeView(result.View, result.CheckPresentation);
    }

    private void CloseNarrativeDialogue()
    {
        if (narrativeDialogueSession != null && narrativeDialogueSession.IsActive)
            narrativeDialogueSession.End();
        HideNarrativeCheckTooltip();
        narrativeHistory.Clear();
        if (narrativeDialogueOverlay != null)
            narrativeDialogueOverlay.style.display = DisplayStyle.None;
        if (narrativeChoicesContainer != null)
            narrativeChoicesContainer.Clear();
        if (narrativeHistoryContainer != null)
            narrativeHistoryContainer.Clear();
        RefreshTimeControlAvailability();
        ResumeAfterBlockingModalIfReady();
    }
}

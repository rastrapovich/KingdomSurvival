using System;
using System.Collections;
using System.Collections.Generic;
using KingdomSurvival.DialogueDatabase;
using KingdomSurvival.UILayout;
using UnityEngine;
using UnityEngine.UIElements;

public partial class PrototypeUIController
{
    private enum NarrativeUiHistoryKind
    {
        Block,
        PlayerChoice,
        CheckResult
    }

    // Строится самим UI поверх NarrativeDialogueView — сама сессия v2
    // истории не хранит (§12). Не влияет на игровое состояние.
    private sealed class NarrativeUiHistoryEntry
    {
        public NarrativeUiHistoryKind Kind;
        public string SpeakerDisplayName;
        public string SpeakerRole;
        public string Text;

        // Заполнено для Block (пассивная проверка блока) и для CheckResult
        // (активная проверка) — единый Narrative Check Presentation Layer,
        // см. PrototypeUIController.NarrativeCheckPresentation.cs.
        public NarrativeCheckPresentationData CheckPresentation;

        // Тип исходного текстового блока (§13 инструкции "новое отображение
        // пассивных наблюдений и проверок") — history renderer различает
        // checked-observation (Observation/Memory/HeroThought/Narration с
        // CheckPresentation) от настоящих реплик (MainLine/CompanionLine) по
        // этому полю, а не по имени говорящего или наличию текста.
        public DialogueTextBlockKind BlockKind;

        public static NarrativeUiHistoryEntry ForBlock(
            string speakerDisplayName,
            string speakerRole,
            string text,
            NarrativeCheckPresentationData checkPresentation,
            DialogueTextBlockKind blockKind)
        {
            return new NarrativeUiHistoryEntry
            {
                Kind = NarrativeUiHistoryKind.Block,
                SpeakerDisplayName = speakerDisplayName,
                SpeakerRole = speakerRole,
                Text = text,
                CheckPresentation = checkPresentation,
                BlockKind = blockKind
            };
        }

        public static NarrativeUiHistoryEntry ForPlayerChoice(string text)
        {
            return new NarrativeUiHistoryEntry { Kind = NarrativeUiHistoryKind.PlayerChoice, Text = text };
        }

        public static NarrativeUiHistoryEntry ForCheckResult(NarrativeCheckPresentationData checkPresentation)
        {
            return new NarrativeUiHistoryEntry
            {
                Kind = NarrativeUiHistoryKind.CheckResult,
                CheckPresentation = checkPresentation
            };
        }
    }

    private NarrativeDialogueRuntimeSession narrativeDialogueSession;
    private DialogueDatabaseAsset narrativeDialogueDatabase;
    private readonly List<NarrativeUiHistoryEntry> narrativeHistory = new List<NarrativeUiHistoryEntry>();
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
        UILayoutRuntimeApplier.ApplyBackground(target, definition);
    }

    // Единая точка показа нового представления узла: сначала причинный
    // результат проверки (если был), затем видимые блоки, затем варианты
    // ответа — порядок из §14 ("сначала причинный текст, затем механика").
    private void DisplayNarrativeView(NarrativeDialogueView view, NarrativeCheckPresentationData checkPresentation)
    {
        if (checkPresentation != null)
            narrativeHistory.Add(NarrativeUiHistoryEntry.ForCheckResult(checkPresentation));

        for (int i = 0; i < view.VisibleTextBlocks.Count; i++)
        {
            NarrativeDialogueVisibleBlock block = view.VisibleTextBlocks[i];
            narrativeHistory.Add(NarrativeUiHistoryEntry.ForBlock(
                block.SpeakerDisplayName, block.SpeakerRole, block.Text, block.CheckPresentation, block.Kind));
        }

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

    private void RenderNarrativeDialogueHistory()
    {
        if (narrativeHistoryContainer == null)
            return;

        narrativeHistoryContainer.Clear();
        VisualElement lastEntry = null;
        for (int i = 0; i < narrativeHistory.Count; i++)
        {
            NarrativeUiHistoryEntry entry = narrativeHistory[i];

            if (entry.Kind == NarrativeUiHistoryKind.PlayerChoice)
            {
                Label playerLine = new Label("Вы: " + entry.Text);
                playerLine.AddToClassList("narrative-dialogue-history-player");
                narrativeHistoryContainer.Add(playerLine);
                lastEntry = playerLine;
                continue;
            }

            if (entry.Kind == NarrativeUiHistoryKind.CheckResult)
            {
                VisualElement checkLine = new VisualElement();
                checkLine.AddToClassList("narrative-dialogue-history-check-result");
                checkLine.Add(BuildNarrativeCheckHeaderElement(entry.CheckPresentation));
                narrativeHistoryContainer.Add(checkLine);
                lastEntry = checkLine;
                continue;
            }

            VisualElement block = new VisualElement();
            block.AddToClassList("narrative-dialogue-history-entry");

            // Checked-observation (§3 инструкции "новое отображение пассивных
            // наблюдений и проверок"): Observation/Memory/HeroThought/
            // Narration с пассивной проверкой рисуются одной инлайн-строкой
            // "ИСТОЧНИК: РЕЗУЛЬТАТ — текст" без подписи говорящего — это
            // наблюдение героя, а не реплика NPC рядом с которым оно возникло.
            // Настоящие реплики (MainLine/CompanionLine) сохраняют подпись
            // говорящего и текст отдельной строкой даже если у них тоже есть
            // проверка (заголовок тогда рисуется отдельно, без текста).
            bool isCheckedObservation = entry.CheckPresentation != null &&
                (entry.BlockKind == DialogueTextBlockKind.Observation ||
                 entry.BlockKind == DialogueTextBlockKind.Memory ||
                 entry.BlockKind == DialogueTextBlockKind.HeroThought ||
                 entry.BlockKind == DialogueTextBlockKind.Narration);

            if (isCheckedObservation)
            {
                block.Add(BuildNarrativePassiveCheckLine(entry.CheckPresentation, entry.Text));
                narrativeHistoryContainer.Add(block);
                lastEntry = block;
                continue;
            }

            if (entry.CheckPresentation != null)
                block.Add(BuildNarrativeCheckHeaderElement(entry.CheckPresentation));

            Label speaker = new Label(entry.SpeakerDisplayName);
            speaker.AddToClassList("narrative-dialogue-history-speaker");
            block.Add(speaker);

            Label text = new Label(entry.Text);
            text.AddToClassList("narrative-dialogue-history-text");
            block.Add(text);

            narrativeHistoryContainer.Add(block);
            lastEntry = block;
        }

        if (lastEntry != null && narrativeTextScroll != null)
        {
            VisualElement target = lastEntry;
            narrativeTextScroll.schedule.Execute(() =>
            {
                if (target != null && target.panel != null)
                    narrativeTextScroll.ScrollTo(target);
            });
        }
    }

    private void ApplyNarrativeSpeakerPortrait(string speakerId)
    {
        if (narrativePortrait == null)
            return;

        DialogueSpeakerData speaker = narrativeDialogueDatabase != null
            ? narrativeDialogueDatabase.FindSpeaker(speakerId)
            : null;
        Sprite portrait = speaker != null ? speaker.Portrait : null;
        if (portrait != null)
        {
            narrativePortrait.style.backgroundImage = new StyleBackground(portrait);
            narrativePortrait.style.unityBackgroundScaleMode = ScaleMode.ScaleAndCrop;
            if (narrativePortraitPlaceholder != null)
                narrativePortraitPlaceholder.style.display = DisplayStyle.None;
        }
        else
        {
            narrativePortrait.style.backgroundImage = default(StyleBackground);
            if (narrativePortraitPlaceholder != null)
                narrativePortraitPlaceholder.style.display = DisplayStyle.Flex;
        }
    }

    private void OnNarrativeDialogueChoiceSelected(string choiceId, string choiceText)
    {
        if (!IsNarrativeDialogueActive)
            return;

        narrativeHistory.Add(NarrativeUiHistoryEntry.ForPlayerChoice(choiceText));

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

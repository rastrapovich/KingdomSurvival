using System;
using System.Collections;
using System.Collections.Generic;
using KingdomSurvival.Chapter01;
using KingdomSurvival.DialogueDatabase;
using KingdomSurvival.UILayout;
using UnityEngine;
using UnityEngine.UIElements;

// UI-M04 (ProjectDocs/UI_ARCHITECTURE.md §9): постоянная структура диалога
// (overlay/panel/portrait/speaker/role/text-scroll/history/choices/
// check-tooltip) переехала в Prototype_Main.uxml ("narrative-dialogue-
// overlay") + Narrative.uss, позиции/dimming читаются из UI Конструктора
// (KingdomSurvivalUILayouts.asset, экран "narrative-dialogue", теперь
// autoApply). Controller здесь только находит уже существующие элементы
// через BindRequiredElement и наполняет их данными/шаблонами — не создаёт
// постоянное дерево через `new VisualElement`.
//
// Портретный конвейер (ApplyNarrativeSpeakerPortrait ниже) этим переносом
// не тронут вообще (§8 доктрины): Sprite из DialogueDatabase + рамка из
// UXML + геометрия из UI Конструктора + индивидуальная кадрировка
// (OverridePortraitFraming/PortraitScale/PortraitOffsetNormalized/
// PortraitFlipX) на говорящего — защищённая связка.
//
// Сборка ResponseGroup (BuildNarrativeHistoryGroupElement и построение
// заголовков проверки в PrototypeUIController.NarrativeCheckPresentation.cs)
// сознательно НЕ шаблонизирована этим этапом: форма записи отличается по
// виду сегмента (CheckResult/CheckedObservation/обычная реплика с 0..N
// абзацами) — это не единообразная повторяемая строка, а переменная по
// форме композиция без единого inline style.* (§2 доктрины разрешает
// динамическое построение кода без "сотни new Label" c ручными style.width/
// style.backgroundColor — здесь их нет, только текст и CSS-классы).
public partial class PrototypeUIController
{
    private const string NarrativeDialogueScreenName = "Диалог";

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
    private Label narrativePortraitPlaceholder;
    private Label narrativeSpeakerLabel;
    private Label narrativeRoleLabel;
    private ScrollView narrativeTextScroll;
    private VisualElement narrativeHistoryContainer;
    private VisualElement narrativeChoicesContainer;

    private bool narrativeUiBound;

    private VisualTreeAsset narrativeChoiceButtonTemplate;
    private VisualTreeAsset narrativeChoiceSecondaryTemplate;
    private VisualTreeAsset narrativeHistoryPlayerLineTemplate;

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

    // ------------------------------------------------------------------
    // Инициализация — только поиск элементов, постоянное дерево не строит.
    // ------------------------------------------------------------------

    private void InitializeNarrativeDialogueUi()
    {
        if (interfaceRoot == null || narrativeUiBound)
            return;

        narrativeDialogueOverlay = BindRequiredElement<VisualElement>(interfaceRoot, NarrativeDialogueScreenName, "narrative-dialogue-overlay");
        narrativePortrait = BindRequiredElement<VisualElement>(interfaceRoot, NarrativeDialogueScreenName, "narrative-dialogue-portrait");
        narrativePortraitPlaceholder = BindRequiredElement<Label>(interfaceRoot, NarrativeDialogueScreenName, "narrative-dialogue-portrait-placeholder");
        narrativeSpeakerLabel = BindRequiredElement<Label>(interfaceRoot, NarrativeDialogueScreenName, "narrative-dialogue-speaker");
        narrativeRoleLabel = BindRequiredElement<Label>(interfaceRoot, NarrativeDialogueScreenName, "narrative-dialogue-role");
        narrativeTextScroll = BindRequiredElement<ScrollView>(interfaceRoot, NarrativeDialogueScreenName, "narrative-dialogue-text");
        narrativeHistoryContainer = BindRequiredElement<VisualElement>(interfaceRoot, NarrativeDialogueScreenName, "narrative-dialogue-history");
        narrativeChoicesContainer = BindRequiredElement<VisualElement>(interfaceRoot, NarrativeDialogueScreenName, "narrative-dialogue-choices");
        narrativeCheckTooltip = BindRequiredElement<VisualElement>(interfaceRoot, NarrativeDialogueScreenName, "narrative-check-tooltip");

        if (narrativeDialogueOverlay == null ||
            narrativePortrait == null ||
            narrativePortraitPlaceholder == null ||
            narrativeSpeakerLabel == null ||
            narrativeRoleLabel == null ||
            narrativeTextScroll == null ||
            narrativeHistoryContainer == null ||
            narrativeChoicesContainer == null ||
            narrativeCheckTooltip == null)
            return;

        narrativeUiBound = true;
        narrativeDialogueOverlay.style.display = DisplayStyle.None;
    }

    // Единственная точка входа игрового кода в диалог. Активные проверки
    // разрешаются через общий NarrativeCheckResolver (§7); принудительного
    // исхода здесь нет и быть не может — это привилегия только Preview
    // в редакторе (§13/§20).
    public bool TryOpenNarrativeDialogueById(string dialogueId)
    {
        if (!narrativeUiBound)
            return false;

        if (narrativeDialogueDatabase == null)
            narrativeDialogueDatabase = DialogueDatabaseRuntime.LoadDefaultDatabase();
        if (narrativeDialogueDatabase == null)
        {
            Debug.LogError("Narrative UI: не найдена база диалогов Resources/" + DialogueDatabaseAsset.ResourcesPath + ".asset");
            return false;
        }

        // P09-T05: HasBlockingModalWorkExceptCamp, а не HasBlockingModalWork —
        // экран Лагеря обязан пропускать D11C поверх себя (см. комментарий
        // над HasBlockingModalWork в ModalQueue.cs).
        if (gameState == null || isGameOver || IsNarrativeDialogueActive || HasBlockingModalWorkExceptCamp())
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
            gameState.WorldSeed,
            Chapter01ContextBuilder.GetPartySize(gameState));

        if (!started)
        {
            Debug.LogError("Narrative UI: не удалось открыть диалог '" + dialogueId + "'.\n" + error);
            return false;
        }

        narrativeHistory.Clear();
        PauseForBlockingModal();
        narrativeDialogueOverlay.style.display = DisplayStyle.Flex;
        // Narrative Dialogue — верхний блокирующий gameplay-слой. Любой
        // fullscreen-экран (Camp/Hero/Journal и будущие аналоги) может
        // вызвать диалог после собственного BringToFront(), поэтому при
        // каждом открытии возвращаем overlay на вершину sibling stack.
        narrativeDialogueOverlay.BringToFront();
        DisplayNarrativeView(view, null);
        if (timeToggleButton != null)
        {
            timeToggleButton.SetEnabled(false);
            timeToggleButton.tooltip = "Сначала завершите разговор";
        }
        return true;
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

    // Кнопка варианта ответа и вторичная строка (механика/подсказка
    // недоступности) клонируются из NarrativeChoiceButton.uxml/
    // NarrativeChoiceSecondary.uxml как плоские соседи narrativeChoicesContainer
    // — PrototypeUIController.NarrativePresentation.cs (ApplyNarrativeLayoutPresentation)
    // читает классы прямых детей контейнера напрямую через ElementAt(i), поэтому
    // клон не остаётся обёрнутым в TemplateContainer (см. InstantiateFlatTemplate).
    private void RenderNarrativeDialogueChoices(NarrativeDialogueView view)
    {
        narrativeChoicesContainer.Clear();

        for (int i = 0; i < view.AvailableChoices.Count; i++)
        {
            NarrativeDialogueChoiceView choiceView = view.AvailableChoices[i];
            string choiceId = choiceView.ChoiceId;
            DialogueChoiceKind choiceKind = choiceView.Kind;
            // Continue — кнопка "читать дальше" между шагами одной сцены,
            // не реплика героя (§15 инструкции P06): всегда "…", независимо
            // от авторского Text, без вероятности/tooltip/механики.
            string choiceText = choiceKind == DialogueChoiceKind.Continue ? "…" : choiceView.Text;

            Button button = InstantiateFlatTemplate<Button>(LoadNarrativeChoiceButtonTemplate(), "narrative-dialogue-choice");
            if (button == null)
                continue;
            button.text = choiceText;
            button.clicked += () => OnNarrativeDialogueChoiceSelected(choiceId, choiceText, choiceKind);
            if (choiceKind == DialogueChoiceKind.Exit)
                button.AddToClassList("narrative-dialogue-choice-exit");
            else if (choiceKind == DialogueChoiceKind.Continue)
                button.AddToClassList("narrative-dialogue-choice-continue");
            narrativeChoicesContainer.Add(button);

            if (choiceKind != DialogueChoiceKind.Continue && !string.IsNullOrWhiteSpace(choiceView.MechanicalSummary))
            {
                Label mechanic = InstantiateFlatTemplate<Label>(LoadNarrativeChoiceSecondaryTemplate(), "narrative-dialogue-choice-secondary");
                if (mechanic != null)
                {
                    mechanic.text = choiceView.MechanicalSummary;
                    mechanic.AddToClassList("narrative-dialogue-choice-mechanic");
                    narrativeChoicesContainer.Add(mechanic);
                }
            }
        }

        for (int i = 0; i < view.DisabledChoices.Count; i++)
        {
            NarrativeDialogueChoiceView disabledView = view.DisabledChoices[i];

            Button button = InstantiateFlatTemplate<Button>(LoadNarrativeChoiceButtonTemplate(), "narrative-dialogue-choice");
            if (button == null)
                continue;
            button.text = disabledView.Text;
            button.AddToClassList("narrative-dialogue-choice-disabled");
            button.SetEnabled(false);
            narrativeChoicesContainer.Add(button);

            if (!string.IsNullOrWhiteSpace(disabledView.DisabledHint))
            {
                Label hint = InstantiateFlatTemplate<Label>(LoadNarrativeChoiceSecondaryTemplate(), "narrative-dialogue-choice-secondary");
                if (hint != null)
                {
                    hint.text = disabledView.DisabledHint;
                    hint.AddToClassList("narrative-dialogue-choice-hint");
                    narrativeChoicesContainer.Add(hint);
                }
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
                Label playerLine = InstantiateFlatTemplate<Label>(LoadNarrativeHistoryPlayerLineTemplate(), "narrative-dialogue-history-player");
                if (playerLine != null)
                {
                    playerLine.text = "Вы: " + item.PlayerChoiceText;
                    narrativeHistoryContainer.Add(playerLine);
                }
                continue;
            }

            narrativeHistoryContainer.Add(BuildNarrativeHistoryGroupElement(item));
        }
    }

    // Строит один визуальный контейнер для целой ResponseGroup — все
    // сегменты одного view (§1/§7 дополнения к инструкции). Сегменты
    // считает NarrativeUiHistoryGrouping (чистая, тестируемая логика, не
    // зависящая от UI Toolkit); здесь только раскладка по VisualElement.
    // Форма сегмента отличается от кейса к кейсу (см. комментарий над
    // классом) — намеренно не шаблонизировано этим переносом.
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

    private void OnNarrativeDialogueChoiceSelected(string choiceId, string choiceText, DialogueChoiceKind choiceKind)
    {
        if (!IsNarrativeDialogueActive)
            return;

        // Continue — управляющий элемент ("читать дальше"), а не реплика
        // героя: не должен попадать в историю как "Вы: …" (§15 инструкции P06).
        if (choiceKind != DialogueChoiceKind.Continue)
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

            // P08-T03: N10 заканчивается действием «Выбрать состав похода» без
            // эффектов — реальный набор 0-4 бойцов происходит в picker'е Экрана
            // героя (панель «СОСТАВ ПОХОДА»), не в самом диалоге.
            if (string.Equals(completedDialogueId, Chapter01Ids.Dialogues.D10, StringComparison.Ordinal))
                OpenHeroScreen();

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
        // Chapter01StoryDirector.HandleDialogueCompleted (вызван выше, до
        // закрытия) мог изменить ресурсы (например, Food -12 при потере
        // скота в P05) — без перерисовки верхняя панель показывала бы
        // старое число до следующего не связанного действия.
        RefreshInterface();
        RefreshTimeControlAvailability();
        ResumeAfterBlockingModalIfReady();
    }

    // Единственное намеренное отклонение от общего идиома шаблонов проекта
    // (Journal/Hero Screen добавляют в дерево сам TemplateContainer —
    // instance.Add(); см. PrototypeUIController.Journal.cs/HeroScreen.cs):
    // здесь клон нужно вернуть плоским прямым ребёнком родителя, потому что
    // PrototypeUIController.NarrativePresentation.cs (ApplyNarrativeLayoutPresentation)
    // проверяет классы через narrativeChoicesContainer/narrativeHistoryContainer
    // .ElementAt(i).ClassListContains(...) напрямую по прямым детям контейнера —
    // обёртка TemplateContainer эти классы не несёт и сломала бы проверку.
    private static T InstantiateFlatTemplate<T>(VisualTreeAsset template, string rootName) where T : VisualElement
    {
        if (template == null)
            return null;

        TemplateContainer instance = template.Instantiate();
        T root = instance.Q<T>(rootName);
        if (root == null)
            return null;

        root.RemoveFromHierarchy();
        return root;
    }

    private VisualTreeAsset LoadNarrativeChoiceButtonTemplate()
    {
        if (narrativeChoiceButtonTemplate == null)
            narrativeChoiceButtonTemplate = Resources.Load<VisualTreeAsset>("Templates/NarrativeChoiceButton");
        return narrativeChoiceButtonTemplate;
    }

    private VisualTreeAsset LoadNarrativeChoiceSecondaryTemplate()
    {
        if (narrativeChoiceSecondaryTemplate == null)
            narrativeChoiceSecondaryTemplate = Resources.Load<VisualTreeAsset>("Templates/NarrativeChoiceSecondary");
        return narrativeChoiceSecondaryTemplate;
    }

    private VisualTreeAsset LoadNarrativeHistoryPlayerLineTemplate()
    {
        if (narrativeHistoryPlayerLineTemplate == null)
            narrativeHistoryPlayerLineTemplate = Resources.Load<VisualTreeAsset>("Templates/NarrativeHistoryPlayerLine");
        return narrativeHistoryPlayerLineTemplate;
    }
}

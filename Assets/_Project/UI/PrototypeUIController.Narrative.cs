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
// Иллюстрация события при наличии заменяет портрет на всю сцену; без неё
// сохраняется портретный конвейер: Sprite говорящего из DialogueDatabase,
// рамка UXML, геометрия UI Конструктора и индивидуальная кадрировка
// (OverridePortraitFraming/PortraitScale/PortraitOffsetNormalized/PortraitFlipX).
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

    // Один элемент UI-истории. ResponseGroup соответствует одному вызову
    // DisplayNarrativeView(), а runtime-контракт гарантирует в нём максимум
    // одну реплику. Поэтому разные фразы и разные говорящие всегда создают
    // отдельные последовательные элементы истории.
    private sealed class NarrativeUiHistoryItem
    {
        public NarrativeUiHistoryItemKind Kind;

        // Только для PlayerChoice.
        public string PlayerChoiceText;

        // Только для ResponseGroup — список совместим с прежней моделью,
        // но канонически содержит не больше одного блока.
        public NarrativeCheckPresentationData LeadingActiveCheck;
        public IReadOnlyList<NarrativeDialogueVisibleBlock> Blocks;

        // 12Е-7: особенности, сработавшие на выборе, который привёл к шагу.
        public List<FeatureActivation> Features;

        public static NarrativeUiHistoryItem ForPlayerChoice(string text)
        {
            return new NarrativeUiHistoryItem { Kind = NarrativeUiHistoryItemKind.PlayerChoice, PlayerChoiceText = text };
        }

        public static NarrativeUiHistoryItem ForResponseGroup(
            NarrativeCheckPresentationData leadingActiveCheck,
            IReadOnlyList<NarrativeDialogueVisibleBlock> blocks,
            List<FeatureActivation> features = null)
        {
            return new NarrativeUiHistoryItem
            {
                Kind = NarrativeUiHistoryItemKind.ResponseGroup,
                LeadingActiveCheck = leadingActiveCheck,
                Blocks = blocks,
                Features = features
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

    // ПР-12А: содержание режимов кампании регистрируется до создания
    // любой партии — UI читает только CampaignContent.
    static PrototypeUIController()
    {
        KingdomSurvival.Chapter01.Chapter01Content.Register();
        KingdomSurvival.FreePlay.FreePlayContent.Register();
    }

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

        // ПР-06А: те же спутники, что в Chapter01ContextBuilder — бойцы и свита.
        List<string> presentCompanionIds = Chapter01ContextBuilder.GetPresentCompanionIds(gameState);

        List<string> presentItemIds = Chapter01ContextBuilder.GetPresentItemIds(gameState);

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
            Chapter01ContextBuilder.GetPartySize(gameState),
            gameState);

        if (!started)
        {
            Debug.LogError("Narrative UI: не удалось открыть диалог '" + dialogueId + "'.\n" + error);
            return false;
        }

        CloseJournalForMandatoryEvent();
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

    // Единая точка показа нового presentation-шага. В view находится ровно
    // одна текущая реплика (или ни одной в техническом узле); следующие
    // TextBlock выдаются runtime по одному через нейтральный Continue.
    private void DisplayNarrativeView(NarrativeDialogueView view, NarrativeCheckPresentationData checkPresentation,
        List<FeatureActivation> features = null)
    {
        narrativeHistory.Add(NarrativeUiHistoryItem.ForResponseGroup(checkPresentation, view.VisibleTextBlocks, features));

        NarrativeDialogueVisibleBlock latestBlock = view.VisibleTextBlocks.Count > 0
            ? view.VisibleTextBlocks[view.VisibleTextBlocks.Count - 1]
            : null;
        narrativeSpeakerLabel.text = latestBlock != null ? latestBlock.SpeakerDisplayName : string.Empty;
        narrativeRoleLabel.text = latestBlock != null ? latestBlock.SpeakerRole : string.Empty;
        ApplyNarrativeSpeakerPortrait(view.DialogueId,
            latestBlock != null ? latestBlock.SpeakerId : string.Empty);

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
    // ResponseGroup — одна текущая реплика (и, при наличии, результат
    // активной проверки). NarrativePresentation.cs гасит прочитанные шаги
    // и прокручивает историю именно по этим границам.
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

    // Строит визуальный контейнер одного presentation-шага — общая отрисовка
    // с превью редактора (NarrativeDialogueRendering.BuildHistoryGroup).
    private VisualElement BuildNarrativeHistoryGroupElement(NarrativeUiHistoryItem item)
    {
        VisualElement group = NarrativeDialogueRendering.BuildHistoryGroup(item.LeadingActiveCheck, item.Blocks, NarrativeCheckTooltipHost);
        if (item.Features == null || item.Features.Count == 0)
            return group;

        // 12Е-7: сработавшая особенность — сразу после итога проверки (если
        // он есть) и до ответной реплики.
        int index = 0;
        for (int i = 0; i < group.childCount; i++)
        {
            if (group[i].ClassListContains("narrative-dialogue-history-check-result"))
            {
                index = i + 1;
                break;
            }
        }
        foreach (FeatureActivation activation in item.Features)
            group.Insert(index++, BuildNarrativeFeatureElement(activation));
        return group;
    }

    // «Особенность · Гаррик — «Зацепка»» и что она дала. Цвет один и тот же
    // при любом исходе: он отмечает особенность, а не правильный ответ.
    private VisualElement BuildNarrativeFeatureElement(FeatureActivation activation)
    {
        VisualElement element = new VisualElement();
        element.AddToClassList("narrative-dialogue-history-feature");

        Label title = new Label("Особенность · " + FeaturePresentation.Title(gameState, activation));
        title.AddToClassList("narrative-dialogue-history-feature-title");
        element.Add(title);

        string bodyText = FeaturePresentation.Body(activation);
        if (!string.IsNullOrWhiteSpace(bodyText))
        {
            Label body = new Label(bodyText);
            body.AddToClassList("narrative-dialogue-history-feature-body");
            element.Add(body);
        }
        return element;
    }

    // Иллюстрация события при наличии заменяет портрет на всю сцену; без неё —
    // портрет говорящего с его кадрировкой (NarrativeDialogueRendering).
    private void ApplyNarrativeSpeakerPortrait(string dialogueId, string speakerId)
    {
        NarrativeDialogueRendering.ApplySpeakerPortrait(
            narrativePortrait, narrativePortraitPlaceholder, narrativeDialogueDatabase,
            dialogueId, speakerId, GetNarrativeScreenSize());
    }

    private Vector2 GetNarrativeScreenSize()
    {
        VisualElement screen = interfaceRoot != null ? interfaceRoot.Q<VisualElement>("screen") : null;
        return screen != null && screen.resolvedStyle.width > 0f && screen.resolvedStyle.height > 0f
            ? new Vector2(screen.resolvedStyle.width, screen.resolvedStyle.height)
            : Vector2.zero;
    }

    private void OnNarrativeDialogueChoiceSelected(string choiceId, string choiceText, DialogueChoiceKind choiceKind)
    {
        if (!IsNarrativeDialogueActive)
            return;

        // Continue — управляющий элемент ("читать дальше"), а не реплика
        // героя: не должен попадать в историю как "Вы: …" (§15 инструкции P06).
        if (choiceKind != DialogueChoiceKind.Continue)
            narrativeHistory.Add(NarrativeUiHistoryItem.ForPlayerChoice(choiceText));

        // 12Е-7: что сработало на этом выборе (проверка, переброс, новое сведение).
        int featureSequence = FeatureDispatcher.LastActivationSequence(gameState);
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
        List<FeatureActivation> features = FeatureDispatcher.ActivationsSince(gameState, featureSequence);

        if (result.DialogueEnded)
        {
            // Разговор закрывается этим выбором — сработавшее остаётся в донесениях.
            if (gameState != null && features.Count > 0)
                AddReport(string.Join("\n", FeaturePresentation.Lines(gameState, features)));

            string completedDialogueId = narrativeDialogueSession.DialogueId;
            Chapter01StoryDirector.HandleDialogueCompleted(gameState, completedDialogueId);
            OnStoryDialogueCompleted(completedDialogueId);
            AwardEncounterExperience(completedDialogueId);
            CloseNarrativeDialogue();

            // P08-T03: N10 заканчивается действием «Выбрать состав похода» без
            // эффектов — реальный набор 0-4 бойцов происходит в picker'е Экрана
            // героя (панель «СОСТАВ ПОХОДА»), не в самом диалоге.
            // ПР-04: сцена завершена и её последствия применены — устойчивая
            // точка для автосохранения (не посреди реплик и выбора).
            Autosave();

            if (string.Equals(completedDialogueId, Chapter01Ids.Dialogues.D10, StringComparison.Ordinal))
            {
                gameState.Narrative?.SetFlag(Chapter01Ids.Flags.PartyGatheringSeen);
                OpenHeroScreen();
            }

            return;
        }

        DisplayNarrativeView(result.View, result.CheckPresentation, features);
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

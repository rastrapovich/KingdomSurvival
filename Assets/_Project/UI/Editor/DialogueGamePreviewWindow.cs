using System;
using System.Collections.Generic;
using KingdomSurvival.DialogueDatabase;
using KingdomSurvival.DialogueDatabase.Editor;
using KingdomSurvival.UILayout;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

// Превью диалога «как в игре»: настоящее окно диалога из Prototype_Main.uxml
// с игровыми стилями, раскладкой UI Конструктора и той же отрисовкой
// истории и проверок (NarrativeDialogueRendering), что и в игре. Кадр
// строится в эталонном разрешении и масштабируется под окно — пропорции
// совпадают с игрой. Разговор идёт через тот же NarrativeDialogueRuntimeSession;
// отличие от игры только одно — тестовые условия и принудительный исход
// проверки (DialoguePreviewContext).
public sealed class DialogueGamePreviewWindow : EditorWindow
{
    private const string MainUxmlPath = "Assets/_Project/UI/Prototype/Prototype_Main.uxml";

    [InitializeOnLoadMethod]
    private static void Register()
    {
        DialogueGamePreview.Opener = ShowFor;
    }

    public static void ShowFor(string dialogueId)
    {
        DialogueGamePreviewWindow window = GetWindow<DialogueGamePreviewWindow>("Превью диалога");
        window.minSize = new Vector2(480f, 320f);
        window.dialogueId = dialogueId ?? string.Empty;
        window.Restart();
        window.Focus();
    }

    private sealed class HistoryItem
    {
        public string PlayerText;
        public NarrativeCheckPresentationData LeadingCheck;
        public IReadOnlyList<NarrativeDialogueVisibleBlock> Blocks;
    }

    [SerializeField] private string dialogueId = string.Empty;
    [SerializeField] private DialoguePreviewContext context = new DialoguePreviewContext();
    [SerializeField] private bool contextOpen;

    private DialogueDatabaseAsset database;
    private NarrativeDialogueRuntimeSession session;
    private readonly List<HistoryItem> history = new List<HistoryItem>();
    private readonly List<string> issues = new List<string>();
    private bool ended;
    private string startError;

    // Игровое окно диалога
    private VisualElement host;
    private VisualElement gameRoot;
    private VisualElement overlay;
    private VisualElement portrait;
    private Label portraitPlaceholder;
    private Label speakerLabel;
    private Label roleLabel;
    private ScrollView textScroll;
    private VisualElement historyContainer;
    private VisualElement choicesContainer;
    private NarrativeDialogueRendering.CheckTooltip tooltip;
    private Vector2 reference = new Vector2(1920f, 1080f);

    // Панель редактора
    private Label titleLabel;
    private Label checkSummary;
    private VisualElement issuesPanel;
    private IMGUIContainer contextPanel;
    private ToolbarToggle contextToggle;

    private VisualTreeAsset choiceButtonTemplate;
    private VisualTreeAsset choiceSecondaryTemplate;
    private VisualTreeAsset playerLineTemplate;

    private void CreateGUI()
    {
        VisualElement root = rootVisualElement;
        root.style.flexDirection = FlexDirection.Column;

        Toolbar toolbar = new Toolbar();
        ToolbarButton restart = new ToolbarButton(Restart) { text = "↺ Сначала", tooltip = "Начать разговор заново с текущими тестовыми условиями" };
        toolbar.Add(restart);
        contextToggle = new ToolbarToggle { text = "Тестовые условия", value = contextOpen };
        contextToggle.RegisterValueChangedCallback(evt =>
        {
            contextOpen = evt.newValue;
            contextPanel.style.display = contextOpen ? DisplayStyle.Flex : DisplayStyle.None;
        });
        toolbar.Add(contextToggle);
        titleLabel = new Label { style = { unityFontStyleAndWeight = FontStyle.Bold, marginLeft = 8, unityTextAlign = TextAnchor.MiddleLeft } };
        toolbar.Add(titleLabel);
        toolbar.Add(new ToolbarSpacer { flex = true });
        checkSummary = new Label { style = { unityTextAlign = TextAnchor.MiddleRight, marginRight = 6 } };
        checkSummary.RegisterCallback<ClickEvent>(_ =>
            issuesPanel.style.display = issuesPanel.style.display == DisplayStyle.Flex ? DisplayStyle.None : DisplayStyle.Flex);
        toolbar.Add(checkSummary);
        root.Add(toolbar);

        issuesPanel = new VisualElement { style = { display = DisplayStyle.None, paddingLeft = 6, paddingRight = 6, paddingTop = 4, paddingBottom = 4 } };
        root.Add(issuesPanel);

        contextPanel = new IMGUIContainer(() =>
        {
            if (context.Draw())
                Restart();
        });
        contextPanel.style.display = contextOpen ? DisplayStyle.Flex : DisplayStyle.None;
        contextPanel.style.paddingLeft = 6;
        contextPanel.style.paddingRight = 6;
        contextPanel.style.paddingBottom = 6;
        root.Add(contextPanel);

        host = new VisualElement { style = { flexGrow = 1, overflow = Overflow.Hidden, backgroundColor = new Color(0.06f, 0.06f, 0.07f) } };
        host.RegisterCallback<GeometryChangedEvent>(_ => FitFrame());
        root.Add(host);

        BuildGameFrame();
        Restart();
    }

    // ------------------------------------------------------------------
    // Кадр игры: клон Prototype_Main.uxml, видно только окно диалога.
    // ------------------------------------------------------------------

    private void BuildGameFrame()
    {
        UILayoutDatabaseAsset layoutDatabase = UILayoutRuntimeApplier.LoadDefaultDatabase();
        if (layoutDatabase != null)
            reference = layoutDatabase.ReferenceResolution;

        VisualTreeAsset main = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(MainUxmlPath);
        if (main == null)
        {
            host.Add(new Label("Не найдена разметка игры: " + MainUxmlPath));
            return;
        }

        gameRoot = main.Instantiate();
        gameRoot.style.position = Position.Absolute;
        gameRoot.style.width = reference.x;
        gameRoot.style.height = reference.y;
        gameRoot.style.transformOrigin = new TransformOrigin(0, 0, 0);
        host.Add(gameRoot);

        VisualElement screen = gameRoot.Q<VisualElement>("screen");
        if (screen != null)
        {
            screen.style.width = reference.x;
            screen.style.height = reference.y;
        }

        overlay = gameRoot.Q<VisualElement>("narrative-dialogue-overlay");
        if (overlay == null)
        {
            host.Add(new Label("В разметке игры нет окна диалога (narrative-dialogue-overlay)."));
            return;
        }

        // Всё, кроме окна диалога и его предков, скрыто.
        for (VisualElement node = overlay; node != null && node != gameRoot; node = node.parent)
        {
            VisualElement parent = node.parent;
            if (parent == null)
                break;
            foreach (VisualElement sibling in parent.Children())
            {
                if (sibling != node)
                    sibling.style.display = DisplayStyle.None;
            }
        }

        overlay.style.display = DisplayStyle.Flex;
        portrait = overlay.Q<VisualElement>("narrative-dialogue-portrait");
        portraitPlaceholder = overlay.Q<Label>("narrative-dialogue-portrait-placeholder");
        speakerLabel = overlay.Q<Label>("narrative-dialogue-speaker");
        roleLabel = overlay.Q<Label>("narrative-dialogue-role");
        textScroll = overlay.Q<ScrollView>("narrative-dialogue-text");
        historyContainer = overlay.Q<VisualElement>("narrative-dialogue-history");
        choicesContainer = overlay.Q<VisualElement>("narrative-dialogue-choices");
        tooltip = new NarrativeDialogueRendering.CheckTooltip(overlay.Q<VisualElement>("narrative-check-tooltip"), overlay);

        UILayoutScreenBinder.ApplyScreen(UILayoutDatabaseAsset.NarrativeDialogueScreenId, gameRoot, reference);

        choiceButtonTemplate = Resources.Load<VisualTreeAsset>("Templates/NarrativeChoiceButton");
        choiceSecondaryTemplate = Resources.Load<VisualTreeAsset>("Templates/NarrativeChoiceSecondary");
        playerLineTemplate = Resources.Load<VisualTreeAsset>("Templates/NarrativeHistoryPlayerLine");
    }

    // Эталонный кадр масштабируется целиком и центрируется в окне.
    private void FitFrame()
    {
        if (gameRoot == null || host == null)
            return;
        float width = host.resolvedStyle.width;
        float height = host.resolvedStyle.height;
        if (width <= 0f || height <= 0f)
            return;

        float scale = Mathf.Min(width / reference.x, height / reference.y);
        gameRoot.style.scale = new Scale(new Vector3(scale, scale, 1f));
        gameRoot.style.left = (width - reference.x * scale) * 0.5f;
        gameRoot.style.top = (height - reference.y * scale) * 0.5f;
    }

    // ------------------------------------------------------------------
    // Разговор
    // ------------------------------------------------------------------

    private void Restart()
    {
        if (session != null && session.IsActive)
            session.End();
        session = null;
        history.Clear();
        ended = false;
        startError = null;

        database = Resources.Load<DialogueDatabaseAsset>(DialogueDatabaseAsset.ResourcesPath);
        DialogueDefinitionData dialogue = database != null ? database.FindDialogue(dialogueId) : null;
        if (titleLabel != null)
            titleLabel.text = dialogue != null && !string.IsNullOrWhiteSpace(dialogue.Title) ? dialogue.Title : dialogueId;

        RefreshIssues();

        if (database == null || string.IsNullOrEmpty(dialogueId))
        {
            startError = database == null ? "Не найдена база диалогов." : "Выберите диалог в базе диалогов и нажмите «▶ Играть».";
            Render(null);
            return;
        }

        context.Build(out HeroProfileData hero, out NarrativeStateData state, out List<string> companions, out List<string> items);
        session = new NarrativeDialogueRuntimeSession();
        bool started = session.Start(database, dialogueId, hero, state, out NarrativeDialogueView view, out string error,
            companions, items, context.WorldSeed);
        if (!started)
        {
            session = null;
            startError = string.IsNullOrWhiteSpace(error) ? "Диалог не запускается." : error;
            Render(null);
            return;
        }

        view = session.BuildViewPreview(context.RevealHiddenTextForAuthor);
        history.Add(new HistoryItem { Blocks = view.VisibleTextBlocks });
        Render(view);
    }

    private void OnChoice(string choiceId, string choiceText, DialogueChoiceKind kind)
    {
        if (session == null || !session.IsActive)
            return;

        // «…» — «читать дальше», не реплика героя: в историю не попадает.
        if (kind != DialogueChoiceKind.Continue)
            history.Add(new HistoryItem { PlayerText = choiceText });

        NarrativeDialogueSelectionResult result;
        try
        {
            result = session.SelectChoicePreview(choiceId, context.ForcedOutcome);
        }
        catch (InvalidOperationException exception)
        {
            startError = exception.Message;
            Render(null);
            return;
        }

        if (result.DialogueEnded)
        {
            ended = true;
            Render(null);
            return;
        }

        NarrativeDialogueView view = session.BuildViewPreview(context.RevealHiddenTextForAuthor);
        history.Add(new HistoryItem { LeadingCheck = result.CheckPresentation, Blocks = view.VisibleTextBlocks });
        Render(view);
    }

    private void Render(NarrativeDialogueView view)
    {
        if (historyContainer == null || choicesContainer == null)
            return;

        tooltip?.Hide();
        historyContainer.Clear();
        NarrativeDialogueVisibleBlock latest = null;
        foreach (HistoryItem item in history)
        {
            if (item.PlayerText != null)
            {
                Label playerLine = InstantiateFlat<Label>(playerLineTemplate, "narrative-dialogue-history-player");
                if (playerLine != null)
                {
                    playerLine.text = "Вы: " + item.PlayerText;
                    historyContainer.Add(playerLine);
                }
                continue;
            }

            VisualElement group = NarrativeDialogueRendering.BuildHistoryGroup(item.LeadingCheck, item.Blocks, tooltip);
            AppendAuthorOnlyText(group, item.Blocks);
            historyContainer.Add(group);
            if (item.Blocks != null && item.Blocks.Count > 0)
                latest = item.Blocks[item.Blocks.Count - 1];
        }

        speakerLabel.text = latest != null ? latest.SpeakerDisplayName : string.Empty;
        roleLabel.text = latest != null ? latest.SpeakerRole : string.Empty;
        NarrativeDialogueRendering.ApplySpeakerPortrait(portrait, portraitPlaceholder, database, dialogueId,
            latest != null ? latest.SpeakerId : string.Empty, reference);

        RenderChoices(view);
        NarrativeDialogueRendering.ApplyLayoutPresentation(overlay, speakerLabel, roleLabel, historyContainer, choicesContainer, reference);
        NarrativeDialogueRendering.ApplyReadHistoryOpacity(historyContainer);
        ScrollToLatest();
    }

    // Упущенный текст провалившейся пассивной проверки — только для автора.
    private void AppendAuthorOnlyText(VisualElement group, IReadOnlyList<NarrativeDialogueVisibleBlock> blocks)
    {
        if (!context.RevealHiddenTextForAuthor || blocks == null)
            return;
        foreach (NarrativeDialogueVisibleBlock block in blocks)
        {
            if (block == null || string.IsNullOrEmpty(block.PreviewOnlyHiddenText))
                continue;
            Label hidden = new Label("[только превью] " + block.PreviewOnlyHiddenText);
            hidden.AddToClassList("narrative-dialogue-history-text");
            hidden.style.opacity = 0.6f;
            hidden.style.unityFontStyleAndWeight = FontStyle.Italic;
            group.Add(hidden);
        }
    }

    // Та же раскладка кнопок, что в игре (PrototypeUIController.RenderNarrativeDialogueChoices).
    private void RenderChoices(NarrativeDialogueView view)
    {
        choicesContainer.Clear();

        if (view == null)
        {
            string message = startError ?? (ended ? "Разговор завершён." : string.Empty);
            if (!string.IsNullOrEmpty(message))
                AddSecondary(message, "narrative-dialogue-choice-hint");
            Button again = InstantiateFlat<Button>(choiceButtonTemplate, "narrative-dialogue-choice");
            if (again != null && (ended || startError != null))
            {
                again.text = "↺ Сыграть сначала";
                again.clicked += Restart;
                choicesContainer.Add(again);
            }
            return;
        }

        foreach (NarrativeDialogueChoiceView choiceView in view.AvailableChoices)
        {
            string choiceId = choiceView.ChoiceId;
            DialogueChoiceKind kind = choiceView.Kind;
            string text = kind == DialogueChoiceKind.Continue ? "…" : choiceView.Text;

            Button button = InstantiateFlat<Button>(choiceButtonTemplate, "narrative-dialogue-choice");
            if (button == null)
                continue;
            button.text = text;
            button.clicked += () => OnChoice(choiceId, text, kind);
            if (kind == DialogueChoiceKind.Exit)
                button.AddToClassList("narrative-dialogue-choice-exit");
            else if (kind == DialogueChoiceKind.Continue)
                button.AddToClassList("narrative-dialogue-choice-continue");
            choicesContainer.Add(button);

            if (kind != DialogueChoiceKind.Continue && !string.IsNullOrWhiteSpace(choiceView.MechanicalSummary))
                AddSecondary(choiceView.MechanicalSummary, "narrative-dialogue-choice-mechanic");
        }

        foreach (NarrativeDialogueChoiceView disabledView in view.DisabledChoices)
        {
            Button button = InstantiateFlat<Button>(choiceButtonTemplate, "narrative-dialogue-choice");
            if (button == null)
                continue;
            button.text = disabledView.Text;
            button.AddToClassList("narrative-dialogue-choice-disabled");
            button.SetEnabled(false);
            choicesContainer.Add(button);
            if (!string.IsNullOrWhiteSpace(disabledView.DisabledHint))
                AddSecondary(disabledView.DisabledHint, "narrative-dialogue-choice-hint");
        }
    }

    private void AddSecondary(string text, string className)
    {
        Label line = InstantiateFlat<Label>(choiceSecondaryTemplate, "narrative-dialogue-choice-secondary");
        if (line == null)
            return;
        line.text = text;
        line.AddToClassList(className);
        choicesContainer.Add(line);
    }

    private void ScrollToLatest()
    {
        if (textScroll == null || historyContainer == null)
            return;
        textScroll.schedule.Execute(() =>
        {
            if (historyContainer.childCount > 0 && textScroll.panel != null)
                textScroll.ScrollTo(historyContainer.ElementAt(historyContainer.childCount - 1));
        }).StartingIn(1);
    }

    private static T InstantiateFlat<T>(VisualTreeAsset template, string rootName) where T : VisualElement
    {
        if (template == null)
            return null;
        T element = template.Instantiate().Q<T>(rootName);
        element?.RemoveFromHierarchy();
        return element;
    }

    // ------------------------------------------------------------------
    // Проверка диалога — строка справа в панели и список по клику.
    // ------------------------------------------------------------------

    private void RefreshIssues()
    {
        issues.Clear();
        if (database != null && !string.IsNullOrEmpty(dialogueId))
            database.CollectValidationIssuesForDialogue(dialogueId, issues);

        if (checkSummary == null)
            return;
        checkSummary.text = issues.Count == 0 ? "✓ Ошибок нет" : "⚠ Ошибок: " + issues.Count + " ▾";
        checkSummary.style.color = issues.Count == 0 ? new Color(0.55f, 0.8f, 0.55f) : new Color(0.95f, 0.7f, 0.3f);
        checkSummary.tooltip = issues.Count == 0 ? string.Empty : "Показать список";

        issuesPanel.Clear();
        foreach (string issue in issues)
            issuesPanel.Add(new HelpBox(issue, HelpBoxMessageType.Warning));
        if (issues.Count == 0)
            issuesPanel.style.display = DisplayStyle.None;
    }

    private void OnDisable()
    {
        if (session != null && session.IsActive)
            session.End();
    }
}

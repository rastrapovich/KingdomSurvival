using System.Collections;
using UnityEngine;
using UnityEngine.UIElements;

public partial class PrototypeUIController
{
    private NarrativeDialogueSession narrativeDialogueSession;
    private VisualElement narrativeDialogueOverlay;
    private Label narrativeSpeakerLabel;
    private Label narrativeRoleLabel;
    private Label narrativeTextLabel;
    private VisualElement narrativeChoicesContainer;

    private bool IsNarrativeDialogueActive =>
        narrativeDialogueSession != null && narrativeDialogueSession.IsActive;

    private void Awake()
    {
        narrativeDialogueSession = new NarrativeDialogueSession();

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        StartCoroutine(InstallNarrativeDebugTrigger());
#endif
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    private IEnumerator InstallNarrativeDebugTrigger()
    {
        // PrototypeUIController.Debug builds its menu in Start().
        yield return null;

        if (!DebugToolsAvailable || debugPanel == null)
            yield break;

        ScrollView scroll = debugPanel.Q<ScrollView>(className: "debug-scroll");
        if (scroll == null)
            yield break;

        AddDebugSectionTitle(scroll, "НАРРАТИВ");

        Button narrativeButton = CreateDebugActionButton(
            "ОТКРЫТЬ ТЕСТОВЫЙ ДИАЛОГ",
            DebugOpenNarrativePrototype);
        scroll.Add(narrativeButton);
    }
#endif

    private void DebugOpenNarrativePrototype()
    {
        if (debugPanel != null)
            debugPanel.style.display = DisplayStyle.None;

        if (!TryOpenNarrativeDialogue(CreateNarrativePrototypeDialogue()))
            AddReport("[DEBUG] Тестовый диалог сейчас нельзя открыть.");
    }

    public bool TryOpenNarrativeDialogue(NarrativeDialogueDefinition dialogue)
    {
        if (dialogue == null ||
            gameState == null ||
            isGameOver ||
            IsNarrativeDialogueActive ||
            HasBlockingModalWork())
        {
            return false;
        }

        if (!EnsureNarrativeDialogueUi())
            return false;

        narrativeDialogueSession.Start(dialogue);
        PauseForBlockingModal();
        narrativeDialogueOverlay.style.display = DisplayStyle.Flex;
        RenderNarrativeDialogueNode();

        if (timeToggleButton != null)
        {
            timeToggleButton.SetEnabled(false);
            timeToggleButton.tooltip = "Сначала завершите разговор";
        }

        return true;
    }

    private bool EnsureNarrativeDialogueUi()
    {
        if (narrativeDialogueOverlay != null &&
            narrativeDialogueOverlay.panel != null)
        {
            return true;
        }

        if (interfaceRoot == null)
            return false;

        VisualElement screen = interfaceRoot.Q<VisualElement>("screen");
        if (screen == null)
            return false;

        StyleSheet narrativeStyle = Resources.Load<StyleSheet>("Prototype_Narrative");
        if (narrativeStyle == null)
        {
            Debug.LogError(
                "Narrative UI: не найден Resources/Prototype_Narrative.uss.");
            return false;
        }

        interfaceRoot.styleSheets.Add(narrativeStyle);

        narrativeDialogueOverlay = new VisualElement
        {
            name = "narrative-dialogue-overlay",
            pickingMode = PickingMode.Position
        };
        narrativeDialogueOverlay.AddToClassList("narrative-dialogue-overlay");

        VisualElement portrait = new VisualElement
        {
            name = "narrative-dialogue-portrait"
        };
        portrait.AddToClassList("narrative-dialogue-portrait");

        Label portraitPlaceholder = new Label("ПОРТРЕТ\nПЕРСОНАЖА");
        portraitPlaceholder.AddToClassList("narrative-dialogue-portrait-placeholder");
        portrait.Add(portraitPlaceholder);

        VisualElement panel = new VisualElement();
        panel.AddToClassList("narrative-dialogue-panel");

        narrativeSpeakerLabel = new Label();
        narrativeSpeakerLabel.AddToClassList("narrative-dialogue-speaker");
        panel.Add(narrativeSpeakerLabel);

        narrativeRoleLabel = new Label();
        narrativeRoleLabel.AddToClassList("narrative-dialogue-role");
        panel.Add(narrativeRoleLabel);

        VisualElement divider = new VisualElement();
        divider.AddToClassList("narrative-dialogue-divider");
        panel.Add(divider);

        ScrollView textScroll = new ScrollView(ScrollViewMode.Vertical);
        textScroll.AddToClassList("narrative-dialogue-text-scroll");

        narrativeTextLabel = new Label();
        narrativeTextLabel.AddToClassList("narrative-dialogue-text");
        textScroll.Add(narrativeTextLabel);
        panel.Add(textScroll);

        narrativeChoicesContainer = new VisualElement();
        narrativeChoicesContainer.AddToClassList("narrative-dialogue-choices");
        panel.Add(narrativeChoicesContainer);

        narrativeDialogueOverlay.Add(panel);
        narrativeDialogueOverlay.Add(portrait);
        screen.Add(narrativeDialogueOverlay);
        narrativeDialogueOverlay.style.display = DisplayStyle.None;
        return true;
    }

    private void RenderNarrativeDialogueNode()
    {
        if (!IsNarrativeDialogueActive)
            return;

        NarrativeDialogueNode node = narrativeDialogueSession.CurrentNode;
        narrativeSpeakerLabel.text = node.Speaker;
        narrativeRoleLabel.text = node.Role;
        narrativeTextLabel.text = node.Text;

        narrativeChoicesContainer.Clear();

        for (int i = 0; i < node.Choices.Count; i++)
        {
            int choiceIndex = i;
            NarrativeDialogueChoice choice = node.Choices[i];

            Button button = new Button(
                () => OnNarrativeDialogueChoiceSelected(choiceIndex))
            {
                text = choice.Text
            };
            button.AddToClassList("narrative-dialogue-choice");

            if (choice.EndsDialogue)
                button.AddToClassList("narrative-dialogue-choice-exit");

            narrativeChoicesContainer.Add(button);
        }
    }

    private void OnNarrativeDialogueChoiceSelected(int choiceIndex)
    {
        if (!IsNarrativeDialogueActive)
            return;

        bool continues = narrativeDialogueSession.SelectChoice(choiceIndex);
        if (!continues)
        {
            CloseNarrativeDialogue();
            return;
        }

        RenderNarrativeDialogueNode();
    }

    private void CloseNarrativeDialogue()
    {
        if (narrativeDialogueSession != null &&
            narrativeDialogueSession.IsActive)
        {
            narrativeDialogueSession.End();
        }

        if (narrativeDialogueOverlay != null)
            narrativeDialogueOverlay.style.display = DisplayStyle.None;

        if (narrativeChoicesContainer != null)
            narrativeChoicesContainer.Clear();

        RefreshTimeControlAvailability();
        ResumeAfterBlockingModalIfReady();
    }

    private static NarrativeDialogueDefinition CreateNarrativePrototypeDialogue()
    {
        const string speaker = "Мельник";
        const string role = "Житель Дома · прототип";

        return new NarrativeDialogueDefinition(
            "prototype_miller",
            "opening",
            new NarrativeDialogueNode(
                "opening",
                speaker,
                role,
                "Плотину после паводка повело сильнее, чем я думал. " +
                "Вода уходит не туда, куда должна.",
                new NarrativeDialogueChoice(
                    "Покажи, где заметили проблему.",
                    "details"),
                new NarrativeDialogueChoice(
                    "Что могло это вызвать?",
                    "cause"),
                NarrativeDialogueChoice.Exit(
                    "Вернёмся к этому позже.")),
            new NarrativeDialogueNode(
                "details",
                speaker,
                role,
                "Сначала просели опоры у старого шлюза. Рабочие говорят, " +
                "что утром вода уже шла ниже обычного.",
                new NarrativeDialogueChoice(
                    "А следы вокруг проверяли?",
                    "tracks"),
                new NarrativeDialogueChoice(
                    "Что ты сам думаешь?",
                    "cause"),
                NarrativeDialogueChoice.Exit(
                    "Пока достаточно.")),
            new NarrativeDialogueNode(
                "cause",
                speaker,
                role,
                "После паводка всякое бывает. Но здесь слишком многое " +
                "изменилось за одну ночь. Я бы сперва осмотрел старую часть плотины.",
                new NarrativeDialogueChoice(
                    "Покажи старую часть плотины.",
                    "old_dam"),
                new NarrativeDialogueChoice(
                    "Рабочие что-нибудь заметили?",
                    "workers"),
                new NarrativeDialogueChoice(
                    "Вернёмся к тому, что ты видел.",
                    "details"),
                NarrativeDialogueChoice.Exit(
                    "Ясно. Продолжим позже.")),
            new NarrativeDialogueNode(
                "tracks",
                speaker,
                role,
                "Следы есть, но после паводка двор весь в грязи. " +
                "Один рабочий уверяет, что видел свежие отметины у настила.",
                new NarrativeDialogueChoice(
                    "Позови тех, кто работал ночью.",
                    "workers"),
                new NarrativeDialogueChoice(
                    "Сначала хочу ещё раз услышать всё с начала.",
                    "opening"),
                NarrativeDialogueChoice.Exit(
                    "Этого пока хватит.")),
            new NarrativeDialogueNode(
                "workers",
                speaker,
                role,
                "Один говорит, что вода резко поднялась. Другой — что она, " +
                "наоборот, ушла. Оба стояли здесь в одну и ту же ночь.",
                new NarrativeDialogueChoice(
                    "Значит, осмотрим всё по порядку.",
                    "old_dam"),
                NarrativeDialogueChoice.Exit(
                    "Я поговорю с ними позже.")),
            new NarrativeDialogueNode(
                "old_dam",
                speaker,
                role,
                "Начни со старого шлюза. Там дерево темнее и крепёж старый — " +
                "сразу увидишь. Потом сравни с новыми опорами.",
                NarrativeDialogueChoice.Exit(
                    "Хорошо. Я осмотрю."),
                new NarrativeDialogueChoice(
                    "Ещё один вопрос.",
                    "opening")));
    }
}

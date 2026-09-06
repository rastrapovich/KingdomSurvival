using System.Collections;
using KingdomSurvival.DialogueDatabase;
using KingdomSurvival.UILayout;
using UnityEngine;
using UnityEngine.UIElements;

public partial class PrototypeUIController
{
    private NarrativeDialogueSession narrativeDialogueSession;
    private DialogueDatabaseAsset narrativeDialogueDatabase;
    private VisualElement narrativeDialogueOverlay;
    private VisualElement narrativePortrait;
    private VisualElement narrativePanel;
    private Label narrativePortraitPlaceholder;
    private Label narrativeSpeakerLabel;
    private Label narrativeRoleLabel;
    private Label narrativeTextLabel;
    private ScrollView narrativeTextScroll;
    private VisualElement narrativeChoicesContainer;

    private bool IsNarrativeDialogueActive => narrativeDialogueSession != null && narrativeDialogueSession.IsActive;

    private void Awake()
    {
        narrativeDialogueSession = new NarrativeDialogueSession();
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
        scroll.Add(CreateDebugActionButton("ОТКРЫТЬ ТЕСТОВЫЙ ДИАЛОГ", DebugOpenNarrativePrototype));
    }
#endif

    private void DebugOpenNarrativePrototype()
    {
        if (debugPanel != null)
            debugPanel.style.display = DisplayStyle.None;
        if (!TryOpenNarrativeDialogueById("prototype_miller"))
            AddReport("[DEBUG] Тестовый диалог сейчас нельзя открыть.");
    }

    public bool TryOpenNarrativeDialogueById(string dialogueId)
    {
        if (narrativeDialogueDatabase == null)
            narrativeDialogueDatabase = DialogueDatabaseRuntime.LoadDefaultDatabase();
        if (narrativeDialogueDatabase == null)
        {
            Debug.LogError("Narrative UI: не найдена база диалогов Resources/" + DialogueDatabaseAsset.ResourcesPath + ".asset");
            return false;
        }

        NarrativeDialogueDefinition dialogue;
        string error;
        if (!narrativeDialogueDatabase.TryBuildRuntime(dialogueId, out dialogue, out error))
        {
            Debug.LogError("Narrative UI: не удалось открыть диалог '" + dialogueId + "'.\n" + error);
            return false;
        }

        return TryOpenNarrativeDialogue(dialogue);
    }

    public bool TryOpenNarrativeDialogue(NarrativeDialogueDefinition dialogue)
    {
        if (dialogue == null || gameState == null || isGameOver || IsNarrativeDialogueActive || HasBlockingModalWork())
            return false;
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
        VisualElement divider = new VisualElement();
        divider.AddToClassList("narrative-dialogue-divider");
        narrativePanel.Add(divider);
        narrativeTextScroll = new ScrollView(ScrollViewMode.Vertical) { name = "narrative-dialogue-text" };
        narrativeTextScroll.AddToClassList("narrative-dialogue-text-scroll");
        narrativeTextLabel = new Label();
        narrativeTextLabel.AddToClassList("narrative-dialogue-text");
        narrativeTextScroll.Add(narrativeTextLabel);
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

        float actualWidth = screen.resolvedStyle.width;
        float actualHeight = screen.resolvedStyle.height;
        if (actualWidth <= 0f || actualHeight <= 0f)
        {
            actualWidth = database.ReferenceResolution.x;
            actualHeight = database.ReferenceResolution.y;
        }

        Vector2 reference = database.ReferenceResolution;
        Vector2 actual = new Vector2(actualWidth, actualHeight);
        ApplyLayoutElement(narrativeDialogueOverlay, layout.FindElement("overlay"), reference, actual);
        ApplyLayoutElement(narrativePanel, layout.FindElement("panel"), reference, actual);
        ApplyLayoutElement(narrativePortrait, layout.FindElement("portrait"), reference, actual);
        ApplyLayoutElement(narrativeSpeakerLabel, layout.FindElement("speaker"), reference, actual);
        ApplyLayoutElement(narrativeRoleLabel, layout.FindElement("role"), reference, actual);
        ApplyLayoutElement(narrativeTextScroll, layout.FindElement("text"), reference, actual);
        ApplyLayoutElement(narrativeChoicesContainer, layout.FindElement("choices"), reference, actual);
    }

    private static void ApplyLayoutElement(
        VisualElement target,
        UILayoutElementDefinition definition,
        Vector2 reference,
        Vector2 actual)
    {
        if (target == null || definition == null)
            return;
        UILayoutRuntimeApplier.ApplyRect(target, definition, reference, actual);
        UILayoutRuntimeApplier.ApplyBackground(target, definition);
    }

    private void RenderNarrativeDialogueNode()
    {
        if (!IsNarrativeDialogueActive)
            return;
        NarrativeDialogueNode node = narrativeDialogueSession.CurrentNode;
        narrativeSpeakerLabel.text = node.Speaker;
        narrativeRoleLabel.text = node.Role;
        narrativeTextLabel.text = node.Text;
        ApplyNarrativeSpeakerPortrait(node.SpeakerId);
        narrativeChoicesContainer.Clear();
        for (int i = 0; i < node.Choices.Count; i++)
        {
            int choiceIndex = i;
            NarrativeDialogueChoice choice = node.Choices[i];
            Button button = new Button(() => OnNarrativeDialogueChoiceSelected(choiceIndex)) { text = choice.Text };
            button.AddToClassList("narrative-dialogue-choice");
            if (choice.EndsDialogue)
                button.AddToClassList("narrative-dialogue-choice-exit");
            narrativeChoicesContainer.Add(button);
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
        if (narrativeDialogueSession != null && narrativeDialogueSession.IsActive)
            narrativeDialogueSession.End();
        if (narrativeDialogueOverlay != null)
            narrativeDialogueOverlay.style.display = DisplayStyle.None;
        if (narrativeChoicesContainer != null)
            narrativeChoicesContainer.Clear();
        RefreshTimeControlAvailability();
        ResumeAfterBlockingModalIfReady();
    }
}

using System.Collections.Generic;
using UnityEngine.UIElements;

// Бриф правок (22 пункта) п. 15: события похода, решения и результаты
// подаются тем же компонентом, что разговоры, — слоем диалога
// (narrative-dialogue-overlay), а не отдельной табличкой. Прежнее окно
// событий остаётся источником содержимого: код событий, решений и очереди
// заполняет его как раньше и навешивает свои обработчики, а показ целиком
// уходит в слой диалога — заголовок как говорящий, описание и последствие
// репликами, кнопки как варианты ответа. Нажатие варианта передаётся
// исходной кнопке, поэтому логика решений и подтверждений не меняется.
// Пока слой занят разговором или окном места, событие ждёт и открывается
// после них. Фон текущего места виден под мягким затемнением.
public partial class PrototypeUIController
{
    private bool eventDialogueShown;
    private bool eventDialoguePending;
    private bool eventDialogueSoftened;

    private bool IsIncidentModalRequested =>
        openedIncident != null || openedDecision != null || (activeQueuedModal != null && activeQueuedModal.Decision == null);

    private void PresentIncidentModalAsDialogue()
    {
        if (incidentModalOverlay == null || narrativeDialogueOverlay == null)
            return;
        incidentModalOverlay.style.display = DisplayStyle.None;
        // Слой занят разговором или окном места — событие подождёт.
        if (IsNarrativeDialogueActive || IsLocationInteractionActive)
        {
            eventDialoguePending = true;
            return;
        }
        eventDialoguePending = false;
        eventDialogueShown = true;

        if (narrativePortrait != null)
            narrativePortrait.style.display = DisplayStyle.None;
        if (narrativeSpeakerLabel != null)
            narrativeSpeakerLabel.text = incidentModalTitle.text;
        if (narrativeRoleLabel != null)
            narrativeRoleLabel.text = openedDecision != null ? "НУЖЕН ПРИКАЗ" : "СОБЫТИЕ";

        if (narrativeHistoryContainer != null)
        {
            narrativeHistoryContainer.Clear();
            AddEventDialogueLine(incidentModalDescription.text, false);
            AddEventDialogueLine(incidentModalConsequence.text, true);
        }

        if (narrativeChoicesContainer != null)
        {
            narrativeChoicesContainer.Clear();
            foreach (Button source in new[] { decisionOptionAButton, decisionOptionBButton, incidentUnderstoodButton })
            {
                if (source == null || source.style.display == DisplayStyle.None)
                    continue;
                Button choice = InstantiateFlatTemplate<Button>(LoadNarrativeChoiceButtonTemplate(), "narrative-dialogue-choice");
                if (choice == null)
                    continue;
                choice.name = "event-dialogue-choice-" + source.name;
                choice.text = source.text;
                choice.SetEnabled(source.enabledSelf);
                Button target = source;
                choice.clicked += () => PressEventSourceButton(target);
                narrativeChoicesContainer.Add(choice);
            }
        }

        if (!narrativeDialogueOverlay.ClassListContains(DialogueOverPlaceClass))
        {
            narrativeDialogueOverlay.AddToClassList(DialogueOverPlaceClass);
            eventDialogueSoftened = true;
        }
        narrativeDialogueOverlay.style.display = DisplayStyle.Flex;
        narrativeDialogueOverlay.BringToFront();
    }

    private void AddEventDialogueLine(string text, bool secondary)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;
        VisualElement block = new VisualElement();
        block.AddToClassList("narrative-dialogue-history-entry");
        Label label = new Label(text);
        label.AddToClassList("narrative-dialogue-history-text");
        if (secondary)
            label.AddToClassList("event-dialogue-consequence");
        block.Add(label);
        narrativeHistoryContainer.Add(block);
    }

    // Нажатие — та же исходная кнопка: все её обработчики (решение,
    // «понятно», обновление интерфейса) срабатывают как раньше.
    private static void PressEventSourceButton(Button source)
    {
        using (NavigationSubmitEvent submit = NavigationSubmitEvent.GetPooled())
        {
            submit.target = source;
            source.SendEvent(submit);
        }
    }

    private void HideEventDialogue()
    {
        eventDialoguePending = false;
        if (!eventDialogueShown)
            return;
        eventDialogueShown = false;
        if (narrativeDialogueOverlay != null && !IsNarrativeDialogueActive && !IsLocationInteractionActive)
            narrativeDialogueOverlay.style.display = DisplayStyle.None;
        if (eventDialogueSoftened)
        {
            narrativeDialogueOverlay?.RemoveFromClassList(DialogueOverPlaceClass);
            eventDialogueSoftened = false;
        }
        narrativeChoicesContainer?.Clear();
        narrativeHistoryContainer?.Clear();
        if (narrativePortrait != null)
            narrativePortrait.style.display = DisplayStyle.Flex;
    }

    // Отложенное событие — как только слой диалога освободился.
    private void TickEventDialogue()
    {
        if (!eventDialoguePending || IsNarrativeDialogueActive || IsLocationInteractionActive)
            return;
        if (!IsIncidentModalRequested)
        {
            eventDialoguePending = false;
            return;
        }
        PresentIncidentModalAsDialogue();
    }
}

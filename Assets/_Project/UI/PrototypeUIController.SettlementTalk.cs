using System.Collections.Generic;
using UnityEngine.UIElements;

// Брифы правок (19 пунктов) пп. 2, 10: короткий разговор поверх экрана
// поселения в том же слое диалога (без сцены Базы диалогов): реплики по
// очереди сверху, варианты — кнопками ответа. Им пользуются найм в корчме
// и разговор с торговцем («Покажи свой товар»). Открытие разговора времени
// не тратит; часы двигает только само дело (найм, подтверждённая сделка).
public partial class PrototypeUIController
{
    private bool settlementTalkOpen;
    private bool settlementTalkSoftened;

    private bool IsSettlementTalkOpen => settlementTalkOpen;

    private void ShowSettlementTalk(string speaker, string role, IEnumerable<(string Text, bool Secondary)> lines,
        IEnumerable<(string Name, string Text, bool Enabled, System.Action OnClick)> choices)
    {
        if (narrativeDialogueOverlay == null || IsNarrativeDialogueActive)
            return;
        settlementTalkOpen = true;
        if (narrativePortrait != null)
            narrativePortrait.style.display = DisplayStyle.None;
        if (narrativeSpeakerLabel != null)
            narrativeSpeakerLabel.text = speaker ?? string.Empty;
        if (narrativeRoleLabel != null)
            narrativeRoleLabel.text = role ?? string.Empty;
        if (narrativeHistoryContainer != null)
        {
            narrativeHistoryContainer.Clear();
            foreach ((string text, bool secondary) in lines)
                AddEventDialogueLine(text, secondary);
        }
        if (narrativeChoicesContainer != null)
        {
            narrativeChoicesContainer.Clear();
            foreach ((string name, string text, bool enabled, System.Action onClick) in choices)
            {
                Button button = InstantiateFlatTemplate<Button>(LoadNarrativeChoiceButtonTemplate(), "narrative-dialogue-choice");
                if (button == null)
                    continue;
                button.name = name;
                button.text = text;
                button.SetEnabled(enabled);
                System.Action captured = onClick;
                button.clicked += () => captured?.Invoke();
                narrativeChoicesContainer.Add(button);
            }
        }
        if (!narrativeDialogueOverlay.ClassListContains(DialogueOverPlaceClass))
        {
            narrativeDialogueOverlay.AddToClassList(DialogueOverPlaceClass);
            settlementTalkSoftened = true;
        }
        narrativeDialogueOverlay.style.display = DisplayStyle.Flex;
        narrativeDialogueOverlay.BringToFront();
    }

    private void CloseSettlementTalk()
    {
        if (!settlementTalkOpen)
            return;
        settlementTalkOpen = false;
        hireDialogueOpen = false;
        merchantTalkPersonId = null;
        if (narrativeDialogueOverlay != null)
            narrativeDialogueOverlay.style.display = DisplayStyle.None;
        if (settlementTalkSoftened)
        {
            narrativeDialogueOverlay?.RemoveFromClassList(DialogueOverPlaceClass);
            settlementTalkSoftened = false;
        }
        narrativeChoicesContainer?.Clear();
        narrativeHistoryContainer?.Clear();
        if (narrativePortrait != null)
            narrativePortrait.style.display = DisplayStyle.Flex;
    }

    // ------------------------------------------------------------------
    // Бриф правок (19 пунктов) п. 2: торговец как человек. Клик по нему —
    // разговор: кто он и чем торгует, отдельной репликой «Покажи свой
    // товар» — торговля тем же торговцем и тем же товаром, что у кнопки
    // лавки. После торговли из разговора — назад в этот разговор; из кнопки
    // лавки — назад на страницу поселения. Тексты — [РАБОЧЕЕ].
    // ------------------------------------------------------------------

    private string merchantTalkPersonId;
    // Торговля открыта из разговора с этим человеком — «Назад» вернёт к нему.
    private string tradeReturnPersonId;

    private void OpenMerchantTalk(SettlementPersonDefinition person)
    {
        SettlementProfileDefinition profile = SettlementService.Profile(gameState, settlementLocationId);
        MerchantDefinition merchant = profile?.FindMerchant(person?.MerchantId);
        if (person == null || merchant == null)
            return;
        bool talk = !string.IsNullOrEmpty(person.DialogueId) && SettlementDialogueExists(person.DialogueId);
        string personId = person.Id;
        string merchantId = merchant.Id;
        string dialogueId = person.DialogueId;

        List<(string, bool)> lines = new List<(string, bool)>();
        string role = string.IsNullOrEmpty(person.Role) ? merchant.Role : person.Role;
        if (!string.IsNullOrEmpty(person.Activity))
            lines.Add((person.Activity + ".", true));
        lines.Add(("— " + person.DisplayName + (string.IsNullOrEmpty(role) ? string.Empty : ", " + role) +
                   ". Чем богаты — то на прилавке, цену не прячу.", false));

        List<(string, string, bool, System.Action)> choices = new List<(string, string, bool, System.Action)>
        {
            ("merchant-talk-trade", "Покажи свой товар.", true, () =>
            {
                CloseSettlementTalk();
                tradeReturnPersonId = personId;
                OpenSettlementTrade(merchantId);
            })
        };
        if (talk)
        {
            choices.Add(("merchant-talk-dialogue", "Поговорим о другом.", true, () =>
            {
                CloseSettlementTalk();
                StartSettlementDialogue(dialogueId);
            }));
        }
        choices.Add(("merchant-talk-leave", "Пойду.", true, CloseSettlementTalk));

        ShowSettlementTalk(person.DisplayName, (role ?? string.Empty).ToUpperInvariant(), lines, choices);
        merchantTalkPersonId = personId;
    }

    // Возврат после торговли, открытой из разговора: тот же человек, если он
    // ещё здесь.
    private void ReturnToMerchantTalkAfterTrade()
    {
        string personId = tradeReturnPersonId;
        tradeReturnPersonId = null;
        if (string.IsNullOrEmpty(personId) || !IsSettlementScreenOpen)
            return;
        SettlementProfileDefinition profile = SettlementService.Profile(gameState, settlementLocationId);
        TavernDefinition tavern = TavernService.Tavern(gameState, settlementLocationId);
        SettlementPersonDefinition person = SettlementAllPeople(profile, tavern).Find(p => p.Id == personId);
        if (person != null)
            OpenMerchantTalk(person);
    }
}

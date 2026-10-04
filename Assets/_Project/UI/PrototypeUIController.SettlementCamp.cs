using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

// ПР-12К (канон v1.53 §28.9): лагерь у поселения в окне места (Location
// Interaction). Снаружи: войти всем отрядом, разбить лагерь и выбрать
// входящую группу («С вами» / «Останется в лагере» / «Не допускается:
// причина»), ночлег в лагере, собраться и продолжить путь. Внутри — обычное
// содержание места для тех, кто вошёл, и «Вернуться к лагерю». Состояние —
// GameState.SettlementCamp (SettlementCampService); здесь только показ.
public partial class PrototypeUIController
{
    private bool settlementPicking;
    // Вошли всем отрядом, без лагеря (только на время окна).
    private bool settlementInsideAll;
    private readonly HashSet<string> settlementEntering = new HashSet<string>(StringComparer.Ordinal);

    private bool IsSettlementInside =>
        settlementInsideAll || SettlementCampService.IsCommanderInside(gameState);

    // True — окно поселения нарисовано здесь (снаружи или выбор группы).
    private bool RenderSettlementChoices(LocationData location)
    {
        if (location == null || !location.IsSettlement || IsSettlementInside)
            return false;

        if (settlementPicking)
        {
            RenderSettlementPicker(location);
            return true;
        }

        bool hasCamp = SettlementCampService.HasCamp(gameState);
        List<string> blocked = new List<string>();
        foreach (string personId in SettlementCampService.Companions(gameState))
        {
            string reason = SettlementCampService.AdmissionBlockReason(gameState, personId);
            if (!string.IsNullOrEmpty(reason))
                blocked.Add(PersonName(personId) + " — " + reason);
        }

        if (!hasCamp)
        {
            AddInteractionChoice("ВОЙТИ ВСЕМ ОТРЯДОМ",
                blocked.Count > 0 ? "Не допускается: " + string.Join("; ", blocked) + "." : "Отряд входит вместе.",
                blocked.Count == 0,
                () =>
                {
                    settlementInsideAll = true;
                    RenderLocationInteractionChoices(location);
                });
            AddInteractionChoice("РАЗБИТЬ ЛАГЕРЬ СНАРУЖИ", "Выбрать, кто пойдёт с вами, а кто останется в лагере.", true,
                () => OpenSettlementPicker(location));
        }
        else
        {
            AddInteractionChoice("ВОЙТИ", "Выбрать, кто пойдёт с вами в поселение.", true,
                () => OpenSettlementPicker(location));
            CampRest.CanRest(gameState, out string restReason);
            AddInteractionChoice("НОЧЛЕГ В ЛАГЕРЕ", restReason, string.IsNullOrEmpty(restReason), () =>
            {
                CloseLocationInteraction();
                OpenCampScreen();
            });
            AddInteractionChoice("СОБРАТЬСЯ И ПРОДОЛЖИТЬ ПУТЬ", "Все живые снова вместе; лагерь свёрнут.", true, () =>
            {
                List<string> names = SettlementCampService.Gather(gameState);
                AddReport(names.Count > 0 ? "Отряд собрался: " + string.Join(", ", names) + " снова с вами." : "Лагерь свёрнут.");
                CloseLocationInteraction();
                Autosave();
            });
        }
        AddInteractionChoice("ОТМЕНИТЬ", "Остаться у поселения, ничего не предпринимая.", true, OnLocationInteractionCancelClicked);
        return true;
    }

    private void OpenSettlementPicker(LocationData location)
    {
        settlementPicking = true;
        settlementEntering.Clear();
        foreach (string personId in SettlementCampService.Companions(gameState))
        {
            bool waitedBefore = SettlementCampService.HasCamp(gameState) &&
                                gameState.SettlementCamp.WaitingIds.Contains(personId);
            if (string.IsNullOrEmpty(SettlementCampService.AdmissionBlockReason(gameState, personId)) && !waitedBefore)
                settlementEntering.Add(personId);
        }
        RenderLocationInteractionChoices(location);
    }

    private void RenderSettlementPicker(LocationData location)
    {
        CommanderData hero = gameState.GetSelectedCommander();
        AddInteractionChoice((hero != null ? hero.Name : "Командир") + " — с вами", "Командир всегда входит сам.", false, null);
        foreach (string personId in SettlementCampService.Companions(gameState))
        {
            string captured = personId;
            string block = SettlementCampService.AdmissionBlockReason(gameState, personId);
            if (!string.IsNullOrEmpty(block))
            {
                AddInteractionChoice(PersonName(personId) + " — в лагере", "Не допускается: " + block + ".", false, null);
                continue;
            }
            bool entering = settlementEntering.Contains(personId);
            AddInteractionChoice(PersonName(personId) + (entering ? " — с вами" : " — останется в лагере"),
                null, true, () =>
                {
                    if (!settlementEntering.Remove(captured))
                        settlementEntering.Add(captured);
                    RenderLocationInteractionChoices(location);
                });
        }

        AddInteractionChoice("ВОЙТИ", "Оставшиеся ждут у входа: они в походе, но не рядом с вами.", true, () =>
        {
            if (!SettlementCampService.Enter(gameState, settlementEntering, out string reason))
            {
                AddReport(reason);
                return;
            }
            settlementPicking = false;
            settlementInsideAll = !SettlementCampService.HasCamp(gameState);
            RenderLocationInteractionDescription(location);
            RenderLocationInteractionChoices(location);
            Autosave();
        });
        AddInteractionChoice("НАЗАД", null, true, () =>
        {
            settlementPicking = false;
            RenderLocationInteractionChoices(location);
        });
    }

    // Внутри поселения — к обычному содержанию места добавляется выход.
    private void AddSettlementInsideChoices(LocationData location)
    {
        if (location == null || !location.IsSettlement || !IsSettlementInside)
            return;
        if (SettlementCampService.IsCommanderInside(gameState))
        {
            AddInteractionChoice("ВЕРНУТЬСЯ К ЛАГЕРЮ", "Ожидающие снова рядом; группу можно сменить.", true, () =>
            {
                SettlementCampService.ReturnToCamp(gameState);
                RenderLocationInteractionDescription(location);
                RenderLocationInteractionChoices(location);
                Autosave();
            });
        }
        else
        {
            AddInteractionChoice("ВЫЙТИ ИЗ ПОСЕЛЕНИЯ", null, true, () =>
            {
                settlementInsideAll = false;
                RenderLocationInteractionChoices(location);
            });
        }
    }

    // Строка о лагере под описанием места.
    private string SettlementCampLine(LocationData location)
    {
        if (location == null || !location.IsSettlement || !SettlementCampService.HasCamp(gameState))
            return string.Empty;
        List<string> names = new List<string>();
        foreach (string personId in gameState.SettlementCamp.WaitingIds)
            names.Add(PersonName(personId));
        if (names.Count == 0)
            return "Лагерь у входа.";
        return SettlementCampService.IsCommanderInside(gameState)
            ? "В лагере ждут: " + string.Join(", ", names) + "."
            : "Вы у лагеря; с вами снова: " + string.Join(", ", names) + ".";
    }

    private void ResetSettlementView()
    {
        settlementPicking = false;
        settlementInsideAll = false;
        settlementEntering.Clear();
    }

    private string PersonName(string personId)
    {
        ResidentState resident = HomePeopleService.Find(gameState, personId);
        return resident != null && !string.IsNullOrEmpty(resident.DisplayName) ? resident.DisplayName : personId;
    }

    private void AddInteractionChoice(string text, string hint, bool enabled, Action onClick)
    {
        Button button = InstantiateFlatTemplate<Button>(LoadNarrativeChoiceButtonTemplate(), "narrative-dialogue-choice");
        if (button == null)
            return;
        button.text = text;
        button.SetEnabled(enabled);
        if (onClick != null)
            button.clicked += onClick;
        narrativeChoicesContainer.Add(button);
        if (string.IsNullOrWhiteSpace(hint))
            return;
        Label label = InstantiateFlatTemplate<Label>(LoadNarrativeChoiceSecondaryTemplate(), "narrative-dialogue-choice-secondary");
        if (label == null)
            return;
        label.text = hint;
        label.AddToClassList("narrative-dialogue-choice-hint");
        narrativeChoicesContainer.Add(label);
    }
}

using System.Collections.Generic;

// Бриф правок (19 пунктов) пп. 10, 18: найм — только разговором в корчме.
// В корчме под «Ищут работу» — кандидаты (бывает, что никого); нажатие
// открывает разговор в слое диалога поверх поселения: кто он, чем полезен и
// сколько просит в игровые сутки, затем «Нанять» или «Не сейчас». Нанятый
// встаёт на свободное место бойца; ПКМ по его карточке в «Отряде» —
// характеристики и та же плата.
public partial class PrototypeUIController
{
    private bool hireDialogueOpen;

    private void RenderHireCandidates()
    {
        List<HireCandidateData> candidates = HireService.Candidates(gameState, settlementLocationId);
        if (candidates.Count == 0)
            return;
        AddSettlementSection("Ищут работу");
        foreach (HireCandidateData candidate in candidates)
        {
            HireCandidateData captured = candidate;
            AddSettlementAction(candidate.Name + " — " + candidate.RoleLabel,
                candidate.Benefit + " · " + candidate.DailyWage + " зол. в сутки", true, () => OpenHireDialogue(captured), false);
        }
    }

    private void OpenHireDialogue(HireCandidateData candidate)
    {
        if (candidate == null || narrativeDialogueOverlay == null || IsNarrativeDialogueActive)
            return;
        bool canHire = HireService.CanHire(gameState, settlementLocationId, candidate.Id, out string reason);
        int free = gameState.HasActiveExpedition ? GameState.ExpeditionFighterSlots - gameState.ActiveExpedition.FighterIds.Count : 0;
        string candidateId = candidate.Id;
        ShowSettlementTalk(candidate.Name, "ИЩЕТ РАБОТУ · " + candidate.RoleLabel.ToUpperInvariant(),
            new List<(string, bool)>
            {
                ("— Возьмёте с собой — пойду. Я " + candidate.RoleLabel + ": " + candidate.Benefit + ".", false),
                ("Плата — " + candidate.DailyWage + " зол. за игровые сутки, пока идёт с отрядом. Свободных мест в отряде: " +
                 System.Math.Max(0, free) + "." + (canHire ? string.Empty : " " + reason), true)
            },
            new List<(string, string, bool, System.Action)>
            {
                ("hire-dialogue-accept", "Нанять — " + candidate.DailyWage + " зол. в сутки", canHire, () => ConfirmHire(candidateId)),
                ("hire-dialogue-decline", "Не сейчас", true, CloseHireDialogue)
            });
        hireDialogueOpen = true;
    }

    private void ConfirmHire(string candidateId)
    {
        bool hired = HireService.TryHire(gameState, settlementLocationId, candidateId, out string message);
        AddReport(message);
        CloseHireDialogue();
        if (hired)
        {
            // Разговор закончен делом — один шаг часов поселения (п. 14 прошлого пакета).
            SpendSettlementActionTime();
            Autosave();
        }
        ShowSettlementMessage(message);
        RenderSettlement();
    }

    private void CloseHireDialogue()
    {
        if (!hireDialogueOpen)
            return;
        CloseSettlementTalk();
    }

    // ПКМ по бойцу отряда: характеристики (тот же расчёт, что в бою) и плата.
    private void ShowRosterPersonInfo(string personId)
    {
        ResidentState resident = PeopleService.Find(gameState, personId);
        if (resident == null)
            return;
        AssembledCombatStats stats = CombatStatsAssembler.Compute(gameState, personId);
        string text = string.Empty;
        if (stats.HasTemplate)
        {
            UnitCombatStats f = stats.Final;
            text = "Жизни " + resident.CurrentHitPoints + " / " + f.MaxHitPoints + " · атака " + f.Attack + " · защита " + f.Defense +
                   " · урон " + f.Damage + " · ход " + f.Movement + " · инициатива " + f.Initiative + " · дальность " + f.AttackRange + ".";
        }
        HireContractData contract = HireService.Contract(gameState, personId);
        if (contract != null)
            text += "\n\nПлата: " + contract.DailyWage + " зол. за игровые сутки.";
        if (!string.IsNullOrWhiteSpace(resident.ShortDescription))
            text = resident.ShortDescription + "\n\n" + text;
        ShowHeroInfoPopup(resident.DisplayName, resident.RoleLabel, text);
    }
}

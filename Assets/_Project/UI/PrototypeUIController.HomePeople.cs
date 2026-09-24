using System.Collections.Generic;
using System.Text;
using KingdomSurvival.Chapter01;
using UnityEngine;
using UnityEngine.UIElements;

// ПР-06А: люди Дома в интерфейсе — блок «Люди Дома» на экране Дома
// (сводка, функции, работа по настилу, жители) и выбор свиты при
// подготовке похода с прогнозом, что потеряет Дом. Данные — только из
// реестра (HomePeopleService / HomeFunctionResolver / HomeLife).
public partial class PrototypeUIController
{
    private const string HomePeopleScreenName = "Люди Дома";
    private const string RetinueSelectedClass = "hero-screen-retinue-option--selected";

    private Label homePeopleSummary;
    private Label homePeopleMaintenance;
    private Label homePeopleCare;
    private VisualElement homePeopleWorkRow;
    private Label homePeopleWorkLabel;
    private Button homePeopleWorkButton;
    private VisualElement homePeopleList;
    private Label homePeopleDetail;
    private VisualTreeAsset personRowTemplate;
    private bool homePeopleBindAttempted;
    private bool homePeopleBound;
    private string homePeopleSignature;

    private Button retinueNoneButton;
    private Button retinueOstafiyButton;
    private Button retinueLadaButton;
    private Label retinueDescription;
    private Label retinueForecast;
    private bool retinueBound;
    private string retinueSignature;

    // Выбор свиты в подготовке (как selectedFighterIds для бойцов).
    private string selectedRetinueId;

    private void RefreshHomePeopleUi()
    {
        if (gameState == null || interfaceRoot == null)
            return;

        BindHomePeople();
        BindRetinue();
        if (homePeopleBound)
            RefreshHomePeopleIfChanged();
        if (retinueBound)
            RefreshRetinueIfChanged();
    }

    // ------------------------------------------------------------------
    // Блок «Люди Дома»
    // ------------------------------------------------------------------

    private void BindHomePeople()
    {
        if (homePeopleBindAttempted)
            return;
        homePeopleBindAttempted = true;

        homePeopleSummary = BindRequiredElement<Label>(interfaceRoot, HomePeopleScreenName, "home-people-summary");
        homePeopleMaintenance = BindRequiredElement<Label>(interfaceRoot, HomePeopleScreenName, "home-people-maintenance");
        homePeopleCare = BindRequiredElement<Label>(interfaceRoot, HomePeopleScreenName, "home-people-care");
        homePeopleWorkRow = BindRequiredElement<VisualElement>(interfaceRoot, HomePeopleScreenName, "home-people-work-row");
        homePeopleWorkLabel = BindRequiredElement<Label>(interfaceRoot, HomePeopleScreenName, "home-people-work-label");
        homePeopleWorkButton = BindRequiredElement<Button>(interfaceRoot, HomePeopleScreenName, "home-people-work-button");
        homePeopleList = BindRequiredElement<VisualElement>(interfaceRoot, HomePeopleScreenName, "home-people-list");
        homePeopleDetail = BindRequiredElement<Label>(interfaceRoot, HomePeopleScreenName, "home-people-detail");

        homePeopleBound = homePeopleSummary != null && homePeopleMaintenance != null && homePeopleCare != null &&
                          homePeopleWorkRow != null && homePeopleWorkLabel != null && homePeopleWorkButton != null &&
                          homePeopleList != null && homePeopleDetail != null;
        if (homePeopleBound)
            homePeopleWorkButton.clicked += OnYardDeckClicked;
    }

    // Пересборка только при изменении: подпись — состояния людей, функции,
    // работа (с точностью до часа).
    private void RefreshHomePeopleIfChanged()
    {
        HomeFunctionReport maintenance = HomeFunctionResolver.Resolve(gameState, HomeFunctionResolver.MaintenanceId);
        HomeFunctionReport care = HomeFunctionResolver.Resolve(gameState, HomeFunctionResolver.CareId);
        HomeWorkState deck = HomeLife.FindWork(gameState, HomeLife.YardDeckWorkId);

        StringBuilder signature = new StringBuilder();
        foreach (ResidentState resident in HomePeopleService.All(gameState))
        {
            signature.Append(resident.PersonId).Append(':').Append((int)resident.LifeStatus)
                .Append((int)resident.Injury).Append(HomePeopleService.IsInExpedition(gameState, resident.PersonId) ? 'E' : 'H')
                .Append(resident.CurrentHitPoints).Append('|');
        }
        signature.Append(maintenance.Status).Append(maintenance.ExecutorId).Append(care.Status).Append(care.ExecutorId);
        signature.Append(deck == null ? "-" : deck.Completed ? "done" : ((int)HomeLife.RemainingWork(deck)).ToString());
        signature.Append(Chapter01HomeActivities.IsYardDeckOffered(gameState)).Append(gameState.Gold >= HomeLife.YardDeckGoldCost);
        signature.Append(Chapter01FisherFamily.IsOffered(gameState));

        string current = signature.ToString();
        if (current == homePeopleSignature)
            return;
        homePeopleSignature = current;

        int members = HomePeopleService.CountHomeMembers(gameState);
        int present = HomePeopleService.CountHomePresent(gameState);
        homePeopleSummary.text = members + " жителей · дома " + present + " · в походе " + (members - present);
        homePeopleMaintenance.text = FunctionLine("Ремонт", maintenance);
        homePeopleCare.text = FunctionLine("Уход за ранеными", care);

        RefreshYardDeckRow(deck, maintenance);
        RebuildPeopleList();
    }

    private static string FunctionLine(string title, HomeFunctionReport report)
    {
        switch (report.Status)
        {
            case HomeFunctionStatus.Working:
                return title + ": работает — " + report.ExecutorName + ".";
            case HomeFunctionStatus.Limited:
                return title + ": ограничено — " + report.Reason + ".";
            default:
                return title + ": остановлено — " + report.Reason + ".";
        }
    }

    private void RefreshYardDeckRow(HomeWorkState deck, HomeFunctionReport maintenance)
    {
        bool offered = Chapter01HomeActivities.IsYardDeckOffered(gameState);
        if (deck == null && !offered)
        {
            homePeopleWorkRow.style.display = DisplayStyle.None;
            return;
        }

        homePeopleWorkRow.style.display = DisplayStyle.Flex;
        if (deck == null)
        {
            homePeopleWorkLabel.text = "Хозяйственный настил во дворе разбит паводком. По нему обходят лужи.";
            homePeopleWorkButton.style.display = DisplayStyle.Flex;
            homePeopleWorkButton.text = "Восстановить · " + HomeLife.YardDeckGoldCost + " золота";
            homePeopleWorkButton.SetEnabled(gameState.Gold >= HomeLife.YardDeckGoldCost);
            return;
        }

        homePeopleWorkButton.style.display = DisplayStyle.None;
        if (deck.Completed)
        {
            homePeopleWorkLabel.text = "Хозяйственный настил восстановлен.";
            return;
        }

        double remaining = HomeLife.RemainingWork(deck);
        double rate = maintenance.Rate;
        homePeopleWorkLabel.text = rate > 0.0
            ? "Ремонт настила идёт: осталось около " + Mathf.CeilToInt((float)(remaining / rate)) + " ч."
            : "Ремонт настила приостановлен: " + maintenance.Reason + ".";
    }

    private void OnYardDeckClicked()
    {
        if (gameState == null)
            return;

        HomeLife.TryStartYardDeck(gameState, out string message);
        AddReport(message);
        homePeopleSignature = null;
        RefreshInterface();
    }

    private void RebuildPeopleList()
    {
        homePeopleList.Clear();
        if (personRowTemplate == null)
            personRowTemplate = Resources.Load<VisualTreeAsset>("Templates/PersonRow");
        if (personRowTemplate == null)
            return;

        // ПР-06Б: семья у ворот — отдельная строка до ответа; подробности
        // (кто, что умеет, что изменится) — в детали по клику, решение —
        // в сцене «Люди у ворот» из дел Дома.
        if (Chapter01FisherFamily.IsOffered(gameState))
        {
            TemplateContainer offer = personRowTemplate.Instantiate();
            Button offerRow = offer.Q<Button>("person-row");
            offerRow.Q<Label>("person-row-name").text = "Семья Тихона";
            offerRow.Q<Label>("person-row-role").text = "у ворот · " + Chapter01FisherFamily.MemberCount + " человека";
            offerRow.Q<Label>("person-row-status").text = "ждёт ответа";
            offerRow.clicked += () => homePeopleDetail.text = Chapter01FisherFamily.BuildOfferSummary(gameState) +
                                                              " Ответить — в делах Дома: «Люди у ворот».";
            homePeopleList.Add(offer);
        }

        int background = 0;
        foreach (ResidentState resident in HomePeopleService.All(gameState))
        {
            if (resident.PersonId.StartsWith("home.background.", System.StringComparison.Ordinal))
            {
                if (resident.IsHomeMember)
                    background++;
                continue;
            }

            TemplateContainer instance = personRowTemplate.Instantiate();
            Button row = instance.Q<Button>("person-row");
            row.Q<Label>("person-row-name").text = resident.DisplayName;
            row.Q<Label>("person-row-role").text = resident.RoleLabel;
            row.Q<Label>("person-row-status").text = PersonStatus(resident);
            row.EnableInClassList("person-row--away", HomePeopleService.IsInExpedition(gameState, resident.PersonId));
            row.EnableInClassList("person-row--dead", !resident.IsAlive);

            ResidentState captured = resident;
            row.clicked += () => ShowPersonDetail(captured);
            homePeopleList.Add(instance);
        }

        homePeopleDetail.text = "Другие семьи — " + background + " жителей.";
    }

    private string PersonStatus(ResidentState resident)
    {
        if (!resident.IsAlive)
            return "погиб(ла)";
        if (resident.Membership != ResidentMembership.HomeMember)
            return "ушёл(ла) из Дома";

        string place = HomePeopleService.IsInExpedition(gameState, resident.PersonId) ? "в походе" : "дома";
        if (resident.Injury == ResidentInjury.Recovering)
            place += " · ранен(а)";
        if (resident.HasCombatState && resident.CurrentHitPoints < resident.MaxHitPoints)
            place += " · " + resident.CurrentHitPoints + "/" + resident.MaxHitPoints + " HP";
        return place;
    }

    private void ShowPersonDetail(ResidentState resident)
    {
        homePeopleDetail.text = resident.DisplayName + " — " + resident.RoleLabel + ". " + resident.ShortDescription;
    }

    // ------------------------------------------------------------------
    // Свита в подготовке похода
    // ------------------------------------------------------------------

    private void BindRetinue()
    {
        if (retinueBound || interfaceRoot == null)
            return;

        retinueNoneButton = interfaceRoot.Q<Button>("hero-screen-retinue-none");
        retinueOstafiyButton = interfaceRoot.Q<Button>("hero-screen-retinue-ostafiy");
        retinueLadaButton = interfaceRoot.Q<Button>("hero-screen-retinue-lada");
        retinueDescription = interfaceRoot.Q<Label>("hero-screen-retinue-description");
        retinueForecast = interfaceRoot.Q<Label>("hero-screen-retinue-forecast");
        if (retinueNoneButton == null || retinueOstafiyButton == null || retinueLadaButton == null ||
            retinueDescription == null || retinueForecast == null)
            return;

        retinueNoneButton.clicked += () => SelectRetinue(null);
        retinueOstafiyButton.clicked += () => SelectRetinue(HomePeopleService.OstafiyId);
        retinueLadaButton.clicked += () => SelectRetinue(HomePeopleService.LadaId);
        retinueBound = true;
    }

    private void SelectRetinue(string personId)
    {
        if (!string.IsNullOrEmpty(personId) && !HomePeopleService.CanJoinAsRetinue(gameState, personId, out string reason))
        {
            AddReport("Нельзя взять в свиту: " + reason + ".");
            return;
        }

        selectedRetinueId = personId;
        // Если поход уже подготовлен и ещё не тронулся — меняем его свиту.
        if (gameState.HasActiveExpedition && ContinuousPreparationCommands.CanEditPreparedRoster(gameState))
            ContinuousPreparationCommands.TrySetPreparedRetinue(gameState, personId, out _);
        retinueSignature = null;
    }

    private void ApplySelectedRetinueToExpedition()
    {
        if (gameState == null || !gameState.HasActiveExpedition)
            return;

        if (!ContinuousPreparationCommands.TrySetPreparedRetinue(gameState, selectedRetinueId, out string message) &&
            !string.IsNullOrEmpty(selectedRetinueId))
        {
            AddReport(message);
        }
    }

    private void RefreshRetinueIfChanged()
    {
        // Недоступного больше специалиста (погиб, ранен) из выбора убираем.
        if (!string.IsNullOrEmpty(selectedRetinueId) &&
            !HomePeopleService.CanJoinAsRetinue(gameState, selectedRetinueId, out _) &&
            !HomePeopleService.IsInExpedition(gameState, selectedRetinueId))
        {
            selectedRetinueId = null;
        }

        List<string> leaving = new List<string>(selectedFighterIds);
        if (!string.IsNullOrEmpty(selectedRetinueId))
            leaving.Add(selectedRetinueId);

        string current = (selectedRetinueId ?? "-") + "|" + string.Join(",", leaving) + "|" + homePeopleSignature;
        if (current == retinueSignature)
            return;
        retinueSignature = current;

        retinueNoneButton.EnableInClassList(RetinueSelectedClass, string.IsNullOrEmpty(selectedRetinueId));
        retinueOstafiyButton.EnableInClassList(RetinueSelectedClass, selectedRetinueId == HomePeopleService.OstafiyId);
        retinueLadaButton.EnableInClassList(RetinueSelectedClass, selectedRetinueId == HomePeopleService.LadaId);
        retinueOstafiyButton.SetEnabled(HomePeopleService.CanJoinAsRetinue(gameState, HomePeopleService.OstafiyId, out _));
        retinueLadaButton.SetEnabled(HomePeopleService.CanJoinAsRetinue(gameState, HomePeopleService.LadaId, out _));

        ResidentState chosen = HomePeopleService.Find(gameState, selectedRetinueId);
        retinueDescription.text = chosen != null
            ? chosen.DisplayName + ": " + chosen.ShortDescription
            : "Отряд уйдёт без специалиста. Свита не идёт в бой, но ест из припасов похода.";

        HomeFunctionReport maintenance = HomeFunctionResolver.Forecast(gameState, HomeFunctionResolver.MaintenanceId, leaving);
        HomeFunctionReport care = HomeFunctionResolver.Forecast(gameState, HomeFunctionResolver.CareId, leaving);
        retinueForecast.text = "Дома после выхода: " + FunctionLine("ремонт", maintenance) + " " +
                               FunctionLine("уход", care) + " Припасы похода: " +
                               (1 + selectedFighterIds.Count + (chosen != null ? 1 : 0)) + " в сутки.";
    }
}

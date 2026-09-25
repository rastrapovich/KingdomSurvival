using System.Collections.Generic;
using System.Text;
using KingdomSurvival.Chapter01;
using UnityEngine;
using UnityEngine.UIElements;

// ПР-06А / ПР-07А-2: люди Дома в интерфейсе — блок «Люди Дома» на экране
// Дома (сводка, строки ремонта и ухода в «Заботах», жители, семьи) и выбор
// свиты на экране героя. Данные — только из реестра (HomePeopleService /
// HomeFunctionResolver / HomeLife). Ремонт настила — карточка «Заботы»
// (PrototypeUIController.HomeScreen.cs).
public partial class PrototypeUIController
{
    private const string HomePeopleScreenName = "Люди Дома";
    private const string RetinueSelectedClass = "hero-screen-retinue-option--selected";

    private Label homePeopleSummary;
    private Label homePeopleMaintenance;
    private Label homePeopleCare;
    private Label homePeopleFishing;
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

    private void RefreshHomePeopleUi()
    {
        if (gameState == null || interfaceRoot == null)
            return;

        DeliverHomeNews();
        BindHomePeople();
        BindRetinue();
        if (homePeopleBound)
            RefreshHomePeopleIfChanged();
        if (retinueBound)
            RefreshRetinueIfChanged();
        RefreshHomeScreenIfChanged();
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
        homePeopleFishing = BindRequiredElement<Label>(interfaceRoot, HomePeopleScreenName, "home-people-fishing");
        homePeopleList = BindRequiredElement<VisualElement>(interfaceRoot, HomePeopleScreenName, "home-people-list");
        homePeopleDetail = BindRequiredElement<Label>(interfaceRoot, HomePeopleScreenName, "home-people-detail");

        homePeopleBound = homePeopleSummary != null && homePeopleMaintenance != null && homePeopleCare != null &&
                          homePeopleFishing != null &&
                          homePeopleList != null && homePeopleDetail != null;
    }

    // Подпись состояния людей, функций и подготовки: карточки пересобираются
    // только при её изменении — идущие часы не сбрасывают прокрутку и
    // раскрытия (§13.2).
    private string BuildHomePeopleSignature()
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
        HomeFunctionReport fishing = HomeFunctionResolver.Resolve(gameState, HomeFunctionResolver.FishingId);
        signature.Append('|').Append(fishing.Status).Append(HomeFunctionResolver.IsFishingOpen(gameState))
            .Append((int)((gameState.People != null ? gameState.People.FishingEarned : 0.0) * 10.0));
        signature.Append('|').Append(string.Join(",", ExpeditionPreparation.GetFighterIds(gameState)))
            .Append('|').Append(ExpeditionPreparation.GetRetinueId(gameState));
        return signature.ToString();
    }

    private void RefreshHomePeopleIfChanged()
    {
        string current = BuildHomePeopleSignature();
        if (current == homePeopleSignature)
            return;
        homePeopleSignature = current;

        HomeFunctionReport maintenance = HomeFunctionResolver.Resolve(gameState, HomeFunctionResolver.MaintenanceId);
        HomeFunctionReport care = HomeFunctionResolver.Resolve(gameState, HomeFunctionResolver.CareId);

        int members = HomePeopleService.CountHomeMembers(gameState);
        int present = HomePeopleService.CountHomePresent(gameState);
        homePeopleSummary.text = members + " жителей · дома " + present + " · в походе " + (members - present);
        homePeopleMaintenance.text = FunctionLine("Ремонт", maintenance);
        homePeopleCare.text = FunctionLine("Уход за ранеными", care);
        string fishingLine = Chapter01HomeView.FishingLine(gameState);
        homePeopleFishing.text = fishingLine ?? string.Empty;
        homePeopleFishing.style.display = fishingLine != null ? DisplayStyle.Flex : DisplayStyle.None;

        RebuildPeopleList();
    }

    private static string FunctionLine(string title, HomeFunctionReport report)
    {
        switch (report.Status)
        {
            case HomeFunctionStatus.Working:
                return title + ": работает — " + report.ExecutorName + ".";
            case HomeFunctionStatus.Limited:
                return title + ": медленнее — " + report.Reason + ".";
            default:
                return title + ": остановлено — " + report.Reason + ".";
        }
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
                                                              " Ответить — в делах главы: «Люди у ворот».";
            homePeopleList.Add(offer);
        }

        // Стабильный порядок (§4): Командир, бойцы, сюжетные жители, принятые
        // семьи — так, как они лежат в реестре; фоновые семьи — одной строкой.
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
            row.EnableInClassList("person-row--prepared",
                ExpeditionPreparation.IsPrepared(gameState, resident.PersonId) &&
                !HomePeopleService.IsInExpedition(gameState, resident.PersonId));

            ResidentState captured = resident;
            row.clicked += () => ShowPersonDetail(captured);
            homePeopleList.Add(instance);
        }

        homePeopleDetail.text = "Другие семьи — " + background + " жителей: взрослые, дети и старики четырёх домов.";
    }

    // ПР-07Б: сведения о Доме. Дома — новости выдаются сразу; в пути —
    // один раз запоминается известное на момент выхода; по возвращении
    // отложенные новости приходят одной записью «Пока вас не было…».
    private void DeliverHomeNews()
    {
        string returnSummary = HomeKnowledge.Refresh(gameState, DescribeHomeForSnapshot);
        if (!string.IsNullOrEmpty(returnSummary))
            AddReport(returnSummary);

        if (HomePeopleService.HasDeparted(gameState))
            return;
        foreach (string news in HomeKnowledge.TakePendingNews(gameState))
            AddReport(news);
    }

    private static IList<string> DescribeHomeForSnapshot(GameState state)
    {
        List<string> lines = new List<string>();
        foreach (HomeObjectView view in Chapter01HomeView.DescribeObjects(state))
            lines.Add(view.Title + ": " + view.Short + ".");
        foreach (HomeCareView care in Chapter01HomeView.DescribeCares(state))
            lines.Add("Забота — " + care.Title + ": " + care.Status);
        string fishing = Chapter01HomeView.FishingLine(state);
        if (fishing != null)
            lines.Add(fishing);
        return lines;
    }

    private string PersonStatus(ResidentState resident)
    {
        if (!resident.IsAlive)
            return "нет в живых";
        if (resident.Membership != ResidentMembership.HomeMember)
            return "больше не живёт в Доме";

        string place;
        if (HomePeopleService.IsInExpedition(gameState, resident.PersonId))
            place = "в походе";
        else if (ExpeditionPreparation.IsPrepared(gameState, resident.PersonId))
            place = "собирается в поход";
        else
            place = "дома";

        if (resident.Injury == ResidentInjury.Recovering)
            place += " · тяжёлая рана";
        if (resident.HasCombatState && resident.CurrentHitPoints < resident.MaxHitPoints)
            place += " · " + resident.CurrentHitPoints + "/" + resident.MaxHitPoints + " HP";
        return place;
    }

    private void ShowPersonDetail(ResidentState resident)
    {
        homePeopleDetail.text = PersonCardText(resident);
    }

    // Карточка человека (§7.2): имя, роль, описание, семья, место и
    // состояние, настоящее HP, домашняя функция, походная роль.
    private string PersonCardText(ResidentState resident)
    {
        StringBuilder text = new StringBuilder();
        text.Append(resident.DisplayName).Append(" — ").Append(resident.RoleLabel).Append(". ");
        if (!string.IsNullOrEmpty(resident.ShortDescription))
            text.Append(resident.ShortDescription).Append(' ');
        if (resident.HouseholdId == Chapter01FisherFamily.HouseholdId)
            text.Append("Семья Тихона. ");
        text.Append("Сейчас: ").Append(PersonStatus(resident)).Append('.');
        if (resident.HasCombatState)
            text.Append(" HP ").Append(resident.CurrentHitPoints).Append('/').Append(resident.MaxHitPoints).Append('.');

        switch (resident.TravelRole)
        {
            case ResidentTravelRole.Commander:
                text.Append(" Ведёт поход.");
                break;
            case ResidentTravelRole.Combatant:
                text.Append(" В походе — боец.");
                break;
            case ResidentTravelRole.Retinue:
                text.Append(" В походе — специалист свиты, в бой не идёт.");
                break;
            default:
                text.Append(resident.AgeGroup == ResidentAgeGroup.Child ? " Ребёнок, остаётся дома." : " Остаётся дома.");
                break;
        }
        return text.ToString();
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

    // ПР-07А-1: свита — часть общего подготовленного состава
    // (ExpeditionPreparation); проверки — в команде.
    private void SelectRetinue(string personId)
    {
        if (!ExpeditionPreparation.TrySetRetinue(gameState, personId, out string message))
            AddReport(message);
        retinueSignature = null;
        RefreshStableUiAfterStateChange();
    }

    private string SelectedRetinueId => gameState != null ? ExpeditionPreparation.GetRetinueId(gameState) : null;

    private void RefreshRetinueIfChanged()
    {
        string retinueId = SelectedRetinueId;
        List<string> leaving = new List<string>(ExpeditionPreparation.GetFighterIds(gameState));
        if (!string.IsNullOrEmpty(retinueId))
            leaving.Add(retinueId);
        // Прогноз учитывает всех уходящих, включая Командира (§8.5).
        CommanderData commander = gameState.GetSelectedCommander();
        if (commander != null)
            leaving.Add(commander.Id);

        string current = (retinueId ?? "-") + "|" + string.Join(",", leaving) + "|" + homePeopleSignature;
        if (current == retinueSignature)
            return;
        retinueSignature = current;

        retinueNoneButton.EnableInClassList(RetinueSelectedClass, string.IsNullOrEmpty(retinueId));
        retinueOstafiyButton.EnableInClassList(RetinueSelectedClass, retinueId == HomePeopleService.OstafiyId);
        retinueLadaButton.EnableInClassList(RetinueSelectedClass, retinueId == HomePeopleService.LadaId);
        bool editable = ExpeditionPreparation.CanEdit(gameState);
        retinueNoneButton.SetEnabled(editable);
        retinueOstafiyButton.SetEnabled(editable && HomePeopleService.CanJoinAsRetinue(gameState, HomePeopleService.OstafiyId, out _));
        retinueLadaButton.SetEnabled(editable && HomePeopleService.CanJoinAsRetinue(gameState, HomePeopleService.LadaId, out _));

        ResidentState chosen = HomePeopleService.Find(gameState, retinueId);
        retinueDescription.text = chosen != null
            ? chosen.DisplayName + ": " + chosen.ShortDescription
            : "Отряд уйдёт без специалиста. Свита не идёт в бой, но ест из припасов похода.";

        HomeFunctionReport maintenance = HomeFunctionResolver.Forecast(gameState, HomeFunctionResolver.MaintenanceId, leaving);
        HomeFunctionReport care = HomeFunctionResolver.Forecast(gameState, HomeFunctionResolver.CareId, leaving);
        retinueForecast.text = "Дома после выхода: " + FunctionLine("ремонт", maintenance) + " " +
                               FunctionLine("уход", care) + " Припасы похода: " + leaving.Count + " в сутки.";
    }
}

using System.Collections.Generic;
using System.Text;
using KingdomSurvival.Chapter01;
using UnityEngine;
using UnityEngine.UIElements;

// ПР-07А-2 (PR07_HOME_SPEC §4–§7): экран Дома — сводка с пояснениями по
// нажатию, образ Дома из слоёв, объекты «что видим → почему известно → что
// можно сделать», заботы. Модель — Chapter01HomeView и HomeOverview; здесь
// только отображение и вызовы команд. Пока отряд в пути, живые домашние
// данные не показываются (сведения из похода — ПР-07Б).
public partial class PrototypeUIController
{
    private const string HomeScreenName = "Дом";
    private const string HomeSceneArtFolder = "HomeScene/";

    private static readonly Dictionary<string, string> HomeSceneLayerNames = new Dictionary<string, string>
    {
        { Chapter01HomeView.WaterId, "home-scene-water" },
        { Chapter01HomeView.MillId, "home-scene-mill" },
        { Chapter01HomeView.DamId, "home-scene-dam" },
        { Chapter01HomeView.WalkwayId, "home-scene-walkway" },
        { Chapter01HomeView.YardDeckId, "home-scene-yard" },
        { Chapter01HomeView.LivestockId, "home-scene-livestock" }
    };

    private bool homeScreenBindAttempted;
    private bool homeScreenBound;
    private string homeScreenSignature;
    private string homeSummaryDetailKind;
    private string selectedHomeObjectId;

    private Label homeDefenseLabel;
    private Label homeSummaryDetail;
    private Label homeAwayNotice;
    private VisualElement homeMainRow;
    private VisualElement homePeoplePanel;
    private VisualElement homeObjectsList;
    private VisualElement homeCaresList;
    private VisualElement homeSummaryStrip;
    private readonly Dictionary<string, Button> homeSceneLayers = new Dictionary<string, Button>();
    private readonly Dictionary<string, string> homeSceneLayerClasses = new Dictionary<string, string>();

    private void BindHomeScreen()
    {
        if (homeScreenBindAttempted || interfaceRoot == null)
            return;
        homeScreenBindAttempted = true;

        homeDefenseLabel = BindRequiredElement<Label>(interfaceRoot, HomeScreenName, "home-defense-label");
        homeSummaryDetail = BindRequiredElement<Label>(interfaceRoot, HomeScreenName, "home-summary-detail");
        homeAwayNotice = BindRequiredElement<Label>(interfaceRoot, HomeScreenName, "home-away-notice");
        homeMainRow = BindRequiredElement<VisualElement>(interfaceRoot, HomeScreenName, "home-main-row");
        homePeoplePanel = BindRequiredElement<VisualElement>(interfaceRoot, HomeScreenName, "home-people-panel");
        homeObjectsList = BindRequiredElement<VisualElement>(interfaceRoot, HomeScreenName, "home-objects-list");
        homeCaresList = BindRequiredElement<VisualElement>(interfaceRoot, HomeScreenName, "home-cares-list");
        homeSummaryStrip = interfaceRoot.Q<VisualElement>(className: "home-summary-strip");

        Button gold = BindRequiredElement<Button>(interfaceRoot, HomeScreenName, "home-summary-gold");
        Button food = BindRequiredElement<Button>(interfaceRoot, HomeScreenName, "home-summary-food");
        Button people = BindRequiredElement<Button>(interfaceRoot, HomeScreenName, "home-summary-people");
        Button defense = BindRequiredElement<Button>(interfaceRoot, HomeScreenName, "home-summary-defense");

        homeScreenBound = homeDefenseLabel != null && homeSummaryDetail != null && homeAwayNotice != null &&
                          homeMainRow != null && homePeoplePanel != null && homeObjectsList != null &&
                          homeCaresList != null && gold != null && food != null && people != null && defense != null;
        if (!homeScreenBound)
            return;

        // Пояснение сводки — и по нажатию, и по наведению (§4).
        RegisterSummaryDetail(gold, "gold");
        RegisterSummaryDetail(food, "food");
        RegisterSummaryDetail(people, "people");
        RegisterSummaryDetail(defense, "defense");

        foreach (KeyValuePair<string, string> pair in HomeSceneLayerNames)
        {
            Button layer = interfaceRoot.Q<Button>(pair.Value);
            if (layer == null)
                continue;
            string objectId = pair.Key;
            layer.clicked += () => SelectHomeObject(objectId);
            homeSceneLayers[objectId] = layer;
        }

        BindHomePrep();
    }

    private void RegisterSummaryDetail(Button box, string kind)
    {
        box.clicked += () =>
        {
            homeSummaryDetailKind = homeSummaryDetailKind == kind ? null : kind;
            if (homeSummaryDetailKind == null)
                homeSummaryDetail.style.display = DisplayStyle.None;
            RefreshHomeSummary();
        };
        box.RegisterCallback<PointerEnterEvent>(_ =>
        {
            if (homeSummaryDetailKind == null)
                ShowSummaryDetail(kind);
        });
        box.RegisterCallback<PointerLeaveEvent>(_ =>
        {
            if (homeSummaryDetailKind == null)
                homeSummaryDetail.style.display = DisplayStyle.None;
        });
    }

    private void RefreshHomeScreenIfChanged()
    {
        if (gameState == null)
            return;

        BindHomeScreen();
        if (!homeScreenBound)
            return;

        RefreshHomeSummary();
        RefreshHomePrepIfChanged();

        bool away = HomePeopleService.HasDeparted(gameState);
        IReadOnlyList<HomeObjectView> objects = away ? new List<HomeObjectView>() : Chapter01HomeView.DescribeObjects(gameState);
        IReadOnlyList<HomeCareView> cares = away ? new List<HomeCareView>() : Chapter01HomeView.DescribeCares(gameState);

        StringBuilder signature = new StringBuilder(away ? "away|" : "home|");
        signature.Append(homePeopleSignature).Append('|').Append(selectedHomeObjectId).Append('|');
        foreach (HomeObjectView view in objects)
            signature.Append(view.Id).Append(view.StateKey).Append(view.ActionDialogueId).Append(';');
        foreach (HomeCareView care in cares)
            signature.Append(care.Id).Append(care.Status).Append(care.ActionEnabled).Append(care.Detail).Append(';');

        string current = signature.ToString();
        if (current == homeScreenSignature)
            return;
        homeScreenSignature = current;

        homeAwayNotice.style.display = away ? DisplayStyle.Flex : DisplayStyle.None;
        homeMainRow.style.display = away ? DisplayStyle.None : DisplayStyle.Flex;
        homePeoplePanel.style.display = away ? DisplayStyle.None : DisplayStyle.Flex;
        if (homeSummaryStrip != null)
            homeSummaryStrip.style.display = away ? DisplayStyle.None : DisplayStyle.Flex;
        if (away)
            return;

        RefreshHomeScene(objects);
        RebuildHomeObjects(objects);
        RebuildHomeCares(cares);
    }

    // ------------------------------------------------------------------
    // Сводка
    // ------------------------------------------------------------------

    private void RefreshHomeSummary()
    {
        if (!homeScreenBound)
            return;

        List<ResidentState> defenders = HomeOverview.GetHomeDefenders(gameState);
        homeDefenseLabel.text = defenders.Count == 0
            ? "Защита: некому"
            : defenders.Count <= 2 ? "Защита: мало" : "Защита: есть кому";

        // Закреплённое нажатием пояснение обновляется вместе с числами;
        // пояснение по наведению живёт своими событиями.
        if (homeSummaryDetailKind != null)
            ShowSummaryDetail(homeSummaryDetailKind);
    }

    private void ShowSummaryDetail(string kind)
    {
        string text;
        switch (kind)
        {
            case "gold":
                text = "Деньги Дома: " + gameState.Gold + ". Базовое хозяйство приносит " +
                       BuildingSystem.GetNetDailyGoldIncome(gameState) + " в сутки.";
                break;
            case "food":
                text = "Запасы Дома: " + gameState.Food + ". Поступает " + BuildingSystem.GetDailyFoodIncome(gameState) +
                       " в сутки, дома едят " + gameState.DailyFoodConsumption + ". " + HomeOverview.DescribeFood(gameState) +
                       " Припасы похода считаются отдельно.";
                break;
            case "people":
                text = HomePeopleService.CountHomeMembers(gameState) + " жителей Дома: дома " +
                       HomePeopleService.CountHomePresent(gameState) + ", в походе " +
                       HomePeopleService.CountExpeditionPresent(gameState) + ".";
                break;
            default:
                text = HomeOverview.DescribeDefense(gameState) +
                       " Это оценка того, кто может держать защиту, а не обещание безопасности.";
                break;
        }

        homeSummaryDetail.text = text;
        homeSummaryDetail.style.display = DisplayStyle.Flex;
    }

    // ------------------------------------------------------------------
    // Образ Дома и объекты
    // ------------------------------------------------------------------

    // Слой меняет подпись и класс состояния (форма и символ задаются USS),
    // а если художник положил рисунок Resources/HomeScene/<слой>_<состояние>,
    // он подставляется без изменения кода (ProjectDocs/HOME_SCENE_ART_LIST.md).
    private void RefreshHomeScene(IReadOnlyList<HomeObjectView> objects)
    {
        HashSet<string> shown = new HashSet<string>();
        foreach (HomeObjectView view in objects)
        {
            if (!homeSceneLayers.TryGetValue(view.Id, out Button layer))
                continue;
            shown.Add(view.Id);

            layer.style.display = DisplayStyle.Flex;
            if (homeSceneLayerClasses.TryGetValue(view.Id, out string previous))
                layer.RemoveFromClassList(previous);
            string stateClass = "home-scene-layer--" + view.StateKey;
            layer.AddToClassList(stateClass);
            homeSceneLayerClasses[view.Id] = stateClass;
            layer.EnableInClassList("home-scene-layer--selected", view.Id == selectedHomeObjectId);

            Label caption = layer.Q<Label>(className: "home-scene-caption");
            if (caption != null)
                caption.text = view.Title + "\n" + view.Short;

            string layerName = HomeSceneLayerNames[view.Id].Replace("home-scene-", string.Empty);
            Texture2D art = Resources.Load<Texture2D>(HomeSceneArtFolder + layerName + "_" + view.StateKey);
            layer.style.backgroundImage = art != null ? new StyleBackground(art) : new StyleBackground(StyleKeyword.Null);
            layer.EnableInClassList("home-scene-layer--art", art != null);
        }

        foreach (KeyValuePair<string, Button> pair in homeSceneLayers)
        {
            if (!shown.Contains(pair.Key))
                pair.Value.style.display = DisplayStyle.None;
        }
    }

    private void RebuildHomeObjects(IReadOnlyList<HomeObjectView> objects)
    {
        homeObjectsList.Clear();
        foreach (HomeObjectView view in objects)
        {
            VisualElement row = new VisualElement();
            row.AddToClassList("home-object-row");
            row.EnableInClassList("home-object-row--selected", view.Id == selectedHomeObjectId);

            Label title = new Label(view.Title);
            title.AddToClassList("home-object-title");
            Label state = new Label(view.State);
            state.AddToClassList("home-object-state");
            Label source = new Label(view.Source);
            source.AddToClassList("home-object-source");
            row.Add(title);
            row.Add(state);
            row.Add(source);

            if (!string.IsNullOrEmpty(view.ActionLabel) && !string.IsNullOrEmpty(view.ActionDialogueId))
            {
                string dialogueId = view.ActionDialogueId;
                Button action = new Button(() => OnHomeActivityClicked(dialogueId)) { text = view.ActionLabel };
                action.AddToClassList("game-menu-button");
                action.AddToClassList("home-object-action");
                row.Add(action);
            }

            string objectId = view.Id;
            row.RegisterCallback<ClickEvent>(_ => SelectHomeObject(objectId));
            homeObjectsList.Add(row);
        }
    }

    private void SelectHomeObject(string objectId)
    {
        selectedHomeObjectId = selectedHomeObjectId == objectId ? null : objectId;
        homeScreenSignature = null;
        RefreshHomeScreenIfChanged();
    }

    // ------------------------------------------------------------------
    // Заботы
    // ------------------------------------------------------------------

    private void RebuildHomeCares(IReadOnlyList<HomeCareView> cares)
    {
        homeCaresList.Clear();
        if (cares.Count == 0)
        {
            Label empty = new Label("Сейчас Дом живёт своим чередом.");
            empty.AddToClassList("home-activities-empty");
            homeCaresList.Add(empty);
            return;
        }

        foreach (HomeCareView care in cares)
        {
            VisualElement card = new VisualElement();
            card.AddToClassList("home-care-card");
            card.EnableInClassList("home-care-card--urgent", care.Urgent);

            Label title = new Label(care.Title);
            title.AddToClassList("home-care-title");
            card.Add(title);
            AddCareLine(card, care.Cause, "home-care-cause");
            AddCareLine(card, care.Status, "home-care-status");
            AddCareLine(card, care.Detail, "home-care-detail");

            if (care.Action == HomeCareAction.StartYardDeck)
            {
                Button action = new Button(OnYardDeckCareClicked) { text = care.ActionLabel };
                action.AddToClassList("game-menu-button");
                action.AddToClassList("home-care-action");
                action.SetEnabled(care.ActionEnabled);
                card.Add(action);
            }
            else if (care.Action == HomeCareAction.OpenDialogue && !string.IsNullOrEmpty(care.DialogueId))
            {
                string dialogueId = care.DialogueId;
                Button action = new Button(() => OnHomeActivityClicked(dialogueId)) { text = care.ActionLabel };
                action.AddToClassList("game-menu-button");
                action.AddToClassList("home-care-action");
                card.Add(action);
            }

            homeCaresList.Add(card);
        }
    }

    private static void AddCareLine(VisualElement card, string text, string className)
    {
        if (string.IsNullOrEmpty(text))
            return;
        Label label = new Label(text);
        label.AddToClassList(className);
        card.Add(label);
    }

    private void OnYardDeckCareClicked()
    {
        if (gameState == null)
            return;

        Chapter01HomeActivities.TryStartYardDeck(gameState, out string message);
        AddReport(message);
        homePeopleSignature = null;
        homeScreenSignature = null;
        RefreshInterface();
    }
}

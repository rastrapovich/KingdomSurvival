using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

public partial class PrototypeUIController
{
    private sealed class BuildingCardView
    {
        public BuildingDefinition Definition;
        public VisualElement Root;
        public Label StatusLabel;
        public Label EffectLabel;
        public Label MetaLabel;
        public ProgressBar Progress;
        public Button ActionButton;
    }

    private VisualElement buildingGrid;
    private readonly Dictionary<string, BuildingCardView> buildingCards =
        new Dictionary<string, BuildingCardView>();
    private IVisualElementScheduledItem buildingUiSchedule;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void InstallBuildingUiAfterSceneLoad()
    {
        PrototypeUIController controller =
            UnityEngine.Object.FindFirstObjectByType<PrototypeUIController>();
        if (controller == null)
            return;

        UIDocument document = controller.GetComponent<UIDocument>();
        if (document == null)
            return;

        document.rootVisualElement.schedule.Execute(
            controller.InitializeBuildingUi).ExecuteLater(1);
    }

    private void InitializeBuildingUi()
    {
        if (gameState == null)
        {
            UIDocument document = GetComponent<UIDocument>();
            if (document != null)
            {
                document.rootVisualElement.schedule.Execute(
                    InitializeBuildingUi).ExecuteLater(50);
            }
            return;
        }

        UIDocument uiDocument = GetComponent<UIDocument>();
        if (uiDocument == null)
            return;

        VisualElement root = uiDocument.rootVisualElement;
        buildingGrid = root.Q<VisualElement>(className: "building-grid");
        if (buildingGrid == null)
            return;

        BuildBuildingCards();
        RefreshBuildingUi();

        if (buildingUiSchedule != null)
            buildingUiSchedule.Pause();

        buildingUiSchedule = buildingGrid.schedule.Execute(
            RefreshBuildingUi).Every(200);
    }

    private void BuildBuildingCards()
    {
        buildingGrid.Clear();
        buildingCards.Clear();
        buildingGrid.style.flexDirection = FlexDirection.Row;
        buildingGrid.style.flexWrap = Wrap.Wrap;
        buildingGrid.style.alignItems = Align.Stretch;

        foreach (BuildingDefinition definition in BuildingSystem.GetDefinitions())
        {
            VisualElement card = new VisualElement();
            card.AddToClassList("building-card");
            card.style.width = new Length(31.5f, LengthUnit.Percent);
            card.style.minWidth = 230;
            card.style.marginRight = 8;
            card.style.marginBottom = 8;
            card.style.paddingLeft = 12;
            card.style.paddingRight = 12;
            card.style.paddingTop = 10;
            card.style.paddingBottom = 10;

            VisualElement header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.justifyContent = Justify.SpaceBetween;
            header.style.alignItems = Align.Center;

            Label title = new Label(definition.DisplayName.ToUpperInvariant());
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.fontSize = 15;
            title.style.whiteSpace = WhiteSpace.Normal;
            title.style.flexGrow = 1;

            Label status = new Label();
            status.style.fontSize = 10;
            status.style.marginLeft = 8;
            status.style.unityTextAlign = TextAnchor.MiddleRight;

            header.Add(title);
            header.Add(status);
            card.Add(header);

            Label image = new Label("ИЗОБРАЖЕНИЕ ПОСТРОЙКИ");
            image.style.height = 72;
            image.style.marginTop = 8;
            image.style.marginBottom = 8;
            image.style.unityTextAlign = TextAnchor.MiddleCenter;
            image.style.fontSize = 10;
            image.style.opacity = 0.55f;
            card.Add(image);

            Label description = new Label(definition.Description);
            description.style.whiteSpace = WhiteSpace.Normal;
            description.style.fontSize = 11;
            description.style.marginBottom = 6;
            card.Add(description);

            Label effect = new Label(definition.EffectText);
            effect.style.whiteSpace = WhiteSpace.Normal;
            effect.style.fontSize = 11;
            effect.style.unityFontStyleAndWeight = FontStyle.Bold;
            effect.style.marginBottom = 7;
            card.Add(effect);

            Label meta = new Label();
            meta.style.whiteSpace = WhiteSpace.Normal;
            meta.style.fontSize = 10;
            meta.style.marginBottom = 6;
            card.Add(meta);

            ProgressBar progress = new ProgressBar
            {
                lowValue = 0f,
                highValue = 1f
            };
            progress.style.height = 18;
            progress.style.marginBottom = 7;
            card.Add(progress);

            Button action = new Button();
            action.style.height = 34;
            string capturedId = definition.Id;
            action.clicked += () => OnBuildingActionClicked(capturedId);
            card.Add(action);

            buildingGrid.Add(card);
            buildingCards[definition.Id] = new BuildingCardView
            {
                Definition = definition,
                Root = card,
                StatusLabel = status,
                EffectLabel = effect,
                MetaLabel = meta,
                Progress = progress,
                ActionButton = action
            };
        }
    }

    private void RefreshBuildingUi()
    {
        if (gameState == null || buildingGrid == null)
            return;

        BuildingSystem.Synchronize(gameState);

        List<string> notices = BuildingSystem.ConsumeNotices(gameState);
        if (notices.Count > 0)
        {
            foreach (string notice in notices)
                AddReport(notice);

            RefreshArmyPanel();
        }

        if (goldIncomeLabel != null)
        {
            int netGold = BuildingSystem.GetNetDailyGoldIncome(gameState);
            goldIncomeLabel.text = netGold >= 0 ? "+" + netGold : netGold.ToString();
        }

        if (foodIncomeLabel != null)
            foodIncomeLabel.text = "+" + BuildingSystem.GetDailyFoodIncome(gameState);

        foreach (KeyValuePair<string, BuildingCardView> pair in buildingCards)
            RefreshBuildingCard(pair.Value);
    }

    private void RefreshBuildingCard(BuildingCardView view)
    {
        BuildingStateData state = BuildingSystem.GetBuildingState(
            gameState,
            view.Definition.Id);
        if (state == null)
            return;

        bool isBarracks = view.Definition.Id == BuildingSystem.BarracksId;
        bool recruiting = isBarracks && BuildingSystem.IsRecruitmentActive(gameState);

        switch (state.Status)
        {
            case BuildingStatus.Completed:
                view.StatusLabel.text = "РАБОТАЕТ";
                view.Progress.style.display = recruiting
                    ? DisplayStyle.Flex
                    : DisplayStyle.None;

                if (isBarracks)
                {
                    view.MetaLabel.text =
                        "Постоянные бойцы: " + gameState.Fighters.Count +
                        "/" + BuildingSystem.PrototypeMaxFighters +
                        " · организация защиты поселения ещё не утверждена";

                    if (recruiting)
                    {
                        double remaining = BuildingSystem.GetRecruitmentHoursRemaining(gameState);
                        view.Progress.value = (float)BuildingSystem.GetRecruitmentProgress01(gameState);
                        view.Progress.title =
                            "Найм · осталось " + ContinuousExpeditionCommands.FormatHours(remaining);
                        view.ActionButton.text = "ИДЁТ ПОДГОТОВКА БОЙЦА";
                        view.ActionButton.SetEnabled(false);
                    }
                    else
                    {
                        view.ActionButton.text =
                            "НАНЯТЬ БОЙЦА · " + BuildingSystem.RecruitGoldCost + " ЗОЛОТА";
                        view.ActionButton.SetEnabled(BuildingSystem.CanRecruit(gameState));
                    }
                }
                else
                {
                    view.MetaLabel.text = BuildCompletedBuildingMeta(view.Definition);
                    view.ActionButton.text = "ПОСТРОЕНО";
                    view.ActionButton.SetEnabled(false);
                }
                break;

            case BuildingStatus.Constructing:
                double constructionRemaining = BuildingSystem.GetConstructionHoursRemaining(
                    gameState,
                    view.Definition.Id);
                view.StatusLabel.text = "СТРОИТСЯ";
                view.MetaLabel.text =
                    "Осталось: " +
                    ContinuousExpeditionCommands.FormatHours(constructionRemaining);
                view.Progress.style.display = DisplayStyle.Flex;
                view.Progress.value = (float)BuildingSystem.GetConstructionProgress01(
                    gameState,
                    view.Definition.Id);
                view.Progress.title = "Строительство";
                view.ActionButton.text = "СТРОИТСЯ";
                view.ActionButton.SetEnabled(false);
                break;

            case BuildingStatus.Locked:
                view.StatusLabel.text = "ПОЗЖЕ";
                view.MetaLabel.text = "Не входит в первый слой развития поселения.";
                view.Progress.style.display = DisplayStyle.None;
                view.ActionButton.text = "ЗАБЛОКИРОВАНО";
                view.ActionButton.SetEnabled(false);
                break;

            default:
                view.StatusLabel.text = "НЕ ПОСТРОЕНО";
                view.MetaLabel.text =
                    "Стоимость: " + view.Definition.GoldCost +
                    " золота · время: " +
                    ContinuousExpeditionCommands.FormatHours(
                        view.Definition.ConstructionHours);
                view.Progress.style.display = DisplayStyle.None;
                view.ActionButton.text = "ПОСТРОИТЬ";
                view.ActionButton.SetEnabled(
                    gameState.Gold >= view.Definition.GoldCost &&
                    !BuildingSystem.HasActiveConstruction(gameState));
                break;
        }
    }

    private string BuildCompletedBuildingMeta(BuildingDefinition definition)
    {
        List<string> parts = new List<string>();
        if (definition.DailyGoldIncome != 0)
            parts.Add("доход +" + definition.DailyGoldIncome + " золота/сутки");
        if (definition.DailyFoodIncome != 0)
            parts.Add("доход +" + definition.DailyFoodIncome + " пищи/сутки");
        if (definition.DailyGoldUpkeep != 0)
            parts.Add("содержание " + definition.DailyGoldUpkeep + " золота/сутки");

        return parts.Count > 0
            ? string.Join(" · ", parts)
            : "Постройка действует.";
    }

    private void OnBuildingActionClicked(string buildingId)
    {
        if (gameState == null || isGameOver)
            return;

        string message;
        if (buildingId == BuildingSystem.BarracksId &&
            BuildingSystem.IsCompleted(gameState, BuildingSystem.BarracksId))
        {
            BuildingSystem.TryStartRecruitment(gameState, out message);
        }
        else
        {
            BuildingSystem.TryStartConstruction(gameState, buildingId, out message);
        }

        AddReport(message);
        RefreshInterface();
        RefreshBuildingUi();
    }
}

using UnityEngine;
using UnityEngine.UIElements;

public partial class PrototypeUIController
{
    private bool armyBottomPolishInitialized;
    private IVisualElementScheduledItem armyBottomPolishInitItem;
    private IVisualElementScheduledItem armyBottomPolishMaintenanceItem;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void InitializeArmyBottomPolishRuntime()
    {
        PrototypeUIController controller =
            UnityEngine.Object.FindAnyObjectByType<PrototypeUIController>();

        if (controller == null)
            return;

        UIDocument document = controller.GetComponent<UIDocument>();

        if (document == null)
            return;

        controller.armyBottomPolishInitItem = document.rootVisualElement.schedule
            .Execute(controller.TryInitializeArmyBottomPolish)
            .Every(100);
    }

    private void TryInitializeArmyBottomPolish()
    {
        if (armyBottomPolishInitialized)
            return;

        if (!stableArmyLayoutInitialized ||
            interfaceRoot == null ||
            armyScreen == null ||
            capitalGarrisonDropZone == null ||
            commanderGarrisonDropZone == null ||
            capitalGarrisonList == null ||
            commanderGarrisonList == null ||
            supplyConsumptionLabel == null ||
            supplyDaysLabel == null)
        {
            return;
        }

        ApplyArmyBottomPolish();
        BindArmyBottomPolishResourceCallbacks();

        armyBottomPolishMaintenanceItem = interfaceRoot.schedule
            .Execute(MaintainArmyBottomPolish)
            .Every(250);

        armyBottomPolishInitialized = true;
        armyBottomPolishInitItem?.Pause();
    }

    private void BindArmyBottomPolishResourceCallbacks()
    {
        armyGoldMinusButton.clicked += OnArmyBottomPolishResourceChanged;
        armyGoldPlusButton.clicked += OnArmyBottomPolishResourceChanged;
        supplyMinusButton.clicked += OnArmyBottomPolishResourceChanged;
        supplyPlusButton.clicked += OnArmyBottomPolishResourceChanged;
    }

    private void OnArmyBottomPolishResourceChanged()
    {
        ApplyArmySupplyPolish();
    }

    private void ApplyArmyBottomPolish()
    {
        VisualElement armyPanel =
            armyScreen.Q<VisualElement>(className: "army-panel");
        VisualElement topRow =
            armyScreen.Q<VisualElement>(className: "commander-supply-row");
        VisualElement transferBoard =
            armyScreen.Q<VisualElement>(className: "army-transfer-board");

        if (armyPanel == null || topRow == null || transferBoard == null)
            return;

        armyPanel.style.flexDirection = FlexDirection.Column;
        armyPanel.style.justifyContent = Justify.SpaceBetween;

        topRow.style.height = 330;
        topRow.style.minHeight = 330;
        topRow.style.maxHeight = 330;
        topRow.style.flexGrow = 0;
        topRow.style.flexShrink = 0;
        topRow.style.marginBottom = 0;

        transferBoard.style.width = Length.Percent(82);
        transferBoard.style.height = 231;
        transferBoard.style.minHeight = 231;
        transferBoard.style.maxHeight = 231;
        transferBoard.style.flexGrow = 0;
        transferBoard.style.flexShrink = 0;
        transferBoard.style.alignSelf = Align.FlexStart;
        transferBoard.style.marginTop = 0;
        transferBoard.style.marginBottom = 0;

        ConfigureBottomGarrisonZone(
            capitalGarrisonDropZone,
            capitalGarrisonList,
            true);
        ConfigureBottomGarrisonZone(
            commanderGarrisonDropZone,
            commanderGarrisonList,
            false);

        ApplyArmySupplyPolish();
    }

    private void ConfigureBottomGarrisonZone(
        VisualElement zone,
        VisualElement list,
        bool capital)
    {
        zone.style.height = 113;
        zone.style.minHeight = 113;
        zone.style.maxHeight = 113;
        zone.style.flexGrow = 0;
        zone.style.flexShrink = 0;
        zone.style.marginBottom = capital ? 5 : 0;
        zone.style.paddingLeft = 10;
        zone.style.paddingRight = 10;
        zone.style.paddingTop = 7;
        zone.style.paddingBottom = 7;

        Label title = zone.Q<Label>(className: "army-roster-title");
        Label summary = zone.Q<Label>(className: "army-roster-summary");
        Label empty = zone.Q<Label>(className: "army-roster-empty-label");

        if (title != null)
        {
            title.style.marginTop = 0;
            title.style.marginBottom = 4;
            title.style.height = 14;
            title.style.minHeight = 14;
            title.style.maxHeight = 14;
            title.style.unityTextAlign = TextAnchor.MiddleLeft;
        }

        if (summary != null)
        {
            summary.style.display = DisplayStyle.None;
            summary.style.visibility = Visibility.Hidden;
        }

        if (empty != null)
        {
            empty.style.display = DisplayStyle.None;
            empty.style.visibility = Visibility.Hidden;
        }

        list.style.width = Length.Percent(100);
        list.style.height = 78;
        list.style.minHeight = 78;
        list.style.maxHeight = 78;
        list.style.flexGrow = 0;
        list.style.flexShrink = 0;
        list.style.flexDirection = FlexDirection.Row;
        list.style.flexWrap = Wrap.NoWrap;
        list.style.alignItems = Align.FlexStart;
        list.style.justifyContent = Justify.FlexStart;
        list.style.marginTop = 0;
        list.style.marginBottom = 0;
        list.style.paddingLeft = 0;
        list.style.paddingRight = 0;
        list.style.paddingTop = 0;
        list.style.paddingBottom = 0;

        AlignArmyCards(list);
    }

    private void AlignArmyCards(VisualElement list)
    {
        if (list == null)
            return;

        list.Query<VisualElement>(className: "fighter-card")
            .ForEach(card =>
            {
                card.style.alignSelf = Align.FlexStart;
                card.style.marginLeft = 0;
                card.style.marginTop = 0;
                card.style.marginRight = 8;
                card.style.marginBottom = 0;
            });
    }

    private void ApplyArmySupplyPolish()
    {
        VisualElement supplyBlock =
            armyScreen.Q<VisualElement>(className: "military-supply-block");

        if (supplyBlock == null || gameState == null)
            return;

        supplyBlock.style.height = 190;
        supplyBlock.style.minHeight = 190;
        supplyBlock.style.maxHeight = 190;
        supplyBlock.style.paddingBottom = 10;

        int dailyConsumption = gameState.HasActiveExpedition
            ? gameState.ExpeditionSupplyConsumption
            : selectedFighterIds.Count > 0
                ? selectedFighterIds.Count + 1
                : 0;

        int fullDays = dailyConsumption > 0
            ? gameState.ArmySupply / dailyConsumption
            : 0;

        string expectedConsumption = dailyConsumption > 0
            ? "Расход: " + dailyConsumption + " / день"
            : "Расход: —";
        string expectedDays = dailyConsumption > 0
            ? "Хватит на: " + fullDays + " дн."
            : "Хватит на: —";

        if (supplyConsumptionLabel.text != expectedConsumption)
            supplyConsumptionLabel.text = expectedConsumption;

        if (supplyDaysLabel.text != expectedDays)
            supplyDaysLabel.text = expectedDays;

        ConfigureArmySupplyNote(supplyConsumptionLabel, 4);
        ConfigureArmySupplyNote(supplyDaysLabel, 1);
    }

    private static void ConfigureArmySupplyNote(Label label, float marginTop)
    {
        if (label == null)
            return;

        label.style.height = 14;
        label.style.minHeight = 14;
        label.style.maxHeight = 14;
        label.style.flexGrow = 0;
        label.style.flexShrink = 0;
        label.style.marginTop = marginTop;
        label.style.marginBottom = 0;
        label.style.fontSize = 9;
        label.style.whiteSpace = WhiteSpace.NoWrap;
        label.style.unityTextAlign = TextAnchor.MiddleLeft;
    }

    private void MaintainArmyBottomPolish()
    {
        if (!armyBottomPolishInitialized)
            return;

        Label capitalSummary =
            capitalGarrisonDropZone.Q<Label>(className: "army-roster-summary");
        Label commanderSummary =
            commanderGarrisonDropZone.Q<Label>(className: "army-roster-summary");
        Label capitalEmpty =
            capitalGarrisonDropZone.Q<Label>(className: "army-roster-empty-label");
        Label commanderEmpty =
            commanderGarrisonDropZone.Q<Label>(className: "army-roster-empty-label");

        KeepArmyAuxiliaryLabelHidden(capitalSummary);
        KeepArmyAuxiliaryLabelHidden(commanderSummary);
        KeepArmyAuxiliaryLabelHidden(capitalEmpty);
        KeepArmyAuxiliaryLabelHidden(commanderEmpty);

        AlignArmyCards(capitalGarrisonList);
        AlignArmyCards(commanderGarrisonList);
        ApplyArmySupplyPolish();
    }

    private static void KeepArmyAuxiliaryLabelHidden(Label label)
    {
        if (label == null)
            return;

        label.style.display = DisplayStyle.None;
        label.style.visibility = Visibility.Hidden;
    }
}

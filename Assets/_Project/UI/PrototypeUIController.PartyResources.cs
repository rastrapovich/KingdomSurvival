using UnityEngine.UIElements;

// Бриф исправлений п. 5: золото похода, припасы и «хватит на N дней» —
// постоянно в нижней строке, рядом с часами. Те же поля состояния
// (ArmyGold, ArmySupply) и та же формула расхода (ExpeditionSupplyConsumption),
// без второй копии ресурсов. Вне похода расход — на полный состав, как в
// прежней оценке запаса снабжения.
public partial class PrototypeUIController
{
    private Label partyResourcesLabel;

    private void RefreshPartyResources()
    {
        if (partyResourcesLabel == null)
            partyResourcesLabel = interfaceRoot?.Q<Label>("party-resources-label");
        if (partyResourcesLabel == null || gameState == null)
            return;
        partyResourcesLabel.text = DescribePartyResources(gameState, out bool shortOnSupply);
        partyResourcesLabel.EnableInClassList("shell-resources-label--short", shortOnSupply);
        // Бриф правок (19 пунктов) п. 13: ноль припасов — красным.
        partyResourcesLabel.EnableInClassList("shell-resources-label--empty", gameState.ArmySupply <= 0);
        partyResourcesLabel.tooltip = "Золото и припасы отряда. Расход: " + gameState.ExpeditionSupplyConsumption +
                                      " в день" + (gameState.HasActiveExpedition ? "." : " (на полный состав похода).");
    }

    private static string DescribePartyResources(GameState state, out bool shortOnSupply)
    {
        int consumption = state.ExpeditionSupplyConsumption;
        string days;
        if (state.ArmySupply <= 0)
            days = "припасов нет";
        else if (consumption <= 0)
            days = "расхода нет";
        else
        {
            int full = state.ArmySupply / consumption;
            days = full <= 0 ? "хватит меньше чем на день" : "хватит на " + full + " " + DaysWord(full);
        }
        shortOnSupply = consumption > 0 && state.ArmySupply < consumption * 2;
        return "Золото: " + state.ArmyGold + "   ·   Припасы: " + state.ArmySupply + " (" + days + ")";
    }

    private static string DaysWord(int count)
    {
        int last2 = count % 100;
        int last = count % 10;
        if (last2 >= 11 && last2 <= 14)
            return "дней";
        if (last == 1)
            return "день";
        return last >= 2 && last <= 4 ? "дня" : "дней";
    }
}

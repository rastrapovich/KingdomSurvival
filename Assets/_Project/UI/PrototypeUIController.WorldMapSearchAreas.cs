using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

// 12Т (SETTLEMENTS_TRADE_TAVERN_SPEC.md §12): области поиска на глобальной
// карте — приметы, а не точный маркер. Какие области видны, решает
// WorldMapSearchAreas/WorldMapKnowledgeService (один источник); здесь только
// рисунок в слое «world-map-search-areas». Найденное место область убирает.
public partial class PrototypeUIController
{
    private VisualElement worldMapSearchLayer;
    private string worldMapSearchSignature;
    private float worldMapSearchTimer;

    private void TickWorldMapSearchAreas()
    {
        worldMapSearchTimer += Time.unscaledDeltaTime;
        if (worldMapSearchTimer < 0.5f)
            return;
        worldMapSearchTimer = 0f;
        if (worldMapSearchLayer == null && interfaceRoot != null)
            worldMapSearchLayer = interfaceRoot.Q<VisualElement>("world-map-search-areas");
        if (worldMapSearchLayer == null)
            return;

        List<ActiveSearchArea> areas = gameState != null ? WorldMapSearchAreas.Active(gameState) : new List<ActiveSearchArea>();
        string signature = string.Empty;
        foreach (ActiveSearchArea area in areas)
            signature += area.Search.SearchId + "/" + area.Stage.StageId + ";";
        if (signature == worldMapSearchSignature)
            return;
        worldMapSearchSignature = signature;

        worldMapSearchLayer.Clear();
        foreach (ActiveSearchArea area in areas)
        {
            WorldMapSearchStageDefinition stage = area.Stage;
            VisualElement element = new VisualElement { pickingMode = PickingMode.Ignore };
            element.AddToClassList("world-map-search-area");
            element.style.left = Length.Percent(Mathf.Clamp(stage.MinXPercent, 0f, 100f));
            element.style.top = Length.Percent(Mathf.Clamp(stage.MinYPercent, 0f, 100f));
            element.style.width = Length.Percent(Mathf.Max(1f, stage.MaxXPercent - stage.MinXPercent));
            element.style.height = Length.Percent(Mathf.Max(1f, stage.MaxYPercent - stage.MinYPercent));
            if (!string.IsNullOrWhiteSpace(stage.NeutralLabel))
            {
                Label label = new Label(stage.NeutralLabel) { pickingMode = PickingMode.Ignore };
                label.AddToClassList("world-map-search-area-label");
                element.Add(label);
            }
            worldMapSearchLayer.Add(element);
        }
    }
}

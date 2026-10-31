using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

// 12Т (SETTLEMENTS_TRADE_TAVERN_SPEC.md §12): области поиска на глобальной
// карте — приметы, а не точный маркер. Какие области видны, решает
// WorldMapSearchAreas/WorldMapKnowledgeService (один источник); здесь только
// рисунок в слое «world-map-search-areas».
// Бриф исправлений п. 15: область — небольшое пятно неправильной формы с
// тушевым краем (как кластеры земли карты), а не прямоугольник; найденное и
// обысканное место область убирает целиком.
public partial class PrototypeUIController
{
    private const int SearchBlobVertices = 18;

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

        // Бриф правок (19 пунктов) п. 9: только область цели, выбранной в журнале.
        List<ActiveSearchArea> areas = gameState != null
            ? WorldMapSearchAreas.ForGoal(gameState, selectedJournalGoalId)
            : new List<ActiveSearchArea>();
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
            Vector2[] outline = SearchBlobOutline(area.Search.SearchId + "/" + stage.StageId);
            element.generateVisualContent += context => DrawSearchBlob(context, element.contentRect, outline);
            if (!string.IsNullOrWhiteSpace(stage.NeutralLabel))
            {
                Label label = new Label(stage.NeutralLabel) { pickingMode = PickingMode.Ignore };
                label.AddToClassList("world-map-search-area-label");
                element.Add(label);
            }
            worldMapSearchLayer.Add(element);
        }
    }

    // Контур пятна в долях своего прямоугольника: радиус по углу колеблется
    // по устойчивому хэшу (после загрузки форма та же), края сглажены.
    private static Vector2[] SearchBlobOutline(string key)
    {
        uint hash = SettlementService.StableHash(key);
        float[] radius = new float[SearchBlobVertices];
        for (int i = 0; i < SearchBlobVertices; i++)
        {
            hash = hash * 1664525u + 1013904223u;
            radius[i] = 0.72f + (hash >> 8) % 1000 / 1000f * 0.28f;
        }
        Vector2[] points = new Vector2[SearchBlobVertices];
        for (int i = 0; i < SearchBlobVertices; i++)
        {
            float r = (radius[(i + SearchBlobVertices - 1) % SearchBlobVertices] + radius[i] * 2f +
                       radius[(i + 1) % SearchBlobVertices]) / 4f;
            float angle = i * Mathf.PI * 2f / SearchBlobVertices;
            points[i] = new Vector2(0.5f + Mathf.Cos(angle) * 0.5f * r, 0.5f + Mathf.Sin(angle) * 0.5f * r);
        }
        return points;
    }

    private static void DrawSearchBlob(MeshGenerationContext context, Rect rect, Vector2[] outline)
    {
        if (rect.width <= 1f || rect.height <= 1f)
            return;
        Painter2D painter = context.painter2D;
        painter.BeginPath();
        for (int i = 0; i <= outline.Length; i++)
        {
            Vector2 a = outline[i % outline.Length];
            Vector2 b = outline[(i + 1) % outline.Length];
            Vector2 mid = new Vector2(rect.x + (a.x + b.x) * 0.5f * rect.width, rect.y + (a.y + b.y) * 0.5f * rect.height);
            Vector2 corner = new Vector2(rect.x + a.x * rect.width, rect.y + a.y * rect.height);
            if (i == 0)
                painter.MoveTo(mid);
            else
                painter.QuadraticCurveTo(corner, mid);
        }
        painter.ClosePath();
        // Бриф правок (19 пунктов) п. 9: выбранная область чуть ярче.
        painter.fillColor = new Color(0.93f, 0.78f, 0.45f, 0.26f);
        painter.Fill();
        painter.strokeColor = new Color(0.16f, 0.12f, 0.08f, 0.92f);
        painter.lineWidth = 3f;
        painter.lineJoin = LineJoin.Round;
        painter.Stroke();
    }
}

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

public partial class PrototypeUIController
{
    private bool v124RosterPresentationInitialized;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void InitializeV124RosterPresentationRuntime()
    {
        PrototypeUIController controller =
            UnityEngine.Object.FindAnyObjectByType<PrototypeUIController>();
        if (controller == null)
            return;

        UIDocument document = controller.GetComponent<UIDocument>();
        if (document == null)
            return;

        document.rootVisualElement.schedule
            .Execute(controller.TryInitializeV124RosterPresentation)
            .ExecuteLater(180);
    }

    private void TryInitializeV124RosterPresentation()
    {
        if (v124RosterPresentationInitialized)
            return;

        if (interfaceRoot == null || gameState == null)
        {
            UIDocument document = GetComponent<UIDocument>();
            if (document != null)
            {
                document.rootVisualElement.schedule
                    .Execute(TryInitializeV124RosterPresentation)
                    .ExecuteLater(60);
            }
            return;
        }

        v124RosterPresentationInitialized = true;
        interfaceRoot.schedule
            .Execute(ApplyV124RosterPresentation)
            .Every(100);
        ApplyV124RosterPresentation();
    }

    private void ApplyV124RosterPresentation()
    {
        if (gameState == null)
            return;

        if (activeExpeditionDetails != null && gameState.HasActiveExpedition)
            activeExpeditionDetails.text = RemoveLegacyDefenseLines(activeExpeditionDetails.text);

        ReplaceSettlementTerms(expeditionStatusLabel);
        ReplaceSettlementTerms(activeExpeditionDetails);
        ReplaceSettlementTerms(returnExpeditionButton);
    }

    private static string RemoveLegacyDefenseLines(string source)
    {
        if (string.IsNullOrEmpty(source))
            return source;

        string[] lines = source.Split('\n');
        List<string> kept = new List<string>();
        foreach (string line in lines)
        {
            if (line.StartsWith("Сила отряда:") ||
                line.StartsWith("Гарнизон столицы:"))
            {
                continue;
            }
            kept.Add(line);
        }
        return string.Join("\n", kept);
    }

    private static void ReplaceSettlementTerms(TextElement element)
    {
        if (element == null || string.IsNullOrEmpty(element.text))
            return;

        element.text = element.text
            .Replace("Ожидает приказа короля", "Ожидает решения")
            .Replace("ожидает приказа короля", "ожидает решения")
            .Replace("столицу", "поселение")
            .Replace("столицы", "поселения")
            .Replace("столице", "поселении")
            .Replace("Столицу", "Поселение")
            .Replace("Столицы", "Поселения")
            .Replace("Столице", "Поселении");
    }
}

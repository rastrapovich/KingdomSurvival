using System.Collections.Generic;

// Shared result containers for the continuous strategic simulation and modal UI.
public class StrategicSimulationResult
{
    public List<string> Messages = new List<string>();
    public List<ExpeditionIncidentOccurrence> NewExpeditionIncidents =
        new List<ExpeditionIncidentOccurrence>();

    public bool HadNotableOccurrence;

    public StrategicModalNotice ResearchNotice;
    public StrategicModalNotice ExpeditionReturnNotice;

    // Сигнал "сейчас теоретически может произойти Encounter" (E01-T12,
    // Encounters/Runtime/EncounterOpportunity.cs). Только плоские данные —
    // StrategicSimulationResult живёт в Core, который ничего не реферит, и
    // не может ссылаться на типы модуля Encounters. Сам выбор и открытие
    // диалога происходят выше, в UI-слое (PrototypeUIController.Encounters.cs),
    // который уже умеет открывать Narrative Dialogue по ID.
    public bool HasRoadEncounterOpportunity;
    public string RoadEncounterOpportunityId = string.Empty;
    public double RoadEncounterWorldHour;
    public string RoadEncounterRegionId = string.Empty;
}

public class StrategicModalNotice
{
    public string Title;
    public string Description;
    public string Consequence;
}

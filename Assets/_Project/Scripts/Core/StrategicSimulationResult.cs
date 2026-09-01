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
}

public class StrategicModalNotice
{
    public string Title;
    public string Description;
    public string Consequence;
}

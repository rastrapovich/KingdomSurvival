using System.Collections.Generic;

// Shared result containers for the continuous strategic simulation and modal UI.
// The historical name DayResolutionResult is retained for compatibility with
// existing event systems; it no longer implies a player-triggered day step.
public class DayResolutionResult
{
    public List<string> Messages = new List<string>();
    public List<ExpeditionIncidentOccurrence> NewExpeditionIncidents =
        new List<ExpeditionIncidentOccurrence>();

    public bool HadNotableOccurrence;

    // Retained while the existing event definitions are migrated. The continuous
    // simulation does not use this field to advance a discrete turn.
    public bool SkipExpeditionOccurrencesForDay;

    public DayModalNotice ResearchNotice;
    public DayModalNotice ExpeditionReturnNotice;
}

public class DayModalNotice
{
    public string Title;
    public string Description;
    public string Consequence;
}

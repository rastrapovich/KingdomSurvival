using System.Collections.Generic;

// ПР-12К (канон v1.53 §28.9–28.10): техническая проверка лагеря у
// поселения и внешних участников. Только для разработки (Debug-панель и
// тесты): нейтральное техническое поселение, один наёмник на шаблоне
// лучника и одно существо-союзник на шаблоне лесного зверя. Это не
// канонический каталог, не факты мира и не цены найма; портретов и
// биографий у них нет. Существо в тестовое поселение не пускают — оно
// ждёт в лагере и сражается в дорожном бою.
public static class SettlementCampDevFixture
{
    public const string SettlementId = "dev.settlement";
    public const string MercenaryId = "dev.mercenary";
    public const string CreatureId = "dev.creature_ally";
    public const string MercenaryUnitTypeId = "archer";
    public const string CreatureUnitTypeId = "forest_beast";
    public const string CreatureAdmissionBlock = "зверя в поселение не пустят";

    // Готовит проверку у текущего положения отряда. Сообщение — что сделано.
    public static bool Apply(GameState state, out string report)
    {
        report = string.Empty;
        if (state == null || !state.HasActiveExpedition)
        {
            report = "Тестовое поселение — только в походе.";
            return false;
        }

        ExpeditionData expedition = state.ActiveExpedition;
        LocationData settlement = state.FindLocation(SettlementId);
        if (settlement == null)
        {
            settlement = new LocationData(SettlementId, "Техническое поселение", 0.0, "низкая")
            {
                RegionId = "dev",
                RegionName = "Проверка",
                MapSlotIndex = state.Locations.Count,
                MapXPercent = expedition.CurrentMapXPercent,
                MapYPercent = expedition.CurrentMapYPercent,
                IsDiscovered = true,
                IsVisibleOnMap = true,
                IsSettlement = true,
                InteractionDescription = "Техническое поселение для проверки лагеря у входа. Новых фактов мира здесь нет."
            };
            state.Locations.Add(settlement);
        }

        List<string> added = new List<string>();
        AddExternal(state, MercenaryId, "Наёмник (проверка)", MercenaryUnitTypeId, CampaignParticipantOrigin.Mercenary, string.Empty, added);
        AddExternal(state, CreatureId, "Зверь-союзник (проверка)", CreatureUnitTypeId, CampaignParticipantOrigin.CreatureAlly, CreatureAdmissionBlock, added);

        expedition.LocationId = settlement.Id;
        expedition.Phase = CommanderState.AtLocation;
        report = "Тестовое поселение у отряда." + (added.Count > 0 ? " В поход взяты: " + string.Join(", ", added) + "." : string.Empty);
        return true;
    }

    private static void AddExternal(GameState state, string id, string name, string unitTypeId,
        CampaignParticipantOrigin origin, string admissionBlock, List<string> added)
    {
        ExpeditionData expedition = state.ActiveExpedition;
        if (expedition.FighterIds.Contains(id))
            return;
        // Внешний участник занимает место бойца — сверх лимита не берётся.
        if (expedition.FighterIds.Count >= GameState.ExpeditionFighterSlots)
            return;

        if (state.FindFighter(id) == null)
            state.Fighters.Add(new FighterData(id, name, origin == CampaignParticipantOrigin.CreatureAlly ? "Существо-союзник" : "Наёмник", 1, 0, unitTypeId));

        ResidentState resident = HomePeopleService.Find(state, id);
        if (resident == null)
        {
            resident = new ResidentState
            {
                PersonId = id,
                DisplayName = name,
                RoleLabel = origin == CampaignParticipantOrigin.CreatureAlly ? "существо-союзник" : "наёмник",
                Membership = ResidentMembership.External,
                Origin = origin,
                TravelRole = ResidentTravelRole.Combatant,
                UnitTypeId = unitTypeId,
                SettlementAdmissionBlock = admissionBlock
            };
            state.People.Residents.Add(resident);
            HomePeopleService.EnsureCombatState(resident);
        }
        expedition.FighterIds.Add(id);
        added.Add(name);
    }
}

// 12Т: техническая проверка торговли, корчмы и доски (Debug-панель и тесты).
// Берёт техническое поселение ПР-12К (SettlementCampDevFixture) и назначает
// ему профиль из «Базы поселений»; добавляет золото похода до 60 — только
// здесь, обычная партия денег не получает. Это не каталог и не факты мира.
public static class SettlementTradeDevFixture
{
    public const string TradingPostProfileId = "settlement.trading.post";
    public const int DevMoney = 60;

    public static bool Apply(GameState state, string profileId, out string report)
    {
        if (!SettlementCampDevFixture.Apply(state, out report))
            return false;
        SettlementProfileDefinition profile = SettlementCatalog.Current.FindProfile(profileId);
        if (profile == null)
        {
            report = "Профиля «" + profileId + "» нет в Базе поселений.";
            return false;
        }
        SettlementService.EnsureState(state);
        SettlementInstanceData instance = SettlementService.FindInstance(state, SettlementCampDevFixture.SettlementId);
        if (instance == null)
        {
            instance = new SettlementInstanceData { LocationId = SettlementCampDevFixture.SettlementId };
            state.Settlements.Instances.Add(instance);
        }
        if (instance.ProfileId != profile.Id)
        {
            // Смена профиля технического места — с чистого состояния.
            instance.ProfileId = profile.Id;
            instance.Merchants.Clear();
            instance.Offers.Clear();
            instance.Notices.Clear();
        }
        if (state.ArmyGold < DevMoney)
            state.ArmyGold = DevMoney;
        report += " Профиль: «" + profile.Name + "», золото похода: " + state.ArmyGold + ".";
        return true;
    }
}

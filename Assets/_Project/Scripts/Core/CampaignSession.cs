// ПР-01: владелец текущей кампании. Кампания живёт здесь, а не в компоненте
// сцены, поэтому переживает смену сцен (главное меню, будущий бой в
// BattleSandbox): сцена при загрузке забирает уже идущую кампанию, а не
// создаёт новую. Создание и загрузка кампании — только через Begin; выход
// без продолжения — End. Статическое состояние сбрасывается Unity-слоем при
// старте Play Mode (на случай выключенной перезагрузки домена).
public static class CampaignSession
{
    private static GameState current;

    public static GameState Current => current;

    public static bool HasActive => current != null;

    // Версия меняется при каждой новой или загруженной кампании — по ней
    // сцена отличает «та же кампания» от «другая кампания».
    public static int Generation { get; private set; }

    // ПР-03: бой, в который кампания ушла (сцена боя читает его при старте),
    // и итог, с которым сцена боя вернула кампанию (основная сцена применяет
    // его один раз при подхвате). Оба живут вместе с кампанией.
    public static CampaignBattleRequest PendingBattle { get; private set; }
    public static CampaignBattleResult CompletedBattle { get; private set; }

    public static void Begin(GameState gameState)
    {
        current = gameState ?? throw new System.ArgumentNullException(nameof(gameState));
        PendingBattle = null;
        CompletedBattle = null;
        Generation++;
    }

    public static void End()
    {
        current = null;
        PendingBattle = null;
        CompletedBattle = null;
        Generation++;
    }

    public static void Reset()
    {
        current = null;
        PendingBattle = null;
        CompletedBattle = null;
        Generation = 0;
    }

    public static void EnterBattle(CampaignBattleRequest request)
    {
        if (current == null)
            throw new System.InvalidOperationException("Нет кампании, из которой можно войти в бой.");

        PendingBattle = request ?? throw new System.ArgumentNullException(nameof(request));
        CompletedBattle = null;
    }

    // Сцена боя закончила бой: запрос снят, итог ждёт основную сцену.
    public static void CompleteBattle(CampaignBattleResult result)
    {
        if (PendingBattle == null || result == null || result.BattleId != PendingBattle.BattleId)
            throw new System.InvalidOperationException("Итог не относится к текущему бою кампании.");

        CompletedBattle = result;
        PendingBattle = null;
    }

    // Основная сцена забирает итог один раз.
    public static CampaignBattleResult TakeCompletedBattle()
    {
        CampaignBattleResult result = CompletedBattle;
        CompletedBattle = null;
        return result;
    }
}

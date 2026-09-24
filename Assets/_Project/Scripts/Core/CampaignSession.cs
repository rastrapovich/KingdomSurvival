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

    public static void Begin(GameState gameState)
    {
        current = gameState ?? throw new System.ArgumentNullException(nameof(gameState));
        Generation++;
    }

    public static void End()
    {
        current = null;
        Generation++;
    }

    public static void Reset()
    {
        current = null;
        Generation = 0;
    }
}

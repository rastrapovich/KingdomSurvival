using KingdomSurvival.MapLabBridge;
using KingdomSurvival.UnitDatabase;
using UnityEngine;

namespace KingdomSurvival.BattleSandbox
{
    // Рисунки существ по видам (19.11.2026): База существ узнаёт выбранный
    // вид карты отсюда — сама она на сборку видов не ссылается.
    internal static class UnitArtViewsBridge
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Register()
        {
            UnitArtViews.ActiveViewProvider = () => MapLabGameViews.ActiveId;
        }
    }
}

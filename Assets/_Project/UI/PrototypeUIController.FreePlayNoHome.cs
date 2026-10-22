using UnityEngine.UIElements;

public partial class PrototypeUIController
{
    // Канон v1.58 §6.0: в «Свободной игре» Дом временно убран — остаются
    // карта, лагерь, экран героя и журнал. Данные Дома в партии живут как
    // прежде (люди, кладовая, приток), но не показываются: нет кнопки и
    // экрана Дома, маркера Дома на карте и строки запасов Дома. Между
    // походами герой стоит на стоянке — точке старта сгенерированной карты.
    // Кампании кризисов не затрагиваются.
    private bool IsFreePlayWithoutHome => gameState != null && CampaignContent.IsFreePlay(gameState);

    private void ApplyFreePlayHomeVisibility()
    {
        if (gameState == null)
            return;

        bool showHome = !IsFreePlayWithoutHome;
        SetShown(navCapitalButton, showHome);
        SetShown(worldMapCapitalButton, showHome);
        SetShown(worldMapFocusHomeButton, showHome);
        SetShown(foodLabel, showHome);
        SetShown(foodIncomeLabel, showHome);
        SetShown(populationLabel, showHome);
        SetShown(foodConsumptionLabel, showHome);

        // Под картой нет экрана Дома — карта всегда открыта.
        if (!showHome && (!openedScreen.HasValue || openedScreen.Value == MainScreen.Capital))
            OpenScreen(MainScreen.Expeditions);
    }

    private static void SetShown(VisualElement element, bool shown)
    {
        if (element == null)
            return;
        DisplayStyle display = shown ? DisplayStyle.Flex : DisplayStyle.None;
        if (element.style.display != display)
            element.style.display = display;
    }
}

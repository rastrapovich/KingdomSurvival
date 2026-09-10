using KingdomSurvival.UILayout;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Применение экранов `UI Конструктора` к прототипному интерфейсу.
///
/// Экран диалога здесь не участвует: у него собственный код применения в
/// <c>PrototypeUIController.Narrative</c> и выключенный флаг `autoApply`.
///
/// Обычные элементы меняются только по явно включённым override-флагам.
/// Тип `Portrait` сам является явным выбором preset-рамки и изображения,
/// поэтому применяет их без отдельных override Rect/Background.
/// </summary>
public partial class PrototypeUIController
{
    private bool uiLayoutScreensHooked;

    private void InitializeUILayoutScreens()
    {
        if (interfaceRoot == null || uiLayoutScreensHooked)
            return;

        VisualElement screen = interfaceRoot.Q<VisualElement>("screen");
        if (screen == null)
            return;

        uiLayoutScreensHooked = true;
        screen.RegisterCallback<GeometryChangedEvent>(_ => ApplyUILayoutScreens());
        ApplyUILayoutScreens();
    }

    private void ApplyUILayoutScreens()
    {
        if (interfaceRoot == null)
            return;

        VisualElement screen = interfaceRoot.Q<VisualElement>("screen");
        if (screen == null)
            return;

        float width = screen.resolvedStyle.width;
        float height = screen.resolvedStyle.height;
        UILayoutScreenBinder.ApplyAutoScreens(interfaceRoot, new Vector2(width, height));
    }
}

using KingdomSurvival.UILayout;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Применение экранов `UI Конструктора` к прототипному интерфейсу.
///
/// Экран диалога здесь не участвует: у него собственный код применения в
/// <c>PrototypeUIController.Narrative</c> и выключенный флаг `autoApply`.
///
/// Байндер меняет только те свойства, которые designer явно включил в
/// конструкторе (`Переопределять прямоугольник / фон / текст`). Пока
/// переопределения выключены, вёрстка полностью остаётся за USS.
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

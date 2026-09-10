using UnityEngine;
using UnityEngine.UIElements;

public partial class PrototypeUIController
{
    private bool continuousControlsPolishInitialized;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void InitializeContinuousControlsPolishRuntime()
    {
        PrototypeUIController controller =
            UnityEngine.Object.FindAnyObjectByType<PrototypeUIController>();

        if (controller == null)
            return;

        UIDocument document = controller.GetComponent<UIDocument>();
        if (document == null)
            return;

        document.rootVisualElement.schedule
            .Execute(controller.TryInitializeContinuousControlsPolish)
            .ExecuteLater(130);
    }

    private void TryInitializeContinuousControlsPolish()
    {
        if (continuousControlsPolishInitialized)
            return;

        if (!continuousTimeInitialized ||
            interfaceRoot == null ||
            gameState == null ||
            timeToggleButton == null ||
            dayLabel == null)
        {
            ScheduleContinuousControlsPolishRetry();
            return;
        }

        RebindContinuousTimeButtons();
        RepositionContinuousDayBox();

        // focusable/Focus() здесь исторически включает клавиатурный ввод
        // для interfaceRoot вообще — сама пауза по пробелу убрана (раздел
        // 1/19 инструкции про карту и время), но OnWorldMapLocationCardKeyDown
        // (PrototypeUIController.WorldMapLocationActions.cs, Escape) всё ещё
        // полагается на то, что interfaceRoot фокусируем и в фокусе.
        interfaceRoot.focusable = true;
        interfaceRoot.Focus();

        interfaceRoot.schedule
            .Execute(RefreshContinuousControlsPolish)
            .Every(100);

        continuousControlsPolishInitialized = true;
        RefreshContinuousControlsPolish();
    }

    private void ScheduleContinuousControlsPolishRetry()
    {
        UIDocument document = GetComponent<UIDocument>();
        if (document == null)
            return;

        document.rootVisualElement.schedule
            .Execute(TryInitializeContinuousControlsPolish)
            .ExecuteLater(60);
    }

    // Раздел 20 инструкции про карту и время: ×1/×3/×5/×10 убраны вместе с
    // ручным Пуском — это QoL для более позднего прохода, не обязательная
    // часть первой версии "движение = течение времени". Сами Core-методы
    // (ToggleSpeed/SetSpeedMultiplier/GetSpeedMultiplier) не тронуты — ими
    // пользуются существующие тесты и, при необходимости, внутренняя логика.
    private void RefreshContinuousControlsPolish()
    {
        if (gameState == null || isGameOver)
            return;

        RepositionContinuousDayBox();
    }

    private void RepositionContinuousDayBox()
    {
        if (dayLabel == null ||
            timeToggleButton == null ||
            dayLabel.parent == null ||
            timeToggleButton.parent == null)
        {
            return;
        }

        VisualElement dayBox = dayLabel.parent;
        VisualElement timeControlHost = timeToggleButton.parent;

        if (dayBox.parent != timeControlHost)
        {
            dayBox.RemoveFromHierarchy();
            timeControlHost.Add(dayBox);
            timeToggleButton.BringToFront();
        }

        dayBox.style.flexGrow = 0f;
        dayBox.style.flexShrink = 0f;
        dayBox.style.width = 158f;
        dayBox.style.minWidth = 158f;
        dayBox.style.marginLeft = 6f;
        dayBox.style.marginRight = 6f;
    }
}

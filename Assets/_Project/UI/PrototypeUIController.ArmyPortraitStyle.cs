using UnityEngine;
using UnityEngine.UIElements;

public partial class PrototypeUIController
{
    private const string ArmyPortraitStyleResource =
        "Prototype_ArmyPortraitStable";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void InitializeArmyPortraitStyleRuntime()
    {
        PrototypeUIController controller =
            UnityEngine.Object.FindAnyObjectByType<PrototypeUIController>();
        if (controller == null)
            return;

        UIDocument document = controller.GetComponent<UIDocument>();
        if (document == null)
            return;

        StyleSheet styleSheet =
            Resources.Load<StyleSheet>(ArmyPortraitStyleResource);
        if (styleSheet == null)
        {
            Debug.LogError(
                "PrototypeUIController: не найден стабильный USS армии " +
                ArmyPortraitStyleResource + ".");
            return;
        }

        // Стиль добавляется один раз после базовых USS и больше не меняется
        // runtime-поллером. Это исключает скачки размеров между кадрами.
        document.rootVisualElement.styleSheets.Add(styleSheet);
    }
}

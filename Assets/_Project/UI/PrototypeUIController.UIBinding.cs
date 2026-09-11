using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Общий helper для миграции runtime-экранов на UXML (ProjectDocs/
/// UI_ARCHITECTURE.md §5). Находит обязательный элемент по имени и явно
/// логирует, какого именно элемента не хватает в UXML, вместо
/// NullReferenceException где-то ниже по цепочке инициализации экрана.
/// Первый потребитель — Journal (UI-M02); дальше переиспользуется каждым
/// следующим мигрированным экраном.
/// </summary>
public partial class PrototypeUIController
{
    private static T BindRequiredElement<T>(VisualElement root, string screenName, string elementName)
        where T : VisualElement
    {
        T element = root?.Q<T>(elementName);
        if (element == null)
            Debug.LogError(screenName + ": обязательный UXML-элемент '" + elementName + "' не найден.");

        return element;
    }
}

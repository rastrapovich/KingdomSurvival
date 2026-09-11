using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

// UI-M06: quick-expedition-popup стал постоянным статичным узлом
// Prototype_Main.uxml (раньше CreateQuickExpeditionPopup() всегда
// создавал его через new VisualElement(), т.к. узла физически не было).
// Карточки локаций клонируются из Templates/ExpeditionLocationCard.uxml.
// По пути также консолидированы правки из бывшего
// PrototypeUIController.NavigationAndInteractionFixes.cs (переименование
// "Экспедиция" → "Карта"/"Локации" стало статичным текстом в UXML).
public sealed class ExpeditionPopupLayoutTests
{
    private static string ReadPrototypeMainUxml()
    {
        string path = Path.Combine(
            Application.dataPath, "_Project", "UI", "Prototype", "Prototype_Main.uxml");
        return File.ReadAllText(path);
    }

    [Test]
    public void Quick_Expedition_Popup_Is_A_Static_Node_In_Uxml()
    {
        string uxml = ReadPrototypeMainUxml();

        StringAssert.Contains("name=\"quick-expedition-popup\"", uxml);
        StringAssert.Contains("name=\"quick-expedition-popup-title\"", uxml);
        StringAssert.Contains("name=\"quick-expedition-order-label\"", uxml);
    }

    [Test]
    public void Map_And_Locations_Buttons_Have_Final_Static_Labels()
    {
        string uxml = ReadPrototypeMainUxml();

        // Раньше ApplyMapAndLocationsLabels() переименовывала эти кнопки
        // программно при каждом запуске — теперь это финальный статичный
        // текст, второй подписи ("Экспедиция"/"ЭКСПЕДИЦИЯ") быть не должно.
        StringAssert.Contains(
            "name=\"nav-expeditions-button\" text=\"Карта\" tooltip=\"Глобальная карта\"",
            uxml);
        StringAssert.Contains(
            "name=\"persistent-commander-expedition-button\" text=\"ЛОКАЦИИ\" tooltip=\"Открытые локации\"",
            uxml);
    }

    [Test]
    public void ExpeditionLocationCard_Template_Exists_With_All_Bindable_Parts()
    {
        VisualTreeAsset template = Resources.Load<VisualTreeAsset>("Templates/ExpeditionLocationCard");
        Assert.IsNotNull(template, "Не найден Resources/Templates/ExpeditionLocationCard.uxml.");

        TemplateContainer instance = template.Instantiate();

        Assert.IsNotNull(instance.Q<VisualElement>("quick-expedition-card"));
        Assert.IsNotNull(instance.Q<Label>("quick-expedition-card-name"));
        Assert.IsNotNull(instance.Q<VisualElement>("quick-expedition-card-image"));
        Assert.IsNotNull(instance.Q<Label>("quick-expedition-card-image-label"));
        Assert.IsNotNull(instance.Q<Label>("quick-expedition-card-distance"));
        Assert.IsNotNull(instance.Q<Label>("quick-expedition-card-threat"));
        Assert.IsNotNull(instance.Q<Button>("quick-expedition-card-action"));
    }

    [Test]
    public void ExpeditionLocationCard_Image_Is_Not_A_Button()
    {
        // UI-M06: картинка карточки больше не кликабельна — отправка/смена
        // маршрута идёт через отдельную кнопку-действие
        // (quick-expedition-card-action). Раньше картинка сама была Button
        // с обработчиком клика, который уже был фактически отключён
        // (picking-mode Ignore) в бывшем NavigationAndInteractionFixes.cs.
        VisualTreeAsset template = Resources.Load<VisualTreeAsset>("Templates/ExpeditionLocationCard");
        Assert.IsNotNull(template);

        TemplateContainer instance = template.Instantiate();
        Assert.IsNull(instance.Q<Button>("quick-expedition-card-image"));
        Assert.IsNotNull(instance.Q<VisualElement>("quick-expedition-card-image"));
    }
}

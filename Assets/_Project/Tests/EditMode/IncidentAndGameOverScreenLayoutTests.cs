using System.Collections.Generic;
using System.IO;
using KingdomSurvival.UILayout;
using NUnit.Framework;
using UnityEngine;

// UI-M07 (ProjectDocs/UI_ARCHITECTURE.md §9): Incident и Game Over уже были
// близки к целевой модели — структура в Prototype_Main.uxml, Controller
// только биндит Q<T>(...), никаких Build*/Ensure* методов. Этого файла не
// хватало: раньше оба экрана были покрыты только обобщёнными проверками
// в UILayoutDatabaseTests.cs. Этот тест фиксирует то же самое явно, по
// образцу CampScreenLayoutTests.cs, и закрывает §11 доктрины ("для каждого
// мигрированного экрана — Structure/UILayout test").
public sealed class IncidentAndGameOverScreenLayoutTests
{
    private const string IncidentScreenId = "incident-modal";
    private const string GameOverScreenId = "game-over";

    private static UILayoutDatabaseAsset LoadDatabase()
    {
        UILayoutDatabaseAsset database = Resources.Load<UILayoutDatabaseAsset>(UILayoutDatabaseAsset.ResourcesPath);
        Assert.IsNotNull(database, "Не найдена база UILayout Resources/" + UILayoutDatabaseAsset.ResourcesPath + ".asset");
        return database;
    }

    [TestCase(IncidentScreenId, "incident-modal-overlay")]
    [TestCase(GameOverScreenId, "game-over-overlay")]
    public void Screen_Is_Registered_And_AutoApplied(string screenId, string expectedRootName)
    {
        UILayoutDatabaseAsset database = LoadDatabase();
        UILayoutScreenDefinition screen = database.FindScreen(screenId);

        Assert.IsNotNull(screen, "Экран '" + screenId + "' должен быть зарегистрирован в UILayoutDatabaseAsset.");
        Assert.IsTrue(screen.AutoApply, screenId + " — обычный (не narrative-dialogue) экран: должен применяться generic-байндером.");
        Assert.AreEqual(expectedRootName, screen.RootName);
    }

    [TestCase(IncidentScreenId)]
    [TestCase(GameOverScreenId)]
    public void RequiredElements_AreAllResolvable_WithCorrectParents(string screenId)
    {
        UILayoutDatabaseAsset database = LoadDatabase();
        UILayoutScreenDefinition screen = database.FindScreen(screenId);
        Assert.IsNotNull(screen);
        Assert.Greater(screen.RequiredElements.Count, 0, "У '" + screenId + "' должен быть непустой список обязательных элементов.");

        foreach (UILayoutRequiredElement required in screen.RequiredElements)
        {
            UILayoutElementDefinition element = screen.FindElement(required.ElementId);
            Assert.IsNotNull(element, "Обязательный элемент '" + required.ElementId + "' отсутствует в экране " + screenId + ".");
            Assert.AreEqual(
                required.ExpectedParentId,
                element.ParentId ?? string.Empty,
                "У '" + required.ElementId + "' родитель не совпадает с ожидаемым.");
        }
    }

    [Test]
    public void Database_Passes_Full_Validation()
    {
        UILayoutDatabaseAsset database = LoadDatabase();
        List<string> issues = new List<string>();
        database.CollectValidationIssues(issues);
        Assert.That(issues, Is.Empty, string.Join("\n", issues));
    }

    // Каждый targetName обязан существовать как name="..." в
    // Prototype_Main.uxml — иначе UI Конструктор ссылается в никуда
    // (см. UILayoutScreenBinder.ResolveTarget: Q<VisualElement>(targetName)).
    [TestCase(IncidentScreenId)]
    [TestCase(GameOverScreenId)]
    public void Element_TargetNames_Exist_In_Uxml(string screenId)
    {
        UILayoutDatabaseAsset database = LoadDatabase();
        UILayoutScreenDefinition screen = database.FindScreen(screenId);
        Assert.IsNotNull(screen);

        string uxmlPath = Path.Combine(
            Application.dataPath, "_Project", "UI", "Prototype", "Prototype_Main.uxml");
        string uxml = File.ReadAllText(uxmlPath);

        foreach (UILayoutElementDefinition element in screen.Elements)
        {
            string needle = "name=\"" + element.TargetName + "\"";
            StringAssert.Contains(
                needle, uxml,
                "targetName '" + element.TargetName + "' экрана " + screenId + " не найден в Prototype_Main.uxml.");
        }
    }
}

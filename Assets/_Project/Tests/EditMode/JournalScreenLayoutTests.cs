using System.Collections.Generic;
using System.IO;
using KingdomSurvival.UILayout;
using NUnit.Framework;
using UnityEngine;

// UI-M02 (ProjectDocs/UI_ARCHITECTURE.md §9): Journal переехал с полностью
// программного построения (старый PrototypeUIController.Journal.cs строил
// весь экран через new VisualElement/new Label) на реальный постоянный
// UXML-узел "journal-overlay" + экран "journal" в
// KingdomSurvivalUILayouts.asset — тот же паттерн, что уже работает для
// Camp (см. CampScreenLayoutTests.cs). Эти тесты фиксируют, что перенос не
// потерял ни одного обязательного узла и что имена в базе действительно
// совпадают с именами в Prototype_Main.uxml.
public sealed class JournalScreenLayoutTests
{
    private const string JournalScreenId = "journal";

    private static UILayoutDatabaseAsset LoadDatabase()
    {
        UILayoutDatabaseAsset database = Resources.Load<UILayoutDatabaseAsset>(UILayoutDatabaseAsset.ResourcesPath);
        Assert.IsNotNull(database, "Не найдена база UILayout Resources/" + UILayoutDatabaseAsset.ResourcesPath + ".asset");
        return database;
    }

    [Test]
    public void Journal_Screen_Is_Registered_And_AutoApplied()
    {
        UILayoutDatabaseAsset database = LoadDatabase();
        UILayoutScreenDefinition screen = database.FindScreen(JournalScreenId);

        Assert.IsNotNull(screen, "Экран 'journal' должен быть зарегистрирован в UILayoutDatabaseAsset.");
        Assert.IsTrue(screen.AutoApply, "Journal — обычный (не narrative-dialogue) экран: должен применяться generic-байндером.");
        Assert.AreEqual("journal-overlay", screen.RootName);
    }

    [Test]
    public void Journal_RequiredElements_AreAllResolvable_WithCorrectParents()
    {
        UILayoutDatabaseAsset database = LoadDatabase();
        UILayoutScreenDefinition screen = database.FindScreen(JournalScreenId);
        Assert.IsNotNull(screen);
        Assert.Greater(screen.RequiredElements.Count, 0, "У Journal должен быть непустой список обязательных элементов.");

        foreach (UILayoutRequiredElement required in screen.RequiredElements)
        {
            UILayoutElementDefinition element = screen.FindElement(required.ElementId);
            Assert.IsNotNull(element, "Обязательный элемент '" + required.ElementId + "' отсутствует в экране journal.");
            Assert.AreEqual(
                required.ExpectedParentId,
                element.ParentId ?? string.Empty,
                "У '" + required.ElementId + "' родитель не совпадает с ожидаемым.");
        }
    }

    [Test]
    public void Journal_Database_Passes_Full_Validation()
    {
        UILayoutDatabaseAsset database = LoadDatabase();
        List<string> issues = new List<string>();
        database.CollectValidationIssues(issues);
        Assert.That(issues, Is.Empty, string.Join("\n", issues));
    }

    // Каждый targetName в базе journal обязан существовать как name="..." в
    // Prototype_Main.uxml — иначе UI Конструктор ссылается в никуда
    // (см. UILayoutScreenBinder.ResolveTarget: Q<VisualElement>(targetName)).
    [Test]
    public void Journal_Element_TargetNames_Exist_In_Uxml()
    {
        UILayoutDatabaseAsset database = LoadDatabase();
        UILayoutScreenDefinition screen = database.FindScreen(JournalScreenId);
        Assert.IsNotNull(screen);

        string uxmlPath = Path.Combine(
            Application.dataPath, "_Project", "UI", "Prototype", "Prototype_Main.uxml");
        string uxml = File.ReadAllText(uxmlPath);

        foreach (UILayoutElementDefinition element in screen.Elements)
        {
            string needle = "name=\"" + element.TargetName + "\"";
            StringAssert.Contains(
                needle, uxml,
                "targetName '" + element.TargetName + "' экрана journal не найден в Prototype_Main.uxml.");
        }
    }

    // Шаблон повторяемой строки цели должен существовать и содержать все
    // узлы, которые CreateJournalGoalRow ищет через instance.Q<T>(...).
    [Test]
    public void JournalGoalRow_Template_Exists_With_Expected_Named_Elements()
    {
        string path = Path.Combine(
            Application.dataPath, "_Project", "UI", "Templates", "Resources", "Templates", "JournalGoalRow.uxml");
        string uxml = File.ReadAllText(path);

        StringAssert.Contains("name=\"journal-goal-row\"", uxml);
        StringAssert.Contains("name=\"journal-goal-row-title\"", uxml);
        StringAssert.Contains("name=\"journal-goal-row-badge\"", uxml);
        StringAssert.Contains("name=\"journal-goal-row-subtitle\"", uxml);
    }
}

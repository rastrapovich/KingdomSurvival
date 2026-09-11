using System.Collections.Generic;
using System.IO;
using KingdomSurvival.UILayout;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

// UI-M05: карточки построек больше не описаны 45 захардкоженными записями
// в KingdomSurvivalUILayouts.asset (по 9 полей на каждое из 5 зданий) —
// вместо этого один UXML-template (Templates/BuildingCard.uxml) на каждую
// BuildingDefinition. От capital-screen в базе остались только сам экран
// и building-grid; эти тесты фиксируют, что запись не разрослась обратно.
public sealed class BuildingCardsLayoutTests
{
    private const string CapitalScreenId = "capital-screen";

    private static UILayoutDatabaseAsset LoadDatabase()
    {
        UILayoutDatabaseAsset database = Resources.Load<UILayoutDatabaseAsset>(UILayoutDatabaseAsset.ResourcesPath);
        Assert.IsNotNull(database, "Не найдена база UILayout Resources/" + UILayoutDatabaseAsset.ResourcesPath + ".asset");
        return database;
    }

    [Test]
    public void Capital_Screen_Is_Registered_And_AutoApplied()
    {
        UILayoutDatabaseAsset database = LoadDatabase();
        UILayoutScreenDefinition screen = database.FindScreen(CapitalScreenId);

        Assert.IsNotNull(screen, "Экран 'capital-screen' должен быть зарегистрирован в UILayoutDatabaseAsset.");
        Assert.IsTrue(screen.AutoApply);
        Assert.AreEqual("capital-screen", screen.RootName);
    }

    [Test]
    public void Capital_Screen_No_Longer_Has_Hardcoded_Per_Building_Entries()
    {
        UILayoutDatabaseAsset database = LoadDatabase();
        UILayoutScreenDefinition screen = database.FindScreen(CapitalScreenId);
        Assert.IsNotNull(screen);

        foreach (UILayoutElementDefinition element in screen.Elements)
        {
            StringAssert.DoesNotStartWith(
                "building-card-fields_granaries", element.Id);
            StringAssert.DoesNotStartWith(
                "building-card-market", element.Id);
            StringAssert.DoesNotStartWith(
                "building-card-barracks", element.Id);
            StringAssert.DoesNotStartWith(
                "building-card-city_walls", element.Id);
            StringAssert.DoesNotStartWith(
                "building-card-mine", element.Id);
        }

        // capital-screen (корень) + building-grid — карточки зданий больше
        // не хранятся поэлементно в базе, они приходят из UXML-template.
        Assert.AreEqual(2, screen.Elements.Count);
    }

    [Test]
    public void Capital_Database_Passes_Full_Validation()
    {
        UILayoutDatabaseAsset database = LoadDatabase();
        List<string> issues = new List<string>();
        database.CollectValidationIssues(issues);
        Assert.That(issues, Is.Empty, string.Join("\n", issues));
    }

    // building-grid резолвится генерик-байндером строго по имени (см.
    // UILayoutScreenBinder.ResolveTarget), а не по классу — targetName
    // обязан существовать как name="..." в самой разметке.
    [Test]
    public void Capital_Element_TargetNames_Exist_In_Uxml()
    {
        UILayoutDatabaseAsset database = LoadDatabase();
        UILayoutScreenDefinition screen = database.FindScreen(CapitalScreenId);
        Assert.IsNotNull(screen);

        string uxmlPath = Path.Combine(
            Application.dataPath, "_Project", "UI", "Prototype", "Prototype_Main.uxml");
        string uxml = File.ReadAllText(uxmlPath);

        foreach (UILayoutElementDefinition element in screen.Elements)
        {
            string needle = "name=\"" + element.TargetName + "\"";
            StringAssert.Contains(
                needle, uxml,
                "targetName '" + element.TargetName + "' экрана capital-screen не найден в Prototype_Main.uxml.");
        }
    }

    [Test]
    public void BuildingCard_Template_Exists_With_All_Bindable_Parts()
    {
        VisualTreeAsset template = Resources.Load<VisualTreeAsset>("Templates/BuildingCard");
        Assert.IsNotNull(template, "Не найден Resources/Templates/BuildingCard.uxml.");

        TemplateContainer instance = template.Instantiate();

        Assert.IsNotNull(instance.Q<VisualElement>("building-card"));
        Assert.IsNotNull(instance.Q<Label>("building-card-title"));
        Assert.IsNotNull(instance.Q<Label>("building-card-status"));
        Assert.IsNotNull(instance.Q<Label>("building-card-image"));
        Assert.IsNotNull(instance.Q<Label>("building-card-description"));
        Assert.IsNotNull(instance.Q<Label>("building-card-effect"));
        Assert.IsNotNull(instance.Q<Label>("building-card-meta"));
        Assert.IsNotNull(instance.Q<ProgressBar>("building-card-progress"));
        Assert.IsNotNull(instance.Q<Button>("building-card-action"));
    }
}

using System.Collections.Generic;
using System.IO;
using KingdomSurvival.UILayout;
using NUnit.Framework;
using UnityEngine;

// ПР-07А-2: экран Дома заменил каталог построек. В UI-конструкторе у
// capital-screen два элемента — сам экран и образ Дома (home-scene);
// карточки забот, людей и мест состава строятся кодом. Все именованные
// части экрана, к которым привязывается код, обязаны быть в разметке.
public sealed class HomeScreenLayoutTests
{
    private const string CapitalScreenId = "capital-screen";

    private static readonly string[] BoundNames =
    {
        "capital-screen", "home-screen-scroll", "gold-label", "gold-income-label", "food-label",
        "food-income-label", "food-consumption-label", "population-label", "home-defense-label",
        "home-summary-gold", "home-summary-food", "home-summary-people", "home-summary-defense",
        "home-summary-detail", "home-away-notice", "home-main-row", "home-scene",
        "home-scene-water", "home-scene-mill", "home-scene-dam", "home-scene-walkway", "home-scene-yard",
        "home-scene-livestock", "home-objects-list", "home-cares-list", "home-people-maintenance",
        "home-people-care", "home-activities-panel", "home-activities-list", "home-activities-empty",
        "home-people-panel", "home-people-summary", "home-people-list", "home-people-detail",
        "home-prep-panel", "home-prep-title", "home-prep-message", "home-prep-route-button",
        "home-prep-slots", "home-prep-candidates", "home-prep-forecast"
    };

    private static UILayoutDatabaseAsset LoadDatabase()
    {
        UILayoutDatabaseAsset database = Resources.Load<UILayoutDatabaseAsset>(UILayoutDatabaseAsset.ResourcesPath);
        Assert.IsNotNull(database, "Не найдена база UILayout Resources/" + UILayoutDatabaseAsset.ResourcesPath + ".asset");
        return database;
    }

    private static string ReadMainUxml()
    {
        return File.ReadAllText(Path.Combine(Application.dataPath, "_Project", "UI", "Prototype", "Prototype_Main.uxml"));
    }

    [Test]
    public void CapitalScreen_IsRegistered_WithHomeSceneOnly()
    {
        UILayoutScreenDefinition screen = LoadDatabase().FindScreen(CapitalScreenId);
        Assert.IsNotNull(screen);
        Assert.IsTrue(screen.AutoApply);
        Assert.AreEqual("capital-screen", screen.RootName);
        Assert.AreEqual(2, screen.Elements.Count);
        Assert.IsNotNull(screen.FindElement("home-scene"));
    }

    [Test]
    public void CapitalElement_TargetNames_ExistInUxml()
    {
        UILayoutScreenDefinition screen = LoadDatabase().FindScreen(CapitalScreenId);
        string uxml = ReadMainUxml();
        foreach (UILayoutElementDefinition element in screen.Elements)
            StringAssert.Contains("name=\"" + element.TargetName + "\"", uxml);
    }

    [Test]
    public void BoundHomeParts_ExistInUxml()
    {
        string uxml = ReadMainUxml();
        foreach (string name in BoundNames)
            StringAssert.Contains("name=\"" + name + "\"", uxml, name);
    }

    [Test]
    public void OldCatalogMoodAndDefeat_AreGoneFromUxml()
    {
        string uxml = ReadMainUxml();
        foreach (string gone in new[] { "building-grid", "mood-label", "game-over-overlay", "gold-minus10-button", "home-people-work-row" })
            StringAssert.DoesNotContain("name=\"" + gone + "\"", uxml, gone);
        Assert.IsNull(LoadDatabase().FindScreen("game-over"));
    }

    [Test]
    public void Database_Passes_Full_Validation()
    {
        List<string> issues = new List<string>();
        LoadDatabase().CollectValidationIssues(issues);
        Assert.That(issues, Is.Empty, string.Join("\n", issues));
    }
}

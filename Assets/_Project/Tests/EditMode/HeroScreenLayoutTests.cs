using System.Collections.Generic;
using System.IO;
using KingdomSurvival.UILayout;
using NUnit.Framework;
using UnityEngine;

// UI-M03 (ProjectDocs/UI_ARCHITECTURE.md §9): Hero Screen переехал с
// полностью программного построения (старый PrototypeUIController.
// HeroScreen.cs строил весь экран через new VisualElement/new Label и
// собственные хелперы CreateHeroScreenPanel/StyleHeroScreenButton/цвета) на
// реальный постоянный UXML-узел "hero-screen-overlay" + экран "hero-screen"
// в KingdomSurvivalUILayouts.asset — та же модель, что уже работает для
// Camp/Journal (см. CampScreenLayoutTests.cs/JournalScreenLayoutTests.cs).
// Экран "hero-screen" уже существовал в базе (частично, ~57 элементов) —
// этот проход заменил запись полностью на схему, сгенерированную напрямую
// из фактического UXML, поэтому в требованиях покрыты и старые, и новые
// (supply/tooltips/roster-available/roster-card sub-элементы) узлы.
public sealed class HeroScreenLayoutTests
{
    private const string HeroScreenId = "hero-screen";

    private static UILayoutDatabaseAsset LoadDatabase()
    {
        UILayoutDatabaseAsset database = Resources.Load<UILayoutDatabaseAsset>(UILayoutDatabaseAsset.ResourcesPath);
        Assert.IsNotNull(database, "Не найдена база UILayout Resources/" + UILayoutDatabaseAsset.ResourcesPath + ".asset");
        return database;
    }

    [Test]
    public void HeroScreen_Screen_Is_Registered_And_AutoApplied()
    {
        UILayoutDatabaseAsset database = LoadDatabase();
        UILayoutScreenDefinition screen = database.FindScreen(HeroScreenId);

        Assert.IsNotNull(screen, "Экран 'hero-screen' должен быть зарегистрирован в UILayoutDatabaseAsset.");
        Assert.IsTrue(screen.AutoApply, "Hero Screen — обычный (не narrative-dialogue) экран: должен применяться generic-байндером.");
        Assert.AreEqual("hero-screen-overlay", screen.RootName);
    }

    [Test]
    public void HeroScreen_RequiredElements_AreAllResolvable_WithCorrectParents()
    {
        UILayoutDatabaseAsset database = LoadDatabase();
        UILayoutScreenDefinition screen = database.FindScreen(HeroScreenId);
        Assert.IsNotNull(screen);
        Assert.Greater(screen.RequiredElements.Count, 0, "У Hero Screen должен быть непустой список обязательных элементов.");

        foreach (UILayoutRequiredElement required in screen.RequiredElements)
        {
            UILayoutElementDefinition element = screen.FindElement(required.ElementId);
            Assert.IsNotNull(element, "Обязательный элемент '" + required.ElementId + "' отсутствует в экране hero-screen.");
            Assert.AreEqual(
                required.ExpectedParentId,
                element.ParentId ?? string.Empty,
                "У '" + required.ElementId + "' родитель не совпадает с ожидаемым.");
        }
    }

    [Test]
    public void HeroScreen_Database_Passes_Full_Validation()
    {
        UILayoutDatabaseAsset database = LoadDatabase();
        List<string> issues = new List<string>();
        database.CollectValidationIssues(issues);
        Assert.That(issues, Is.Empty, string.Join("\n", issues));
    }

    // Каждый targetName в базе hero-screen обязан существовать как
    // name="..." в Prototype_Main.uxml — иначе UI Конструктор ссылается в
    // никуда (см. UILayoutScreenBinder.ResolveTarget: Q<VisualElement>(targetName)).
    [Test]
    public void HeroScreen_Element_TargetNames_Exist_In_Uxml()
    {
        UILayoutDatabaseAsset database = LoadDatabase();
        UILayoutScreenDefinition screen = database.FindScreen(HeroScreenId);
        Assert.IsNotNull(screen);

        string uxmlPath = Path.Combine(
            Application.dataPath, "_Project", "UI", "Prototype", "Prototype_Main.uxml");
        string uxml = File.ReadAllText(uxmlPath);

        foreach (UILayoutElementDefinition element in screen.Elements)
        {
            string needle = "name=\"" + element.TargetName + "\"";
            StringAssert.Contains(
                needle, uxml,
                "targetName '" + element.TargetName + "' экрана hero-screen не найден в Prototype_Main.uxml.");
        }
    }

    // Пять фиксированных карточек состава (командир + 4 бойца) — каждая
    // должна иметь все sub-элементы, которые контроллер реально биндит
    // (FillHeroScreenRosterCard/WireHeroScreenRosterCardInteractions).
    [TestCase("hero-screen-roster-commander")]
    [TestCase("hero-screen-roster-fighter-1")]
    [TestCase("hero-screen-roster-fighter-2")]
    [TestCase("hero-screen-roster-fighter-3")]
    [TestCase("hero-screen-roster-fighter-4")]
    public void HeroScreen_RosterCard_Has_All_Bound_SubElements(string cardName)
    {
        UILayoutDatabaseAsset database = LoadDatabase();
        UILayoutScreenDefinition screen = database.FindScreen(HeroScreenId);
        Assert.IsNotNull(screen);

        string[] suffixes =
        {
            "", "-portrait", "-name", "-role", "-hp", "-condition", "-level", "-healthbar", "-role-chip", "-hint"
        };

        foreach (string suffix in suffixes)
        {
            string id = cardName + suffix;
            Assert.IsNotNull(screen.FindElement(id), "В экране hero-screen отсутствует элемент карточки состава '" + id + "'.");
        }
    }

    // Шесть качеств и семь боевых характеристик — фиксированные слоты,
    // каждый должен иметь бокс и лейбл значения.
    [Test]
    public void HeroScreen_Qualities_And_Stats_Have_Fixed_Value_Slots()
    {
        UILayoutDatabaseAsset database = LoadDatabase();
        UILayoutScreenDefinition screen = database.FindScreen(HeroScreenId);
        Assert.IsNotNull(screen);

        string[] qualitySuffixes = { "strength", "dexterity", "fortitude", "instinct", "judgment", "character" };
        foreach (string suffix in qualitySuffixes)
        {
            Assert.IsNotNull(screen.FindElement("hero-screen-quality-" + suffix));
            Assert.IsNotNull(screen.FindElement("hero-screen-quality-" + suffix + "-value"));
        }

        string[] statSuffixes = { "health", "attack", "defense", "damage", "movement", "initiative", "attack-range" };
        foreach (string suffix in statSuffixes)
        {
            Assert.IsNotNull(screen.FindElement("hero-screen-stat-" + suffix + "-value"));
            Assert.IsNotNull(screen.FindElement("hero-screen-unit-card-stat-" + suffix));
            Assert.IsNotNull(screen.FindElement("hero-screen-unit-card-stat-" + suffix + "-value"));
        }
    }
}

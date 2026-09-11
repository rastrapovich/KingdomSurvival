using System;
using System.Collections.Generic;
using System.IO;
using KingdomSurvival.UILayout;
using NUnit.Framework;
using UnityEngine;

// Camp Screen v1 переехал с полностью программного построения (старый
// PrototypeUIController.Camp.cs) на реальный постоянный UXML-узел
// "camp-screen" + экран "camp" в KingdomSurvivalUILayouts.asset — тот же
// паттерн, что уже работает для expeditions-screen (см. production-
// инструкцию по переработке Camp). Эти тесты фиксируют, что перенос не
// потерял ни одного обязательного узла и что имена в базе действительно
// совпадают с именами в Prototype_Main.uxml — без этого UI Конструктор
// молча указывал бы в пустоту.
public sealed class CampScreenLayoutTests
{
    private const string CampScreenId = "camp";

    private static UILayoutDatabaseAsset LoadDatabase()
    {
        UILayoutDatabaseAsset database = Resources.Load<UILayoutDatabaseAsset>(UILayoutDatabaseAsset.ResourcesPath);
        Assert.IsNotNull(database, "Не найдена база UILayout Resources/" + UILayoutDatabaseAsset.ResourcesPath + ".asset");
        return database;
    }

    [Test]
    public void Camp_Screen_Is_Registered_And_AutoApplied()
    {
        UILayoutDatabaseAsset database = LoadDatabase();
        UILayoutScreenDefinition screen = database.FindScreen(CampScreenId);

        Assert.IsNotNull(screen, "Экран 'camp' должен быть зарегистрирован в UILayoutDatabaseAsset.");
        Assert.IsTrue(screen.AutoApply, "Camp — обычный (не narrative-dialogue) экран: должен применяться generic-байндером.");
        Assert.AreEqual("camp-screen", screen.RootName);
    }

    [Test]
    public void Camp_RequiredElements_AreAllResolvable_WithCorrectParents()
    {
        UILayoutDatabaseAsset database = LoadDatabase();
        UILayoutScreenDefinition screen = database.FindScreen(CampScreenId);
        Assert.IsNotNull(screen);
        Assert.Greater(screen.RequiredElements.Count, 0, "У Camp должен быть непустой список обязательных элементов.");

        foreach (UILayoutRequiredElement required in screen.RequiredElements)
        {
            UILayoutElementDefinition element = screen.FindElement(required.ElementId);
            Assert.IsNotNull(element, "Обязательный элемент '" + required.ElementId + "' отсутствует в экране camp.");
            Assert.AreEqual(
                required.ExpectedParentId,
                element.ParentId ?? string.Empty,
                "У '" + required.ElementId + "' родитель не совпадает с ожидаемым.");
        }
    }

    [Test]
    public void Camp_Art_Element_Is_Image_Kind()
    {
        UILayoutDatabaseAsset database = LoadDatabase();
        UILayoutElementDefinition art = database.FindScreen(CampScreenId).FindElement("camp-art");

        Assert.IsNotNull(art);
        Assert.AreEqual(UILayoutElementKind.Image, art.Kind);
        Assert.AreEqual("camp-art", art.TargetName);
    }

    // Раздел 26 инструкции: "изменение Sprite в UILayoutDatabase не требует
    // изменения C#" — это верно ровно тогда, когда camp-art в принципе
    // применяется байндером хоть с каким-то содержимым. Сам override пока
    // выключен по умолчанию (раздел "Default_Database_Does_Not_Enable_
    // Legacy_Override_Flags" — общий инвариант всей базы), но Kind.Image +
    // валидный TargetName достаточны, чтобы дизайнер включил
    // overrideBackground и назначил Sprite без единой правки кода.
    [Test]
    public void Camp_Database_Passes_Full_Validation()
    {
        UILayoutDatabaseAsset database = LoadDatabase();
        List<string> issues = new List<string>();
        database.CollectValidationIssues(issues);
        Assert.That(issues, Is.Empty, string.Join("\n", issues));
    }

    // Каждый targetName в базе camp обязан существовать как name="..." в
    // Prototype_Main.uxml — иначе UI Конструктор ссылается в никуда
    // (см. UILayoutScreenBinder.ResolveTarget: Q<VisualElement>(targetName)).
    [Test]
    public void Camp_Element_TargetNames_Exist_In_Uxml()
    {
        UILayoutDatabaseAsset database = LoadDatabase();
        UILayoutScreenDefinition screen = database.FindScreen(CampScreenId);
        Assert.IsNotNull(screen);

        string uxmlPath = Path.Combine(
            Application.dataPath, "_Project", "UI", "Prototype", "Prototype_Main.uxml");
        string uxml = File.ReadAllText(uxmlPath);

        foreach (UILayoutElementDefinition element in screen.Elements)
        {
            string needle = "name=\"" + element.TargetName + "\"";
            StringAssert.Contains(
                needle, uxml,
                "targetName '" + element.TargetName + "' экрана camp не найден в Prototype_Main.uxml.");
        }
    }
}

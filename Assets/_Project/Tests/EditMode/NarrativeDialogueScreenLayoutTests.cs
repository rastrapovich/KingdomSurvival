using System.Collections.Generic;
using System.IO;
using KingdomSurvival.UILayout;
using NUnit.Framework;
using UnityEngine;

// UI-M04 (ProjectDocs/UI_ARCHITECTURE.md §9): Диалог переехал с
// EnsureNarrativeDialogueUi()/ApplyNarrativeLayout()/ReparentNarrativeElement()/
// ApplyLayoutElement() (собственный код применения, autoApply=false) на
// постоянный UXML-узел "narrative-dialogue-overlay" в Prototype_Main.uxml +
// Narrative.uss, применяемый общим UILayoutScreenBinder (autoApply=true) —
// тот же паттерн, что уже подтверждён для Journal/Hero Screen. Родительство
// speaker/role — дети overlay, а не panel (см. requiredElements ниже): так
// они позиционируются рядом с портретом, а не внутри текстовой панели —
// это единственный экран, где родительство важно проверить явно, потому
// что раньше его расставлял собственный ReparentNarrativeElement().
public sealed class NarrativeDialogueScreenLayoutTests
{
    private const string NarrativeDialogueScreenId = "narrative-dialogue";

    private static UILayoutDatabaseAsset LoadDatabase()
    {
        UILayoutDatabaseAsset database = Resources.Load<UILayoutDatabaseAsset>(UILayoutDatabaseAsset.ResourcesPath);
        Assert.IsNotNull(database, "Не найдена база UILayout Resources/" + UILayoutDatabaseAsset.ResourcesPath + ".asset");
        return database;
    }

    [Test]
    public void Narrative_Screen_Is_Registered_And_AutoApplied()
    {
        UILayoutDatabaseAsset database = LoadDatabase();
        UILayoutScreenDefinition screen = database.FindScreen(NarrativeDialogueScreenId);

        Assert.IsNotNull(screen, "Экран 'narrative-dialogue' должен быть зарегистрирован в UILayoutDatabaseAsset.");
        Assert.IsTrue(screen.AutoApply, "Narrative Dialogue — обычный экран после UI-M04: должен применяться generic-байндером.");
        Assert.AreEqual("narrative-dialogue-overlay", screen.RootName);
    }

    [Test]
    public void Narrative_RequiredElements_AreAllResolvable_WithCorrectParents()
    {
        UILayoutDatabaseAsset database = LoadDatabase();
        UILayoutScreenDefinition screen = database.FindScreen(NarrativeDialogueScreenId);
        Assert.IsNotNull(screen);
        Assert.Greater(screen.RequiredElements.Count, 0, "У Narrative Dialogue должен быть непустой список обязательных элементов.");

        foreach (UILayoutRequiredElement required in screen.RequiredElements)
        {
            UILayoutElementDefinition element = screen.FindElement(required.ElementId);
            Assert.IsNotNull(element, "Обязательный элемент '" + required.ElementId + "' отсутствует в экране narrative-dialogue.");
            Assert.AreEqual(
                required.ExpectedParentId,
                element.ParentId ?? string.Empty,
                "У '" + required.ElementId + "' родитель не совпадает с ожидаемым.");
        }
    }

    // Speaker/role — дети overlay (не panel): позиционируются рядом с
    // портретом. Text/choices — дети panel. Portrait — тоже ребёнок overlay.
    // Это критично сверить явно: до UI-M04 родительство расставлял
    // собственный код (ReparentNarrativeElement), теперь — только UXML.
    [Test]
    public void Narrative_Speaker_And_Role_Are_Children_Of_Overlay_Not_Panel()
    {
        UILayoutDatabaseAsset database = LoadDatabase();
        UILayoutScreenDefinition screen = database.FindScreen(NarrativeDialogueScreenId);
        Assert.IsNotNull(screen);

        Assert.AreEqual("overlay", screen.FindElement("speaker")?.ParentId);
        Assert.AreEqual("overlay", screen.FindElement("role")?.ParentId);
        Assert.AreEqual("overlay", screen.FindElement("portrait")?.ParentId);
        Assert.AreEqual("overlay", screen.FindElement("panel")?.ParentId);
        Assert.AreEqual("panel", screen.FindElement("text")?.ParentId);
        Assert.AreEqual("panel", screen.FindElement("choices")?.ParentId);
    }

    [Test]
    public void Narrative_Database_Passes_Full_Validation()
    {
        UILayoutDatabaseAsset database = LoadDatabase();
        List<string> issues = new List<string>();
        database.CollectValidationIssues(issues);
        Assert.That(issues, Is.Empty, string.Join("\n", issues));
    }

    // Каждый targetName в базе narrative-dialogue обязан существовать как
    // name="..." в Prototype_Main.uxml — иначе UI Конструктор ссылается в
    // никуда (см. UILayoutScreenBinder.ResolveTarget: Q<VisualElement>(targetName)).
    [Test]
    public void Narrative_Element_TargetNames_Exist_In_Uxml()
    {
        UILayoutDatabaseAsset database = LoadDatabase();
        UILayoutScreenDefinition screen = database.FindScreen(NarrativeDialogueScreenId);
        Assert.IsNotNull(screen);

        string uxmlPath = Path.Combine(
            Application.dataPath, "_Project", "UI", "Prototype", "Prototype_Main.uxml");
        string uxml = File.ReadAllText(uxmlPath);

        foreach (UILayoutElementDefinition element in screen.Elements)
        {
            string needle = "name=\"" + element.TargetName + "\"";
            StringAssert.Contains(
                needle, uxml,
                "targetName '" + element.TargetName + "' экрана narrative-dialogue не найден в Prototype_Main.uxml.");
        }
    }

    // narrative-check-tooltip не входит в записи UI Конструктора (позиция
    // считается на лету относительно anchor'а — PositionNarrativeCheckTooltip),
    // но обязан существовать как постоянный узел UXML, потому что
    // PrototypeUIController.NarrativeCheckPresentation.cs теперь только
    // находит его через BindRequiredElement, а не создаёт.
    [Test]
    public void Narrative_Check_Tooltip_Node_Exists_In_Uxml()
    {
        string uxmlPath = Path.Combine(
            Application.dataPath, "_Project", "UI", "Prototype", "Prototype_Main.uxml");
        string uxml = File.ReadAllText(uxmlPath);

        StringAssert.Contains("name=\"narrative-check-tooltip\"", uxml);
    }

    // Шаблоны повторяемого контента (кнопка варианта ответа/вторичная
    // строка/строка реплики игрока) должны существовать и содержать узлы,
    // которые PrototypeUIController.Narrative.cs ищет через
    // InstantiateFlatTemplate<T>(template, rootName).
    [Test]
    public void Narrative_Templates_Exist_With_Expected_Named_Root()
    {
        AssertTemplateHasRoot("NarrativeChoiceButton.uxml", "narrative-dialogue-choice");
        AssertTemplateHasRoot("NarrativeChoiceSecondary.uxml", "narrative-dialogue-choice-secondary");
        AssertTemplateHasRoot("NarrativeHistoryPlayerLine.uxml", "narrative-dialogue-history-player");
    }

    private static void AssertTemplateHasRoot(string fileName, string expectedName)
    {
        string path = Path.Combine(
            Application.dataPath, "_Project", "UI", "Templates", "Resources", "Templates", fileName);
        string uxml = File.ReadAllText(path);
        StringAssert.Contains("name=\"" + expectedName + "\"", uxml, fileName + " должен содержать узел '" + expectedName + "'.");
    }
}

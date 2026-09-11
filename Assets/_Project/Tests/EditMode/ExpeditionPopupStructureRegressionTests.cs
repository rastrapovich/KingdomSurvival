using System.IO;
using NUnit.Framework;
using UnityEngine;

// UI-M06 regression: карточки экспедиций клонируются из
// Templates/ExpeditionLocationCard.uxml вместо ручной сборки в C#, и
// бывший PrototypeUIController.NavigationAndInteractionFixes.cs (хрупкий
// позиционный поиск card.ElementAt(1)/row.ElementAt(1), дублирующий
// скрытый Label поверх исходного) полностью влит в ExpeditionViews.cs —
// второго файла, патчащего те же карточки, больше нет.
public sealed class ExpeditionPopupStructureRegressionTests
{
    [Test]
    public void NavigationAndInteractionFixes_File_No_Longer_Exists()
    {
        string path = Path.Combine(
            Application.dataPath, "_Project", "UI",
            "PrototypeUIController.NavigationAndInteractionFixes.cs");
        Assert.IsFalse(
            File.Exists(path),
            "Патч-файл должен быть полностью влит в PrototypeUIController.ExpeditionViews.cs.");
    }

    [Test]
    public void ExpeditionViews_Controller_Does_Not_Build_Cards_By_Hand_In_CSharp()
    {
        string source = ReadUiFile("PrototypeUIController.ExpeditionViews.cs");

        StringAssert.DoesNotContain(
            "new VisualElement(",
            source,
            "Карточки экспедиций должны клонироваться из UXML-template, а не собираться через new VisualElement(...).");
        StringAssert.DoesNotContain(
            "new Label(",
            source,
            "ExpeditionViews не должен создавать Label через код — текст задаётся на элементе из template.");
        StringAssert.DoesNotContain(
            "new Button(",
            source,
            "Кнопка действия карточки должна приходить из template, а не создаваться в коде.");
        StringAssert.Contains(
            "Instantiate()",
            source,
            "ExpeditionViews должен клонировать карточку через VisualTreeAsset.Instantiate().");
    }

    [Test]
    public void ExpeditionViews_Controller_Has_Exactly_One_Instantiate_Call_Site()
    {
        string source = ReadUiFile("PrototypeUIController.ExpeditionViews.cs");

        int occurrences = 0;
        int index = 0;
        while ((index = source.IndexOf("Instantiate()", index)) >= 0)
        {
            occurrences++;
            index += "Instantiate()".Length;
        }

        Assert.AreEqual(
            1, occurrences,
            "Ровно одна точка клонирования карточки локации — цикл по gameState.Locations, без спец-обработки по id.");
    }

    [Test]
    public void ExpeditionViews_Controller_Has_No_Positional_Element_Lookup()
    {
        // Бывший хрупкий приём NavigationAndInteractionFixes.cs — искать
        // части карточки по индексу ребёнка, а не по имени.
        string source = ReadUiFile("PrototypeUIController.ExpeditionViews.cs");

        StringAssert.DoesNotContain("ElementAt(", source);
    }

    [Test]
    public void ExpeditionViews_Controller_Does_Not_Reference_Dead_Large_Image_Binding()
    {
        // BindLargeExpeditionImages()/bigExpeditionImages искали
        // .location-card/.location-name/.location-image-placeholder —
        // классов с такими именами в текущем Prototype_Main.uxml нет уже
        // давно, это был мёртвый код.
        string source = ReadUiFile("PrototypeUIController.ExpeditionViews.cs");

        StringAssert.DoesNotContain("BindLargeExpeditionImages", source);
        StringAssert.DoesNotContain("bigExpeditionImages", source);
        StringAssert.DoesNotContain("location-card", source);
    }

    private static string ReadUiFile(string fileName)
    {
        string path = Path.Combine(Application.dataPath, "_Project", "UI", fileName);
        return File.ReadAllText(path);
    }
}

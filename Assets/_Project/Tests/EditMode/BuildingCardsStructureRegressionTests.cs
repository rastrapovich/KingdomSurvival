using System.IO;
using NUnit.Framework;
using UnityEngine;

// UI-M05 regression: карточки построек клонируются из Templates/BuildingCard.uxml
// (VisualTreeAsset.Instantiate()) вместо того, чтобы собираться `new
// VisualElement()`/`new Label()` в C# с инлайновыми style.* на каждую из
// 5 построек (ProjectDocs/UI_ARCHITECTURE.md §30/§11 — тот же приём
// защиты, что уже применяется в CampUiRefreshTests.cs/
// JournalScreenStructureRegressionTests.cs).
public sealed class BuildingCardsStructureRegressionTests
{
    [Test]
    public void Buildings_Controller_Does_Not_Build_Cards_By_Hand_In_CSharp()
    {
        string source = ReadUiFile("PrototypeUIController.Buildings.cs");

        StringAssert.DoesNotContain(
            "new VisualElement(",
            source,
            "Карточки построек должны клонироваться из UXML-template, а не собираться через new VisualElement(...).");
        StringAssert.DoesNotContain(
            "new Label(",
            source,
            "Buildings не должен создавать Label через код — текст задаётся на элементе из template.");
        StringAssert.DoesNotContain(
            "new ProgressBar",
            source,
            "ProgressBar карточки должен приходить из template, а не создаваться в коде.");
        StringAssert.DoesNotContain(
            "new Button",
            source,
            "Кнопка действия карточки должна приходить из template, а не создаваться в коде.");
        StringAssert.Contains(
            "Instantiate()",
            source,
            "Buildings должен клонировать карточку через VisualTreeAsset.Instantiate().");
    }

    [Test]
    public void Buildings_Controller_Has_Exactly_One_Instantiate_Call_Site()
    {
        string source = ReadUiFile("PrototypeUIController.Buildings.cs");

        int occurrences = 0;
        int index = 0;
        while ((index = source.IndexOf("Instantiate()", index)) >= 0)
        {
            occurrences++;
            index += "Instantiate()".Length;
        }

        Assert.AreEqual(
            1, occurrences,
            "Ровно одна точка клонирования карточки постройки — цикл по BuildingSystem.GetDefinitions(), без спец-обработки по id.");
    }

    private static string ReadUiFile(string fileName)
    {
        string path = Path.Combine(Application.dataPath, "_Project", "UI", fileName);
        return File.ReadAllText(path);
    }
}

using System.IO;
using NUnit.Framework;
using UnityEngine;

// UI-M08 regression: карточка осмотра локации биндится по имени
// (BindWorldMapLocationCard/BindWorldMapLocationActionElements), а не
// собирается и не переупорядочивается вручную в C#.
public sealed class WorldMapLocationCardStructureRegressionTests
{
    [Test]
    public void WorldMapInteractionPolish_Does_Not_Build_Location_Card_By_Hand()
    {
        string source = ReadUiFile("PrototypeUIController.WorldMapInteractionPolish.cs");

        StringAssert.DoesNotContain(
            "new VisualElement",
            source,
            "Карточка осмотра локации должна биндиться из статичного UXML-узла, а не собираться через new VisualElement(...).");
        StringAssert.DoesNotContain(
            "new Label(",
            source,
            "WorldMapInteractionPolish не должен создавать Label через код — текст задаётся на элементе из UXML.");
        StringAssert.DoesNotContain(
            "new Button(",
            source,
            "Кнопка закрытия карточки должна приходить из UXML, а не создаваться в коде.");
        StringAssert.DoesNotContain("EnsureWorldMapLocationCard", source);
        StringAssert.Contains("BindWorldMapLocationCard", source);
    }

    [Test]
    public void WorldMapLocationActions_Does_Not_Build_Or_Reorder_Elements_By_Hand()
    {
        string source = ReadUiFile("PrototypeUIController.WorldMapLocationActions.cs");

        StringAssert.DoesNotContain(
            "new Label",
            source,
            "Метка присутствия армии должна приходить из UXML, а не создаваться в коде.");
        StringAssert.DoesNotContain(
            "new Button(",
            source,
            "Кнопка исследования должна приходить из UXML, а не создаваться в коде.");
        StringAssert.DoesNotContain(
            "RemoveFromHierarchy",
            source,
            "Порядок частей карточки задаётся разметкой — переупорядочивать элементы кодом больше не нужно.");
        StringAssert.DoesNotContain("EnsureWorldMapLocationActionElements", source);
        StringAssert.Contains("BindWorldMapLocationActionElements", source);
    }

    private static string ReadUiFile(string fileName)
    {
        string path = Path.Combine(Application.dataPath, "_Project", "UI", fileName);
        return File.ReadAllText(path);
    }
}

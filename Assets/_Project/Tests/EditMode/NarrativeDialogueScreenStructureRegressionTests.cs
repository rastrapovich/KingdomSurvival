using System.IO;
using NUnit.Framework;
using UnityEngine;

// Regression-тест ProjectDocs/UI_ARCHITECTURE.md §11 (ключевой
// regression-тест архитектуры): "Production fullscreen screens must not
// build their static visual tree in C#." Narrative Dialogue (UI-M04) — самый
// чувствительный экран этой миграции: этот файл защищает его от отката к
// программному построению постоянной структуры, тем же приёмом (чтение
// файла + StringAssert), что уже применяется в CampUiRefreshTests.cs/
// JournalScreenStructureRegressionTests.cs/HeroScreenStructureRegressionTests.cs.
//
// Сборка ResponseGroup (BuildNarrativeHistoryGroupElement и построение
// заголовков проверки в PrototypeUIController.NarrativeCheckPresentation.cs)
// сознательно НЕ входит в проверяемый список: это переменная по форме
// динамическая композиция (варианты диалога/текстовые блоки — легитимная
// динамика по §2 доктрины), а не постоянная структура экрана.
public sealed class NarrativeDialogueScreenStructureRegressionTests
{
    // С открывающей скобкой: "ApplyNarrativeLayout(" не должен ловить
    // легитимный, сохранённый метод ApplyNarrativeLayoutPresentation(...) из
    // PrototypeUIController.NarrativePresentation.cs.
    private static readonly string[] ForbiddenBuildMethods =
    {
        "EnsureNarrativeDialogueUi(",
        "ApplyNarrativeLayout(",
        "ReparentNarrativeElement(",
        "ApplyLayoutElement(",
        "EnsureNarrativeCheckTooltip(",
    };

    [Test]
    public void Narrative_Controller_Does_Not_Build_Its_Static_Visual_Tree_In_CSharp()
    {
        string source = ReadUiFile("PrototypeUIController.Narrative.cs");

        foreach (string method in ForbiddenBuildMethods)
        {
            StringAssert.DoesNotContain(
                method,
                source,
                "Narrative Dialogue не должен строить постоянную структуру через C# (" + method + "): " +
                "она должна жить в Prototype_Main.uxml/Narrative.uss (ProjectDocs/UI_ARCHITECTURE.md).");
        }
    }

    [Test]
    public void Narrative_Controller_No_Longer_Loads_Old_Runtime_Stylesheet()
    {
        string source = ReadUiFile("PrototypeUIController.Narrative.cs");

        // Старый Resources/Prototype_Narrative.uss загружался и добавлялся в
        // interfaceRoot.styleSheets вручную из EnsureNarrativeDialogueUi() —
        // после UI-M04 стиль подключается статично через
        // Prototype_Main.uxml (<Style src="Narrative.uss" />).
        StringAssert.DoesNotContain("Prototype_Narrative", source);
        StringAssert.DoesNotContain("interfaceRoot.styleSheets.Add", source);
    }

    [Test]
    public void Narrative_Controller_Uses_Templates_For_Choices_And_Player_History_Line()
    {
        string source = ReadUiFile("PrototypeUIController.Narrative.cs");

        StringAssert.Contains(
            "Instantiate()",
            source,
            "Кнопки вариантов ответа/строка реплики игрока должны клонироваться из UXML-template.");
        StringAssert.Contains("Templates/NarrativeChoiceButton", source);
        StringAssert.Contains("Templates/NarrativeChoiceSecondary", source);
        StringAssert.Contains("Templates/NarrativeHistoryPlayerLine", source);
    }

    [Test]
    public void Narrative_Check_Tooltip_Is_Bound_Not_Constructed()
    {
        string source = ReadUiFile("PrototypeUIController.NarrativeCheckPresentation.cs");

        StringAssert.DoesNotContain(
            "new VisualElement { name = \"narrative-check-tooltip\"",
            source,
            "narrative-check-tooltip должен быть постоянным узлом Prototype_Main.uxml, привязанным через BindRequiredElement, а не создаваться в коде.");
    }

    private static string ReadUiFile(string fileName)
    {
        string path = Path.Combine(Application.dataPath, "_Project", "UI", fileName);
        return File.ReadAllText(path);
    }
}

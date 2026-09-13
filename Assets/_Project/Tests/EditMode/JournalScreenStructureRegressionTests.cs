using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;

// Regression-тест ProjectDocs/UI_ARCHITECTURE.md §30/§11 (ключевой
// regression-тест архитектуры): "Production fullscreen screens must not
// build their static visual tree in C#." Journal (UI-M02) — второй экран,
// прошедший эту миграцию после Camp; этот файл защищает его от отката к
// программному построению дерева, тем же приёмом (чтение файла +
// StringAssert), что уже применяется в CampUiRefreshTests.cs.
public sealed class JournalScreenStructureRegressionTests
{
    private static readonly string[] ForbiddenBuildMethods =
    {
        "BuildJournalScreen",
        "BuildJournalHeader",
        "BuildJournalTabs",
        "BuildJournalGoalsColumn",
        "BuildJournalGoalSection",
        "BuildJournalDetailColumn",
    };

    [Test]
    public void Journal_Controller_Does_Not_Build_Its_Static_Visual_Tree_In_CSharp()
    {
        string source = ReadUiFile("PrototypeUIController.Journal.cs");

        foreach (string method in ForbiddenBuildMethods)
        {
            StringAssert.DoesNotContain(
                method,
                source,
                "Journal не должен строить постоянную структуру через C# (" + method + "): " +
                "она должна жить в Prototype_Main.uxml/Journal.uss (ProjectDocs/UI_ARCHITECTURE.md).");
        }
    }

    [Test]
    public void Journal_Controller_Uses_Template_For_Goal_Rows_Instead_Of_New_VisualElement()
    {
        string source = ReadUiFile("PrototypeUIController.Journal.cs");

        StringAssert.DoesNotContain(
            "new VisualElement(",
            source,
            "Строки целей Journal должны клонироваться из UXML-template, а не собираться через new VisualElement(...).");
        StringAssert.DoesNotContain(
            "new Label(",
            source,
            "Journal не должен создавать Label через код — текст берётся из UXML или задаётся на элементе из template.");
        StringAssert.Contains(
            "Instantiate()",
            source,
            "Journal должен клонировать строку цели через VisualTreeAsset.Instantiate().");
    }

    [Test]
    public void Journal_Controller_No_Longer_Depends_On_HeroScreen_Private_Helpers()
    {
        string source = ReadUiFile("PrototypeUIController.Journal.cs");

        // Раньше Journal напрямую переиспользовал приватные хелперы/цвета
        // Hero Screen (CreateHeroScreenPanel/StyleHeroScreenButton/
        // HeroScreenGold и т.п.) — теперь у него собственная зависимость от
        // общих классов Prototype_SharedPanels.uss (.ks-*), не от чужого C#.
        // Это разрывает связь до того, как Hero Screen (UI-M03) удалит эти
        // хелперы при своей миграции.
        StringAssert.DoesNotContain("CreateHeroScreenPanel", source);
        StringAssert.DoesNotContain("StyleHeroScreenButton", source);
        StringAssert.DoesNotContain("SetHeroScreenBorder", source);
        StringAssert.DoesNotContain("SetHeroScreenRadius", source);
        StringAssert.DoesNotContain("HeroScreenGold", source);
        StringAssert.DoesNotContain("HeroScreenText", source);
        StringAssert.DoesNotContain("HeroScreenMuted", source);
        StringAssert.DoesNotContain("HeroScreenBackdrop", source);
        StringAssert.DoesNotContain("HeroScreenPanelDeep", source);
    }

    [Test]
    public void Journal_Open_And_Close_Use_Shared_Blocking_Time_Policy()
    {
        string source = ReadUiFile("PrototypeUIController.Journal.cs");

        StringAssert.Contains("PauseForBlockingModal();", source);
        StringAssert.Contains("ResumeAfterBlockingModalIfReady();", source);
    }

    private static string ReadUiFile(string fileName)
    {
        string path = Path.Combine(Application.dataPath, "_Project", "UI", fileName);
        return File.ReadAllText(path);
    }
}

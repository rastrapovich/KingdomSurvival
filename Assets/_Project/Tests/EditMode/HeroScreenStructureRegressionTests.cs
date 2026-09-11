using System.IO;
using NUnit.Framework;
using UnityEngine;

// Regression-тест ProjectDocs/UI_ARCHITECTURE.md §30/§11 (ключевой
// regression-тест архитектуры): "Production fullscreen screens must not
// build their static visual tree in C#." Hero Screen (UI-M03) — третий
// экран, прошедший эту миграцию после Camp/Journal; этот файл защищает его
// от отката к программному построению дерева и от возврата приватных
// стилевых хелперов, которые теперь заменены общими классами
// Prototype_SharedPanels.uss (.ks-*) + HeroScreen.uss.
public sealed class HeroScreenStructureRegressionTests
{
    private static readonly string[] ForbiddenBuildMethods =
    {
        "BuildHeroScreen",
        "BuildHeroScreenHeader",
        "BuildHeroScreenLeftColumn",
        "BuildHeroScreenExperienceBlock",
        "BuildHeroScreenCenterColumn",
        "BuildHeroScreenRightColumn",
        "BuildHeroScreenRosterBar",
        "BuildHeroScreenJourneySummaryPanel",
        "BuildHeroScreenUnitCard",
        "CreateHeroScreenColumnScroll",
    };

    private static readonly string[] ForbiddenStyleHelpers =
    {
        "CreateHeroScreenPanel",
        "CreateHeroScreenWrapRow",
        "StyleHeroScreenButton",
        "SetHeroScreenBorder",
        "SetHeroScreenRadius",
        "CreateHeroScreenHealthBar",
        "GetHeroScreenHealthColor",
        "HeroScreenSlug",
        "HeroScreenBackdrop",
        "HeroScreenPanel =",
        "HeroScreenPanelDeep",
        "HeroScreenBorder =",
        "HeroScreenGold",
        "HeroScreenText =",
        "HeroScreenMuted",
        "HeroScreenSlotEmpty",
    };

    [Test]
    public void HeroScreen_Controller_Does_Not_Build_Its_Static_Visual_Tree_In_CSharp()
    {
        string source = ReadUiFile("PrototypeUIController.HeroScreen.cs");

        foreach (string method in ForbiddenBuildMethods)
        {
            StringAssert.DoesNotContain(
                method,
                source,
                "Hero Screen не должен строить постоянную структуру через C# (" + method + "): " +
                "она должна жить в Prototype_Main.uxml/HeroScreen.uss (ProjectDocs/UI_ARCHITECTURE.md).");
        }
    }

    [Test]
    public void HeroScreen_Controller_Does_Not_Reintroduce_Private_Style_Helpers()
    {
        string source = ReadUiFile("PrototypeUIController.HeroScreen.cs");

        foreach (string helper in ForbiddenStyleHelpers)
        {
            StringAssert.DoesNotContain(
                helper,
                source,
                "Hero Screen больше не должен объявлять/использовать приватный стилевой хелпер '" + helper +
                "' — цвета и компонентные стили живут в Prototype_SharedPanels.uss (.ks-*) и HeroScreen.uss.");
        }
    }

    [Test]
    public void HeroScreen_Controller_Uses_Templates_For_Dynamic_Rows_Instead_Of_New_VisualElement()
    {
        string source = ReadUiFile("PrototypeUIController.HeroScreen.cs");

        StringAssert.DoesNotContain(
            "new VisualElement(",
            source,
            "Динамические узлы Hero Screen (теги/особенности/доступные бойцы/строки характеристик) должны клонироваться из UXML-template, а не собираться через new VisualElement(...).");
        StringAssert.DoesNotContain(
            "new Label(",
            source,
            "Hero Screen не должен создавать Label через код — текст берётся из UXML или задаётся на элементе из template.");
        StringAssert.DoesNotContain(
            "new Button(",
            source,
            "Hero Screen не должен создавать Button через код — постоянные кнопки статичны в UXML.");
        StringAssert.Contains(
            "Instantiate()",
            source,
            "Hero Screen должен клонировать динамические узлы через VisualTreeAsset.Instantiate().");
    }

    // Пересборка ростера/тегов/особенностей теперь заполняет фиксированные
    // (roster) или шаблонные (chip/hint/stat-row) узлы, а не создаёт слоты
    // снаряжения/инвентаря/способностей/свиты заново каждый Refresh — эти
    // четыре набора полностью статичны в UXML (фиксированное количество).
    [Test]
    public void HeroScreen_Fixed_Count_Panels_Are_Not_Rebuilt_Every_Refresh()
    {
        string source = ReadUiFile("PrototypeUIController.HeroScreen.cs");

        StringAssert.DoesNotContain("RefreshHeroScreenAbilities", source);
        StringAssert.DoesNotContain("RefreshHeroScreenSlots", source);
        StringAssert.DoesNotContain("RefreshHeroScreenRetinue", source);
    }

    private static string ReadUiFile(string fileName)
    {
        string path = Path.Combine(Application.dataPath, "_Project", "UI", fileName);
        return File.ReadAllText(path);
    }
}

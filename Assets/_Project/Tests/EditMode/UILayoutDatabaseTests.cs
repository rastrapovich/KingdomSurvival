using System.Collections.Generic;
using KingdomSurvival.UILayout;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

public sealed class UILayoutDatabaseTests
{
    [Test]
    public void Database_Loads_Default_And_Has_Narrative_Screen()
    {
        UILayoutDatabaseAsset database = Resources.Load<UILayoutDatabaseAsset>(UILayoutDatabaseAsset.ResourcesPath);
        Assert.IsNotNull(database);
        Assert.IsNotNull(database.FindScreen("narrative-dialogue"));
    }

    [Test]
    public void Database_Default_Layout_Has_No_Validation_Issues()
    {
        UILayoutDatabaseAsset database = Resources.Load<UILayoutDatabaseAsset>(UILayoutDatabaseAsset.ResourcesPath);
        Assert.IsNotNull(database);
        List<string> issues = new List<string>();
        database.CollectValidationIssues(issues);
        Assert.That(issues, Is.Empty);
    }

    [Test]
    public void Narrative_Text_Rect_Is_Converted_From_ScreenSpace_To_PanelLocalSpace()
    {
        UILayoutDatabaseAsset database = Resources.Load<UILayoutDatabaseAsset>(UILayoutDatabaseAsset.ResourcesPath);
        UILayoutScreenDefinition screen = database.FindScreen("narrative-dialogue");
        UILayoutElementDefinition panel = screen.FindElement("panel");
        UILayoutElementDefinition text = screen.FindElement("text");

        Rect local;
        bool success = UILayoutRuntimeApplier.TryGetLocalReferenceRect(screen, text, out local);

        Assert.IsTrue(success);
        Assert.AreEqual("panel", text.ParentId);
        Assert.AreEqual(text.Rect.x - panel.Rect.x, local.x, 0.001f);
        Assert.AreEqual(text.Rect.y - panel.Rect.y, local.y, 0.001f);
        Assert.AreEqual(text.Rect.width, local.width, 0.001f);
        Assert.AreEqual(text.Rect.height, local.height, 0.001f);
    }

    /// <summary>
    /// Тест намеренно не фиксирует конкретные значения затемнения, кегля и
    /// выравнивания: это подогнанные в `UI Конструкторе` величины, и дизайнер
    /// меняет их без изменения кода. Проверяется контракт слоя — что значения
    /// доходят до рантайма пригодными к применению.
    /// </summary>
    [Test]
    public void Narrative_Default_Layout_Exposes_Dimming_And_Text_Presentation()
    {
        UILayoutDatabaseAsset database = Resources.Load<UILayoutDatabaseAsset>(UILayoutDatabaseAsset.ResourcesPath);
        UILayoutScreenDefinition screen = database.FindScreen("narrative-dialogue");
        UILayoutElementDefinition speaker = screen.FindElement("speaker");
        UILayoutElementDefinition choices = screen.FindElement("choices");

        Assert.IsNotNull(speaker);
        Assert.IsNotNull(choices);

        // Диалог перекрывает игру, поэтому затемнение обязано быть заметным.
        Assert.IsTrue(screen.UsesDimming);
        Assert.Greater(screen.DimmingOpacity, 0f);
        Assert.LessOrEqual(screen.DimmingOpacity, 1f);

        // Имя говорящего должно читаться заметнее строки вариантов ответа.
        Assert.Greater(speaker.FontSize, 0);
        Assert.Greater(choices.FontSize, 0);
        Assert.Greater(speaker.FontSize, choices.FontSize);

        // Оба элемента текстовые, значит текстовые свойства применимы.
        Assert.IsTrue(speaker.IsTextual);
        Assert.IsTrue(choices.IsTextual);

        // Выравнивание любого сочетания разрешается в конкретный якорь.
        Assert.IsTrue(System.Enum.IsDefined(
            typeof(TextAnchor),
            UILayoutRuntimeApplier.ResolveTextAnchor(
                speaker.HorizontalAlignment,
                speaker.VerticalAlignment)));
        Assert.IsTrue(System.Enum.IsDefined(
            typeof(TextAnchor),
            UILayoutRuntimeApplier.ResolveTextAnchor(
                choices.HorizontalAlignment,
                choices.VerticalAlignment)));
    }

    /// <summary>
    /// Экраны, добавленные в `UI Конструктор`, должны быть привязываемыми:
    /// у каждого элемента есть имя для поиска в дереве UI.
    /// </summary>
    [Test]
    public void Every_Screen_Element_Has_Binding_Target()
    {
        UILayoutDatabaseAsset database = Resources.Load<UILayoutDatabaseAsset>(UILayoutDatabaseAsset.ResourcesPath);
        Assert.IsNotNull(database);

        foreach (UILayoutScreenDefinition screen in database.Screens)
        {
            Assert.IsNotEmpty(screen.Id, "Экран без идентификатора.");
            foreach (UILayoutElementDefinition element in screen.Elements)
            {
                Assert.IsNotEmpty(
                    element.TargetName,
                    screen.Id + "/" + element.Id + ": не задано имя для привязки.");
            }
        }
    }

    /// <summary>
    /// Диалог применяется собственным кодом `PrototypeUIController`, поэтому
    /// не должен попадать в generic-применение и получать двойную вёрстку.
    /// </summary>
    [Test]
    public void Narrative_Screen_Is_Excluded_From_Generic_Auto_Apply()
    {
        UILayoutDatabaseAsset database = Resources.Load<UILayoutDatabaseAsset>(UILayoutDatabaseAsset.ResourcesPath);
        UILayoutScreenDefinition screen = database.FindScreen(
            UILayoutDatabaseAsset.NarrativeDialogueScreenId);

        Assert.IsNotNull(screen);
        Assert.IsFalse(screen.AutoApply);
    }

    /// <summary>
    /// Пока дизайнер не включил переопределение, конструктор не должен
    /// вмешиваться в вёрстку USS ни на одном экране.
    /// </summary>
    [Test]
    public void Default_Database_Does_Not_Override_Uss_Layout()
    {
        UILayoutDatabaseAsset database = Resources.Load<UILayoutDatabaseAsset>(UILayoutDatabaseAsset.ResourcesPath);
        Assert.IsNotNull(database);

        foreach (UILayoutScreenDefinition screen in database.Screens)
        {
            foreach (UILayoutElementDefinition element in screen.Elements)
            {
                Assert.IsFalse(
                    element.OverrideRect || element.OverrideBackground || element.OverrideText,
                    screen.Id + "/" + element.Id + ": включено переопределение вёрстки.");
            }
        }
    }

    [Test]
    public void Text_Alignment_Resolver_Covers_Center_And_Right()
    {
        Assert.AreEqual(
            TextAnchor.MiddleCenter,
            UILayoutRuntimeApplier.ResolveTextAnchor(
                UILayoutTextHorizontalAlignment.Center,
                UILayoutTextVerticalAlignment.Middle));
        Assert.AreEqual(
            TextAnchor.LowerRight,
            UILayoutRuntimeApplier.ResolveTextAnchor(
                UILayoutTextHorizontalAlignment.Right,
                UILayoutTextVerticalAlignment.Bottom));
    }

    /// <summary>
    /// Regression: точный Rect из UI Конструктора должен иметь приоритет над
    /// legacy min/max из USS. Именно `.narrative-dialogue-portrait` раньше
    /// оставлял `min-height: 360px`, поэтому runtime-рамка 253x131 физически
    /// становилась примерно 253x360 и кадрировала портрет иначе, чем preview.
    /// </summary>
    [Test]
    public void ApplyRect_Clears_Legacy_MinMax_Size_Constraints()
    {
        VisualElement target = new VisualElement();
        target.style.minWidth = 500f;
        target.style.minHeight = 360f;
        target.style.maxWidth = 430f;
        target.style.maxHeight = 900f;

        UILayoutElementDefinition definition = new UILayoutElementDefinition();
        definition.SetRect(new Rect(10f, 20f, 253.5f, 131.3f));

        UILayoutRuntimeApplier.ApplyRect(
            target,
            definition,
            new Vector2(1920f, 1080f),
            new Vector2(1920f, 1080f));

        Assert.That(target.style.width.value.value, Is.EqualTo(253.5f).Within(0.001f));
        Assert.That(target.style.height.value.value, Is.EqualTo(131.3f).Within(0.001f));
        Assert.That(target.style.minWidth.value.value, Is.EqualTo(0f).Within(0.001f));
        Assert.That(target.style.minHeight.value.value, Is.EqualTo(0f).Within(0.001f));
        Assert.AreEqual(StyleKeyword.None, target.style.maxWidth.keyword);
        Assert.AreEqual(StyleKeyword.None, target.style.maxHeight.keyword);
    }
}

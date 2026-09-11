using System.Collections.Generic;
using KingdomSurvival.UILayout;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

public sealed class UILayoutDatabaseTests
{
    [TestCase(PortraitSize.XS, 100, 140)]
    [TestCase(PortraitSize.S, 150, 210)]
    [TestCase(PortraitSize.M, 200, 280)]
    [TestCase(PortraitSize.L, 300, 420)]
    [TestCase(PortraitSize.XL, 400, 560)]
    public void Portrait_Size_Table_Uses_Canonical_Five_By_Seven_Presets(
        PortraitSize size,
        int expectedWidth,
        int expectedHeight)
    {
        PortraitSizeDefinition definition = PortraitSizeTable.Get(size);

        Assert.AreEqual(size, definition.Size);
        Assert.AreEqual(expectedWidth, definition.Width);
        Assert.AreEqual(expectedHeight, definition.Height);
        Assert.AreEqual(
            definition.Width * PortraitSizeTable.AspectHeight,
            definition.Height * PortraitSizeTable.AspectWidth);
    }

    [Test]
    public void Portrait_Element_Rejects_Free_Resize_And_Preserves_Framing_When_Preset_Changes()
    {
        UILayoutElementDefinition element = new UILayoutElementDefinition();
        Rect legacyRect = new Rect(272.86887f, 195.0693f, 441.35843f, 237.2761f);
        Vector2 legacyCenter = legacyRect.center;
        element.SetRect(legacyRect);
        element.SetImageScale(1.35f);
        element.SetImageOffset(new Vector2(27f, -43f));

        element.SetKind(UILayoutElementKind.Portrait);

        Assert.AreEqual(PortraitSize.L, element.PortraitSize);
        Assert.AreEqual(legacyCenter.x, element.Rect.center.x, 0.001f);
        Assert.AreEqual(legacyCenter.y, element.Rect.center.y, 0.001f);
        Assert.AreEqual(300f, element.Rect.width, 0.001f);
        Assert.AreEqual(420f, element.Rect.height, 0.001f);
        Assert.IsFalse(element.SupportsFreeResize);

        element.SetRect(new Rect(17f, 29f, 777f, 888f));
        Assert.AreEqual(17f, element.Rect.x, 0.001f);
        Assert.AreEqual(29f, element.Rect.y, 0.001f);
        Assert.AreEqual(300f, element.Rect.width, 0.001f);
        Assert.AreEqual(420f, element.Rect.height, 0.001f);

        Vector2 centerBeforePresetChange = element.Rect.center;
        element.SetPortraitSize(PortraitSize.XL);

        Assert.AreEqual(centerBeforePresetChange.x, element.Rect.center.x, 0.001f);
        Assert.AreEqual(centerBeforePresetChange.y, element.Rect.center.y, 0.001f);
        Assert.AreEqual(400f, element.Rect.width, 0.001f);
        Assert.AreEqual(560f, element.Rect.height, 0.001f);
        Assert.AreEqual(1.35f, element.ImageScale, 0.001f);
        Assert.AreEqual(new Vector2(27f, -43f), element.ImageOffset);
    }

    [Test]
    public void Database_Loads_Default_And_Has_Narrative_Screen()
    {
        UILayoutDatabaseAsset database = Resources.Load<UILayoutDatabaseAsset>(UILayoutDatabaseAsset.ResourcesPath);
        Assert.IsNotNull(database);
        Assert.IsNotNull(database.FindScreen("narrative-dialogue"));
    }

    // Раньше портрет диалога был жёстко зафиксирован на пресете L. Правило
    // ослаблено по решению автора: допустим любой канонический пресет
    // (XS/S/M/L/XL) — важно только то, что это действительно Portrait с
    // размером из PortraitSizeTable (5:7), а не свободный Rect и не
    // рассинхронизированные width/height.
    [Test]
    public void Narrative_Portrait_UsesCanonicalPreset_And_Is_Always_Applied()
    {
        UILayoutDatabaseAsset database = Resources.Load<UILayoutDatabaseAsset>(UILayoutDatabaseAsset.ResourcesPath);
        UILayoutElementDefinition portrait = database
            .FindScreen(UILayoutDatabaseAsset.NarrativeDialogueScreenId)
            .FindElement("portrait");

        Assert.IsNotNull(portrait);
        Assert.AreEqual(UILayoutElementKind.Portrait, portrait.Kind);

        PortraitSizeDefinition definition = PortraitSizeTable.Get(portrait.PortraitSize);
        Assert.AreEqual(definition.Width, portrait.Rect.width, 0.001f);
        Assert.AreEqual(definition.Height, portrait.Rect.height, 0.001f);
        Assert.IsTrue(UILayoutScreenBinder.ShouldApplyRect(portrait));
        Assert.IsTrue(UILayoutScreenBinder.ShouldApplyBackground(portrait));
    }

    [Test]
    public void Generic_Image_Keeps_Its_Free_Rect_And_Does_Not_Inherit_Portrait_Rules()
    {
        UILayoutElementDefinition image = new UILayoutElementDefinition();
        image.SetKind(UILayoutElementKind.Image);
        image.SetRect(new Rect(11f, 22f, 350f, 500f));

        Assert.IsTrue(image.SupportsFreeResize);
        Assert.AreEqual(350f, image.Rect.width, 0.001f);
        Assert.AreEqual(500f, image.Rect.height, 0.001f);
        Assert.IsFalse(UILayoutScreenBinder.ShouldApplyRect(image));
        Assert.IsFalse(UILayoutScreenBinder.ShouldApplyBackground(image));
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
    /// У обычных элементов конструктор не должен вмешиваться в USS, пока
    /// дизайнер не включил legacy override-флаги. Portrait имеет отдельный
    /// явный контракт применения и проверяется отдельным тестом выше.
    /// </summary>
    [Test]
    public void Default_Database_Does_Not_Enable_Legacy_Override_Flags()
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

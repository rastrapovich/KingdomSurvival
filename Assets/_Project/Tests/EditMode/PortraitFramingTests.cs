using KingdomSurvival.DialogueDatabase;
using KingdomSurvival.UILayout;
using NUnit.Framework;
using UnityEngine;

public sealed class PortraitFramingTests
{
    [Test]
    public void SpeakerFraming_DefaultsAreNeutral()
    {
        DialogueSpeakerData speaker = new DialogueSpeakerData();

        Assert.IsFalse(speaker.OverridePortraitFraming);
        Assert.That(speaker.PortraitScale, Is.EqualTo(1f).Within(0.0001f));
        Assert.That(speaker.PortraitOffsetNormalized, Is.EqualTo(Vector2.zero));
        Assert.IsFalse(speaker.PortraitFlipX);
    }

    [Test]
    public void ResolveImageOffset_CombinesReferencePixelsAndNormalizedSpeakerPan()
    {
        UILayoutElementDefinition definition = new UILayoutElementDefinition();
        definition.SetRect(new Rect(100f, 50f, 400f, 600f));
        definition.SetImageOffset(new Vector2(100f, 40f));

        Vector2 offset = UILayoutRuntimeApplier.ResolveImageOffset(
            definition,
            new Vector2(1920f, 1080f),
            new Vector2(960f, 540f),
            new Vector2(0.1f, -0.2f));

        // Общий pan: (50, 20). Фактическая рамка: 200×300.
        // Индивидуальный pan: (20, -60). Итог: (70, -40).
        Assert.That(offset.x, Is.EqualTo(70f).Within(0.001f));
        Assert.That(offset.y, Is.EqualTo(-40f).Within(0.001f));
    }

    [Test]
    public void ResolveImageFrameSize_FollowsActualResolution()
    {
        UILayoutElementDefinition definition = new UILayoutElementDefinition();
        definition.SetRect(new Rect(0f, 0f, 400f, 600f));

        Vector2 size = UILayoutRuntimeApplier.ResolveImageFrameSize(
            definition,
            new Vector2(1920f, 1080f),
            new Vector2(960f, 540f));

        Assert.That(size.x, Is.EqualTo(200f).Within(0.001f));
        Assert.That(size.y, Is.EqualTo(300f).Within(0.001f));
    }

    [Test]
    public void ResolveImageScale_MultipliesGlobalAndSpeakerZoomAndCanFlipX()
    {
        UILayoutElementDefinition definition = new UILayoutElementDefinition();
        definition.SetImageScale(1.2f);

        Vector3 normal = UILayoutRuntimeApplier.ResolveImageScale(definition, 1.5f, false);
        Vector3 flipped = UILayoutRuntimeApplier.ResolveImageScale(definition, 1.5f, true);

        Assert.That(normal.x, Is.EqualTo(1.8f).Within(0.001f));
        Assert.That(normal.y, Is.EqualTo(1.8f).Within(0.001f));
        Assert.That(flipped.x, Is.EqualTo(-1.8f).Within(0.001f));
        Assert.That(flipped.y, Is.EqualTo(1.8f).Within(0.001f));
    }

    [Test]
    public void ResolveImageScaleMode_MatchesUILayoutModes()
    {
        Assert.AreEqual(ScaleMode.ScaleAndCrop, UILayoutRuntimeApplier.ResolveImageScaleMode(UILayoutImageMode.Cover));
        Assert.AreEqual(ScaleMode.ScaleToFit, UILayoutRuntimeApplier.ResolveImageScaleMode(UILayoutImageMode.Contain));
        Assert.AreEqual(ScaleMode.StretchToFill, UILayoutRuntimeApplier.ResolveImageScaleMode(UILayoutImageMode.Stretch));
    }

    // ---- "Свободное кадрирование полного портрета" ---------------------
    //
    // Главная проверка инструкции: ResolveImageRect всегда возвращает
    // прямоугольник ПОЛНОГО (несжатого/необрезанного по пропорции) изображения
    // — Cover/Contain задают только его базовый размер, а обрезка возникает
    // исключительно оттого, что часть этого прямоугольника выходит за рамку
    // (ответственность вызывающей стороны — overflow/clip), а не потому, что
    // сюда заранее подставили уже урезанный Sprite.

    [Test]
    public void ResolveImageRect_Cover_SquareSpriteInVerticalFrame_FillsFrameAndOverflowsHorizontally()
    {
        // квадратный Sprite → вертикальная рамка
        Vector2 source = new Vector2(1024f, 1024f);
        Vector2 frame = new Vector2(300f, 400f);

        Rect rect = UILayoutRuntimeApplier.ResolveImageRect(source, frame, UILayoutImageMode.Cover, 1f, Vector2.zero);

        // Cover тянет по большей из двух пропорций: 400/1024 — картинка
        // становится 400×400 и покрывает рамку по высоте полностью, но по
        // ширине (400) шире рамки (300) — 50px с каждой стороны "за кадром".
        Assert.That(rect.height, Is.EqualTo(400f).Within(0.01f));
        Assert.That(rect.width, Is.EqualTo(400f).Within(0.01f));
        Assert.GreaterOrEqual(rect.width, frame.x);
        Assert.GreaterOrEqual(rect.height, frame.y);
    }

    [Test]
    public void ResolveImageRect_Cover_VerticalSpriteInWideFrame_FillsFrameAndOverflowsVertically()
    {
        // вертикальный Sprite → широкая рамка
        Vector2 source = new Vector2(400f, 1200f);
        Vector2 frame = new Vector2(800f, 300f);

        Rect rect = UILayoutRuntimeApplier.ResolveImageRect(source, frame, UILayoutImageMode.Cover, 1f, Vector2.zero);

        Assert.That(rect.width, Is.EqualTo(800f).Within(0.01f));
        Assert.GreaterOrEqual(rect.width, frame.x);
        Assert.GreaterOrEqual(rect.height, frame.y);
    }

    [Test]
    public void ResolveImageRect_Cover_PanUpAndDown_OnlyMovesPositionNotSize()
    {
        Vector2 source = new Vector2(1024f, 1024f);
        Vector2 frame = new Vector2(300f, 400f);

        Rect noPan = UILayoutRuntimeApplier.ResolveImageRect(source, frame, UILayoutImageMode.Cover, 1f, Vector2.zero);
        Rect panUp = UILayoutRuntimeApplier.ResolveImageRect(source, frame, UILayoutImageMode.Cover, 1f, new Vector2(0f, -50f));
        Rect panDown = UILayoutRuntimeApplier.ResolveImageRect(source, frame, UILayoutImageMode.Cover, 1f, new Vector2(0f, 50f));

        Assert.That(panUp.y, Is.EqualTo(noPan.y - 50f).Within(0.01f));
        Assert.That(panDown.y, Is.EqualTo(noPan.y + 50f).Within(0.01f));
        // Регрессия ключевой жалобы инструкции: pan никогда не уменьшает
        // доступную область исходника — width/height не меняются.
        Assert.That(panUp.size, Is.EqualTo(noPan.size));
        Assert.That(panDown.size, Is.EqualTo(noPan.size));
    }

    [Test]
    public void ResolveImageRect_Contain_FitsEntirelyInsideFrameAndCanPanWithinEmptySpace()
    {
        Vector2 source = new Vector2(1024f, 1024f);
        Vector2 frame = new Vector2(300f, 400f);

        Rect rect = UILayoutRuntimeApplier.ResolveImageRect(source, frame, UILayoutImageMode.Contain, 1f, Vector2.zero);

        // Contain вписывает целиком — оба измерения не больше рамки.
        Assert.LessOrEqual(rect.width, frame.x + 0.01f);
        Assert.LessOrEqual(rect.height, frame.y + 0.01f);

        Rect panned = UILayoutRuntimeApplier.ResolveImageRect(source, frame, UILayoutImageMode.Contain, 1f, new Vector2(0f, 30f));
        Assert.That(panned.y, Is.EqualTo(rect.y + 30f).Within(0.01f));
        Assert.That(panned.size, Is.EqualTo(rect.size));
    }

    [TestCase(0.5f)]
    [TestCase(1f)]
    [TestCase(2f)]
    [TestCase(4f)]
    public void ResolveImageRect_ScaleMultipliesDisplaySizeLinearly(float scale)
    {
        Vector2 source = new Vector2(1024f, 1024f);
        Vector2 frame = new Vector2(300f, 400f);

        Rect baseRect = UILayoutRuntimeApplier.ResolveImageRect(source, frame, UILayoutImageMode.Cover, 1f, Vector2.zero);
        Rect scaledRect = UILayoutRuntimeApplier.ResolveImageRect(source, frame, UILayoutImageMode.Cover, scale, Vector2.zero);

        Assert.That(scaledRect.width, Is.EqualTo(baseRect.width * scale).Within(0.01f));
        Assert.That(scaledRect.height, Is.EqualTo(baseRect.height * scale).Within(0.01f));
    }

    [Test]
    public void ResolveImageRect_UiScaleTimesSpeakerScale_MatchesResolveImageScaleMagnitude()
    {
        // UIScale × SpeakerScale — тот же порядок объединения, что уже
        // тестируется для ResolveImageScale; здесь проверяем, что
        // полученная величина действительно масштабирует imageRect.
        UILayoutElementDefinition definition = new UILayoutElementDefinition();
        definition.SetImageScale(1.2f);
        float speakerScale = 1.5f;

        Vector3 combined = UILayoutRuntimeApplier.ResolveImageScale(definition, speakerScale, false);
        float magnitude = Mathf.Abs(combined.y);

        Vector2 source = new Vector2(1024f, 1024f);
        Vector2 frame = new Vector2(300f, 400f);
        Rect unscaled = UILayoutRuntimeApplier.ResolveImageRect(source, frame, UILayoutImageMode.Cover, 1f, Vector2.zero);
        Rect resolved = UILayoutRuntimeApplier.ResolveImageRect(source, frame, UILayoutImageMode.Cover, magnitude, Vector2.zero);

        Assert.That(resolved.width, Is.EqualTo(unscaled.width * 1.2f * 1.5f).Within(0.01f));
    }

    [Test]
    public void ResolveImageRect_FlipDoesNotChangePositionOrSize()
    {
        // Flip X (после pan) не должен менять Offset — отражение решается
        // отдельно (знаком CSS scale/GUIUtility.ScaleAroundPivot вокруг
        // центра уже готового imageRect), а не через ResolveImageRect.
        Vector2 source = new Vector2(1024f, 1024f);
        Vector2 frame = new Vector2(300f, 400f);
        Vector2 offset = new Vector2(-20f, 15f);

        UILayoutElementDefinition definition = new UILayoutElementDefinition();
        Vector3 normalScale = UILayoutRuntimeApplier.ResolveImageScale(definition, 1f, false);
        Vector3 flippedScale = UILayoutRuntimeApplier.ResolveImageScale(definition, 1f, true);

        Rect normalRect = UILayoutRuntimeApplier.ResolveImageRect(
            source, frame, UILayoutImageMode.Cover, Mathf.Abs(normalScale.y), offset);
        Rect flippedRect = UILayoutRuntimeApplier.ResolveImageRect(
            source, frame, UILayoutImageMode.Cover, Mathf.Abs(flippedScale.y), offset);

        // Величина (Abs) одинакова для обоих случаев — flip кодируется
        // только знаком X, который в ResolveImageRect не участвует.
        Assert.That(flippedRect.position, Is.EqualTo(normalRect.position));
        Assert.That(flippedRect.size, Is.EqualTo(normalRect.size));
    }

    [Test]
    public void ResolveSpriteSize_UsesSpriteRect_NotWholeAtlasTexture()
    {
        // Sprite из Atlas: Texture — весь атлас (1024×1024), а конкретный
        // портрет внутри — только часть его.
        Texture2D atlas = new Texture2D(1024, 1024);
        try
        {
            Sprite sprite = Sprite.Create(atlas, new Rect(200f, 300f, 150f, 220f), new Vector2(0.5f, 0.5f));
            try
            {
                Vector2 size = UILayoutRuntimeApplier.ResolveSpriteSize(sprite);

                Assert.That(size.x, Is.EqualTo(150f).Within(0.01f));
                Assert.That(size.y, Is.EqualTo(220f).Within(0.01f));
                Assert.AreNotEqual(atlas.width, size.x);
                Assert.AreNotEqual(atlas.height, size.y);
            }
            finally
            {
                Object.DestroyImmediate(sprite);
            }
        }
        finally
        {
            Object.DestroyImmediate(atlas);
        }
    }

    [Test]
    public void ResolveImageRect_VeryStrongOffset_CanLeaveEmptySpaceInsideFrame()
    {
        // Художник должен иметь полный контроль и при желании даже оставить
        // пустую область внутри рамки — Offset намеренно не ограничивается
        // границами изображения.
        Vector2 source = new Vector2(1024f, 1024f);
        Vector2 frame = new Vector2(300f, 400f);
        Vector2 hugeOffset = new Vector2(2000f, 2000f);

        Rect rect = UILayoutRuntimeApplier.ResolveImageRect(source, frame, UILayoutImageMode.Cover, 1f, hugeOffset);

        // Изображение полностью уехало за пределы рамки по X и по Y —
        // ни один из его углов больше не пересекает рамку.
        Assert.Greater(rect.x, frame.x);
        Assert.Greater(rect.y, frame.y);
    }

    [Test]
    public void ResolveImageRect_SameInputs_DialoguePreviewAndRuntimeAgree()
    {
        // Совпадение rect между Dialogue Preview и Runtime гарантировано
        // архитектурно — оба вызывают один и тот же статический метод.
        // Явный regression-тест фиксирует контракт: детерминированность при
        // одинаковых аргументах (никакого скрытого состояния).
        Vector2 source = new Vector2(800f, 600f);
        Vector2 frame = new Vector2(320f, 420f);
        Vector2 offset = new Vector2(12f, -8f);

        Rect first = UILayoutRuntimeApplier.ResolveImageRect(source, frame, UILayoutImageMode.Contain, 1.3f, offset);
        Rect second = UILayoutRuntimeApplier.ResolveImageRect(source, frame, UILayoutImageMode.Contain, 1.3f, offset);

        Assert.That(second.position, Is.EqualTo(first.position));
        Assert.That(second.size, Is.EqualTo(first.size));
    }
}

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
}

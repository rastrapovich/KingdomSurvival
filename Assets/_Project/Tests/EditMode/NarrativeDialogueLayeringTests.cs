using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;

public sealed class NarrativeDialogueLayeringTests
{
    [Test]
    public void Narrative_Open_Brings_Overlay_To_Front_After_Showing_It()
    {
        string source = ReadUiFile("PrototypeUIController.Narrative.cs");
        const string methodSignature = "public bool TryOpenNarrativeDialogueById(string dialogueId)";
        const string nextMethodSignature = "private void DisplayNarrativeView(";

        int methodStart = source.IndexOf(methodSignature, StringComparison.Ordinal);
        int methodEnd = source.IndexOf(nextMethodSignature, methodStart, StringComparison.Ordinal);
        Assert.That(methodStart, Is.GreaterThanOrEqualTo(0));
        Assert.That(methodEnd, Is.GreaterThan(methodStart));

        string methodBody = source.Substring(methodStart, methodEnd - methodStart);
        int showIndex = methodBody.IndexOf(
            "narrativeDialogueOverlay.style.display = DisplayStyle.Flex;",
            StringComparison.Ordinal);
        int bringToFrontIndex = methodBody.IndexOf(
            "narrativeDialogueOverlay.BringToFront();",
            StringComparison.Ordinal);
        int displayViewIndex = methodBody.IndexOf(
            "DisplayNarrativeView(view, null);",
            StringComparison.Ordinal);

        Assert.That(showIndex, Is.GreaterThanOrEqualTo(0));
        Assert.That(
            bringToFrontIndex,
            Is.GreaterThan(showIndex),
            "Narrative Dialogue должен подниматься поверх уже открытого fullscreen-экрана сразу после показа overlay.");
        Assert.That(displayViewIndex, Is.GreaterThan(bringToFrontIndex));
    }

    private static string ReadUiFile(string fileName)
    {
        string path = Path.Combine(Application.dataPath, "_Project", "UI", fileName);
        return File.ReadAllText(path);
    }
}

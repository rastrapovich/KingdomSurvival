using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;

public class CampUiRefreshTests
{
    [Test]
    public void CampNavigation_IsRefreshedFromStableStateChangePath()
    {
        AssertMethodContains(
            "PrototypeUIController.StableUI.cs",
            "private void RefreshStableUiAfterStateChange()",
            "RefreshCampNavButtonState();");
    }

    [Test]
    public void CampNavigation_IsRefreshedFromContinuousPresentationPath()
    {
        AssertMethodContains(
            "PrototypeUIController.ContinuousTimePresentation.cs",
            "private void RefreshContinuousTimeUi(bool refreshPanels)",
            "RefreshCampNavButtonState();");
    }

    private static void AssertMethodContains(
        string fileName,
        string methodSignature,
        string expectedStatement)
    {
        string path = Path.Combine(Application.dataPath, "_Project", "UI", fileName);
        string source = File.ReadAllText(path);

        int methodStart = source.IndexOf(methodSignature, StringComparison.Ordinal);
        Assert.That(methodStart, Is.GreaterThanOrEqualTo(0),
            "Не найден метод " + methodSignature + " в " + fileName + ".");

        int nextMethod = source.IndexOf(
            "\n    private ",
            methodStart + methodSignature.Length,
            StringComparison.Ordinal);
        string methodBody = nextMethod >= 0
            ? source.Substring(methodStart, nextMethod - methodStart)
            : source.Substring(methodStart);

        StringAssert.Contains(expectedStatement, methodBody,
            "Camp должен обновлять доступность кнопки в каждом основном UI refresh path.");
    }
}

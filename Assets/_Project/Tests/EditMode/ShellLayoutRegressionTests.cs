using System.IO;
using NUnit.Framework;
using UnityEngine;

public sealed class ShellLayoutRegressionTests
{
    private static string ReadMainUxml()
    {
        string path = Path.Combine(
            Application.dataPath,
            "_Project",
            "UI",
            "Prototype",
            "Prototype_Main.uxml");
        return File.ReadAllText(path);
    }

    [Test]
    public void LegacyKingAndCommanderShellElementsAreRemoved()
    {
        string uxml = ReadMainUxml();

        StringAssert.DoesNotContain("KINGDOM SURVIVAL", uxml);
        StringAssert.DoesNotContain("КОРОЛЕВСКИЕ ДОНЕСЕНИЯ", uxml);
        StringAssert.DoesNotContain("persistent-commander", uxml);
        StringAssert.Contains("text=\"ДОНЕСЕНИЯ\"", uxml);
    }

    [Test]
    public void ResourcesLiveInsideCapitalAndNotBottomBar()
    {
        string uxml = ReadMainUxml();
        int capitalStart = uxml.IndexOf("name=\"capital-screen\"");
        int resourcePanel = uxml.IndexOf("name=\"capital-resource-panel\"");
        int mapStart = uxml.IndexOf("name=\"expeditions-screen\"");

        Assert.That(capitalStart, Is.GreaterThanOrEqualTo(0));
        Assert.That(resourcePanel, Is.GreaterThan(capitalStart));
        Assert.That(resourcePanel, Is.LessThan(mapStart));
        StringAssert.DoesNotContain("shell-resource-bar", uxml);
        StringAssert.DoesNotContain("shell-resource-strip", uxml);
    }

    [Test]
    public void BottomBarKeepsFiveNavigationButtonsAndTimeIndicator()
    {
        string uxml = ReadMainUxml();
        int bottomStart = uxml.IndexOf("class=\"shell-bottom-bar\"");
        int bottomEnd = uxml.IndexOf("class=\"top-bar shell-debug-host\"");
        string bottom = uxml.Substring(bottomStart, bottomEnd - bottomStart);

        StringAssert.Contains("nav-capital-button", bottom);
        StringAssert.Contains("nav-expeditions-button", bottom);
        StringAssert.Contains("nav-hero-button", bottom);
        StringAssert.Contains("nav-journal-button", bottom);
        StringAssert.Contains("nav-camp-button", bottom);
        StringAssert.Contains("day-label", bottom);
        StringAssert.DoesNotContain("gold-label", bottom);
        StringAssert.DoesNotContain("population-label", bottom);
    }
}

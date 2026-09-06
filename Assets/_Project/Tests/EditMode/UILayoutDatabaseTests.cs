using System.Collections.Generic;
using KingdomSurvival.UILayout;
using NUnit.Framework;
using UnityEngine;

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
}

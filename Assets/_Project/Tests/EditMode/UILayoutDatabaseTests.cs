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
}

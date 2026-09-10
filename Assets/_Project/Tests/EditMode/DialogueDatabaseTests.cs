using System.Collections.Generic;
using KingdomSurvival.DialogueDatabase;
using NUnit.Framework;
using UnityEngine;

// prototype_miller (технический демо-диалог для наглядной проверки всех
// NarrativeConditionType/групп All-Any/Negate) удалён из
// KingdomSurvivalDialogues.asset как более не нужный — вместе с ним удалены
// тесты, привязанные к его конкретному ID/содержимому. Условия/группы/
// Negate сами по себе покрыты NarrativeCheckSystemTests.cs и
// DialogueDatabaseCheckSystemTests.cs (программно собранный демо-граф),
// runtime-сессия — Chapter01FourResidentsTests.cs на реальном N01.
public sealed class DialogueDatabaseTests
{
    [Test]
    public void DefaultDatabase_Has_No_Validation_Issues()
    {
        DialogueDatabaseAsset database = Resources.Load<DialogueDatabaseAsset>(DialogueDatabaseAsset.ResourcesPath);
        Assert.IsNotNull(database);

        List<string> issues = new List<string>();
        database.CollectValidationIssues(issues);

        Assert.That(issues, Is.Empty);
    }
}

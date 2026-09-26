using System.Collections.Generic;
using System.Reflection;
using KingdomSurvival.Encounters;
using KingdomSurvival.Encounters.Editor;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

// Окно встреч для автора: правила выпадения читаются одной фразой по-русски,
// окно строится на настоящей базе на всех трёх вкладках.
public sealed class EncounterEditorWindowTests
{
    [Test]
    public void DescribeWhen_ReadsAsOneRussianSentence()
    {
        EncounterDefinition encounter = new EncounterDefinition
        {
            Category = EncounterCategory.Road,
            DiscoveryChancePercent = 90,
            MaxOccurrencesPerGame = 1,
            AllowedRegionIds = new List<string> { "road" },
            RequiredFlagsAll = new List<string> { "RATS_SEEN" },
            ForbiddenFlags = new List<string> { "RATS_GONE" }
        };

        string text = EncounterEditorLabels.DescribeWhen(encounter);

        StringAssert.StartsWith("В дороге (регионы: road), один раз за игру, шанс 90%.", text);
        StringAssert.Contains("Только если: есть флаги RATS_SEEN.", text);
        StringAssert.Contains("Не выпадает, если: есть флаги RATS_GONE.", text);
    }

    [Test]
    public void DescribeWhen_RepeatsAndCooldown_DirectCall()
    {
        EncounterDefinition repeat = new EncounterDefinition { MaxOccurrencesPerGame = 3, CooldownHours = 24, DiscoveryChancePercent = 40 };
        StringAssert.Contains("до 3 раз за игру, не чаще раза в 24 ч, шанс 40%", EncounterEditorLabels.DescribeWhen(repeat));

        EncounterDefinition direct = new EncounterDefinition { SelectionMode = EncounterSelectionMode.Direct, UnlimitedOccurrences = true, Category = EncounterCategory.Camp };
        Assert.AreEqual("На стоянке, сколько угодно раз, вызывается напрямую.", EncounterEditorLabels.DescribeWhen(direct));
    }

    [Test]
    public void ListLine_HasNoTechnicalCodes()
    {
        EncounterDefinition encounter = new EncounterDefinition
        {
            Category = EncounterCategory.Road,
            DurationClass = EncounterDurationClass.Micro,
            DiscoveryChancePercent = 90,
            MaxOccurrencesPerGame = 1
        };
        Assert.AreEqual("Дорога · мини-сцена · раз за игру · 90%", EncounterEditorLabels.DescribeListLine(encounter));
    }

    [Test]
    public void Window_BuildsAllTabs_OnRealDatabase()
    {
        EncounterDatabaseWindow window = ScriptableObject.CreateInstance<EncounterDatabaseWindow>();
        try
        {
            window.CreateGUI();
            VisualElement root = window.rootVisualElement;
            Assert.IsTrue(root.Query<Label>().ToList().Exists(label => label.text == "КОГДА ВЫПАДАЕТ"), "Карточка встречи построена.");

            FieldInfo tab = typeof(EncounterDatabaseWindow).GetField("activeTab", BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo show = typeof(EncounterDatabaseWindow).GetMethod("ShowActiveTab", BindingFlags.Instance | BindingFlags.NonPublic);
            foreach (object value in System.Enum.GetValues(tab.FieldType))
            {
                tab.SetValue(window, value);
                Assert.DoesNotThrow(() => show.Invoke(window, null), "Вкладка " + value);
                if (value.ToString() == "Flags")
                    Assert.IsTrue(root.Query<Label>().ToList().Exists(label => label.text == "ГДЕ ИСПОЛЬЗУЕТСЯ"), "Карточка флага построена.");
            }
        }
        finally
        {
            Object.DestroyImmediate(window);
        }
    }
}

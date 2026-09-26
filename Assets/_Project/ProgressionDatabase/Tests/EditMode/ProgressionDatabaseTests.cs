using System.Collections.Generic;
using System.Linq;
using KingdomSurvival.UnitDatabase;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace KingdomSurvival.ProgressionDatabase.Tests
{
    // «База развития»: файл на месте, у Командира и каждого типа Базы существ
    // есть карта 1–100, база проходит проверку и в исходном виде совпадает с
    // рабочими значениями ядра, конвертация база ↔ ядро ничего не теряет.
    public sealed class ProgressionDatabaseTests
    {
        private static ProgressionDatabaseAsset Load()
        {
            ProgressionDatabaseAsset database = Resources.Load<ProgressionDatabaseAsset>(ProgressionDatabaseAsset.ResourcesPath);
            Assert.IsNotNull(database, "Нет Resources/" + ProgressionDatabaseAsset.ResourcesPath);
            return database;
        }

        [Test]
        public void Database_HasHeroAndEveryUnitType_With100Levels()
        {
            ProgressionDatabaseAsset database = Load();
            Assert.IsNotNull(database.FindProfile(ProgressionRules.HeroProfileId));
            UnitDatabaseAsset units = Resources.Load<UnitDatabaseAsset>(UnitDatabaseAsset.ResourcesPath);
            Assert.IsNotNull(units);
            foreach (UnitDefinitionData unit in units.Units)
            {
                ProgressionProfileRecord profile = database.FindProfile(unit.Id);
                Assert.IsNotNull(profile, "Нет карты развития для типа " + unit.Id);
                Assert.AreEqual(ProgressionProfile.LevelCount, profile.levels.Count, unit.Id);
            }
        }

        [Test]
        public void Database_PassesValidation()
        {
            List<string> errors = new List<string>();
            List<string> warnings = new List<string>();
            Load().CollectValidationIssues(errors, warnings);
            Assert.That(errors, Is.Empty, string.Join("\n", errors));
        }

        [Test]
        public void Database_Catalogs_AreComplete()
        {
            ProgressionCatalog catalog = Load().ToCatalog();
            Assert.AreEqual(6, catalog.Qualities.Count);
            Assert.AreEqual(24, catalog.Competencies.Count);
            Assert.IsNotNull(catalog.FindTrait(NarrativeTraitIds.KnowsTheWay));
            Assert.IsNotNull(catalog.FindTrait(NarrativeTraitIds.Naturalist));
        }

        [Test]
        public void Database_ToRules_KeepsHeroCurveAndRules()
        {
            ProgressionDatabaseAsset database = Load();
            ProgressionRules rules = database.ToRules();
            ProgressionProfile hero = rules.GetProfile(ProgressionRules.HeroProfileId);
            ProgressionProfileRecord record = database.FindProfile(ProgressionRules.HeroProfileId);

            long expected = record.levels.Take(ProgressionProfile.LevelCount - 1).Sum(level => (long)level.experienceToNext);
            Assert.AreEqual(expected, hero.TotalExperienceForLevel(ProgressionProfile.LevelCount));
            Assert.AreEqual(database.rules.participationPercent, rules.ParticipationPercent);
            Assert.AreEqual(record.levels.Count(level => level.choice), hero.CountChoiceLevels(0, ProgressionProfile.LevelCount));
        }

        [Test]
        public void FillFrom_ThenToRules_RoundTripsProfilesAndCatalog()
        {
            ProgressionRules source = ProgressionRules.CreateDefault();
            ProgressionProfile guard = source.GetProfile("guard");
            guard.Levels[9].Bonus = new StatModifier { Defense = 2 };
            guard.Levels[9].BattleExperience = 321;
            guard.Levels[9].Note = "ветеран строя";
            guard.StartingLevel = 3;

            ProgressionDatabaseAsset database = ScriptableObject.CreateInstance<ProgressionDatabaseAsset>();
            try
            {
                database.FillFrom(source, ProgressionCatalog.CreateDefault());
                ProgressionRules result = database.ToRules();
                ProgressionProfile copy = result.GetProfile("guard");
                Assert.AreEqual(2, copy.GetLevel(10).Bonus.Defense);
                Assert.AreEqual(321, copy.GetLevel(10).BattleExperience);
                Assert.AreEqual("ветеран строя", copy.GetLevel(10).Note);
                Assert.AreEqual(3, copy.StartingLevel);
                Assert.AreEqual(source.GetProfile("guard").TotalExperienceForLevel(100), copy.TotalExperienceForLevel(100));
                CollectionAssert.AreEqual(
                    source.GetProfile("guard").StartingCompetencies.Select(entry => entry.CompetencyId).ToList(),
                    copy.StartingCompetencies.Select(entry => entry.CompetencyId).ToList());
                Assert.AreEqual(24, database.ToCatalog().Competencies.Count);
            }
            finally
            {
                Object.DestroyImmediate(database);
            }
        }

        [Test]
        public void Runtime_Apply_ChangesCoreAndReset()
        {
            try
            {
                ProgressionDatabaseRuntime.Apply(Load());
                Assert.AreEqual(Load().competencies.Count, ProgressionCatalog.Current.Competencies.Count);
                Assert.IsTrue(ProgressionRules.Current.HasProfile(ProgressionRules.HeroProfileId));
            }
            finally
            {
                ProgressionDatabaseRuntime.ResetToDefaults();
            }
        }

        // Окно строит все страницы без исключений: правила, карту каждого
        // типа с таблицей 100 уровней, перечни, теги и проверку.
        [Test]
        public void Window_BuildsEveryPage()
        {
            Editor.ProgressionDatabaseWindow window = ScriptableObject.CreateInstance<Editor.ProgressionDatabaseWindow>();
            try
            {
                window.CreateGUI();
                System.Reflection.MethodInfo show = typeof(Editor.ProgressionDatabaseWindow).GetMethod("ShowPage",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                Assert.IsNotNull(show);
                List<string> pages = new List<string> { "rules", "qualities", "traits", "competencies", "tags", "validation" };
                pages.AddRange(Load().profiles.Select(profile => "profile:" + profile.id));
                foreach (string page in pages)
                {
                    show.Invoke(window, new object[] { page });
                    if (!page.StartsWith("profile:"))
                        continue;
                    List<MultiColumnListView> tables =
                        window.rootVisualElement.Query<MultiColumnListView>().ToList();
                    Assert.AreEqual(1, tables.Count, page);
                    Assert.AreEqual(ProgressionProfile.LevelCount, tables[0].itemsSource.Count, page + ": 100 уровней в таблице.");
                }
            }
            finally
            {
                Object.DestroyImmediate(window);
            }
        }
    }
}

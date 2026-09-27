using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace KingdomSurvival.ProgressionDatabase.Tests
{
    // 12Е-1: каталог особенностей, приёмов и приказов (FEATURES_REFERENCES_ADAPTATION.md)
    // целиком в значениях ядра и в «Базе развития»; ссылки между записями
    // разрешаются; активны только записи с кодом; схема 1 → 2 не теряет правок.
    public sealed class ProgressionFeatureCatalogTests
    {
        private static List<TraitCatalogEntry> Defaults()
        {
            return ProgressionCatalog.CreateDefault().Traits;
        }

        [Test]
        public void Defaults_ContainWholeCatalog_ByLayerAndArea()
        {
            List<TraitCatalogEntry> traits = Defaults();
            Assert.AreEqual(52, traits.Count(t => t.Code.StartsWith("Б-")), "Боевые особенности Б-01…Б-52.");
            Assert.AreEqual(116, traits.Count(t => t.Code.StartsWith("Н-")), "Остальные особенности Н-01…Н-116.");
            Assert.AreEqual(22, traits.Count(t => t.Layer == FeatureLayer.Technique), "Приёмы П-01…П-22.");
            Assert.AreEqual(6, traits.Count(t => t.Layer == FeatureLayer.Order), "Приказы ПК-01…ПК-06.");
            Assert.IsTrue(traits.Where(t => t.Code.StartsWith("Б-")).All(t => t.Combat && t.Layer == FeatureLayer.Feature));
            Assert.IsTrue(traits.Where(t => t.Code.StartsWith("Н-")).All(t => !t.Combat && t.Layer == FeatureLayer.Feature));
            Assert.IsTrue(traits.Where(t => t.Layer == FeatureLayer.Order).All(t => t.Owner == FeatureOwner.Commander));
        }

        [Test]
        public void Defaults_IdsAndCodesAreUnique_AndEveryEntryHasOneToThreeRanks()
        {
            List<TraitCatalogEntry> traits = Defaults();
            Assert.AreEqual(traits.Count, traits.Select(t => t.Id).Distinct().Count(), "ID уникальны.");
            List<string> codes = traits.Where(t => t.Code != "—").Select(t => t.Code).ToList();
            Assert.AreEqual(codes.Count, codes.Distinct().Count(), "Номера каталога уникальны.");
            foreach (TraitCatalogEntry trait in traits)
            {
                Assert.That(trait.Ranks.Count, Is.InRange(1, TraitCatalogEntry.MaxRanks), trait.Code + " " + trait.Name);
                Assert.IsFalse(string.IsNullOrWhiteSpace(trait.Name), trait.Id);
                Assert.IsFalse(string.IsNullOrWhiteSpace(trait.Description), trait.Id);
                Assert.IsFalse(string.IsNullOrWhiteSpace(trait.Display), trait.Id + ": способ отображения обязателен (§0.1).");
            }
        }

        [Test]
        public void Defaults_ReferencesResolve()
        {
            ProgressionCatalog catalog = ProgressionCatalog.CreateDefault();
            HashSet<string> competencies = new HashSet<string>(catalog.Competencies.Select(c => c.Id));
            foreach (TraitCatalogEntry trait in catalog.Traits)
            {
                foreach (FeatureRequirement requirement in trait.Requirements)
                {
                    switch (requirement.Kind)
                    {
                        case FeatureRequirementKind.Competency:
                            Assert.IsTrue(competencies.Contains(requirement.Id), trait.Code + ": " + requirement.Id);
                            break;
                        case FeatureRequirementKind.Quality:
                            Assert.IsTrue(System.Enum.TryParse(requirement.Id, out HeroQuality _), trait.Code + ": " + requirement.Id);
                            break;
                        default:
                            Assert.IsNotNull(catalog.FindTrait(requirement.Id), trait.Code + ": " + requirement.Id);
                            break;
                    }
                }
                foreach (string id in trait.ExcludesIds)
                    Assert.IsNotNull(catalog.FindTrait(id), trait.Code + " ⟷ " + id);
                foreach (string id in trait.OpensIds)
                    Assert.AreNotEqual(FeatureLayer.Feature, catalog.FindTrait(id)?.Layer ?? FeatureLayer.Feature, trait.Code + " → " + id);
            }
        }

        [Test]
        public void Defaults_SampleCardsKeepCatalogMeaning()
        {
            ProgressionCatalog catalog = ProgressionCatalog.CreateDefault();

            TraitCatalogEntry holdLine = catalog.Traits.Single(t => t.Code == "Б-01");
            Assert.AreEqual("Держать строй", holdLine.Name);
            Assert.AreEqual(3, holdLine.Ranks.Count);
            Assert.AreEqual("Командир строя", holdLine.RankName(3));
            Assert.IsTrue(holdLine.Requirements.Any(r => r.Id == NarrativeCompetencyIds.ShieldAndLine && r.Value == 2));

            TraitCatalogEntry scout = catalog.Traits.Single(t => t.Code == "Н-50");
            CollectionAssert.AreEqual(new[] { "Разведчик", "Глазомер", "Читает строй" }, scout.Ranks.Select(r => r.Name).ToArray());

            TraitCatalogEntry warlord = catalog.Traits.Single(t => t.Code == "Б-49");
            Assert.AreEqual(FeatureKind.Mastery, warlord.Kind);
            Assert.IsTrue(warlord.Requirements.Any(r => r.Kind == FeatureRequirementKind.Feature && r.Id == catalog.Traits.Single(t => t.Code == "Б-39").Id));

            TraitCatalogEntry heavyHand = catalog.Traits.Single(t => t.Code == "Б-32");
            Assert.Contains(catalog.Traits.Single(t => t.Code == "П-18").Id, heavyHand.OpensIds);
            Assert.Contains(catalog.Traits.Single(t => t.Code == "ПК-01").Id, catalog.Traits.Single(t => t.Code == "Б-45").OpensIds);

            TraitCatalogEntry oneBlade = catalog.Traits.Single(t => t.Code == "Б-30");
            Assert.Contains(catalog.Traits.Single(t => t.Code == "Б-31").Id, oneBlade.ExcludesIds);

            Assert.AreEqual(FeatureLimit.Expedition, catalog.Traits.Single(t => t.Code == "Н-01").Limit);
            Assert.AreEqual(FeatureKind.Flaw, catalog.Traits.Single(t => t.Code == "Н-103").Kind);
            Assert.AreEqual(FeatureOwner.Personal, catalog.Traits.Single(t => t.Code == "Н-84").Owner);
        }

        [Test]
        public void OnlyImplementedEntriesAreActive()
        {
            foreach (TraitCatalogEntry trait in Defaults())
            {
                Assert.AreEqual(ProgressionFeatureImplementations.IsImplemented(trait.Id), trait.Status == FeatureStatus.Active,
                    trait.Code + " " + trait.Name);
                Assert.AreEqual(ProgressionFeatureImplementations.IsStub(trait.Id), trait.Status == FeatureStatus.Stub,
                    trait.Code + " " + trait.Name + ": статус «Заглушка» — только у заглушек движка.");
            }
            Assert.AreEqual("Н-47", ProgressionCatalog.CreateDefault().FindTrait(NarrativeTraitIds.KnowsTheWay).Code);
            Assert.IsNotNull(ProgressionCatalog.CreateDefault().FindTrait(NarrativeTraitIds.Naturalist));
        }

        [Test]
        public void Asset_RoundTripsEveryCardField()
        {
            ProgressionDatabaseAsset database = ScriptableObject.CreateInstance<ProgressionDatabaseAsset>();
            try
            {
                database.FillFrom(ProgressionRules.CreateDefault(), ProgressionCatalog.CreateDefault());
                List<TraitCatalogEntry> source = Defaults();
                List<TraitCatalogEntry> loaded = database.ToCatalog().Traits;
                Assert.AreEqual(source.Count, loaded.Count);
                for (int i = 0; i < source.Count; i++)
                {
                    TraitCatalogEntry a = source[i];
                    TraitCatalogEntry b = loaded[i];
                    Assert.AreEqual(a.Id, b.Id);
                    Assert.AreEqual(a.Code, b.Code);
                    Assert.AreEqual(a.Layer, b.Layer, a.Id);
                    Assert.AreEqual(a.Owner, b.Owner, a.Id);
                    Assert.AreEqual(a.Kind, b.Kind, a.Id);
                    Assert.AreEqual(a.Status, b.Status, a.Id);
                    Assert.AreEqual(a.Implementation, b.Implementation, a.Id);
                    Assert.AreEqual(a.Sources, b.Sources, a.Id);
                    Assert.AreEqual(a.Limit, b.Limit, a.Id);
                    Assert.AreEqual(a.Display, b.Display, a.Id);
                    Assert.AreEqual(a.Ranks.Count, b.Ranks.Count, a.Id);
                    Assert.AreEqual(a.Requirements.Count, b.Requirements.Count, a.Id);
                    CollectionAssert.AreEqual(a.ExcludesIds, b.ExcludesIds, a.Id);
                    CollectionAssert.AreEqual(a.OpensIds, b.OpensIds, a.Id);
                }
            }
            finally
            {
                Object.DestroyImmediate(database);
            }
        }

        [Test]
        public void UpgradeSchema_AddsCatalog_KeepsEditedNames_OnlyOnce()
        {
            ProgressionDatabaseAsset database = ScriptableObject.CreateInstance<ProgressionDatabaseAsset>();
            try
            {
                database.FillFrom(ProgressionRules.CreateDefault(), new ProgressionCatalog());
                database.schemaVersion = 1;
                database.traits = new List<TraitRecord>
                {
                    new TraitRecord { id = NarrativeTraitIds.KnowsTheWay, displayName = "Своя правка", description = "Своё описание" },
                    new TraitRecord { id = NarrativeTraitIds.Naturalist, displayName = "Натуралист", description = "Старое" }
                };

                Assert.IsTrue(database.UpgradeSchema());
                Assert.AreEqual(ProgressionDatabaseAsset.CurrentSchemaVersion, database.schemaVersion);
                Assert.AreEqual(Defaults().Count, database.traits.Count);
                TraitRecord knowsTheWay = database.FindTrait(NarrativeTraitIds.KnowsTheWay);
                Assert.AreEqual("Своя правка", knowsTheWay.displayName);
                Assert.AreEqual("Своё описание", knowsTheWay.description);
                Assert.AreEqual("Н-47", knowsTheWay.code);
                Assert.AreEqual(FeatureStatus.Active, knowsTheWay.status);

                Assert.IsFalse(database.UpgradeSchema(), "Повторное обновление ничего не делает.");
                database.traits.RemoveAt(database.traits.Count - 1);
                Assert.AreEqual(1, database.AddMissingTraits());
                Assert.AreEqual(0, database.AddMissingTraits());
            }
            finally
            {
                Object.DestroyImmediate(database);
            }
        }

        [Test]
        public void Asset_IsUpgraded_AndHasNoTraitIssues()
        {
            ProgressionDatabaseAsset database = Resources.Load<ProgressionDatabaseAsset>(ProgressionDatabaseAsset.ResourcesPath);
            Assert.IsNotNull(database);
            Assert.AreEqual(ProgressionDatabaseAsset.CurrentSchemaVersion, database.schemaVersion, "Файл базы обновлён до карточек каталога.");
            foreach (TraitCatalogEntry entry in Defaults())
                Assert.IsNotNull(database.FindTrait(entry.Id), "Нет в базе: " + entry.Code + " " + entry.Name);

            List<string> errors = new List<string>();
            List<string> warnings = new List<string>();
            database.CollectValidationIssues(errors, warnings);
            Assert.That(errors, Is.Empty, string.Join("\n", errors));
            Assert.That(warnings.Where(w => w.Contains("Активна") || w.Contains("код в игре") || w.Contains("аглушк")), Is.Empty);
        }

        [Test]
        public void Window_TraitsPage_ListsAllAndShowsCard()
        {
            Editor.ProgressionDatabaseWindow window = ScriptableObject.CreateInstance<Editor.ProgressionDatabaseWindow>();
            try
            {
                window.CreateGUI();
                typeof(Editor.ProgressionDatabaseWindow)
                    .GetMethod("ShowPage", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    .Invoke(window, new object[] { "traits" });
                ListView list = window.rootVisualElement.Q<ListView>("trait-list");
                Assert.IsNotNull(list);
                ProgressionDatabaseAsset database = Resources.Load<ProgressionDatabaseAsset>(ProgressionDatabaseAsset.ResourcesPath);
                Assert.AreEqual(database.traits.Count, list.itemsSource.Count);
                list.SetSelection(0);
                Assert.IsNotNull(window.rootVisualElement.Query<DropdownField>().Where(field => field.label == "Статус").First(),
                    "Карточка выбранной записи построена.");
            }
            finally
            {
                Object.DestroyImmediate(window);
            }
        }
    }
}

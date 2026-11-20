using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace KingdomSurvival.BattleSandbox.Tests
{
    // Пакет «Боевые состояния»: валидатор соответствия импортированных
    // снимков (ProjectDocs/CombatStatusPack/Data) и нормализованного каталога.
    // Тексты источника не разбираются — сверяются ID, имена, отметки и связи.
    public sealed class StatusCatalogDataTests
    {
        private static readonly string Root = Path.GetFullPath("ProjectDocs/CombatStatusPack");
        private static JObject Load(string name) => JObject.Parse(File.ReadAllText(Path.Combine(Root, "Data", name)));

        [Test]
        public void EveryCatalogStatus_MatchesSourceIdNameAndCategory()
        {
            JArray rows = (JArray)Load("combat_statuses.json")["rows"];
            Assert.AreEqual(89, rows.Count);
            List<string> mismatches = new List<string>();
            foreach (JToken row in rows)
            {
                string id = (string)row["id"];
                SandboxStatusDefinition def = SandboxStatusCatalog.Get(id);
                if (def == null)
                    mismatches.Add(id + ": нет в каталоге");
                else if (def.Name != (string)row["name"] || def.Category != (string)row["category"])
                    mismatches.Add(id + ": " + def.Name + "/" + def.Category + " ≠ " + row["name"] + "/" + row["category"]);
                if ((bool?)row["deleted"] == true)
                    mismatches.Add(id + ": удалённое состояние не должно быть в каталоге");
            }
            CollectionAssert.IsEmpty(mismatches, string.Join("\n", mismatches));
            Assert.AreEqual(rows.Count, SandboxStatusCatalog.All.Count, "каталог не содержит лишних ID");
        }

        [Test]
        public void StrongControlGroup_MatchesCommonRules()
        {
            string[] expected = { "status.025", "status.028", "status.030", "status.032", "status.033", "status.056", "status.058", "status.059", "status.061", "status.079" };
            CollectionAssert.AreEquivalent(expected, SandboxStatusCatalog.All.Where(def => def.StrongControl).Select(def => def.Id));
        }

        [Test]
        public void EveryStatus_HasIconInManifest_AndLocalSprite()
        {
            JArray items = (JArray)Load("status_icon_manifest.json")["items"];
            HashSet<string> mapped = new HashSet<string>(items.Select(item => (string)item["statusId"]));
            List<string> missing = new List<string>();
            foreach (SandboxStatusDefinition def in SandboxStatusCatalog.All)
            {
                if (!mapped.Contains(def.IconId))
                    missing.Add(def.Id + ": нет в manifest");
                string png = Path.GetFullPath("Assets/_Project/BattleSandbox/Resources/StatusIcons/" + def.IconId.Replace('.', '_') + ".png");
                if (!File.Exists(png))
                    missing.Add(def.Id + ": нет локального PNG " + png + " (растровые ассеты не в git — скопировать из пакета)");
            }
            CollectionAssert.IsEmpty(missing, string.Join("\n", missing));
        }

        [Test]
        public void PassiveBank_FullImport_CountsAndFlagsPreserved()
        {
            JObject bank = Load("passive_tags_effective.json");
            JArray records = (JArray)bank["records"];
            JObject counts = Load("package_counts.json");
            Assert.AreEqual((int)counts["tagsTotal"], records.Count);
            Assert.AreEqual((int)counts["favoriteFlags"], records.Count(r => (bool?)r["favorite"] == true));
            Assert.AreEqual((int)counts["trash"], records.Count(r => (bool?)r["deleted"] == true));
            Assert.AreEqual((int)counts["activeOutsideCombat"], records.Count(r => (bool?)r["archived"] == true && (bool?)r["deleted"] != true));
            Assert.AreEqual(records.Count, records.Select(r => (string)r["id"]).Distinct().Count(), "ID тегов уникальны");

            HashSet<string> purged = new HashSet<string>(((JArray)bank["purgedIds"]).Select(t => (string)t));
            Assert.IsFalse(records.Any(r => purged.Contains((string)r["id"])), "навсегда удалённые ID не воскрешены");

            JObject selection = Load("selection_manifest.json");
            HashSet<string> trash = new HashSet<string>(((JArray)selection["trashIds"]).Select(t => (string)t));
            HashSet<string> active = new HashSet<string>(((JArray)selection["activeCombatIds"]).Select(t => (string)t));
            Assert.AreEqual((int)counts["activeCombat"], active.Count);
            Assert.IsFalse(active.Overlaps(trash), "корзина не входит в активный пул");
            Assert.IsTrue(((JArray)selection["selectedActiveCombatIds"]).All(t => active.Contains((string)t)), "избранные — подмножество активных");
        }

        [Test]
        public void StatusBindings_PointToCatalogStatuses()
        {
            JArray records = (JArray)Load("passive_tags_effective.json")["records"];
            List<string> unknown = new List<string>();
            foreach (JToken record in records.Where(r => r["statusIds"] != null))
            {
                foreach (JToken id in (JArray)record["statusIds"])
                {
                    if (SandboxStatusCatalog.Get((string)id) == null)
                        unknown.Add(record["id"] + " → " + id);
                }
            }
            CollectionAssert.IsEmpty(unknown, string.Join("\n", unknown));
        }

        [Test]
        public void PassiveCatalog_OnlyActiveCombatTags_NoTrashNoArchive()
        {
            JObject selection = Load("selection_manifest.json");
            HashSet<string> active = new HashSet<string>(((JArray)selection["activeCombatIds"]).Select(t => (string)t));
            List<string> wrong = SandboxPassiveCatalog.All.Where(def => !active.Contains(def.TagId)).Select(def => def.TagId).ToList();
            CollectionAssert.IsEmpty(wrong, "в исполнитель попали неактивные/удалённые теги: " + string.Join(", ", wrong));
            foreach (SandboxPassiveDefinition def in SandboxPassiveCatalog.All)
            {
                foreach (SandboxPassiveRule rule in def.Rules)
                {
                    foreach (string statusId in rule.StatusIds)
                        Assert.IsNotNull(SandboxStatusCatalog.Get(statusId), def.TagId + " → " + statusId);
                }
            }
        }
    }
}

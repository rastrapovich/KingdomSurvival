using System.Linq;
using KingdomSurvival.BattlefieldDatabase;
using KingdomSurvival.DialogueDatabase;
using KingdomSurvival.QuestWorkshopBridge.Editor;
using KingdomSurvival.UnitDatabase;
using KingdomSurvival.WorldMapVisual;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace KingdomSurvival.QuestWorkshopBridge.Tests
{
    // Место мастерской как место игры: место карты, правило генератора и
    // исследуемое место переносятся во временные базы; повторный перенос
    // не трогает расстановку, сделанную в Unity.
    public sealed class QuestWorkshopPlacesTests
    {
        private LocalLocationDatabaseAsset locals;
        private WorldMapDatabaseAsset map;
        private WorldMapGeneratorAsset generator;
        private DialogueDatabaseAsset dialogues;
        private WorkshopPlaceAssets assets;

        [SetUp]
        public void SetUp()
        {
            locals = ScriptableObject.CreateInstance<LocalLocationDatabaseAsset>();
            map = ScriptableObject.CreateInstance<WorldMapDatabaseAsset>();
            generator = ScriptableObject.CreateInstance<WorldMapGeneratorAsset>();
            dialogues = ScriptableObject.CreateInstance<DialogueDatabaseAsset>();
            assets = new WorkshopPlaceAssets
            {
                Locals = locals,
                Map = map,
                Generator = generator,
                Dialogues = dialogues,
                // Поля боя и существа — настоящие базы проекта, только для чтения.
                Battlefields = AssetDatabase.LoadAssetAtPath<BattlefieldDatabaseAsset>(WorkshopPlaceAssets.BattlefieldsPath),
                Units = Resources.Load<UnitDatabaseAsset>(UnitDatabaseAsset.ResourcesPath)
            };
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(locals);
            Object.DestroyImmediate(map);
            Object.DestroyImmediate(generator);
            Object.DestroyImmediate(dialogues);
        }

        private const string Workspace = @"{
  ""format"": ""ks-quest-workspace"", ""version"": 1, ""savedAt"": ""2026-10-23T12:00:00.000Z"", ""revision"": 3,
  ""workspace"": {
    ""name"": ""Тест"", ""people"": [],
    ""facts"": [ { ""id"": ""fact_cleared"", ""title"": ""Логово пусто"", ""kind"": ""flag"", ""unityId"": ""test.cave.cleared"" } ],
    ""quests"": [ { ""id"": ""quest_cave"", ""title"": ""Стук в пещере"", ""line"": ""Тест"", ""unityId"": """", ""scenes"": [
      { ""id"": ""scene_wall"", ""title"": ""Завал"", ""unityId"": ""test_cave_wall"", ""startNodeId"": ""n1"",
        ""conditions"": { ""mode"": ""All"", ""items"": [] },
        ""nodes"": [ { ""id"": ""n1"", ""title"": ""1"", ""speakerId"": """",
          ""blocks"": [ { ""id"": ""b1"", ""kind"": ""Narration"", ""text"": ""Камни уложены изнутри."", ""conditions"": { ""mode"": ""All"", ""items"": [] }, ""effects"": [] } ],
          ""choices"": [ { ""id"": ""c1"", ""text"": ""Отойти."", ""kind"": ""Exit"", ""unavailable"": ""Hidden"", ""conditions"": { ""mode"": ""All"", ""items"": [] }, ""effects"": [], ""failureEffects"": [] } ] } ] } ] } ],
    ""places"": [ {
      ""id"": ""place_cave"", ""name"": ""Стучащая пещера"", ""description"": ""Из глубины слышны удары."",
      ""location"": {
        ""enabled"": true, ""worldId"": ""test_knocking_cave"", ""modeId"": ""freeplay.home_region"",
        ""map"": { ""enabled"": true, ""threat"": ""средняя"", ""visible"": true, ""discovered"": false },
        ""generator"": { ""enabled"": true, ""terrains"": [""Hills"", ""Mountains""], ""minDistanceHexes"": 8, ""slotTags"": ""Cave"", ""drawMark"": true },
        ""local"": { ""enabled"": true, ""id"": ""local.test_knocking_cave"", ""battlefieldId"": ""old_mine_01"",
          ""objects"": [
            { ""id"": ""cave.wall"", ""label"": ""Завал"", ""actionLabel"": ""Осмотреть завал"", ""kind"": ""Dialogue"", ""dialogue"": ""scene_wall"", ""onceOnly"": true },
            { ""id"": ""cave.depth"", ""label"": ""Глубина"", ""actionLabel"": ""Заглянуть"", ""kind"": ""Inspect"", ""text"": ""Тихо."", ""onceOnly"": false, ""requiresFlag"": ""fact_cleared"" }
          ],
          ""enemies"": [
            { ""instanceId"": ""cave.beast.1"", ""unitTypeId"": ""forest_beast"", ""level"": 1, ""encounterId"": ""cave.lair"" },
            { ""instanceId"": ""cave.beast.2"", ""unitTypeId"": ""forest_beast"", ""level"": 1, ""encounterId"": ""cave.lair"" }
          ],
          ""encounters"": [ { ""id"": ""cave.lair"", ""allowRetreat"": true, ""resolvedFlag"": ""fact_cleared"" } ]
        }
      }
    } ]
  }
}";

        private static WorkshopProject Project(string json = Workspace) => WorkshopProject.Parse(json);

        [Test]
        public void NewPlace_CreatesMapLocationGeneratorRuleAndExplorablePlace()
        {
            Assume.That(assets.Battlefields, Is.Not.Null, "Нужна База полей боя проекта.");
            WorkshopProject project = Project();
            WorkshopPlaceReport report = QuestWorkshopPlaces.Analyze(project, assets).Single();
            Assert.That(report.HasErrors, Is.False, string.Join("\n", report.Issues));
            Assert.That(report.State, Is.EqualTo(WorkshopSceneState.New));

            WorkshopImportResult result = new WorkshopImportResult();
            QuestWorkshopPlaces.Apply(project, new[] { report.Place }, assets, result);

            WorldMapLocationDefinition mapLocation = map.FindLocation("test_knocking_cave");
            Assert.That(mapLocation, Is.Not.Null);
            Assert.That(mapLocation.DisplayName, Is.EqualTo("Стучащая пещера"));
            Assert.That(mapLocation.InteractionDescription, Is.EqualTo("Из глубины слышны удары."));
            Assert.That(mapLocation.ModeId, Is.EqualTo(CampaignStartOptions.FreePlayId));
            Assert.That(mapLocation.Mode, Is.EqualTo(WorldMapPlacementMode.Anchored));

            WorldMapLandmarkRule rule = generator.Settings.Landmarks.Single(r => r.LocationId == "test_knocking_cave");
            Assert.That(rule.Count, Is.EqualTo(1));
            CollectionAssert.AreEquivalent(new[] { WorldMapGameplayTerrainType.Hills, WorldMapGameplayTerrainType.Mountains }, rule.Terrains);

            LocalLocationDefinition place = locals.locations.Single(l => l.Id == "local.test_knocking_cave");
            Assert.That(place.WorldLocationId, Is.EqualTo("test_knocking_cave"));
            Assert.That(place.ModeId, Is.EqualTo(CampaignStartOptions.FreePlayId));
            Assert.That(place.FindObject("cave.wall").DialogueId, Is.EqualTo("test_cave_wall"), "Ссылка на сцену мастерской становится ID диалога.");
            Assert.That(place.FindObject("cave.depth").RequiresFlag, Is.EqualTo("test.cave.cleared"), "Сведение мастерской становится флагом Unity.");
            Assert.That(place.FindEncounter("cave.lair").ResolvedFlag, Is.EqualTo("test.cave.cleared"));
            Assert.That(place.FindEncounter("cave.lair").BattleIdPrefix, Is.EqualTo("battle.local.test_knocking_cave.cave.lair."));
            Assert.That(locals.pointFormatVersion, Is.EqualTo(LocalLocationDatabaseAsset.CurrentPointFormatVersion));

            Assert.That(result.ValidationIssues, Is.Empty, "Заготовка расстановки проходит проверку места:\n" + string.Join("\n", result.ValidationIssues));
            Assert.That(result.AddedMapLocations, Is.EquivalentTo(new[] { "test_knocking_cave" }));
            Assert.That(result.AddedLocalLocations, Is.EquivalentTo(new[] { "local.test_knocking_cave" }));
            Assert.That(QuestWorkshopPlaces.Analyze(project, assets).Single().State, Is.EqualTo(WorkshopSceneState.Same), "После переноса всё совпадает.");
        }

        [Test]
        public void SecondTransfer_UpdatesContent_KeepsPointsMadeInUnity()
        {
            WorkshopProject project = Project();
            QuestWorkshopPlaces.Apply(project, project.Places, assets, new WorkshopImportResult());
            LocalLocationDefinition place = locals.locations.Single();
            place.FindObject("cave.wall").Point = new LocalPointData(777, 555);
            place.Objects.Add(new LocalObjectDefinition { Id = "cave.unity_only", Kind = LocalObjectKind.Inspect, Text = "Сделано в Unity." });

            WorkshopProject edited = Project(Workspace.Replace("\"label\": \"Завал\"", "\"label\": \"Каменный завал\""));
            WorkshopPlaceReport report = QuestWorkshopPlaces.Analyze(edited, assets).Single();
            Assert.That(report.State, Is.EqualTo(WorkshopSceneState.Changed));
            Assert.That(report.Issues.Any(i => i.Message.Contains("cave.unity_only")), Is.True, "Объект только из Unity виден в отчёте.");

            WorkshopImportResult result = new WorkshopImportResult();
            QuestWorkshopPlaces.Apply(edited, edited.Places, assets, result);
            LocalLocationDefinition updated = locals.locations.Single();
            Assert.That(updated.FindObject("cave.wall").Label, Is.EqualTo("Каменный завал"));
            Assert.That(updated.FindObject("cave.wall").Point.X, Is.EqualTo(777f), "Точку из Unity перенос не трогает.");
            Assert.That(updated.FindObject("cave.unity_only"), Is.Not.Null, "Объект, которого нет в мастерской, остаётся.");
            Assert.That(result.UpdatedLocalLocations, Is.EquivalentTo(new[] { "local.test_knocking_cave" }));
            Assert.That(result.AddedMapLocations, Is.Empty);
        }

        [Test]
        public void InitialPoints_AreUsedOnlyWhenTheRecordIsCreated()
        {
            string withPoints = Workspace
                .Replace("\"kind\": \"Inspect\", \"text\": \"Тихо.\"", "\"kind\": \"Inspect\", \"text\": \"Тихо.\", \"point\": { \"x\": 1500, \"y\": 600 }")
                .Replace("{ \"id\": \"cave.lair\", \"allowRetreat\": true,", "{ \"id\": \"cave.lair\", \"area\": { \"x\": 900, \"y\": 300, \"width\": 120, \"height\": 480 }, \"retreat\": { \"x\": 450, \"y\": 540 }, \"allowRetreat\": true,")
                .Replace("\"battlefieldId\": \"old_mine_01\",", "\"battlefieldId\": \"old_mine_01\", \"entrance\": { \"x\": 260, \"y\": 520, \"label\": \"Лаз\" },");
            WorkshopProject project = Project(withPoints);
            QuestWorkshopPlaces.Apply(project, project.Places, assets, new WorkshopImportResult());
            LocalLocationDefinition place = locals.locations.Single();
            Assert.That(place.FindObject("cave.depth").Point.X, Is.EqualTo(1500f));
            Assert.That(place.FindEncounter("cave.lair").TriggerArea.X, Is.EqualTo(900f));
            Assert.That(place.FindEncounter("cave.lair").RetreatPoint.X, Is.EqualTo(450f));
            Assert.That(place.Entrances[0].Label, Is.EqualTo("Лаз"));
            Assert.That(place.Entrances[0].Point.X, Is.EqualTo(260f));

            WorkshopProject moved = Project(withPoints.Replace("\"x\": 1500, \"y\": 600", "\"x\": 700, \"y\": 600"));
            QuestWorkshopPlaces.Apply(moved, moved.Places, assets, new WorkshopImportResult());
            Assert.That(locals.locations.Single().FindObject("cave.depth").Point.X, Is.EqualTo(1500f), "Точку существующего объекта правят в Unity, не мастерская.");
        }

        [Test]
        public void BrokenReferences_AreErrors_AndAreNotTransferred()
        {
            string broken = Workspace
                .Replace("\"unitTypeId\": \"forest_beast\", \"level\": 1, \"encounterId\": \"cave.lair\" },", "\"unitTypeId\": \"no_such_beast\", \"level\": 1, \"encounterId\": \"cave.lair\" },")
                .Replace("\"dialogue\": \"scene_wall\"", "\"dialogue\": \"no_such_dialogue\"");
            WorkshopProject project = Project(broken);
            WorkshopPlaceReport report = QuestWorkshopPlaces.Analyze(project, assets).Single();
            Assert.That(report.State, Is.EqualTo(WorkshopSceneState.Errors));
            Assert.That(report.Issues.Any(i => i.Message.Contains("no_such_dialogue")), Is.True);
            if (assets.Units != null)
                Assert.That(report.Issues.Any(i => i.Message.Contains("no_such_beast")), Is.True);

            WorkshopImportResult result = new WorkshopImportResult();
            QuestWorkshopPlaces.Apply(project, project.Places, assets, result);
            Assert.That(result.Skipped, Has.Count.EqualTo(1));
            Assert.That(locals.locations, Is.Empty);
            Assert.That(map.FindLocation("test_knocking_cave"), Is.Null);
        }

        [Test]
        public void Snapshot_ListsUnityPlacesAndSuggestions()
        {
            WorkshopProject project = Project();
            QuestWorkshopPlaces.Apply(project, project.Places, assets, new WorkshopImportResult());
            string json = QuestWorkshopImporter.BuildSnapshotJson(dialogues, null, null, "abc1234", null, assets);
            JsonNode root = new JsonNode(QuestWorkshopJson.Parse(json));
            Assert.That(root["localLocations"].Items().Single()["id"].Str(), Is.EqualTo("local.test_knocking_cave"));
            Assert.That(root["mapLocations"].Items().Single()["modeId"].Str(), Is.EqualTo(CampaignStartOptions.FreePlayId));
            Assert.That(root["landmarks"].Items().Any(l => l["locationId"].Str() == "test_knocking_cave"), Is.True);
            if (assets.Battlefields != null)
                Assert.That(root["battlefields"].Strings(), Does.Contain("old_mine_01"));
        }
    }
}

using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using KingdomSurvival.DialogueDatabase;
using KingdomSurvival.Encounters;
using KingdomSurvival.QuestWorkshopBridge.Editor;
using NUnit.Framework;
using UnityEngine;

namespace KingdomSurvival.QuestWorkshopBridge.Tests
{
    public sealed class QuestWorkshopBridgeTests
    {
        private DialogueDatabaseAsset dialogues;
        private EncounterDatabaseAsset encounters;
        private EncounterFlagRegistryAsset flags;

        [SetUp]
        public void SetUp()
        {
            dialogues = ScriptableObject.CreateInstance<DialogueDatabaseAsset>();
            encounters = ScriptableObject.CreateInstance<EncounterDatabaseAsset>();
            flags = ScriptableObject.CreateInstance<EncounterFlagRegistryAsset>();
            PrivateList<EncounterDatabaseAsset, EncounterPoolDefinition>(encounters, "pools")
                .Add(new EncounterPoolDefinition { PoolId = "ROAD_POOL_01", DisplayName = "Дорога" });
            Speakers().Add(JsonUtility.FromJson<DialogueSpeakerData>("{\"id\":\"narrator\",\"displayName\":\"Рассказчик\"}"));
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(dialogues);
            Object.DestroyImmediate(encounters);
            Object.DestroyImmediate(flags);
        }

        // Сцена в формате мастерской: ID с префиксами, пассивная и активная
        // проверки, условия, эффекты, реплика спутника и сцена-встреча.
        private const string WorkspaceJson = @"{
  ""format"": ""ks-quest-workspace"", ""version"": 1, ""savedAt"": ""2026-10-18T12:00:00.000Z"", ""revision"": 7,
  ""workspace"": {
    ""schemaVersion"": 1, ""name"": ""Тест"",
    ""people"": [
      { ""id"": ""unity_person_narrator"", ""name"": ""Рассказчик"", ""role"": """", ""unityId"": ""narrator"" },
      { ""id"": ""person_test_guest"", ""name"": ""Гость"", ""role"": ""Путник"", ""unityId"": ""test_guest"" }
    ],
    ""facts"": [
      { ""id"": ""unity_flag_test.flag.met"", ""title"": ""Встретили гостя"", ""kind"": ""flag"", ""text"": ""Гость у ворот"", ""unityId"": ""test.flag.met"", ""category"": ""test"", ""status"": ""Active"" },
      { ""id"": ""fact_local"", ""title"": ""Местное"", ""kind"": ""knowledge"", ""text"": """", ""unityId"": ""test.knowledge.local"" }
    ],
    ""quests"": [ {
      ""id"": ""quest_test"", ""title"": ""Гость"", ""line"": ""Тест · линия"", ""mode"": ""Свободная игра"", ""role"": ""Региональная история"",
      ""archived"": false, ""unityId"": """",
      ""scenes"": [ {
        ""id"": ""scene_a"", ""title"": ""Гость у ворот"", ""summary"": ""Кто-то стучит."", ""unityId"": ""test_guest_gate"",
        ""unityCategory"": """", ""unityStatus"": """", ""unityTags"": [""тест""],
        ""startNodeId"": ""ud:test_guest_gate:start"",
        ""conditions"": { ""mode"": ""All"", ""items"": [ { ""id"": ""c1"", ""type"": ""FlagSet"", ""ref"": ""unity_flag_freeplay.mode"", ""value"": 0, ""negate"": false } ] },
        ""encounter"": { ""enabled"": true, ""encounterId"": ""TEST_GUEST_01"", ""category"": ""Road"", ""status"": ""Draft"", ""poolId"": ""ROAD_POOL_01"",
          ""durationClass"": ""Short"", ""memoryClass"": ""Local"", ""functions"": [""Relationship""], ""tags"": [""test""], ""selectionMode"": ""Pool"",
          ""discoveryChance"": 40, ""weight"": 2, ""unlimited"": false, ""maxOccurrences"": 1, ""cooldownHours"": 0,
          ""forbiddenFlags"": [""test.flag.met""], ""flagsOnComplete"": [""test.flag.met""] },
        ""nodes"": [
          { ""id"": ""ud:test_guest_gate:start"", ""title"": ""Реплика 1"", ""speakerId"": ""unity_person_narrator"",
            ""blocks"": [
              { ""id"": ""ud:test_guest_gate:b_main"", ""kind"": ""Narration"", ""speakerId"": """", ""text"": ""У ворот стоит человек."",
                ""conditions"": { ""mode"": ""All"", ""items"": [] }, ""effects"": [] },
              { ""id"": ""ud:test_guest_gate:b_eye"", ""kind"": ""Observation"", ""speakerId"": """", ""text"": ""Сапоги у него сухие."",
                ""conditions"": { ""mode"": ""All"", ""items"": [ { ""id"": ""c2"", ""type"": ""QualityAtLeast"", ""ref"": ""Instinct"", ""value"": 6, ""negate"": false } ] },
                ""check"": { ""id"": ""unity_check_test_guest_boots"", ""kind"": ""Passive"", ""quality"": ""Judgment"", ""competencyId"": ""observation"", ""difficulty"": 11, ""modifiers"": [] },
                ""effects"": [ { ""id"": ""unity_effect_test.boots"", ""type"": ""AddKnowledge"", ""ref"": ""fact_local"", ""amount"": 0, ""note"": """" } ] },
              { ""id"": ""block_guest_line"", ""kind"": ""CompanionLine"", ""speakerId"": ""person_test_guest"", ""text"": ""— Пустите погреться."",
                ""conditions"": { ""mode"": ""All"", ""items"": [] }, ""effects"": [] }
            ],
            ""choices"": [
              { ""id"": ""ud:test_guest_gate:c_ask"", ""text"": ""Расспросить."", ""kind"": ""ActiveDecisive"", ""unavailable"": ""DisabledWithHint"", ""hint"": ""Гость не доверяет чужим."",
                ""conditions"": { ""mode"": ""All"", ""items"": [] },
                ""successNodeId"": ""ud:test_guest_gate:ok"", ""failureNodeId"": ""ud:test_guest_gate:fail"", ""nextNodeId"": """",
                ""check"": { ""id"": ""unity_check_test_guest_ask"", ""kind"": ""ActiveDecisive"", ""quality"": ""Character"", ""competencyId"": ""negotiation"", ""difficulty"": 13,
                  ""modifiers"": [ { ""id"": ""unity_mod_knows_custom"", ""label"": ""Знает обычай"", ""amount"": 1,
                    ""conditions"": { ""mode"": ""All"", ""items"": [ { ""id"": ""c3"", ""type"": ""KnowledgeKnown"", ""ref"": ""fact_local"", ""value"": 0, ""negate"": false } ] } } ] },
                ""effects"": [ { ""id"": ""unity_effect_test.met"", ""type"": ""SetFlag"", ""ref"": ""unity_flag_test.flag.met"", ""amount"": 0, ""note"": """" },
                               { ""id"": ""effect_plan"", ""type"": ""Planned"", ""ref"": """", ""amount"": 0, ""note"": ""Гость остаётся на ночь"" } ],
                ""failureEffects"": [] },
              { ""id"": ""ud:test_guest_gate:c_leave"", ""text"": ""Не открывать."", ""kind"": ""Exit"", ""unavailable"": ""Hidden"",
                ""conditions"": { ""mode"": ""All"", ""items"": [] }, ""nextNodeId"": """", ""successNodeId"": """", ""failureNodeId"": """", ""effects"": [], ""failureEffects"": [] }
            ] },
          { ""id"": ""ud:test_guest_gate:ok"", ""title"": ""Реплика 2"", ""speakerId"": ""person_test_guest"",
            ""blocks"": [ { ""id"": ""ud:test_guest_gate:b_ok"", ""kind"": ""MainLine"", ""speakerId"": """", ""text"": ""— С дальней мельницы я."", ""conditions"": { ""mode"": ""All"", ""items"": [] }, ""effects"": [] } ],
            ""choices"": [ { ""id"": ""ud:test_guest_gate:c_end1"", ""text"": ""Впустить."", ""kind"": ""Exit"", ""unavailable"": ""Hidden"", ""conditions"": { ""mode"": ""All"", ""items"": [] }, ""effects"": [], ""failureEffects"": [] } ] },
          { ""id"": ""ud:test_guest_gate:fail"", ""title"": ""Реплика 3"", ""speakerId"": ""person_test_guest"",
            ""blocks"": [ { ""id"": ""ud:test_guest_gate:b_fail"", ""kind"": ""MainLine"", ""speakerId"": """", ""text"": ""— Долгая история."", ""conditions"": { ""mode"": ""All"", ""items"": [] }, ""effects"": [] } ],
            ""choices"": [ { ""id"": ""ud:test_guest_gate:c_end2"", ""text"": ""Уйти."", ""kind"": ""Exit"", ""unavailable"": ""Hidden"", ""conditions"": { ""mode"": ""All"", ""items"": [] }, ""effects"": [], ""failureEffects"": [] } ] }
        ]
      },
      { ""id"": ""scene_plan"", ""title"": ""Позже"", ""summary"": """", ""startNodeId"": """", ""conditions"": { ""mode"": ""All"", ""items"": [] }, ""nodes"": [] } ]
    } ]
  }
}";

        private WorkshopProject Project()
        {
            return WorkshopProject.Parse(WorkspaceJson);
        }

        private WorkshopScene Scene(WorkshopProject project)
        {
            return project.Quests[0].Scenes[0];
        }

        [Test]
        public void Json_ParsesNestedValuesAndSerializesBack()
        {
            object parsed = QuestWorkshopJson.Parse("{\"a\":[1,2.5,\"x\\n\\u0416\",true,null],\"b\":{\"c\":\"д\"}}");
            JsonNode node = new JsonNode(parsed);
            Assert.That(node["a"].Items().Count(), Is.EqualTo(5));
            Assert.That(node["a"].Items().ElementAt(2).Str(), Is.EqualTo("x\nЖ"));
            Assert.That(node["b"]["c"].Str(), Is.EqualTo("д"));
            Assert.That(node["missing"]["deeper"].Str("нет"), Is.EqualTo("нет"));
            string again = QuestWorkshopJson.Serialize(parsed);
            Assert.That(new JsonNode(QuestWorkshopJson.Parse(again))["a"].Items().ElementAt(1).Str(), Is.EqualTo("2.5"));
        }

        [Test]
        public void Project_ReadsScenesAndOptionalObjects()
        {
            WorkshopProject project = Project();
            Assert.That(project.Revision, Is.EqualTo(7));
            WorkshopScene scene = Scene(project);
            Assert.That(scene.UnityId, Is.EqualTo("test_guest_gate"));
            Assert.That(scene.Encounter, Is.Not.Null);
            Assert.That(scene.Nodes[0].Blocks[0].Check, Is.Null, "Реплика без проверки не получает пустую проверку.");
            Assert.That(scene.Nodes[0].Blocks[1].Check, Is.Not.Null);
            Assert.That(project.Quests[0].Scenes[1].Encounter, Is.Null);
        }

        [Test]
        public void Analyze_NewScene_IsTransferableAndPlanSceneIsPlan()
        {
            WorkshopProject project = Project();
            List<WorkshopSceneReport> reports = QuestWorkshopImporter.Analyze(project, dialogues, encounters);
            Assert.That(reports[0].State, Is.EqualTo(WorkshopSceneState.New), string.Join("\n", reports[0].Issues));
            Assert.That(reports[0].Transferable, Is.True);
            Assert.That(reports[0].EncounterState, Is.EqualTo("новая"));
            Assert.That(reports[0].Issues.Any(i => i.Severity == WorkshopIssueSeverity.Warning && i.Message.Contains("Гость остаётся на ночь")), Is.True,
                "Авторский замысел предупреждает, что нужен код.");
            Assert.That(reports[1].State, Is.EqualTo(WorkshopSceneState.Plan));
        }

        [Test]
        public void Apply_CreatesDialogueWithUnityIds()
        {
            WorkshopProject project = Project();
            WorkshopImportResult result = QuestWorkshopImporter.Apply(project, new[] { Scene(project) }, dialogues, encounters, flags);
            Assert.That(result.AddedDialogues, Is.EquivalentTo(new[] { "test_guest_gate" }), result.Summary());

            DialogueDefinitionData dialogue = dialogues.FindDialogue("test_guest_gate");
            Assert.That(dialogue.Title, Is.EqualTo("Гость у ворот"));
            Assert.That(dialogue.Category, Is.EqualTo(DialogueCategory.RandomEncounter), "Сцена-встреча по умолчанию — RandomEncounter.");
            Assert.That(dialogue.Status, Is.EqualTo(DialogueProductionStatus.Working));
            Assert.That(dialogue.DeveloperComment, Is.EqualTo("Кто-то стучит."));
            Assert.That(dialogue.Tags, Is.EquivalentTo(new[] { "тест" }));
            Assert.That(dialogue.StartNodeId, Is.EqualTo("start"));

            DialogueNodeData start = dialogue.Nodes[0];
            Assert.That(start.SpeakerId, Is.EqualTo("narrator"));
            Assert.That(start.TextBlocks[0].Kind, Is.EqualTo(DialogueTextBlockKind.Narration));
            DialogueTextBlockData eye = start.TextBlocks[1];
            Assert.That(eye.BlockId, Is.EqualTo("b_eye"));
            Assert.That(eye.HasPassiveCheck, Is.True);
            Assert.That(eye.PassiveCheck.CheckId, Is.EqualTo("test_guest_boots"));
            Assert.That(eye.PassiveCheck.Quality, Is.EqualTo(HeroQuality.Judgment));
            Assert.That(eye.Conditions.Conditions[0].Type, Is.EqualTo(NarrativeConditionType.QualityAtLeast));
            Assert.That(eye.Conditions.Conditions[0].QualityParam, Is.EqualTo(HeroQuality.Instinct));
            Assert.That(eye.OnRevealEffects[0].StringParam, Is.EqualTo("test.knowledge.local"), "Сведение без префикса берёт unityId из справочника.");
            Assert.That(start.TextBlocks[2].SpeakerIdOverride, Is.EqualTo("test_guest"));
            Assert.That(start.TextBlocks[2].BlockId, Is.EqualTo("block_guest_line"), "Собственный ID мастерской переносится как есть.");

            DialogueChoiceData ask = start.Choices[0];
            Assert.That(ask.ChoiceId, Is.EqualTo("c_ask"));
            Assert.That(ask.Kind, Is.EqualTo(DialogueChoiceKind.ActiveDecisive));
            Assert.That(ask.UnavailablePresentation, Is.EqualTo(DialogueChoiceUnavailablePresentation.DisabledWithHint));
            Assert.That(ask.UnavailableHint, Is.EqualTo("Гость не доверяет чужим."), "Подсказка недоступного ответа переносится.");
            Assert.That(ask.Check.CheckId, Is.EqualTo("test_guest_ask"));
            Assert.That(ask.Check.Kind, Is.EqualTo(NarrativeCheckKind.ActiveDecisive));
            Assert.That(ask.Check.ModifierRules[0].SourceId, Is.EqualTo("knows_custom"));
            Assert.That(ask.Check.ModifierRules[0].Condition.Conditions[0].StringParam, Is.EqualTo("test.knowledge.local"));
            Assert.That(ask.SuccessNodeId, Is.EqualTo("ok"));
            Assert.That(ask.FailureNodeId, Is.EqualTo("fail"));
            Assert.That(ask.SuccessEffects.Count, Is.EqualTo(1), "Авторский замысел в Unity не переносится.");
            Assert.That(ask.SuccessEffects[0].EffectExecutionId, Is.EqualTo("test.met"));
            Assert.That(ask.SuccessEffects[0].StringParam, Is.EqualTo("test.flag.met"));
            Assert.That(start.Choices[1].IsExit, Is.True);

            Assert.That(result.AddedSpeakers, Is.EquivalentTo(new[] { "test_guest" }));
            Assert.That(dialogues.FindSpeaker("test_guest").DisplayName, Is.EqualTo("Гость"));

            List<string> issues = new List<string>();
            dialogues.CollectValidationIssuesForDialogue("test_guest_gate", issues);
            Assert.That(issues, Is.Empty, string.Join("\n", issues));
        }

        [Test]
        public void Apply_CreatesEncounterAndRegistersItsFlags()
        {
            WorkshopProject project = Project();
            WorkshopImportResult result = QuestWorkshopImporter.Apply(project, new[] { Scene(project) }, dialogues, encounters, flags);
            EncounterDefinition encounter = encounters.FindById("TEST_GUEST_01");
            Assert.That(encounter, Is.Not.Null, result.Summary());
            Assert.That(encounter.DialogueId, Is.EqualTo("test_guest_gate"));
            Assert.That(encounter.DisplayName, Is.EqualTo("Гость у ворот"));
            Assert.That(encounter.DurationClass, Is.EqualTo(EncounterDurationClass.Short));
            Assert.That(encounter.MemoryClass, Is.EqualTo(EncounterMemoryClass.Local));
            Assert.That(encounter.Functions, Is.EquivalentTo(new[] { EncounterFunction.Relationship }));
            Assert.That(encounter.DiscoveryChancePercent, Is.EqualTo(40));
            Assert.That(encounter.SelectionWeight, Is.EqualTo(2));
            Assert.That(encounter.RequiredConditions.Conditions[0].StringParam, Is.EqualTo("freeplay.mode"));
            Assert.That(encounter.ForbiddenFlags, Is.EquivalentTo(new[] { "test.flag.met" }));
            EncounterFlagDefinition flag = flags.FindById("test.flag.met");
            Assert.That(flag, Is.Not.Null);
            Assert.That(flag.DisplayName, Is.EqualTo("Встретили гостя"));
            Assert.That(flag.Category, Is.EqualTo("test"));
        }

        [Test]
        public void Apply_IsIdempotent()
        {
            WorkshopProject project = Project();
            QuestWorkshopImporter.Apply(project, new[] { Scene(project) }, dialogues, encounters, flags);
            List<WorkshopSceneReport> reports = QuestWorkshopImporter.Analyze(project, dialogues, encounters);
            Assert.That(reports[0].State, Is.EqualTo(WorkshopSceneState.Same), string.Join("\n", reports[0].Differences));
            Assert.That(reports[0].EncounterState, Is.EqualTo("совпадает"));
            Assert.That(reports[0].Transferable, Is.False);

            WorkshopImportResult second = QuestWorkshopImporter.Apply(project, new[] { Scene(project) }, dialogues, encounters, flags);
            Assert.That(second.Changed, Is.False, second.Summary());
        }

        [Test]
        public void Apply_UpdatesExistingDialogue_KeepsCommentAndGraphLayout()
        {
            DialogueDefinitionData old = new DialogueDefinitionData();
            JsonUtility.FromJsonOverwrite(
                "{\"id\":\"test_guest_gate\",\"title\":\"Старое\",\"category\":2,\"status\":2,\"developerComment\":\"Комментарий из Unity\",\"startNodeId\":\"start\"," +
                "\"tags\":[\"старый\"],\"nodes\":[{\"id\":\"start\",\"speakerId\":\"narrator\",\"text\":\"Старый текст\",\"textBlocks\":[],\"choices\":[]," +
                "\"editorPosition\":{\"x\":320,\"y\":140},\"hasEditorPosition\":true}],\"schemaVersion\":1}", old);
            Dialogues().Add(old);

            WorkshopProject project = Project();
            List<WorkshopSceneReport> reports = QuestWorkshopImporter.Analyze(project, dialogues, encounters);
            Assert.That(reports[0].State, Is.EqualTo(WorkshopSceneState.Changed));
            Assert.That(reports[0].Differences.Any(d => d.StartsWith("Название")), Is.True, string.Join("\n", reports[0].Differences));

            WorkshopImportResult result = QuestWorkshopImporter.Apply(project, new[] { Scene(project) }, dialogues, encounters, flags);
            Assert.That(result.UpdatedDialogues, Is.EquivalentTo(new[] { "test_guest_gate" }), result.Summary());
            DialogueDefinitionData updated = dialogues.FindDialogue("test_guest_gate");
            Assert.That(updated, Is.SameAs(old), "Запись обновляется на месте, а не заменяется новой.");
            Assert.That(updated.Title, Is.EqualTo("Гость у ворот"));
            Assert.That(updated.DeveloperComment, Is.EqualTo("Комментарий из Unity"), "Комментарий разработчика мастерская не трогает.");
            Assert.That(updated.Category, Is.EqualTo(DialogueCategory.SideQuest), "Пустая категория в сцене оставляет категорию Unity.");
            Assert.That(updated.Status, Is.EqualTo(DialogueProductionStatus.Approved));
            Assert.That(updated.Tags, Is.EquivalentTo(new[] { "тест" }), "Теги сцены заменяют теги Unity.");
            Assert.That(updated.Nodes[0].HasEditorPosition, Is.True);
            Assert.That(updated.Nodes[0].EditorPosition, Is.EqualTo(new Vector2(320, 140)), "Позиция узла в графе сохраняется.");
            Assert.That(updated.Nodes[0].TextBlocks.Count, Is.EqualTo(3));
        }

        [Test]
        public void Analyze_ReportsCrossSceneTargetAndMissingPool()
        {
            string json = WorkspaceJson
                .Replace("\"successNodeId\": \"ud:test_guest_gate:ok\"", "\"successNodeId\": \"ud:other_dialogue:somewhere\"")
                .Replace("\"poolId\": \"ROAD_POOL_01\"", "\"poolId\": \"NO_SUCH_POOL\"");
            WorkshopProject project = WorkshopProject.Parse(json);
            WorkshopSceneReport report = QuestWorkshopImporter.Analyze(project, dialogues, encounters)[0];
            Assert.That(report.State, Is.EqualTo(WorkshopSceneState.Errors));
            Assert.That(report.Issues.Any(i => i.Message.Contains("другой сцены")), Is.True);
            Assert.That(report.Issues.Any(i => i.Message.Contains("NO_SUCH_POOL")), Is.True);

            WorkshopImportResult result = QuestWorkshopImporter.Apply(project, new[] { Scene(project) }, dialogues, encounters, flags);
            Assert.That(result.Changed, Is.False);
            Assert.That(result.Skipped.Count, Is.EqualTo(1));
        }

        [Test]
        public void Analyze_ShowsDialogueDatabaseValidationBeforeTransfer()
        {
            string json = WorkspaceJson
                .Replace(@"""text"": ""Расспросить."", ""kind"": ""ActiveDecisive""", @"""text"": ""Расспросить."", ""kind"": ""ActiveReturnable""")
                .Replace(@"""id"": ""unity_check_test_guest_ask"", ""kind"": ""ActiveDecisive""", @"""id"": ""unity_check_test_guest_ask"", ""kind"": ""ActiveReturnable""");
            WorkshopSceneReport report = QuestWorkshopImporter.Analyze(WorkshopProject.Parse(json), dialogues, encounters)[0];
            Assert.That(report.Issues.Any(i => i.Message.StartsWith("Проверка базы диалогов") && i.Message.Contains("UnlockCheck")), Is.True,
                string.Join("\n", report.Issues));
            Assert.That(report.Transferable, Is.True, "Замечание базы — предупреждение, перенос не блокирует.");
        }

        [Test]
        public void Analyze_SceneWithoutUnityId_NeedsId()
        {
            WorkshopProject project = WorkshopProject.Parse(WorkspaceJson.Replace("\"unityId\": \"test_guest_gate\"", "\"unityId\": \"\""));
            WorkshopSceneReport report = QuestWorkshopImporter.Analyze(project, dialogues, encounters)[0];
            Assert.That(report.State, Is.EqualTo(WorkshopSceneState.NoUnityId));
            Assert.That(report.Transferable, Is.False);
        }

        // Квест из шаблона мастерской: сцены связаны переходом «после разговора» и
        // ответом в реплику другой сцены — в Unity он уходит одним диалогом.
        private const string LinkedQuestJson = @"{ ""workspace"": { ""name"": ""Тест"", ""people"": [], ""facts"": [], ""quests"": [ {
  ""id"": ""quest_linked"", ""title"": ""Просьба"", ""summary"": ""Сосед просит помочь."", ""unityId"": ""linked_request"", ""unityMode"": """",
  ""startSceneId"": ""s1"", ""line"": ""Тест"", ""mode"": ""Свободная игра"",
  ""scenes"": [
    { ""id"": ""s1"", ""title"": ""Просьба"", ""startNodeId"": ""n1"", ""nextSceneId"": ""s2"", ""conditions"": { ""mode"": ""All"", ""items"": [] },
      ""nodes"": [ { ""id"": ""n1"", ""speakerId"": ""narrator"", ""blocks"": [ { ""id"": ""b1"", ""kind"": ""MainLine"", ""text"": ""Сосед у ворот."", ""conditions"": { ""mode"": ""All"", ""items"": [] }, ""effects"": [] } ],
        ""choices"": [ { ""id"": ""c1"", ""text"": ""Выслушать."", ""kind"": ""Exit"", ""conditions"": { ""mode"": ""All"", ""items"": [] }, ""effects"": [], ""failureEffects"": [] },
                       { ""id"": ""c2"", ""text"": ""Сразу к делу."", ""kind"": ""Normal"", ""nextNodeId"": ""n3"", ""conditions"": { ""mode"": ""All"", ""items"": [] }, ""effects"": [], ""failureEffects"": [] } ] } ] },
    { ""id"": ""s2"", ""title"": ""Препятствие"", ""startNodeId"": ""n2"", ""nextSceneId"": ""s3"", ""conditions"": { ""mode"": ""All"", ""items"": [] },
      ""nodes"": [ { ""id"": ""n2"", ""speakerId"": ""narrator"", ""blocks"": [ { ""id"": ""b2"", ""kind"": ""MainLine"", ""text"": ""Мост размыт."", ""conditions"": { ""mode"": ""All"", ""items"": [] }, ""effects"": [] } ], ""choices"": [] } ] },
    { ""id"": ""s3"", ""title"": ""Решение"", ""startNodeId"": ""n3"", ""nextSceneId"": """", ""conditions"": { ""mode"": ""All"", ""items"": [] },
      ""nodes"": [ { ""id"": ""n3"", ""speakerId"": ""narrator"", ""blocks"": [ { ""id"": ""b3"", ""kind"": ""MainLine"", ""text"": ""Решай."", ""conditions"": { ""mode"": ""All"", ""items"": [] }, ""effects"": [] } ],
        ""choices"": [ { ""id"": ""c3"", ""text"": ""Помочь."", ""kind"": ""Exit"", ""conditions"": { ""mode"": ""All"", ""items"": [] }, ""effects"": [], ""failureEffects"": [] } ] } ] },
    { ""id"": ""s4"", ""title"": ""Последствие"", ""startNodeId"": """", ""conditions"": { ""mode"": ""All"", ""items"": [] }, ""nodes"": [] }
  ] } ] } }";

        [Test]
        public void LinkedQuest_TransfersAsOneDialogue()
        {
            WorkshopProject project = WorkshopProject.Parse(LinkedQuestJson);
            WorkshopQuest quest = project.Quests[0];
            Assert.That(quest.TransfersAsSingleDialogue, Is.True);
            List<WorkshopScene> units = quest.TransferUnits();
            Assert.That(units.Count, Is.EqualTo(1));
            Assert.That(units[0].IsMerged, Is.True);
            Assert.That(units[0].UnityId, Is.EqualTo("linked_request"));

            WorkshopSceneReport report = QuestWorkshopImporter.Analyze(project, dialogues, encounters).Single();
            Assert.That(report.State, Is.EqualTo(WorkshopSceneState.New), string.Join("\n", report.Issues));

            QuestWorkshopImporter.Apply(project, units, dialogues, encounters, flags);
            DialogueDefinitionData dialogue = dialogues.FindDialogue("linked_request");
            Assert.That(dialogue.Title, Is.EqualTo("Просьба"));
            Assert.That(dialogue.StartNodeId, Is.EqualTo("n1"));
            Assert.That(dialogue.Nodes.Select(n => n.Id), Is.EqualTo(new[] { "n1", "n2", "n3" }));
            DialogueChoiceData listen = dialogue.Nodes[0].Choices[0];
            Assert.That(listen.Kind, Is.EqualTo(DialogueChoiceKind.Normal), "«Закончить разговор» в сцене с продолжением ведёт дальше.");
            Assert.That(listen.NextNodeId, Is.EqualTo("n2"));
            DialogueChoiceData bridge = dialogue.Nodes[1].Choices.Single();
            Assert.That(bridge.Kind, Is.EqualTo(DialogueChoiceKind.Continue));
            Assert.That(bridge.NextNodeId, Is.EqualTo("n3"));
            Assert.That(dialogue.Nodes[2].Choices.Single().IsExit, Is.True, "Последняя сцена без продолжения заканчивает разговор.");

            List<string> issues = new List<string>();
            dialogues.CollectValidationIssuesForDialogue("linked_request", issues);
            Assert.That(issues, Is.Empty, string.Join("\n", issues));
            Assert.That(QuestWorkshopImporter.Analyze(project, dialogues, encounters).Single().State, Is.EqualTo(WorkshopSceneState.Same));
        }

        [Test]
        public void Sync_DetectsWhereTheSceneWasEdited()
        {
            QuestWorkshopSyncState sync = new QuestWorkshopSyncState();
            WorkshopProject project = Project();
            QuestWorkshopImporter.Apply(project, new[] { Scene(project) }, dialogues, encounters, flags, sync);
            Assert.That(QuestWorkshopImporter.Analyze(project, dialogues, encounters, sync)[0].State, Is.EqualTo(WorkshopSceneState.Same));

            // Правка в Unity: мастерская её не затирает молча.
            DialogueDefinitionData dialogue = dialogues.FindDialogue("test_guest_gate");
            SetPrivate(dialogue, "title", "Гость у ворот (правка в Unity)");
            WorkshopSceneReport unityEdit = QuestWorkshopImporter.Analyze(project, dialogues, encounters, sync)[0];
            Assert.That(unityEdit.State, Is.EqualTo(WorkshopSceneState.UnityNewer));
            Assert.That(unityEdit.Transferable, Is.False);
            Assert.That(unityEdit.AutoTransferable, Is.False);

            // Правка с обеих сторон — конфликт: перенос только явным выбором.
            WorkshopProject edited = WorkshopProject.Parse(WorkspaceJson.Replace(@"""title"": ""Гость у ворот""", @"""title"": ""Гость у ворот ночью"""));
            WorkshopSceneReport conflict = QuestWorkshopImporter.Analyze(edited, dialogues, encounters, sync)[0];
            Assert.That(conflict.State, Is.EqualTo(WorkshopSceneState.Conflict));
            Assert.That(conflict.Transferable, Is.True);
            Assert.That(conflict.AutoTransferable, Is.False);

            // Unity вернули как было — правка только в мастерской, её можно переносить сразу.
            SetPrivate(dialogue, "title", "Гость у ворот");
            WorkshopSceneReport workshopEdit = QuestWorkshopImporter.Analyze(edited, dialogues, encounters, sync)[0];
            Assert.That(workshopEdit.State, Is.EqualTo(WorkshopSceneState.Changed));
            Assert.That(workshopEdit.Direction, Is.EqualTo(WorkshopSyncDirection.WorkshopNewer));
            Assert.That(workshopEdit.AutoTransferable, Is.True);
        }

        [Test]
        public void Speakers_RenameFlowsFromWorkshop()
        {
            QuestWorkshopSyncState sync = new QuestWorkshopSyncState();
            WorkshopProject project = Project();
            QuestWorkshopImporter.Apply(project, new[] { Scene(project) }, dialogues, encounters, flags, sync);
            Assert.That(QuestWorkshopImporter.AnalyzeSpeakers(project, dialogues, sync), Is.Empty, "После переноса говорящие совпадают.");

            WorkshopProject renamed = WorkshopProject.Parse(WorkspaceJson.Replace(@"""name"": ""Гость""", @"""name"": ""Путник с мельницы"""));
            List<WorkshopSpeakerReport> reports = QuestWorkshopImporter.AnalyzeSpeakers(renamed, dialogues, sync);
            Assert.That(reports.Single().SpeakerId, Is.EqualTo("test_guest"));
            Assert.That(reports.Single().AutoTransferable, Is.True);
            WorkshopImportResult result = new WorkshopImportResult();
            QuestWorkshopImporter.ApplySpeakers(renamed, reports, dialogues, result, sync);
            Assert.That(dialogues.FindSpeaker("test_guest").DisplayName, Is.EqualTo("Путник с мельницы"));
            Assert.That(QuestWorkshopImporter.AnalyzeSpeakers(renamed, dialogues, sync), Is.Empty);
        }

        private static void SetPrivate(object target, string field, object value)
        {
            target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
        }

        [Test]
        public void Snapshot_ContainsDialoguesSpeakersAndPools()
        {
            WorkshopProject project = Project();
            QuestWorkshopImporter.Apply(project, new[] { Scene(project) }, dialogues, encounters, flags);
            JsonNode snapshot = new JsonNode(QuestWorkshopJson.Parse(QuestWorkshopImporter.BuildSnapshotJson(dialogues, encounters, flags, "abc1234")));
            Assert.That(snapshot["format"].Str(), Is.EqualTo("ks-unity-snapshot"));
            Assert.That(snapshot["commit"].Str(), Is.EqualTo("abc1234"));
            Assert.That(snapshot["dialogues"].Items().Single()["id"].Str(), Is.EqualTo("test_guest_gate"));
            Assert.That(snapshot["dialogues"].Items().Single()["nodes"].Items().First()["textBlocks"].Items().Count(), Is.EqualTo(3));
            Assert.That(snapshot["speakers"].Items().Select(s => s["id"].Str()), Is.EquivalentTo(new[] { "narrator", "test_guest" }));
            Assert.That(snapshot["encounters"].Items().Single()["EncounterId"].Str(), Is.EqualTo("TEST_GUEST_01"));
            Assert.That(snapshot["pools"].Items().Single()["PoolId"].Str(), Is.EqualTo("ROAD_POOL_01"));
        }

        private List<DialogueDefinitionData> Dialogues()
        {
            return PrivateList<DialogueDatabaseAsset, DialogueDefinitionData>(dialogues, "dialogues");
        }

        private List<DialogueSpeakerData> Speakers()
        {
            return PrivateList<DialogueDatabaseAsset, DialogueSpeakerData>(dialogues, "speakers");
        }

        private static List<T> PrivateList<TOwner, T>(TOwner owner, string field)
        {
            FieldInfo info = typeof(TOwner).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic);
            return (List<T>)info.GetValue(owner);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using KingdomSurvival.BattlefieldDatabase;
using KingdomSurvival.DialogueDatabase;
using KingdomSurvival.UnitDatabase;
using KingdomSurvival.WorldMapVisual;
using UnityEditor;
using UnityEngine;

namespace KingdomSurvival.QuestWorkshopBridge.Editor
{
    // Место мастерской как место игры (place.location в workspace.json):
    // место глобальной карты (База карты), правило генератора карты свободной
    // игры (слот place:<id>) и исследуемое место (База локаций — объекты,
    // противники, столкновения). Мастерская владеет содержанием; расстановку,
    // разметку местности, рисунок и свет правят в Unity — перенос их не
    // трогает. Новое место получает заготовку расстановки на рисунке-заглушке.
    public sealed class WorkshopPlace
    {
        public string Id = string.Empty;
        public string Name = string.Empty;
        public string Description = string.Empty;
        public WorkshopPlaceLocation Location;

        public static WorkshopPlace Read(JsonNode node)
        {
            WorkshopPlace place = new WorkshopPlace
            {
                Id = node["id"].Str(),
                Name = node["name"].Str(),
                Description = node["description"].Str()
            };
            if (node.Has("location"))
                place.Location = WorkshopPlaceLocation.Read(node["location"]);
            return place;
        }
    }

    public sealed class WorkshopPlaceLocation
    {
        public bool Enabled;
        public string WorldId = string.Empty;
        public string ModeId = string.Empty;

        public bool MapEnabled;
        public string MapName = string.Empty;
        public string MapDescription = string.Empty;
        public string Threat = string.Empty;
        public bool Visible = true;
        public bool Discovered;

        public bool GeneratorEnabled;
        public List<string> Terrains = new List<string>();
        public bool NearWater;
        public bool NearVillage;
        public float MinDistanceHexes = 9f;
        public string SlotTags = string.Empty;
        public bool DrawMark = true;

        public bool LocalEnabled;
        public string LocalId = string.Empty;
        public string DisplayName = string.Empty;
        public string BattlefieldId = string.Empty;
        public double HoursPerInteraction;
        public readonly List<WorkshopLocalObject> Objects = new List<WorkshopLocalObject>();
        public readonly List<WorkshopLocalEnemy> Enemies = new List<WorkshopLocalEnemy>();
        public readonly List<WorkshopLocalEncounter> Encounters = new List<WorkshopLocalEncounter>();

        public static WorkshopPlaceLocation Read(JsonNode node)
        {
            JsonNode map = node["map"];
            JsonNode generator = node["generator"];
            JsonNode local = node["local"];
            WorkshopPlaceLocation location = new WorkshopPlaceLocation
            {
                Enabled = node["enabled"].Bool(true),
                WorldId = node["worldId"].Str().Trim(),
                ModeId = node["modeId"].Str().Trim(),
                MapEnabled = map["enabled"].Bool(true),
                MapName = map["name"].Str(),
                MapDescription = map["description"].Str(),
                Threat = map["threat"].Str(),
                Visible = map["visible"].Bool(true),
                Discovered = map["discovered"].Bool(),
                GeneratorEnabled = generator["enabled"].Bool(true),
                Terrains = generator["terrains"].Strings(),
                NearWater = generator["nearWater"].Bool(),
                NearVillage = generator["nearVillage"].Bool(),
                MinDistanceHexes = (float)generator["minDistanceHexes"].Num(9),
                SlotTags = generator["slotTags"].Str().Trim(),
                DrawMark = generator["drawMark"].Bool(true),
                LocalEnabled = local["enabled"].Bool(local.IsObject),
                LocalId = local["id"].Str().Trim(),
                DisplayName = local["displayName"].Str(),
                BattlefieldId = local["battlefieldId"].Str().Trim(),
                HoursPerInteraction = local["hoursPerInteraction"].Num()
            };
            foreach (JsonNode item in local["objects"].Items())
                location.Objects.Add(new WorkshopLocalObject
                {
                    Id = item["id"].Str().Trim(),
                    Label = item["label"].Str(),
                    ActionLabel = item["actionLabel"].Str(),
                    Kind = item["kind"].Str("Dialogue"),
                    Dialogue = item["dialogue"].Str().Trim(),
                    Text = item["text"].Str(),
                    OnceOnly = item["onceOnly"].Bool(true),
                    RequiresFlag = item["requiresFlag"].Str().Trim(),
                    HiddenWhenFlag = item["hiddenWhenFlag"].Str().Trim(),
                    InteractRadius = (float)item["interactRadius"].Num()
                });
            foreach (JsonNode item in local["enemies"].Items())
                location.Enemies.Add(new WorkshopLocalEnemy
                {
                    InstanceId = item["instanceId"].Str().Trim(),
                    UnitTypeId = item["unitTypeId"].Str().Trim(),
                    Level = Math.Max(1, item["level"].Int(1)),
                    EncounterId = item["encounterId"].Str().Trim()
                });
            foreach (JsonNode item in local["encounters"].Items())
                location.Encounters.Add(new WorkshopLocalEncounter
                {
                    Id = item["id"].Str().Trim(),
                    BattleIdPrefix = item["battleIdPrefix"].Str().Trim(),
                    Intro = item["intro"].Str().Trim(),
                    AllowRetreat = item["allowRetreat"].Bool(true),
                    PreparedCompanionId = item["preparedCompanionId"].Str().Trim(),
                    ResolvedFlag = item["resolvedFlag"].Str().Trim()
                });
            return location;
        }
    }

    public sealed class WorkshopLocalObject
    {
        public string Id = string.Empty;
        public string Label = string.Empty;
        public string ActionLabel = string.Empty;
        public string Kind = "Dialogue";
        // ID сцены мастерской или ID диалога Unity.
        public string Dialogue = string.Empty;
        public string Text = string.Empty;
        public bool OnceOnly = true;
        public string RequiresFlag = string.Empty;
        public string HiddenWhenFlag = string.Empty;
        public float InteractRadius;
    }

    public sealed class WorkshopLocalEnemy
    {
        public string InstanceId = string.Empty;
        public string UnitTypeId = string.Empty;
        public int Level = 1;
        public string EncounterId = string.Empty;
    }

    public sealed class WorkshopLocalEncounter
    {
        public string Id = string.Empty;
        public string BattleIdPrefix = string.Empty;
        // ID сцены мастерской или ID диалога Unity — вступление перед боем.
        public string Intro = string.Empty;
        public bool AllowRetreat = true;
        public string PreparedCompanionId = string.Empty;
        public string ResolvedFlag = string.Empty;
    }

    // Базы Unity, в которые переносится место. Окно передаёт ассеты проекта,
    // тесты — временные экземпляры.
    public sealed class WorkshopPlaceAssets
    {
        public const string MapDatabasePath = "Assets/_Project/WorldMapVisual/Resources/WorldMapVisual/KingdomSurvivalWorldMapDatabase.asset";
        public const string BattlefieldsPath = "Assets/_Project/BattlefieldDatabase/Resources/BattlefieldDatabase/KingdomSurvivalBattlefields.asset";

        public LocalLocationDatabaseAsset Locals;
        public WorldMapDatabaseAsset Map;
        public WorldMapGeneratorAsset Generator;
        public BattlefieldDatabaseAsset Battlefields;
        public DialogueDatabaseAsset Dialogues;
        public UnitDatabaseAsset Units;

        public static WorkshopPlaceAssets LoadProject(DialogueDatabaseAsset dialogues = null)
        {
            WorldMapDatabaseAsset map = AssetDatabase.LoadAssetAtPath<WorldMapDatabaseAsset>(MapDatabasePath);
            return new WorkshopPlaceAssets
            {
                Locals = AssetDatabase.LoadAssetAtPath<LocalLocationDatabaseAsset>(LocalLocationDatabaseAsset.AssetPath),
                Map = map,
                Generator = map != null && map.Generator != null
                    ? map.Generator
                    : AssetDatabase.LoadAssetAtPath<WorldMapGeneratorAsset>(WorldMapGeneratorAsset.AssetPath),
                Battlefields = AssetDatabase.LoadAssetAtPath<BattlefieldDatabaseAsset>(BattlefieldsPath),
                Dialogues = dialogues != null ? dialogues : QuestWorkshopPaths.LoadDialogues(),
                Units = Resources.Load<UnitDatabaseAsset>(UnitDatabaseAsset.ResourcesPath)
            };
        }

        public IEnumerable<UnityEngine.Object> Targets()
        {
            if (Locals != null)
                yield return Locals;
            if (Map != null)
                yield return Map;
            if (Generator != null)
                yield return Generator;
        }
    }

    public sealed class WorkshopPlaceReport
    {
        public WorkshopPlace Place;
        public WorkshopSceneState State = WorkshopSceneState.Plan;
        public string MapState = string.Empty;
        public string GeneratorState = string.Empty;
        public string LocalState = string.Empty;
        public readonly List<WorkshopIssue> Issues = new List<WorkshopIssue>();
        public readonly List<string> Differences = new List<string>();
        public bool HasErrors => Issues.Exists(i => i.Severity == WorkshopIssueSeverity.Error);
        public bool Transferable => !HasErrors && (State == WorkshopSceneState.New || State == WorkshopSceneState.Changed);
    }

    public static class QuestWorkshopPlaces
    {
        public const string StateNew = "новое";
        public const string StateSame = "совпадает";
        public const string StateChanged = "изменено";

        private static readonly Regex IdPattern = new Regex("^[A-Za-z0-9_.\\-]+$", RegexOptions.Compiled);

        // Размер рисунка-заглушки нового места — как у пилота шахты.
        private const float PlaceholderWidth = 1920f;
        private const float PlaceholderHeight = 1080f;
        private const int PlaceholderHexesAcross = 60;

        public static IEnumerable<WorkshopPlace> WithLocation(WorkshopProject project)
        {
            return project.Places.Where(p => p.Location != null && p.Location.Enabled);
        }

        // ── Анализ ──────────────────────────────────────────────────────────

        public static List<WorkshopPlaceReport> Analyze(WorkshopProject project, WorkshopPlaceAssets assets)
        {
            List<WorkshopPlaceReport> reports = new List<WorkshopPlaceReport>();
            Dictionary<string, WorkshopPlace> worldOwners = new Dictionary<string, WorkshopPlace>(StringComparer.Ordinal);
            Dictionary<string, WorkshopPlace> localOwners = new Dictionary<string, WorkshopPlace>(StringComparer.Ordinal);
            foreach (WorkshopPlace place in WithLocation(project))
            {
                WorkshopPlaceReport report = new WorkshopPlaceReport { Place = place };
                reports.Add(report);
                WorkshopPlaceLocation location = place.Location;
                Validate(project, place, assets, report.Issues);
                if (location.WorldId.Length > 0 && !worldOwners.TryAdd(location.WorldId, place))
                    Error(report.Issues, "ID места карты «" + location.WorldId + "» уже у места «" + worldOwners[location.WorldId].Name + "».");
                if (location.LocalEnabled && location.LocalId.Length > 0 && !localOwners.TryAdd(location.LocalId, place))
                    Error(report.Issues, "ID исследуемого места «" + location.LocalId + "» уже у места «" + localOwners[location.LocalId].Name + "».");
                if (report.HasErrors)
                {
                    report.State = WorkshopSceneState.Errors;
                    continue;
                }

                bool anyNew = false, anyChanged = false;
                if (location.MapEnabled)
                {
                    WorldMapLocationDefinition current = assets.Map != null ? assets.Map.FindLocation(location.WorldId) : null;
                    report.MapState = current == null ? StateNew : SameMap(current, place) ? StateSame : StateChanged;
                    Track(report.MapState, ref anyNew, ref anyChanged);
                    if (report.MapState != StateSame)
                        report.Differences.Add("Место карты «" + location.WorldId + "»: " + report.MapState + DescribeMap(current, place) + ".");
                }

                if (location.GeneratorEnabled)
                {
                    WorldMapLandmarkRule current = FindRule(assets.Generator, location.WorldId);
                    WorldMapLandmarkRule next = BuildRule(place, current);
                    report.GeneratorState = current == null ? StateNew : JsonUtility.ToJson(current) == JsonUtility.ToJson(next) ? StateSame : StateChanged;
                    Track(report.GeneratorState, ref anyNew, ref anyChanged);
                    if (report.GeneratorState != StateSame)
                        report.Differences.Add("Правило генератора карты «" + next.Id + "» (слот place:" + location.WorldId + "): " + report.GeneratorState + ".");
                }

                if (location.LocalEnabled)
                {
                    LocalLocationDefinition current = assets.Locals?.locations?.Find(l => l != null && l.Id == location.LocalId);
                    List<string> notes = new List<string>();
                    LocalLocationDefinition next = BuildLocal(project, place, current, notes);
                    report.LocalState = current == null ? StateNew : JsonUtility.ToJson(current) == JsonUtility.ToJson(next) ? StateSame : StateChanged;
                    Track(report.LocalState, ref anyNew, ref anyChanged);
                    if (report.LocalState != StateSame)
                    {
                        report.Differences.Add("Исследуемое место «" + location.LocalId + "»: " + report.LocalState + ".");
                        report.Differences.AddRange(notes);
                    }
                    if (current != null)
                        foreach (string extra in OnlyInUnity(current, location))
                            report.Issues.Add(new WorkshopIssue { Severity = WorkshopIssueSeverity.Info, Message = extra });
                    foreach (string issue in ValidateBuilt(project, next, assets))
                        report.Issues.Add(new WorkshopIssue { Severity = WorkshopIssueSeverity.Warning, Message = "Проверка места: " + issue });
                }

                bool allNew = new[] { report.MapState, report.GeneratorState, report.LocalState }.Where(s => s.Length > 0).All(s => s == StateNew);
                report.State = anyNew && allNew ? WorkshopSceneState.New
                    : anyNew || anyChanged ? WorkshopSceneState.Changed : WorkshopSceneState.Same;
            }
            return reports;
        }

        private static void Track(string state, ref bool anyNew, ref bool anyChanged)
        {
            if (state == StateNew)
                anyNew = true;
            else if (state == StateChanged)
                anyChanged = true;
        }

        private static void Error(List<WorkshopIssue> issues, string message)
        {
            issues.Add(new WorkshopIssue { Severity = WorkshopIssueSeverity.Error, Message = message });
        }

        private static void Warn(List<WorkshopIssue> issues, string message)
        {
            issues.Add(new WorkshopIssue { Severity = WorkshopIssueSeverity.Warning, Message = message });
        }

        public static void Validate(WorkshopProject project, WorkshopPlace place, WorkshopPlaceAssets assets, List<WorkshopIssue> issues)
        {
            WorkshopPlaceLocation location = place.Location;
            if (location.WorldId.Length == 0)
                Error(issues, "Не задан ID места карты (worldId).");
            else if (!IdPattern.IsMatch(location.WorldId))
                Error(issues, "ID места карты «" + location.WorldId + "»: только латиница, цифры, точка, дефис и подчёркивание.");
            if (location.ModeId.Length == 0)
                Warn(issues, "Режим не задан: место появится во всех режимах, в том числе в авторском мире кризиса.");
            if (!location.MapEnabled && !location.GeneratorEnabled && !location.LocalEnabled)
                Warn(issues, "Ни одна часть места не включена — переносить нечего.");

            if (location.MapEnabled && assets.Map == null)
                Error(issues, "База карты не найдена: " + WorkshopPlaceAssets.MapDatabasePath);
            if (location.GeneratorEnabled && assets.Generator == null)
                Error(issues, "Генератор карты не найден: " + WorldMapGeneratorAsset.AssetPath);
            foreach (string terrain in location.Terrains)
                if (!Enum.TryParse(terrain, false, out WorldMapGameplayTerrainType _))
                    Error(issues, "Неизвестная местность генератора «" + terrain + "» (OpenGround, Field, Forest, Swamp, Hills, Mountains…).");
            if (location.GeneratorEnabled && location.ModeId.Length > 0 && location.ModeId != CampaignStartOptions.FreePlayId)
                Warn(issues, "Генератор строит карту только для свободной игры (" + CampaignStartOptions.FreePlayId + "), а режим места — " + location.ModeId + ".");

            if (!location.LocalEnabled)
                return;
            if (assets.Locals == null)
                Error(issues, "База локаций не найдена: " + LocalLocationDatabaseAsset.AssetPath);
            else if (assets.Locals.pointFormatVersion < LocalLocationDatabaseAsset.CurrentPointFormatVersion && assets.Locals.locations.Count > 0)
                Error(issues, "База локаций в прежнем формате клеток: откройте её в редакторе, чтобы она перевелась в точки.");
            if (location.LocalId.Length == 0)
                Error(issues, "Не задан ID исследуемого места (local.id).");
            else if (!IdPattern.IsMatch(location.LocalId))
                Error(issues, "ID исследуемого места «" + location.LocalId + "»: недопустимые символы.");
            if (location.BattlefieldId.Length > 0 && assets.Battlefields != null && assets.Battlefields.FindById(location.BattlefieldId) == null)
                Error(issues, "Нет поля боя «" + location.BattlefieldId + "» в Базе полей боя.");
            if (location.BattlefieldId.Length == 0 && location.Encounters.Count > 0 &&
                assets.Locals?.locations?.Find(l => l != null && l.Id == location.LocalId) == null)
                Error(issues, "У нового места со столкновениями не задано поле боя (local.battlefieldId).");

            HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (WorkshopLocalObject item in location.Objects)
            {
                if (item.Id.Length == 0 || !IdPattern.IsMatch(item.Id))
                    Error(issues, "Объект «" + item.Label + "»: пустой или недопустимый ID.");
                else if (!ids.Add(item.Id))
                    Error(issues, "Повторный ID «" + item.Id + "».");
                if (item.Kind != "Dialogue" && item.Kind != "Inspect")
                    Error(issues, "Объект «" + item.Id + "»: вид «" + item.Kind + "» — нужен Dialogue или Inspect.");
                if (item.Kind == "Dialogue")
                    CheckDialogue(project, assets, issues, "Объект «" + item.Id + "»", item.Dialogue, true);
                if (item.Kind == "Inspect" && item.Text.Trim().Length == 0)
                    Warn(issues, "Объект «" + item.Id + "»: осмотр без текста.");
            }
            HashSet<string> encounterIds = new HashSet<string>(location.Encounters.Select(e => e.Id), StringComparer.Ordinal);
            foreach (WorkshopLocalEnemy enemy in location.Enemies)
            {
                if (enemy.InstanceId.Length == 0 || !IdPattern.IsMatch(enemy.InstanceId))
                    Error(issues, "Противник без ID или с недопустимым ID.");
                else if (!ids.Add(enemy.InstanceId))
                    Error(issues, "Повторный ID «" + enemy.InstanceId + "».");
                if (enemy.UnitTypeId.Length == 0)
                    Error(issues, "Противник «" + enemy.InstanceId + "»: не задан тип существа.");
                else if (assets.Units != null && assets.Units.FindById(enemy.UnitTypeId) == null)
                    Error(issues, "Противник «" + enemy.InstanceId + "»: типа «" + enemy.UnitTypeId + "» нет в Базе существ.");
                if (!encounterIds.Contains(enemy.EncounterId))
                    Error(issues, "Противник «" + enemy.InstanceId + "»: нет столкновения «" + enemy.EncounterId + "».");
            }
            foreach (WorkshopLocalEncounter encounter in location.Encounters)
            {
                if (encounter.Id.Length == 0 || !IdPattern.IsMatch(encounter.Id))
                    Error(issues, "Столкновение без ID или с недопустимым ID.");
                else if (!ids.Add(encounter.Id))
                    Error(issues, "Повторный ID «" + encounter.Id + "».");
                if (!location.Enemies.Exists(e => e.EncounterId == encounter.Id))
                    Warn(issues, "Столкновение «" + encounter.Id + "» без противников никогда не начнётся.");
                if (encounter.ResolvedFlag.Length == 0)
                    Warn(issues, "Столкновение «" + encounter.Id + "»: нет флага исчерпания — победа не откроет объекты места по флагу.");
                if (encounter.Intro.Length > 0)
                    CheckDialogue(project, assets, issues, "Вступление столкновения «" + encounter.Id + "»", encounter.Intro, false);
            }
        }

        private static void CheckDialogue(WorkshopProject project, WorkshopPlaceAssets assets, List<WorkshopIssue> issues, string owner, string reference, bool required)
        {
            if (reference.Length == 0)
            {
                if (required)
                    Error(issues, owner + ": не указан диалог (сцена мастерской или ID диалога Unity).");
                return;
            }
            WorkshopScene scene = FindScene(project, reference);
            string dialogueId = ResolveDialogue(project, reference);
            if (scene != null && dialogueId.Length == 0)
            {
                Error(issues, owner + ": у сцены «" + scene.Title + "» нет ID диалога Unity.");
                return;
            }
            if (scene != null && scene.Quest.TransfersAsSingleDialogue)
                Warn(issues, owner + ": квест «" + scene.Quest.Title + "» переносится одним диалогом — объект откроет его начало, а не сцену «" + scene.Title + "».");
            bool inDatabase = assets.Dialogues != null && assets.Dialogues.FindDialogue(dialogueId) != null;
            if (!inDatabase && !WorkshopDialogueIds(project).Contains(dialogueId))
                Error(issues, owner + ": диалога «" + dialogueId + "» нет ни в Базе диалогов, ни среди сцен мастерской.");
            else if (!inDatabase)
                issues.Add(new WorkshopIssue { Severity = WorkshopIssueSeverity.Info, Message = owner + ": диалог «" + dialogueId + "» появится после переноса сцены." });
        }

        private static HashSet<string> WorkshopDialogueIds(WorkshopProject project)
        {
            HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (WorkshopQuest quest in project.Quests)
                foreach (WorkshopScene unit in quest.TransferUnits())
                    if (unit.UnityId.Length > 0 && unit.Nodes.Count > 0)
                        ids.Add(unit.UnityId);
            return ids;
        }

        public static WorkshopScene FindScene(WorkshopProject project, string sceneId)
        {
            foreach (WorkshopQuest quest in project.Quests)
                foreach (WorkshopScene scene in quest.Scenes)
                    if (scene.Id == sceneId)
                        return scene;
            return null;
        }

        // Сцена мастерской → ID её диалога Unity (у квеста одним диалогом —
        // ID квеста); иначе ссылка уже ID диалога Unity.
        public static string ResolveDialogue(WorkshopProject project, string reference)
        {
            if (string.IsNullOrEmpty(reference))
                return string.Empty;
            WorkshopScene scene = FindScene(project, reference);
            if (scene == null)
                return reference;
            return scene.Quest.TransfersAsSingleDialogue ? scene.Quest.UnityId : scene.UnityId;
        }

        private static List<string> ValidateBuilt(WorkshopProject project, LocalLocationDefinition built, WorkshopPlaceAssets assets)
        {
            if (assets.Battlefields == null)
                return new List<string>();
            HashSet<string> workshopDialogues = WorkshopDialogueIds(project);
            LocationVisualDefinition visual = assets.Locals != null ? assets.Locals.FindVisual(built.Id) : null;
            return LocalLocationValidator.Validate(built, assets.Battlefields,
                id => workshopDialogues.Contains(id) || (assets.Dialogues != null && assets.Dialogues.FindDialogue(id) != null),
                assets.Units != null ? id => assets.Units.FindById(id) != null : (Func<string, bool>)null,
                LocationVisualGeometry.BlockedAreas(visual, built));
        }

        private static IEnumerable<string> OnlyInUnity(LocalLocationDefinition current, WorkshopPlaceLocation location)
        {
            foreach (LocalObjectDefinition item in current.Objects)
                if (item != null && !location.Objects.Exists(o => o.Id == item.Id))
                    yield return "Объект «" + item.Id + "» есть только в Unity — перенос его не трогает.";
            foreach (LocalEnemyDefinition enemy in current.Enemies)
                if (enemy != null && !location.Enemies.Exists(e => e.InstanceId == enemy.InstanceId))
                    yield return "Противник «" + enemy.InstanceId + "» есть только в Unity — перенос его не трогает.";
            foreach (LocalEncounterDefinition encounter in current.Encounters)
                if (encounter != null && !location.Encounters.Exists(e => e.Id == encounter.Id))
                    yield return "Столкновение «" + encounter.Id + "» есть только в Unity — перенос его не трогает.";
        }

        // ── Место карты ─────────────────────────────────────────────────────

        private static string MapName(WorkshopPlace place) =>
            place.Location.MapName.Trim().Length > 0 ? place.Location.MapName.Trim() : place.Name;

        private static string MapDescription(WorkshopPlace place) =>
            place.Location.MapDescription.Trim().Length > 0 ? place.Location.MapDescription.Trim() : place.Description.Trim();

        private static string MapThreat(WorkshopPlace place) =>
            place.Location.Threat.Trim().Length > 0 ? place.Location.Threat.Trim() : "неизвестна";

        private static bool SameMap(WorldMapLocationDefinition current, WorkshopPlace place)
        {
            return current.DisplayName == MapName(place) && current.InteractionDescription == MapDescription(place) &&
                   current.Threat == MapThreat(place) && current.InitiallyVisibleOnMap == place.Location.Visible &&
                   current.InitiallyDiscovered == place.Location.Discovered && (current.ModeId ?? string.Empty) == place.Location.ModeId &&
                   current.Mode == WorldMapPlacementMode.Anchored;
        }

        private static string DescribeMap(WorldMapLocationDefinition current, WorkshopPlace place)
        {
            if (current == null)
                return " — «" + MapName(place) + "», режим " + (place.Location.ModeId.Length > 0 ? place.Location.ModeId : "любой");
            List<string> parts = new List<string>();
            if (current.DisplayName != MapName(place))
                parts.Add("название «" + current.DisplayName + "» → «" + MapName(place) + "»");
            if (current.InteractionDescription != MapDescription(place))
                parts.Add("описание");
            if (current.Threat != MapThreat(place))
                parts.Add("угроза");
            if (current.InitiallyVisibleOnMap != place.Location.Visible || current.InitiallyDiscovered != place.Location.Discovered)
                parts.Add("видимость");
            if ((current.ModeId ?? string.Empty) != place.Location.ModeId)
                parts.Add("режим «" + current.ModeId + "» → «" + place.Location.ModeId + "»");
            if (current.Mode != WorldMapPlacementMode.Anchored)
                parts.Add("размещение → по слоту");
            return parts.Count > 0 ? " (" + string.Join(", ", parts) + ")" : string.Empty;
        }

        private static bool ApplyMap(WorldMapDatabaseAsset map, WorkshopPlace place)
        {
            SerializedObject serialized = new SerializedObject(map);
            SerializedProperty list = serialized.FindProperty("locations");
            SerializedProperty entry = null;
            for (int i = 0; i < list.arraySize; i++)
            {
                SerializedProperty item = list.GetArrayElementAtIndex(i);
                if (item.FindPropertyRelative("id").stringValue == place.Location.WorldId)
                {
                    entry = item;
                    break;
                }
            }
            bool created = entry == null;
            if (created)
            {
                list.InsertArrayElementAtIndex(list.arraySize);
                entry = list.GetArrayElementAtIndex(list.arraySize - 1);
                entry.FindPropertyRelative("id").stringValue = place.Location.WorldId;
                entry.FindPropertyRelative("researchResultText").stringValue = string.Empty;
                entry.FindPropertyRelative("explorationHours").doubleValue = 0.0;
                entry.FindPropertyRelative("rewardArmyGold").intValue = 0;
                entry.FindPropertyRelative("rewardArmySupply").intValue = 0;
                entry.FindPropertyRelative("spawnSlotId").stringValue = string.Empty;
                entry.FindPropertyRelative("requiredSlotTags").ClearArray();
                entry.FindPropertyRelative("fixedXPercent").floatValue = 50f;
                entry.FindPropertyRelative("fixedYPercent").floatValue = 50f;
                entry.FindPropertyRelative("icon").objectReferenceValue = null;
                entry.FindPropertyRelative("iconTint").colorValue = Color.white;
                entry.FindPropertyRelative("iconScale").floatValue = 1f;
            }
            entry.FindPropertyRelative("displayName").stringValue = MapName(place);
            entry.FindPropertyRelative("interactionDescription").stringValue = MapDescription(place);
            entry.FindPropertyRelative("threat").stringValue = MapThreat(place);
            entry.FindPropertyRelative("initiallyVisibleOnMap").boolValue = place.Location.Visible;
            entry.FindPropertyRelative("initiallyDiscovered").boolValue = place.Location.Discovered;
            entry.FindPropertyRelative("modeId").stringValue = place.Location.ModeId;
            entry.FindPropertyRelative("mode").enumValueIndex = (int)WorldMapPlacementMode.Anchored;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return created;
        }

        // ── Правило генератора ──────────────────────────────────────────────

        private static WorldMapLandmarkRule FindRule(WorldMapGeneratorAsset generator, string worldId)
        {
            List<WorldMapLandmarkRule> rules = generator?.Settings?.Landmarks;
            return rules?.Find(r => r != null && r.LocationId == worldId);
        }

        public static WorldMapLandmarkRule BuildRule(WorkshopPlace place, WorldMapLandmarkRule current)
        {
            WorkshopPlaceLocation location = place.Location;
            WorldMapLandmarkRule rule = current != null ? current.Clone() : new WorldMapLandmarkRule { Id = location.WorldId, Enabled = true };
            rule.Name = MapName(place);
            rule.Count = 1;
            rule.LocationId = location.WorldId;
            rule.Terrains = location.Terrains
                .Select(t => Enum.TryParse(t, false, out WorldMapGameplayTerrainType type) ? type : (WorldMapGameplayTerrainType?)null)
                .Where(t => t.HasValue).Select(t => t.Value).ToList();
            rule.NearWater = location.NearWater;
            rule.NearVillage = location.NearVillage;
            rule.MinDistanceHexes = location.MinDistanceHexes > 0 ? location.MinDistanceHexes : 9f;
            rule.SlotTags = location.SlotTags;
            rule.DrawMark = location.DrawMark;
            return rule;
        }

        // ── Исследуемое место ───────────────────────────────────────────────

        // Содержание из мастерской поверх записи Unity: точки, разметка,
        // рисунок и прочие настройки Unity сохраняются; новое место и новые
        // объекты получают заготовку расстановки.
        public static LocalLocationDefinition BuildLocal(WorkshopProject project, WorkshopPlace place, LocalLocationDefinition existing, List<string> notes = null)
        {
            WorkshopPlaceLocation source = place.Location;
            LocalLocationDefinition location = existing != null
                ? JsonUtility.FromJson<LocalLocationDefinition>(JsonUtility.ToJson(existing))
                : CreatePlaceholder(source);
            location.Id = source.LocalId;
            location.ModeId = source.ModeId;
            location.WorldLocationId = source.WorldId;
            location.Kind = LocalLocationKind.Place;
            location.DisplayName = source.DisplayName.Trim().Length > 0 ? source.DisplayName.Trim() : place.Name;
            if (source.BattlefieldId.Length > 0)
                location.BattlefieldId = source.BattlefieldId;
            if (source.HoursPerInteraction > 0)
                location.HoursPerInteraction = source.HoursPerInteraction;

            Layout layout = new Layout(location, source);
            foreach (WorkshopLocalObject item in source.Objects)
            {
                LocalObjectDefinition target = location.FindObject(item.Id);
                if (target == null)
                {
                    target = new LocalObjectDefinition { Id = item.Id, Point = layout.NextObjectPoint() };
                    location.Objects.Add(target);
                    notes?.Add("Объект «" + item.Id + "» — новый" + layout.Hint + ".");
                }
                target.Label = item.Label;
                target.ActionLabel = item.ActionLabel;
                target.Kind = item.Kind == "Inspect" ? LocalObjectKind.Inspect : LocalObjectKind.Dialogue;
                target.DialogueId = target.Kind == LocalObjectKind.Dialogue ? ResolveDialogue(project, item.Dialogue) : string.Empty;
                target.Text = item.Text;
                target.OnceOnly = item.OnceOnly;
                target.RequiresFlag = QuestWorkshopConverter.FlagId(project, item.RequiresFlag);
                target.HiddenWhenFlag = QuestWorkshopConverter.FlagId(project, item.HiddenWhenFlag);
                if (item.InteractRadius > 0)
                    target.InteractRadius = item.InteractRadius;
            }

            foreach (WorkshopLocalEncounter item in source.Encounters)
            {
                LocalEncounterDefinition target = location.FindEncounter(item.Id);
                if (target == null)
                {
                    target = new LocalEncounterDefinition { Id = item.Id };
                    layout.PlaceEncounter(target);
                    location.Encounters.Add(target);
                    notes?.Add("Столкновение «" + item.Id + "» — новое" + layout.Hint + ".");
                }
                target.BattleIdPrefix = item.BattleIdPrefix.Length > 0 ? item.BattleIdPrefix : "battle." + source.LocalId + "." + item.Id + ".";
                target.IntroDialogueId = ResolveDialogue(project, item.Intro);
                target.AllowRetreat = item.AllowRetreat;
                target.PreparedStartCompanionId = item.PreparedCompanionId;
                target.ResolvedFlag = QuestWorkshopConverter.FlagId(project, item.ResolvedFlag);
            }

            foreach (WorkshopLocalEnemy item in source.Enemies)
            {
                LocalEnemyDefinition target = location.FindEnemy(item.InstanceId);
                if (target == null)
                {
                    target = new LocalEnemyDefinition { InstanceId = item.InstanceId, Point = layout.NextEnemyPoint(location.FindEncounter(item.EncounterId)) };
                    location.Enemies.Add(target);
                    notes?.Add("Противник «" + item.InstanceId + "» — новый" + layout.Hint + ".");
                }
                target.UnitTypeId = item.UnitTypeId;
                target.Level = Math.Max(1, item.Level);
                target.EncounterId = item.EncounterId;
            }
            return location;
        }

        // Рисунок-заглушка: овал проходимой земли в камне, вход слева.
        public static LocalLocationDefinition CreatePlaceholder(WorkshopPlaceLocation source)
        {
            LocalLocationDefinition location = new LocalLocationDefinition
            {
                Id = source.LocalId,
                BattlefieldId = source.BattlefieldId,
                PlaceholderArt = true,
                CanvasWidth = PlaceholderWidth,
                CanvasHeight = PlaceholderHeight,
                HexesAcross = PlaceholderHexesAcross,
                BattleFrameWidth = PlaceholderWidth,
                HoursPerInteraction = 0.25
            };
            WorldMapHexGrid grid = location.CreateGrid();
            WorldMapTerrainLayer layer = new WorldMapTerrainLayer(grid);
            for (int index = 0; index < grid.CellCount; index++)
            {
                WorldMapHexCell cell = grid.CellAt(index);
                grid.CellCenter(cell, out double x, out double y);
                double dx = (x - PlaceholderWidth / 2) / 860.0;
                double dy = (y - PlaceholderHeight / 2) / 420.0;
                layer.Set(cell, dx * dx + dy * dy <= 1 ? WorldMapGameplayTerrainType.OpenGround : WorldMapGameplayTerrainType.Cliffs);
            }
            location.TerrainCells = layer.Encode();
            location.Entrances.Add(new LocalEntranceDefinition
            {
                Id = source.LocalId + ".entrance",
                Label = "Выход",
                Point = new LocalPointData(240, PlaceholderHeight / 2)
            });
            return location;
        }

        // Заготовка точек: объекты — между входом и серединой, столкновения —
        // справа, каждое в своей полосе. В существующем месте новые точки
        // ставятся у входа — их нужно расставить в Базе локаций.
        private sealed class Layout
        {
            private readonly LocalLocationDefinition location;
            private readonly bool placeholder;
            private readonly int objectCount;
            private readonly int encounterCount;
            private int objectIndex;
            private int encounterIndex;
            private readonly Dictionary<string, int> enemiesPlaced = new Dictionary<string, int>(StringComparer.Ordinal);

            public Layout(LocalLocationDefinition location, WorkshopPlaceLocation source)
            {
                this.location = location;
                placeholder = location.PlaceholderArt && location.Objects.Count == 0 && location.Encounters.Count == 0 && location.Enemies.Count == 0;
                objectCount = Math.Max(1, source.Objects.Count);
                encounterCount = Math.Max(1, source.Encounters.Count);
            }

            public string Hint => placeholder ? string.Empty : ", точка у входа — расставить в Базе локаций";

            private LocalPointData Entrance =>
                location.Entrances.Count > 0 && location.Entrances[0] != null ? location.Entrances[0].Point : new LocalPointData(location.CanvasWidth / 2, location.CanvasHeight / 2);

            public LocalPointData NextObjectPoint()
            {
                int i = objectIndex++;
                if (!placeholder)
                    return new LocalPointData(Entrance.X + 60 + 40 * i, Entrance.Y);
                float x = objectCount == 1 ? 700f : 480f + i * (680f / (objectCount - 1));
                float y = i % 2 == 0 ? 410f : 670f;
                return new LocalPointData(x, y);
            }

            public void PlaceEncounter(LocalEncounterDefinition encounter)
            {
                int i = encounterIndex++;
                if (!placeholder)
                {
                    encounter.TriggerArea = new LocalAreaData(Entrance.X + 200, Entrance.Y - 60, 80, 120);
                    encounter.RetreatPoint = new LocalPointData(Entrance.X + 40, Entrance.Y);
                    return;
                }
                float band = 560f / encounterCount;
                encounter.TriggerArea = new LocalAreaData(1250f, 260f + i * band, 140f, band);
                encounter.RetreatPoint = new LocalPointData(330f, PlaceholderHeight / 2);
            }

            public LocalPointData NextEnemyPoint(LocalEncounterDefinition encounter)
            {
                string key = encounter?.Id ?? string.Empty;
                enemiesPlaced.TryGetValue(key, out int n);
                enemiesPlaced[key] = n + 1;
                if (!placeholder || encounter == null)
                    return new LocalPointData(Entrance.X + 320 + 50 * n, Entrance.Y);
                LocalAreaData area = encounter.TriggerArea;
                float centerY = area.Y + area.Height / 2;
                return new LocalPointData(1540f + (n % 2) * 70f, Mathf.Clamp(centerY + (n - 1) * 80f, 300f, 780f));
            }
        }

        // ── Перенос ─────────────────────────────────────────────────────────

        public static void Apply(WorkshopProject project, IEnumerable<WorkshopPlace> places, WorkshopPlaceAssets assets, WorkshopImportResult result)
        {
            foreach (WorkshopPlace place in places)
            {
                List<WorkshopIssue> issues = new List<WorkshopIssue>();
                Validate(project, place, assets, issues);
                WorkshopIssue error = issues.Find(i => i.Severity == WorkshopIssueSeverity.Error);
                if (error != null)
                {
                    result.Skipped.Add(place.Name + " — " + error.Message);
                    continue;
                }
                WorkshopPlaceLocation location = place.Location;

                if (location.MapEnabled && assets.Map != null)
                {
                    WorldMapLocationDefinition current = assets.Map.FindLocation(location.WorldId);
                    if (current == null || !SameMap(current, place))
                    {
                        bool created = ApplyMap(assets.Map, place);
                        (created ? result.AddedMapLocations : result.UpdatedMapLocations).Add(location.WorldId);
                        EditorUtility.SetDirty(assets.Map);
                    }
                }

                if (location.GeneratorEnabled && assets.Generator != null)
                {
                    List<WorldMapLandmarkRule> rules = assets.Generator.Settings.Landmarks;
                    WorldMapLandmarkRule current = FindRule(assets.Generator, location.WorldId);
                    WorldMapLandmarkRule next = BuildRule(place, current);
                    if (current == null)
                    {
                        rules.Add(next);
                        result.AddedLandmarks.Add(next.Id);
                        EditorUtility.SetDirty(assets.Generator);
                    }
                    else if (JsonUtility.ToJson(current) != JsonUtility.ToJson(next))
                    {
                        rules[rules.IndexOf(current)] = next;
                        result.UpdatedLandmarks.Add(next.Id);
                        EditorUtility.SetDirty(assets.Generator);
                    }
                }

                if (location.LocalEnabled && assets.Locals != null)
                {
                    List<LocalLocationDefinition> list = assets.Locals.locations;
                    LocalLocationDefinition current = list.Find(l => l != null && l.Id == location.LocalId);
                    LocalLocationDefinition next = BuildLocal(project, place, current);
                    if (current == null)
                    {
                        if (list.Count == 0)
                            assets.Locals.pointFormatVersion = LocalLocationDatabaseAsset.CurrentPointFormatVersion;
                        list.Add(next);
                        result.AddedLocalLocations.Add(next.Id);
                        EditorUtility.SetDirty(assets.Locals);
                    }
                    else if (JsonUtility.ToJson(current) != JsonUtility.ToJson(next))
                    {
                        list[list.IndexOf(current)] = next;
                        result.UpdatedLocalLocations.Add(next.Id);
                        EditorUtility.SetDirty(assets.Locals);
                    }
                    foreach (string issue in ValidateBuilt(project, next, assets))
                        result.ValidationIssues.Add(next.Id + ": " + issue);
                }
            }
        }

        // ── Снимок для мастерской ───────────────────────────────────────────

        // Места Unity в снимке: что уже есть в Базе карты, генераторе и Базе
        // локаций, плюс списки для подсказок (поля боя, типы существ).
        public static void AppendSnapshot(StringBuilder json, WorkshopPlaceAssets assets)
        {
            List<object> map = new List<object>();
            if (assets?.Map != null)
                foreach (WorldMapLocationDefinition location in assets.Map.Locations)
                    if (location != null)
                        map.Add(new Dictionary<string, object>
                        {
                            ["id"] = location.Id, ["displayName"] = location.DisplayName, ["modeId"] = location.ModeId ?? string.Empty,
                            ["description"] = location.InteractionDescription, ["threat"] = location.Threat
                        });
            List<object> landmarks = new List<object>();
            if (assets?.Generator != null)
                foreach (WorldMapLandmarkRule rule in assets.Generator.Settings.Landmarks)
                    if (rule != null)
                        landmarks.Add(new Dictionary<string, object>
                        {
                            ["id"] = rule.Id, ["name"] = rule.Name, ["locationId"] = rule.LocationId ?? string.Empty, ["enabled"] = rule.Enabled,
                            ["terrains"] = (rule.Terrains ?? new List<WorldMapGameplayTerrainType>()).Select(t => (object)t.ToString()).ToList()
                        });
            List<object> locals = new List<object>();
            if (assets?.Locals != null)
                foreach (LocalLocationDefinition location in assets.Locals.locations)
                    if (location != null && !location.IsBattlefield)
                        locals.Add(new Dictionary<string, object>
                        {
                            ["id"] = location.Id, ["worldLocationId"] = location.WorldLocationId, ["modeId"] = location.ModeId,
                            ["displayName"] = location.DisplayName, ["battlefieldId"] = location.BattlefieldId,
                            ["objects"] = location.Objects.Where(o => o != null).Select(o => (object)new Dictionary<string, object>
                            {
                                ["id"] = o.Id, ["label"] = o.Label, ["actionLabel"] = o.ActionLabel, ["kind"] = o.Kind.ToString(),
                                ["dialogueId"] = o.DialogueId, ["text"] = o.Text, ["onceOnly"] = o.OnceOnly,
                                ["requiresFlag"] = o.RequiresFlag, ["hiddenWhenFlag"] = o.HiddenWhenFlag
                            }).ToList(),
                            ["enemies"] = location.Enemies.Where(e => e != null).Select(e => (object)new Dictionary<string, object>
                            {
                                ["instanceId"] = e.InstanceId, ["unitTypeId"] = e.UnitTypeId, ["level"] = (double)e.Level, ["encounterId"] = e.EncounterId
                            }).ToList(),
                            ["encounters"] = location.Encounters.Where(e => e != null).Select(e => (object)new Dictionary<string, object>
                            {
                                ["id"] = e.Id, ["battleIdPrefix"] = e.BattleIdPrefix, ["introDialogueId"] = e.IntroDialogueId,
                                ["allowRetreat"] = e.AllowRetreat, ["preparedCompanionId"] = e.PreparedStartCompanionId, ["resolvedFlag"] = e.ResolvedFlag
                            }).ToList()
                        });
            List<object> battlefields = new List<object>();
            if (assets?.Battlefields != null)
                foreach (BattlefieldDefinitionData field in assets.Battlefields.Battlefields)
                    if (field != null)
                        battlefields.Add(field.Id);
            List<object> units = new List<object>();
            if (assets?.Units != null)
                foreach (UnitDefinitionData unit in assets.Units.Units)
                    if (unit != null)
                        units.Add(new Dictionary<string, object> { ["id"] = unit.Id, ["name"] = unit.DisplayLabel });

            json.Append(",\"mapLocations\":").Append(QuestWorkshopJson.Serialize(map));
            json.Append(",\"landmarks\":").Append(QuestWorkshopJson.Serialize(landmarks));
            json.Append(",\"localLocations\":").Append(QuestWorkshopJson.Serialize(locals));
            json.Append(",\"battlefields\":").Append(QuestWorkshopJson.Serialize(battlefields));
            json.Append(",\"unitTypes\":").Append(QuestWorkshopJson.Serialize(units));
        }

        public static string Describe(WorkshopPlaceReport report)
        {
            List<string> parts = new List<string>();
            if (report.MapState.Length > 0)
                parts.Add("карта: " + report.MapState);
            if (report.GeneratorState.Length > 0)
                parts.Add("генератор: " + report.GeneratorState);
            if (report.LocalState.Length > 0)
                parts.Add("место: " + report.LocalState);
            return string.Join(" · ", parts);
        }
    }
}

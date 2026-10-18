using System;
using System.Collections.Generic;
using System.IO;

namespace KingdomSurvival.QuestWorkshopBridge.Editor
{
    // Проект Мастерской квестов (QuestWorkshop/workspace.json) в том объёме,
    // который нужен для переноса в Unity. Остальные авторские поля (Хроника,
    // цели, последствия, паспорт истории) остаются в мастерской.
    public sealed class WorkshopProject
    {
        public string SavedAt = string.Empty;
        public int Revision;
        public string Name = string.Empty;
        public readonly List<WorkshopQuest> Quests = new List<WorkshopQuest>();
        public readonly Dictionary<string, WorkshopPerson> People = new Dictionary<string, WorkshopPerson>(StringComparer.Ordinal);
        public readonly Dictionary<string, WorkshopFact> Facts = new Dictionary<string, WorkshopFact>(StringComparer.Ordinal);

        public static WorkshopProject Load(string path)
        {
            return Parse(File.ReadAllText(path));
        }

        public static WorkshopProject Parse(string json)
        {
            JsonNode root = new JsonNode(QuestWorkshopJson.Parse(json));
            JsonNode workspace = root.Has("workspace") ? root["workspace"] : root;
            if (!workspace.Has("quests"))
                throw new FormatException("В файле нет списка квестов (workspace.quests).");

            WorkshopProject project = new WorkshopProject
            {
                SavedAt = root["savedAt"].Str(),
                Revision = root["revision"].Int(),
                Name = workspace["name"].Str()
            };

            foreach (JsonNode person in workspace["people"].Items())
            {
                WorkshopPerson p = new WorkshopPerson
                {
                    Id = person["id"].Str(),
                    Name = person["name"].Str(),
                    Role = person["role"].Str(),
                    UnityId = person["unityId"].Str()
                };
                if (p.Id.Length > 0)
                    project.People[p.Id] = p;
            }

            foreach (JsonNode fact in workspace["facts"].Items())
            {
                WorkshopFact f = new WorkshopFact
                {
                    Id = fact["id"].Str(),
                    Title = fact["title"].Str(),
                    Kind = fact["kind"].Str(),
                    Text = fact["text"].Str(),
                    UnityId = fact["unityId"].Str(),
                    Category = fact["category"].Str(),
                    Status = fact["status"].Str(),
                    Notes = fact["notes"].Str()
                };
                if (f.Id.Length > 0)
                    project.Facts[f.Id] = f;
            }

            foreach (JsonNode quest in workspace["quests"].Items())
                project.Quests.Add(WorkshopQuest.Read(quest));

            return project;
        }
    }

    public sealed class WorkshopPerson
    {
        public string Id = string.Empty;
        public string Name = string.Empty;
        public string Role = string.Empty;
        public string UnityId = string.Empty;
    }

    public sealed class WorkshopFact
    {
        public string Id = string.Empty;
        public string Title = string.Empty;
        public string Kind = string.Empty;
        public string Text = string.Empty;
        public string UnityId = string.Empty;
        public string Category = string.Empty;
        public string Status = string.Empty;
        public string Notes = string.Empty;
    }

    public sealed class WorkshopQuest
    {
        public string Id = string.Empty;
        public string Title = string.Empty;
        public string Summary = string.Empty;
        public string Line = string.Empty;
        public string Role = string.Empty;
        public string Mode = string.Empty;
        public string Status = string.Empty;
        public string Approval = string.Empty;
        public string Stage = string.Empty;
        public string UnityId = string.Empty;
        public string UnityMode = string.Empty;
        public string UnityCategory = string.Empty;
        public string StartSceneId = string.Empty;
        public bool Archived;
        public bool HasSourceSnapshot;
        public readonly List<WorkshopScene> Scenes = new List<WorkshopScene>();

        // Есть ли ответы, ведущие в реплики других сцен, или переходы «после разговора».
        public bool HasCrossSceneLinks()
        {
            Dictionary<string, WorkshopScene> owner = new Dictionary<string, WorkshopScene>(StringComparer.Ordinal);
            foreach (WorkshopScene scene in Scenes)
                foreach (WorkshopNode node in scene.Nodes)
                    owner[node.Id] = scene;
            foreach (WorkshopScene scene in Scenes)
            {
                if (scene.NextSceneId.Length > 0 && scene.Nodes.Count > 0 && Scenes.Exists(s => s.Id == scene.NextSceneId && s.Nodes.Count > 0))
                    return true;
                foreach (WorkshopNode node in scene.Nodes)
                    foreach (WorkshopChoice choice in node.Choices)
                        foreach (string target in new[] { choice.NextNodeId, choice.SuccessNodeId, choice.FailureNodeId })
                            if (target.Length > 0 && owner.TryGetValue(target, out WorkshopScene other) && other != scene)
                                return true;
            }
            return false;
        }

        // «single» — весь квест один диалог (ID в квесте), «scenes» — по сценам.
        // Пустое значение в мастерской — автоматически, как ksQuestUnityMode.
        public bool TransfersAsSingleDialogue => UnityMode == "single" || (UnityMode != "scenes" && HasCrossSceneLinks());

        // Что уходит в Unity: сцены или одна сборная сцена всего квеста.
        public List<WorkshopScene> TransferUnits()
        {
            List<WorkshopScene> units = new List<WorkshopScene>();
            if (TransfersAsSingleDialogue)
            {
                if (Scenes.Exists(s => s.Nodes.Count > 0))
                    units.Add(MergedScene());
                return units;
            }
            units.AddRange(Scenes);
            return units;
        }

        // Тот же алгоритм, что ksMergedQuestScene в мастерской: «Закончить разговор»
        // в сцене с продолжением ведёт к первой реплике следующей сцены, узел без
        // ответов получает «…» (Continue).
        public WorkshopScene MergedScene()
        {
            List<WorkshopScene> written = Scenes.FindAll(s => s.Nodes.Count > 0);
            WorkshopScene start = written.Find(s => s.Id == StartSceneId) ?? (written.Count > 0 ? written[0] : null);
            WorkshopScene merged = new WorkshopScene
            {
                Quest = this,
                IsMerged = true,
                Id = Id + "__unity",
                Title = Title,
                Summary = Summary,
                StartNodeId = start != null ? start.StartNodeId : string.Empty,
                UnityIdField = UnityId,
                UnityCategory = UnityCategory,
                UnityTags = start != null ? new List<string>(start.UnityTags) : new List<string>(),
                Conditions = start != null ? start.Conditions : new WorkshopConditionGroup(),
                Encounter = start?.Encounter
            };
            foreach (WorkshopScene scene in written)
            {
                WorkshopScene next = written.Find(s => s.Id == scene.NextSceneId);
                foreach (WorkshopNode node in scene.Nodes)
                {
                    WorkshopNode copy = node.Clone();
                    if (next != null)
                    {
                        foreach (WorkshopChoice choice in copy.Choices)
                        {
                            if (choice.Kind != "Exit")
                                continue;
                            choice.Kind = "Normal";
                            choice.NextNodeId = next.StartNodeId;
                        }
                        if (copy.Choices.Count == 0)
                            copy.Choices.Add(new WorkshopChoice { Id = node.Id + "__next", Text = "…", Kind = "Continue", NextNodeId = next.StartNodeId, Unavailable = "Hidden" });
                    }
                    merged.Nodes.Add(copy);
                }
            }
            return merged;
        }

        public static WorkshopQuest Read(JsonNode node)
        {
            WorkshopQuest quest = new WorkshopQuest
            {
                Id = node["id"].Str(),
                Title = node["title"].Str(),
                Summary = node["summary"].Str(),
                Line = node["line"].Str(),
                Role = node["role"].Str(),
                Mode = node["mode"].Str(),
                Status = node["status"].Str(),
                Approval = node["approval"].Str(),
                Stage = node["stage"].Str(),
                UnityId = node["unityId"].Str().Trim(),
                UnityMode = node["unityMode"].Str(),
                UnityCategory = node["unityCategory"].Str(),
                StartSceneId = node["startSceneId"].Str(),
                Archived = node["archived"].Bool(),
                HasSourceSnapshot = node.Has("sourceSnapshot")
            };
            foreach (JsonNode scene in node["scenes"].Items())
                quest.Scenes.Add(WorkshopScene.Read(scene, quest));
            return quest;
        }
    }

    public sealed class WorkshopScene
    {
        public WorkshopQuest Quest;
        public bool IsMerged;
        public string Id = string.Empty;
        public string Title = string.Empty;
        public string Summary = string.Empty;
        public string Notes = string.Empty;
        public string StartNodeId = string.Empty;
        public string NextSceneId = string.Empty;
        public string UnityIdField = string.Empty;
        public string UnityCategory = string.Empty;
        public string UnityStatus = string.Empty;
        public List<string> UnityTags = new List<string>();
        public WorkshopConditionGroup Conditions = new WorkshopConditionGroup();
        public readonly List<WorkshopNode> Nodes = new List<WorkshopNode>();
        public WorkshopEncounter Encounter;

        // ID диалога Unity — те же правила, что и в мастерской (ksSceneUnityId).
        public string UnityId
        {
            get
            {
                if (UnityIdField.Length > 0 || IsMerged)
                    return UnityIdField;
                if (Id.StartsWith("unity_scene_", StringComparison.Ordinal))
                    return Id.Substring("unity_scene_".Length);
                if (Quest != null && Quest.UnityId.Length > 0 && Quest.Scenes.Count == 1 && Quest.HasSourceSnapshot)
                    return Quest.UnityId;
                return string.Empty;
            }
        }

        public static WorkshopScene Read(JsonNode node, WorkshopQuest quest)
        {
            WorkshopScene scene = new WorkshopScene
            {
                Quest = quest,
                Id = node["id"].Str(),
                Title = node["title"].Str(),
                Summary = node["summary"].Str(),
                Notes = node["notes"].Str(),
                StartNodeId = node["startNodeId"].Str(),
                NextSceneId = node["nextSceneId"].Str(),
                UnityIdField = node["unityId"].Str().Trim(),
                UnityCategory = node["unityCategory"].Str(),
                UnityStatus = node["unityStatus"].Str(),
                UnityTags = node["unityTags"].Strings(),
                Conditions = WorkshopConditionGroup.Read(node["conditions"])
            };
            foreach (JsonNode child in node["nodes"].Items())
                scene.Nodes.Add(WorkshopNode.Read(child));
            if (node.Has("encounter"))
                scene.Encounter = WorkshopEncounter.Read(node["encounter"]);
            return scene;
        }
    }

    public sealed class WorkshopNode
    {
        public string Id = string.Empty;
        public string Title = string.Empty;
        public string SpeakerId = string.Empty;
        public readonly List<WorkshopBlock> Blocks = new List<WorkshopBlock>();
        public readonly List<WorkshopChoice> Choices = new List<WorkshopChoice>();

        // Копия узла с собственными ответами (реплики общие — их не меняют).
        public WorkshopNode Clone()
        {
            WorkshopNode copy = new WorkshopNode { Id = Id, Title = Title, SpeakerId = SpeakerId };
            copy.Blocks.AddRange(Blocks);
            foreach (WorkshopChoice choice in Choices)
                copy.Choices.Add(choice.Clone());
            return copy;
        }

        public static WorkshopNode Read(JsonNode node)
        {
            WorkshopNode result = new WorkshopNode
            {
                Id = node["id"].Str(),
                Title = node["title"].Str(),
                SpeakerId = node["speakerId"].Str()
            };
            foreach (JsonNode block in node["blocks"].Items())
                result.Blocks.Add(WorkshopBlock.Read(block));
            foreach (JsonNode choice in node["choices"].Items())
                result.Choices.Add(WorkshopChoice.Read(choice));
            return result;
        }
    }

    public sealed class WorkshopBlock
    {
        public string Id = string.Empty;
        public string Kind = string.Empty;
        public string SpeakerId = string.Empty;
        public string Text = string.Empty;
        public WorkshopConditionGroup Conditions = new WorkshopConditionGroup();
        public WorkshopCheck Check;
        public readonly List<WorkshopEffect> Effects = new List<WorkshopEffect>();

        public static WorkshopBlock Read(JsonNode node)
        {
            WorkshopBlock block = new WorkshopBlock
            {
                Id = node["id"].Str(),
                Kind = node["kind"].Str(),
                SpeakerId = node["speakerId"].Str(),
                Text = node["text"].Str(),
                Conditions = WorkshopConditionGroup.Read(node["conditions"]),
                Check = node.Has("check") ? WorkshopCheck.Read(node["check"]) : null
            };
            foreach (JsonNode effect in node["effects"].Items())
                block.Effects.Add(WorkshopEffect.Read(effect));
            return block;
        }
    }

    public sealed class WorkshopChoice
    {
        public string Id = string.Empty;
        public string Text = string.Empty;
        public string Kind = string.Empty;
        public string Unavailable = string.Empty;
        public string Hint = string.Empty;
        public string Intent = string.Empty;
        public string NextNodeId = string.Empty;
        public string SuccessNodeId = string.Empty;
        public string FailureNodeId = string.Empty;
        public WorkshopConditionGroup Conditions = new WorkshopConditionGroup();
        public WorkshopCheck Check;
        public readonly List<WorkshopEffect> Effects = new List<WorkshopEffect>();
        public readonly List<WorkshopEffect> FailureEffects = new List<WorkshopEffect>();

        public bool IsActive => Kind == "ActiveReturnable" || Kind == "ActiveDecisive";

        // Поверхностная копия: списки эффектов общие, меняются только вид и переход.
        public WorkshopChoice Clone()
        {
            return (WorkshopChoice)MemberwiseClone();
        }

        public static WorkshopChoice Read(JsonNode node)
        {
            WorkshopChoice choice = new WorkshopChoice
            {
                Id = node["id"].Str(),
                Text = node["text"].Str(),
                Kind = node["kind"].Str("Normal"),
                Unavailable = node["unavailable"].Str("Hidden"),
                Hint = node["hint"].Str(),
                Intent = node["intent"].Str(),
                NextNodeId = node["nextNodeId"].Str(),
                SuccessNodeId = node["successNodeId"].Str(),
                FailureNodeId = node["failureNodeId"].Str(),
                Conditions = WorkshopConditionGroup.Read(node["conditions"]),
                Check = node.Has("check") ? WorkshopCheck.Read(node["check"]) : null
            };
            foreach (JsonNode effect in node["effects"].Items())
                choice.Effects.Add(WorkshopEffect.Read(effect));
            foreach (JsonNode effect in node["failureEffects"].Items())
                choice.FailureEffects.Add(WorkshopEffect.Read(effect));
            return choice;
        }
    }

    public sealed class WorkshopConditionGroup
    {
        public string Mode = "All";
        public readonly List<WorkshopCondition> Items = new List<WorkshopCondition>();

        public static WorkshopConditionGroup Read(JsonNode node)
        {
            WorkshopConditionGroup group = new WorkshopConditionGroup { Mode = node["mode"].Str("All") };
            foreach (JsonNode item in node["items"].Items())
            {
                group.Items.Add(new WorkshopCondition
                {
                    Id = item["id"].Str(),
                    Type = item["type"].Str(),
                    Ref = item["ref"].Str(),
                    Value = item["value"].Int(),
                    Negate = item["negate"].Bool()
                });
            }
            return group;
        }
    }

    public sealed class WorkshopCondition
    {
        public string Id = string.Empty;
        public string Type = string.Empty;
        public string Ref = string.Empty;
        public int Value;
        public bool Negate;
    }

    public sealed class WorkshopEffect
    {
        public string Id = string.Empty;
        public string Type = string.Empty;
        public string Ref = string.Empty;
        public int Amount;
        public string Note = string.Empty;

        public static WorkshopEffect Read(JsonNode node)
        {
            return new WorkshopEffect
            {
                Id = node["id"].Str(),
                Type = node["type"].Str(),
                Ref = node["ref"].Str(),
                Amount = node["amount"].Int(),
                Note = node["note"].Str()
            };
        }
    }

    public sealed class WorkshopCheck
    {
        public string Id = string.Empty;
        public string Kind = string.Empty;
        public string Quality = string.Empty;
        public string CompetencyId = string.Empty;
        public int Difficulty = 13;
        public readonly List<WorkshopModifier> Modifiers = new List<WorkshopModifier>();

        public static WorkshopCheck Read(JsonNode node)
        {
            WorkshopCheck check = new WorkshopCheck
            {
                Id = node["id"].Str(),
                Kind = node["kind"].Str(),
                Quality = node["quality"].Str(),
                CompetencyId = node["competencyId"].Str(),
                Difficulty = node["difficulty"].Int(13)
            };
            foreach (JsonNode modifier in node["modifiers"].Items())
            {
                check.Modifiers.Add(new WorkshopModifier
                {
                    Id = modifier["id"].Str(),
                    Label = modifier["label"].Str(),
                    Amount = modifier["amount"].Int(),
                    Conditions = WorkshopConditionGroup.Read(modifier["conditions"])
                });
            }
            return check;
        }
    }

    public sealed class WorkshopModifier
    {
        public string Id = string.Empty;
        public string Label = string.Empty;
        public int Amount;
        public WorkshopConditionGroup Conditions = new WorkshopConditionGroup();
    }

    public sealed class WorkshopEncounter
    {
        public bool Enabled;
        public string EncounterId = string.Empty;
        public string DisplayName = string.Empty;
        public string Description = string.Empty;
        public string Category = "Road";
        public string Status = "Draft";
        public string PoolId = string.Empty;
        public string DurationClass = "Standard";
        public string MemoryClass = "None";
        public string SelectionMode = "Pool";
        public List<string> Functions = new List<string>();
        public List<string> Tags = new List<string>();
        public int DiscoveryChance;
        public int Weight = 1;
        public bool Unlimited;
        public int MaxOccurrences = 1;
        public int CooldownHours;
        public List<string> Regions = new List<string>();
        public List<string> RequiredLocationTags = new List<string>();
        public List<string> ForbiddenLocationTags = new List<string>();
        public List<string> RequiredFlagsAll = new List<string>();
        public List<string> RequiredFlagsAny = new List<string>();
        public List<string> ForbiddenFlags = new List<string>();
        public List<string> FlagsOnStart = new List<string>();
        public List<string> FlagsOnComplete = new List<string>();
        public List<string> ClearFlagsOnComplete = new List<string>();
        public string DesignerNotes = string.Empty;
        public string FutureHooks = string.Empty;

        public static WorkshopEncounter Read(JsonNode node)
        {
            return new WorkshopEncounter
            {
                Enabled = node["enabled"].Bool(true),
                EncounterId = node["encounterId"].Str().Trim(),
                DisplayName = node["displayName"].Str(),
                Description = node["description"].Str(),
                Category = node["category"].Str("Road"),
                Status = node["status"].Str("Draft"),
                PoolId = node["poolId"].Str().Trim(),
                DurationClass = node["durationClass"].Str("Standard"),
                MemoryClass = node["memoryClass"].Str("None"),
                SelectionMode = node["selectionMode"].Str("Pool"),
                Functions = node["functions"].Strings(),
                Tags = node["tags"].Strings(),
                DiscoveryChance = node["discoveryChance"].Int(),
                Weight = node["weight"].Int(1),
                Unlimited = node["unlimited"].Bool(),
                MaxOccurrences = node["maxOccurrences"].Int(1),
                CooldownHours = node["cooldownHours"].Int(),
                Regions = node["regions"].Strings(),
                RequiredLocationTags = node["requiredLocationTags"].Strings(),
                ForbiddenLocationTags = node["forbiddenLocationTags"].Strings(),
                RequiredFlagsAll = node["requiredFlagsAll"].Strings(),
                RequiredFlagsAny = node["requiredFlagsAny"].Strings(),
                ForbiddenFlags = node["forbiddenFlags"].Strings(),
                FlagsOnStart = node["flagsOnStart"].Strings(),
                FlagsOnComplete = node["flagsOnComplete"].Strings(),
                ClearFlagsOnComplete = node["clearFlagsOnComplete"].Strings(),
                DesignerNotes = node["designerNotes"].Str(),
                FutureHooks = node["futureHooks"].Str()
            };
        }
    }
}

using System.Collections.Generic;
using System.IO;
using System.Reflection;
using KingdomSurvival.DialogueDatabase;
using KingdomSurvival.Encounters;
using UnityEditor;
using UnityEngine;

namespace KingdomSurvival.FreePlay.Editor
{
    // ПР-12Б: первичное наполнение базы диалогов сценами свободной игры.
    // Сцены лежат в Seed/*.json (формат полей DialogueDefinitionData). Засев
    // только ДОБАВЛЯЕТ недостающие сцены и говорящих и никогда не
    // перезаписывает уже существующие: после засева текст правится в базе
    // диалогов, а не здесь.
    public static class FreePlayContentSeed
    {
        public const string DialogueDatabasePath = "Assets/_Project/DialogueDatabase/Resources/DialogueDatabase/KingdomSurvivalDialogues.asset";
        public const string SeedFolder = "Assets/_Project/FreePlay/Editor/Seed";
        public const string EncounterDatabasePath = "Assets/_Project/Encounters/Resources/Encounters/KingdomSurvivalEncounters.asset";
        public const string EncounterFlagsPath = "Assets/_Project/Encounters/Resources/Encounters/KingdomSurvivalEncounterFlags.asset";

        private static readonly (string Id, string Name, string Role)[] Speakers =
        {
            ("coalburner", "Углежог", "Хутор у кромки Чёрного леса"),
            ("agnessa", "Агнесса", "Разведчица"),
            ("miller_son", "Сын мельника", "С дальней мельницы")
        };

        [MenuItem("Kingdom Survival/Свободная игра/Добавить недостающие сцены в базу диалогов")]
        public static void Seed()
        {
            DialogueDatabaseAsset database = AssetDatabase.LoadAssetAtPath<DialogueDatabaseAsset>(DialogueDatabasePath);
            if (database == null)
            {
                Debug.LogError("Свободная игра: база диалогов не найдена: " + DialogueDatabasePath);
                return;
            }

            List<string> added = AddMissing(database);
            if (added.Count > 0)
                EditorUtility.SetDirty(database);

            EncounterDatabaseAsset encounters = AssetDatabase.LoadAssetAtPath<EncounterDatabaseAsset>(EncounterDatabasePath);
            EncounterFlagRegistryAsset flags = AssetDatabase.LoadAssetAtPath<EncounterFlagRegistryAsset>(EncounterFlagsPath);
            if (encounters != null && flags != null)
            {
                List<string> addedEncounters = AddMissingEncounters(encounters, flags);
                if (addedEncounters.Count > 0)
                {
                    EditorUtility.SetDirty(encounters);
                    EditorUtility.SetDirty(flags);
                    added.AddRange(addedEncounters);
                }
            }
            else
            {
                Debug.LogError("Свободная игра: база встреч или реестр флагов не найдены.");
            }

            if (added.Count > 0)
                AssetDatabase.SaveAssets();
            Debug.Log(added.Count > 0
                ? "Свободная игра: добавлено в базу диалогов — " + string.Join(", ", added) + "."
                : "Свободная игра: все сцены уже в базе диалогов.");
        }

        // Добавляет недостающих говорящих и сцены. Возвращает, что добавлено.
        public static List<string> AddMissing(DialogueDatabaseAsset database)
        {
            List<string> added = new List<string>();
            List<DialogueSpeakerData> speakers = PrivateList<DialogueSpeakerData>(database, "speakers");
            foreach ((string id, string name, string role) in Speakers)
            {
                if (database.FindSpeaker(id) != null)
                    continue;
                speakers.Add(JsonUtility.FromJson<DialogueSpeakerData>(
                    "{\"id\":\"" + id + "\",\"displayName\":\"" + name + "\",\"role\":\"" + role + "\"}"));
                added.Add("говорящий " + id);
            }

            List<DialogueDefinitionData> dialogues = PrivateList<DialogueDefinitionData>(database, "dialogues");
            foreach (DialogueDefinitionData dialogue in LoadSeedDialogues())
            {
                if (database.FindDialogue(dialogue.Id) != null)
                    continue;
                dialogues.Add(dialogue);
                added.Add(dialogue.Id);
            }
            return added;
        }

        [System.Serializable]
        private sealed class FlagSeed
        {
            public List<EncounterFlagDefinition> flags = new List<EncounterFlagDefinition>();
        }

        // Добавляет недостающие флаги реестра и встречи свободной игры.
        public static List<string> AddMissingEncounters(EncounterDatabaseAsset encounters, EncounterFlagRegistryAsset registry)
        {
            List<string> added = new List<string>();
            List<EncounterFlagDefinition> flagList = PrivateList<EncounterFlagRegistryAsset, EncounterFlagDefinition>(registry, "flags");
            foreach (string file in SeedFiles(SeedFolder + "/Flags"))
            {
                foreach (EncounterFlagDefinition flag in JsonUtility.FromJson<FlagSeed>(File.ReadAllText(file)).flags)
                {
                    if (registry.FindById(flag.FlagId) != null)
                        continue;
                    flagList.Add(flag);
                    added.Add("флаг " + flag.FlagId);
                }
            }

            List<EncounterDefinition> encounterList = PrivateList<EncounterDatabaseAsset, EncounterDefinition>(encounters, "encounters");
            foreach (string file in SeedFiles(SeedFolder + "/Encounters"))
            {
                EncounterDefinition encounter = JsonUtility.FromJson<EncounterDefinition>(File.ReadAllText(file));
                if (encounters.FindById(encounter.EncounterId) != null)
                    continue;
                encounterList.Add(encounter);
                added.Add("встреча " + encounter.EncounterId);
            }
            return added;
        }

        private static string[] SeedFiles(string folder)
        {
            if (!Directory.Exists(folder))
                return new string[0];
            string[] files = Directory.GetFiles(folder, "*.json");
            System.Array.Sort(files, System.StringComparer.Ordinal);
            return files;
        }

        public static List<DialogueDefinitionData> LoadSeedDialogues()
        {
            List<DialogueDefinitionData> dialogues = new List<DialogueDefinitionData>();
            if (!Directory.Exists(SeedFolder))
                return dialogues;
            string[] files = Directory.GetFiles(SeedFolder, "*.json");
            System.Array.Sort(files, System.StringComparer.Ordinal);
            foreach (string file in files)
                dialogues.Add(JsonUtility.FromJson<DialogueDefinitionData>(File.ReadAllText(file)));
            return dialogues;
        }

        private static List<T> PrivateList<T>(DialogueDatabaseAsset database, string fieldName)
        {
            return PrivateList<DialogueDatabaseAsset, T>(database, fieldName);
        }

        private static List<T> PrivateList<TOwner, T>(TOwner owner, string fieldName)
        {
            FieldInfo field = typeof(TOwner).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            List<T> list = (List<T>)field.GetValue(owner);
            if (list == null)
            {
                list = new List<T>();
                field.SetValue(owner, list);
            }
            return list;
        }
    }
}

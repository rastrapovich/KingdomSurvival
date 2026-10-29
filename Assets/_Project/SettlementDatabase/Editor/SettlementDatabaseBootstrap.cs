using System.Collections.Generic;
using System.IO;
using System.Reflection;
using KingdomSurvival.DialogueDatabase;
using UnityEditor;
using UnityEngine;

namespace KingdomSurvival.SettlementDatabase.Editor
{
    // 12Т: создание «Базы поселений» с рабочим примером и засев его сцен в
    // Базу диалогов. Всё идемпотентно и только добавляет: правленные автором
    // профили не перезаписываются, удалённая автором сцена второй раз не
    // засевается (seededDialogueIds). Во время игры ассеты не создаются.
    [InitializeOnLoad]
    public static class SettlementDatabaseBootstrap
    {
        public const string SeedFolder = "Assets/_Project/SettlementDatabase/Editor/Seed";
        public const string DialogueDatabasePath = "Assets/_Project/DialogueDatabase/Resources/DialogueDatabase/KingdomSurvivalDialogues.asset";

        static SettlementDatabaseBootstrap()
        {
            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode)
                    return;
                bool created = AssetDatabase.LoadAssetAtPath<SettlementDatabaseAsset>(SettlementDatabaseAsset.AssetPath) == null;
                SettlementDatabaseAsset database = LoadOrCreate();
                if (created)
                    SeedDialogues(database);
            };
        }

        // -executeMethod KingdomSurvival.SettlementDatabase.Editor.SettlementDatabaseBootstrap.RunBatch:
        // создать/дополнить Базу предметов и Базу поселений и засеять сцены
        // примера — без открытия окон (batchmode).
        public static void RunBatch()
        {
            ItemDatabase.Editor.ItemDatabaseBootstrap.LoadOrCreate();
            SettlementDatabaseAsset database = LoadOrCreate();
            List<string> added = SettlementDatabaseBootstrap.AddMissingPilot(database);
            if (added.Count > 0)
                EditorUtility.SetDirty(database);
            List<string> seeded = SeedDialogues(database);
            AssetDatabase.SaveAssets();
            Debug.Log("12Т: базы готовы. Пример: " + string.Join(", ", added) + "; сцены: " + string.Join(", ", seeded));
        }

        public static SettlementDatabaseAsset LoadOrCreate()
        {
            SettlementDatabaseAsset database = AssetDatabase.LoadAssetAtPath<SettlementDatabaseAsset>(SettlementDatabaseAsset.AssetPath);
            if (database != null)
                return database;
            EnsureFolder("Assets/_Project/SettlementDatabase", "Resources");
            EnsureFolder("Assets/_Project/SettlementDatabase/Resources", "SettlementDatabase");
            database = ScriptableObject.CreateInstance<SettlementDatabaseAsset>();
            AddMissingPilot(database);
            AssetDatabase.CreateAsset(database, SettlementDatabaseAsset.AssetPath);
            AssetDatabase.SaveAssets();
            return database;
        }

        // Недостающие профили и поручения рабочего примера.
        public static List<string> AddMissingPilot(SettlementDatabaseAsset database)
        {
            List<string> added = new List<string>();
            database.profiles ??= new List<SettlementProfileEntry>();
            database.errands ??= new List<ErrandDefinition>();
            foreach (SettlementProfileEntry entry in SettlementPilotContent.Profiles())
            {
                if (database.FindEntry(entry.Data.Id) != null)
                    continue;
                database.profiles.Add(entry);
                added.Add(entry.Data.Name);
            }
            foreach (ErrandDefinition errand in SettlementPilotContent.Errands())
            {
                if (database.errands.Exists(e => e != null && e.Id == errand.Id))
                    continue;
                database.errands.Add(errand);
                added.Add("поручение «" + errand.Title + "»");
            }
            return added;
        }

        // Говорящие и сцены пилота — в Базу диалогов (только недостающие и
        // ещё ни разу не засеянные).
        public static List<string> SeedDialogues(SettlementDatabaseAsset settlements)
        {
            List<string> added = new List<string>();
            DialogueDatabaseAsset database = AssetDatabase.LoadAssetAtPath<DialogueDatabaseAsset>(DialogueDatabasePath);
            if (database == null || settlements == null)
                return added;
            settlements.seededDialogueIds ??= new List<string>();

            List<DialogueSpeakerData> speakers = PrivateList<DialogueSpeakerData>(database, "speakers");
            foreach ((string id, string name, string role) in SettlementPilotContent.Speakers)
            {
                if (database.FindSpeaker(id) != null)
                    continue;
                speakers.Add(JsonUtility.FromJson<DialogueSpeakerData>(
                    "{\"id\":\"" + id + "\",\"displayName\":\"" + name + "\",\"role\":\"" + role + "\"}"));
                added.Add("говорящий " + id);
            }

            List<DialogueDefinitionData> dialogues = PrivateList<DialogueDefinitionData>(database, "dialogues");
            if (Directory.Exists(SeedFolder))
            {
                string[] files = Directory.GetFiles(SeedFolder, "*.json");
                System.Array.Sort(files, System.StringComparer.Ordinal);
                foreach (string file in files)
                {
                    DialogueDefinitionData dialogue = JsonUtility.FromJson<DialogueDefinitionData>(File.ReadAllText(file));
                    if (dialogue == null || string.IsNullOrEmpty(dialogue.Id) || settlements.seededDialogueIds.Contains(dialogue.Id))
                        continue;
                    settlements.seededDialogueIds.Add(dialogue.Id);
                    if (database.FindDialogue(dialogue.Id) != null)
                        continue;
                    dialogues.Add(dialogue);
                    added.Add(dialogue.Id);
                }
            }

            EditorUtility.SetDirty(settlements);
            if (added.Count > 0)
                EditorUtility.SetDirty(database);
            AssetDatabase.SaveAssets();
            return added;
        }

        private static List<T> PrivateList<T>(DialogueDatabaseAsset owner, string fieldName)
        {
            FieldInfo field = typeof(DialogueDatabaseAsset).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            List<T> list = (List<T>)field.GetValue(owner);
            if (list == null)
            {
                list = new List<T>();
                field.SetValue(owner, list);
            }
            return list;
        }

        private static void EnsureFolder(string parent, string name)
        {
            if (!AssetDatabase.IsValidFolder(parent + "/" + name))
                AssetDatabase.CreateFolder(parent, name);
        }
    }
}

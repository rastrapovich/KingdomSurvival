using KingdomSurvival.UnitDatabase;
using UnityEditor;
using UnityEngine;

namespace KingdomSurvival.ProgressionDatabase.Editor
{
    // Создание «Базы развития» из значений ядра по умолчанию и синхронизация с
    // Базой существ: у Командира и каждого типа персонажа есть свой профиль.
    public static class ProgressionDatabaseBootstrap
    {
        public const string UnitDatabasePath = "Assets/_Project/UnitDatabase/Resources/UnitDatabase/KingdomSurvivalUnits.asset";

        public static ProgressionDatabaseAsset LoadOrCreate()
        {
            ProgressionDatabaseAsset database = AssetDatabase.LoadAssetAtPath<ProgressionDatabaseAsset>(ProgressionDatabaseAsset.AssetPath);
            if (database != null)
                return database;

            EnsureFolder("Assets/_Project/ProgressionDatabase", "Resources");
            EnsureFolder("Assets/_Project/ProgressionDatabase/Resources", "ProgressionDatabase");
            database = ScriptableObject.CreateInstance<ProgressionDatabaseAsset>();
            database.FillFrom(ProgressionRules.CreateDefault(), ProgressionCatalog.CreateDefault());
            AssetDatabase.CreateAsset(database, ProgressionDatabaseAsset.AssetPath);
            SyncWithUnits(database, AssetDatabase.LoadAssetAtPath<UnitDatabaseAsset>(UnitDatabasePath));
            AssetDatabase.SaveAssets();
            return database;
        }

        // Добавляет недостающие профили: Командир и все типы Базы существ.
        // Существующие профили не трогает (кроме пустого названия).
        public static bool SyncWithUnits(ProgressionDatabaseAsset database, UnitDatabaseAsset units)
        {
            if (database == null)
                return false;
            bool changed = false;

            if (database.FindProfile(ProgressionRules.HeroProfileId) == null)
            {
                database.profiles.Insert(0, ProgressionDatabaseAsset.ToRecord(ProgressionRules.CreateDefaultProfile(ProgressionRules.HeroProfileId)));
                changed = true;
            }

            if (units != null)
            {
                foreach (UnitDefinitionData unit in units.Units)
                {
                    if (unit == null || string.IsNullOrWhiteSpace(unit.Id))
                        continue;
                    ProgressionProfileRecord profile = database.FindProfile(unit.Id);
                    if (profile == null)
                    {
                        ProgressionProfile source = ProgressionRules.CreateDefaultProfile(unit.Id);
                        source.DisplayName = unit.DisplayLabel;
                        // Существа — противники: уровень задаёт их силу, опыт
                        // они не копят и в отряд не приходят.
                        if (unit.Category == UnitCategory.Creature)
                        {
                            source.Progresses = false;
                            source.StartingCompetencies.Clear();
                        }
                        database.profiles.Add(ProgressionDatabaseAsset.ToRecord(source));
                        changed = true;
                    }
                    else if (string.IsNullOrWhiteSpace(profile.displayName))
                    {
                        profile.displayName = unit.DisplayLabel;
                        changed = true;
                    }
                }
            }

            if (changed)
                EditorUtility.SetDirty(database);
            return changed;
        }

        // Для batchmode: -executeMethod KingdomSurvival.ProgressionDatabase.Editor.ProgressionDatabaseBootstrap.CreateFromCommandLine
        public static void CreateFromCommandLine()
        {
            ProgressionDatabaseAsset database = LoadOrCreate();
            SyncWithUnits(database, AssetDatabase.LoadAssetAtPath<UnitDatabaseAsset>(UnitDatabasePath));
            AssetDatabase.SaveAssets();
            Debug.Log("База развития: профилей " + database.profiles.Count + ", компетенций " + database.competencies.Count + ".");
        }

        private static void EnsureFolder(string parent, string name)
        {
            if (!AssetDatabase.IsValidFolder(parent + "/" + name))
                AssetDatabase.CreateFolder(parent, name);
        }
    }
}

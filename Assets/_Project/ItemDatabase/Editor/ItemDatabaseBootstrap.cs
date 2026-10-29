using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace KingdomSurvival.ItemDatabase.Editor
{
    // 12Т: создание «Базы предметов» и перенос исходного каталога ядра.
    // Идемпотентно: добавляются только недостающие ID; названия, цены,
    // изображения и эффекты, уже правленные автором, не перезаписываются.
    // При загрузке редактора база создаётся, если её ещё нет, — игра не
    // остаётся без каталога. Во время игры ассеты не создаются.
    [InitializeOnLoad]
    public static class ItemDatabaseBootstrap
    {
        static ItemDatabaseBootstrap()
        {
            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode)
                    return;
                LoadOrCreate();
            };
        }

        public static ItemDatabaseAsset LoadOrCreate()
        {
            ItemDatabaseAsset database = AssetDatabase.LoadAssetAtPath<ItemDatabaseAsset>(ItemDatabaseAsset.AssetPath);
            if (database == null)
            {
                EnsureFolder("Assets/_Project/ItemDatabase", "Resources");
                EnsureFolder("Assets/_Project/ItemDatabase/Resources", "ItemDatabase");
                database = ScriptableObject.CreateInstance<ItemDatabaseAsset>();
                AssetDatabase.CreateAsset(database, ItemDatabaseAsset.AssetPath);
            }
            if (AddMissingDefaults(database).Count > 0)
            {
                EditorUtility.SetDirty(database);
                AssetDatabase.SaveAssets();
            }
            return database;
        }

        // Добавляет исходные определения, которых нет в базе. Возвращает ID.
        public static List<string> AddMissingDefaults(ItemDatabaseAsset database)
        {
            List<string> added = new List<string>();
            if (database == null)
                return added;
            database.items ??= new List<ItemDatabaseEntry>();
            foreach (ItemDefinition definition in ItemCatalog.DefaultDefinitions())
            {
                if (database.Find(definition.Id) != null)
                    continue;
                database.items.Add(new ItemDatabaseEntry { Data = definition });
                added.Add(definition.Id);
            }
            return added;
        }

        private static void EnsureFolder(string parent, string name)
        {
            if (!AssetDatabase.IsValidFolder(parent + "/" + name))
                AssetDatabase.CreateFolder(parent, name);
        }
    }
}

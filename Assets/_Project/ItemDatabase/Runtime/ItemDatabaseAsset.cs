using System;
using System.Collections.Generic;
using UnityEngine;

namespace KingdomSurvival.ItemDatabase
{
    // 12Т: запись «Базы предметов» — определение вещи ядра (то же, что читает
    // игра), её изображение и заметки автора (игроку не показываются).
    [Serializable]
    public sealed class ItemDatabaseEntry
    {
        public ItemDefinition Data = new ItemDefinition();
        public Sprite Icon;
        [TextArea(2, 6)] public string AuthorNotes = string.Empty;
    }

    // «База предметов» (Kingdom Survival → База предметов): единственный
    // авторский каталог вещей игры — снаряжение, расходники, товары,
    // артефакты и сюжетные вещи. Во время игры не меняется: купленное,
    // заряды и владельцы живут в сохранении партии.
    public sealed class ItemDatabaseAsset : ScriptableObject
    {
        public const string ResourcesPath = "ItemDatabase/KingdomSurvivalItems";
        public const string AssetPath = "Assets/_Project/ItemDatabase/Resources/ItemDatabase/KingdomSurvivalItems.asset";

        public List<ItemDatabaseEntry> items = new List<ItemDatabaseEntry>();

        public ItemDatabaseEntry Find(string itemId)
        {
            if (string.IsNullOrEmpty(itemId) || items == null)
                return null;
            return items.Find(entry => entry?.Data != null && entry.Data.Id == itemId);
        }

        public Sprite IconOf(string itemId) => Find(itemId)?.Icon;

        // Каталог ядра — копии записей: правка ассета в редакторе не меняет
        // уже назначенный каталог посреди кадра.
        public ItemCatalogData ToCatalog()
        {
            List<ItemDefinition> definitions = new List<ItemDefinition>();
            if (items != null)
            {
                foreach (ItemDatabaseEntry entry in items)
                {
                    if (entry?.Data != null && !string.IsNullOrWhiteSpace(entry.Data.Id))
                        definitions.Add(entry.Data.Clone());
                }
            }
            return new ItemCatalogData(definitions);
        }
    }
}

using System.Collections.Generic;
using KingdomSurvival.ArtAssets;
using KingdomSurvival.MapLab;
using UnityEngine;

namespace KingdomSurvival.MapLabBridge
{
    // Роль рисунка Базы ассетов в декоре арены (по категории и высоте записи).
    public enum MapLabDecorRole
    {
        Tree,
        Stump,
        TallPlant,
        LowGrass,
        Boulder,
        SmallRock,
    }

    public sealed class MapLabDecorArt
    {
        public string Id;            // ID рисунка в наборе MapLab ("art:" + ID записи)
        public string AssetId;       // ID записи Базы ассетов
        public MapLabDecorRole Role;
        public float Height;         // высота карточки в мире MapLab
        public float HalfWidth;      // полуширина карточки в мире MapLab
    }

    // Единый каталог: записи Базы ассетов → рисунки MapLab по тому же ID.
    // Один PNG, одна опора; размер экземпляра задаёт потребитель (масштаб
    // scale: единиц мира MapLab на единицу мира мест). Назначение записи
    // решает, кто её берёт (UsableOnArena / UsableOnMap). Рисунок без
    // основного вида и листы кадров (анимация) пропускаются.
    public static class MapLabArtAssets
    {
        public const string IdPrefix = "art:";
        public const string BlockSuffix = "#block";

        public static MapLabArtSet BuildSet(bool arena, float scale, List<MapLabDecorArt> decor)
        {
            ArtAssetDatabaseAsset catalog = ArtAssetDatabaseAsset.Current;
            if (catalog == null || catalog.assets == null)
                return null;
            MapLabArtSet set = ScriptableObject.CreateInstance<MapLabArtSet>();
            set.name = arena ? "Арена · База ассетов" : "Карта · База ассетов";
            set.hideFlags = HideFlags.HideAndDontSave;
            foreach (ArtAssetDefinition asset in catalog.assets)
            {
                if (asset == null || (arena ? !asset.UsableOnArena : !asset.UsableOnMap) || asset.IsAnimated)
                    continue;
                ArtAssetView view = asset.PrimaryView;
                Sprite sprite = asset.MainSprite(view);
                if (sprite == null || sprite.texture == null)
                    continue;
                Rect rect = sprite.rect;
                // Лист нескольких рисунков MapLab карточкой не покажет.
                if (Mathf.Abs(rect.width - sprite.texture.width) > 1f || Mathf.Abs(rect.height - sprite.texture.height) > 1f)
                    continue;
                MapLabDecorRole? role = RoleOf(asset);
                // Фигуры бойцов на арене крупнее людей мест: мелочь (трава, камешки,
                // бурьян) увеличивается, иначе рядом с ними теряется.
                float boost = !arena || role == null ? 1f
                    : role == MapLabDecorRole.LowGrass ? 2.2f
                    : role == MapLabDecorRole.SmallRock ? 2f
                    : role == MapLabDecorRole.TallPlant ? 1.5f : 1f;
                float height = rect.height / asset.SafePixelsPerUnit * scale * boost;
                Vector2 pivot = asset.Settings(view).Pivot;
                MapLabArtEntry entry = Entry(IdPrefix + asset.Id, asset, sprite, height, pivot);
                set.Entries.Add(entry);
                if (decor == null || role == null)
                    continue;
                decor.Add(new MapLabDecorArt
                {
                    Id = entry.Id, AssetId = asset.Id, Role = role.Value, Height = height,
                    HalfWidth = height * rect.width / Mathf.Max(1f, rect.height) * 0.5f,
                });
            }
            return set;
        }

        // Копия рисунка с заданной высотой карточки (валун на клетке — во всю клетку).
        public static MapLabArtEntry AddSized(MapLabArtSet set, MapLabDecorArt art, string suffix, float height)
        {
            MapLabArtEntry source = set.Find(art.Id);
            if (source == null)
                return null;
            string id = art.Id + suffix;
            MapLabArtEntry existing = set.Find(id);
            if (existing != null)
                return existing;
            MapLabArtEntry copy = JsonUtility.FromJson<MapLabArtEntry>(JsonUtility.ToJson(source));
            copy.Id = id;
            copy.Texture = source.Texture;
            copy.WorldHeight = height;
            set.Entries.Add(copy);
            return copy;
        }

        private static MapLabArtEntry Entry(string id, ArtAssetDefinition asset, Sprite sprite, float height, Vector2 pivot)
        {
            return new MapLabArtEntry
            {
                Id = id,
                Category = asset.Category == ArtAssetCategory.Rocks ? MapLabArtCategory.Rock
                    : asset.Category == ArtAssetCategory.Items ? MapLabArtCategory.Prop
                    : MapLabArtCategory.Vegetation,
                Texture = sprite.texture,
                Width = Mathf.RoundToInt(sprite.rect.width),
                Height = Mathf.RoundToInt(sprite.rect.height),
                WorldHeight = height,
                Pivot = pivot,
                PivotHint = pivot,
                ScaleRange = Vector2.one,
                SolidRect = new Rect(0f, 0f, 1f, 1f),
                Light = MapLabArtLight.BakedInImage,
                Enabled = true,
                Weight = 1f,
                SizeScale = 1f,
                HeightCorrection = 1f,
                Provenance = "База ассетов: " + asset.Name,
            };
        }

        // Роль по категории и игровому размеру записи (единицы мира мест,
        // человек ≈ 1,7): низкое дерево — пень, высокое растение — бурьян.
        // Теги пакета (ID рисунка) уточняют роль: высота записи — по холсту,
        // а не по силуэту, поэтому по ней низкое от высокого отличается грубо.
        private static readonly HashSet<string> TallPlants = new HashSet<string>
            { "Reedgrass", "Fireweed", "Nettle", "Burdock", "Fern", "Cattail", "TallCattail", "Raspberry" };
        private static readonly HashSet<string> SmallRocks = new HashSet<string> { "SmallRock", "GroundCobble" };

        public static MapLabDecorRole? RoleOf(ArtAssetDefinition asset)
        {
            if (asset.Tags != null)
                foreach (string tag in asset.Tags)
                {
                    if (string.IsNullOrEmpty(tag)) continue;
                    if (tag.EndsWith("Stump")) return MapLabDecorRole.Stump;
                    if (TallPlants.Contains(tag)) return MapLabDecorRole.TallPlant;
                    if (SmallRocks.Contains(tag)) return MapLabDecorRole.SmallRock;
                }
            float height = asset.Height;
            switch (asset.Category)
            {
                case ArtAssetCategory.Trees: return height < 1.5f ? MapLabDecorRole.Stump : MapLabDecorRole.Tree;
                case ArtAssetCategory.Vegetation: return height < 0.75f ? MapLabDecorRole.LowGrass : MapLabDecorRole.TallPlant;
                case ArtAssetCategory.Rocks: return height < 0.9f ? MapLabDecorRole.SmallRock : MapLabDecorRole.Boulder;
                default: return null;
            }
        }
    }
}

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
        // Объект местности арены (тег «arena-object:<вид>»): ставится только на
        // клетки этого объекта, в общий декор не идёт.
        ArenaObject,
        // Высокая луговая трава пакета «Grass_*» (от 0,75 м): не бурьян у деревьев,
        // а трава пятен и россыпи арены. Значения сериализуются — только в конец.
        TallGrass,
    }

    public sealed class MapLabDecorArt
    {
        public string Id;            // ID рисунка в наборе MapLab ("art:" + ID записи)
        public string AssetId;       // ID записи Базы ассетов
        public MapLabDecorRole Role;
        public float Height;         // высота карточки в мире MapLab
        public float HalfWidth;      // полуширина карточки в мире MapLab
        public string ObjectKind;    // вид объекта местности арены (для ArenaObject)
        public bool Pack;            // трава пакета «Grass_*» или «MapGrass_*» (трава карты в Базе)
        public bool Ink;             // контурный (тушевой) пучок травы карты
        public float Priority = 1f;  // приоритет записи 0–10 (вес выбора среди своей роли)
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
        public const string ObjectSuffix = "#object";
        // Слот рисунка объекта местности арены: тег записи Базы ассетов
        // «arena-object:<вид>» (wattle, stone_wall, fallen_log, deadfall, trunk,
        // bridge, root_nest, ritual_stone). Нет такой записи — объект рисует
        // доска программно.
        public const string ArenaObjectTagPrefix = "arena-object:";

        public static string ArenaObjectKindOf(ArtAssetDefinition asset)
        {
            if (asset?.Tags == null)
                return null;
            foreach (string tag in asset.Tags)
                if (tag != null && tag.StartsWith(ArenaObjectTagPrefix))
                    return tag.Substring(ArenaObjectTagPrefix.Length).Trim();
            return null;
        }

        // Рисунок слота для слоя доски (разрушаемые объекты): pick выбирает
        // вариант, рисунок — в текущем виде карты (свой PNG вида, если есть).
        // null — записи нет.
        public static Texture2D ArenaObjectTexture(string kind, int pick, out Vector2 pivot)
        {
            pivot = new Vector2(0.5f, 0f);
            ArtAssetDatabaseAsset catalog = ArtAssetDatabaseAsset.Current;
            if (catalog?.assets == null || string.IsNullOrEmpty(kind))
                return null;
            List<ArtAssetDefinition> found = new List<ArtAssetDefinition>();
            foreach (ArtAssetDefinition asset in catalog.assets)
                if (asset != null && asset.UsableOnArena && !asset.IsAnimated && ArenaObjectKindOf(asset) == kind && asset.MainSprite(asset.PrimaryView) != null)
                    found.Add(asset);
            if (found.Count == 0)
                return null;
            found.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
            ArtAssetDefinition chosen = found[((pick % found.Count) + found.Count) % found.Count];
            ArtAssetView view = chosen.PrimaryView;
            pivot = chosen.Settings(view).Pivot;
            Texture2D own = MapLabMapViews.Active?.FindTexture(IdPrefix + chosen.Id);
            return own != null ? own : chosen.MainSprite(view).texture;
        }

        // Рисунок декора арены по роли (бурьян, дерево…) — для слоя доски
        // (кустарник и лес клетки: могут выгореть в бою). Выбор устойчив по pick.
        public static Texture2D ArenaDecorTexture(MapLabDecorRole role, int pick, out Vector2 pivot)
        {
            pivot = new Vector2(0.5f, 0f);
            ArtAssetDatabaseAsset catalog = ArtAssetDatabaseAsset.Current;
            if (catalog?.assets == null)
                return null;
            List<ArtAssetDefinition> found = new List<ArtAssetDefinition>();
            foreach (ArtAssetDefinition asset in catalog.assets)
            {
                if (asset == null || !asset.UsableOnArena || asset.IsAnimated || ArenaObjectKindOf(asset) != null || RoleOf(asset) != role || asset.ArenaPriority <= 0f)
                    continue;
                Sprite sprite = asset.MainSprite(asset.PrimaryView);
                if (sprite == null || sprite.texture == null || Mathf.Abs(sprite.rect.width - sprite.texture.width) > 1f || Mathf.Abs(sprite.rect.height - sprite.texture.height) > 1f)
                    continue;
                found.Add(asset);
            }
            if (found.Count == 0)
                return null;
            found.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
            // Выбор по приоритету записи (вес — квадрат), устойчивый по pick.
            float totalWeight = 0f;
            foreach (ArtAssetDefinition candidate in found)
                totalWeight += ArenaWeight(candidate.ArenaPriority);
            float at = (Mathf.Abs(pick) % 997) / 997f * totalWeight;
            ArtAssetDefinition chosen = found[found.Count - 1];
            foreach (ArtAssetDefinition candidate in found)
            {
                at -= ArenaWeight(candidate.ArenaPriority);
                if (at < 0f) { chosen = candidate; break; }
            }
            ArtAssetView view = chosen.PrimaryView;
            pivot = chosen.Settings(view).Pivot;
            Texture2D own = MapLabMapViews.Active?.FindTexture(IdPrefix + chosen.Id);
            return own != null ? own : chosen.MainSprite(view).texture;
        }

        public static bool HasArenaObjectArt(string kind)
        {
            ArtAssetDatabaseAsset catalog = ArtAssetDatabaseAsset.Current;
            if (catalog?.assets == null || string.IsNullOrEmpty(kind))
                return false;
            foreach (ArtAssetDefinition asset in catalog.assets)
                if (asset != null && asset.UsableOnArena && !asset.IsAnimated && ArenaObjectKindOf(asset) == kind && asset.MainSprite(asset.PrimaryView) != null)
                    return true;
            return false;
        }

        public static MapLabArtSet BuildSet(bool arena, float scale, List<MapLabDecorArt> decor, System.Func<MapLabDecorRole, float> roleScale = null)
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
                string objectKind = arena ? ArenaObjectKindOf(asset) : null;
                MapLabDecorRole? role = objectKind != null ? MapLabDecorRole.ArenaObject : RoleOf(asset);
                // Фигуры бойцов на арене крупнее людей мест: мелочь (трава, камешки,
                // бурьян) увеличивается, иначе рядом с ними теряется.
                float boost = !arena || role == null ? 1f
                    : role == MapLabDecorRole.LowGrass ? 2.2f
                    : role == MapLabDecorRole.SmallRock ? 2f
                    : role == MapLabDecorRole.TallPlant || role == MapLabDecorRole.TallGrass ? 1.5f : 1f;
                // Размер по роли из «Генератора арен» (деревья, трава, бурьян).
                if (arena && role != null && roleScale != null)
                    boost *= Mathf.Max(0.05f, roleScale(role.Value));
                float height = rect.height / asset.SafePixelsPerUnit * scale * boost;
                Vector2 pivot = asset.Settings(view).Pivot;
                MapLabArtEntry entry = Entry(IdPrefix + asset.Id, asset, sprite, height, pivot);
                entry.WalkBend = asset.WalkBend >= 0f ? asset.WalkBend
                    : role == MapLabDecorRole.LowGrass || role == MapLabDecorRole.TallPlant || role == MapLabDecorRole.TallGrass ? 1f : 0f;
                entry.NoCastShadow = HasTagPrefix(asset, MapGrassInkPrefix);
                set.Entries.Add(entry);
                if (decor == null || role == null)
                    continue;
                decor.Add(new MapLabDecorArt
                {
                    Id = entry.Id, AssetId = asset.Id, Role = role.Value, Height = height,
                    HalfWidth = height * rect.width / Mathf.Max(1f, rect.height) * 0.5f,
                    ObjectKind = objectKind,
                    Pack = IsGrassPack(asset),
                    Ink = HasTagPrefix(asset, MapGrassInkPrefix),
                    Priority = Mathf.Clamp(asset.ArenaPriority, 0f, ArtAssetDefinition.MaxArenaPriority),
                });
            }
            return set;
        }

        // Лес карты мира: прежние породы набора baseSet остаются, к ним
        // добавляются деревья Базы ассетов с назначением «Карта и арена» или
        // «Только карта» — автор подключает нужные, меняя назначение в
        // карточке. scale — единиц мира карты на единицу мира мест (взрослое
        // дерево ≈ избе). Таких деревьев нет — null (набор карты как был).
        public static MapLabArtSet BuildMapForestSet(MapLabArtSet baseSet, float scale, out int deciduous, out int conifers)
        {
            deciduous = conifers = 0;
            if (baseSet == null)
                return null;
            List<MapLabDecorArt> decor = new List<MapLabDecorArt>();
            MapLabArtSet art = BuildSet(false, scale, decor);
            if (art == null)
                return null;
            List<MapLabArtEntry> trees = new List<MapLabArtEntry>();
            foreach (MapLabDecorArt item in decor)
            {
                if (item.Role != MapLabDecorRole.Tree)
                    continue;
                MapLabArtEntry entry = art.Find(item.Id);
                ArtAssetDefinition asset = ArtAssetDatabaseAsset.FindCurrent(item.AssetId);
                if (entry == null || asset == null)
                    continue;
                bool conifer = IsConifer(asset);
                entry.Kind = conifer ? MapLabObjectKind.Conifer : MapLabObjectKind.Deciduous;
                entry.Category = MapLabArtCategory.Vegetation;
                // Тот же разброс размера, что у прежних пород карты.
                entry.ScaleRange = new Vector2(0.85f, 1.12f);
                trees.Add(entry);
                if (conifer) conifers++; else deciduous++;
            }
            if (trees.Count == 0)
            {
                Object.DestroyImmediate(art);
                deciduous = conifers = 0;
                return null;
            }
            MapLabArtSet set = ScriptableObject.CreateInstance<MapLabArtSet>();
            set.name = "Карта · лес с деревьями Базы ассетов";
            set.hideFlags = HideFlags.HideAndDontSave;
            foreach (MapLabArtEntry entry in baseSet.Entries)
                if (entry != null)
                    set.Entries.Add(entry);
            set.Entries.AddRange(trees);
            Object.DestroyImmediate(art);
            return set;
        }

        private static readonly string[] ConiferWords = { "Spruce", "Pine", "Fir", "Ель", "ель", "Сосн", "сосн", "Пихт", "пихт" };

        public static bool IsConifer(ArtAssetDefinition asset)
        {
            foreach (string word in ConiferWords)
            {
                if (!string.IsNullOrEmpty(asset.Name) && asset.Name.Contains(word)) return true;
                if (asset.Tags != null && asset.Tags.Exists(tag => tag != null && tag.Contains(word))) return true;
            }
            return false;
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

        // Вес выбора по приоритету 0–10 — квадрат: 0,1 → 0,01, 1 → 1, 10 → 100 (разрыв
        // между редким и главным в 10 000 раз). 1 у всех — равные шансы, как раньше.
        public static float ArenaWeight(float priority) => Mathf.Max(0f, priority) * Mathf.Max(0f, priority);

        // Выбор рисунка по приоритету записи (вес — ArenaWeight; 1 у всех — равные шансы,
        // как до появления приоритета). roll — случайное число [0;1).
        public static MapLabDecorArt Pick(List<MapLabDecorArt> pool, float roll)
        {
            float total = 0f;
            foreach (MapLabDecorArt art in pool)
                total += ArenaWeight(art.Priority);
            if (total <= 0f)
                return pool[(int)(roll * pool.Count) % pool.Count];
            float at = Mathf.Clamp01(roll) * total;
            foreach (MapLabDecorArt art in pool)
            {
                at -= ArenaWeight(art.Priority);
                if (at < 0f)
                    return art;
            }
            return pool[pool.Count - 1];
        }

        // То же для рисунков набора: копии ковра пакета («#carpet») несут приоритет
        // в Weight, остальные берутся поровну.
        public static MapLabArtEntry PickEntry(List<MapLabArtEntry> pool, float roll)
        {
            float Weight(MapLabArtEntry e) => e.Id != null && e.Id.EndsWith("#carpet") ? ArenaWeight(e.Weight) : 1f;
            float total = 0f;
            foreach (MapLabArtEntry e in pool)
                total += Weight(e);
            if (total <= 0f)
                return pool[(int)(roll * pool.Count) % pool.Count];
            float at = Mathf.Clamp01(roll) * total;
            foreach (MapLabArtEntry e in pool)
            {
                at -= Weight(e);
                if (at < 0f)
                    return e;
            }
            return pool[pool.Count - 1];
        }

        // Трава пакета «Grass_*» (тег — ID варианта поставки). Высота записи
        // решает только низкая она или высокая; высокая — не бурьян у деревьев.
        public const string GrassPackPrefix = "Grass_";
        // Трава глобальной карты (набор этапа 05), перенесённая в Базу ассетов: «MapGrass_*».
        public const string MapGrassPrefix = "MapGrass_";
        public const string MapGrassInkPrefix = "MapGrass_Ink";

        public static bool HasTagPrefix(ArtAssetDefinition asset, string prefix)
        {
            if (asset?.Tags == null)
                return false;
            foreach (string tag in asset.Tags)
                if (tag != null && tag.StartsWith(prefix))
                    return true;
            return false;
        }

        public static bool IsGrassPack(ArtAssetDefinition asset)
        {
            if (asset?.Tags == null)
                return false;
            foreach (string tag in asset.Tags)
                if (tag != null && (tag.StartsWith(GrassPackPrefix) || tag.StartsWith(MapGrassPrefix)))
                    return true;
            return false;
        }

        public static MapLabDecorRole? RoleOf(ArtAssetDefinition asset)
        {
            // Трава карты («MapGrass_*») — всегда низкая: высота записи считается по холсту
            // с большими полями вокруг тонких штрихов и вышла бы «высокой».
            if (HasTagPrefix(asset, MapGrassPrefix))
                return MapLabDecorRole.LowGrass;
            if (IsGrassPack(asset))
                return asset.Height < 0.75f ? MapLabDecorRole.LowGrass : MapLabDecorRole.TallGrass;
            if (asset.Tags != null)
                foreach (string tag in asset.Tags)
                {
                    if (string.IsNullOrEmpty(tag)) continue;
                    if (tag.EndsWith("Stump")) return MapLabDecorRole.Stump;
                }
            if (!string.IsNullOrEmpty(asset.Name) && (asset.Name.Contains("пень") || asset.Name.Contains("Пень")))
                return MapLabDecorRole.Stump;
            if (asset.Tags != null)
                foreach (string tag in asset.Tags)
                {
                    if (string.IsNullOrEmpty(tag)) continue;
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

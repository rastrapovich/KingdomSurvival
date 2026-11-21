using System.Collections.Generic;
using KingdomSurvival.BattlefieldDatabase;
using KingdomSurvival.MapLabBridge;
using UnityEngine;
using UnityEngine.UIElements;

namespace KingdomSurvival.BattleSandbox
{
    // Бриф «Поверхности арены»: разрушаемые объекты местности (плетень)
    // рисуются слоем доски, а не кадром MapLab-арены — кадр при разрушении не
    // перестраивается. Целый объект — рисунок слота arena-object:<вид>,
    // разрушенный — arena-object:<вид>_broken (обломки, по ним ходят).
    // Картинки сортируются по глубине вместе с фигурами; рисунок — в текущем
    // виде карты. Нет записи в Базе ассетов — остаётся программный значок.
    public sealed partial class HexBoardElement
    {
        // Ширина рисунка в ширинах клетки (холст с полями по краям).
        private const float BoardArtCellWidths = 1.05f;

        private readonly Dictionary<string, Image> terrainArtImages = new Dictionary<string, Image>();
        private readonly Dictionary<string, (Texture2D Texture, Vector2 Pivot)> terrainArtCache = new Dictionary<string, (Texture2D, Vector2)>();
        private string terrainArtCacheView;

        // Вид разрушаемого объекта, рисуемый доской (null — не этим слоем).
        private static string BoardArtKind(SandboxBattleObject item)
        {
            if (item == null || !item.IsTerrain || SandboxTerrainObjects.Get(item.Kind).Indestructible)
                return null;
            string kind = MapLabArenaDriver.ObjectKindId(item.Kind);
            return item.IsDestroyed ? kind + "_broken" : kind;
        }

        // Рисунки — в текущем виде карты: смена вида сбрасывает кэш.
        private void EnsureArtCacheView()
        {
            string view = KingdomSurvival.MapLab.MapLabMapViews.ActiveId;
            if (terrainArtCacheView != view)
            {
                terrainArtCache.Clear();
                terrainArtCacheView = view;
            }
        }

        private (Texture2D Texture, Vector2 Pivot) BoardArt(string kind, HexCoord cell)
        {
            EnsureArtCacheView();
            int pick = Mathf.Abs(cell.Q * 73856093 ^ cell.R * 19349663);
            string key = kind + "@" + pick % 7;
            if (!terrainArtCache.TryGetValue(key, out (Texture2D, Vector2) art))
            {
                Texture2D texture = MapLabArtAssets.ArenaObjectTexture(kind, pick, out Vector2 pivot);
                terrainArtCache[key] = art = (texture, pivot);
            }
            return art;
        }

        // Программный значок не нужен: объект нарисован слоем доски.
        private bool HasBoardArt(SandboxBattleObject item, HexCoord cell)
        {
            string kind = BoardArtKind(item);
            return kind != null && BoardArt(kind, cell).Texture != null;
        }

        // 20.11.2026: верхний слой арены MapLab — стоящие объекты (деревья,
        // камни, стенки) на прозрачном фоне. Лежит над рисунком доски (сетка,
        // отметки, заливки), но под фигурами и картинками объектов доски:
        // линии клеток уходят под деревья. null — слоя нет.
        private Image terrainOverlay;
        private Texture terrainOverlayTexture;

        public void SetTerrainOverlay(Texture texture)
        {
            if (terrainOverlayTexture == texture)
                return;
            terrainOverlayTexture = texture;
            SyncUnitImages();
        }

        // Слои «впереди бойца» (арена): объекты, стоящие ближе к зрителю, чем ноги бойца,
        // ложатся поверх него (и поверх более дальних), но под более близких бойцов.
        private readonly List<(float KeyY, float ShareY)> frontRows = new List<(float, float)>();
        private readonly List<(float KeyY, Texture Texture)> frontLayers = new List<(float, Texture)>();
        private readonly List<Image> frontImages = new List<Image>();

        // Строки земли, где стоят живые бойцы: ключ порядка слоёв и доля кадра (Y вниз, плоская).
        public IReadOnlyList<(float KeyY, float ShareY)> FrontRows => frontRows;

        public void SetFrontLayers(IReadOnlyList<(float KeyY, Texture Texture)> layers)
        {
            frontLayers.Clear();
            if (layers != null) frontLayers.AddRange(layers);
            SyncUnitImages();
        }

        private void NoteFrontRow(HexLayout layout, Vector2 unitCenter)
        {
            Rect frame = layout.Frame;
            if (Relief == null || frame.height < 1f) return;
            Vector2 flat = BattlefieldReliefMath.Lower(Relief, frame, unitCenter);
            frontRows.Add((unitCenter.y, (flat.y - frame.y) / frame.height));
        }

        private void AddFrontLayers(HexLayout layout, List<(string id, float key, VisualElement element)> depthEntries)
        {
            int used = 0;
            if (Relief != null && terrainOverlayTexture != null)
            {
                Rect frame = layout.Frame;
                foreach ((float keyY, Texture texture) in frontLayers)
                {
                    if (texture == null) continue;
                    while (frontImages.Count <= used)
                    {
                        Image created = new Image { pickingMode = PickingMode.Ignore, scaleMode = ScaleMode.StretchToFill };
                        created.style.position = Position.Absolute;
                        frontImages.Add(created);
                    }
                    Image image = frontImages[used];
                    if (image.parent != this) Add(image);
                    image.image = texture;
                    image.style.display = DisplayStyle.Flex;
                    image.style.left = frame.x;
                    image.style.top = frame.y;
                    image.style.width = frame.width;
                    image.style.height = frame.height;
                    depthEntries.Add(("front:" + used, keyY + 0.25f, image));
                    used++;
                }
            }
            for (int i = used; i < frontImages.Count; i++)
                frontImages[i].RemoveFromHierarchy();
        }

        private void SyncTerrainOverlay(HexLayout layout)
        {
            if (terrainOverlayTexture == null || Relief == null)
            {
                terrainOverlay?.RemoveFromHierarchy();
                terrainOverlay = null;
                return;
            }
            if (terrainOverlay == null)
            {
                terrainOverlay = new Image { name = "arena-objects-overlay", pickingMode = PickingMode.Ignore, scaleMode = ScaleMode.StretchToFill };
                terrainOverlay.style.position = Position.Absolute;
            }
            if (terrainOverlay.parent != this)
                Add(terrainOverlay);
            terrainOverlay.image = terrainOverlayTexture;
            Rect frame = layout.Frame;
            terrainOverlay.style.left = frame.x;
            terrainOverlay.style.top = frame.y;
            terrainOverlay.style.width = frame.width;
            terrainOverlay.style.height = frame.height;
            // Первым ребёнком: всё, что рисует сама доска, — под ним; фигуры — над ним.
            terrainOverlay.SendToBack();
        }

        // Поверхности клеток, уже нарисованные в краске земли арены (слои
        // MapLabArenaSurfaces.Bit): у них программная заливка не рисуется.
        private IReadOnlyDictionary<HexCoord, ulong> paintedSurfaces;

        public IReadOnlyDictionary<HexCoord, ulong> PaintedSurfaces
        {
            get => paintedSurfaces;
            set
            {
                paintedSurfaces = value;
                MarkDirtyRepaint();
            }
        }

        private bool Painted(HexCoord cell, MapLabArenaSurface surface) =>
            Relief != null && paintedSurfaces != null && paintedSurfaces.TryGetValue(cell, out ulong layers) &&
            (layers & MapLabArenaSurfaces.Bit(surface)) != 0UL;

        private bool PaintedGround(HexCoord cell, SandboxGround ground)
        {
            MapLabArenaSurface surface = MapLabArenaDriver.GroundSurface(ground);
            return surface != 0 && Painted(cell, surface);
        }

        private bool PaintedEffect(HexCoord cell, SandboxEffectKind kind)
        {
            MapLabArenaSurface surface = MapLabArenaDriver.EffectSurface(kind);
            return surface != 0 && Painted(cell, surface);
        }

        private (Texture2D Texture, Vector2 Pivot) VegetationArt(HexCoord cell, SandboxVegetation vegetation)
        {
            MapLabDecorRole role = vegetation == SandboxVegetation.Forest ? MapLabDecorRole.Tree : MapLabDecorRole.TallPlant;
            EnsureArtCacheView();
            int pick = Mathf.Abs(cell.Q * 92821 ^ cell.R * 68917);
            string key = "veg:" + role + "@" + pick % 7;
            if (!terrainArtCache.TryGetValue(key, out (Texture2D, Vector2) art))
            {
                Texture2D texture = MapLabArtAssets.ArenaDecorTexture(role, pick, out Vector2 pivot);
                terrainArtCache[key] = art = (texture, pivot);
            }
            return art;
        }

        // Растительность клетки нарисована рисунком (программный значок не нужен).
        private bool HasVegetationArt(HexCoord cell, SandboxVegetation vegetation) =>
            (vegetation == SandboxVegetation.Forest || vegetation == SandboxVegetation.Brush) && VegetationArt(cell, vegetation).Texture != null;

        private void SyncTerrainObjectImages(HexLayout layout, List<(string id, float key, VisualElement element)> depthEntries)
        {
            HashSet<string> visible = new HashSet<string>();
            if (battle != null)
            {
                float width = layout.Size * Mathf.Sqrt(3f) * BoardArtCellWidths;
                foreach (SandboxBattleObject item in battle.Objects)
                {
                    string kind = BoardArtKind(item);
                    if (kind == null)
                        continue;
                    foreach (HexCoord cell in item.Cells)
                    {
                        if (!IsBoardCell(cell))
                            continue;
                        (Texture2D texture, Vector2 pivot) = BoardArt(kind, cell);
                        if (texture == null)
                            continue;
                        string id = "terrain-art:" + item.Id + ":" + cell;
                        visible.Add(id);
                        if (!terrainArtImages.TryGetValue(id, out Image image))
                        {
                            image = new Image { pickingMode = PickingMode.Ignore, scaleMode = ScaleMode.StretchToFill };
                            image.style.position = Position.Absolute;
                            terrainArtImages[id] = image;
                            Add(image);
                        }
                        float height = width * texture.height / Mathf.Max(1f, texture.width);
                        Vector2 ground = layout.GetCenter(cell);
                        image.image = texture;
                        image.style.width = width;
                        image.style.height = height;
                        image.style.left = ground.x - width * pivot.x;
                        image.style.top = ground.y - height * (1f - pivot.y);
                        image.style.display = DisplayStyle.Flex;
                        // Чуть позади фигуры той же клетки (по обломкам ходят).
                        depthEntries.Add((id, ground.y - 0.5f, image));
                    }
                }
                // Кустарник и лес клетки — рисунками Базы ассетов у задней
                // кромки клетки (боец в середине клетки виден); выгорел —
                // рисунок снимается.
                foreach (KeyValuePair<HexCoord, SandboxVegetation> pair in battle.VegetationCells)
                {
                    if (!IsBoardCell(pair.Key) || !HasVegetationArt(pair.Key, pair.Value))
                        continue;
                    bool forest = pair.Value == SandboxVegetation.Forest;
                    (Texture2D texture, Vector2 pivot) = VegetationArt(pair.Key, pair.Value);
                    float w = layout.Size * Mathf.Sqrt(3f) * (forest ? 0.95f : 0.62f);
                    float h = w * texture.height / Mathf.Max(1f, texture.width);
                    Vector2 ground = layout.GetCenter(pair.Key) + new Vector2((forest ? -0.28f : 0.3f) * layout.Size, -0.38f * layout.Size * layout.VerticalScale);
                    string id = "terrain-veg:" + pair.Key;
                    visible.Add(id);
                    if (!terrainArtImages.TryGetValue(id, out Image image))
                    {
                        image = new Image { pickingMode = PickingMode.Ignore, scaleMode = ScaleMode.StretchToFill };
                        image.style.position = Position.Absolute;
                        terrainArtImages[id] = image;
                        Add(image);
                    }
                    image.image = texture;
                    image.style.width = w;
                    image.style.height = h;
                    image.style.left = ground.x - w * pivot.x;
                    image.style.top = ground.y - h * (1f - pivot.y);
                    image.style.display = DisplayStyle.Flex;
                    depthEntries.Add((id, ground.y, image));
                }
            }
            List<string> stale = null;
            foreach (KeyValuePair<string, Image> pair in terrainArtImages)
            {
                if (visible.Contains(pair.Key))
                    continue;
                (stale ??= new List<string>()).Add(pair.Key);
                pair.Value.RemoveFromHierarchy();
            }
            if (stale != null)
                foreach (string id in stale)
                    terrainArtImages.Remove(id);
        }
    }
}

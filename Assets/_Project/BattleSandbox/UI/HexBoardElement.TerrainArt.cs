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

        // Вид разрушаемого объекта, рисуемый доской (null — не этим слоем).
        private static string BoardArtKind(SandboxBattleObject item)
        {
            if (item == null || !item.IsTerrain || SandboxTerrainObjects.Get(item.Kind).Indestructible)
                return null;
            string kind = MapLabArenaDriver.ObjectKindId(item.Kind);
            return item.IsDestroyed ? kind + "_broken" : kind;
        }

        private (Texture2D Texture, Vector2 Pivot) BoardArt(string kind, HexCoord cell)
        {
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

        // Тени бойцов на арене: тот же рисунок бойца, опрокинутый и наклонённый на землю по свету —
        // силуэт, а не пятно; цвет и плотность те же, что у теней растений (проход шейдера карточек).
        private sealed class UnitShadow { public VisualElement Box; public Image Picture; }
        private readonly Dictionary<string, UnitShadow> unitShadows = new Dictionary<string, UnitShadow>();
        private readonly HashSet<string> shadowSeen = new HashSet<string>();

        // Куда падает тень на экране (единичный вектор, Y вниз); задаёт арена.
        public Vector2 ShadowDirection { get; set; } = new Vector2(1f, 0.1f);
        // Оси тени на экране в долях рисунка бойца (Y вниз): «верх» рисунка ложится
        // вдоль Along, его правая сторона — вдоль Across. Те же оси, что у силуэтных
        // теней кустов и деревьев арены (MapLabArena.ShadowScreenAxes).
        public Vector2 ShadowAlong { get; set; } = new Vector2(0.4f, -0.2f);
        public Vector2 ShadowAcross { get; set; } = Vector2.right;

        private bool UnitShadowsOn => Relief != null && terrainOverlayTexture != null;

        private void SyncUnitShadow(SandboxUnitState unit, Image image, Vector2 ground, bool corpse, List<(string id, float key, VisualElement element)> depthEntries)
        {
            if (!UnitShadowsOn || corpse || image == null || image.sprite == null) return;
            if (!unitShadows.TryGetValue(unit.Id, out UnitShadow shadow))
            {
                shadow = new UnitShadow { Box = new VisualElement { pickingMode = PickingMode.Ignore }, Picture = new Image { pickingMode = PickingMode.Ignore } };
                shadow.Box.style.position = Position.Absolute;
                shadow.Picture.style.position = Position.Absolute;
                shadow.Picture.style.left = shadow.Picture.style.top = 0f;
                shadow.Picture.style.width = Length.Percent(100);
                shadow.Picture.style.height = Length.Percent(100);
                shadow.Picture.tintColor = new Color(0.07f, 0.09f, 0.03f, 1f);
                shadow.Box.Add(shadow.Picture);
                unitShadows[unit.Id] = shadow;
            }
            shadowSeen.Add(unit.Id);
            if (shadow.Box.parent != this) Add(shadow.Box);
            float left = image.style.left.value.value, top = image.style.top.value.value;
            shadow.Box.style.left = left;
            shadow.Box.style.top = top;
            shadow.Box.style.width = image.style.width.value.value;
            shadow.Box.style.height = image.style.height.value.value;
            shadow.Box.style.opacity = image.resolvedStyle.opacity * KingdomSurvival.MapLabBridge.MapLabArena.SilhouetteShadowOpacity;
            // Ноги — неподвижная точка. Тень — сдвиг рисунка, как у кустов и деревьев
            // арены: правая сторона рисунка остаётся вдоль кадра (Across), верх уходит
            // по свету (Along). Линейная карта M (столбцы: M·(1,0) = Across,
            // M·(0,1) = −Along) раскладывается в поворот·масштаб·поворот: внешний
            // поворот и масштаб — у рамки, внутренний поворот — у картинки (UI Toolkit
            // применяет масштаб до поворота, иначе сдвиг не собрать).
            Vector2 origin = new Vector2(ground.x - left, ground.y - top);
            shadow.Box.style.transformOrigin = new TransformOrigin(origin.x, origin.y, 0f);
            shadow.Picture.style.transformOrigin = new TransformOrigin(origin.x, origin.y, 0f);
            Vector2 along = ShadowAlong.sqrMagnitude > 1e-6f ? ShadowAlong : new Vector2(0.4f, -0.2f);
            Vector2 across = ShadowAcross.sqrMagnitude > 1e-6f ? ShadowAcross : Vector2.right;
            float m00 = across.x, m01 = -along.x, m10 = across.y, m11 = -along.y;
            float e = (m00 + m11) * 0.5f, f = (m00 - m11) * 0.5f, g = (m10 + m01) * 0.5f, h = (m10 - m01) * 0.5f;
            float q = Mathf.Sqrt(e * e + h * h), r = Mathf.Sqrt(f * f + g * g);
            float a1 = Mathf.Atan2(g, f), a2 = Mathf.Atan2(h, e);
            float outer = (a2 + a1) * 0.5f * Mathf.Rad2Deg, inner = (a2 - a1) * 0.5f * Mathf.Rad2Deg;
            float sx = q + r, sy = q - r;
            if (Mathf.Abs(sy) < 0.02f) sy = sy < 0f ? -0.02f : 0.02f;
            shadow.Box.style.rotate = new Rotate(new Angle(outer, AngleUnit.Degree));
            shadow.Box.style.scale = new Scale(new Vector3(sx, sy, 1f));
            shadow.Picture.style.rotate = new Rotate(new Angle(inner, AngleUnit.Degree));
            shadow.Picture.sprite = image.sprite;
            shadow.Picture.scaleMode = image.scaleMode;
            Vector3 flip = image.style.scale.value.value;
            shadow.Picture.style.scale = new Scale(new Vector3(flip.x < 0f ? -1f : 1f, 1f, 1f));
            shadow.Box.style.display = image.style.display.value == DisplayStyle.None ? DisplayStyle.None : DisplayStyle.Flex;
            depthEntries.Add(("shadow:" + unit.Id, ground.y - 0.4f, shadow.Box));
        }

        private void FinishUnitShadows()
        {
            List<string> gone = null;
            foreach (KeyValuePair<string, UnitShadow> pair in unitShadows)
                if (!shadowSeen.Contains(pair.Key))
                {
                    pair.Value.Box.RemoveFromHierarchy();
                    (gone ??= new List<string>()).Add(pair.Key);
                }
            if (gone != null) foreach (string id in gone) unitShadows.Remove(id);
            shadowSeen.Clear();
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

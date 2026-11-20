using System.Collections.Generic;
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

        private (Texture2D Texture, Vector2 Pivot) BoardArt(string kind, HexCoord cell)
        {
            string view = KingdomSurvival.MapLab.MapLabMapViews.ActiveId;
            if (terrainArtCacheView != view)
            {
                terrainArtCache.Clear();
                terrainArtCacheView = view;
            }
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

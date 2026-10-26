using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace KingdomSurvival.WorldMapVisual
{
    // Рисунки художника (деревья, камыш, горы, деревни, знаки мест), которые
    // не впекаются в рисунок карты, а показываются отдельным слоем поверх
    // него — видеокартой, в разрешении самих рисунков и с mip-картами. Так
    // знак остаётся тем самым рисунком, который загрузили, и не мылится при
    // приближении: слой масштабируется вместе с картой.
    public sealed class WorldMapSigns
    {
        // Все рисунки слоя в одной текстуре: один проход отрисовки и
        // правильный порядок «сверху вниз» между разными рисунками.
        public Texture2D Atlas;
        public readonly List<WorldMapSign> Items = new List<WorldMapSign>();

        public void Destroy()
        {
            if (Atlas == null)
                return;
            if (Application.isPlaying)
                Object.Destroy(Atlas);
            else
                Object.DestroyImmediate(Atlas);
            Atlas = null;
        }
    }

    // Один рисунок на карте: точка основания и высота — в процентах карты,
    // часть атласа — в UV (начало снизу слева, как у текстур Unity).
    public struct WorldMapSign
    {
        public float XPercent;
        public float YPercent;
        public float HeightPercent;
        public float Aspect;
        public Rect Uv;
        public bool Flip;
        public Color32 Tint;
    }

    // Слой рисунков: растягивается на весь элемент карты и рисует знаки
    // четырёхугольниками из атласа в порядке списка (он уже отсортирован
    // сверху вниз). Клики не ловит.
    public sealed class WorldMapSignLayer : VisualElement
    {
        private const int QuadsPerChunk = 8000;
        private WorldMapSigns signs;

        public WorldMapSignLayer()
        {
            pickingMode = PickingMode.Ignore;
            style.position = Position.Absolute;
            style.left = 0;
            style.top = 0;
            style.right = 0;
            style.bottom = 0;
            generateVisualContent += Draw;
            RegisterCallback<GeometryChangedEvent>(_ => MarkDirtyRepaint());
        }

        public WorldMapSigns Signs
        {
            get => signs;
            set
            {
                signs = value;
                MarkDirtyRepaint();
            }
        }

        private void Draw(MeshGenerationContext context)
        {
            if (signs == null || signs.Atlas == null || signs.Items.Count == 0)
                return;
            Rect rect = contentRect;
            if (rect.width <= 0f || rect.height <= 0f)
                return;

            List<WorldMapSign> items = signs.Items;
            for (int start = 0; start < items.Count; start += QuadsPerChunk)
            {
                int count = Mathf.Min(QuadsPerChunk, items.Count - start);
                MeshWriteData mesh = context.Allocate(count * 4, count * 6, signs.Atlas);
                for (int i = 0; i < count; i++)
                {
                    WorldMapSign sign = items[start + i];
                    float h = sign.HeightPercent / 100f * rect.height;
                    float w = h * sign.Aspect;
                    float x = sign.XPercent / 100f * rect.width;
                    float y = sign.YPercent / 100f * rect.height;
                    float left = x - w * 0.5f, right = x + w * 0.5f, top = y - h, bottom = y;
                    float u0 = sign.Flip ? sign.Uv.xMax : sign.Uv.xMin;
                    float u1 = sign.Flip ? sign.Uv.xMin : sign.Uv.xMax;
                    Vector2 uvTopLeft = new Vector2(u0, sign.Uv.yMax);
                    Vector2 uvTopRight = new Vector2(u1, sign.Uv.yMax);
                    Vector2 uvBottomRight = new Vector2(u1, sign.Uv.yMin);
                    Vector2 uvBottomLeft = new Vector2(u0, sign.Uv.yMin);
                    mesh.SetNextVertex(new Vertex { position = new Vector3(left, top, Vertex.nearZ), tint = sign.Tint, uv = uvTopLeft });
                    mesh.SetNextVertex(new Vertex { position = new Vector3(right, top, Vertex.nearZ), tint = sign.Tint, uv = uvTopRight });
                    mesh.SetNextVertex(new Vertex { position = new Vector3(right, bottom, Vertex.nearZ), tint = sign.Tint, uv = uvBottomRight });
                    mesh.SetNextVertex(new Vertex { position = new Vector3(left, bottom, Vertex.nearZ), tint = sign.Tint, uv = uvBottomLeft });
                    ushort b = (ushort)(i * 4);
                    mesh.SetNextIndex(b);
                    mesh.SetNextIndex((ushort)(b + 1));
                    mesh.SetNextIndex((ushort)(b + 2));
                    mesh.SetNextIndex(b);
                    mesh.SetNextIndex((ushort)(b + 2));
                    mesh.SetNextIndex((ushort)(b + 3));
                }
            }
        }
    }
}

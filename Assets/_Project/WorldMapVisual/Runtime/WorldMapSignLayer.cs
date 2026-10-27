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

namespace KingdomSurvival.WorldMapVisual
{
    // Слой текстуры карты для видеокарты. Растянутый (Tiled = false) —
    // картинка на всю карту (подкраска местности, вода и дороги, виньетка).
    // Плиточный — текстура художника повтором в её собственном разрешении
    // (подложка, узоры местности, рябь, бумага); где её видно — сетка
    // прозрачности Alpha (GridWidth × GridHeight узлов на всю карту, null —
    // везде одинаково), умноженная на UniformAlpha.
    public sealed class WorldMapTextureLayer
    {
        public string Name = string.Empty;
        public Texture2D Texture;
        public bool Tiled;
        public float TileWidthPercent = 100f;
        public float TileHeightPercent = 100f;
        public int GridWidth;
        public int GridHeight;
        public float[] Alpha;
        public float UniformAlpha = 1f;

        // Плитка по маске: треугольники области (x, y по три вершины) в
        // пикселях исходной карты SourceWidth × SourceHeight — край рваный и
        // чёткий, а не растворение в прозрачность.
        public float[] Triangles;
        // Заливка треугольниками без текстуры: цвет Tint или свой цвет у
        // каждого треугольника (TriangleColors, по одному на треугольник).
        public Color Tint = Color.white;
        public Color32[] TriangleColors;
        // Прозрачность в каждой вершине (по три на треугольник) — заливка
        // тает к краю захода на соседнюю.
        public float[] VertexAlpha;
        // Векторный слой: заливки и мазки туши; рисует Painter2D со
        // сглаживанием в экранных пикселях — чётко при любом приближении.
        public List<WorldMapVectorShape> Shapes;
        public float SourceWidth = 1f;
        public float SourceHeight = 1f;
    }

    // Одна заливка: контуры в пикселях исходной карты, один цвет. OddEven —
    // вложенные контуры вырезают дыры (озеро в лесу, кольцо мазка).
    public sealed class WorldMapVectorShape
    {
        public Color Color = Color.black;
        public bool OddEven = true;
        public readonly List<Vector2[]> Contours = new List<Vector2[]>();
    }

    // Карта, разложенная на слои: снизу вверх Layers, затем свои рисунки.
    // Под всеми — однотонный фон (цвет основы), его даёт сам рисунок карты.
    public sealed class WorldMapLayers
    {
        public readonly List<WorldMapTextureLayer> Layers = new List<WorldMapTextureLayer>();
        public WorldMapSigns Signs = new WorldMapSigns();

        public void Destroy()
        {
            foreach (WorldMapTextureLayer layer in Layers)
            {
                if (layer?.Texture == null)
                    continue;
                if (Application.isPlaying)
                    Object.Destroy(layer.Texture);
                else
                    Object.DestroyImmediate(layer.Texture);
                layer.Texture = null;
            }
            Layers.Clear();
            Signs?.Destroy();
        }
    }

    // Все слои карты одним элементом: каждый слой текстуры — свой дочерний
    // элемент, рисунки — сверху. Растягивается на весь элемент карты.
    public sealed class WorldMapLayersView : VisualElement
    {
        private WorldMapLayers layers;

        public WorldMapLayersView()
        {
            pickingMode = PickingMode.Ignore;
            style.position = Position.Absolute;
            style.left = 0;
            style.top = 0;
            style.right = 0;
            style.bottom = 0;
        }

        public WorldMapLayers Layers
        {
            get => layers;
            set
            {
                layers = value;
                Clear();
                if (layers == null)
                    return;
                foreach (WorldMapTextureLayer layer in layers.Layers)
                {
                    if (layer != null && (layer.Texture != null || layer.Shapes != null || layer.Triangles != null))
                        Add(new WorldMapTextureLayerView(layer));
                }
                Add(new WorldMapSignLayer { Signs = layers.Signs });
            }
        }
    }

    public sealed class WorldMapTextureLayerView : VisualElement
    {
        private const int QuadsPerChunk = 15000;
        private readonly WorldMapTextureLayer layer;

        public WorldMapTextureLayerView(WorldMapTextureLayer layer)
        {
            this.layer = layer;
            pickingMode = PickingMode.Ignore;
            style.position = Position.Absolute;
            style.left = 0;
            style.top = 0;
            style.right = 0;
            style.bottom = 0;
            generateVisualContent += Draw;
            RegisterCallback<GeometryChangedEvent>(_ => MarkDirtyRepaint());
        }

        private void Draw(MeshGenerationContext context)
        {
            Rect rect = contentRect;
            if (layer == null || rect.width <= 0f || rect.height <= 0f)
                return;
            float sx = rect.width / Mathf.Max(1f, layer.SourceWidth), sy = rect.height / Mathf.Max(1f, layer.SourceHeight);

            if (layer.Shapes != null)
            {
                Painter2D painter = context.painter2D;
                foreach (WorldMapVectorShape shape in layer.Shapes)
                {
                    if (shape == null || shape.Contours.Count == 0)
                        continue;
                    painter.fillColor = shape.Color;
                    painter.BeginPath();
                    foreach (Vector2[] contour in shape.Contours)
                    {
                        if (contour == null || contour.Length < 3)
                            continue;
                        painter.MoveTo(new Vector2(contour[0].x * sx, contour[0].y * sy));
                        for (int i = 1; i < contour.Length; i++)
                            painter.LineTo(new Vector2(contour[i].x * sx, contour[i].y * sy));
                        painter.ClosePath();
                    }
                    painter.Fill(shape.OddEven ? FillRule.OddEven : FillRule.NonZero);
                }
                return;
            }

            if (layer.Texture == null && layer.Triangles == null)
                return;
            float tileW = layer.Tiled ? Mathf.Max(0.0001f, layer.TileWidthPercent / 100f * rect.width) : rect.width;
            float tileH = layer.Tiled ? Mathf.Max(0.0001f, layer.TileHeightPercent / 100f * rect.height) : rect.height;
            Vector2 Uv(float x, float y) => new Vector2(x / tileW, 1f - y / tileH);

            if (layer.Triangles != null)
            {
                // Плитка по маске или заливка: готовые треугольники области.
                // Общие вершины соседних треугольников — одна вершина сетки.
                Color tint = layer.Tint;
                Color32 color = new Color(tint.r, tint.g, tint.b, Mathf.Clamp01(tint.a * layer.UniformAlpha));
                int triangles = layer.Triangles.Length / 6;
                bool perTriangle = layer.TriangleColors != null && layer.TriangleColors.Length >= triangles;
                bool perVertex = layer.VertexAlpha != null && layer.VertexAlpha.Length >= triangles * 3;
                List<Vertex> vertices = new List<Vertex>();
                List<ushort> indices = new List<ushort>();
                Dictionary<(float, float, int), ushort> shared = new Dictionary<(float, float, int), ushort>();
                void Flush()
                {
                    if (indices.Count == 0)
                        return;
                    MeshWriteData mesh = layer.Texture != null ? context.Allocate(vertices.Count, indices.Count, layer.Texture) : context.Allocate(vertices.Count, indices.Count);
                    foreach (Vertex vertex in vertices)
                        mesh.SetNextVertex(vertex);
                    foreach (ushort index in indices)
                        mesh.SetNextIndex(index);
                    vertices.Clear();
                    indices.Clear();
                    shared.Clear();
                }
                for (int t = 0; t < triangles; t++)
                {
                    if (vertices.Count > ushort.MaxValue - 3)
                        Flush();
                    if (perTriangle)
                    {
                        color = layer.TriangleColors[t];
                        color.a = (byte)Mathf.RoundToInt(color.a * Mathf.Clamp01(layer.UniformAlpha));
                    }
                    for (int v = 0; v < 3; v++)
                    {
                        int i = t * 6 + v * 2;
                        float x = layer.Triangles[i] * sx, y = layer.Triangles[i + 1] * sy;
                        Color32 vertexColor = color;
                        if (perVertex)
                            vertexColor.a = (byte)Mathf.RoundToInt(color.a * Mathf.Clamp01(layer.VertexAlpha[t * 3 + v]));
                        int colorKey = vertexColor.r | vertexColor.g << 8 | vertexColor.b << 16 | vertexColor.a << 24;
                        if (!shared.TryGetValue((x, y, colorKey), out ushort index))
                        {
                            index = (ushort)vertices.Count;
                            shared[(x, y, colorKey)] = index;
                            vertices.Add(new Vertex { position = new Vector3(x, y, Vertex.nearZ), tint = vertexColor, uv = Uv(x, y) });
                        }
                        indices.Add(index);
                    }
                }
                Flush();
                return;
            }

            if (layer.Alpha == null || layer.GridWidth < 2 || layer.GridHeight < 2)
            {
                byte a = (byte)Mathf.Clamp(Mathf.RoundToInt(layer.UniformAlpha * 255f), 0, 255);
                if (a == 0)
                    return;
                MeshWriteData mesh = context.Allocate(4, 6, layer.Texture);
                Color32 tint = new Color32(255, 255, 255, a);
                mesh.SetNextVertex(new Vertex { position = new Vector3(0f, 0f, Vertex.nearZ), tint = tint, uv = Uv(0f, 0f) });
                mesh.SetNextVertex(new Vertex { position = new Vector3(rect.width, 0f, Vertex.nearZ), tint = tint, uv = Uv(rect.width, 0f) });
                mesh.SetNextVertex(new Vertex { position = new Vector3(rect.width, rect.height, Vertex.nearZ), tint = tint, uv = Uv(rect.width, rect.height) });
                mesh.SetNextVertex(new Vertex { position = new Vector3(0f, rect.height, Vertex.nearZ), tint = tint, uv = Uv(0f, rect.height) });
                WriteQuadIndices(mesh, 0);
                return;
            }

            // Сетка прозрачности: четырёхугольник на клетку сетки, пустые
            // (все углы прозрачны) пропускаются.
            int gw = layer.GridWidth, gh = layer.GridHeight;
            float stepX = rect.width / (gw - 1), stepY = rect.height / (gh - 1);
            List<int> quads = new List<int>();
            for (int j = 0; j < gh - 1; j++)
            {
                for (int i = 0; i < gw - 1; i++)
                {
                    int k = j * gw + i;
                    if (layer.Alpha[k] > 0.002f || layer.Alpha[k + 1] > 0.002f || layer.Alpha[k + gw] > 0.002f || layer.Alpha[k + gw + 1] > 0.002f)
                        quads.Add(k);
                }
            }

            for (int start = 0; start < quads.Count; start += QuadsPerChunk)
            {
                int count = Mathf.Min(QuadsPerChunk, quads.Count - start);
                MeshWriteData mesh = context.Allocate(count * 4, count * 6, layer.Texture);
                for (int q = 0; q < count; q++)
                {
                    int k = quads[start + q];
                    int i = k % gw, j = k / gw;
                    float x0 = i * stepX, x1 = (i + 1) * stepX, y0 = j * stepY, y1 = (j + 1) * stepY;
                    mesh.SetNextVertex(Corner(x0, y0, layer.Alpha[k], Uv(x0, y0)));
                    mesh.SetNextVertex(Corner(x1, y0, layer.Alpha[k + 1], Uv(x1, y0)));
                    mesh.SetNextVertex(Corner(x1, y1, layer.Alpha[k + gw + 1], Uv(x1, y1)));
                    mesh.SetNextVertex(Corner(x0, y1, layer.Alpha[k + gw], Uv(x0, y1)));
                    WriteQuadIndices(mesh, q * 4);
                }
            }
        }

        private Vertex Corner(float x, float y, float alpha, Vector2 uv) => new Vertex
        {
            position = new Vector3(x, y, Vertex.nearZ),
            tint = new Color32(255, 255, 255, (byte)Mathf.Clamp(Mathf.RoundToInt(alpha * layer.UniformAlpha * 255f), 0, 255)),
            uv = uv
        };

        private static void WriteQuadIndices(MeshWriteData mesh, int first)
        {
            mesh.SetNextIndex((ushort)first);
            mesh.SetNextIndex((ushort)(first + 1));
            mesh.SetNextIndex((ushort)(first + 2));
            mesh.SetNextIndex((ushort)first);
            mesh.SetNextIndex((ushort)(first + 2));
            mesh.SetNextIndex((ushort)(first + 3));
        }
    }
}

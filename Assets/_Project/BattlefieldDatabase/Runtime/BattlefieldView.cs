using System;
using System.Collections.Generic;
using KingdomSurvival.BattleSandbox;
using UnityEngine;
using UnityEngine.UIElements;

namespace KingdomSurvival.BattlefieldDatabase
{
    // Поле боя без участников: фон в кадре 16:9 и основной вид гексов
    // (подложка, картинка гекса, картинка рамки, линия). Одно и то же
    // в бою и в окне базы. Состояния гексов и участников рисует то, что
    // кладут поверх (HexBoardElement в бою, слой правки в окне).
    public sealed class BattlefieldView : VisualElement
    {
        private static readonly Color EmptyFrameColor = new Color(0.055f, 0.065f, 0.075f, 1f);

        private readonly bool cover;
        private readonly VisualElement frame;
        private readonly Image background;
        private readonly VisualElement placeholderLayer;
        private readonly Label placeholderLabel;
        private static readonly Color PlaceholderRock = new Color(0.10f, 0.095f, 0.09f, 1f);
        private static readonly Color PlaceholderFloor = new Color(0.27f, 0.24f, 0.20f, 1f);
        private readonly VisualElement fillLayer;
        private readonly VisualElement imageLayer;
        private readonly VisualElement lineLayer;
        private readonly List<Image> hexImages = new List<Image>();
        private readonly List<Image> frameImages = new List<Image>();
        private readonly List<HexCoord> activeCells = new List<HexCoord>();

        private BattlefieldDefinitionData battlefield;
        private BattlefieldHexStyle hexStyle = new BattlefieldHexStyle();
        private bool worldUnderlay;
        // Поле, собранное в Базе локаций: живой кадр места вместо картинки поля.
        private Texture sceneTexture;
        private readonly HashSet<HexCoord> underlayBlocked = new HashSet<HexCoord>();

        public Rect FrameRect { get; private set; }
        public BattlefieldGridLayout Layout { get; private set; }
        public BattlefieldDefinitionData Battlefield => battlefield;
        public BattlefieldHexStyle HexStyle => hexStyle;
        public event Action LayoutChanged;

        // Рельеф под полем (арена MapLab): заливка, линии и картинки гексов
        // ложатся на землю. null — плоское поле.
        private IBattlefieldRelief relief;

        public IBattlefieldRelief Relief
        {
            get => relief;
            set
            {
                relief = value;
                SyncImages();
                fillLayer.MarkDirtyRepaint();
                lineLayer.MarkDirtyRepaint();
            }
        }

        // ПР-12К (канон v1.54 §28.3): бой на месте рисуется поверх рисунка
        // места — кадр прозрачный, без фона и заглушки поля; сетка — все
        // клетки арены (стены боя задаёт место, а не отключённые гексы поля).
        public bool WorldUnderlay
        {
            get => worldUnderlay;
            set
            {
                worldUnderlay = value;
                frame.style.backgroundColor = value ? Color.clear : EmptyFrameColor;
                Refresh();
            }
        }

        // Стены боя на рисунке места: их клетки не рисуются, как отключённые
        // гексы обычного поля.
        public void SetWorldUnderlay(IEnumerable<HexCoord> blockedCells)
        {
            underlayBlocked.Clear();
            if (blockedCells != null)
                underlayBlocked.UnionWith(blockedCells);
            WorldUnderlay = true;
        }

        // ПР-12К: гексы всей локации — элемент целиком изображает рисунок
        // места, сетка — LocationBattleGrid в его прямоугольнике.
        private LocationBattleGrid? locationGrid;

        public bool HasLocationGrid => locationGrid.HasValue;

        // Плотность рисованной линии сетки для рисунка места (SetBattleGrid);
        // меньше нуля — у стиля ровная линия.
        public float GridInkOpacity => hexStyle.InkLines ? hexStyle.InkOpacity * hexStyle.Opacity : -1f;

        // Сетку (заливку и линии гексов) рисует мир под доской — рендерер места
        // между землёй и предметами, — а не этот элемент поверх картинки: иначе
        // линии ложились бы на людей и предметы.
        private bool gridDrawnBelow;

        public bool GridDrawnBelow
        {
            get => gridDrawnBelow;
            set
            {
                if (gridDrawnBelow == value) return;
                gridDrawnBelow = value;
                DisplayStyle display = value ? DisplayStyle.None : DisplayStyle.Flex;
                fillLayer.style.display = display;
                lineLayer.style.display = display;
            }
        }

        // Клетки поля на рисунке места: кадр поля (FrameRect) ↔ canvasRect в
        // пикселях рисунка. Центры, радиус гекса (с зазором стиля), сжатие по
        // высоте, цвета и толщина линии — в пикселях рисунка. false — поле ещё
        // не разложено.
        public bool GridOnCanvas(Rect canvasRect, List<Vector2> centers, out float radius, out float verticalScale, out Color fill, out Color line,
            out float lineWidth)
        {
            centers.Clear();
            radius = lineWidth = 0f;
            verticalScale = Layout.VerticalScale;
            fill = hexStyle.Fade(hexStyle.FillColor);
            line = hexStyle.Fade(hexStyle.LineColor);
            Rect frameRect = FrameRect;
            if (frameRect.width < 1f || frameRect.height < 1f || Layout.Size <= 0.01f)
                return false;
            float scale = canvasRect.height / frameRect.height;
            foreach (HexCoord cell in activeCells)
            {
                Vector2 center = Layout.GetCenter(cell.Q, cell.R);
                centers.Add(canvasRect.position + Vector2.Scale(center - frameRect.position,
                    new Vector2(canvasRect.width / frameRect.width, canvasRect.height / frameRect.height)));
            }
            radius = HexRadius * scale;
            lineWidth = hexStyle.LineWidth * scale;
            return true;
        }

        public void SetLocationGrid(LocationBattleGrid? grid)
        {
            locationGrid = grid;
            Refresh();
        }

        public BattlefieldView(bool cover)
        {
            this.cover = cover;
            pickingMode = PickingMode.Ignore;
            style.overflow = Overflow.Hidden;

            frame = new VisualElement { name = "battlefield-frame", pickingMode = PickingMode.Ignore };
            frame.style.position = Position.Absolute;
            frame.style.overflow = Overflow.Hidden;
            frame.style.backgroundColor = EmptyFrameColor;
            background = new Image
            {
                name = "battlefield-background",
                pickingMode = PickingMode.Ignore,
                scaleMode = ScaleMode.ScaleAndCrop
            };
            SetAbsoluteFill(background);
            frame.Add(background);
            Add(frame);

            // ПР-12К: техническая заглушка поля, которое ждёт рисунка: камень
            // на отключённых гексах и пол на доступных — чтобы стены места были
            // видны и в исследовании, и в бою.
            placeholderLayer = CreateLayer("battlefield-placeholder", DrawPlaceholder);
            placeholderLabel = new Label("ВРЕМЕННЫЙ ФОН · ЖДЁТ РИСУНКА") { name = "battlefield-placeholder-label", pickingMode = PickingMode.Ignore };
            placeholderLabel.style.position = Position.Absolute;
            placeholderLabel.style.fontSize = 11f;
            placeholderLabel.style.color = new Color(0.78f, 0.72f, 0.60f, 0.55f);
            frame.Add(placeholderLabel);

            fillLayer = CreateLayer("battlefield-hex-fill", DrawFill);
            imageLayer = CreateLayer("battlefield-hex-images", null);
            lineLayer = CreateLayer("battlefield-hex-lines", DrawLines);

            RegisterCallback<GeometryChangedEvent>(_ => Relayout());
        }

        // Показать поле. В бою и в Play Mode изменения базы подхватываются
        // на ходу: стиль и поле — те же объекты, что правит окно базы.
        public void Show(BattlefieldDefinitionData field, BattlefieldHexStyle style)
        {
            battlefield = field;
            hexStyle = style ?? new BattlefieldHexStyle();
            Refresh();
        }

        // Живой кадр места (поле, собранное в Базе локаций) — ровно кадр поля:
        // без обрезки, масштаба и сдвига картинки поля. null — снова картинка.
        public void SetSceneTexture(Texture texture)
        {
            sceneTexture = texture;
            Refresh();
        }

        public bool HasSceneTexture => sceneTexture != null;

        public void Refresh()
        {
            if (sceneTexture != null)
            {
                background.sprite = null;
                background.image = sceneTexture;
                background.scaleMode = ScaleMode.StretchToFill;
            }
            else
            {
                background.image = null;
                background.scaleMode = ScaleMode.ScaleAndCrop;
                background.sprite = battlefield != null && !worldUnderlay ? battlefield.Background : null;
            }
            background.style.display = background.sprite != null || sceneTexture != null ? DisplayStyle.Flex : DisplayStyle.None;
            bool placeholder = IsPlaceholder;
            placeholderLayer.style.display = placeholder ? DisplayStyle.Flex : DisplayStyle.None;
            placeholderLabel.style.display = placeholder ? DisplayStyle.Flex : DisplayStyle.None;
            activeCells.Clear();
            IEnumerable<HexCoord> cells = locationGrid.HasValue ? locationGrid.Value.Cells() : BattlefieldFrame.ActiveCells(worldUnderlay ? null : battlefield);
            foreach (HexCoord cell in cells)
            {
                if (!worldUnderlay || !underlayBlocked.Contains(cell))
                    activeCells.Add(cell);
            }
            Relayout();
        }

        public void EnableLiveRefresh(long intervalMs)
        {
            schedule.Execute(Refresh).Every(intervalMs);
        }

        private void Relayout()
        {
            Rect area = contentRect;
            if (float.IsNaN(area.width) || area.width <= 1f || area.height <= 1f)
                return;

            Rect gridArea = BattlefieldFrame.GetGridArea(battlefield);
            if (locationGrid.HasValue)
            {
                // Весь элемент — рисунок места.
                FrameRect = new Rect(0f, 0f, area.width, area.height);
                Layout = locationGrid.Value.LayoutIn(FrameRect);
            }
            else
            {
                FrameRect = BattlefieldFrame.FitFrame(area, gridArea, cover);
                Layout = BattlefieldFrame.ComputeLayout(FrameRect, gridArea);
            }

            frame.style.left = FrameRect.x;
            frame.style.top = FrameRect.y;
            frame.style.width = FrameRect.width;
            frame.style.height = FrameRect.height;

            float scale = battlefield != null && sceneTexture == null ? battlefield.BackgroundScale : 1f;
            Vector2 offset = battlefield != null && sceneTexture == null ? battlefield.BackgroundOffset : Vector2.zero;
            background.style.scale = new Scale(new Vector3(scale, scale, 1f));
            background.style.translate = new Translate(
                new Length(offset.x * FrameRect.width, LengthUnit.Pixel),
                new Length(offset.y * FrameRect.height, LengthUnit.Pixel));

            placeholderLabel.style.left = 12f;
            placeholderLabel.style.bottom = 8f;
            placeholderLayer.MarkDirtyRepaint();
            SyncImages();
            fillLayer.MarkDirtyRepaint();
            lineLayer.MarkDirtyRepaint();
            LayoutChanged?.Invoke();
        }

        private float HexRadius => Layout.Size * (1f - hexStyle.Gap);

        private void SyncImages()
        {
            SyncImageSet(hexImages, hexStyle.HexImage, hexStyle.HexImageTint);
            SyncImageSet(frameImages, hexStyle.FrameImage, hexStyle.FrameImageTint);
        }

        private void SyncImageSet(List<Image> images, Sprite sprite, Color tint)
        {
            int needed = sprite != null ? activeCells.Count : 0;
            while (images.Count < needed)
            {
                Image image = new Image { pickingMode = PickingMode.Ignore, scaleMode = ScaleMode.StretchToFill };
                image.style.position = Position.Absolute;
                images.Add(image);
            }

            // Рамка всегда поверх картинки гекса.
            for (int i = 0; i < images.Count; i++)
            {
                Image image = images[i];
                if (i >= needed)
                {
                    image.RemoveFromHierarchy();
                    continue;
                }

                if (image.parent != imageLayer)
                    imageLayer.Add(image);
                Vector2 box = Layout.GetHexBox(HexRadius) * hexStyle.ImageScale;
                Vector2 center = BattlefieldReliefMath.Raise(relief, FrameRect, Layout.GetCenter(activeCells[i].Q, activeCells[i].R));
                image.sprite = sprite;
                image.tintColor = hexStyle.Fade(tint);
                image.style.left = center.x - box.x * 0.5f;
                image.style.top = center.y - box.y * 0.5f;
                image.style.width = box.x;
                image.style.height = box.y;
            }

            foreach (Image frameImage in frameImages)
            {
                if (frameImage.parent == imageLayer)
                    frameImage.BringToFront();
            }
        }

        private bool IsPlaceholder => !worldUnderlay && sceneTexture == null && battlefield != null && battlefield.AwaitingArt && battlefield.Background == null;

        private void DrawPlaceholder(MeshGenerationContext context)
        {
            if (!IsPlaceholder || Layout.Size <= 0.01f)
                return;

            Painter2D painter = context.painter2D;
            foreach (HexCoord cell in SandboxArenaShape.Cells())
            {
                painter.fillColor = battlefield.IsCellDisabled(cell.Q, cell.R) ? PlaceholderRock : PlaceholderFloor;
                BattlefieldFrame.FillHexPath(painter, Layout.GetCenter(cell.Q, cell.R), Layout.Size * 1.02f, Layout.VerticalScale);
                painter.Fill();
            }
        }

        private void DrawFill(MeshGenerationContext context)
        {
            Color fill = hexStyle.Fade(hexStyle.FillColor);
            if (fill.a <= 0.001f || Layout.Size <= 0.01f)
                return;

            Painter2D painter = context.painter2D;
            painter.fillColor = fill;
            foreach (HexCoord cell in activeCells)
            {
                HexPath(painter, Layout.GetCenter(cell.Q, cell.R), HexRadius);
                painter.Fill();
            }
        }

        private void DrawLines(MeshGenerationContext context)
        {
            Color line = hexStyle.Fade(hexStyle.LineColor);
            if (line.a <= 0.001f || hexStyle.LineWidth <= 0.01f || Layout.Size <= 0.01f)
                return;

            if (hexStyle.InkLines)
            {
                DrawInkLines(context);
                return;
            }
            Painter2D painter = context.painter2D;
            painter.strokeColor = line;
            painter.lineWidth = hexStyle.LineWidth;
            foreach (HexCoord cell in activeCells)
            {
                HexPath(painter, Layout.GetCenter(cell.Q, cell.R), HexRadius);
                painter.Stroke();
            }
        }

        // Рисованная сетка (перо дорог MapLab, BattlefieldInkGrid): каждое
        // ребро один раз, по рельефу; рисунок штрихов привязан к решётке и при
        // зуме не меняется. Геометрия строится заново только при смене клеток,
        // размера кадра или рельефа.
        private readonly List<Vector2> inkCenters = new List<Vector2>();
        private readonly List<Vector2> inkPositions = new List<Vector2>();
        private readonly List<float> inkAlphas = new List<float>();
        private readonly List<int> inkIndices = new List<int>();
        private int inkKey;

        private void DrawInkLines(MeshGenerationContext context)
        {
            BattlefieldInkPen pen = BattlefieldInkPen.Current ?? new BattlefieldInkPen();
            float pixels = panel != null ? panel.scaledPixelsPerPoint : 1f;
            Rect frame = FrameRect;
            int key = 17;
            unchecked
            {
                foreach (HexCoord cell in activeCells) key = key * 31 + cell.Q * 977 + cell.R;
                key = key * 31 + Mathf.RoundToInt(Layout.Size * 100f);
                key = key * 31 + Mathf.RoundToInt(frame.x * 10f) * 7 + Mathf.RoundToInt(frame.y * 10f);
                key = key * 31 + Mathf.RoundToInt(frame.width * 10f) * 7 + Mathf.RoundToInt(frame.height * 10f);
                key = key * 31 + Mathf.RoundToInt(Layout.VerticalScale * 1000f);
                key = key * 31 + Mathf.RoundToInt(pixels * 100f);
                key = key * 31 + Mathf.RoundToInt(hexStyle.InkOpacity * hexStyle.Opacity * 1000f);
                key = key * 31 + (relief != null ? relief.GetHashCode() : 0);
                key = key * 31 + pen.GetHashCode();
            }
            if (key != inkKey || inkPositions.Count == 0)
            {
                inkKey = key;
                inkCenters.Clear();
                foreach (HexCoord cell in activeCells)
                    inkCenters.Add(Layout.GetCenter(cell.Q, cell.R));
                IBattlefieldRelief shownRelief = relief;
                Func<Vector2, Vector2> raise = shownRelief != null ? p => BattlefieldReliefMath.Raise(shownRelief, frame, p) : (Func<Vector2, Vector2>)null;
                BattlefieldInkGrid.Result grid = BattlefieldInkGrid.Build(inkCenters, Layout.Size, Layout.VerticalScale, raise, pen,
                    new BattlefieldInkGrid.Options { Opacity = hexStyle.InkOpacity * hexStyle.Opacity, PixelScale = pixels, Endings = true });
                BattlefieldInkGrid.Triangulate(grid.Strokes, 1.1f / Mathf.Max(0.1f, pixels), inkPositions, inkAlphas, inkIndices);
            }
            WriteInkMesh(context, pen.Color);
        }

        // UI Toolkit: не больше 65535 вершин в одном куске сетки. Штрих —
        // по 4 вершины на точку, куски режутся по границе штриха.
        private void WriteInkMesh(MeshGenerationContext context, Color color)
        {
            WriteInkMesh(context, color, inkPositions, inkAlphas, inkIndices);
        }

        public static void WriteInkMesh(MeshGenerationContext context, Color color, List<Vector2> positions, List<float> alphas, List<int> indices)
        {
            const int chunkVertices = 60000;
            int start = 0, index = 0;
            while (start < positions.Count && index < indices.Count)
            {
                int indexStart = index;
                int maxVertex = start;
                // Берём треугольники, пока все их вершины укладываются в кусок.
                while (index < indices.Count)
                {
                    int hi = Mathf.Max(indices[index], Mathf.Max(indices[index + 1], indices[index + 2]));
                    if (hi - start >= chunkVertices)
                        break;
                    maxVertex = Mathf.Max(maxVertex, hi);
                    index += 3;
                }
                int vertexCount = maxVertex - start + 1;
                int triangles = index - indexStart;
                if (triangles == 0)
                    break;
                MeshWriteData mesh = context.Allocate(vertexCount, triangles);
                for (int v = start; v < start + vertexCount; v++)
                {
                    Color c = color;
                    c.a = color.a * alphas[v];
                    mesh.SetNextVertex(new Vertex { position = new Vector3(positions[v].x, positions[v].y, Vertex.nearZ), tint = c });
                }
                for (int i = indexStart; i < index; i++)
                    mesh.SetNextIndex((ushort)(indices[i] - start));
                start += vertexCount;
            }
        }

        private void HexPath(Painter2D painter, Vector2 flatCenter, float radius)
        {
            if (relief != null)
                BattlefieldReliefMath.DrapedHexPath(painter, relief, FrameRect, flatCenter, radius, Layout.VerticalScale);
            else
                BattlefieldFrame.FillHexPath(painter, flatCenter, radius, Layout.VerticalScale);
        }

        private VisualElement CreateLayer(string layerName, Action<MeshGenerationContext> draw)
        {
            VisualElement layer = new VisualElement { name = layerName, pickingMode = PickingMode.Ignore };
            SetAbsoluteFill(layer);
            if (draw != null)
                layer.generateVisualContent += draw;
            Add(layer);
            return layer;
        }

        private static void SetAbsoluteFill(VisualElement element)
        {
            element.style.position = Position.Absolute;
            element.style.left = 0f;
            element.style.right = 0f;
            element.style.top = 0f;
            element.style.bottom = 0f;
        }
    }
}

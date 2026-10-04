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

        public Rect FrameRect { get; private set; }
        public BattlefieldGridLayout Layout { get; private set; }
        public BattlefieldDefinitionData Battlefield => battlefield;
        public BattlefieldHexStyle HexStyle => hexStyle;
        public event Action LayoutChanged;

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

        public void Refresh()
        {
            background.sprite = battlefield != null ? battlefield.Background : null;
            background.style.display = background.sprite != null ? DisplayStyle.Flex : DisplayStyle.None;
            bool placeholder = IsPlaceholder;
            placeholderLayer.style.display = placeholder ? DisplayStyle.Flex : DisplayStyle.None;
            placeholderLabel.style.display = placeholder ? DisplayStyle.Flex : DisplayStyle.None;
            activeCells.Clear();
            activeCells.AddRange(BattlefieldFrame.ActiveCells(battlefield));
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
            FrameRect = BattlefieldFrame.FitFrame(area, gridArea, cover);
            Layout = BattlefieldFrame.ComputeLayout(FrameRect, gridArea);

            frame.style.left = FrameRect.x;
            frame.style.top = FrameRect.y;
            frame.style.width = FrameRect.width;
            frame.style.height = FrameRect.height;

            float scale = battlefield != null ? battlefield.BackgroundScale : 1f;
            Vector2 offset = battlefield != null ? battlefield.BackgroundOffset : Vector2.zero;
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
                Vector2 center = Layout.GetCenter(activeCells[i].Q, activeCells[i].R);
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

        private bool IsPlaceholder => battlefield != null && battlefield.AwaitingArt && battlefield.Background == null;

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
                BattlefieldFrame.FillHexPath(painter, Layout.GetCenter(cell.Q, cell.R), HexRadius, Layout.VerticalScale);
                painter.Fill();
            }
        }

        private void DrawLines(MeshGenerationContext context)
        {
            Color line = hexStyle.Fade(hexStyle.LineColor);
            if (line.a <= 0.001f || hexStyle.LineWidth <= 0.01f || Layout.Size <= 0.01f)
                return;

            Painter2D painter = context.painter2D;
            painter.strokeColor = line;
            painter.lineWidth = hexStyle.LineWidth;
            foreach (HexCoord cell in activeCells)
            {
                BattlefieldFrame.FillHexPath(painter, Layout.GetCenter(cell.Q, cell.R), HexRadius, Layout.VerticalScale);
                painter.Stroke();
            }
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

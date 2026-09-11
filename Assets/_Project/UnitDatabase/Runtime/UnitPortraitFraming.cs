using KingdomSurvival.UILayout;
using UnityEngine;
using UnityEngine.UIElements;

namespace KingdomSurvival.UnitDatabase
{
    /// <summary>
    /// Общая математика кадрирования портретов Базы существ. Editor preview
    /// и runtime используют один и тот же путь: полный Sprite -> Cover/Contain
    /// -> Scale -> нормализованный Offset -> Flip X -> clip рамкой.
    /// </summary>
    public static class UnitPortraitFraming
    {
        public const float MinimumScale = 0.05f;
        public const float LegacyPreviewWidth = 150f;
        public const float LegacyPreviewHeight = 200f;

        public static Rect ResolveImageRect(
            Vector2 sourceSize,
            Vector2 frameSize,
            PortraitFitMode fitMode,
            float scale,
            Vector2 normalizedOffset)
        {
            Vector2 pixelOffset = new Vector2(
                normalizedOffset.x * frameSize.x,
                normalizedOffset.y * frameSize.y);

            return UILayoutRuntimeApplier.ResolveImageRect(
                sourceSize,
                frameSize,
                fitMode == PortraitFitMode.Contain
                    ? UILayoutImageMode.Contain
                    : UILayoutImageMode.Cover,
                Mathf.Max(MinimumScale, scale),
                pixelOffset);
        }

        public static Vector2 LegacyPixelsToNormalized(Vector2 legacyOffset)
        {
            return new Vector2(
                legacyOffset.x / LegacyPreviewWidth,
                legacyOffset.y / LegacyPreviewHeight);
        }
    }

    /// <summary>
    /// Code-only UI Toolkit viewport для портрета существа. Сам элемент —
    /// рамка и маска; вложенный Image получает рассчитанный прямоугольник
    /// полного Sprite, поэтому pan не теряет заранее обрезанные пиксели.
    /// </summary>
    public sealed class UnitPortraitElement : VisualElement
    {
        private readonly Image imageLayer;
        private Sprite portrait;
        private PortraitFitMode fitMode = PortraitFitMode.Cover;
        private float portraitScale = 1f;
        private Vector2 normalizedOffset;
        private bool flipX;

        public UnitPortraitElement()
        {
            style.position = Position.Relative;
            style.overflow = Overflow.Hidden;

            imageLayer = new Image
            {
                name = "__unit-portrait-image",
                scaleMode = ScaleMode.StretchToFill,
                pickingMode = PickingMode.Ignore,
                tintColor = Color.white
            };
            imageLayer.style.position = Position.Absolute;
            Add(imageLayer);

            RegisterCallback<GeometryChangedEvent>(_ => RefreshGeometry());
        }

        public Sprite Portrait => portrait;

        public Color TintColor
        {
            get => imageLayer.tintColor;
            set => imageLayer.tintColor = value;
        }

        public void SetPortrait(UnitDefinitionData unit)
        {
            if (unit == null)
            {
                SetPortrait(null, PortraitFitMode.Cover, 1f, Vector2.zero, false);
                return;
            }

            SetPortrait(
                unit.Portrait,
                unit.PortraitFitMode,
                unit.PortraitScale,
                unit.PortraitOffsetNormalized,
                unit.PortraitFlipX);
        }

        public void SetPortrait(
            Sprite sprite,
            PortraitFitMode mode,
            float scale,
            Vector2 offsetNormalized,
            bool shouldFlipX)
        {
            portrait = sprite;
            fitMode = mode;
            portraitScale = Mathf.Max(UnitPortraitFraming.MinimumScale, scale);
            normalizedOffset = offsetNormalized;
            flipX = shouldFlipX;

            imageLayer.sprite = portrait;
            imageLayer.style.display = portrait != null
                ? DisplayStyle.Flex
                : DisplayStyle.None;
            RefreshGeometry();
        }

        public void ClearPortrait()
        {
            SetPortrait(null, PortraitFitMode.Cover, 1f, Vector2.zero, false);
        }

        public void RefreshGeometry()
        {
            if (portrait == null)
                return;

            float width = contentRect.width;
            float height = contentRect.height;
            if (float.IsNaN(width) || float.IsInfinity(width) ||
                float.IsNaN(height) || float.IsInfinity(height) ||
                width <= 0f || height <= 0f)
                return;

            Rect imageRect = UnitPortraitFraming.ResolveImageRect(
                new Vector2(portrait.rect.width, portrait.rect.height),
                new Vector2(width, height),
                fitMode,
                portraitScale,
                normalizedOffset);

            imageLayer.style.left = imageRect.x;
            imageLayer.style.top = imageRect.y;
            imageLayer.style.right = StyleKeyword.Auto;
            imageLayer.style.bottom = StyleKeyword.Auto;
            imageLayer.style.width = imageRect.width;
            imageLayer.style.height = imageRect.height;
            imageLayer.style.translate = new Translate(0f, 0f);
            imageLayer.style.scale = new Scale(new Vector3(flipX ? -1f : 1f, 1f, 1f));
            imageLayer.MarkDirtyRepaint();
        }
    }
}

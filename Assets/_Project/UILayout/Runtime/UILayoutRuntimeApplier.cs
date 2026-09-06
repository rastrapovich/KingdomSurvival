using UnityEngine;
using UnityEngine.UIElements;

namespace KingdomSurvival.UILayout
{
    public static class UILayoutRuntimeApplier
    {
        private const string BackgroundLayerName = "__ui-layout-background";

        public static UILayoutDatabaseAsset LoadDefaultDatabase()
        {
            return Resources.Load<UILayoutDatabaseAsset>(UILayoutDatabaseAsset.ResourcesPath);
        }

        public static bool TryGetLocalReferenceRect(
            UILayoutScreenDefinition screen,
            UILayoutElementDefinition definition,
            out Rect localRect)
        {
            localRect = definition != null ? definition.Rect : default(Rect);
            if (definition == null)
                return false;

            if (string.IsNullOrWhiteSpace(definition.ParentId))
                return true;
            if (screen == null)
                return false;

            UILayoutElementDefinition parent = screen.FindElement(definition.ParentId);
            if (parent == null)
                return false;

            localRect = definition.Rect;
            localRect.position -= parent.Rect.position;
            return true;
        }

        public static void ApplyRect(
            VisualElement target,
            UILayoutElementDefinition definition,
            Vector2 referenceResolution,
            Vector2 actualResolution)
        {
            ApplyRect(target, definition, null, referenceResolution, actualResolution);
        }

        public static void ApplyRect(
            VisualElement target,
            UILayoutElementDefinition definition,
            UILayoutScreenDefinition screen,
            Vector2 referenceResolution,
            Vector2 actualResolution)
        {
            if (target == null || definition == null)
                return;

            float sx = referenceResolution.x > 0f && actualResolution.x > 0f
                ? actualResolution.x / referenceResolution.x
                : 1f;
            float sy = referenceResolution.y > 0f && actualResolution.y > 0f
                ? actualResolution.y / referenceResolution.y
                : 1f;

            Rect r;
            if (!TryGetLocalReferenceRect(screen, definition, out r))
                r = definition.Rect;

            target.style.position = Position.Absolute;
            target.style.left = r.x * sx;
            target.style.top = r.y * sy;
            target.style.right = StyleKeyword.Auto;
            target.style.bottom = StyleKeyword.Auto;
            target.style.width = r.width * sx;
            target.style.height = r.height * sy;
        }

        public static void ApplyBackground(VisualElement target, UILayoutElementDefinition definition)
        {
            if (target == null || definition == null)
                return;

            bool hasImage = definition.Sprite != null || definition.Texture != null;
            VisualElement background = target.Q<VisualElement>(BackgroundLayerName);
            if (!hasImage)
            {
                if (background != null)
                    background.RemoveFromHierarchy();
                return;
            }

            if (background == null)
            {
                background = new VisualElement
                {
                    name = BackgroundLayerName,
                    pickingMode = PickingMode.Ignore
                };
                background.style.position = Position.Absolute;
                background.style.left = 0f;
                background.style.right = 0f;
                background.style.top = 0f;
                background.style.bottom = 0f;
                target.Insert(0, background);
            }

            target.style.overflow = Overflow.Hidden;
            if (definition.Sprite != null)
                background.style.backgroundImage = new StyleBackground(definition.Sprite);
            else
                background.style.backgroundImage = new StyleBackground(definition.Texture);

            Color tint = definition.Tint;
            tint.a *= definition.Opacity;
            background.style.unityBackgroundImageTintColor = tint;
            background.style.unityBackgroundScaleMode = definition.ImageMode == UILayoutImageMode.Stretch
                ? ScaleMode.StretchToFill
                : definition.ImageMode == UILayoutImageMode.Contain
                    ? ScaleMode.ScaleToFit
                    : ScaleMode.ScaleAndCrop;
            background.style.translate = new Translate(definition.ImageOffset.x, definition.ImageOffset.y);
            background.style.scale = new Scale(new Vector3(
                definition.ImageScale,
                definition.ImageScale,
                1f));
        }

        public static void ApplyDimming(
            VisualElement target,
            UILayoutScreenDefinition screen)
        {
            if (target == null || screen == null)
                return;

            target.style.backgroundColor = new Color(
                5f / 255f,
                7f / 255f,
                8f / 255f,
                screen.DimmingOpacity);
        }

        public static void ApplyTextStyle(
            VisualElement target,
            UILayoutElementDefinition definition,
            Vector2 referenceResolution,
            Vector2 actualResolution)
        {
            if (target == null || definition == null)
                return;

            float sx = referenceResolution.x > 0f && actualResolution.x > 0f
                ? actualResolution.x / referenceResolution.x
                : 1f;
            float sy = referenceResolution.y > 0f && actualResolution.y > 0f
                ? actualResolution.y / referenceResolution.y
                : 1f;
            float textScale = Mathf.Max(0.01f, Mathf.Min(sx, sy));

            if (definition.Font != null)
                target.style.unityFont = definition.Font;
            target.style.fontSize = Mathf.Max(1f, definition.FontSize * textScale);
            target.style.color = definition.TextColor;
            target.style.unityFontStyleAndWeight = definition.FontStyle;
            target.style.unityTextAlign = ResolveTextAnchor(
                definition.HorizontalAlignment,
                definition.VerticalAlignment);
        }

        public static TextAnchor ResolveTextAnchor(
            UILayoutTextHorizontalAlignment horizontal,
            UILayoutTextVerticalAlignment vertical)
        {
            if (vertical == UILayoutTextVerticalAlignment.Middle)
            {
                if (horizontal == UILayoutTextHorizontalAlignment.Center)
                    return TextAnchor.MiddleCenter;
                if (horizontal == UILayoutTextHorizontalAlignment.Right)
                    return TextAnchor.MiddleRight;
                return TextAnchor.MiddleLeft;
            }

            if (vertical == UILayoutTextVerticalAlignment.Bottom)
            {
                if (horizontal == UILayoutTextHorizontalAlignment.Center)
                    return TextAnchor.LowerCenter;
                if (horizontal == UILayoutTextHorizontalAlignment.Right)
                    return TextAnchor.LowerRight;
                return TextAnchor.LowerLeft;
            }

            if (horizontal == UILayoutTextHorizontalAlignment.Center)
                return TextAnchor.UpperCenter;
            if (horizontal == UILayoutTextHorizontalAlignment.Right)
                return TextAnchor.UpperRight;
            return TextAnchor.UpperLeft;
        }
    }
}

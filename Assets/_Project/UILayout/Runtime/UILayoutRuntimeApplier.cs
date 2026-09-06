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

        public static void ApplyRect(
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

            Rect r = definition.Rect;
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
    }
}

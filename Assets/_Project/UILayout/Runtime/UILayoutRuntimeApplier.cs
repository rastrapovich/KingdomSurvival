using UnityEngine;
using UnityEngine.UIElements;

namespace KingdomSurvival.UILayout
{
    public static class UILayoutRuntimeApplier
    {
        private const string BackgroundLayerName = "__ui-layout-background";
        private const string DynamicImageLayerName = "__ui-layout-dynamic-image";

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

            ResolveResolutionScale(referenceResolution, actualResolution, out float sx, out float sy);

            Rect r;
            if (!TryGetLocalReferenceRect(screen, definition, out r))
                r = definition.Rect;

            // Когда геометрия элемента передана UILayout, она должна быть
            // окончательной. Старые USS min/max ограничения иначе продолжают
            // участвовать в layout после того, как мы выставили точный Rect.
            // Например, legacy `.narrative-dialogue-portrait { min-height: 360px; }`
            // растягивал рамку 253x131 из UI Конструктора обратно до 253x360,
            // из-за чего runtime кадрировал портрет иначе, чем оба editor preview.
            ClearLegacySizeConstraints(target);

            target.style.position = Position.Absolute;
            target.style.left = r.x * sx;
            target.style.top = r.y * sy;
            target.style.right = StyleKeyword.Auto;
            target.style.bottom = StyleKeyword.Auto;
            target.style.width = r.width * sx;
            target.style.height = r.height * sy;
        }

        /// <summary>
        /// Старый overload оставлен для обратной совместимости. Когда вызывающий
        /// код знает reference/actual resolution, нужно использовать overload ниже,
        /// чтобы ImageOffset интерпретировался одинаково в editor preview и runtime.
        /// </summary>
        public static void ApplyBackground(VisualElement target, UILayoutElementDefinition definition)
        {
            ApplyBackground(target, definition, Vector2.one, Vector2.one);
        }

        /// <summary>
        /// Применяет статическое изображение элемента layout. ImageOffset хранится
        /// в пикселях reference resolution и масштабируется к фактическому экрану.
        /// Если поверх элемента сейчас показано динамическое изображение (например,
        /// портрет говорящего), статический слой остаётся fallback и не просвечивает.
        /// </summary>
        public static void ApplyBackground(
            VisualElement target,
            UILayoutElementDefinition definition,
            Vector2 referenceResolution,
            Vector2 actualResolution)
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
                background = CreateImageLayer(BackgroundLayerName);
                target.Insert(0, background);
            }

            target.style.overflow = Overflow.Hidden;
            if (definition.Sprite != null)
                background.style.backgroundImage = new StyleBackground(definition.Sprite);
            else
                background.style.backgroundImage = new StyleBackground(definition.Texture);

            ApplyImagePresentation(
                background,
                definition,
                referenceResolution,
                actualResolution,
                1f,
                Vector2.zero,
                false);

            VisualElement dynamicImage = target.Q<VisualElement>(DynamicImageLayerName);
            background.style.display = dynamicImage != null ? DisplayStyle.None : DisplayStyle.Flex;
        }

        /// <summary>
        /// Рисует внешний Sprite внутри layout-рамки теми же правилами, что
        /// использует UI Конструктор. Это основной путь для портретов из базы
        /// диалогов: layout задаёт рамку/режим/общий zoom/pan, а говорящий может
        /// добавить свой zoom, нормализованное смещение и отражение по X.
        ///
        /// Рамка (<paramref name="target"/>) и изображение — два разных
        /// элемента. Рамка только обрезает (overflow:hidden) то, что
        /// оказалось за её границей; сам динамический слой всегда получает
        /// СВОЙ размер и позицию, вычисленные <see cref="ResolveImageRect"/>
        /// по полному (необрезанному) Sprite, и рисуется StretchToFill —
        /// пропорции уже заложены в размер прямоугольника, повторно обрезать
        /// через ScaleAndCrop не нужно и вредно (обрезанные пиксели заранее
        /// исключались бы из перетаскивания).
        /// </summary>
        public static void ApplyDynamicImage(
            VisualElement target,
            Sprite sprite,
            UILayoutElementDefinition definition,
            Vector2 referenceResolution,
            Vector2 actualResolution,
            float additionalScale = 1f,
            Vector2 normalizedFrameOffset = default(Vector2),
            bool flipX = false)
        {
            if (target == null)
                return;

            if (sprite == null)
            {
                ClearDynamicImage(target);
                return;
            }

            VisualElement dynamicImage = target.Q<VisualElement>(DynamicImageLayerName);
            if (dynamicImage == null)
            {
                dynamicImage = CreateSizedImageLayer(DynamicImageLayerName);
                target.Add(dynamicImage);
            }

            target.style.overflow = Overflow.Hidden;
            dynamicImage.style.display = DisplayStyle.Flex;
            dynamicImage.style.backgroundImage = new StyleBackground(sprite);
            dynamicImage.style.unityBackgroundScaleMode = ScaleMode.StretchToFill;

            UILayoutImageMode mode = definition != null ? definition.ImageMode : UILayoutImageMode.Cover;
            Vector2 frameSize = definition != null
                ? ResolveImageFrameSize(definition, referenceResolution, actualResolution)
                : new Vector2(target.resolvedStyle.width, target.resolvedStyle.height);
            Vector2 offset = definition != null
                ? ResolveImageOffset(definition, referenceResolution, actualResolution, normalizedFrameOffset)
                : new Vector2(normalizedFrameOffset.x * frameSize.x, normalizedFrameOffset.y * frameSize.y);

            // ResolveImageScale уже умеет корректно объединять общий и
            // индивидуальный zoom и кодировать отражение знаком X —
            // переиспользуем его: величина идёт в ResolveImageRect (реальный
            // размер прямоугольника), а знак — в CSS-flip вокруг центра уже
            // готового элемента (Flip не должен влиять на Offset — §10).
            Vector3 resolvedScale = ResolveImageScale(definition, additionalScale, flipX);
            float magnitude = Mathf.Abs(resolvedScale.y);
            bool flip = resolvedScale.x < 0f;

            Rect imageRect = ResolveImageRect(ResolveSpriteSize(sprite), frameSize, mode, magnitude, offset);

            dynamicImage.style.left = imageRect.x;
            dynamicImage.style.top = imageRect.y;
            dynamicImage.style.right = StyleKeyword.Auto;
            dynamicImage.style.bottom = StyleKeyword.Auto;
            dynamicImage.style.width = imageRect.width;
            dynamicImage.style.height = imageRect.height;
            dynamicImage.style.translate = new Translate(0f, 0f);
            dynamicImage.style.scale = new Scale(new Vector3(flip ? -1f : 1f, 1f, 1f));

            Color tint = definition != null ? definition.Tint : Color.white;
            float opacity = definition != null ? definition.Opacity : 1f;
            tint.a *= opacity;
            dynamicImage.style.unityBackgroundImageTintColor = tint;

            VisualElement background = target.Q<VisualElement>(BackgroundLayerName);
            if (background != null)
                background.style.display = DisplayStyle.None;
        }

        /// <summary>
        /// Убирает динамический Sprite и возвращает видимость статическому
        /// layout-background, если он назначен. Используется при отсутствии
        /// портрета говорящего и при закрытии/смене представления.
        /// </summary>
        public static void ClearDynamicImage(VisualElement target)
        {
            if (target == null)
                return;

            VisualElement dynamicImage = target.Q<VisualElement>(DynamicImageLayerName);
            if (dynamicImage != null)
                dynamicImage.RemoveFromHierarchy();

            VisualElement background = target.Q<VisualElement>(BackgroundLayerName);
            if (background != null)
                background.style.display = DisplayStyle.Flex;
        }

        /// <summary>
        /// Единая точка расчёта прямоугольника ПОЛНОГО изображения (без
        /// предварительной обрезки) относительно рамки. Cover/Contain задают
        /// только базовый размер по пропорциям исходника — сама обрезка
        /// возникает исключительно потому, что часть этого прямоугольника
        /// оказывается за пределами рамки (overflow:hidden/clip у вызывающей
        /// стороны), а не потому, что мы заранее вписали и обрезали Sprite
        /// через ScaleAndCrop. Тот же расчёт обязаны использовать preview
        /// Базы диалогов, preview UI Конструктора и runtime — иначе
        /// кадрирование в трёх местах неизбежно разойдётся.
        /// </summary>
        public static Rect ResolveImageRect(
            Vector2 sourceSize,
            Vector2 frameSize,
            UILayoutImageMode mode,
            float scale,
            Vector2 offset)
        {
            Vector2 baseSize = ResolveBaseDisplaySize(sourceSize, frameSize, mode);
            float safeScale = Mathf.Max(0.05f, scale);
            Vector2 displaySize = baseSize * safeScale;

            return new Rect(
                (frameSize.x - displaySize.x) * 0.5f + offset.x,
                (frameSize.y - displaySize.y) * 0.5f + offset.y,
                displaySize.x,
                displaySize.y);
        }

        /// <summary>
        /// Базовый (до индивидуального zoom) размер полного изображения по
        /// правилам Cover/Contain/Stretch. Cover/Contain сохраняют
        /// пропорции исходника и МОГУТ выйти за пределы рамки (Cover) или
        /// оставить пустое поле внутри неё (Contain) — обрезка/пустое поле
        /// не встроены сюда, это отдельный эффект clip'а рамкой.
        /// </summary>
        private static Vector2 ResolveBaseDisplaySize(Vector2 sourceSize, Vector2 frameSize, UILayoutImageMode mode)
        {
            if (sourceSize.x <= 0f || sourceSize.y <= 0f)
                return frameSize;

            switch (mode)
            {
                case UILayoutImageMode.Contain:
                    {
                        float s = Mathf.Min(frameSize.x / sourceSize.x, frameSize.y / sourceSize.y);
                        return sourceSize * s;
                    }
                case UILayoutImageMode.Stretch:
                    return frameSize;
                default: // Cover
                    {
                        float s = Mathf.Max(frameSize.x / sourceSize.x, frameSize.y / sourceSize.y);
                        return sourceSize * s;
                    }
            }
        }

        /// <summary>
        /// Размер исходного изображения для расчёта пропорций. Намеренно
        /// читает <see cref="Sprite.rect"/>, а не размер всей Texture — это
        /// единственный правильный источник, когда портрет позже окажется
        /// внутри Sprite Atlas (Texture тогда — весь атлас, а не конкретный
        /// портрет).
        /// </summary>
        public static Vector2 ResolveSpriteSize(Sprite sprite)
        {
            return sprite != null
                ? new Vector2(sprite.rect.width, sprite.rect.height)
                : Vector2.zero;
        }

        public static ScaleMode ResolveImageScaleMode(UILayoutImageMode mode)
        {
            switch (mode)
            {
                case UILayoutImageMode.Stretch:
                    return ScaleMode.StretchToFill;
                case UILayoutImageMode.Contain:
                    return ScaleMode.ScaleToFit;
                default:
                    return ScaleMode.ScaleAndCrop;
            }
        }

        /// <summary>
        /// Возвращает фактический размер layout-рамки после масштабирования
        /// reference resolution. Нужен editor preview и индивидуальному pan.
        /// </summary>
        public static Vector2 ResolveImageFrameSize(
            UILayoutElementDefinition definition,
            Vector2 referenceResolution,
            Vector2 actualResolution)
        {
            if (definition == null)
                return Vector2.zero;

            ResolveResolutionScale(referenceResolution, actualResolution, out float sx, out float sy);
            return new Vector2(
                definition.Rect.width * sx,
                definition.Rect.height * sy);
        }

        /// <summary>
        /// Объединяет общий ImageOffset layout (reference pixels) и
        /// индивидуальное смещение говорящего (доля ширины/высоты рамки).
        /// </summary>
        public static Vector2 ResolveImageOffset(
            UILayoutElementDefinition definition,
            Vector2 referenceResolution,
            Vector2 actualResolution,
            Vector2 normalizedFrameOffset)
        {
            if (definition == null)
                return Vector2.zero;

            ResolveResolutionScale(referenceResolution, actualResolution, out float sx, out float sy);
            Vector2 frameSize = new Vector2(
                definition.Rect.width * sx,
                definition.Rect.height * sy);

            return new Vector2(
                definition.ImageOffset.x * sx + normalizedFrameOffset.x * frameSize.x,
                definition.ImageOffset.y * sy + normalizedFrameOffset.y * frameSize.y);
        }

        /// <summary>
        /// Объединяет общий ImageScale layout и индивидуальный zoom говорящего.
        /// Отражение меняет только знак X и не влияет на величину zoom.
        /// </summary>
        public static Vector3 ResolveImageScale(
            UILayoutElementDefinition definition,
            float additionalScale,
            bool flipX)
        {
            float layoutScale = definition != null ? definition.ImageScale : 1f;
            float total = Mathf.Max(0.05f, layoutScale) * Mathf.Max(0.05f, additionalScale);
            return new Vector3(flipX ? -total : total, total, 1f);
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

            ResolveResolutionScale(referenceResolution, actualResolution, out float sx, out float sy);
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

        private static VisualElement CreateImageLayer(string name)
        {
            VisualElement layer = new VisualElement
            {
                name = name,
                pickingMode = PickingMode.Ignore
            };
            layer.style.position = Position.Absolute;
            layer.style.left = 0f;
            layer.style.right = 0f;
            layer.style.top = 0f;
            layer.style.bottom = 0f;
            return layer;
        }

        /// <summary>
        /// UILayout Rect имеет приоритет над legacy USS-ограничениями размера.
        /// Точные width/height задаются сразу после этого метода; здесь снимаются
        /// только ограничения, способные их переопределить после cascade/layout.
        /// Остальные визуальные свойства USS (цвет, border, alignment и т. п.)
        /// сохраняются.
        /// </summary>
        private static void ClearLegacySizeConstraints(VisualElement target)
        {
            target.style.minWidth = 0f;
            target.style.minHeight = 0f;
            target.style.maxWidth = StyleKeyword.None;
            target.style.maxHeight = StyleKeyword.None;
        }

        /// <summary>
        /// В отличие от <see cref="CreateImageLayer"/> (растянут на 100%
        /// рамки — годится для статического фона со ScaleAndCrop), этот слой
        /// НЕ имеет собственного стартового размера: left/top/width/height
        /// выставляются каждый раз в <see cref="ApplyDynamicImage"/> по
        /// <see cref="ResolveImageRect"/> и обычно не совпадают с рамкой —
        /// именно потому, что изображение не обрезано заранее.
        /// </summary>
        private static VisualElement CreateSizedImageLayer(string name)
        {
            VisualElement layer = new VisualElement
            {
                name = name,
                pickingMode = PickingMode.Ignore
            };
            layer.style.position = Position.Absolute;
            return layer;
        }

        private static void ApplyImagePresentation(
            VisualElement imageLayer,
            UILayoutElementDefinition definition,
            Vector2 referenceResolution,
            Vector2 actualResolution,
            float additionalScale,
            Vector2 normalizedFrameOffset,
            bool flipX)
        {
            Color tint = definition.Tint;
            tint.a *= definition.Opacity;
            imageLayer.style.unityBackgroundImageTintColor = tint;
            imageLayer.style.unityBackgroundScaleMode = ResolveImageScaleMode(definition.ImageMode);

            Vector2 offset = ResolveImageOffset(
                definition,
                referenceResolution,
                actualResolution,
                normalizedFrameOffset);
            imageLayer.style.translate = new Translate(offset.x, offset.y);
            imageLayer.style.scale = new Scale(ResolveImageScale(definition, additionalScale, flipX));
        }

        private static void ResolveResolutionScale(
            Vector2 referenceResolution,
            Vector2 actualResolution,
            out float sx,
            out float sy)
        {
            sx = referenceResolution.x > 0f && actualResolution.x > 0f
                ? actualResolution.x / referenceResolution.x
                : 1f;
            sy = referenceResolution.y > 0f && actualResolution.y > 0f
                ? actualResolution.y / referenceResolution.y
                : 1f;
        }
    }
}

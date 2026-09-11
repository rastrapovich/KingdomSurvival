using System;
using System.Collections.Generic;
using UnityEngine;

namespace KingdomSurvival.UILayout
{
    public enum UILayoutImageMode
    {
        Cover,
        Contain,
        Stretch
    }

    public enum UILayoutTextHorizontalAlignment
    {
        Left,
        Center,
        Right
    }

    public enum UILayoutTextVerticalAlignment
    {
        Top,
        Middle,
        Bottom
    }

    /// <summary>
    /// Роль элемента в экране. Определяет, какие группы свойств показывает
    /// конструктор и что применяет рантайм. Значение по умолчанию `Panel`
    /// сохраняет поведение старых записей базы.
    /// </summary>
    public enum UILayoutElementKind
    {
        Panel,
        Text,
        Image,
        Button,
        Container,
        Portrait
    }

    public static class UILayoutPortraitRect
    {
        public static Rect ResizeKeepingCenter(Rect source, PortraitSize size)
        {
            PortraitSizeDefinition definition = PortraitSizeTable.Get(size);
            Vector2 center = source.center;
            return new Rect(
                center.x - definition.Width * 0.5f,
                center.y - definition.Height * 0.5f,
                definition.Width,
                definition.Height);
        }
    }

    [Serializable]
    public sealed class UILayoutRequiredElement
    {
        [SerializeField] private string elementId = string.Empty;
        [SerializeField] private string expectedParentId = string.Empty;

        public string ElementId => elementId ?? string.Empty;
        public string ExpectedParentId => expectedParentId ?? string.Empty;
    }

    [Serializable]
    public sealed class UILayoutElementDefinition
    {
        [SerializeField] private string id = string.Empty;
        [SerializeField] private string displayName = string.Empty;
        [SerializeField] private string parentId = string.Empty;
        [SerializeField] private UILayoutElementKind kind = UILayoutElementKind.Panel;
        [SerializeField] private PortraitSize portraitSize = PortraitSize.M;
        [SerializeField] private string targetName = string.Empty;
        [SerializeField] private bool overrideRect;
        [SerializeField] private bool overrideBackground;
        [SerializeField] private bool overrideText;
        [SerializeField] private string previewText = string.Empty;
        [SerializeField] private Rect rect = new Rect(0f, 0f, 320f, 180f);
        [SerializeField] private Sprite sprite;
        [SerializeField] private Texture2D texture;
        [SerializeField] private UILayoutImageMode imageMode = UILayoutImageMode.Cover;
        [SerializeField, Min(0.05f)] private float imageScale = 1f;
        [SerializeField] private Vector2 imageOffset = Vector2.zero;
        [SerializeField] private Color tint = Color.white;
        [SerializeField, Range(0f, 1f)] private float opacity = 1f;
        [SerializeField] private Font font;
        [SerializeField, Min(1)] private int fontSize = 16;
        [SerializeField] private Color textColor = Color.white;
        [SerializeField] private FontStyle fontStyle = FontStyle.Normal;
        [SerializeField] private UILayoutTextHorizontalAlignment horizontalAlignment = UILayoutTextHorizontalAlignment.Left;
        [SerializeField] private UILayoutTextVerticalAlignment verticalAlignment = UILayoutTextVerticalAlignment.Top;

        public string Id => id;
        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? id : displayName;
        public string ParentId => parentId ?? string.Empty;
        public UILayoutElementKind Kind => kind;
        public PortraitSize PortraitSize => portraitSize;

        /// <summary>
        /// Имя элемента в UXML, к которому привязывается запись. Если не задано,
        /// используется <see cref="Id"/>.
        /// </summary>
        public string TargetName => string.IsNullOrWhiteSpace(targetName) ? id : targetName;

        public bool OverrideRect => overrideRect;
        public bool OverrideBackground => overrideBackground;
        public bool OverrideText => overrideText;
        public string PreviewText => previewText ?? string.Empty;
        public Rect Rect => IsPortrait
            ? UILayoutPortraitRect.ResizeKeepingCenter(rect, portraitSize)
            : rect;
        public Sprite Sprite => sprite;
        public Texture2D Texture => texture;
        public UILayoutImageMode ImageMode => imageMode;
        public float ImageScale => Mathf.Max(0.05f, imageScale);
        public Vector2 ImageOffset => imageOffset;
        public Color Tint => tint;
        public float Opacity => Mathf.Clamp01(opacity);
        public Font Font => font;
        public int FontSize => Mathf.Max(1, fontSize);
        public Color TextColor => textColor;
        public FontStyle FontStyle => fontStyle;
        public UILayoutTextHorizontalAlignment HorizontalAlignment => horizontalAlignment;
        public UILayoutTextVerticalAlignment VerticalAlignment => verticalAlignment;

        public bool HasImage => sprite != null || texture != null;

        /// <summary>
        /// Текстовые свойства применимы к элементу.
        /// </summary>
        public bool IsTextual => kind == UILayoutElementKind.Text || kind == UILayoutElementKind.Button;
        public bool IsPortrait => kind == UILayoutElementKind.Portrait;
        public bool SupportsFreeResize => !IsPortrait;

        public void SetKind(UILayoutElementKind value)
        {
            if (kind == value)
                return;

            if (value == UILayoutElementKind.Portrait)
            {
                portraitSize = PortraitSizeTable.FindNearest(rect.width, rect.height);
                overrideRect = false;
                overrideBackground = false;
                overrideText = false;
            }

            kind = value;
            NormalizePortraitFrame();
        }

        public void SetRect(Rect value)
        {
            if (IsPortrait)
            {
                PortraitSizeDefinition definition = PortraitSizeTable.Get(portraitSize);
                value.width = definition.Width;
                value.height = definition.Height;
            }

            rect = value;
        }

        public void SetPortraitSize(PortraitSize value)
        {
            Rect current = Rect;
            portraitSize = PortraitSizeTable.Get(value).Size;
            rect = UILayoutPortraitRect.ResizeKeepingCenter(current, portraitSize);
        }

        public void SetImageScale(float value) => imageScale = Mathf.Max(0.05f, value);
        public void SetImageOffset(Vector2 value) => imageOffset = value;

        public bool HasCanonicalPortraitFrame()
        {
            if (!IsPortrait)
                return true;

            PortraitSizeDefinition definition = PortraitSizeTable.Get(portraitSize);
            return portraitSize == definition.Size &&
                   Mathf.Approximately(rect.width, definition.Width) &&
                   Mathf.Approximately(rect.height, definition.Height) &&
                   imageMode != UILayoutImageMode.Stretch;
        }

        public bool NormalizePortraitFrame()
        {
            if (!IsPortrait)
                return false;

            PortraitSize normalizedSize = PortraitSizeTable.Get(portraitSize).Size;
            Rect normalized = UILayoutPortraitRect.ResizeKeepingCenter(rect, normalizedSize);
            bool changed = portraitSize != normalizedSize ||
                           !Mathf.Approximately(rect.x, normalized.x) ||
                           !Mathf.Approximately(rect.y, normalized.y) ||
                           !Mathf.Approximately(rect.width, normalized.width) ||
                           !Mathf.Approximately(rect.height, normalized.height);
            portraitSize = normalizedSize;
            rect = normalized;

            if (imageMode == UILayoutImageMode.Stretch)
            {
                imageMode = UILayoutImageMode.Cover;
                changed = true;
            }

            if (overrideRect || overrideBackground || overrideText)
            {
                overrideRect = false;
                overrideBackground = false;
                overrideText = false;
                changed = true;
            }

            return changed;
        }

        public void ResetImageTransform()
        {
            imageScale = 1f;
            imageOffset = Vector2.zero;
        }
    }

    [Serializable]
    public sealed class UILayoutScreenDefinition
    {
        [SerializeField] private string id = string.Empty;
        [SerializeField] private string displayName = string.Empty;
        [SerializeField] private string description = string.Empty;
        [SerializeField] private bool usesDimming = true;
        [SerializeField, Range(0f, 1f)] private float dimmingOpacity = 0.68f;

        [Tooltip("Применять экран generic-байндером по именам элементов UXML.")]
        [SerializeField] private bool autoApply;

        [SerializeField] private string rootName = string.Empty;
        [SerializeField] private List<UILayoutRequiredElement> requiredElements = new List<UILayoutRequiredElement>();
        [SerializeField] private List<UILayoutElementDefinition> elements = new List<UILayoutElementDefinition>();

        public string Id => id;
        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? id : displayName;
        public string Description => description ?? string.Empty;
        public bool UsesDimming => usesDimming;
        public float DimmingOpacity => Mathf.Clamp01(dimmingOpacity);
        public bool AutoApply => autoApply;

        /// <summary>
        /// Имя корневого элемента UXML, внутри которого ищутся элементы экрана.
        /// Пустое значение означает поиск от корня интерфейса.
        /// </summary>
        public string RootName => rootName ?? string.Empty;

        public IReadOnlyList<UILayoutRequiredElement> RequiredElements =>
            requiredElements ?? (IReadOnlyList<UILayoutRequiredElement>)Array.Empty<UILayoutRequiredElement>();

        public IReadOnlyList<UILayoutElementDefinition> Elements =>
            elements ?? (IReadOnlyList<UILayoutElementDefinition>)Array.Empty<UILayoutElementDefinition>();

        public UILayoutElementDefinition FindElement(string elementId)
        {
            if (string.IsNullOrWhiteSpace(elementId) || elements == null)
                return null;

            for (int i = 0; i < elements.Count; i++)
            {
                UILayoutElementDefinition element = elements[i];
                if (element != null && string.Equals(element.Id, elementId, StringComparison.Ordinal))
                    return element;
            }

            return null;
        }
    }

    [CreateAssetMenu(fileName = "KingdomSurvivalUILayouts", menuName = "Kingdom Survival/UI Layout Database")]
    public sealed class UILayoutDatabaseAsset : ScriptableObject
    {
        public const string ResourcesPath = "UILayout/KingdomSurvivalUILayouts";

        public const string NarrativeDialogueScreenId = "narrative-dialogue";

        [SerializeField] private Vector2Int referenceResolution = new Vector2Int(1920, 1080);
        [SerializeField] private List<UILayoutScreenDefinition> screens = new List<UILayoutScreenDefinition>();

        public Vector2Int ReferenceResolution => referenceResolution;
        public IReadOnlyList<UILayoutScreenDefinition> Screens =>
            screens ?? (IReadOnlyList<UILayoutScreenDefinition>)Array.Empty<UILayoutScreenDefinition>();

        public UILayoutScreenDefinition FindScreen(string screenId)
        {
            if (string.IsNullOrWhiteSpace(screenId) || screens == null)
                return null;

            for (int i = 0; i < screens.Count; i++)
            {
                UILayoutScreenDefinition screen = screens[i];
                if (screen != null && string.Equals(screen.Id, screenId, StringComparison.Ordinal))
                    return screen;
            }

            return null;
        }

        /// <summary>
        /// Миграция и защитная нормализация: любой элемент типа Portrait
        /// хранит только канонический preset 5:7. Центр рамки и настройки
        /// изображения при этом не меняются.
        /// </summary>
        public int NormalizePortraitFrames()
        {
            int changed = 0;
            for (int screenIndex = 0; screenIndex < Screens.Count; screenIndex++)
            {
                UILayoutScreenDefinition screen = Screens[screenIndex];
                if (screen == null)
                    continue;

                for (int elementIndex = 0; elementIndex < screen.Elements.Count; elementIndex++)
                {
                    UILayoutElementDefinition element = screen.Elements[elementIndex];
                    if (element != null && element.NormalizePortraitFrame())
                        changed++;
                }
            }

            return changed;
        }

        private void OnValidate()
        {
            NormalizePortraitFrames();
        }

        public void CollectValidationIssues(List<string> issues)
        {
            if (issues == null)
                throw new ArgumentNullException(nameof(issues));

            issues.Clear();
            HashSet<string> screenIds = new HashSet<string>(StringComparer.Ordinal);
            for (int screenIndex = 0; screenIndex < Screens.Count; screenIndex++)
            {
                UILayoutScreenDefinition screen = Screens[screenIndex];
                if (screen == null || string.IsNullOrWhiteSpace(screen.Id))
                {
                    issues.Add("Экран #" + (screenIndex + 1) + ": отсутствует ID.");
                    continue;
                }

                if (!screenIds.Add(screen.Id))
                    issues.Add("Повторяющийся ID экрана: " + screen.Id + ".");

                ValidateElements(screen, issues);
                ValidateParents(screen, issues);
                ValidateRequiredElements(screen, issues);
            }
        }

        private static void ValidateElements(
            UILayoutScreenDefinition screen,
            List<string> issues)
        {
            HashSet<string> elementIds = new HashSet<string>(StringComparer.Ordinal);
            for (int elementIndex = 0; elementIndex < screen.Elements.Count; elementIndex++)
            {
                UILayoutElementDefinition element = screen.Elements[elementIndex];
                if (element == null || string.IsNullOrWhiteSpace(element.Id))
                {
                    issues.Add(screen.Id + ": элемент #" + (elementIndex + 1) + " без ID.");
                    continue;
                }

                if (!elementIds.Add(element.Id))
                    issues.Add(screen.Id + ": повторяющийся ID элемента " + element.Id + ".");
                if (element.Rect.width <= 0f || element.Rect.height <= 0f)
                    issues.Add(screen.Id + "/" + element.Id + ": ширина и высота должны быть больше нуля.");
                if (!element.HasCanonicalPortraitFrame())
                {
                    issues.Add(
                        screen.Id + "/" + element.Id +
                        ": Portrait должен использовать канонический preset 5:7 и режим Cover/Contain.");
                }
                if (element.FontSize <= 0)
                    issues.Add(screen.Id + "/" + element.Id + ": размер шрифта должен быть больше нуля.");
                if (element.OverrideText && !element.IsTextual)
                {
                    issues.Add(
                        screen.Id + "/" + element.Id +
                        ": включено переопределение текста, но тип элемента не текстовый.");
                }
            }
        }

        private static void ValidateParents(
            UILayoutScreenDefinition screen,
            List<string> issues)
        {
            for (int elementIndex = 0; elementIndex < screen.Elements.Count; elementIndex++)
            {
                UILayoutElementDefinition element = screen.Elements[elementIndex];
                if (element == null || string.IsNullOrWhiteSpace(element.Id) || string.IsNullOrWhiteSpace(element.ParentId))
                    continue;

                if (string.Equals(element.Id, element.ParentId, StringComparison.Ordinal))
                {
                    issues.Add(screen.Id + "/" + element.Id + ": элемент не может быть родителем самому себе.");
                    continue;
                }

                if (screen.FindElement(element.ParentId) == null)
                {
                    issues.Add(screen.Id + "/" + element.Id + ": не найден родитель '" + element.ParentId + "'.");
                    continue;
                }

                if (HasParentCycle(screen, element))
                    issues.Add(screen.Id + "/" + element.Id + ": обнаружен цикл в иерархии родителей.");
            }
        }

        private static bool HasParentCycle(
            UILayoutScreenDefinition screen,
            UILayoutElementDefinition start)
        {
            HashSet<string> visited = new HashSet<string>(StringComparer.Ordinal);
            UILayoutElementDefinition current = start;
            while (current != null)
            {
                if (!visited.Add(current.Id))
                    return true;
                if (string.IsNullOrWhiteSpace(current.ParentId))
                    return false;
                current = screen.FindElement(current.ParentId);
            }

            return false;
        }

        /// <summary>
        /// Схема обязательных элементов задаётся данными экрана. Для экрана
        /// диалога сохранена встроенная схема на случай, если данные пусты.
        /// </summary>
        private static void ValidateRequiredElements(
            UILayoutScreenDefinition screen,
            List<string> issues)
        {
            IReadOnlyList<UILayoutRequiredElement> required = screen.RequiredElements;
            if (required.Count > 0)
            {
                for (int i = 0; i < required.Count; i++)
                {
                    UILayoutRequiredElement entry = required[i];
                    if (entry == null || string.IsNullOrWhiteSpace(entry.ElementId))
                        continue;
                    ValidateRequiredElement(screen, issues, entry.ElementId, entry.ExpectedParentId);
                }

                return;
            }

            if (!string.Equals(screen.Id, NarrativeDialogueScreenId, StringComparison.Ordinal))
                return;

            ValidateRequiredElement(screen, issues, "overlay", string.Empty);
            ValidateRequiredElement(screen, issues, "panel", "overlay");
            ValidateRequiredElement(screen, issues, "portrait", "overlay");
            ValidateRequiredElement(screen, issues, "speaker", "overlay");
            ValidateRequiredElement(screen, issues, "role", "overlay");
            ValidateRequiredElement(screen, issues, "text", "panel");
            ValidateRequiredElement(screen, issues, "choices", "panel");
        }

        private static void ValidateRequiredElement(
            UILayoutScreenDefinition screen,
            List<string> issues,
            string elementId,
            string expectedParentId)
        {
            UILayoutElementDefinition element = screen.FindElement(elementId);
            if (element == null)
            {
                issues.Add(screen.Id + ": отсутствует обязательный элемент '" + elementId + "'.");
                return;
            }

            if (!string.Equals(element.ParentId, expectedParentId, StringComparison.Ordinal))
            {
                string expected = string.IsNullOrWhiteSpace(expectedParentId)
                    ? "корень экрана"
                    : "'" + expectedParentId + "'";
                issues.Add(
                    screen.Id + "/" + elementId +
                    ": ожидаемый родитель — " + expected +
                    ", текущий — '" + element.ParentId + "'.");
            }
        }
    }
}

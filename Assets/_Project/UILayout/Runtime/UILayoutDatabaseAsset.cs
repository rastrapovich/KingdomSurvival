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

    [Serializable]
    public sealed class UILayoutElementDefinition
    {
        [SerializeField] private string id = string.Empty;
        [SerializeField] private string displayName = string.Empty;
        [SerializeField] private Rect rect = new Rect(0f, 0f, 320f, 180f);
        [SerializeField] private Sprite sprite;
        [SerializeField] private Texture2D texture;
        [SerializeField] private UILayoutImageMode imageMode = UILayoutImageMode.Cover;
        [SerializeField, Min(0.05f)] private float imageScale = 1f;
        [SerializeField] private Vector2 imageOffset = Vector2.zero;
        [SerializeField] private Color tint = Color.white;
        [SerializeField, Range(0f, 1f)] private float opacity = 1f;

        public string Id => id;
        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? id : displayName;
        public Rect Rect => rect;
        public Sprite Sprite => sprite;
        public Texture2D Texture => texture;
        public UILayoutImageMode ImageMode => imageMode;
        public float ImageScale => Mathf.Max(0.05f, imageScale);
        public Vector2 ImageOffset => imageOffset;
        public Color Tint => tint;
        public float Opacity => Mathf.Clamp01(opacity);

        public void SetRect(Rect value) => rect = value;
        public void SetImageScale(float value) => imageScale = Mathf.Max(0.05f, value);
        public void SetImageOffset(Vector2 value) => imageOffset = value;
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
        [SerializeField] private List<UILayoutElementDefinition> elements = new List<UILayoutElementDefinition>();

        public string Id => id;
        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? id : displayName;
        public IReadOnlyList<UILayoutElementDefinition> Elements => elements;

        public UILayoutElementDefinition FindElement(string elementId)
        {
            if (string.IsNullOrWhiteSpace(elementId))
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

        [SerializeField] private Vector2Int referenceResolution = new Vector2Int(1920, 1080);
        [SerializeField] private List<UILayoutScreenDefinition> screens = new List<UILayoutScreenDefinition>();

        public Vector2Int ReferenceResolution => referenceResolution;
        public IReadOnlyList<UILayoutScreenDefinition> Screens => screens;

        public UILayoutScreenDefinition FindScreen(string screenId)
        {
            if (string.IsNullOrWhiteSpace(screenId))
                return null;

            for (int i = 0; i < screens.Count; i++)
            {
                UILayoutScreenDefinition screen = screens[i];
                if (screen != null && string.Equals(screen.Id, screenId, StringComparison.Ordinal))
                    return screen;
            }

            return null;
        }

        public void CollectValidationIssues(List<string> issues)
        {
            if (issues == null)
                throw new ArgumentNullException(nameof(issues));

            issues.Clear();
            HashSet<string> screenIds = new HashSet<string>(StringComparer.Ordinal);
            for (int screenIndex = 0; screenIndex < screens.Count; screenIndex++)
            {
                UILayoutScreenDefinition screen = screens[screenIndex];
                if (screen == null || string.IsNullOrWhiteSpace(screen.Id))
                {
                    issues.Add("Экран #" + (screenIndex + 1) + ": отсутствует ID.");
                    continue;
                }

                if (!screenIds.Add(screen.Id))
                    issues.Add("Повторяющийся ID экрана: " + screen.Id + ".");

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
                }
            }
        }
    }
}

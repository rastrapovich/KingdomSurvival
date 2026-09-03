using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace KingdomSurvival.UnitDatabase.Editor
{
    [InitializeOnLoad]
    internal static class BattlefieldAnchorPreviewOverlay
    {
        private const string MarkerName = "battlefield-anchor-marker";
        private const float AnchorFromBottom = 0.15f;
        private const float MarkerSize = 10f;
        private const float PreviewViewportWidth = 260f;
        private const float PreviewViewportHeight = 320f;
        private const float PreviewCardWidth = 300f;
        private const float PreviewCardHeight = 370f;
        private const float BaseSpriteBoxSize = 220f;
        private const float AnchorViewportY = 0.68f;

        private static readonly BindingFlags FieldFlags =
            BindingFlags.Instance | BindingFlags.NonPublic;

        private static readonly FieldInfo BattlefieldPreviewField =
            typeof(UnitDatabaseWindow).GetField("battlefieldPreview", FieldFlags);
        private static readonly FieldInfo DatabaseField =
            typeof(UnitDatabaseWindow).GetField("database", FieldFlags);
        private static readonly FieldInfo SelectedUnitIndexField =
            typeof(UnitDatabaseWindow).GetField("selectedUnitIndex", FieldFlags);

        static BattlefieldAnchorPreviewOverlay()
        {
            EditorApplication.update -= Update;
            EditorApplication.update += Update;
        }

        private static void Update()
        {
            UnitDatabaseWindow[] windows = Resources.FindObjectsOfTypeAll<UnitDatabaseWindow>();
            for (int i = 0; i < windows.Length; i++)
                UpdateWindow(windows[i]);
        }

        private static void UpdateWindow(UnitDatabaseWindow window)
        {
            if (window == null || BattlefieldPreviewField == null ||
                DatabaseField == null || SelectedUnitIndexField == null)
            {
                return;
            }

            Image preview = BattlefieldPreviewField.GetValue(window) as Image;
            UnitDatabaseAsset database = DatabaseField.GetValue(window) as UnitDatabaseAsset;
            int selectedIndex = (int)SelectedUnitIndexField.GetValue(window);
            if (preview == null || preview.parent == null || database == null ||
                selectedIndex < 0 || selectedIndex >= database.Units.Count)
            {
                return;
            }

            UnitDefinitionData unit = database.Units[selectedIndex];
            VisualElement viewport = preview.parent;
            VisualElement card = viewport.parent;
            VisualElement previewsRow = card != null ? card.parent : null;

            StyleWorkspace(viewport, card, previewsRow);

            float viewportWidth = viewport.resolvedStyle.width;
            float viewportHeight = viewport.resolvedStyle.height;
            if (viewportWidth <= 1f || viewportHeight <= 1f)
                return;

            float safeScale = Mathf.Max(0.1f, unit.BattlefieldScale);
            float spriteBoxSize = BaseSpriteBoxSize * safeScale;
            Vector2 offset = unit.BattlefieldOffset;
            float anchorX = viewportWidth * 0.5f;
            float anchorY = viewportHeight * AnchorViewportY;

            preview.style.left = anchorX - spriteBoxSize * 0.5f + offset.x;
            preview.style.top = anchorY - spriteBoxSize * (1f - AnchorFromBottom) + offset.y;
            preview.style.right = StyleKeyword.Auto;
            preview.style.bottom = StyleKeyword.Auto;
            preview.style.width = spriteBoxSize;
            preview.style.height = spriteBoxSize;
            preview.style.scale = new Scale(Vector3.one);
            preview.transform.position = Vector3.zero;

            VisualElement marker = viewport.Q<VisualElement>(MarkerName);
            if (marker == null)
            {
                marker = new VisualElement
                {
                    name = MarkerName,
                    pickingMode = PickingMode.Ignore,
                    tooltip = "Красная точка — центр гекса. Опорная точка миниатюры находится на 15% выше её нижнего края."
                };
                marker.style.position = Position.Absolute;
                marker.style.width = MarkerSize;
                marker.style.height = MarkerSize;
                marker.style.backgroundColor = new Color(0.95f, 0.08f, 0.06f, 1f);
                marker.style.borderTopLeftRadius = MarkerSize * 0.5f;
                marker.style.borderTopRightRadius = MarkerSize * 0.5f;
                marker.style.borderBottomLeftRadius = MarkerSize * 0.5f;
                marker.style.borderBottomRightRadius = MarkerSize * 0.5f;
                marker.style.borderLeftWidth = 1f;
                marker.style.borderRightWidth = 1f;
                marker.style.borderTopWidth = 1f;
                marker.style.borderBottomWidth = 1f;
                Color outline = new Color(0.18f, 0.02f, 0.02f, 1f);
                marker.style.borderLeftColor = outline;
                marker.style.borderRightColor = outline;
                marker.style.borderTopColor = outline;
                marker.style.borderBottomColor = outline;
                viewport.Add(marker);
            }

            marker.style.left = anchorX - MarkerSize * 0.5f;
            marker.style.top = anchorY - MarkerSize * 0.5f;
            marker.BringToFront();
        }

        private static void StyleWorkspace(
            VisualElement viewport,
            VisualElement card,
            VisualElement previewsRow)
        {
            viewport.style.width = PreviewViewportWidth;
            viewport.style.height = PreviewViewportHeight;
            viewport.style.minWidth = PreviewViewportWidth;
            viewport.style.minHeight = PreviewViewportHeight;
            viewport.style.overflow = Overflow.Visible;

            if (card != null)
            {
                card.style.width = PreviewCardWidth;
                card.style.height = PreviewCardHeight;
                card.style.minWidth = PreviewCardWidth;
                card.style.minHeight = PreviewCardHeight;
                card.style.flexShrink = 0f;
                card.style.overflow = Overflow.Visible;
            }

            if (previewsRow != null)
            {
                previewsRow.style.height = PreviewCardHeight + 24f;
                previewsRow.style.minHeight = PreviewCardHeight + 24f;
                previewsRow.style.alignItems = Align.FlexStart;
                previewsRow.style.overflow = Overflow.Visible;
            }
        }
    }
}

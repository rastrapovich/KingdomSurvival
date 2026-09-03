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
            float viewportWidth = viewport.resolvedStyle.width;
            float viewportHeight = viewport.resolvedStyle.height;
            if (viewportWidth <= 1f || viewportHeight <= 1f)
                return;

            float safeScale = Mathf.Max(0.1f, unit.BattlefieldScale);
            float spriteBoxSize = Mathf.Min(viewportWidth, viewportHeight) * safeScale;
            Vector2 offset = unit.BattlefieldOffset;
            float centerX = viewportWidth * 0.5f;
            float centerY = viewportHeight * 0.5f;

            preview.style.left = centerX - spriteBoxSize * 0.5f + offset.x;
            preview.style.top = centerY - spriteBoxSize * (1f - AnchorFromBottom) + offset.y;
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

            marker.style.left = centerX - MarkerSize * 0.5f;
            marker.style.top = centerY - MarkerSize * 0.5f;
            marker.BringToFront();
        }
    }
}

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace KingdomSurvival.BattleSandbox
{
    internal static class BattlefieldUnitAnchorAdjustmentBootstrap
    {
        private const string BattleSandboxSceneName = "BattleSandbox";
        private static GameObject runnerObject;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Initialize()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            EnsureRunnerFor(SceneManager.GetActiveScene());
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            EnsureRunnerFor(scene);
        }

        private static void EnsureRunnerFor(Scene scene)
        {
            if (!string.Equals(scene.name, BattleSandboxSceneName, System.StringComparison.Ordinal))
            {
                if (runnerObject != null)
                    Object.Destroy(runnerObject);
                runnerObject = null;
                return;
            }

            if (runnerObject != null)
                return;

            runnerObject = new GameObject("Battlefield Unit Anchor Adjustment");
            runnerObject.hideFlags = HideFlags.HideInHierarchy;
            runnerObject.AddComponent<BattlefieldUnitAnchorAdjustment>();
        }
    }

    [DefaultExecutionOrder(10000)]
    internal sealed class BattlefieldUnitAnchorAdjustment : MonoBehaviour
    {
        internal const float AnchorFromBottom = 0.15f;
        private const string BoardName = "battle-sandbox-board";
        private const float HealthBarHeight = 4.2f;
        private const float HealthBarBottomInset = 8f;

        private void LateUpdate()
        {
            UIDocument[] documents = Object.FindObjectsByType<UIDocument>(FindObjectsSortMode.None);
            for (int i = 0; i < documents.Length; i++)
            {
                VisualElement root = documents[i] != null ? documents[i].rootVisualElement : null;
                VisualElement board = root?.Q<VisualElement>(BoardName);
                if (board == null)
                    continue;

                ApplyAnchor(board);
                return;
            }
        }

        private static void ApplyAnchor(VisualElement board)
        {
            List<Image> unitImages = new List<Image>();
            List<VisualElement> healthBars = new List<VisualElement>();

            foreach (VisualElement child in board.Children())
            {
                if (child is Image image && image.sprite != null)
                {
                    unitImages.Add(image);
                    continue;
                }

                if (IsUnitHealthBar(child))
                    healthBars.Add(child);
            }

            for (int i = 0; i < unitImages.Count; i++)
            {
                Image image = unitImages[i];
                float spriteHeight = image.resolvedStyle.height;
                if (spriteHeight <= 1f)
                    continue;

                // The anchor sits 15% above the sprite's bottom edge.
                // Matching that anchor to the logical hex center moves the whole
                // miniature slightly lower than the previous bottom-edge anchor.
                float verticalShift = -spriteHeight * (0.5f - AnchorFromBottom);
                image.transform.position = new Vector3(0f, verticalShift, 0f);

                VisualElement healthBar = FindNearestHealthBar(image, healthBars);
                if (healthBar != null)
                    healthBar.transform.position = new Vector3(0f, verticalShift, 0f);
            }
        }

        private static bool IsUnitHealthBar(VisualElement element)
        {
            if (element == null || element.childCount != 1)
                return false;

            float height = element.resolvedStyle.height;
            return Mathf.Abs(height - HealthBarHeight) <= 1.5f;
        }

        private static VisualElement FindNearestHealthBar(
            Image image,
            List<VisualElement> healthBars)
        {
            if (image == null || healthBars == null || healthBars.Count == 0)
                return null;

            float imageCenterX = image.resolvedStyle.left + image.resolvedStyle.width * 0.5f;
            float expectedBarY = image.resolvedStyle.top + image.resolvedStyle.height -
                                 HealthBarBottomInset - HealthBarHeight * 0.5f;
            VisualElement best = null;
            float bestDistance = float.MaxValue;

            for (int i = 0; i < healthBars.Count; i++)
            {
                VisualElement bar = healthBars[i];
                float barCenterX = bar.resolvedStyle.left + bar.resolvedStyle.width * 0.5f;
                float barCenterY = bar.resolvedStyle.top + bar.resolvedStyle.height * 0.5f;
                float dx = barCenterX - imageCenterX;
                float dy = barCenterY - expectedBarY;
                float distance = dx * dx + dy * dy;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = bar;
                }
            }

            return best;
        }
    }
}

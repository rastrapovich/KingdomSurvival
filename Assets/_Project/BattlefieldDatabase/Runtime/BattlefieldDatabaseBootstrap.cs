using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace KingdomSurvival.BattlefieldDatabase
{
    internal static class BattlefieldDatabaseBootstrap
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
            if (!string.Equals(scene.name, BattleSandboxSceneName, StringComparison.Ordinal))
            {
                if (runnerObject != null)
                    UnityEngine.Object.Destroy(runnerObject);
                runnerObject = null;
                return;
            }

            if (runnerObject != null)
                return;

            runnerObject = new GameObject("Battlefield Database Runtime");
            runnerObject.hideFlags = HideFlags.HideInHierarchy;
            runnerObject.AddComponent<BattlefieldBackgroundRunner>();
        }
    }

    internal sealed class BattlefieldBackgroundRunner : MonoBehaviour
    {
        private const string BoardElementName = "battle-sandbox-board";
        private const string SurfaceElementName = "battlefield-surface";
        private const string BackgroundElementName = "battlefield-background";

        private BattlefieldDatabaseAsset database;
        private VisualElement wrappedSurface;

        private void Awake()
        {
            database = Resources.Load<BattlefieldDatabaseAsset>(BattlefieldDatabaseAsset.ResourcesPath);
        }

        private void Update()
        {
            if (database == null)
                return;

            if (wrappedSurface != null && wrappedSurface.panel != null)
                return;
            wrappedSurface = null;

            BattlefieldDefinitionData battlefield = database.GetSandboxBattlefield();
            if (battlefield == null || battlefield.Background == null)
                return;

            UIDocument[] documents = UnityEngine.Object.FindObjectsByType<UIDocument>(
                FindObjectsSortMode.None);
            for (int i = 0; i < documents.Length; i++)
            {
                VisualElement root = documents[i] != null
                    ? documents[i].rootVisualElement
                    : null;
                if (root == null)
                    continue;

                VisualElement board = root.Q<VisualElement>(BoardElementName);
                if (board == null)
                    continue;
                if (board.parent != null && board.parent.name == SurfaceElementName)
                    continue;

                wrappedSurface = WrapBoard(board, battlefield);
                if (wrappedSurface != null)
                    return;
            }
        }

        private static VisualElement WrapBoard(
            VisualElement board,
            BattlefieldDefinitionData battlefield)
        {
            VisualElement parent = board.parent;
            if (parent == null)
                return null;

            int index = parent.IndexOf(board);
            if (index < 0)
                return null;

            board.RemoveFromHierarchy();

            VisualElement surface = new VisualElement
            {
                name = SurfaceElementName,
                pickingMode = PickingMode.Ignore
            };
            surface.style.flexGrow = 1f;
            surface.style.flexShrink = 1f;
            surface.style.minWidth = 620f;
            surface.style.minHeight = 520f;
            surface.style.marginRight = 14f;
            surface.style.position = Position.Relative;
            surface.style.overflow = Overflow.Hidden;
            surface.style.backgroundColor = new Color(0.055f, 0.065f, 0.075f, 1f);

            Image background = new Image
            {
                name = BackgroundElementName,
                pickingMode = PickingMode.Ignore,
                scaleMode = ScaleMode.ScaleAndCrop,
                sprite = battlefield.Background
            };
            background.style.position = Position.Absolute;
            background.style.left = 0f;
            background.style.right = 0f;
            background.style.top = 0f;
            background.style.bottom = 0f;

            board.style.marginRight = 0f;
            board.style.position = Position.Absolute;
            board.style.left = 0f;
            board.style.right = 0f;
            board.style.top = 0f;
            board.style.bottom = 0f;
            board.style.backgroundColor = Color.clear;

            // Фон всегда находится под интерактивной сеткой. Никаких изменений
            // private/static полей через reflection и никаких overlay-fallback.
            surface.Add(background);
            surface.Add(board);
            parent.Insert(index, surface);

            Action applyFraming = () =>
            {
                float scale = battlefield.BackgroundScale;
                Vector2 offset = battlefield.BackgroundOffset;
                background.style.scale = new Scale(new Vector3(scale, scale, 1f));
                background.transform.position = new Vector3(
                    offset.x * surface.contentRect.width,
                    offset.y * surface.contentRect.height,
                    0f);
            };

            surface.RegisterCallback<GeometryChangedEvent>(_ => applyFraming());
            applyFraming();
            return surface;
        }
    }
}

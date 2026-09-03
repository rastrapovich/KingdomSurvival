using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace KingdomSurvival.BattleSandbox
{
    [DefaultExecutionOrder(12000)]
    internal sealed class BattlefieldTerrainDetailsOverlay : MonoBehaviour
    {
        private static readonly BindingFlags InstanceFlags =
            BindingFlags.Instance | BindingFlags.NonPublic;

        private static readonly FieldInfo BattleField =
            typeof(HexBoardElement).GetField("battle", InstanceFlags);
        private static readonly MethodInfo TryGetHexAtMethod =
            typeof(HexBoardElement).GetMethod("TryGetHexAt", InstanceFlags);

        private HexBoardElement board;
        private VisualElement root;
        private VisualElement popup;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (SceneManager.GetActiveScene().name != "BattleSandbox")
                return;

            if (FindFirstObjectByType<BattlefieldTerrainDetailsOverlay>() != null)
                return;

            GameObject host = new GameObject("BattlefieldTerrainDetailsOverlay");
            host.hideFlags = HideFlags.HideAndDontSave;
            DontDestroyOnLoad(host);
            host.AddComponent<BattlefieldTerrainDetailsOverlay>();
        }

        private void Update()
        {
            if (SceneManager.GetActiveScene().name != "BattleSandbox")
            {
                Destroy(gameObject);
                return;
            }

            if (board != null && board.panel != null && root != null)
                return;

            AttachToBoard();
        }

        private void OnDisable()
        {
            DetachFromBoard();
            ClosePopup();
        }

        private void AttachToBoard()
        {
            UIDocument[] documents = FindObjectsByType<UIDocument>(FindObjectsSortMode.None);
            for (int i = 0; i < documents.Length; i++)
            {
                VisualElement candidateRoot = documents[i] != null
                    ? documents[i].rootVisualElement
                    : null;
                HexBoardElement candidateBoard = candidateRoot != null
                    ? candidateRoot.Q<HexBoardElement>("battle-sandbox-board")
                    : null;
                if (candidateBoard == null)
                    continue;

                DetachFromBoard();
                root = candidateRoot;
                board = candidateBoard;
                board.RegisterCallback<PointerDownEvent>(OnBoardPointerDown);
                return;
            }
        }

        private void DetachFromBoard()
        {
            if (board != null)
                board.UnregisterCallback<PointerDownEvent>(OnBoardPointerDown);

            board = null;
            root = null;
        }

        private void OnBoardPointerDown(PointerDownEvent evt)
        {
            if (evt.button != 1 || board == null || root == null ||
                BattleField == null || TryGetHexAtMethod == null)
            {
                return;
            }

            SandboxBattle battle = BattleField.GetValue(board) as SandboxBattle;
            if (battle == null)
                return;

            object[] args =
            {
                new Vector2(evt.localPosition.x, evt.localPosition.y),
                default(HexCoord)
            };
            bool found = (bool)TryGetHexAtMethod.Invoke(board, args);
            if (!found)
                return;

            HexCoord coord = (HexCoord)args[1];
            if (battle.GetUnitAt(coord) != null)
                return;
            if (battle.GetTerrain(coord) != SandboxTerrain.Difficult)
                return;

            Vector2 rootPosition = root.WorldToLocal(evt.position);
            ShowHillPopup(rootPosition);
            evt.StopPropagation();
        }

        private void ShowHillPopup(Vector2 position)
        {
            ClosePopup();

            popup = new VisualElement
            {
                name = "battlefield-hill-info-popup",
                pickingMode = PickingMode.Position
            };
            popup.style.position = Position.Absolute;
            popup.style.width = 340f;
            popup.style.paddingLeft = 18f;
            popup.style.paddingRight = 18f;
            popup.style.paddingTop = 16f;
            popup.style.paddingBottom = 16f;
            popup.style.backgroundColor = new Color(0.055f, 0.060f, 0.055f, 0.97f);
            popup.style.borderLeftWidth = 1f;
            popup.style.borderRightWidth = 1f;
            popup.style.borderTopWidth = 1f;
            popup.style.borderBottomWidth = 1f;
            Color border = new Color(0.55f, 0.46f, 0.28f, 1f);
            popup.style.borderLeftColor = border;
            popup.style.borderRightColor = border;
            popup.style.borderTopColor = border;
            popup.style.borderBottomColor = border;
            popup.style.borderTopLeftRadius = 5f;
            popup.style.borderTopRightRadius = 5f;
            popup.style.borderBottomLeftRadius = 5f;
            popup.style.borderBottomRightRadius = 5f;

            Label title = new Label("ХОЛМ");
            title.style.fontSize = 18f;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.color = new Color(0.93f, 0.80f, 0.55f, 1f);
            popup.Add(title);

            Label description = new Label(
                "Труднопроходимая возвышенность. Даёт оборонительное преимущество и улучшает позицию стрелков.");
            description.style.marginTop = 8f;
            description.style.fontSize = 12f;
            description.style.whiteSpace = WhiteSpace.Normal;
            description.style.color = new Color(0.78f, 0.76f, 0.69f, 1f);
            popup.Add(description);

            Label movement = new Label(
                "Стоимость входа: " + SandboxTerrainRules.HillMovementCost + " движения");
            movement.style.marginTop = 12f;
            movement.style.fontSize = 12f;
            movement.style.color = new Color(0.86f, 0.82f, 0.70f, 1f);
            popup.Add(movement);

            Label defense = new Label(
                "Защита бойца на холме: +" + SandboxTerrainRules.HillDefenseBonus);
            defense.style.marginTop = 4f;
            defense.style.fontSize = 12f;
            defense.style.unityFontStyleAndWeight = FontStyle.Bold;
            defense.style.color = new Color(0.90f, 0.75f, 0.48f, 1f);
            popup.Add(defense);

            Label range = new Label(
                "Дальний бой: +" + SandboxTerrainRules.HillRangedAttackRangeBonus + " к дальности");
            range.style.marginTop = 4f;
            range.style.fontSize = 12f;
            range.style.unityFontStyleAndWeight = FontStyle.Bold;
            range.style.color = new Color(0.48f, 0.82f, 0.53f, 1f);
            popup.Add(range);

            Button close = new Button(ClosePopup) { text = "ПОНЯТНО" };
            close.style.marginTop = 14f;
            close.style.height = 30f;
            close.style.unityFontStyleAndWeight = FontStyle.Bold;
            popup.Add(close);

            root.Add(popup);
            popup.BringToFront();

            float rootWidth = root.resolvedStyle.width;
            float rootHeight = root.resolvedStyle.height;
            float maxLeft = Mathf.Max(12f, rootWidth - 352f);
            float maxTop = Mathf.Max(12f, rootHeight - 250f);
            popup.style.left = Mathf.Clamp(position.x + 12f, 12f, maxLeft);
            popup.style.top = Mathf.Clamp(position.y + 12f, 12f, maxTop);
        }

        private void ClosePopup()
        {
            if (popup == null)
                return;

            popup.RemoveFromHierarchy();
            popup = null;
        }
    }
}

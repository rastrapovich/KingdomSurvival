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
        // Одна панель на весь бой: несколько экземпляров оверлея (и повторные подписки) не плодят окна.
        private static VisualElement popup;
        private static long lastHandledEvent = -1;

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
            // ПР-12К: и во встроенном бою на месте (основная сцена).
            if (!BattleSandboxController.IsBattleContextActive)
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
            if (evt.timestamp == lastHandledEvent)
                return;
            lastHandledEvent = evt.timestamp;

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
            bool hill = battle.GetTerrain(coord) == SandboxTerrain.Difficult;
            // Бриф «Поверхности арены»: основание и растительность — описание
            // клетки для бойца, чей ход (вход, бонусы, штрафы, исключения).
            bool surface = battle.GetGround(coord) != SandboxGround.Default || battle.GetVegetation(coord) != SandboxVegetation.None ||
                           battle.EffectsAt(coord).Count > 0 || battle.TerrainObjectAt(coord) != null;
            bool blocked = battle.GetTerrain(coord) == SandboxTerrain.Impassable ||
                           (battle.CurrentUnit != null && !battle.IsEnterableFor(battle.CurrentUnit, coord));

            // Описание есть у любой свободной клетки: препятствие — что пройти нельзя,
            // пустое поле — что особых эффектов нет.
            string details = null;
            if (surface || !hill)
            {
                details = battle.DescribeCell(battle.CurrentUnit, coord);
                if (!hill && !surface && !blocked)
                    details += " Особых эффектов нет.";
            }

            Vector2 rootPosition = root.WorldToLocal(evt.position);
            ShowHillPopup(rootPosition, hill, details, blocked);
            evt.StopPropagation();
        }

        private void ShowHillPopup(Vector2 position, bool hill = true, string details = null, bool blocked = false)
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

            Label title = new Label(blocked ? "ПРЕПЯТСТВИЕ" : hill ? "ХОЛМ" : "МЕСТНОСТЬ");
            title.style.fontSize = 18f;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.color = new Color(0.93f, 0.80f, 0.55f, 1f);
            popup.Add(title);

            AddDetailRows(details ??
                "Труднопроходимая возвышенность. Даёт оборонительное преимущество и улучшает позицию стрелков.");
            if (hill && details == null)
                AddHillLines();

            Button close = new Button(ClosePopup) { text = "ПОНЯТНО" };
            FinishPopup(close, position);
        }

        // Описание клетки строками: у каждой — значок по смыслу (защита, вход, дальность, опасность).
        private void AddDetailRows(string text)
        {
            string head = null;
            int colon = text.IndexOf(": ", System.StringComparison.Ordinal);
            if (colon > 0 && colon < 40)
            {
                head = text.Substring(0, colon);
                text = text.Substring(colon + 2);
            }
            if (head != null)
            {
                Label name = new Label(head);
                name.style.marginTop = 6f;
                name.style.fontSize = 13f;
                name.style.color = new Color(0.88f, 0.84f, 0.72f, 1f);
                popup.Add(name);
            }
            foreach (string raw in text.TrimEnd('.').Split(new[] { "; " }, System.StringSplitOptions.RemoveEmptyEntries))
                popup.Add(DetailRow(raw.Trim()));
        }

        private static VisualElement DetailRow(string line)
        {
            string lower = line.ToLowerInvariant();
            string glyph = "•";
            Color color = new Color(0.45f, 0.45f, 0.42f, 1f);
            if (lower.Contains("защит")) { glyph = "З"; color = new Color(0.32f, 0.48f, 0.72f, 1f); }
            else if (lower.Contains("вход") || lower.Contains("пройти") || lower.Contains("непроходим")) { glyph = "Х"; color = new Color(0.36f, 0.58f, 0.36f, 1f); }
            else if (lower.Contains("дальн") || lower.Contains("снаряд")) { glyph = "Д"; color = new Color(0.72f, 0.58f, 0.28f, 1f); }
            else if (lower.Contains("hp") || lower.Contains("огонь") || lower.Contains("яд")) { glyph = "!"; color = new Color(0.74f, 0.32f, 0.26f, 1f); }
            VisualElement row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginTop = 6f;
            Label icon = new Label(glyph);
            icon.style.width = 22f;
            icon.style.height = 22f;
            icon.style.flexShrink = 0f;
            icon.style.unityTextAlign = TextAnchor.MiddleCenter;
            icon.style.unityFontStyleAndWeight = FontStyle.Bold;
            icon.style.fontSize = 12f;
            icon.style.color = Color.white;
            icon.style.backgroundColor = color;
            icon.style.borderTopLeftRadius = icon.style.borderTopRightRadius = icon.style.borderBottomLeftRadius = icon.style.borderBottomRightRadius = 11f;
            icon.style.marginRight = 8f;
            row.Add(icon);
            Label text = new Label(char.ToUpperInvariant(line[0]) + line.Substring(1));
            text.style.fontSize = 12f;
            text.style.whiteSpace = WhiteSpace.Normal;
            text.style.flexShrink = 1f;
            text.style.color = new Color(0.82f, 0.80f, 0.73f, 1f);
            row.Add(text);
            return row;
        }

        private void AddHillLines()
        {
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
        }

        private void FinishPopup(Button close, Vector2 position)
        {
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

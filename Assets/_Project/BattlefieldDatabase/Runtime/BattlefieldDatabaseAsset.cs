using System;
using System.Collections.Generic;
using UnityEngine;

namespace KingdomSurvival.BattlefieldDatabase
{
    [Serializable]
    public sealed class BattlefieldTagDefinition
    {
        [SerializeField] private string id = string.Empty;
        [SerializeField] private string displayLabel = string.Empty;
        [SerializeField] private string category = string.Empty;
        [SerializeField] private Color color = Color.gray;
        [SerializeField, TextArea(2, 4)] private string description = string.Empty;

        public string Id => id;
        public string DisplayLabel => displayLabel;
        public string Category => category;
        public Color Color => color;
        public string Description => description;
    }

    [Serializable]
    public sealed class BattlefieldDefinitionData
    {
        [SerializeField] private string id = string.Empty;
        [SerializeField] private string displayLabel = string.Empty;

        [Header("Фон")]
        [SerializeField] private Sprite background;
        [SerializeField, Min(0.1f)] private float backgroundScale = 1f;
        [SerializeField] private Vector2 backgroundOffset = Vector2.zero;

        [Header("Сетка")]
        [SerializeField, Range(0.5f, 1.5f)] private float gridScale = 1f;
        [SerializeField] private Vector2 gridOffset = Vector2.zero;
        // Гексы, на которые нельзя пойти: (столбец Q, ряд R) арены 7/8/9/10/9/8/7.
        [SerializeField] private List<Vector2Int> disabledCells = new List<Vector2Int>();

        [Header("Вид гекса")]
        [SerializeField] private bool useOwnHexStyle;
        [SerializeField] private BattlefieldHexStyle hexStyle = new BattlefieldHexStyle();

        [Header("Теги")]
        [SerializeField] private List<string> tagIds = new List<string>();

        public string Id => id;
        public string DisplayLabel => displayLabel;
        public Sprite Background => background;
        public float BackgroundScale => Mathf.Max(0.1f, backgroundScale);
        public Vector2 BackgroundOffset => backgroundOffset;
        public float GridScale => Mathf.Clamp(gridScale, 0.5f, 1.5f);
        public Vector2 GridOffset => gridOffset;
        public IReadOnlyList<Vector2Int> DisabledCells =>
            disabledCells ?? (IReadOnlyList<Vector2Int>)Array.Empty<Vector2Int>();
        public bool UseOwnHexStyle => useOwnHexStyle;
        public BattlefieldHexStyle OwnHexStyle => hexStyle;
        public IReadOnlyList<string> TagIds => tagIds;

        public bool IsCellDisabled(int q, int r)
        {
            if (disabledCells == null)
                return false;
            for (int i = 0; i < disabledCells.Count; i++)
            {
                if (disabledCells[i].x == q && disabledCells[i].y == r)
                    return true;
            }
            return false;
        }

        public bool HasTag(string tagId)
        {
            if (string.IsNullOrWhiteSpace(tagId) || tagIds == null)
                return false;

            for (int i = 0; i < tagIds.Count; i++)
            {
                if (string.Equals(tagIds[i], tagId, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }
    }

    [CreateAssetMenu(
        fileName = "KingdomSurvivalBattlefields",
        menuName = "Kingdom Survival/База полей боя")]
    public sealed class BattlefieldDatabaseAsset : ScriptableObject
    {
        public const string ResourcesPath = "BattlefieldDatabase/KingdomSurvivalBattlefields";

        [SerializeField] private string sandboxBattlefieldId = string.Empty;
        [SerializeField] private BattlefieldHexStyle hexStyle = new BattlefieldHexStyle();
        [SerializeField] private List<BattlefieldTagDefinition> tags = new List<BattlefieldTagDefinition>();
        [SerializeField] private List<BattlefieldDefinitionData> battlefields = new List<BattlefieldDefinitionData>();

        public string SandboxBattlefieldId => sandboxBattlefieldId;
        public IReadOnlyList<BattlefieldTagDefinition> Tags => tags;
        public IReadOnlyList<BattlefieldDefinitionData> Battlefields => battlefields;
        // Общий вид гекса для полей без своего стиля.
        public BattlefieldHexStyle HexStyle => hexStyle ?? (hexStyle = new BattlefieldHexStyle());

        public BattlefieldHexStyle GetHexStyle(BattlefieldDefinitionData battlefield)
        {
            return battlefield != null && battlefield.UseOwnHexStyle && battlefield.OwnHexStyle != null
                ? battlefield.OwnHexStyle
                : HexStyle;
        }

        public BattlefieldDefinitionData FindById(string battlefieldId)
        {
            if (string.IsNullOrWhiteSpace(battlefieldId) || battlefields == null)
                return null;

            for (int i = 0; i < battlefields.Count; i++)
            {
                BattlefieldDefinitionData battlefield = battlefields[i];
                if (battlefield != null &&
                    string.Equals(battlefield.Id, battlefieldId, StringComparison.Ordinal))
                {
                    return battlefield;
                }
            }

            return null;
        }

        public BattlefieldTagDefinition FindTag(string tagId)
        {
            if (string.IsNullOrWhiteSpace(tagId) || tags == null)
                return null;

            for (int i = 0; i < tags.Count; i++)
            {
                BattlefieldTagDefinition tag = tags[i];
                if (tag != null && string.Equals(tag.Id, tagId, StringComparison.Ordinal))
                    return tag;
            }

            return null;
        }

        public BattlefieldDefinitionData GetSandboxBattlefield()
        {
            BattlefieldDefinitionData selected = FindById(sandboxBattlefieldId);
            if (selected != null)
                return selected;

            if (battlefields == null)
                return null;

            for (int i = 0; i < battlefields.Count; i++)
            {
                if (battlefields[i] != null)
                    return battlefields[i];
            }

            return null;
        }

        public void CollectValidationIssues(List<string> issues)
        {
            if (issues == null)
                throw new ArgumentNullException(nameof(issues));

            issues.Clear();

            HashSet<string> tagIds = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < tags.Count; i++)
            {
                BattlefieldTagDefinition tag = tags[i];
                if (tag == null || string.IsNullOrWhiteSpace(tag.Id))
                {
                    issues.Add("Тег #" + (i + 1) + ": отсутствует ID.");
                    continue;
                }

                if (!tagIds.Add(tag.Id))
                    issues.Add("Повторяющийся ID тега: " + tag.Id + ".");
            }

            HashSet<string> battlefieldIds = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < battlefields.Count; i++)
            {
                BattlefieldDefinitionData battlefield = battlefields[i];
                if (battlefield == null || string.IsNullOrWhiteSpace(battlefield.Id))
                {
                    issues.Add("Поле #" + (i + 1) + ": отсутствует ID.");
                    continue;
                }

                if (!battlefieldIds.Add(battlefield.Id))
                    issues.Add("Повторяющийся ID поля: " + battlefield.Id + ".");
                if (string.IsNullOrWhiteSpace(battlefield.DisplayLabel))
                    issues.Add(battlefield.Id + ": отсутствует отображаемое название.");
                if (battlefield.Background == null)
                    issues.Add(battlefield.Id + ": не назначено изображение поля.");

                int activeCells = BattlefieldFrame.CountActiveCells(battlefield);
                if (activeCells < BattlefieldFrame.MinActiveCells)
                    issues.Add(battlefield.Id + ": доступно только " + activeCells + " гексов, нужно не меньше " +
                               BattlefieldFrame.MinActiveCells + ".");
                else if (!BattlefieldFrame.AreActiveCellsConnected(battlefield))
                    issues.Add(battlefield.Id + ": доступные гексы разорваны на части — пешие бойцы не дойдут друг до друга.");

                for (int tagIndex = 0; tagIndex < battlefield.TagIds.Count; tagIndex++)
                {
                    string tagId = battlefield.TagIds[tagIndex];
                    if (!tagIds.Contains(tagId))
                        issues.Add(battlefield.Id + ": неизвестный тег " + tagId + ".");
                }
            }

            if (!string.IsNullOrWhiteSpace(sandboxBattlefieldId) &&
                !battlefieldIds.Contains(sandboxBattlefieldId))
            {
                issues.Add("BattleSandbox ссылается на неизвестное поле: " + sandboxBattlefieldId + ".");
            }
        }
    }
}

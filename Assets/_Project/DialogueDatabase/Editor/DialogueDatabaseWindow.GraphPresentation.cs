using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEngine;

namespace KingdomSurvival.DialogueDatabase.Editor
{
    // Информативные ноды Dialogue Graph — вся презентационная логика,
    // которая НЕ зависит от SerializedProperty/IMGUI и поэтому проверяема
    // в EditMode без реального Unity-рендера: снимки узла/ответа/блока,
    // сборка сводок проверок/условий/эффектов, сбор предупреждений и
    // расчёт раскладки (GraphNodeLayoutMetrics). Сам холст (пан/зум/
    // перетаскивание/связи/автораскладка) остаётся в Graph.cs и потребляет
    // эти данные, не дублируя их вычисление.
    public sealed partial class DialogueDatabaseWindow
    {
        public enum GraphDetailMode
        {
            Compact,
            Standard,
            Full
        }

        private const string GraphDetailModePrefKey = "KingdomSurvival.DialogueDatabase.GraphDetailMode";

        private GraphDetailMode graphDetailMode = GraphDetailMode.Standard;

        private void LoadGraphDetailMode()
        {
            graphDetailMode = (GraphDetailMode)EditorPrefs.GetInt(GraphDetailModePrefKey, (int)GraphDetailMode.Standard);
        }

        private void SetGraphDetailMode(GraphDetailMode mode)
        {
            if (graphDetailMode == mode)
                return;

            graphDetailMode = mode;
            EditorPrefs.SetInt(GraphDetailModePrefKey, (int)mode);
        }

        // §29: выбранный узел всегда читается минимум в Standard, даже если
        // общий режим графа — Compact. Полный Full не понижается.
        public static GraphDetailMode ResolveEffectiveGraphDetailMode(GraphDetailMode mode, bool isSelected)
        {
            if (isSelected && mode == GraphDetailMode.Compact)
                return GraphDetailMode.Standard;
            return mode;
        }

        // ------------------------------------------------------------------
        // Снимки данных узла — чистые POCO с публичными полями, чтобы тесты
        // могли строить произвольные сценарии напрямую, без SerializedObject.
        // ------------------------------------------------------------------

        public sealed class GraphTextBlockInfo
        {
            public DialogueTextBlockKind Kind;
            public string Text = string.Empty;
            public NarrativeConditionGroup Conditions;
            public NarrativeCheckSpec PassiveCheck;
            public List<NarrativeEffect> OnRevealEffects = new List<NarrativeEffect>();
        }

        public sealed class GraphChoiceInfo
        {
            public string ChoiceId = string.Empty;
            public string Text = string.Empty;
            public DialogueChoiceKind Kind;
            public NarrativeConditionGroup Conditions;
            public NarrativeCheckSpec Check;
            public string NextNodeId = string.Empty;
            public bool EndsDialogue;
            public string SuccessNodeId = string.Empty;
            public string FailureNodeId = string.Empty;
            public List<NarrativeEffect> SuccessEffects = new List<NarrativeEffect>();
            public List<NarrativeEffect> FailureEffects = new List<NarrativeEffect>();

            public bool IsActiveCheck =>
                Kind == DialogueChoiceKind.ActiveReturnable || Kind == DialogueChoiceKind.ActiveDecisive;

            public bool IsExit => Kind == DialogueChoiceKind.Exit;
        }

        public sealed class GraphNodeInfo
        {
            public string NodeId = string.Empty;
            public string SpeakerId = string.Empty;
            public bool SpeakerKnown;
            public List<GraphTextBlockInfo> TextBlocks = new List<GraphTextBlockInfo>();
            public List<GraphChoiceInfo> Choices = new List<GraphChoiceInfo>();
            public bool IsStart;
            public bool IsReachable = true;
        }

        // ------------------------------------------------------------------
        // Сводки проверок/условий/эффектов — работают напрямую с реальными
        // типами (NarrativeCondition/NarrativeEffect/NarrativeCheckSpec),
        // а не со своими копиями, чтобы формат совпадал с фактическим
        // расчётом и не расходился при будущих изменениях схемы.
        // ------------------------------------------------------------------

        // "ПАССИВНАЯ · СУЖДЕНИЕ · 11" (§9).
        public static string BuildGraphPassiveCheckSummary(NarrativeCheckSpec check)
        {
            if (check == null)
                return string.Empty;
            return "ПАССИВНАЯ · " + BuildGraphCheckMechanicLabel(check) + " · " +
                   check.Difficulty.ToString(CultureInfo.InvariantCulture);
        }

        // "СИЛА + РЕМЕСЛО · 13" — без указания вида проверки: карточка
        // ответа уже подписана "ВОЗВРАТНАЯ ПРОВЕРКА"/"РЕШАЮЩАЯ ПРОВЕРКА"
        // (§16-17). Вероятность успеха намеренно не показывается — она
        // зависит от контекстных модификаторов, не структуры диалога.
        public static string BuildGraphActiveCheckSummary(NarrativeCheckSpec check)
        {
            if (check == null)
                return string.Empty;
            return BuildGraphCheckMechanicLabel(check) + " · " +
                   check.Difficulty.ToString(CultureInfo.InvariantCulture);
        }

        private static string BuildGraphCheckMechanicLabel(NarrativeCheckSpec check)
        {
            string quality = NarrativeQualityLabels.GetLabel(check.Quality).ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(check.CompetencyId))
                return quality;
            return quality + " + " + NarrativeCompetencyLabels.GetLabel(check.CompetencyId).ToUpperInvariant();
        }

        public static string BuildGraphChoiceKindLabel(DialogueChoiceKind kind)
        {
            switch (kind)
            {
                case DialogueChoiceKind.ActiveReturnable: return "ВОЗВРАТНАЯ ПРОВЕРКА";
                case DialogueChoiceKind.ActiveDecisive: return "РЕШАЮЩАЯ ПРОВЕРКА";
                case DialogueChoiceKind.Exit: return "EXIT";
                default: return "ОТВЕТ";
            }
        }

        // Короткие коды условий/эффектов (§10-11) — не дамп сырой структуры,
        // а разговорная сводка вида "FLAG: repair_new" / "NOT FLAG: x".
        public static string BuildGraphConditionSummary(NarrativeCondition condition)
        {
            if (condition == null)
                return string.Empty;

            string not = condition.Negate ? "NOT " : string.Empty;
            switch (condition.Type)
            {
                case NarrativeConditionType.FlagSet:
                    return not + "FLAG: " + condition.StringParam;
                case NarrativeConditionType.KnowledgeKnown:
                    return not + "KNOW: " + condition.StringParam;
                case NarrativeConditionType.RelationAtLeast:
                    return "REL " + condition.StringParam + (condition.Negate ? " < " : " ≥ ") +
                           condition.IntParam.ToString(CultureInfo.InvariantCulture);
                case NarrativeConditionType.RelationAtMost:
                    return "REL " + condition.StringParam + (condition.Negate ? " > " : " ≤ ") +
                           condition.IntParam.ToString(CultureInfo.InvariantCulture);
                case NarrativeConditionType.CompanionPresent:
                    return not + "СПУТНИК: " + condition.StringParam;
                case NarrativeConditionType.ItemPresent:
                    return not + "ПРЕДМЕТ: " + condition.StringParam;
                case NarrativeConditionType.QualityAtLeast:
                    return "КАЧ " + NarrativeQualityLabels.GetLabel(condition.QualityParam).ToUpperInvariant() +
                           (condition.Negate ? " < " : " ≥ ") + condition.IntParam.ToString(CultureInfo.InvariantCulture);
                case NarrativeConditionType.CompetencyAtLeast:
                    return "КОМП " + NarrativeCompetencyLabels.GetLabel(condition.StringParam).ToUpperInvariant() +
                           (condition.Negate ? " < " : " ≥ ") + condition.IntParam.ToString(CultureInfo.InvariantCulture);
                case NarrativeConditionType.CheckSucceeded:
                    return not + "ПРОВЕРКА '" + condition.StringParam + "' УСПЕХ";
                case NarrativeConditionType.CheckFailed:
                    return not + "ПРОВЕРКА '" + condition.StringParam + "' ПРОВАЛ";
                case NarrativeConditionType.CheckNotAttempted:
                    return not + "ПРОВЕРКА '" + condition.StringParam + "' НЕ БЫЛА";
                case NarrativeConditionType.TraitPresent:
                    return not + "ЧЕРТА: " + condition.StringParam;
                default:
                    return condition.Type.ToString();
            }
        }

        // "+ FLAG x" / "− FLAG x" / "+ KNOW y" (§11).
        public static string BuildGraphEffectSummary(NarrativeEffect effect)
        {
            if (effect == null)
                return string.Empty;

            switch (effect.Type)
            {
                case NarrativeEffectType.SetFlag:
                    return "+ FLAG " + effect.StringParam;
                case NarrativeEffectType.ClearFlag:
                    return "− FLAG " + effect.StringParam;
                case NarrativeEffectType.AddKnowledge:
                    return "+ KNOW " + effect.StringParam;
                case NarrativeEffectType.ChangeRelation:
                    return "ОТН " + effect.StringParam + " " +
                           (effect.IntParam >= 0 ? "+" : string.Empty) +
                           effect.IntParam.ToString(CultureInfo.InvariantCulture);
                case NarrativeEffectType.UnlockCheck:
                    return "→ ПРОВЕРКА " + effect.StringParam;
                case NarrativeEffectType.GrantTrait:
                    return "+ ЧЕРТА " + effect.StringParam;
                case NarrativeEffectType.RemoveTrait:
                    return "− ЧЕРТА " + effect.StringParam;
                default:
                    return effect.Type.ToString();
            }
        }

        // Строки условий/эффектов, обрезанные до maxLines с "+ ещё N" —
        // Compact вообще не показывает эти строки (maxLines=0).
        public static void BuildGraphConditionLines(
            NarrativeConditionGroup group,
            int maxLines,
            List<string> outLines,
            out int overflowCount)
        {
            outLines.Clear();
            overflowCount = 0;
            List<NarrativeCondition> conditions = group?.Conditions;
            if (conditions == null || conditions.Count == 0)
                return;

            int shown = Math.Min(conditions.Count, Math.Max(0, maxLines));
            for (int i = 0; i < shown; i++)
            {
                NarrativeCondition condition = conditions[i];
                if (condition != null)
                    outLines.Add(BuildGraphConditionSummary(condition));
            }
            overflowCount = conditions.Count - shown;
        }

        public static void BuildGraphEffectLines(
            IReadOnlyList<NarrativeEffect> effects,
            int maxLines,
            List<string> outLines,
            out int overflowCount)
        {
            outLines.Clear();
            overflowCount = 0;
            if (effects == null || effects.Count == 0)
                return;

            int shown = Math.Min(effects.Count, Math.Max(0, maxLines));
            for (int i = 0; i < shown; i++)
            {
                NarrativeEffect effect = effects[i];
                if (effect != null)
                    outLines.Add(BuildGraphEffectSummary(effect));
            }
            overflowCount = effects.Count - shown;
        }

        public static string BuildOverflowLabel(int overflowCount)
        {
            return overflowCount > 0
                ? "+ ещё " + overflowCount.ToString(CultureInfo.InvariantCulture)
                : null;
        }

        // Обрезка превью текста по границе слова, без разрыва посреди слова
        // там, где это возможно (§8 инструкции по информативным нодам).
        // Используется только в Compact (§3 инструкции "полноценное
        // редактирование нод") — обзорном режиме карты, а не режиме чтения.
        public static string TruncateForGraphPreview(string text, int maxChars)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            string trimmed = text.Trim();
            if (maxChars <= 0 || trimmed.Length <= maxChars)
                return trimmed;

            int cut = maxChars;
            int lastSpace = trimmed.LastIndexOf(' ', Math.Min(cut, trimmed.Length - 1));
            if (lastSpace > maxChars * 0.6)
                cut = lastSpace;

            return trimmed.Substring(0, cut).TrimEnd() + "…";
        }

        // §3 инструкции "полноценное редактирование нод": в Standard/Full
        // литературный текст показывается ПОЛНОСТЬЮ — Compact по-прежнему
        // обрезает (карта дерева, не режим чтения). Один источник истины
        // для того, что реально измеряется (CalcHeight) и что реально
        // рисуется (Label/TextArea) — они обязаны получать одну и ту же
        // строку, иначе высота карточки разойдётся с содержимым.
        public static string ResolveGraphTextForDisplay(string text, GraphDetailMode mode, int compactMaxChars)
        {
            if (mode == GraphDetailMode.Compact)
                return TruncateForGraphPreview(text, compactMaxChars);
            return string.IsNullOrEmpty(text) ? string.Empty : text.Trim();
        }

        private const string EmptyGraphTextPlaceholder = "<пусто>";
        private static GUIStyle narrativeMeasuringStyle;

        // §4 инструкции "полноценное редактирование нод": реальная высота
        // через GUIStyle.CalcHeight (не оценка по числу символов/строк).
        // Стиль-основа — EditorStyles.textArea: и read-only Label
        // невыбранной ноды, и живой TextArea выбранной должны укладываться
        // в один и тот же прямоугольник, посчитанный здесь один раз.
        // Ширина/шрифт передаются в "мировых" (не отмасштабированных
        // graphZoom) единицах — при отрисовке и ширина, и размер шрифта
        // масштабируются на один и тот же graphZoom, поэтому перенос строк
        // не меняется и высоту можно один раз посчитать здесь, а на холсте
        // просто домножить на zoom (тот же приём, что и для остальных
        // размеров в GraphNodeLayoutMetrics).
        public static float ComputeNarrativeTextHeight(string text, float width)
        {
            if (narrativeMeasuringStyle == null)
            {
                narrativeMeasuringStyle = new GUIStyle(EditorStyles.textArea) { wordWrap = true };
            }
            narrativeMeasuringStyle.fontSize = Mathf.RoundToInt(GraphNodeLayoutConstants.NarrativeTextFontSize);

            string content = string.IsNullOrEmpty(text) ? EmptyGraphTextPlaceholder : text;
            return narrativeMeasuringStyle.CalcHeight(new GUIContent(content), Mathf.Max(20f, width));
        }

        // ------------------------------------------------------------------
        // Предупреждения (§25) — та же семантика, что и
        // DialogueDatabaseAsset.CollectDialogueValidationIssues (не второй
        // независимый набор правил, §26), но выражена по одному узлу и
        // короткими фразами, пригодными под тултип бейджа "!".
        // ------------------------------------------------------------------

        public static List<string> CollectGraphNodeWarnings(GraphNodeInfo node, HashSet<string> allNodeIds)
        {
            List<string> warnings = new List<string>();
            if (node == null)
                return warnings;

            if (string.IsNullOrWhiteSpace(node.SpeakerId))
                warnings.Add("Не назначен говорящий.");
            else if (!node.SpeakerKnown)
                warnings.Add("Неизвестный говорящий '" + node.SpeakerId + "'.");

            if (node.TextBlocks == null || node.TextBlocks.Count == 0)
                warnings.Add("Нет текстовых блоков.");

            if (node.Choices == null || node.Choices.Count == 0)
                warnings.Add("Нет вариантов ответа.");

            if (node.Choices != null)
            {
                HashSet<string> choiceIds = new HashSet<string>(StringComparer.Ordinal);
                for (int i = 0; i < node.Choices.Count; i++)
                {
                    GraphChoiceInfo choice = node.Choices[i];
                    if (choice == null)
                        continue;

                    string label = "Ответ #" + (i + 1).ToString(CultureInfo.InvariantCulture);

                    if (!string.IsNullOrWhiteSpace(choice.ChoiceId) && !choiceIds.Add(choice.ChoiceId))
                        warnings.Add(label + ": повторяющийся ChoiceId '" + choice.ChoiceId + "'.");

                    if (choice.IsExit)
                    {
                        if (!string.IsNullOrWhiteSpace(choice.NextNodeId) ||
                            !string.IsNullOrWhiteSpace(choice.SuccessNodeId) ||
                            !string.IsNullOrWhiteSpace(choice.FailureNodeId))
                        {
                            warnings.Add(label + ": EXIT не должен одновременно содержать переход.");
                        }
                        continue;
                    }

                    if (choice.IsActiveCheck)
                    {
                        if (choice.Check == null || string.IsNullOrWhiteSpace(choice.Check.CheckId))
                            warnings.Add(label + ": не задан CheckId проверки.");

                        if (string.IsNullOrWhiteSpace(choice.SuccessNodeId))
                            warnings.Add(label + ": нет ветки успеха.");
                        else if (allNodeIds != null && !allNodeIds.Contains(choice.SuccessNodeId))
                            warnings.Add(label + ": ветка успеха ведёт в отсутствующий узел '" + choice.SuccessNodeId + "'.");

                        if (string.IsNullOrWhiteSpace(choice.FailureNodeId))
                            warnings.Add(label + ": нет ветки провала.");
                        else if (allNodeIds != null && !allNodeIds.Contains(choice.FailureNodeId))
                            warnings.Add(label + ": ветка провала ведёт в отсутствующий узел '" + choice.FailureNodeId + "'.");

                        continue;
                    }

                    if (!choice.EndsDialogue)
                    {
                        if (string.IsNullOrWhiteSpace(choice.NextNodeId))
                            warnings.Add(label + ": нет цели перехода.");
                        else if (allNodeIds != null && !allNodeIds.Contains(choice.NextNodeId))
                            warnings.Add(label + ": переход ведёт в отсутствующий узел '" + choice.NextNodeId + "'.");
                    }
                }
            }

            if (!node.IsStart && !node.IsReachable)
                warnings.Add("Узел недостижим из старта.");

            return warnings;
        }

        // Совпадение по префиксу "Ответ #N: " — те же строки, что и в
        // тултипе бейджа "!", используются повторно, чтобы не считать
        // релевантность предупреждения ответу дважды разными способами.
        public static bool ChoiceHasWarning(List<string> warnings, int choiceIndex)
        {
            if (warnings == null || warnings.Count == 0)
                return false;

            string prefix = "Ответ #" + (choiceIndex + 1).ToString(CultureInfo.InvariantCulture);
            foreach (string warning in warnings)
            {
                if (warning.StartsWith(prefix, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        public static string BuildGraphWarningsTooltip(List<string> warnings)
        {
            if (warnings == null || warnings.Count == 0)
                return string.Empty;
            return string.Join("\n", warnings);
        }

        // ------------------------------------------------------------------
        // Бейджи заголовка (§5) — START рисуется отдельно (уже существует в
        // Graph.cs), здесь — остальные: EXIT/CHECK/COND/FX/KNOW/FLAG/ITEM/
        // UNREACHABLE/"!".
        // ------------------------------------------------------------------

        private static class GraphNodeBadge
        {
            public const string Start = "СТАРТ";
            public const string Exit = "EXIT";
            public const string Check = "CHECK";
            public const string Cond = "COND";
            public const string Fx = "FX";
            public const string Know = "KNOW";
            public const string Flag = "FLAG";
            public const string Item = "ITEM";
            public const string Warning = "!";
            public const string Unreachable = "UNREACHABLE";
        }

        public static List<string> BuildGraphNodeBadges(GraphNodeInfo node, int warningCount)
        {
            List<string> badges = new List<string>();
            if (node == null)
                return badges;

            if (node.IsStart)
                badges.Add(GraphNodeBadge.Start);

            bool hasExit = false, hasCheck = false, hasCond = false, hasFx = false;
            bool hasKnow = false, hasFlag = false, hasItem = false;

            if (node.TextBlocks != null)
            {
                foreach (GraphTextBlockInfo block in node.TextBlocks)
                {
                    if (block == null)
                        continue;

                    if (block.PassiveCheck != null)
                        hasCheck = true;

                    List<NarrativeCondition> conditions = block.Conditions?.Conditions;
                    if (conditions != null && conditions.Count > 0)
                    {
                        hasCond = true;
                        foreach (NarrativeCondition condition in conditions)
                            ClassifyConditionForBadges(condition, ref hasKnow, ref hasFlag, ref hasItem);
                    }

                    if (block.OnRevealEffects != null && block.OnRevealEffects.Count > 0)
                    {
                        hasFx = true;
                        foreach (NarrativeEffect effect in block.OnRevealEffects)
                            ClassifyEffectForBadges(effect, ref hasKnow, ref hasFlag);
                    }
                }
            }

            if (node.Choices != null)
            {
                foreach (GraphChoiceInfo choice in node.Choices)
                {
                    if (choice == null)
                        continue;

                    if (choice.IsExit)
                        hasExit = true;
                    if (choice.IsActiveCheck)
                        hasCheck = true;

                    List<NarrativeCondition> conditions = choice.Conditions?.Conditions;
                    if (conditions != null && conditions.Count > 0)
                    {
                        hasCond = true;
                        foreach (NarrativeCondition condition in conditions)
                            ClassifyConditionForBadges(condition, ref hasKnow, ref hasFlag, ref hasItem);
                    }

                    int effectCount = (choice.SuccessEffects?.Count ?? 0) + (choice.FailureEffects?.Count ?? 0);
                    if (effectCount > 0)
                    {
                        hasFx = true;
                        if (choice.SuccessEffects != null)
                            foreach (NarrativeEffect effect in choice.SuccessEffects)
                                ClassifyEffectForBadges(effect, ref hasKnow, ref hasFlag);
                        if (choice.FailureEffects != null)
                            foreach (NarrativeEffect effect in choice.FailureEffects)
                                ClassifyEffectForBadges(effect, ref hasKnow, ref hasFlag);
                    }
                }
            }

            if (hasExit) badges.Add(GraphNodeBadge.Exit);
            if (hasCheck) badges.Add(GraphNodeBadge.Check);
            if (hasCond) badges.Add(GraphNodeBadge.Cond);
            if (hasFx) badges.Add(GraphNodeBadge.Fx);
            if (hasKnow) badges.Add(GraphNodeBadge.Know);
            if (hasFlag) badges.Add(GraphNodeBadge.Flag);
            if (hasItem) badges.Add(GraphNodeBadge.Item);
            if (!node.IsStart && !node.IsReachable) badges.Add(GraphNodeBadge.Unreachable);
            if (warningCount > 0) badges.Add(GraphNodeBadge.Warning);

            return badges;
        }

        private static void ClassifyConditionForBadges(
            NarrativeCondition condition, ref bool hasKnow, ref bool hasFlag, ref bool hasItem)
        {
            if (condition == null)
                return;
            if (condition.Type == NarrativeConditionType.KnowledgeKnown) hasKnow = true;
            if (condition.Type == NarrativeConditionType.FlagSet) hasFlag = true;
            if (condition.Type == NarrativeConditionType.ItemPresent) hasItem = true;
        }

        private static void ClassifyEffectForBadges(NarrativeEffect effect, ref bool hasKnow, ref bool hasFlag)
        {
            if (effect == null)
                return;
            if (effect.Type == NarrativeEffectType.AddKnowledge) hasKnow = true;
            if (effect.Type == NarrativeEffectType.SetFlag || effect.Type == NarrativeEffectType.ClearFlag) hasFlag = true;
        }

        // Диагностическая строка уровня узла (§24): "CHECK 2 | COND 3 | FX 4".
        public static string BuildGraphNodeDiagnosticSummary(GraphNodeInfo node)
        {
            int checkCount = 0, condCount = 0, fxCount = 0;
            if (node?.TextBlocks != null)
            {
                foreach (GraphTextBlockInfo block in node.TextBlocks)
                {
                    if (block == null)
                        continue;
                    if (block.PassiveCheck != null)
                        checkCount++;
                    condCount += block.Conditions?.Conditions?.Count ?? 0;
                    fxCount += block.OnRevealEffects?.Count ?? 0;
                }
            }

            if (node?.Choices != null)
            {
                foreach (GraphChoiceInfo choice in node.Choices)
                {
                    if (choice == null)
                        continue;
                    if (choice.IsActiveCheck)
                        checkCount++;
                    condCount += choice.Conditions?.Conditions?.Count ?? 0;
                    fxCount += (choice.SuccessEffects?.Count ?? 0) + (choice.FailureEffects?.Count ?? 0);
                }
            }

            return "CHECK " + checkCount.ToString(CultureInfo.InvariantCulture) +
                   " | COND " + condCount.ToString(CultureInfo.InvariantCulture) +
                   " | FX " + fxCount.ToString(CultureInfo.InvariantCulture);
        }

        // ------------------------------------------------------------------
        // Раскладка (§3-5, §30-34 инструкции по информативным нодам; §3-6
        // инструкции "полноценное редактирование нод"): единственный
        // источник истины для отрисовки И hit-test/портов. Высота
        // литературного текста больше не обрезается до фиксированного
        // бюджета символов/строк в Standard/Full — считается реальным
        // GUIStyle.CalcHeight по фактическому тексту, поэтому нода растёт
        // ровно настолько, насколько того требует содержимое. Compact
        // по-прежнему обрезает — это режим обзора дерева, а не чтения сцены.
        // ------------------------------------------------------------------

        public static class GraphNodeLayoutConstants
        {
            public const float HeaderBaseHeight = 30f;
            public const float BadgeRowHeight = 18f;
            public const float ChoicesTitleHeight = 20f;
            public const float FooterHeight = 34f;
            public const float PreviewLineHeight = 14f;
            public const float DetailLineHeight = 15f;
            public const float SectionGap = 4f;
            public const float EmptySectionHeight = PreviewLineHeight + SectionGap;

            // §5/§10 инструкции "читаемые ноды": визуальный отступ карточки
            // TextBlock/Choice (фон + рамка) сверх содержимого.
            public const float CardPadding = 6f;

            // §6 инструкции "полноценное редактирование нод": горизонтальный
            // отступ содержимого от края ноды — тот же margin, что и
            // GraphPadding в Graph.cs (держится равным явно, а не через
            // общую константу, чтобы не тянуть Graph.cs в этот файл).
            public const float ContentMargin = 10f;

            // Внутренний отступ карточки ответа (rowRect → contentX/Width в
            // Graph.cs: по 5px с каждой стороны) — карточка ответа у́же
            // карточки текстового блока на эту величину.
            public const float ChoiceCardInnerPadding = 10f;

            // Базовый (нескейленный, при graphZoom=1) размер шрифта
            // литературного текста — должен совпадать с тем, что реально
            // использует ScaledStyle(...) при отрисовке в Graph.cs, иначе
            // измеренная здесь высота разойдётся с фактически нарисованной.
            public const float NarrativeTextFontSize = 10f;
        }

        public static float GetGraphNodeWidth(GraphDetailMode mode)
        {
            // §6 инструкции "полноценное редактирование нод": заметно шире,
            // чем раньше — узкие ноды делали русский текст нечитаемым и
            // раздували высоту сильнее необходимого.
            switch (mode)
            {
                case GraphDetailMode.Compact: return 310f;
                case GraphDetailMode.Full: return 500f;
                default: return 430f;
            }
        }

        public static float GetGraphTextContentWidth(GraphDetailMode mode)
        {
            return GetGraphNodeWidth(mode) - 2f * GraphNodeLayoutConstants.ContentMargin;
        }

        public static float GetGraphChoiceContentWidth(GraphDetailMode mode)
        {
            return GetGraphTextContentWidth(mode) - GraphNodeLayoutConstants.ChoiceCardInnerPadding;
        }

        public static float GetGraphSpeakerSectionHeight(GraphDetailMode mode)
        {
            switch (mode)
            {
                case GraphDetailMode.Compact: return 26f;
                // §4 инструкции "читаемые ноды": крупнее портрет (46px) +
                // имя + вторая строка роли, если она задана у говорящего.
                case GraphDetailMode.Full: return 62f;
                default: return 44f;
            }
        }

        public static int GetMaxTextBlocksShown(GraphDetailMode mode)
        {
            switch (mode)
            {
                case GraphDetailMode.Compact: return 1;
                case GraphDetailMode.Full: return 8;
                default: return 3;
            }
        }

        public static int GetTextPreviewMaxChars(GraphDetailMode mode)
        {
            switch (mode)
            {
                case GraphDetailMode.Compact: return 50;
                case GraphDetailMode.Full: return 220;
                default: return 110;
            }
        }

        public static int GetChoiceTextPreviewMaxChars(GraphDetailMode mode)
        {
            switch (mode)
            {
                case GraphDetailMode.Compact: return 40;
                case GraphDetailMode.Full: return 140;
                default: return 80;
            }
        }

        public static int GetMaxDetailLinesPerSection(GraphDetailMode mode)
        {
            switch (mode)
            {
                case GraphDetailMode.Compact: return 0;
                case GraphDetailMode.Full: return 4;
                default: return 2;
            }
        }

        private static float ComputeDetailSectionHeight(int itemCount, int maxLines)
        {
            if (itemCount <= 0)
                return 0f;

            int shown = Math.Min(itemCount, Math.Max(0, maxLines));
            float height = shown * GraphNodeLayoutConstants.DetailLineHeight;
            if (itemCount > shown)
                height += GraphNodeLayoutConstants.DetailLineHeight;
            return height;
        }

        // §8 инструкции "читаемые ноды": Conditions/Effects — не голый
        // список строк, а подписанная мини-секция ("ПОКАЗАТЬ ЕСЛИ"/"ПОСЛЕ
        // ПОКАЗА") — заголовок добавляет ровно одну строку высоты, когда
        // сама секция не пуста.
        private static float ComputeLabeledSectionHeight(int itemCount, int maxLines)
        {
            if (itemCount <= 0)
                return 0f;
            return GraphNodeLayoutConstants.PreviewLineHeight + ComputeDetailSectionHeight(itemCount, maxLines);
        }

        // Kind-заголовок блока + ПОЛНЫЙ литературный текст (реальная высота
        // через CalcHeight, §3-4 инструкции "полноценное редактирование
        // нод") + (Standard/Full) строка проверки/условий/эффектов, плюс
        // отступы карточки блока (§5 инструкции "читаемые ноды").
        public static float ComputeTextBlockHeight(GraphTextBlockInfo block, GraphDetailMode mode)
        {
            float height = GraphNodeLayoutConstants.PreviewLineHeight; // заголовок вида блока
            string displayText = ResolveGraphTextForDisplay(block?.Text, mode, GetTextPreviewMaxChars(mode));
            height += ComputeNarrativeTextHeight(displayText, GetGraphTextContentWidth(mode));

            if (mode != GraphDetailMode.Compact && block != null)
            {
                if (block.PassiveCheck != null)
                    height += GraphNodeLayoutConstants.DetailLineHeight;

                int maxLines = GetMaxDetailLinesPerSection(mode);
                height += ComputeLabeledSectionHeight(block.Conditions?.Conditions?.Count ?? 0, maxLines);
                height += ComputeLabeledSectionHeight(block.OnRevealEffects?.Count ?? 0, maxLines);
            }

            return height + GraphNodeLayoutConstants.SectionGap + GraphNodeLayoutConstants.CardPadding;
        }

        // Тип ответа + ПОЛНЫЙ текст ответа (реальная высота через CalcHeight)
        // + строка(и) цели (обычный переход — 1, активная проверка — 2:
        // ✓ успех / ✕ провал) + (Standard/Full) условия/эффекты успеха-
        // провала отдельными подписанными секциями + (если есть) строка
        // ошибки (§12 инструкции по читаемым нодам).
        public static float ComputeChoiceHeight(GraphChoiceInfo choice, GraphDetailMode mode, bool hasWarning)
        {
            float height = GraphNodeLayoutConstants.PreviewLineHeight; // тип ответа
            string displayText = choice != null
                ? ResolveGraphTextForDisplay(choice.Text, mode, GetChoiceTextPreviewMaxChars(mode))
                : string.Empty;
            height += ComputeNarrativeTextHeight(displayText, GetGraphChoiceContentWidth(mode));

            if (choice == null)
                return height + GraphNodeLayoutConstants.SectionGap + GraphNodeLayoutConstants.CardPadding;

            if (choice.IsExit)
            {
                height += GraphNodeLayoutConstants.DetailLineHeight; // "ВЫХОД"
                if (hasWarning)
                    height += GraphNodeLayoutConstants.DetailLineHeight;
                return height + GraphNodeLayoutConstants.SectionGap + GraphNodeLayoutConstants.CardPadding;
            }

            if (choice.IsActiveCheck)
            {
                if (mode != GraphDetailMode.Compact)
                    height += GraphNodeLayoutConstants.DetailLineHeight; // мех. строка "СИЛА + РЕМЕСЛО · 13"
                height += GraphNodeLayoutConstants.DetailLineHeight * 2f; // ✓ успех / ✕ провал
            }
            else
            {
                height += GraphNodeLayoutConstants.DetailLineHeight; // цель перехода
            }

            if (hasWarning)
                height += GraphNodeLayoutConstants.DetailLineHeight;

            if (mode != GraphDetailMode.Compact)
            {
                int maxLines = GetMaxDetailLinesPerSection(mode);
                height += ComputeLabeledSectionHeight(choice.Conditions?.Conditions?.Count ?? 0, maxLines);
                height += ComputeLabeledSectionHeight(choice.SuccessEffects?.Count ?? 0, maxLines);
                height += ComputeLabeledSectionHeight(choice.FailureEffects?.Count ?? 0, maxLines);
            }

            return height + GraphNodeLayoutConstants.SectionGap + GraphNodeLayoutConstants.CardPadding;
        }

        public sealed class GraphTextBlockLayout
        {
            public float Y;
            public float Height;
        }

        public sealed class GraphChoiceLayout
        {
            public float Y;
            public float Height;
        }

        public sealed class GraphNodeLayoutMetrics
        {
            public GraphDetailMode EffectiveMode;
            public float Width;
            public float HeaderHeight;
            public float SpeakerHeight;
            public float TextSectionY;
            public float TextSectionHeight;
            public int TextBlocksShown;
            public int TextBlocksOverflow;
            public List<GraphTextBlockLayout> TextBlocks = new List<GraphTextBlockLayout>();
            public float ChoicesTitleY;
            public float ChoicesTitleHeight;
            public List<GraphChoiceLayout> Choices = new List<GraphChoiceLayout>();
            public float FooterY;
            public float FooterHeight;
            public float TotalHeight;
            public List<string> Badges = new List<string>();
            public List<string> Warnings = new List<string>();
        }

        // Единственная точка расчёта раскладки узла — и отрисовка, и
        // hit-test/центры портов обязаны читать именно эти значения
        // (§3, §30-34), а не пересчитывать их по отдельной формуле.
        public static GraphNodeLayoutMetrics ComputeNodeLayoutMetrics(
            GraphNodeInfo node,
            HashSet<string> allNodeIds,
            GraphDetailMode mode,
            bool isSelected)
        {
            GraphDetailMode effectiveMode = ResolveEffectiveGraphDetailMode(mode, isSelected);
            GraphNodeLayoutMetrics metrics = new GraphNodeLayoutMetrics { EffectiveMode = effectiveMode };

            List<string> warnings = CollectGraphNodeWarnings(node, allNodeIds);
            List<string> badges = BuildGraphNodeBadges(node, warnings.Count);
            metrics.Warnings = warnings;
            metrics.Badges = badges;
            metrics.Width = GetGraphNodeWidth(effectiveMode);
            metrics.HeaderHeight = GraphNodeLayoutConstants.HeaderBaseHeight +
                                    (badges.Count > 0 ? GraphNodeLayoutConstants.BadgeRowHeight : 0f);
            metrics.SpeakerHeight = GetGraphSpeakerSectionHeight(effectiveMode);

            float y = metrics.HeaderHeight + metrics.SpeakerHeight;
            metrics.TextSectionY = y;

            List<GraphTextBlockInfo> blocks = node?.TextBlocks;
            int blockCount = blocks?.Count ?? 0;
            if (blockCount == 0)
            {
                metrics.TextSectionHeight = GraphNodeLayoutConstants.EmptySectionHeight;
            }
            else
            {
                int maxShown = GetMaxTextBlocksShown(effectiveMode);
                int shown = Math.Min(blockCount, maxShown);
                float sectionHeight = 0f;
                for (int i = 0; i < shown; i++)
                {
                    float blockHeight = ComputeTextBlockHeight(blocks[i], effectiveMode);
                    metrics.TextBlocks.Add(new GraphTextBlockLayout { Y = y + sectionHeight, Height = blockHeight });
                    sectionHeight += blockHeight;
                }

                metrics.TextBlocksShown = shown;
                metrics.TextBlocksOverflow = blockCount - shown;
                if (metrics.TextBlocksOverflow > 0)
                    sectionHeight += GraphNodeLayoutConstants.PreviewLineHeight;

                metrics.TextSectionHeight = sectionHeight;
            }
            y += metrics.TextSectionHeight;

            metrics.ChoicesTitleY = y;
            metrics.ChoicesTitleHeight = GraphNodeLayoutConstants.ChoicesTitleHeight;
            y += metrics.ChoicesTitleHeight;

            List<GraphChoiceInfo> choices = node?.Choices;
            int choiceCount = choices?.Count ?? 0;
            if (choiceCount == 0)
            {
                y += GraphNodeLayoutConstants.EmptySectionHeight;
            }
            else
            {
                for (int i = 0; i < choiceCount; i++)
                {
                    bool hasWarning = ChoiceHasWarning(warnings, i);
                    float choiceHeight = ComputeChoiceHeight(choices[i], effectiveMode, hasWarning);
                    metrics.Choices.Add(new GraphChoiceLayout { Y = y, Height = choiceHeight });
                    y += choiceHeight;
                }
            }

            metrics.FooterY = y;
            metrics.FooterHeight = GraphNodeLayoutConstants.FooterHeight;
            y += metrics.FooterHeight;

            metrics.TotalHeight = y;
            return metrics;
        }

        // ------------------------------------------------------------------
        // Мост SerializedProperty → снимок (Editor-only, требует
        // UnityEditor/боксинг через SerializedProperty.boxedValue — сам
        // снимок и вся логика выше от этого не зависят и тестируются
        // без него).
        // ------------------------------------------------------------------

        private GraphNodeInfo BuildGraphNodeInfoFromProperty(
            SerializedProperty node,
            bool isStart,
            bool isReachable)
        {
            GraphNodeInfo info = new GraphNodeInfo
            {
                NodeId = node.FindPropertyRelative("id").stringValue,
                SpeakerId = node.FindPropertyRelative("speakerId").stringValue,
                IsStart = isStart,
                IsReachable = isReachable
            };
            info.SpeakerKnown = !string.IsNullOrWhiteSpace(info.SpeakerId) && database.FindSpeaker(info.SpeakerId) != null;

            SerializedProperty text = node.FindPropertyRelative("text");
            SerializedProperty textBlocks = node.FindPropertyRelative("textBlocks");
            if (textBlocks != null && textBlocks.arraySize > 0)
            {
                for (int i = 0; i < textBlocks.arraySize; i++)
                    info.TextBlocks.Add(BuildGraphTextBlockInfoFromProperty(textBlocks.GetArrayElementAtIndex(i)));
            }
            else if (text != null && !string.IsNullOrEmpty(text.stringValue))
            {
                info.TextBlocks.Add(new GraphTextBlockInfo
                {
                    Kind = DialogueTextBlockKind.MainLine,
                    Text = text.stringValue
                });
            }

            SerializedProperty choices = node.FindPropertyRelative("choices");
            if (choices != null)
            {
                for (int i = 0; i < choices.arraySize; i++)
                    info.Choices.Add(BuildGraphChoiceInfoFromProperty(choices.GetArrayElementAtIndex(i)));
            }

            return info;
        }

        private static GraphTextBlockInfo BuildGraphTextBlockInfoFromProperty(SerializedProperty block)
        {
            GraphTextBlockInfo info = new GraphTextBlockInfo
            {
                Kind = (DialogueTextBlockKind)block.FindPropertyRelative("kind").enumValueIndex,
                Text = block.FindPropertyRelative("text").stringValue
            };

            SerializedProperty conditions = block.FindPropertyRelative("conditions");
            if (conditions != null)
                info.Conditions = (NarrativeConditionGroup)conditions.boxedValue;

            SerializedProperty hasPassiveCheck = block.FindPropertyRelative("hasPassiveCheck");
            if (hasPassiveCheck != null && hasPassiveCheck.boolValue)
            {
                SerializedProperty passiveCheck = block.FindPropertyRelative("passiveCheck");
                if (passiveCheck != null)
                    info.PassiveCheck = (NarrativeCheckSpec)passiveCheck.boxedValue;
            }

            info.OnRevealEffects = ReadEffectsList(block.FindPropertyRelative("onRevealEffects"));

            return info;
        }

        // SerializedProperty.boxedValue не читается напрямую с массива
        // ("... is an array so it cannot be read with boxedValue") — только
        // поэлементно. List<NarrativeEffect> сериализуется как array, поэтому
        // конкретно эти три поля (onRevealEffects/successEffects/
        // failureEffects) нельзя боксить целиком, в отличие от одиночных
        // объектов conditions/passiveCheck/check.
        private static List<NarrativeEffect> ReadEffectsList(SerializedProperty effectsArray)
        {
            List<NarrativeEffect> result = new List<NarrativeEffect>();
            if (effectsArray == null)
                return result;

            for (int i = 0; i < effectsArray.arraySize; i++)
            {
                SerializedProperty element = effectsArray.GetArrayElementAtIndex(i);
                if (element != null)
                    result.Add((NarrativeEffect)element.boxedValue);
            }

            return result;
        }

        private static GraphChoiceInfo BuildGraphChoiceInfoFromProperty(SerializedProperty choice)
        {
            GraphChoiceInfo info = new GraphChoiceInfo
            {
                ChoiceId = choice.FindPropertyRelative("choiceId").stringValue,
                Text = choice.FindPropertyRelative("text").stringValue,
                Kind = (DialogueChoiceKind)choice.FindPropertyRelative("kind").enumValueIndex,
                NextNodeId = choice.FindPropertyRelative("nextNodeId").stringValue,
                EndsDialogue = choice.FindPropertyRelative("endsDialogue").boolValue,
                SuccessNodeId = choice.FindPropertyRelative("successNodeId").stringValue,
                FailureNodeId = choice.FindPropertyRelative("failureNodeId").stringValue
            };

            SerializedProperty conditions = choice.FindPropertyRelative("conditions");
            if (conditions != null)
                info.Conditions = (NarrativeConditionGroup)conditions.boxedValue;

            SerializedProperty check = choice.FindPropertyRelative("check");
            if (check != null)
                info.Check = (NarrativeCheckSpec)check.boxedValue;

            info.SuccessEffects = ReadEffectsList(choice.FindPropertyRelative("successEffects"));
            info.FailureEffects = ReadEffectsList(choice.FindPropertyRelative("failureEffects"));

            return info;
        }
    }
}

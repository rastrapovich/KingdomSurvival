using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace KingdomSurvival.AnimationDatabase.Editor
{
    public enum CreatureAnimationImportSeverity
    {
        Error,
        Warning
    }

    public sealed class CreatureAnimationImportIssue
    {
        public CreatureAnimationImportSeverity Severity { get; }
        public string Message { get; }

        public CreatureAnimationImportIssue(CreatureAnimationImportSeverity severity, string message)
        {
            Severity = severity;
            Message = message ?? string.Empty;
        }

        public override string ToString()
        {
            return (Severity == CreatureAnimationImportSeverity.Error ? "Ошибка: " : "Предупреждение: ") + Message;
        }
    }

    public enum CreatureAnimationActionMatch
    {
        // Точное имя действия (Idle, «Ожидание»).
        Exact,
        // Похожее имя (Run, «Бег»): нужно явное согласие пользователя.
        Suggested,
        // Неизвестное имя: по умолчанию не загружается.
        Unknown
    }

    public sealed class CreatureAnimationSourceFrame
    {
        public string Path { get; }
        public int Number { get; }

        public CreatureAnimationSourceFrame(string path, int number)
        {
            Path = path ?? string.Empty;
            Number = number;
        }
    }

    // Ячейка пакета: один ракурс одного действия.
    public sealed class CreatureAnimationImportCell
    {
        public CreatureAnimationDirection Direction { get; }
        public List<CreatureAnimationSourceFrame> Frames { get; } = new List<CreatureAnimationSourceFrame>();

        public CreatureAnimationImportCell(CreatureAnimationDirection direction)
        {
            Direction = direction;
        }

        public IEnumerable<int> Numbers => Frames.Select(frame => frame.Number);
    }

    // Группа файлов с одним исходным именем действия (папка «Idle», «Run»…).
    public sealed class CreatureAnimationImportGroup
    {
        public string RawName { get; }
        public CreatureAnimationActionMatch Match { get; }
        public CreatureAnimationAction? SuggestedAction { get; }
        public string ClipKey { get; }
        // Выбор пользователя. Null — не загружать.
        public CreatureAnimationAction? ChosenAction { get; set; }
        public SortedDictionary<CreatureAnimationDirection, CreatureAnimationImportCell> Cells { get; } =
            new SortedDictionary<CreatureAnimationDirection, CreatureAnimationImportCell>();

        public CreatureAnimationImportGroup(
            string rawName,
            CreatureAnimationActionMatch match,
            CreatureAnimationAction? suggestedAction,
            string clipKey)
        {
            RawName = rawName ?? string.Empty;
            Match = match;
            SuggestedAction = suggestedAction;
            ClipKey = clipKey ?? string.Empty;
            ChosenAction = suggestedAction;
        }

        public int FileCount => Cells.Values.Sum(cell => cell.Frames.Count);

        public CreatureAnimationImportCell GetOrAddCell(CreatureAnimationDirection direction)
        {
            if (!Cells.TryGetValue(direction, out CreatureAnimationImportCell cell))
            {
                cell = new CreatureAnimationImportCell(direction);
                Cells.Add(direction, cell);
            }
            return cell;
        }
    }

    // Разобранный пакет до применения: ничего ещё не изменено.
    public sealed class CreatureAnimationImportPackage
    {
        public string RootPath { get; }
        public List<CreatureAnimationImportGroup> Groups { get; } = new List<CreatureAnimationImportGroup>();
        public List<CreatureAnimationImportIssue> Issues { get; } = new List<CreatureAnimationImportIssue>();

        public CreatureAnimationImportPackage(string rootPath)
        {
            RootPath = rootPath ?? string.Empty;
        }

        public bool HasErrors => Issues.Any(issue => issue.Severity == CreatureAnimationImportSeverity.Error);
        public int FileCount => Groups.Sum(group => group.FileCount);

        public IEnumerable<CreatureAnimationImportGroup> ChosenGroups =>
            Groups.Where(group => group.ChosenAction.HasValue && group.Cells.Count > 0);

        public void AddError(string message)
        {
            Issues.Add(new CreatureAnimationImportIssue(CreatureAnimationImportSeverity.Error, message));
        }

        public void AddWarning(string message)
        {
            Issues.Add(new CreatureAnimationImportIssue(CreatureAnimationImportSeverity.Warning, message));
        }
    }

    // Распознавание папок и имён экспорта KS Sprite Renderer:
    // Боец/Idle/Front/Idle_Front_0001.png. Регистр не важен.
    public static class CreatureAnimationImportParser
    {
        private static readonly Dictionary<string, CreatureAnimationDirection> DirectionNames =
            new Dictionary<string, CreatureAnimationDirection>(StringComparer.Ordinal)
            {
                { "front", CreatureAnimationDirection.Front },
                { "frontright", CreatureAnimationDirection.FrontRight },
                { "backright", CreatureAnimationDirection.BackRight },
                { "back", CreatureAnimationDirection.Back },
                { "backleft", CreatureAnimationDirection.BackLeft },
                { "frontleft", CreatureAnimationDirection.FrontLeft },
                { "спереди", CreatureAnimationDirection.Front },
                { "спередисправа", CreatureAnimationDirection.FrontRight },
                { "сзадисправа", CreatureAnimationDirection.BackRight },
                { "сзади", CreatureAnimationDirection.Back },
                { "сзадислева", CreatureAnimationDirection.BackLeft },
                { "спередислева", CreatureAnimationDirection.FrontLeft }
            };

        private static readonly Dictionary<string, CreatureAnimationAction> ExactActionNames =
            new Dictionary<string, CreatureAnimationAction>(StringComparer.Ordinal)
            {
                { "idle", CreatureAnimationAction.Idle },
                { "walk", CreatureAnimationAction.Walk },
                { "attack", CreatureAnimationAction.Attack },
                { "hit", CreatureAnimationAction.Hit },
                { "death", CreatureAnimationAction.Death },
                { "block", CreatureAnimationAction.Block },
                { "shoot", CreatureAnimationAction.Shoot },
                { "specialattack", CreatureAnimationAction.SpecialAttack },
                { "victory", CreatureAnimationAction.Victory },
                { "taunt", CreatureAnimationAction.Taunt },
                { "ожидание", CreatureAnimationAction.Idle },
                { "ходьба", CreatureAnimationAction.Walk },
                { "атака", CreatureAnimationAction.Attack },
                { "получениеудара", CreatureAnimationAction.Hit },
                { "смерть", CreatureAnimationAction.Death },
                { "защита", CreatureAnimationAction.Block },
                { "выстрел", CreatureAnimationAction.Shoot },
                { "особаяатака", CreatureAnimationAction.SpecialAttack },
                { "победа", CreatureAnimationAction.Victory },
                { "провокация", CreatureAnimationAction.Taunt }
            };

        // Похожие имена — только предложение, не молчаливое сопоставление.
        // «Hit» и «Attack» никогда не подменяют друг друга.
        private static readonly Dictionary<string, CreatureAnimationAction> SuggestedActionNames =
            new Dictionary<string, CreatureAnimationAction>(StringComparer.Ordinal)
            {
                { "run", CreatureAnimationAction.Walk },
                { "running", CreatureAnimationAction.Walk },
                { "walking", CreatureAnimationAction.Walk },
                { "move", CreatureAnimationAction.Walk },
                { "бег", CreatureAnimationAction.Walk },
                { "stand", CreatureAnimationAction.Idle },
                { "standing", CreatureAnimationAction.Idle },
                { "breath", CreatureAnimationAction.Idle },
                { "breathe", CreatureAnimationAction.Idle },
                { "покой", CreatureAnimationAction.Idle },
                { "hurt", CreatureAnimationAction.Hit },
                { "damage", CreatureAnimationAction.Hit },
                { "damaged", CreatureAnimationAction.Hit },
                { "gethit", CreatureAnimationAction.Hit },
                { "hitreact", CreatureAnimationAction.Hit },
                { "ранение", CreatureAnimationAction.Hit },
                { "удар", CreatureAnimationAction.Hit },
                { "die", CreatureAnimationAction.Death },
                { "dead", CreatureAnimationAction.Death },
                { "dying", CreatureAnimationAction.Death },
                { "гибель", CreatureAnimationAction.Death },
                { "melee", CreatureAnimationAction.Attack },
                { "slash", CreatureAnimationAction.Attack },
                { "strike", CreatureAnimationAction.Attack },
                { "bite", CreatureAnimationAction.Attack },
                { "укус", CreatureAnimationAction.Attack },
                { "shot", CreatureAnimationAction.Shoot },
                { "fire", CreatureAnimationAction.Shoot },
                { "ranged", CreatureAnimationAction.Shoot },
                { "стрельба", CreatureAnimationAction.Shoot },
                { "guard", CreatureAnimationAction.Block },
                { "defend", CreatureAnimationAction.Block },
                { "defense", CreatureAnimationAction.Block },
                { "блок", CreatureAnimationAction.Block },
                { "win", CreatureAnimationAction.Victory },
                { "celebrate", CreatureAnimationAction.Victory },
                { "provoke", CreatureAnimationAction.Taunt }
            };

        public static readonly string[] ImageExtensions = { ".png" };

        public static bool IsImageFile(string path)
        {
            string extension = Path.GetExtension(path ?? string.Empty);
            return ImageExtensions.Any(known => string.Equals(known, extension, StringComparison.OrdinalIgnoreCase));
        }

        // Нижний регистр без разделителей: «Front_Right» → «frontright».
        public static string Normalize(string name)
        {
            if (string.IsNullOrEmpty(name))
                return string.Empty;
            StringBuilder builder = new StringBuilder(name.Length);
            foreach (char symbol in name)
            {
                if (symbol == '_' || symbol == '-' || symbol == ' ' || symbol == '.')
                    continue;
                builder.Append(char.ToLowerInvariant(symbol));
            }
            return builder.ToString();
        }

        public static bool TryMatchDirection(string name, out CreatureAnimationDirection direction)
        {
            return DirectionNames.TryGetValue(Normalize(name), out direction);
        }

        // Особая атака несёт ключ клипа: «SpecialAttack_Leap» → ключ «Leap».
        public static CreatureAnimationActionMatch MatchAction(
            string name,
            out CreatureAnimationAction action,
            out string clipKey)
        {
            clipKey = string.Empty;
            string normalized = Normalize(name);
            if (ExactActionNames.TryGetValue(normalized, out action))
                return CreatureAnimationActionMatch.Exact;

            foreach (string prefix in new[] { "specialattack", "особаяатака" })
            {
                if (normalized.StartsWith(prefix, StringComparison.Ordinal) && normalized.Length > prefix.Length)
                {
                    action = CreatureAnimationAction.SpecialAttack;
                    clipKey = ExtractKeySuffix(name, prefix.Length);
                    return CreatureAnimationActionMatch.Exact;
                }
            }

            if (SuggestedActionNames.TryGetValue(normalized, out action))
                return CreatureAnimationActionMatch.Suggested;

            // «Attack2», «Idle_01»: точное имя с номером — только предложение.
            string withoutDigits = normalized.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9');
            if (withoutDigits.Length > 0 && withoutDigits.Length < normalized.Length &&
                (ExactActionNames.TryGetValue(withoutDigits, out action) ||
                 SuggestedActionNames.TryGetValue(withoutDigits, out action)))
            {
                return CreatureAnimationActionMatch.Suggested;
            }

            action = CreatureAnimationAction.Idle;
            return CreatureAnimationActionMatch.Unknown;
        }

        // Хвост исходного имени после префикса нормализованной длины.
        private static string ExtractKeySuffix(string raw, int normalizedPrefixLength)
        {
            int seen = 0;
            int index = 0;
            while (index < raw.Length && seen < normalizedPrefixLength)
            {
                char symbol = raw[index];
                if (symbol != '_' && symbol != '-' && symbol != ' ' && symbol != '.')
                    seen++;
                index++;
            }
            return raw.Substring(index).Trim('_', '-', ' ', '.');
        }

        // Номер кадра — число в конце имени. Минус — знак, только если перед
        // ним разделитель или начало имени: «Idle_Front_-001» → −1,
        // «Idle_Front-0001» → 1 (дефис здесь разделитель).
        public static bool TryParseFrameNumber(string fileNameWithoutExtension, out int number)
        {
            number = 0;
            string name = fileNameWithoutExtension ?? string.Empty;
            int end = name.Length;
            int start = end;
            while (start > 0 && char.IsDigit(name[start - 1]))
                start--;
            if (start == end)
                return false;

            string digits = name.Substring(start, end - start);
            if (digits.Length > 9)
                return false;
            number = int.Parse(digits, System.Globalization.CultureInfo.InvariantCulture);

            if (start > 0 && name[start - 1] == '-')
            {
                bool signPosition = start - 1 == 0 || IsSeparator(name[start - 2]);
                if (signPosition)
                    number = -number;
            }
            return true;
        }

        private static bool IsSeparator(char symbol)
        {
            return symbol == '_' || symbol == ' ' || symbol == '.' || symbol == '-';
        }

        // Имя файла без номера и разделителей в конце: «Idle_Front_Right».
        public static string StripFrameNumber(string fileNameWithoutExtension)
        {
            string name = fileNameWithoutExtension ?? string.Empty;
            int end = name.Length;
            while (end > 0 && char.IsDigit(name[end - 1]))
                end--;
            if (end > 0 && name[end - 1] == '-')
                end--;
            return name.Substring(0, end).TrimEnd('_', '-', ' ', '.');
        }

        private static string[] Tokens(string name)
        {
            return (name ?? string.Empty).Split(new[] { '_', '-', ' ', '.' }, StringSplitOptions.RemoveEmptyEntries);
        }

        // Ракурс в конце имени файла: последние два слова, затем одно.
        private static bool TrySplitFileName(
            string strippedName,
            out string actionPart,
            out CreatureAnimationDirection direction)
        {
            actionPart = string.Empty;
            direction = CreatureAnimationDirection.Front;
            string[] tokens = Tokens(strippedName);
            for (int take = Math.Min(2, tokens.Length); take >= 1; take--)
            {
                string candidate = string.Join("_", tokens.Skip(tokens.Length - take));
                if (TryMatchDirection(candidate, out direction))
                {
                    actionPart = string.Join("_", tokens.Take(tokens.Length - take));
                    return true;
                }
            }
            return false;
        }

        // Разбор папки существа, действия или ракурса. Файлы — абсолютные пути.
        public static CreatureAnimationImportPackage Analyze(string rootPath, IEnumerable<string> files)
        {
            CreatureAnimationImportPackage package = new CreatureAnimationImportPackage(rootPath);
            string root = NormalizePath(rootPath);
            string rootName = Path.GetFileName(root.TrimEnd('/'));
            Dictionary<string, CreatureAnimationImportGroup> groups =
                new Dictionary<string, CreatureAnimationImportGroup>(StringComparer.Ordinal);

            List<string> imageFiles = (files ?? Enumerable.Empty<string>())
                .Select(NormalizePath)
                .Where(IsImageFile)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (imageFiles.Count == 0)
            {
                package.AddError("В выбранной папке нет PNG-файлов.");
                return package;
            }

            foreach (string file in imageFiles)
            {
                string relative = file.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase)
                    ? file.Substring(root.Length + 1)
                    : Path.GetFileName(file);
                List<string> segments = new List<string> { rootName };
                string[] parts = relative.Split('/');
                for (int i = 0; i < parts.Length - 1; i++)
                    segments.Add(parts[i]);

                string fileName = Path.GetFileNameWithoutExtension(file);
                string stripped = StripFrameNumber(fileName);
                bool fileHasDirection = TrySplitFileName(stripped, out string fileActionPart, out CreatureAnimationDirection fileDirection);

                string actionName = null;
                CreatureAnimationDirection direction;
                int directionSegment = -1;
                for (int i = segments.Count - 1; i >= 0; i--)
                {
                    if (TryMatchDirection(segments[i], out _))
                    {
                        directionSegment = i;
                        break;
                    }
                }

                if (directionSegment >= 0)
                {
                    TryMatchDirection(segments[directionSegment], out direction);
                    if (directionSegment > 0)
                        actionName = segments[directionSegment - 1];
                    else if (fileHasDirection && fileActionPart.Length > 0)
                        actionName = fileActionPart;
                }
                else if (fileHasDirection)
                {
                    direction = fileDirection;
                    actionName = fileActionPart.Length > 0 ? fileActionPart : segments[segments.Count - 1];
                }
                else
                {
                    package.AddWarning(relative + ": не удалось определить ракурс — файл будет пропущен.");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(actionName))
                {
                    package.AddWarning(relative + ": не удалось определить действие — файл будет пропущен.");
                    continue;
                }

                // Имя файла и папка не должны спорить: Hit в папке Attack — ошибка.
                if (fileHasDirection && fileActionPart.Length > 0 &&
                    MatchAction(fileActionPart, out CreatureAnimationAction fileAction, out _) == CreatureAnimationActionMatch.Exact &&
                    MatchAction(actionName, out CreatureAnimationAction folderAction, out _) == CreatureAnimationActionMatch.Exact &&
                    fileAction != folderAction)
                {
                    package.AddError(relative + ": имя файла говорит «" + CreatureAnimationLabels.ActionTitle(fileAction) +
                                     "», а папка — «" + CreatureAnimationLabels.ActionTitle(folderAction) + "».");
                    continue;
                }
                if (fileHasDirection && fileDirection != direction)
                {
                    package.AddError(relative + ": имя файла говорит ракурс «" + CreatureAnimationLabels.DirectionTitle(fileDirection) +
                                     "», а папка — «" + CreatureAnimationLabels.DirectionTitle(direction) + "».");
                    continue;
                }

                string groupKey = Normalize(actionName);
                if (!groups.TryGetValue(groupKey, out CreatureAnimationImportGroup group))
                {
                    CreatureAnimationActionMatch match = MatchAction(actionName, out CreatureAnimationAction matched, out string clipKey);
                    group = new CreatureAnimationImportGroup(
                        actionName,
                        match,
                        match == CreatureAnimationActionMatch.Unknown ? (CreatureAnimationAction?)null : matched,
                        clipKey);
                    groups.Add(groupKey, group);
                    package.Groups.Add(group);
                }

                int number = int.MinValue;
                if (!TryParseFrameNumber(fileName, out number))
                    number = int.MinValue;
                group.GetOrAddCell(direction).Frames.Add(new CreatureAnimationSourceFrame(file, number));
            }

            foreach (CreatureAnimationImportGroup group in package.Groups)
            {
                foreach (CreatureAnimationImportCell cell in group.Cells.Values)
                    FinishCell(package, group.RawName, cell);
                if (group.Match == CreatureAnimationActionMatch.Suggested)
                {
                    package.AddWarning("Папка «" + group.RawName + "» похожа на действие «" +
                                       CreatureAnimationLabels.ActionTitle(group.SuggestedAction.Value) +
                                       "». Проверьте сопоставление перед применением.");
                }
                else if (group.Match == CreatureAnimationActionMatch.Unknown)
                {
                    package.AddWarning("Папка «" + group.RawName + "»: неизвестное действие. Выберите действие или оставьте «Не загружать».");
                }
            }

            ValidateDuplicateTargets(package);
            return package;
        }

        // Одна последовательность в выбранную ячейку «действие × ракурс».
        public static CreatureAnimationImportPackage AnalyzeSequence(
            IEnumerable<string> files,
            CreatureAnimationAction action,
            CreatureAnimationDirection direction,
            string clipKey = null)
        {
            List<string> imageFiles = (files ?? Enumerable.Empty<string>())
                .Select(NormalizePath)
                .Where(IsImageFile)
                .ToList();
            string root = imageFiles.Count > 0 ? Path.GetDirectoryName(imageFiles[0]) : string.Empty;
            CreatureAnimationImportPackage package = new CreatureAnimationImportPackage(root);
            if (imageFiles.Count == 0)
            {
                package.AddError("Нет PNG-файлов для загрузки.");
                return package;
            }

            CreatureAnimationImportGroup group = new CreatureAnimationImportGroup(
                action.ToString(),
                CreatureAnimationActionMatch.Exact,
                action,
                clipKey);
            package.Groups.Add(group);
            CreatureAnimationImportCell cell = group.GetOrAddCell(direction);
            foreach (string file in imageFiles)
            {
                string fileName = Path.GetFileNameWithoutExtension(file);
                cell.Frames.Add(new CreatureAnimationSourceFrame(file, TryParseFrameNumber(fileName, out int number) ? number : int.MinValue));
            }
            FinishCell(package, CreatureAnimationLabels.ActionTitle(action), cell);
            return package;
        }

        // Числовая сортировка (1, 2, 10), пропуски Step допустимы, повтор номера — ошибка.
        private static void FinishCell(CreatureAnimationImportPackage package, string actionName, CreatureAnimationImportCell cell)
        {
            string where = actionName + " → " + CreatureAnimationLabels.DirectionTitle(cell.Direction);
            List<CreatureAnimationSourceFrame> unnumbered = cell.Frames.Where(frame => frame.Number == int.MinValue).ToList();
            if (unnumbered.Count > 0)
            {
                if (cell.Frames.Count == 1)
                {
                    CreatureAnimationSourceFrame single = cell.Frames[0];
                    cell.Frames[0] = new CreatureAnimationSourceFrame(single.Path, 0);
                }
                else
                {
                    package.AddError(where + ": нет номера кадра в конце имени — " +
                                     string.Join(", ", unnumbered.Take(3).Select(frame => Path.GetFileName(frame.Path))) +
                                     (unnumbered.Count > 3 ? " и ещё " + (unnumbered.Count - 3) : string.Empty) + ".");
                    return;
                }
            }

            cell.Frames.Sort((a, b) => a.Number.CompareTo(b.Number));
            for (int i = 1; i < cell.Frames.Count; i++)
            {
                if (cell.Frames[i].Number == cell.Frames[i - 1].Number)
                {
                    package.AddError(where + ": неоднозначный номер кадра " + cell.Frames[i].Number + " — " +
                                     Path.GetFileName(cell.Frames[i - 1].Path) + " и " + Path.GetFileName(cell.Frames[i].Path) + ".");
                }
            }
        }

        // Две папки не могут лечь в одну ячейку («Walk» и «Run» → Ходьба).
        public static void ValidateDuplicateTargets(CreatureAnimationImportPackage package)
        {
            package.Issues.RemoveAll(issue => issue.Message.StartsWith("Две папки", StringComparison.Ordinal));
            Dictionary<string, string> targets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (CreatureAnimationImportGroup group in package.ChosenGroups)
            {
                foreach (CreatureAnimationImportCell cell in group.Cells.Values)
                {
                    string key = group.ChosenAction.Value + "|" + group.ClipKey + "|" + cell.Direction;
                    if (targets.TryGetValue(key, out string other))
                    {
                        package.AddError("Две папки ведут в одну ячейку «" + CreatureAnimationLabels.ActionTitle(group.ChosenAction.Value) +
                                         " → " + CreatureAnimationLabels.DirectionTitle(cell.Direction) + "»: «" + other +
                                         "» и «" + group.RawName + "».");
                    }
                    else
                    {
                        targets.Add(key, group.RawName);
                    }
                }
            }
        }

        public static string NormalizePath(string path)
        {
            return (path ?? string.Empty).Replace('\\', '/').TrimEnd('/');
        }
    }
}

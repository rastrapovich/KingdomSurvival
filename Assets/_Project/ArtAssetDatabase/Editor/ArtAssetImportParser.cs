using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace KingdomSurvival.ArtAssets.Editor
{
    public enum ArtAssetFileKind { Color, Normal }

    // Один ракурс одной части: рисунок и его нормаль (пути файлов).
    public sealed class ArtAssetImportSlot
    {
        public ArtAssetView View;
        // Пусто — основа.
        public string Part = string.Empty;
        public string ColorPath;
        public string NormalPath;
    }

    // Один распознанный объект пакета.
    public sealed class ArtAssetImportGroup
    {
        public string Name;
        // Нормализованное имя источника — подсказка дубликатов.
        public string Key;
        public readonly List<ArtAssetImportSlot> Slots = new List<ArtAssetImportSlot>();

        public ArtAssetImportSlot Slot(ArtAssetView view, string part)
        {
            ArtAssetImportSlot slot = Slots.Find(item => item.View == view && string.Equals(item.Part, part, StringComparison.OrdinalIgnoreCase));
            if (slot == null) { slot = new ArtAssetImportSlot { View = view, Part = part }; Slots.Add(slot); }
            return slot;
        }

        public IEnumerable<string> Parts => Slots.Select(slot => slot.Part).Distinct(StringComparer.OrdinalIgnoreCase);
        public int ColorCount => Slots.Count(slot => slot.ColorPath != null);
        public int NormalCount => Slots.Count(slot => slot.NormalPath != null);
        public int MainViewCount => Slots.Count(slot => slot.Part.Length == 0 && slot.ColorPath != null);
    }

    // Файл, который не удалось однозначно назначить: остаётся для ручного назначения.
    public sealed class ArtAssetImportIssue
    {
        public string Path;
        public string Reason;
    }

    public sealed class ArtAssetImportPlan
    {
        public readonly List<ArtAssetImportGroup> Groups = new List<ArtAssetImportGroup>();
        public readonly List<ArtAssetImportIssue> Unresolved = new List<ArtAssetImportIssue>();
        public bool IsEmpty => Groups.Count == 0;
    }

    // ПР-12Н: разбор пакета файлов и папок. Ракурс — по имени папки или
    // файла (Front, Front_Right, … или русские имена по таблице), нормаль —
    // по слову normal / нормаль. Направление по картинке не угадывается;
    // неоднозначное и нераспознанное — в список для ручного назначения.
    // Нормаль без своего рисунка не становится отдельным предметом.
    public static class ArtAssetImportParser
    {
        public static readonly string[] ImageExtensions = { ".png" };

        // Явная таблица имён ракурсов: английские папки экспорта и русские подписи.
        private static readonly (string alias, ArtAssetView view)[] ViewAliases =
        {
            ("front_right", ArtAssetView.FrontRight), ("frontright", ArtAssetView.FrontRight), ("спереди_справа", ArtAssetView.FrontRight),
            ("back_right", ArtAssetView.BackRight), ("backright", ArtAssetView.BackRight), ("сзади_справа", ArtAssetView.BackRight),
            ("back_left", ArtAssetView.BackLeft), ("backleft", ArtAssetView.BackLeft), ("сзади_слева", ArtAssetView.BackLeft),
            ("front_left", ArtAssetView.FrontLeft), ("frontleft", ArtAssetView.FrontLeft), ("спереди_слева", ArtAssetView.FrontLeft),
            ("front", ArtAssetView.Front), ("спереди", ArtAssetView.Front),
            ("back", ArtAssetView.Back), ("сзади", ArtAssetView.Back)
        };

        private static readonly HashSet<string> NormalTokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "normal", "normals", "normalmap", "nrm", "nm", "нормаль", "нормали", "нормалей" };

        // Слова рисунка, не входящие в имя объекта и части.
        private static readonly HashSet<string> ColorTokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "color", "colour", "albedo", "diffuse", "basecolor", "rgb", "цвет", "рисунок" };

        // Имена основы среди частей.
        private static readonly HashSet<string> MainPartNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "", "основа", "base", "main", "body" };

        public static bool IsImage(string path) =>
            ImageExtensions.Contains(Path.GetExtension(path ?? string.Empty).ToLowerInvariant());

        // Пакет из Проводника или Project: файлы и папки (папки — рекурсивно).
        // forcedAssetName — всё идёт в одну запись (перетаскивание на карточку).
        public static ArtAssetImportPlan ParsePaths(IEnumerable<string> paths, string forcedAssetName = null)
        {
            List<(string full, List<string> segments)> files = new List<(string, List<string>)>();
            List<ArtAssetImportIssue> skipped = new List<ArtAssetImportIssue>();
            foreach (string raw in paths ?? Enumerable.Empty<string>())
            {
                if (string.IsNullOrEmpty(raw)) continue;
                string path = raw.Replace('\\', '/').TrimEnd('/');
                if (Directory.Exists(path))
                {
                    string rootName = Path.GetFileName(path);
                    foreach (string file in Directory.GetFiles(path, "*", SearchOption.AllDirectories).OrderBy(item => item, StringComparer.Ordinal))
                    {
                        string normalized = file.Replace('\\', '/');
                        if (normalized.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) continue;
                        if (!IsImage(normalized)) { skipped.Add(new ArtAssetImportIssue { Path = normalized, Reason = "не PNG — пропущен" }); continue; }
                        List<string> segments = new List<string> { rootName };
                        segments.AddRange(normalized.Substring(path.Length).Trim('/').Split('/'));
                        files.Add((normalized, segments));
                    }
                }
                else if (IsImage(path))
                {
                    // Отдельный файл: родительская папка — запасное имя объекта.
                    string parent = Path.GetFileName(Path.GetDirectoryName(path) ?? string.Empty);
                    List<string> segments = new List<string>();
                    if (!string.IsNullOrEmpty(parent)) segments.Add(parent);
                    segments.Add(Path.GetFileName(path));
                    files.Add((path, segments));
                }
                else skipped.Add(new ArtAssetImportIssue { Path = path, Reason = "не PNG — пропущен" });
            }
            ArtAssetImportPlan plan = ParseFiles(files, forcedAssetName);
            plan.Unresolved.AddRange(skipped);
            return plan;
        }

        // segments — путь от корня перетаскивания: [папка-корень, …, файл].
        // Последняя папка одиночного файла тоже передаётся: она — запасное имя.
        public static ArtAssetImportPlan ParseFiles(IEnumerable<(string full, List<string> segments)> files, string forcedAssetName = null)
        {
            ArtAssetImportPlan plan = new ArtAssetImportPlan();
            Dictionary<string, ArtAssetImportGroup> groups = new Dictionary<string, ArtAssetImportGroup>(StringComparer.OrdinalIgnoreCase);
            // (группа, ракурс, часть, вид) → файлы: дубликаты выявляются до назначения.
            Dictionary<string, List<string>> claims = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, (ArtAssetImportGroup group, ArtAssetView view, string part, ArtAssetFileKind kind)> targets =
                new Dictionary<string, (ArtAssetImportGroup, ArtAssetView, string, ArtAssetFileKind)>(StringComparer.OrdinalIgnoreCase);

            foreach ((string full, List<string> segments) in files)
            {
                if (!Classify(segments, forcedAssetName, out string objectName, out ArtAssetView view, out string part, out ArtAssetFileKind kind, out string problem))
                {
                    plan.Unresolved.Add(new ArtAssetImportIssue { Path = full, Reason = problem });
                    continue;
                }
                string key = NormalizeKey(objectName);
                if (!groups.TryGetValue(key, out ArtAssetImportGroup group))
                {
                    group = new ArtAssetImportGroup { Name = objectName, Key = key };
                    groups.Add(key, group);
                }
                string claim = key + "|" + view + "|" + part.ToLowerInvariant() + "|" + kind;
                if (!claims.TryGetValue(claim, out List<string> list)) { list = new List<string>(); claims.Add(claim, list); targets[claim] = (group, view, part, kind); }
                list.Add(full);
            }

            foreach (KeyValuePair<string, List<string>> claim in claims)
            {
                (ArtAssetImportGroup group, ArtAssetView view, string part, ArtAssetFileKind kind) = targets[claim.Key];
                if (claim.Value.Count > 1)
                {
                    foreach (string path in claim.Value)
                        plan.Unresolved.Add(new ArtAssetImportIssue { Path = path, Reason = "неоднозначно: несколько файлов для «" + group.Name + "» · " +
                            ArtAssetLabels.ViewTitle(view) + (part.Length > 0 ? " · " + part : "") + (kind == ArtAssetFileKind.Normal ? " (нормаль)" : "") });
                    continue;
                }
                ArtAssetImportSlot slot = group.Slot(view, part);
                if (kind == ArtAssetFileKind.Color) slot.ColorPath = claim.Value[0];
                else slot.NormalPath = claim.Value[0];
            }

            foreach (ArtAssetImportGroup group in groups.Values)
            {
                // Нормаль без своего рисунка — не предмет и не пара: на ручное назначение.
                foreach (ArtAssetImportSlot slot in group.Slots.ToList())
                {
                    if (slot.ColorPath != null) continue;
                    if (slot.NormalPath != null)
                        plan.Unresolved.Add(new ArtAssetImportIssue { Path = slot.NormalPath, Reason = "нормаль без рисунка: «" + group.Name + "» · " + ArtAssetLabels.ViewTitle(slot.View) });
                    group.Slots.Remove(slot);
                }
                if (group.Slots.Count == 0) continue;
                group.Slots.Sort((a, b) =>
                {
                    int part = string.Compare(a.Part, b.Part, StringComparison.OrdinalIgnoreCase);
                    return part != 0 ? part : a.View.CompareTo(b.View);
                });
                plan.Groups.Add(group);
            }
            plan.Groups.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            return plan;
        }

        // Один файл: объект, ракурс, часть, рисунок или нормаль.
        public static bool Classify(IReadOnlyList<string> segments, string forcedAssetName, out string objectName, out ArtAssetView view,
            out string part, out ArtAssetFileKind kind, out string problem)
        {
            objectName = null; view = ArtAssetView.Front; part = string.Empty; kind = ArtAssetFileKind.Color; problem = null;
            if (segments == null || segments.Count == 0) { problem = "пустой путь"; return false; }
            string file = segments[segments.Count - 1];
            List<string> stem = Tokens(Path.GetFileNameWithoutExtension(file));

            // Нормаль: слово в имени файла или папка «Normal(s) / Нормали» ниже ракурса.
            bool normal = RemoveTokens(stem, NormalTokens);
            if (!normal && stem.Count > 1 && stem[stem.Count - 1] == "n") { normal = true; stem.RemoveAt(stem.Count - 1); }
            RemoveTokens(stem, ColorTokens);

            // Ракурс в имени файла?
            int fileViews = FindViews(stem, out ArtAssetView fileView, out int viewStart, out int viewLength);
            if (fileViews > 1) { problem = "неоднозначно: в имени несколько ракурсов"; return false; }

            int folderIndex = -1;
            ArtAssetView folderView = ArtAssetView.Front;
            for (int i = segments.Count - 2; i >= 0; i--)
            {
                List<string> tokens = Tokens(segments[i]);
                if (tokens.Count > 0 && tokens.All(token => NormalTokens.Contains(token))) { normal = true; continue; }
                if (FindViews(tokens, out ArtAssetView found, out int start, out int length) == 1 && length == tokens.Count && start == 0)
                {
                    folderIndex = i;
                    folderView = found;
                    break;
                }
            }

            kind = normal ? ArtAssetFileKind.Normal : ArtAssetFileKind.Color;
            if (fileViews == 1)
            {
                if (folderIndex >= 0 && folderView != fileView) { problem = "неоднозначно: ракурс папки и файла различаются"; return false; }
                view = fileView;
                stem.RemoveRange(viewStart, viewLength);
                // Плоские пары cart_Front.png / cart_Front_normal.png: остаток имени — объект.
                string rest = Join(stem);
                if (folderIndex >= 0)
                {
                    part = MainPartNames.Contains(rest) ? string.Empty : rest;
                    objectName = folderIndex > 0 ? segments[folderIndex - 1] : null;
                }
                else
                {
                    objectName = rest.Length > 0 ? rest : segments.Count > 1 ? segments[segments.Count - 2] : null;
                }
            }
            else if (folderIndex >= 0)
            {
                view = folderView;
                string rest = Join(stem);
                part = MainPartNames.Contains(rest) ? string.Empty : rest;
                objectName = folderIndex > 0 ? segments[folderIndex - 1] : null;
            }
            else
            {
                problem = "не удалось определить ракурс";
                return false;
            }

            if (!string.IsNullOrEmpty(forcedAssetName)) objectName = forcedAssetName;
            if (string.IsNullOrWhiteSpace(objectName)) { problem = "не удалось определить объект"; return false; }
            objectName = objectName.Trim();
            return true;
        }

        public static string NormalizeKey(string name) => Join(Tokens(name ?? string.Empty));

        // Слова имени: нижний регистр, разделители _ - пробел точка.
        public static List<string> Tokens(string value)
        {
            List<string> result = new List<string>();
            StringBuilder current = new StringBuilder();
            foreach (char c in value ?? string.Empty)
            {
                if (c == '_' || c == '-' || c == ' ' || c == '.')
                {
                    if (current.Length > 0) { result.Add(current.ToString()); current.Clear(); }
                }
                else current.Append(char.ToLowerInvariant(c));
            }
            if (current.Length > 0) result.Add(current.ToString());
            return result;
        }

        private static string Join(List<string> tokens) => string.Join("_", tokens);

        private static bool RemoveTokens(List<string> tokens, HashSet<string> set) => tokens.RemoveAll(set.Contains) > 0;

        // Ракурсы в словах (сначала двухсловные). Возвращает число разных найденных.
        private static int FindViews(List<string> tokens, out ArtAssetView view, out int start, out int length)
        {
            view = ArtAssetView.Front; start = -1; length = 0;
            HashSet<ArtAssetView> found = new HashSet<ArtAssetView>();
            for (int i = 0; i < tokens.Count; i++)
            {
                string two = i + 1 < tokens.Count ? tokens[i] + "_" + tokens[i + 1] : null;
                ArtAssetView? match = two != null ? Lookup(two) : null;
                int size = 2;
                if (match == null) { match = Lookup(tokens[i]); size = 1; }
                if (match == null) continue;
                if (found.Add(match.Value) && start < 0) { view = match.Value; start = i; length = size; }
                i += size - 1;
            }
            return found.Count;
        }

        private static ArtAssetView? Lookup(string alias)
        {
            foreach ((string name, ArtAssetView value) in ViewAliases)
                if (name == alias) return value;
            return null;
        }
    }
}

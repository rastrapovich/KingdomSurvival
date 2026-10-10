using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace KingdomSurvival.ArtAssets.Editor
{
    public enum ArtAssetFileKind { Color, Normal }

    // ПР-12П: следующий кадр анимации ракурса (номер — из имени файла).
    public sealed class ArtAssetImportFrame
    {
        public int Number;
        public string ColorPath;
        public string NormalPath;
    }

    // Один ракурс одной части: рисунок и его нормаль (пути файлов).
    public sealed class ArtAssetImportSlot
    {
        public ArtAssetView View;
        // Пусто — основа.
        public string Part = string.Empty;
        // Первый кадр (у неподвижного — единственный рисунок).
        public string ColorPath;
        public string NormalPath;
        // Ракурса в имени не было: поставлен ракурс по умолчанию (видно в сводке).
        public bool ViewAssumed;
        // Кадры после первого, по возрастанию номера.
        public readonly List<ArtAssetImportFrame> Frames = new List<ArtAssetImportFrame>();

        public int FrameCount => ColorPath == null ? 0 : 1 + Frames.Count;
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
        // Папка объекта (общая для его файлов) — для «Обновить из папки».
        public string SourceFolder = string.Empty;
        // Пропущено старых рендеров (та же ячейка, отметка времени раньше).
        public int SkippedOlder;
        public int ColorCount => Slots.Count(slot => slot.ColorPath != null);
        public int NormalCount => Slots.Count(slot => slot.NormalPath != null);
        public int MainViewCount => Slots.Count(slot => slot.Part.Length == 0 && slot.ColorPath != null);
        public int MaxFrameCount => Slots.Count == 0 ? 0 : Slots.Max(slot => slot.FrameCount);
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
    // ПР-12П: число последним словом имени (Front/000.png, трава_Front_001.png,
    // трава_Front_001_normal.png) — номер кадра анимации, если в том же ракурсе
    // той же части таких рисунков два и больше; одиночный номер остаётся
    // частью имени (rock_Front_01.png — объект «rock_01»). Без ракурса в имени
    // номер — кадр только при загрузке в выбранную запись или со словом
    // frame / кадр перед ним: rock_01.png, rock_02.png остаются разными объектами.
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

        // Слово перед номером кадра (необязательное).
        private static readonly HashSet<string> FrameTokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "frame", "кадр", "f" };

        // Имя «действия» рендера (KS Sprite Renderer: Объект/Idle/Front/Idle_Front_0001.png):
        // у предмета оно одно и в имя объекта и части не входит.
        private static readonly HashSet<string> ActionTokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "idle", "static", "still", "default", "anim", "animation", "loop", "sway", "wind", "breeze",
            "ожидание", "покой", "статика", "анимация", "ветер", "качание"
        };

        public const string NoViewProblem = "не удалось определить ракурс";

        public static bool IsImage(string path) =>
            ImageExtensions.Contains(Path.GetExtension(path ?? string.Empty).ToLowerInvariant());

        // Пакет из Проводника или Project: файлы и папки (папки — рекурсивно).
        // forcedAssetName — всё идёт в одну запись (перетаскивание на карточку).
        // defaultView — ракурс для файлов без ракурса в имени (новый объект из
        // одного рисунка); внутри папки объекта с ракурсами такие файлы остаются
        // на ручное назначение. null — не назначать.
        public static ArtAssetImportPlan ParsePaths(IEnumerable<string> paths, string forcedAssetName = null, ArtAssetView? defaultView = null)
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
            ArtAssetImportPlan plan = ParseFiles(files, forcedAssetName, defaultView);
            plan.Unresolved.AddRange(skipped);
            return plan;
        }

        // Нормали папкой для одного ассета: ракурс (и часть) → файлы по порядку
        // кадров. Суффикс «_normal» не нужен: всё в папке — нормали. Ракурс —
        // по папке или имени; без ракурса — defaultView. Номер в конце имени —
        // порядок кадров (без номера — по имени).
        public static Dictionary<(ArtAssetView view, string part), List<string>> ParseNormals(IEnumerable<string> paths, string assetName,
            ArtAssetView? defaultView, List<string> problems, Func<string, bool> isPart = null)
        {
            Dictionary<(ArtAssetView view, string part), List<(int frame, string path)>> found = new Dictionary<(ArtAssetView, string), List<(int, string)>>();
            foreach ((string full, List<string> segments) in Collect(paths))
            {
                bool ok = Classify(segments, assetName, true, out _, out ArtAssetView view, out string part, out _, out string problem, out int frame);
                if (!ok)
                {
                    if (problem != NoViewProblem || !defaultView.HasValue) { problems?.Add(Path.GetFileName(full) + ": " + problem); continue; }
                    List<string> stem = Tokens(Path.GetFileNameWithoutExtension(full));
                    RemoveTokens(stem, NormalTokens);
                    if (stem.Count > 1 && stem[stem.Count - 1] == "n") stem.RemoveAt(stem.Count - 1);
                    RemoveStamp(stem);
                    frame = TakeFrameNumber(stem, out _);
                    view = defaultView.Value;
                    part = string.Empty;
                }
                // Остаток имени — часть, только если такая часть у ассета есть (имена нормалей любые).
                string partName = !string.IsNullOrEmpty(part) && isPart != null && isPart(part) ? part : string.Empty;
                (ArtAssetView, string) key = (view, partName);
                if (!found.TryGetValue(key, out List<(int, string)> list)) found[key] = list = new List<(int, string)>();
                list.Add((frame, full));
            }
            return found.ToDictionary(pair => pair.Key, pair => pair.Value.OrderBy(item => item.frame).ThenBy(item => item.path, StringComparer.OrdinalIgnoreCase)
                .Select(item => item.path).ToList());
        }

        // Файлы пакета с путём от корня перетаскивания (папки — рекурсивно).
        private static List<(string full, List<string> segments)> Collect(IEnumerable<string> paths)
        {
            List<(string full, List<string> segments)> files = new List<(string, List<string>)>();
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
                        if (!IsImage(normalized)) continue;
                        List<string> segments = new List<string> { rootName };
                        segments.AddRange(normalized.Substring(path.Length).Trim('/').Split('/'));
                        files.Add((normalized, segments));
                    }
                }
                else if (IsImage(path))
                {
                    string parent = Path.GetFileName(Path.GetDirectoryName(path) ?? string.Empty);
                    List<string> segments = new List<string>();
                    if (!string.IsNullOrEmpty(parent)) segments.Add(parent);
                    segments.Add(Path.GetFileName(path));
                    files.Add((path, segments));
                }
            }
            return files;
        }

        // Разобранный файл пакета (первый проход).
        private sealed class ParsedFile
        {
            public string Full;
            public List<string> Segments;
            public bool Ok;
            public string ObjectName;
            public ArtAssetView View;
            public string Part;
            public ArtAssetFileKind Kind;
            public string Problem;
            public int Frame = -1;

            public string SlotKey => NormalizeKey(ObjectName) + "|" + View + "|" + Part.ToLowerInvariant();
        }

        // segments — путь от корня перетаскивания: [папка-корень, …, файл].
        // Последняя папка одиночного файла тоже передаётся: она — запасное имя.
        public static ArtAssetImportPlan ParseFiles(IEnumerable<(string full, List<string> segments)> files, string forcedAssetName = null,
            ArtAssetView? defaultView = null)
        {
            ArtAssetImportPlan plan = new ArtAssetImportPlan();
            Dictionary<string, ArtAssetImportGroup> groups = new Dictionary<string, ArtAssetImportGroup>(StringComparer.OrdinalIgnoreCase);
            // (группа, ракурс, часть, вид, кадр) → файлы: дубликаты выявляются до назначения.
            Dictionary<string, List<string>> claims = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, (ArtAssetImportGroup group, ArtAssetView view, string part, ArtAssetFileKind kind, int frame)> targets =
                new Dictionary<string, (ArtAssetImportGroup, ArtAssetView, string, ArtAssetFileKind, int)>(StringComparer.OrdinalIgnoreCase);

            HashSet<string> assumed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<(string full, List<string> segments)> viewless = new List<(string, List<string>)>();
            void Claim(string full, string objectName, ArtAssetView view, string part, ArtAssetFileKind kind, int frame)
            {
                string key = NormalizeKey(objectName);
                if (!groups.TryGetValue(key, out ArtAssetImportGroup group))
                {
                    group = new ArtAssetImportGroup { Name = objectName, Key = key };
                    groups.Add(key, group);
                }
                string claim = key + "|" + view + "|" + part.ToLowerInvariant() + "|" + kind + "|" + frame;
                if (!claims.TryGetValue(claim, out List<string> list)) { list = new List<string>(); claims.Add(claim, list); targets[claim] = (group, view, part, kind, frame); }
                list.Add(full);
            }

            // Первый проход — с номерами кадров; одиночный номер в ракурсе — снова как часть имени.
            List<ParsedFile> parsed = new List<ParsedFile>();
            foreach ((string full, List<string> segments) in files)
            {
                ParsedFile file = new ParsedFile { Full = full, Segments = segments };
                file.Ok = Classify(segments, forcedAssetName, true, out file.ObjectName, out file.View, out file.Part, out file.Kind, out file.Problem, out file.Frame);
                parsed.Add(file);
            }
            Dictionary<string, int> numbered = parsed.Where(file => file.Ok && file.Frame >= 0 && file.Kind == ArtAssetFileKind.Color)
                .GroupBy(file => file.SlotKey, StringComparer.OrdinalIgnoreCase).ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
            foreach (ParsedFile file in parsed)
            {
                if (file.Ok && file.Frame >= 0 && (!numbered.TryGetValue(file.SlotKey, out int count) || count < 2))
                    file.Ok = Classify(file.Segments, forcedAssetName, false, out file.ObjectName, out file.View, out file.Part, out file.Kind, out file.Problem, out file.Frame);
                if (!file.Ok)
                {
                    if (defaultView.HasValue && file.Problem == NoViewProblem) viewless.Add((file.Full, file.Segments));
                    else plan.Unresolved.Add(new ArtAssetImportIssue { Path = file.Full, Reason = file.Problem });
                    continue;
                }
                Claim(file.Full, file.ObjectName, file.View, file.Part, file.Kind, file.Frame);
            }

            // Файлы без ракурса: отдельный рисунок (и его пара *_normal) — новый
            // объект в ракурсе по умолчанию; рядом с ракурсами объекта — вручную.
            // Номер — кадр только в выбранной записи или со словом frame / кадр.
            HashSet<string> structured = new HashSet<string>(groups.Keys, StringComparer.OrdinalIgnoreCase);
            List<(string full, string name, string framedName, bool normal, int frame)> loose = new List<(string, string, string, bool, int)>();
            foreach ((string full, List<string> segments) in viewless)
            {
                string parent = segments.Count > 1 ? segments[segments.Count - 2] : string.Empty;
                if (string.IsNullOrEmpty(forcedAssetName) && parent.Length > 0 && structured.Contains(NormalizeKey(parent)))
                {
                    plan.Unresolved.Add(new ArtAssetImportIssue { Path = full, Reason = NoViewProblem + " (рядом с ракурсами «" + parent + "»)" });
                    continue;
                }
                List<string> stem = Tokens(Path.GetFileNameWithoutExtension(segments[segments.Count - 1]));
                bool normal = RemoveTokens(stem, NormalTokens);
                if (!normal && stem.Count > 1 && stem[stem.Count - 1] == "n") { normal = true; stem.RemoveAt(stem.Count - 1); }
                RemoveTokens(stem, ColorTokens);
                List<string> framed = new List<string>(stem);
                int frame = TakeFrameNumber(framed, out bool marker);
                if (frame >= 0 && string.IsNullOrEmpty(forcedAssetName) && !marker) frame = -1;
                string rest = Join(stem), framedRest = Join(framed);
                string name = !string.IsNullOrEmpty(forcedAssetName) ? forcedAssetName : rest.Length > 0 ? rest : parent;
                string framedName = !string.IsNullOrEmpty(forcedAssetName) ? forcedAssetName : framedRest.Length > 0 ? framedRest : parent;
                if (string.IsNullOrWhiteSpace(frame >= 0 ? framedName : name))
                {
                    plan.Unresolved.Add(new ArtAssetImportIssue { Path = full, Reason = "не удалось определить объект" });
                    continue;
                }
                loose.Add((full, name, framedName, normal, frame));
            }
            Dictionary<string, int> looseFrames = loose.Where(item => item.frame >= 0 && !item.normal)
                .GroupBy(item => NormalizeKey(item.framedName), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
            foreach ((string full, string name, string framedName, bool normal, int frame) in loose)
            {
                bool animated = frame >= 0 && looseFrames.TryGetValue(NormalizeKey(framedName), out int count) && count >= 2;
                assumed.Add(full);
                Claim(full, (animated ? framedName : name).Trim(), defaultView.Value, string.Empty,
                    normal ? ArtAssetFileKind.Normal : ArtAssetFileKind.Color, animated ? frame : -1);
            }

            // Ракурс части: кадры по номеру (без номера — первым), нормаль — к рисунку своего кадра.
            Dictionary<ArtAssetImportSlot, SortedDictionary<int, (string color, string normal)>> slotFrames =
                new Dictionary<ArtAssetImportSlot, SortedDictionary<int, (string, string)>>();
            foreach (KeyValuePair<string, List<string>> claim in claims)
            {
                (ArtAssetImportGroup group, ArtAssetView view, string part, ArtAssetFileKind kind, int frame) = targets[claim.Key];
                // Несколько рендеров одного ракурса с отметками времени — берётся самый свежий.
                if (claim.Value.Count > 1)
                {
                    List<(string path, string stamp)> stamped = claim.Value.Select(path => (path, Stamp(path))).ToList();
                    if (stamped.All(item => item.stamp.Length > 0) && stamped.Select(item => item.stamp).Distinct().Count() == stamped.Count)
                    {
                        string newest = stamped.OrderByDescending(item => item.stamp, StringComparer.Ordinal).First().path;
                        group.SkippedOlder += claim.Value.Count - 1;
                        claim.Value.Clear();
                        claim.Value.Add(newest);
                    }
                }
                if (claim.Value.Count > 1)
                {
                    foreach (string path in claim.Value)
                        plan.Unresolved.Add(new ArtAssetImportIssue { Path = path, Reason = "неоднозначно: несколько файлов для «" + group.Name + "» · " +
                            ArtAssetLabels.ViewTitle(view) + (part.Length > 0 ? " · " + part : "") + (frame >= 0 ? " · кадр " + frame : "") +
                            (kind == ArtAssetFileKind.Normal ? " (нормаль)" : "") });
                    continue;
                }
                ArtAssetImportSlot slot = group.Slot(view, part);
                if (!slotFrames.TryGetValue(slot, out SortedDictionary<int, (string color, string normal)> frames))
                    slotFrames[slot] = frames = new SortedDictionary<int, (string, string)>();
                frames.TryGetValue(frame, out (string color, string normal) entry);
                if (kind == ArtAssetFileKind.Color) entry.color = claim.Value[0];
                else entry.normal = claim.Value[0];
                frames[frame] = entry;
            }
            foreach (KeyValuePair<ArtAssetImportSlot, SortedDictionary<int, (string color, string normal)>> pair in slotFrames)
            {
                ArtAssetImportSlot slot = pair.Key;
                ArtAssetImportGroup group = groups.Values.First(item => item.Slots.Contains(slot));
                foreach (KeyValuePair<int, (string color, string normal)> frame in pair.Value)
                {
                    if (frame.Value.color == null)
                    {
                        // Нормаль без своего рисунка — не предмет и не пара: на ручное назначение.
                        plan.Unresolved.Add(new ArtAssetImportIssue { Path = frame.Value.normal, Reason = "нормаль без рисунка: «" + group.Name + "» · " +
                            ArtAssetLabels.ViewTitle(slot.View) + (frame.Key >= 0 ? " · кадр " + frame.Key : "") });
                        continue;
                    }
                    if (slot.ColorPath == null)
                    {
                        slot.ColorPath = frame.Value.color;
                        slot.NormalPath = frame.Value.normal;
                        slot.ViewAssumed = assumed.Contains(frame.Value.color);
                    }
                    else slot.Frames.Add(new ArtAssetImportFrame { Number = frame.Key, ColorPath = frame.Value.color, NormalPath = frame.Value.normal });
                }
            }

            foreach (ArtAssetImportGroup group in groups.Values)
            {
                group.Slots.RemoveAll(slot => slot.ColorPath == null);
                if (group.Slots.Count == 0) continue;
                group.SourceFolder = CommonFolder(group.Slots.Select(slot => slot.ColorPath));
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

        // Общая папка файлов объекта; папки ракурса, действия и нормалей — выше.
        private static string CommonFolder(IEnumerable<string> files)
        {
            List<string> folders = files.Where(path => !string.IsNullOrEmpty(path)).Select(path => (Path.GetDirectoryName(path) ?? string.Empty).Replace('\\', '/')).Distinct().ToList();
            if (folders.Count == 0) return string.Empty;
            string common = folders[0];
            foreach (string folder in folders.Skip(1))
                while (common.Length > 0 && !(folder + "/").StartsWith(common + "/", StringComparison.OrdinalIgnoreCase))
                    common = (Path.GetDirectoryName(common) ?? string.Empty).Replace('\\', '/');
            while (common.Length > 0)
            {
                List<string> tokens = Tokens(Path.GetFileName(common));
                bool service = tokens.Count > 0 && (tokens.All(NormalTokens.Contains) || IsAction(Path.GetFileName(common)) ||
                                                    (FindViews(tokens, out _, out int start, out int length) == 1 && start == 0 && length == tokens.Count));
                if (!service) break;
                common = (Path.GetDirectoryName(common) ?? string.Empty).Replace('\\', '/');
            }
            return common;
        }

        // Номер кадра — последнее слово из цифр (и слово frame / кадр перед ним):
        // убирается из stem. -1 — номера нет.
        private static int TakeFrameNumber(List<string> stem, out bool marker)
        {
            marker = false;
            if (stem.Count == 0) return -1;
            string last = stem[stem.Count - 1];
            if (last.Length == 0 || last.Length > 6 || !last.All(char.IsDigit)) return -1;
            stem.RemoveAt(stem.Count - 1);
            if (stem.Count > 0 && FrameTokens.Contains(stem[stem.Count - 1]))
            {
                stem.RemoveAt(stem.Count - 1);
                marker = true;
            }
            return int.Parse(last);
        }

        // Один файл: объект, ракурс, часть, рисунок или нормаль.
        public static bool Classify(IReadOnlyList<string> segments, string forcedAssetName, out string objectName, out ArtAssetView view,
            out string part, out ArtAssetFileKind kind, out string problem) =>
            Classify(segments, forcedAssetName, false, out objectName, out view, out part, out kind, out problem, out _);

        // frames — номер в конце имени считается кадром (frame ≥ 0) и в имя не входит.
        public static bool Classify(IReadOnlyList<string> segments, string forcedAssetName, bool frames, out string objectName, out ArtAssetView view,
            out string part, out ArtAssetFileKind kind, out string problem, out int frame)
        {
            objectName = null; view = ArtAssetView.Front; part = string.Empty; kind = ArtAssetFileKind.Color; problem = null; frame = -1;
            if (segments == null || segments.Count == 0) { problem = "пустой путь"; return false; }
            string file = segments[segments.Count - 1];
            List<string> stem = Tokens(Path.GetFileNameWithoutExtension(file));

            // Нормаль: слово в имени файла или папка «Normal(s) / Нормали» ниже ракурса.
            bool normal = RemoveTokens(stem, NormalTokens);
            if (!normal && stem.Count > 1 && stem[stem.Count - 1] == "n") { normal = true; stem.RemoveAt(stem.Count - 1); }
            RemoveTokens(stem, ColorTokens);
            // Отметка времени рендера («…__20261009-142811-641») — не имя и не кадр.
            RemoveStamp(stem);
            if (frames) frame = TakeFrameNumber(stem, out _);

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
            // Папка действия над папкой ракурса (Трава/Idle/Front): объект — выше неё.
            int objectIndex = folderIndex - 1;
            if (objectIndex >= 1 && IsAction(segments[objectIndex])) objectIndex--;

            kind = normal ? ArtAssetFileKind.Normal : ArtAssetFileKind.Color;
            if (fileViews == 1)
            {
                if (folderIndex >= 0 && folderView != fileView) { problem = "неоднозначно: ракурс папки и файла различаются"; return false; }
                view = fileView;
                stem.RemoveRange(viewStart, viewLength);
                // Действие рендера перед ракурсом (grass__Idle_Front…) — не часть имени.
                if (viewStart > 0 && viewStart - 1 < stem.Count && ActionTokens.Contains(stem[viewStart - 1])) stem.RemoveAt(viewStart - 1);
                // Плоские пары cart_Front.png / cart_Front_normal.png: остаток имени — объект.
                string rest = Join(stem);
                if (folderIndex >= 0)
                {
                    objectName = objectIndex >= 0 ? segments[objectIndex] : null;
                    part = PartName(stem, objectName, forcedAssetName);
                }
                else
                {
                    // Без папки ракурса: имя — остаток имени файла, иначе папка (папка действия — пропускается).
                    int parent = segments.Count - 2;
                    if (parent >= 1 && IsAction(segments[parent])) parent--;
                    objectName = rest.Length > 0 ? rest : parent >= 0 ? segments[parent] : null;
                }
            }
            else if (folderIndex >= 0)
            {
                view = folderView;
                objectName = objectIndex >= 0 ? segments[objectIndex] : null;
                part = PartName(stem, objectName, forcedAssetName);
            }
            else
            {
                problem = NoViewProblem;
                return false;
            }

            if (!string.IsNullOrEmpty(forcedAssetName)) objectName = forcedAssetName;
            if (string.IsNullOrWhiteSpace(objectName)) { problem = "не удалось определить объект"; return false; }
            objectName = objectName.Trim();
            return true;
        }

        public static string NormalizeKey(string name) => Join(Tokens(name ?? string.Empty));

        // Часть по остатку имени файла в папке ракурса. Основа — пусто, имя
        // основы, только слова действия или имя самого объекта (рендер кладёт
        // имя объекта в имя каждого кадра: Трава/Front/Трава_Front_0001.png).
        private static string PartName(List<string> stem, string objectName, string forcedAssetName)
        {
            List<string> rest = stem.Where(token => !ActionTokens.Contains(token)).ToList();
            string joined = Join(rest);
            if (MainPartNames.Contains(joined)) return string.Empty;
            foreach (string name in new[] { objectName, forcedAssetName })
            {
                if (string.IsNullOrEmpty(name)) continue;
                List<string> own = Tokens(name).Where(token => !ActionTokens.Contains(token)).ToList();
                if (Join(own) == joined) return string.Empty;
            }
            return joined;
        }

        private static bool IsAction(string segment)
        {
            List<string> tokens = Tokens(segment);
            return tokens.Count > 0 && tokens.All(ActionTokens.Contains);
        }

        // Отметка времени рендера: ГГГГММДД, ЧЧММСС и (необязательно) миллисекунды.
        private static void RemoveStamp(List<string> tokens)
        {
            for (int i = 0; i + 1 < tokens.Count; i++)
            {
                if (!IsDigits(tokens[i], 8) || !IsDigits(tokens[i + 1], 6)) continue;
                int count = i + 2 < tokens.Count && tokens[i + 2].Length <= 4 && tokens[i + 2].All(char.IsDigit) ? 3 : 2;
                tokens.RemoveRange(i, count);
                return;
            }
        }

        private static bool IsDigits(string token, int length) => token.Length == length && token.All(char.IsDigit);

        // Отметка времени рендера в имени файла («20261009142811641»); нет — пусто.
        public static string Stamp(string path)
        {
            List<string> tokens = Tokens(Path.GetFileNameWithoutExtension(path ?? string.Empty));
            for (int i = 0; i + 1 < tokens.Count; i++)
            {
                if (!IsDigits(tokens[i], 8) || !IsDigits(tokens[i + 1], 6)) continue;
                string ms = i + 2 < tokens.Count && tokens[i + 2].Length <= 4 && tokens[i + 2].All(char.IsDigit) ? tokens[i + 2].PadLeft(4, '0') : "0000";
                return tokens[i] + tokens[i + 1] + ms;
            }
            return string.Empty;
        }

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

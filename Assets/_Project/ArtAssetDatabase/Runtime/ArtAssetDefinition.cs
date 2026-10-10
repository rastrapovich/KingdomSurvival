using System;
using System.Collections.Generic;
using UnityEngine;

namespace KingdomSurvival.ArtAssets
{
    // Следующий кадр покадровой анимации: рисунок и его карта нормалей.
    [Serializable]
    public sealed class ArtAssetFrame
    {
        public Sprite Sprite;
        public Texture2D NormalMap;
    }

    // Одна часть объекта в одном ракурсе: рисунок, его карта нормалей,
    // смещение и (необязательно) свой силуэт тени.
    [Serializable]
    public sealed class ArtAssetPartView
    {
        public Sprite Sprite;
        // Карта нормалей этого рисунка. В рендере 2D-свет берёт её из
        // настроек импорта рисунка (вторая текстура _NormalMap); поле хранит
        // назначение для проверки, замены и снятия.
        public Texture2D NormalMap;
        // Смещение левого нижнего угла рисунка части от левого нижнего угла
        // рисунка основы, единицы мира (Y вверх). Слои одного кадра — ноль.
        public Vector2 Offset;
        // Свой силуэт тени (крона: тень не совпадает с рисунком).
        public Sprite ShadowSprite;
        // ПР-12П: кадры анимации после первого (первый — Sprite выше). Пусто —
        // неподвижный рисунок. Кадры одного размера с первым; нормаль — своя
        // у каждого кадра (подключается к его рисунку при импорте).
        public List<ArtAssetFrame> Frames = new List<ArtAssetFrame>();
        // Лист кадров: те же кадры (первый и следующие), собранные базой на
        // страницы-атласы, нормали — той же раскладкой (вторая текстура
        // страницы). Места показывают анимацию из листа: кадры одной
        // текстуры собираются в одну отрисовку. Кадры выше — источник правки;
        // SheetKey — их отпечаток (изменились — лист собирается заново).
        public List<Sprite> SheetFrames = new List<Sprite>();
        public List<Texture2D> SheetNormals = new List<Texture2D>();
        public string SheetKey = "";

        public bool HasSprite => Sprite != null;

        // Лист собран и годится: по рисунку на каждый кадр.
        public bool HasSheet
        {
            get
            {
                if (SheetFrames == null || SheetFrames.Count < 2 || SheetFrames.Count != FrameCount) return false;
                foreach (Sprite sprite in SheetFrames) if (sprite == null) return false;
                return true;
            }
        }

        // Кадры для показа: из листа, если он собран, иначе отдельные файлы.
        public Sprite[] PlaybackSprites() => HasSheet ? SheetFrames.ToArray() : FrameSprites();
        public bool HasNormal => Sprite != null && NormalMap != null;

        // Кадров всего, считая первый; кадры без рисунка пропускаются.
        public int FrameCount
        {
            get
            {
                if (Sprite == null) return 0;
                int count = 1;
                if (Frames != null)
                    foreach (ArtAssetFrame frame in Frames) if (frame?.Sprite != null) count++;
                return count;
            }
        }

        public bool IsAnimated => FrameCount > 1;

        // Рисунки кадров по порядку, первый — Sprite.
        public Sprite[] FrameSprites()
        {
            if (Sprite == null) return Array.Empty<Sprite>();
            List<Sprite> result = new List<Sprite> { Sprite };
            if (Frames != null)
                foreach (ArtAssetFrame frame in Frames) if (frame?.Sprite != null) result.Add(frame.Sprite);
            return result.ToArray();
        }

        // Нормаль кадра с этим рисунком (первого или следующего).
        public Texture2D NormalOf(Sprite sprite)
        {
            if (sprite == null) return null;
            if (sprite == Sprite) return NormalMap;
            if (SheetFrames != null)
            {
                int index = SheetFrames.IndexOf(sprite);
                if (index >= 0) return SheetNormals != null && index < SheetNormals.Count ? SheetNormals[index] : null;
            }
            if (Frames != null)
                foreach (ArtAssetFrame frame in Frames) if (frame != null && frame.Sprite == sprite) return frame.NormalMap;
            return null;
        }
    }

    // Логическая часть: «Основа», «Крыша», «Крона», «Ствол» или своё имя.
    // Первая часть — основа: по ней считаются опора, размер и полнота ракурсов.
    [Serializable]
    public sealed class ArtAssetPart
    {
        public string Id = Guid.NewGuid().ToString("N");
        public string Name = "Основа";
        public ArtAssetLayer Layer = ArtAssetLayer.World;
        public int OrderOffset;
        // Тень от солнца — силуэт рисунка на земле.
        public bool ProjectsShadow = true;
        // Тень от огня и других местных источников — перекрытие света по
        // контуру рисунка. 0 — прежняя запись (как тень от солнца), 1 — да, 2 — нет.
        public int FireShadowState;
        public bool ProjectsFireShadow
        {
            get => FireShadowState == 0 ? ProjectsShadow : FireShadowState == 1;
            set => FireShadowState = value ? 1 : 2;
        }
        public List<ArtAssetPartView> Views = new List<ArtAssetPartView>();

        public ArtAssetPartView View(ArtAssetView view)
        {
            if (Views == null) Views = new List<ArtAssetPartView>();
            while (Views.Count < ArtAssetLabels.ViewCount) Views.Add(new ArtAssetPartView());
            if (Views[(int)view] == null) Views[(int)view] = new ArtAssetPartView();
            return Views[(int)view];
        }

        public ArtAssetPartView FindView(ArtAssetView view) =>
            Views != null && (int)view < Views.Count ? Views[(int)view] : null;
    }

    // Настройки одного ракурса: опора и основание на земле. У фронтального
    // и заднего вида они могут отличаться.
    [Serializable]
    public sealed class ArtAssetViewSettings
    {
        // Точка касания земли: доля рисунка основы (0..1, Y вверх, как у Sprite).
        public Vector2 Pivot = new Vector2(.5f, .1f);
        // Занятая область земли: размер и смещение центра от опоры (единицы мира, Y вверх).
        public Vector2 FootprintSize = new Vector2(1.2f, .55f);
        public Vector2 FootprintOffset;
        // Основание кистью: закрашенные клетки заменяют прямоугольник выше.
        public ArtAssetFootprintMask FootprintMask = new ArtAssetFootprintMask();

        public bool UsesFootprintMask => FootprintMask != null && !FootprintMask.IsEmpty;
    }

    // Основание кистью — маска занятой земли ракурса для предметов неровной
    // формы (камень, коряга, частокол). Сетка клеток в единицах мира от опоры
    // (X вправо, Y вглубь — вверх на рисунке): левый нижний угол Origin,
    // сторона клетки Cell, Columns × Rows. Bits — клетки по рядам снизу
    // вверх, по биту на клетку (base64). Ни одной клетки — основание
    // прямоугольником. В местах маска — набор прямоугольников (Rects).
    [Serializable]
    public sealed class ArtAssetFootprintMask
    {
        // Наибольшая сетка: столько клеток по стороне не бывает больше.
        public const int MaxSide = 256;

        public Vector2 Origin;
        public float Cell = .05f;
        public int Columns;
        public int Rows;
        public string Bits = "";

        [NonSerialized] private string decoded;
        [NonSerialized] private bool[] cells;
        [NonSerialized] private int count;
        [NonSerialized] private List<Rect> rects;

        public bool HasGrid => Columns > 0 && Rows > 0 && Cell > 0;
        public bool IsEmpty => !HasGrid || Count == 0;
        public Rect GridRect => new Rect(Origin, new Vector2(Columns, Rows) * Cell);

        public int Count
        {
            get
            {
                Decode();
                return count;
            }
        }

        public bool Get(int column, int row)
        {
            if (column < 0 || row < 0 || column >= Columns || row >= Rows) return false;
            Decode();
            return cells[row * Columns + column];
        }

        // Точка (единицы мира от опоры) — на закрашенной клетке.
        public bool Contains(Vector2 point)
        {
            if (!HasGrid) return false;
            return Get(Mathf.FloorToInt((point.x - Origin.x) / Cell), Mathf.FloorToInt((point.y - Origin.y) / Cell));
        }

        // Пустая сетка, покрывающая area, со стороной клетки cell.
        public void Reset(Rect area, float cell)
        {
            Cell = Mathf.Max(.002f, cell);
            Columns = Mathf.Clamp(Mathf.CeilToInt(area.width / Cell), 1, MaxSide);
            Rows = Mathf.Clamp(Mathf.CeilToInt(area.height / Cell), 1, MaxSide);
            Origin = area.position;
            cells = new bool[Columns * Rows];
            count = 0;
            Encode();
        }

        // Сетка расширяется до area (закрашенное остаётся на месте).
        public void Grow(Rect area)
        {
            if (!HasGrid) { Reset(area, Cell); return; }
            Rect current = GridRect;
            if (current.xMin <= area.xMin && current.yMin <= area.yMin && current.xMax >= area.xMax && current.yMax >= area.yMax) return;
            Decode();
            bool[] old = cells;
            int oldColumns = Columns, oldRows = Rows;
            Vector2 oldOrigin = Origin;
            // Новый угол — по шагу прежней сетки: клетки ложатся точно друг на друга.
            int left = Mathf.Max(0, Mathf.CeilToInt((current.xMin - area.xMin) / Cell));
            int bottom = Mathf.Max(0, Mathf.CeilToInt((current.yMin - area.yMin) / Cell));
            int right = Mathf.Max(0, Mathf.CeilToInt((area.xMax - current.xMax) / Cell));
            int top = Mathf.Max(0, Mathf.CeilToInt((area.yMax - current.yMax) / Cell));
            left = Mathf.Min(left, MaxSide - oldColumns); right = Mathf.Min(right, MaxSide - oldColumns - left);
            bottom = Mathf.Min(bottom, MaxSide - oldRows); top = Mathf.Min(top, MaxSide - oldRows - bottom);
            Columns = oldColumns + left + right;
            Rows = oldRows + bottom + top;
            Origin = oldOrigin - new Vector2(left, bottom) * Cell;
            cells = new bool[Columns * Rows];
            for (int r = 0; r < oldRows; r++)
                for (int c = 0; c < oldColumns; c++)
                    cells[(r + bottom) * Columns + c + left] = old[r * oldColumns + c];
            Encode();
        }

        // Кисть: круг радиуса radius с центром center закрашивается (value)
        // или стирается. true — что-то изменилось.
        public bool Paint(Vector2 center, float radius, bool value)
        {
            if (value) Grow(Rect.MinMaxRect(center.x - radius, center.y - radius, center.x + radius, center.y + radius));
            if (!HasGrid) return false;
            Decode();
            bool changed = false;
            int c0 = Mathf.Max(0, Mathf.FloorToInt((center.x - radius - Origin.x) / Cell));
            int c1 = Mathf.Min(Columns - 1, Mathf.FloorToInt((center.x + radius - Origin.x) / Cell));
            int r0 = Mathf.Max(0, Mathf.FloorToInt((center.y - radius - Origin.y) / Cell));
            int r1 = Mathf.Min(Rows - 1, Mathf.FloorToInt((center.y + radius - Origin.y) / Cell));
            // Клетка меньше кисти — по центру клетки; кисть меньше клетки — клетка под центром.
            float reach = Mathf.Max(radius, Cell * .5f);
            for (int r = r0; r <= r1; r++)
                for (int c = c0; c <= c1; c++)
                {
                    Vector2 point = Origin + new Vector2(c + .5f, r + .5f) * Cell;
                    if ((point - center).sqrMagnitude > reach * reach) continue;
                    int index = r * Columns + c;
                    if (cells[index] == value) continue;
                    cells[index] = value;
                    count += value ? 1 : -1;
                    changed = true;
                }
            if (changed) Encode();
            return changed;
        }

        // Закрасить клетки, чьи центры удовлетворяют условию (прямоугольник, силуэт).
        public void Fill(Func<Vector2, bool> inside)
        {
            if (!HasGrid || inside == null) return;
            Decode();
            count = 0;
            for (int r = 0; r < Rows; r++)
                for (int c = 0; c < Columns; c++)
                {
                    bool value = inside(Origin + new Vector2(c + .5f, r + .5f) * Cell);
                    cells[r * Columns + c] = value;
                    if (value) count++;
                }
            Encode();
        }

        public void Clear()
        {
            Columns = Rows = 0;
            Bits = "";
            cells = null;
            decoded = null;
            count = 0;
            rects = null;
        }

        // Закрашенное — прямоугольниками (единицы мира от опоры): отрезки
        // рядов, одинаковые в соседних рядах, сливаются в один.
        public List<Rect> Rects()
        {
            Decode();
            if (rects != null) return rects;
            rects = new List<Rect>();
            Dictionary<(int, int), int> open = new Dictionary<(int, int), int>();
            Dictionary<(int, int), int> next = new Dictionary<(int, int), int>();
            for (int r = 0; r <= Rows; r++)
            {
                next.Clear();
                if (r < Rows)
                    for (int c = 0; c < Columns; c++)
                    {
                        if (!cells[r * Columns + c]) continue;
                        int start = c;
                        while (c + 1 < Columns && cells[r * Columns + c + 1]) c++;
                        (int, int) span = (start, c);
                        next[span] = open.TryGetValue(span, out int from) ? from : r;
                    }
                foreach (KeyValuePair<(int, int), int> item in open)
                {
                    if (next.ContainsKey(item.Key)) continue;
                    rects.Add(new Rect(Origin + new Vector2(item.Key.Item1, item.Value) * Cell,
                        new Vector2(item.Key.Item2 - item.Key.Item1 + 1, r - item.Value) * Cell));
                }
                (open, next) = (next, open);
            }
            return rects;
        }

        // Охват закрашенного (единицы мира от опоры); пусто — нулевой прямоугольник.
        public Rect Bounds()
        {
            List<Rect> all = Rects();
            if (all.Count == 0) return default;
            Rect result = all[0];
            foreach (Rect rect in all)
                result = Rect.MinMaxRect(Mathf.Min(result.xMin, rect.xMin), Mathf.Min(result.yMin, rect.yMin),
                    Mathf.Max(result.xMax, rect.xMax), Mathf.Max(result.yMax, rect.yMax));
            return result;
        }

        public ArtAssetFootprintMask Clone() =>
            new ArtAssetFootprintMask { Origin = Origin, Cell = Cell, Columns = Columns, Rows = Rows, Bits = Bits };

        private void Decode()
        {
            int size = Mathf.Max(0, Columns) * Mathf.Max(0, Rows);
            if (cells != null && cells.Length == size && string.Equals(decoded, Bits, StringComparison.Ordinal)) return;
            cells = new bool[size];
            count = 0;
            rects = null;
            decoded = Bits;
            if (size == 0 || string.IsNullOrEmpty(Bits)) return;
            byte[] bytes;
            try { bytes = Convert.FromBase64String(Bits); }
            catch (FormatException) { return; }
            for (int i = 0; i < size && (i >> 3) < bytes.Length; i++)
            {
                if ((bytes[i >> 3] & (1 << (i & 7))) == 0) continue;
                cells[i] = true;
                count++;
            }
        }

        private void Encode()
        {
            byte[] bytes = new byte[(cells.Length + 7) / 8];
            for (int i = 0; i < cells.Length; i++)
                if (cells[i]) bytes[i >> 3] |= (byte)(1 << (i & 7));
            Bits = count > 0 ? Convert.ToBase64String(bytes) : "";
            decoded = Bits;
            rects = null;
        }
    }

    // ПР-12Н: запись Базы ассетов — один логический объект с шестью ракурсами.
    // Ссылки из мест — только по Id: не по названию, порядку или пути файла.
    [Serializable]
    public sealed class ArtAssetDefinition
    {
        public const float DefaultPixelsPerUnit = 108f;

        public string Id = NewId();
        public string Name = "Новый ассет";
        public ArtAssetCategory Category;
        public List<string> Tags = new List<string>();
        public bool Favorite;
        public string Credit = "";
        public string SourceUrl = "";
        // Имя источника последнего импорта (папка или общая часть имён файлов) —
        // подсказка для поиска дубликатов, не ключ.
        public string ImportKey = "";
        // Папка, из которой ассет загружен последний раз (вне проекта) —
        // «Обновить из папки» перечитывает её: новые рендеры и нормали.
        public string SourceFolder = "";

        // Масштаб всех ракурсов и частей: сколько пикселей рисунка в единице
        // мира. Прозрачные поля PNG не меняют размер и не сдвигают объект.
        public float PixelsPerUnit = DefaultPixelsPerUnit;

        // Рекомендуемые настройки места; экземпляр может переопределить явно.
        public bool BlocksMovement;
        public bool OccludesLight;
        public float ShadowLength = 1;
        // Какой свет действует на рисунок в местах: солнце (общий свет, смена
        // суток) и огонь (местные источники — костёр, факел). Выключенное
        // солнце — рисунок не темнеет ночью (всегда как днём); выключенный
        // огонь — местные источники его не освещают.
        public bool LitBySun = true;
        public bool LitByFire = true;

        // ПР-12П: покадровая анимация (трава, флаг, вода). Скорость и порядок
        // общие для всех частей и ракурсов; у каждой части — свои кадры.
        // Случайная фаза — у каждого экземпляра свой сдвиг по ID, чтобы
        // заросль из одинаковых кустов не качалась в такт.
        public float FramesPerSecond = 8;
        public ArtAssetPlayback Playback = ArtAssetPlayback.Loop;
        public bool RandomPhase = true;

        public List<ArtAssetViewSettings> ViewSettings = new List<ArtAssetViewSettings>();
        public List<ArtAssetPart> Parts = new List<ArtAssetPart> { new ArtAssetPart() };

        public static string NewId() => "asset_" + Guid.NewGuid().ToString("N").Substring(0, 12);

        public ArtAssetPart MainPart
        {
            get
            {
                if (Parts == null) Parts = new List<ArtAssetPart>();
                if (Parts.Count == 0) Parts.Add(new ArtAssetPart());
                return Parts[0];
            }
        }

        public ArtAssetViewSettings Settings(ArtAssetView view)
        {
            if (ViewSettings == null) ViewSettings = new List<ArtAssetViewSettings>();
            while (ViewSettings.Count < ArtAssetLabels.ViewCount) ViewSettings.Add(new ArtAssetViewSettings());
            if (ViewSettings[(int)view] == null) ViewSettings[(int)view] = new ArtAssetViewSettings();
            return ViewSettings[(int)view];
        }

        public Sprite MainSprite(ArtAssetView view) => Parts != null && Parts.Count > 0 ? Parts[0].FindView(view)?.Sprite : null;

        public bool HasView(ArtAssetView view) => MainSprite(view) != null;

        public bool HasNormal(ArtAssetView view)
        {
            ArtAssetPartView main = Parts != null && Parts.Count > 0 ? Parts[0].FindView(view) : null;
            return main != null && main.HasNormal;
        }

        public int ViewCount
        {
            get
            {
                int count = 0;
                foreach (ArtAssetView view in ArtAssetLabels.Views) if (HasView(view)) count++;
                return count;
            }
        }

        // Нормали считаются по ракурсам, где есть рисунок основы.
        public int NormalCount
        {
            get
            {
                int count = 0;
                foreach (ArtAssetView view in ArtAssetLabels.Views) if (HasNormal(view)) count++;
                return count;
            }
        }

        // Есть ли кадры анимации хотя бы у одной части в каком-нибудь ракурсе.
        public bool IsAnimated
        {
            get
            {
                if (Parts == null) return false;
                foreach (ArtAssetPart part in Parts)
                {
                    if (part?.Views == null) continue;
                    foreach (ArtAssetPartView view in part.Views)
                        if (view != null && view.IsAnimated) return true;
                }
                return false;
            }
        }

        // Наибольшее число кадров среди частей в ракурсе (1 — неподвижный).
        public int FrameCountIn(ArtAssetView view)
        {
            int count = 0;
            if (Parts == null) return count;
            foreach (ArtAssetPart part in Parts)
            {
                ArtAssetPartView slot = part?.FindView(view);
                if (slot != null) count = Math.Max(count, slot.FrameCount);
            }
            return count;
        }

        public bool IsEmpty
        {
            get
            {
                if (Parts == null) return true;
                foreach (ArtAssetPart part in Parts)
                {
                    if (part?.Views == null) continue;
                    foreach (ArtAssetPartView view in part.Views)
                        if (view != null && view.Sprite != null) return false;
                }
                return true;
            }
        }

        // Ракурс, который будет показан вместо запрошенного; false — рисунка нет совсем.
        public bool TryResolveView(ArtAssetView requested, out ArtAssetView shown)
        {
            foreach (ArtAssetView view in ArtAssetLabels.FallbackOrder(requested))
            {
                if (HasView(view)) { shown = view; return true; }
            }
            shown = requested;
            return false;
        }

        // Эталонный ракурс размера: «Спереди», при его отсутствии — первый имеющийся.
        public bool TryReferenceView(out ArtAssetView view) => TryResolveView(ArtAssetView.Front, out view);

        public float SafePixelsPerUnit => PixelsPerUnit > .01f ? PixelsPerUnit : DefaultPixelsPerUnit;

        // Высота рисунка основы в ракурсе (единицы мира, без масштаба экземпляра).
        public float HeightIn(ArtAssetView view)
        {
            Sprite sprite = MainSprite(view);
            return sprite != null ? sprite.rect.height / SafePixelsPerUnit : 0;
        }

        // Игровой размер: высота эталонного ракурса. Запись меняет масштаб всех ракурсов.
        public float Height
        {
            get => TryReferenceView(out ArtAssetView view) ? HeightIn(view) : 0;
            set
            {
                if (value <= 0 || !TryReferenceView(out ArtAssetView view)) return;
                PixelsPerUnit = MainSprite(view).rect.height / value;
            }
        }

        public bool MatchesQuery(string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return true;
            query = query.Trim();
            if (Contains(Name, query) || Contains(Id, query)) return true;
            if (Tags != null)
                foreach (string tag in Tags) if (Contains(tag, query)) return true;
            return false;
        }

        private static bool Contains(string value, string query) =>
            !string.IsNullOrEmpty(value) && value.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;

        public string TagsText
        {
            get => Tags == null ? string.Empty : string.Join(", ", Tags);
            set
            {
                Tags = new List<string>();
                if (string.IsNullOrWhiteSpace(value)) return;
                foreach (string tag in value.Split(','))
                {
                    string trimmed = tag.Trim();
                    if (trimmed.Length > 0 && !Tags.Contains(trimmed)) Tags.Add(trimmed);
                }
            }
        }
    }
}

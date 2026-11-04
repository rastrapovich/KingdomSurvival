using System.Collections.Generic;
using UnityEngine;

namespace KingdomSurvival.MapLab
{
    // Свободные окончания тушевых линий (MAP_STYLE_GUIDE.md §2.5): у видимого
    // конца рисованного следа — «основная линия → просвет → короткий штрих по
    // ходу кривой → редкие уменьшающиеся точки» или сокращённый вариант.
    //
    // Работает по длине дуги одной полилинии пера и её непрозрачности по
    // вершинам (та же, что у ленты: вес слоя × художественные разрывы).
    // Конец следа — место, где непрозрачность уходит ниже порога видимости;
    // технический конец массива (край карты) и шов замкнутого контура концом
    // не считаются (замкнутый контур вызывающий код поворачивает так, чтобы
    // массив начинался в просвете). Завершение кладётся внутрь существующей
    // траектории: часть — за счёт укорочения основной линии, часть — в
    // просвет, не дальше половины просвета до соседнего следа (два хвоста не
    // встречаются). Если места мало — сначала меньше элементов, потом короче,
    // иначе — обычное потерянное окончание.
    //
    // Случайность — отдельный поток (seed карты и положение конца в мире):
    // география и другие потоки не тратятся, зум и перестройка не пересеивают.
    public static class MapLabInkEndings
    {
        // Порог видимости следа по непрозрачности вершины.
        public const float Visible = 0.3f;

        public struct Options
        {
            public float Unit;      // масштаб завершения, ед. мира (толщина пера × масштаб)
            public float Variety;   // 0 — почти всегда полная цепочка со средними размерами, 1 — полный разброс
            public uint Seed;
        }

        public enum Kind { Dash, Dot }

        public struct Element
        {
            public Kind Kind;
            public float S0, S1;    // по длине дуги, S0 ≤ S1
            public float Width;     // множитель толщины пера
            public float Alpha;     // непрозрачность
            public float Elong;     // точка: длина к толщине
            public float Offset;    // точка: сдвиг поперёк линии, доля единицы завершения
        }

        public struct Cut
        {
            public float S;         // где обрывается основная линия
            public int Dir;         // +1 — видимый след до S, −1 — после S
            public float Alpha;     // непрозрачность основной линии у обрыва
            public float ZoneEnd;   // до сюда основная линия погашена (за обрывом)
        }

        public sealed class Plan
        {
            public readonly List<Cut> Cuts = new List<Cut>();
            public readonly List<Element> Elements = new List<Element>();
            public int Considered;  // подлинные свободные концы
            public int Decorated;   // из них с завершением
            public float Unit;

            // Основная линия погашена в зоне завершения.
            public float Mask(float s)
            {
                foreach (Cut c in Cuts)
                {
                    if (c.Dir > 0 && s > c.S && s <= c.ZoneEnd)
                        return 0f;
                    if (c.Dir < 0 && s < c.S && s >= c.ZoneEnd)
                        return 0f;
                }
                return 1f;
            }

            // Перед обрывом линия немного тоньше (кисть отрывается).
            public float Taper(float s)
            {
                float w = 1f;
                float reach = Unit * 2.5f;
                foreach (Cut c in Cuts)
                {
                    float d = (c.S - s) * c.Dir;
                    if (d >= 0f && d < reach)
                        w = Mathf.Min(w, Mathf.Lerp(0.72f, 1f, d / reach));
                }
                return w;
            }
        }

        private struct Run
        {
            public float S0, S1;    // пересечения порога (или концы массива)
            public bool OpenStart, OpenEnd; // конец у края массива
            public int I0, I1;      // первая и последняя видимые вершины
        }

        // points/arc/alpha — вершины одной полилинии (arc по возрастанию).
        // freeEnds — концы массива сами являются художественными концами
        // (короткие штрихи борозд); иначе следы у концов массива не украшаются.
        // hold — вершины, занятые чужой формой (полотно дороги у межи): это
        // примыкание, а не свободный конец — у такого места завершения нет и
        // хвост туда не заходит (null — нет таких вершин).
        public static Plan Build(IReadOnlyList<Vector2> points, IReadOnlyList<float> arc, IReadOnlyList<float> alpha, bool freeEnds, float chance, Options o, IReadOnlyList<bool> hold = null)
        {
            Plan plan = new Plan { Unit = Mathf.Max(1e-3f, o.Unit) };
            int m = arc.Count;
            if (m < 2 || points.Count != m || alpha.Count != m)
                return plan;
            float total = arc[m - 1];
            if (!(total > plan.Unit * 2f))
                return plan;

            List<Run> runs = new List<Run>();
            int i = 0;
            while (i < m)
            {
                if (!(alpha[i] >= Visible))
                {
                    i++;
                    continue;
                }
                int i0 = i;
                while (i + 1 < m && alpha[i + 1] >= Visible)
                    i++;
                int i1 = i;
                Run r = new Run
                {
                    S0 = i0 == 0 ? arc[0] : Cross(arc, alpha, i0 - 1, i0),
                    S1 = i1 == m - 1 ? arc[m - 1] : Cross(arc, alpha, i1, i1 + 1),
                    OpenStart = i0 == 0,
                    OpenEnd = i1 == m - 1,
                    I0 = i0,
                    I1 = i1,
                };
                runs.Add(r);
                i++;
            }

            for (int k = 0; k < runs.Count; k++)
            {
                Run r = runs[k];
                float runLength = r.S1 - r.S0;
                // Начало следа (видимо после S0).
                if (!r.OpenStart || freeEnds)
                {
                    float room = r.OpenStart ? 0f : (k > 0 ? (r.S0 - runs[k - 1].S1) * 0.5f : r.S0 - arc[0]);
                    if (!r.OpenStart && hold != null)
                        room = Mathf.Min(room, HoldDistance(arc, hold, r.I0 - 1, -1, r.S0, plan.Unit));
                    if (room >= 0f)
                        Decorate(plan, points, arc, alpha, r.S0, -1, runLength, room, chance, o);
                }
                // Конец следа (видим до S1).
                if (!r.OpenEnd || freeEnds)
                {
                    float room = r.OpenEnd ? 0f : (k + 1 < runs.Count ? (runs[k + 1].S0 - r.S1) * 0.5f : arc[m - 1] - r.S1);
                    if (!r.OpenEnd && hold != null)
                        room = Mathf.Min(room, HoldDistance(arc, hold, r.I1 + 1, +1, r.S1, plan.Unit));
                    if (room >= 0f)
                        Decorate(plan, points, arc, alpha, r.S1, +1, runLength, room, chance, o);
                }
            }
            plan.Cuts.Sort((a, b) => a.S.CompareTo(b.S));
            return plan;
        }

        // Расстояние по дуге от конца следа до первой занятой вершины за ним.
        // Свободное место до него (минус запас); занятое место ближе четырёх
        // единиц завершения — примыкание: −1 (без завершения).
        private static float HoldDistance(IReadOnlyList<float> arc, IReadOnlyList<bool> hold, int from, int dir, float sEnd, float unit)
        {
            for (int i = from; i >= 0 && i < arc.Count; i += dir)
            {
                if (!hold[i])
                    continue;
                float d = Mathf.Abs(arc[i] - sEnd);
                return d < unit * 4f ? -1f : d - unit;
            }
            return float.MaxValue;
        }

        private static float Cross(IReadOnlyList<float> arc, IReadOnlyList<float> alpha, int a, int b)
        {
            float da = alpha[a] - Visible, db = alpha[b] - Visible;
            float t = Mathf.Abs(da - db) > 1e-6f ? Mathf.Clamp01(da / (da - db)) : 0.5f;
            return Mathf.Lerp(arc[a], arc[b], t);
        }

        private struct Item
        {
            public bool Gap;
            public Kind Kind;
            public float Length, Width, Alpha, Elong, Offset;
        }

        private static void Decorate(Plan plan, IReadOnlyList<Vector2> points, IReadOnlyList<float> arc, IReadOnlyList<float> alpha, float sEnd, int dir, float runLength, float room, float chance, Options o)
        {
            plan.Considered++;
            float u = plan.Unit;
            Vector2 at = PointAt(points, arc, sEnd);
            uint seed = o.Seed ^ (dir > 0 ? 0x5bd1e995u : 0x1b873593u);
            int qx = Mathf.RoundToInt(at.x * 4f), qz = Mathf.RoundToInt(at.y * 4f);
            int draw = 0;
            float Next() => MapLabRandom.Hash01(qx, qz, seed + (uint)(draw++) * 0x9E3779B9u);
            float v = Mathf.Clamp01(o.Variety);
            float J(float a, float b) => Mathf.Lerp((a + b) * 0.5f, Mathf.Lerp(a, b, Next()), v);

            if (Next() >= chance)
                return;
            // Совсем короткий след — без хвоста (обычное потерянное окончание).
            if (runLength < u * 6f)
                return;

            // Вариант: полная цепочка, штрих и 1–2 точки, линия → точка,
            // два неравных штриха → точка, один штрих.
            float[] weights = { 0.38f + (1f - v) * 0.5f, 0.24f, 0.16f, 0.12f, 0.10f };
            float sum = 0f;
            foreach (float w in weights)
                sum += w;
            float pick = Next() * sum;
            int variant = 0;
            while (variant < weights.Length - 1 && pick > weights[variant])
            {
                pick -= weights[variant];
                variant++;
            }
            // Короткий след — только сокращённые варианты.
            if (runLength < u * 14f && (variant == 0 || variant == 3))
                variant = 2;

            List<Item> items = new List<Item>();
            void Gap(float length) => items.Add(new Item { Gap = true, Length = length });
            void Dash(float length, float width, float a) => items.Add(new Item { Kind = Kind.Dash, Length = length, Width = width, Alpha = a });
            float dotWidth = J(0.95f, 1.3f), dotAlpha = J(0.74f, 0.9f);
            void Dots(int count, float firstGap)
            {
                float gap = firstGap;
                for (int k = 0; k < count; k++)
                {
                    float elong = J(0.9f, 2.2f);
                    // Длина следа точки по дуге ≈ её толщина × вытянутость (точная — в шейдере).
                    Gap(gap);
                    items.Add(new Item { Kind = Kind.Dot, Length = u * 0.9f * elong * dotWidth, Width = dotWidth, Alpha = dotAlpha, Elong = elong, Offset = J(-0.45f, 0.45f) });
                    gap *= J(0.85f, 1.7f);
                    dotWidth *= J(0.6f, 0.9f);
                    dotAlpha *= J(0.8f, 0.97f);
                }
            }
            float dash = u * J(2.8f, 5.2f), dashWidth = J(0.68f, 0.9f), dashAlpha = J(0.78f, 0.95f);
            switch (variant)
            {
                case 0:
                    Gap(u * J(1.6f, 2.6f));
                    Dash(dash, dashWidth, dashAlpha);
                    Dots(Next() < 0.55f ? 2 : 3, u * J(1.2f, 2.1f));
                    break;
                case 1:
                    Gap(u * J(1.6f, 2.6f));
                    Dash(dash * J(0.75f, 1f), dashWidth, dashAlpha);
                    Dots(Next() < 0.5f ? 1 : 2, u * J(1.3f, 2.2f));
                    break;
                case 2:
                    dotWidth *= 1.1f;
                    Dots(1, u * J(1.9f, 3f));
                    break;
                case 3:
                    Gap(u * J(1.4f, 2.2f));
                    Dash(dash * J(0.5f, 0.7f), dashWidth, dashAlpha);
                    Gap(u * J(0.9f, 1.5f));
                    Dash(dash * J(0.3f, 0.45f), dashWidth * 0.9f, dashAlpha * 0.9f);
                    Dots(1, u * J(1.3f, 2f));
                    break;
                default:
                    Gap(u * J(1.6f, 2.6f));
                    Dash(dash * J(0.6f, 0.9f), dashWidth, dashAlpha);
                    break;
            }

            // Место: часть — за счёт основной линии, остальное — в просвет.
            float maxInside = runLength * 0.32f;
            float Total()
            {
                float t = 0f;
                foreach (Item it in items)
                    t += it.Length;
                return t;
            }
            float total = Total();
            // Мало места — сначала меньше элементов (последний с его просветом).
            while (total > maxInside + room && items.Count > 2)
            {
                items.RemoveAt(items.Count - 1);
                if (items.Count > 0 && items[items.Count - 1].Gap)
                    items.RemoveAt(items.Count - 1);
                total = Total();
            }
            if (items.Count == 0 || items[items.Count - 1].Gap)
                return;
            // Потом короче.
            if (total > maxInside + room)
            {
                float f = (maxInside + room) / total;
                if (f < 0.55f)
                    return;
                for (int k = 0; k < items.Count; k++)
                {
                    Item it = items[k];
                    it.Length *= f;
                    items[k] = it;
                }
                total *= f;
            }
            if (total < u * 2.5f)
                return;
            float inside = Mathf.Min(maxInside, Mathf.Max(total * 0.5f, total - room));
            float sCut = sEnd - inside * dir;
            float aCut = Mathf.Max(Visible, AlphaAt(arc, alpha, sCut));

            float pos = 0f;
            foreach (Item it in items)
            {
                float a = sCut + pos * dir, b = sCut + (pos + it.Length) * dir;
                pos += it.Length;
                if (it.Gap)
                    continue;
                plan.Elements.Add(new Element
                {
                    Kind = it.Kind,
                    S0 = Mathf.Min(a, b),
                    S1 = Mathf.Max(a, b),
                    Width = it.Width,
                    Alpha = Mathf.Clamp01(aCut * it.Alpha),
                    Elong = it.Elong,
                    Offset = it.Offset * dir,
                });
            }
            float zone = Mathf.Min(total + u * 0.5f, inside + room);
            plan.Cuts.Add(new Cut { S = sCut, Dir = dir, Alpha = aCut, ZoneEnd = sCut + zone * dir });
            plan.Decorated++;
        }

        public static Vector2 PointAt(IReadOnlyList<Vector2> points, IReadOnlyList<float> arc, float s)
        {
            int j = Segment(arc, s);
            float len = arc[j + 1] - arc[j];
            float t = len > 1e-6f ? Mathf.Clamp01((s - arc[j]) / len) : 0f;
            return Vector2.Lerp(points[j], points[j + 1], t);
        }

        public static float AlphaAt(IReadOnlyList<float> arc, IReadOnlyList<float> alpha, float s)
        {
            int j = Segment(arc, s);
            float len = arc[j + 1] - arc[j];
            float t = len > 1e-6f ? Mathf.Clamp01((s - arc[j]) / len) : 0f;
            return Mathf.Lerp(alpha[j], alpha[j + 1], t);
        }

        // Индекс отрезка [j, j+1], содержащего s (двоичный поиск).
        public static int Segment(IReadOnlyList<float> arc, float s)
        {
            int lo = 0, hi = arc.Count - 2;
            if (hi <= 0)
                return 0;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) >> 1;
                if (arc[mid] <= s)
                    lo = mid;
                else
                    hi = mid - 1;
            }
            return lo;
        }
    }
}

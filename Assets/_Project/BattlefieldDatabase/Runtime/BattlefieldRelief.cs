using UnityEngine;
using UnityEngine.UIElements;

namespace KingdomSurvival.BattlefieldDatabase
{
    // Рельеф под полем боя: подъём земли в точке кадра поля. Точка на холме
    // видна выше — сетка, отметки, фигуры и выбор мышью сдвигаются так же,
    // как нарисованная земля. Логика поля (клетки, соседство, дальность,
    // стоимость хода) от рельефа не зависит.
    public interface IBattlefieldRelief
    {
        // frameShare — доли кадра поля (0..1, Y вниз); результат — подъём
        // вверх в долях высоты кадра.
        float LiftShare(Vector2 frameShare);
    }

    public static class BattlefieldReliefMath
    {
        // Плоская точка кадра (пиксели элемента) → видимая точка на земле.
        public static Vector2 Raise(IBattlefieldRelief relief, Rect frame, Vector2 flat)
        {
            if (relief == null || frame.width < 1f || frame.height < 1f)
                return flat;
            Vector2 share = new Vector2((flat.x - frame.x) / frame.width, (flat.y - frame.y) / frame.height);
            return new Vector2(flat.x, flat.y - relief.LiftShare(share) * frame.height);
        }

        // Видимая точка → плоская (обратное к Raise; уклон земли на экране
        // меньше 1, итерации сходятся). Для склона, видимого дважды, — точка
        // ближайшего к начальному приближению участка.
        public static Vector2 Lower(IBattlefieldRelief relief, Rect frame, Vector2 shown)
        {
            if (relief == null || frame.width < 1f || frame.height < 1f)
                return shown;
            Vector2 flat = shown;
            for (int i = 0; i < 6; i++)
            {
                Vector2 share = new Vector2((flat.x - frame.x) / frame.width, (flat.y - frame.y) / frame.height);
                flat = new Vector2(shown.x, shown.y + relief.LiftShare(share) * frame.height);
            }
            return flat;
        }

        // Вершина гекса (острые вершины вверх и вниз) с плоским центром.
        public static Vector2 HexCorner(Vector2 flatCenter, float radius, float verticalScale, int index)
        {
            float angle = Mathf.Deg2Rad * (60f * index - 30f);
            return flatCenter + new Vector2(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius * verticalScale);
        }

        // Контур гекса, лежащего на земле: каждая сторона делится на отрезки,
        // каждая точка поднимается по рельефу — сетка ложится на склон.
        public static void DrapedHexPath(Painter2D painter, IBattlefieldRelief relief, Rect frame, Vector2 flatCenter, float radius, float verticalScale)
        {
            const int steps = 3;
            painter.BeginPath();
            for (int i = 0; i < 6; i++)
            {
                Vector2 a = HexCorner(flatCenter, radius, verticalScale, i);
                Vector2 b = HexCorner(flatCenter, radius, verticalScale, (i + 1) % 6);
                for (int s = 0; s < steps; s++)
                {
                    Vector2 point = Raise(relief, frame, Vector2.Lerp(a, b, s / (float)steps));
                    if (i == 0 && s == 0)
                        painter.MoveTo(point);
                    else
                        painter.LineTo(point);
                }
            }
            painter.ClosePath();
        }

        // Видимая точка внутри гекса, лежащего на земле (по шести поднятым вершинам и центру).
        public static bool DrapedHexContains(IBattlefieldRelief relief, Rect frame, Vector2 flatCenter, float radius, float verticalScale, Vector2 point)
        {
            Vector2 center = Raise(relief, frame, flatCenter);
            for (int i = 0; i < 6; i++)
            {
                Vector2 a = Raise(relief, frame, HexCorner(flatCenter, radius, verticalScale, i));
                Vector2 b = Raise(relief, frame, HexCorner(flatCenter, radius, verticalScale, (i + 1) % 6));
                if (InTriangle(point, center, a, b))
                    return true;
            }
            return false;
        }

        private static bool InTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = Cross(p, a, b), d2 = Cross(p, b, c), d3 = Cross(p, c, a);
            bool negative = d1 < 0f || d2 < 0f || d3 < 0f;
            bool positive = d1 > 0f || d2 > 0f || d3 > 0f;
            return !(negative && positive);
        }

        private static float Cross(Vector2 p, Vector2 a, Vector2 b) => (a.x - p.x) * (b.y - p.y) - (a.y - p.y) * (b.x - p.x);
    }
}

using System.Collections.Generic;
using UnityEngine;

namespace KingdomSurvival.AnimationDatabase
{
    // Выбор направления по экранному вектору. Шесть эталонных векторов —
    // экранные смещения к соседним гексам в текущей проекции поля (в порядке
    // HexFacing), поэтому для соседа выбор точный, а для дальней цели —
    // ближайший сектор в той же системе координат.
    public static class HexFacingMath
    {
        public static HexFacing Nearest(Vector2 screenVector, IReadOnlyList<Vector2> neighborVectors, HexFacing fallback)
        {
            if (neighborVectors == null || neighborVectors.Count != CreatureAnimationLabels.DirectionCount ||
                screenVector.sqrMagnitude < 0.0001f)
            {
                return fallback;
            }

            Vector2 direction = screenVector.normalized;
            HexFacing best = fallback;
            float bestDot = float.MinValue;
            for (int i = 0; i < neighborVectors.Count; i++)
            {
                Vector2 reference = neighborVectors[i];
                if (reference.sqrMagnitude < 0.0001f)
                    continue;
                float dot = Vector2.Dot(direction, reference.normalized);
                if (dot > bestDot + 0.0001f)
                {
                    bestDot = dot;
                    best = (HexFacing)i;
                }
            }
            return best;
        }
    }
}

using System;
using System.Collections.Generic;

namespace KingdomSurvival.BattleSandbox
{
    // ПР-12З: показ перемещения по маршруту с постоянной скоростью. Боец не
    // разгоняется и не тормозит на каждом гексе — иначе ходьба идёт рывками.
    // Трудная местность замедляет свой отрезок. Только представление: правила
    // движения и стоимость пути не меняются.
    public static class SandboxMovementTimeline
    {
        public const float DifficultTerrainSlowdown = 1.35f;

        // Длительность каждого отрезка маршрута при скорости hexesPerSecond.
        public static float[] BuildSegmentDurations(SandboxBattle battle, IReadOnlyList<HexCoord> path, float hexesPerSecond)
        {
            if (path == null || path.Count < 2)
                return Array.Empty<float>();
            float baseDuration = 1f / Math.Max(0.01f, hexesPerSecond);
            float[] durations = new float[path.Count - 1];
            for (int i = 0; i < durations.Length; i++)
            {
                bool difficult = battle != null && battle.GetTerrain(path[i + 1]) == SandboxTerrain.Difficult;
                durations[i] = baseDuration * (difficult ? DifficultTerrainSlowdown : 1f);
            }
            return durations;
        }

        public static float Total(float[] durations)
        {
            float total = 0f;
            if (durations != null)
            {
                foreach (float duration in durations)
                    total += duration;
            }
            return total;
        }

        // Отрезок и доля пути по нему в момент elapsed. False — путь пройден.
        public static bool Locate(float[] durations, float elapsed, out int segment, out float progress)
        {
            segment = 0;
            progress = 0f;
            if (durations == null || durations.Length == 0)
                return false;
            float time = Math.Max(0f, elapsed);
            for (int i = 0; i < durations.Length; i++)
            {
                if (time < durations[i])
                {
                    segment = i;
                    progress = durations[i] > 0f ? time / durations[i] : 1f;
                    return true;
                }
                time -= durations[i];
            }
            segment = durations.Length - 1;
            progress = 1f;
            return false;
        }
    }
}

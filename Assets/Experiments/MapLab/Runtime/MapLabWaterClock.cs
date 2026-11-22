using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace KingdomSurvival.MapLab
{
    // Часы течения воды MapLab (_MapLabWaterTime): штрихи рек и озёр плывут и
    // пересобираются по ним. Ставятся перед каждым кадром любой камеры — в игре,
    // в окнах генераторов вне Play Mode и при ручном Camera.Render (арена, живая
    // карта), где встроенное _Time вне Play Mode не идёт. Реальное время, а не
    // игровое: пауза и ускорение мира течение не трогают. Круг — полчаса, чтобы
    // не терять точность float в шейдере (раз в полчаса рисунок штрихов сменится).
    public static class MapLabWaterClock
    {
        public const float Period = 1800f;
        private static readonly int TimeId = Shader.PropertyToID("_MapLabWaterTime");

        public static float Now => (float)(System.Diagnostics.Stopwatch.GetTimestamp() / (double)System.Diagnostics.Stopwatch.Frequency % Period);

        public static void Push() => Shader.SetGlobalFloat(TimeId, Now);

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
#endif
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Hook()
        {
            RenderPipelineManager.beginContextRendering -= BeforeFrame;
            RenderPipelineManager.beginContextRendering += BeforeFrame;
        }

        private static void BeforeFrame(ScriptableRenderContext context, List<Camera> cameras) => Push();
    }
}

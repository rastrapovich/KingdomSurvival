using UnityEditor;

namespace KingdomSurvival.LocationRendering.Editor
{
    // Проверка состояний героя на предпросмотре окон: включить, подержать, вернуть.
    internal static class FrameStateTest
    {
        public static void Run(LocationWorldRenderer renderer, string name)
        {
            if (renderer == null) return;
            double hold;
            switch (name)
            {
                case "health": renderer.SetHeroHealth(.1f); hold = 3; break;
                case "stun": renderer.SetStun(1, .2f); hold = 2; break;
                case "fade": renderer.FadeTo(1, .8f); hold = 1.8; break;
                case "letterbox": renderer.SetLetterbox(.12f, .6f); hold = 2.5; break;
                default: return;
            }
            double back = EditorApplication.timeSinceStartup + hold;
            void Wait()
            {
                if (EditorApplication.timeSinceStartup < back) return;
                EditorApplication.update -= Wait;
                switch (name)
                {
                    case "health": renderer.SetHeroHealth(1); break;
                    case "stun": renderer.SetStun(0, .4f); break;
                    case "fade": renderer.FadeTo(0, .8f); break;
                    case "letterbox": renderer.SetLetterbox(0, .6f); break;
                }
            }
            EditorApplication.update += Wait;
        }
    }
}

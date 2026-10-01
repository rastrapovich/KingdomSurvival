using UnityEngine;

namespace KingdomSurvival.BattleSandbox
{
    // ПР-12З: общая скорость показа боя — от медленной до быстрой. Ускоряет
    // или замедляет ходьбу, удары, анимации и паузы перед ходом противника.
    // На правила боя не влияет. Хранится у игрока между запусками.
    public static class BattlePresentationSpeed
    {
        public static readonly float[] Steps = { 0.5f, 0.75f, 1f, 1.5f, 2f, 3f };
        public const int DefaultIndex = 2;
        private const string PrefsKey = "KingdomSurvival.BattlePresentationSpeed";

        private static int? index;

        public static int Index
        {
            get
            {
                if (!index.HasValue)
                    index = Mathf.Clamp(PlayerPrefs.GetInt(PrefsKey, DefaultIndex), 0, Steps.Length - 1);
                return index.Value;
            }
            set
            {
                index = Mathf.Clamp(value, 0, Steps.Length - 1);
                PlayerPrefs.SetInt(PrefsKey, index.Value);
                PlayerPrefs.Save();
            }
        }

        public static float Multiplier => Steps[Index];

        public static string Label(int stepIndex)
        {
            int safe = Mathf.Clamp(stepIndex, 0, Steps.Length - 1);
            string value = Steps[safe].ToString("0.##") + "×";
            if (safe == 0)
                return "медленно · " + value;
            if (safe == Steps.Length - 1)
                return "быстро · " + value;
            return value;
        }
    }
}

using UnityEditor;
using UnityEngine;

namespace KingdomSurvival.MapLab.Editor
{
    // Поле «Уровни», как в Photoshop: вход — чёрная, гамма, белая (ползунок
    // диапазона и числа 0–255), выход — чёрная и белая. Возвращает правленую
    // копию; изменения ловит EditorGUI.BeginChangeCheck снаружи.
    public static class MapLabLevelsGUI
    {
        public static MapLabLevels Draw(MapLabLevels source, string title = "Уровни")
        {
            MapLabLevels l = source != null ? source.Clone() : new MapLabLevels();
            EditorGUILayout.LabelField(new GUIContent(title, "Как «Уровни» в Photoshop: вход 0 / 1,00 / 255 и выход 0 / 255 — без изменений."), EditorStyles.miniBoldLabel);

            float inB = l.InputBlack, inW = l.InputWhite;
            EditorGUILayout.MinMaxSlider(new GUIContent("Вход", "Чёрная и белая точки входа: всё темнее чёрной — чёрное, светлее белой — белое."), ref inB, ref inW, 0f, 255f);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PrefixLabel(" ");
                inB = EditorGUILayout.IntField(Mathf.RoundToInt(inB), GUILayout.MinWidth(36));
                l.Gamma = EditorGUILayout.FloatField(new GUIContent("", "Гамма средних тонов: 1,00 — без изменений, больше — светлее."), l.Gamma, GUILayout.MinWidth(40));
                inW = EditorGUILayout.IntField(Mathf.RoundToInt(inW), GUILayout.MinWidth(36));
            }
            l.Gamma = EditorGUILayout.Slider(new GUIContent("Средние тона (гамма)", "1 — без изменений; больше — светлее, меньше — темнее."), l.Gamma, 0.1f, 9.99f);
            inB = Mathf.Clamp(inB, 0f, 253f);
            inW = Mathf.Clamp(inW, inB + 2f, 255f);
            l.InputBlack = inB;
            l.InputWhite = inW;

            float outB = l.OutputBlack, outW = l.OutputWhite;
            EditorGUILayout.MinMaxSlider(new GUIContent("Выход", "Самый тёмный и самый светлый тон результата."), ref outB, ref outW, 0f, 255f);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PrefixLabel(" ");
                outB = EditorGUILayout.IntField(Mathf.RoundToInt(outB), GUILayout.MinWidth(36));
                outW = EditorGUILayout.IntField(Mathf.RoundToInt(outW), GUILayout.MinWidth(36));
            }
            l.OutputBlack = Mathf.Clamp(outB, 0f, 255f);
            l.OutputWhite = Mathf.Clamp(outW, 0f, 255f);
            l.Gamma = Mathf.Clamp(l.Gamma, 0.1f, 9.99f);
            return l;
        }
    }
}

// Генератор арен, обработка кадра: слой стоящих объектов арены рисуется
// дважды — без обработки (прозрачный фон, альфа) и с обработкой (URP
// выводит альфу 1). Склейка: цвет — из обработанного кадра, прозрачность —
// из исходного; деревья и камни получают ту же обработку, что земля под ними.
Shader "Hidden/KingdomSurvival/MapLabArenaAlphaMerge"
{
    Properties
    {
        _MainTex ("Alpha source", 2D) = "black" {}
        _GradedTex ("Graded colour", 2D) = "black" {}
    }
    SubShader
    {
        ZTest Always Cull Off ZWrite Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            sampler2D _GradedTex;
            fixed4 frag (v2f_img i) : SV_Target
            {
                fixed4 source = tex2D(_MainTex, i.uv);
                fixed3 graded = tex2D(_GradedTex, i.uv).rgb;
                // Края силуэта: исходный цвет уже смешан с прозрачным чёрным,
                // обработанный — тоже (фон кадра чёрный).
                return fixed4(graded, source.a);
            }
            ENDCG
        }
    }
}

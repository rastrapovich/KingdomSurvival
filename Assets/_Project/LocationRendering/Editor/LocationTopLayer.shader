// Окно «База локаций»: верхний слой предпросмотра — предметы и люди над
// гексами боя. Цвет — из полного кадра места (с обработкой кадра и светом
// на земле вокруг), прозрачность — из кадра одних предметов и людей на
// прозрачном фоне.
Shader "Hidden/KingdomSurvival/LocationTopLayer"
{
    Properties
    {
        _MainTex ("Full frame", 2D) = "black" {}
        _MaskTex ("Upper only", 2D) = "black" {}
    }

    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            sampler2D _MaskTex;

            float4 frag(v2f_img i) : SV_Target
            {
                return float4(tex2D(_MainTex, i.uv).rgb, saturate(tex2D(_MaskTex, i.uv).a));
            }
            ENDCG
        }
    }
}

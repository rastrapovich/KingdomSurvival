// Арена: слой «впереди бойца» одной строки земли из общего слоя объектов (цвет + ключ глубины).
// Вместо отдельной пары проходов камеры на каждую строку — дешёвая выборка: остаются пиксели,
// опора рисунка которых ближе к зрителю, чем ноги бойца (ключ — глубина опоры, 16 бит в R и G).
Shader "Hidden/KingdomSurvival/MapLabArenaFrontRow"
{
    Properties
    {
        _MainTex ("Colour + alpha", 2D) = "black" {}
        _KeyTex ("Key", 2D) = "black" {}
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
            sampler2D _KeyTex;
            float _ClipKey;
            fixed4 frag (v2f_img i) : SV_Target
            {
                float4 k = tex2D(_KeyTex, i.uv);
                float4 c = tex2D(_MainTex, i.uv);
                // Ключ лежит в sRGB-цели: выборка даёт линейное значение — обратно в байты.
                float2 g = k.rg <= 0.0031308 ? k.rg * 12.92 : 1.055 * pow(k.rg, 1.0 / 2.4) - 0.055;
                float key = (floor(g.x * 255.0 + 0.5) * 256.0 + floor(g.y * 255.0 + 0.5)) / 65535.0;
                float a = key < _ClipKey ? k.a : 0.0;
                return fixed4(c.rgb, a);
            }
            ENDCG
        }
    }
}

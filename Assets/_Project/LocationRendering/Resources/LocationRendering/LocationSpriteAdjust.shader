// ПР-12Р: спрайт под 2D-светом с цветом экземпляра — сдвиг тона,
// насыщенность и яркость (в пространстве HSV, до освещения). Копия прохода
// Sprite-Lit-Default URP 17.6 (свет, нормали, маска) с этой правкой.
// Экземпляр рисунка (SpriteRenderer) — _KsHsv из блока свойств.
// Ковёр раскидки (_KS_PARTICLE, сетка из прямоугольников-«частиц»): своё у
// каждого прямоугольника в UV — TEXCOORD0.zw = тон (обороты), насыщенность;
// TEXCOORD1.xyz = яркость, отражение ±1, поворот (радианы против часовой);
// кадр — _MainTex и прямоугольник кадра _KsUvRect у группы. Спрайтовые
// свойства (цвет, отражение рендерера) у сетки не задаются.
Shader "Kingdom Survival/Location Sprite Adjust"
{
    Properties
    {
        _MainTex("Diffuse", 2D) = "white" {}
        _MaskTex("Mask", 2D) = "white" {}
        _NormalMap("Normal Map", 2D) = "bump" {}
        _KsHsv("Hue (turns), Saturation, Brightness", Vector) = (0, 1, 1, 0)
        _KsUvRect("Particle UV rect (offset, scale)", Vector) = (0, 0, 1, 1)
        [MaterialToggle] _ZWrite("ZWrite", Float) = 0
        [HideInInspector] _Color("Tint", Color) = (1,1,1,1)
        [HideInInspector] _RendererColor("RendererColor", Color) = (1,1,1,1)
        [HideInInspector] _AlphaTex("External Alpha", 2D) = "white" {}
        [HideInInspector] _EnableExternalAlpha("Enable External Alpha", Float) = 0
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"
    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

    // NOTE: одинаковая раскладка во всех проходах (SRP batcher).
    CBUFFER_START(UnityPerMaterial)
        half4 _Color;
        float4 _KsHsv;
        float4 _KsUvRect;
    CBUFFER_END

    half3 KsAdjust(half3 rgb, float3 hsv)
    {
        if (abs(hsv.x) > 1e-4 || abs(hsv.y - 1) > 1e-4)
        {
            float3 c = RgbToHsv(rgb);
            c.x = frac(c.x + hsv.x);
            c.y = saturate(c.y * hsv.y);
            rgb = HsvToRgb(c);
        }
        return rgb * hsv.z;
    }

    float2 KsUv(float2 uv, float flip)
    {
    #if defined(_KS_PARTICLE)
        if (flip < 0) uv.x = 1 - uv.x;
        return uv * _KsUvRect.zw + _KsUvRect.xy;
    #else
        return uv;
    #endif
    }
    ENDHLSL

    SubShader
    {
        Tags {"Queue" = "Transparent" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" }

        Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
        Cull Off
        ZWrite [_ZWrite]

        Pass
        {
            Tags { "LightMode" = "Universal2D" }

            HLSLPROGRAM
            #pragma vertex LitVertex
            #pragma fragment LitFragment

            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/ShapeLightShared.hlsl"

            #pragma multi_compile_instancing
            #pragma multi_compile _ DEBUG_DISPLAY
            #pragma multi_compile_local _ _KS_PARTICLE

            struct Attributes
            {
                float3 positionOS   : POSITION;
                float4 uv           : TEXCOORD0;
            #if defined(_KS_PARTICLE)
                float3 custom       : TEXCOORD1;
            #endif
                float3 normal       : NORMAL;
                half4 color         : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                COMMON_2D_LIT_OUTPUTS
                half4 color         : COLOR;
                float4 adjust       : TEXCOORD4;
            };

            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/CombinedShapeLightShared.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            UNITY_TEXTURE_STREAMING_DEBUG_VARS_FOR_TEX(_MainTex);
            TEXTURE2D(_MaskTex);
            SAMPLER(sampler_MaskTex);
            TEXTURE2D(_NormalMap);
            SAMPLER(sampler_NormalMap);

            Varyings LitVertex(Attributes input)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
            #if defined(_KS_PARTICLE)
                float3 position = input.positionOS;
                o.color = input.color * _Color;
                o.adjust = float4(_KsHsv.x + input.uv.z, _KsHsv.y * input.uv.w, _KsHsv.z * input.custom.x, input.custom.y);
            #else
                SetUpSpriteInstanceProperties();
                float3 position = UnityFlipSprite(input.positionOS, unity_SpriteProps.xy);
                o.color = input.color * _Color * unity_SpriteColor;
                o.adjust = float4(_KsHsv.xyz, 1);
            #endif
                o.positionCS = TransformObjectToHClip(position);
            #if defined(DEBUG_DISPLAY)
                o.positionWS = TransformObjectToWorld(position);
                o.normalWS = TransformObjectToWorldDir(input.normal);
            #endif
                o.uv = KsUv(input.uv.xy, o.adjust.w);
                o.lightingUV = half2(ComputeScreenPos(o.positionCS / o.positionCS.w).xy);
                return o;
            }

            half4 LitFragment(Varyings input) : SV_Target
            {
                half4 texel = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                texel.rgb = KsAdjust(texel.rgb, input.adjust.xyz);
                const half4 main = input.color * texel;
                const half4 mask = SAMPLE_TEXTURE2D(_MaskTex, sampler_MaskTex, input.uv);
                half3 normalTS = UnpackNormal(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, input.uv));

                SurfaceData2D surfaceData;
                InputData2D inputData;
                InitializeSurfaceData(main.rgb, main.a, mask, normalTS, surfaceData);
                InitializeInputData(input.uv, input.lightingUV, inputData);
            #if defined(DEBUG_DISPLAY)
                SETUP_DEBUG_TEXTURE_DATA_2D_NO_TS(inputData, input.positionWS, input.positionCS, _MainTex);
                surfaceData.normalWS = input.normalWS;
            #endif
                return CombinedShapeLightShared(surfaceData, inputData);
            }
            ENDHLSL
        }

        Pass
        {
            Tags { "LightMode" = "NormalsRendering"}

            HLSLPROGRAM
            #pragma vertex NormalsVertex
            #pragma fragment NormalsFragment

            #pragma multi_compile_instancing
            #pragma multi_compile_local _ _KS_PARTICLE

            struct Attributes
            {
                float3 positionOS   : POSITION;
                float4 uv           : TEXCOORD0;
            #if defined(_KS_PARTICLE)
                float3 custom       : TEXCOORD1;
            #endif
                float3 normal       : NORMAL;
                float4 tangent      : TANGENT;
                half4 color         : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                COMMON_2D_NORMALS_OUTPUTS
                half4 color         : COLOR;
                float flip          : TEXCOORD4;
            };

            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/NormalsRenderingShared.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            TEXTURE2D(_NormalMap);
            SAMPLER(sampler_NormalMap);

            Varyings NormalsVertex(Attributes input)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
            #if defined(_KS_PARTICLE)
                // Плоскость частицы смотрит в камеру; базис — по повороту частицы,
                // как у спрайта (нормаль от экрана, касательная вправо).
                float3 position = input.positionOS;
                o.color = input.color * _Color;
                o.flip = input.custom.y;
                float angle = input.custom.z;
                o.normalWS = half3(0, 0, -1);
                o.tangentWS = half3(cos(angle), sin(angle), 0);
                o.bitangentWS = half3(-sin(angle), cos(angle), 0);
            #else
                SetUpSpriteInstanceProperties();
                float3 position = UnityFlipSprite(input.positionOS, unity_SpriteProps.xy);
                o.color = input.color * _Color * unity_SpriteColor;
                o.flip = 1;
                o.normalWS = TransformObjectToWorldDir(input.normal);
                o.tangentWS = TransformObjectToWorldDir(input.tangent.xyz);
                o.bitangentWS = cross(o.normalWS, o.tangentWS) * input.tangent.w;
            #endif
                o.positionCS = TransformObjectToHClip(position);
                o.uv = KsUv(input.uv.xy, o.flip);
                return o;
            }

            half4 NormalsFragment(Varyings input) : SV_Target
            {
                const half4 mainTex = input.color * SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                half3 normalTS = UnpackNormal(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, input.uv));
            #if defined(_KS_PARTICLE)
                // Отражённый рисунок — отражённая нормаль по горизонтали.
                normalTS.x *= input.flip < 0 ? -1 : 1;
                half3 normalWS = TransformTangentToWorld(normalTS, half3x3(input.tangentWS.xyz, input.bitangentWS.xyz, input.normalWS.xyz));
                return half4(0.5 * (normalWS + 1), mainTex.a);
            #else
                SetUpSpriteInstanceProperties();
                return NormalsRenderingShared(mainTex, normalTS, input.tangentWS.xyz, input.bitangentWS.xyz, input.normalWS.xyz);
            #endif
            }
            ENDHLSL
        }

        Pass
        {
            Tags { "LightMode" = "UniversalForward" "Queue"="Transparent" "RenderType"="Transparent"}

            HLSLPROGRAM
            #pragma vertex UnlitVertex
            #pragma fragment UnlitFragment

            #pragma multi_compile_instancing
            #pragma multi_compile_local _ _KS_PARTICLE

            struct Attributes
            {
                float3 positionOS   : POSITION;
                float4 uv           : TEXCOORD0;
            #if defined(_KS_PARTICLE)
                float3 custom       : TEXCOORD1;
            #endif
                half4 color         : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS   : SV_POSITION;
                float2 uv           : TEXCOORD0;
                half4 color         : COLOR;
                float4 adjust       : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            Varyings UnlitVertex(Attributes input)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
            #if defined(_KS_PARTICLE)
                float3 position = input.positionOS;
                o.color = input.color * _Color;
                o.adjust = float4(_KsHsv.x + input.uv.z, _KsHsv.y * input.uv.w, _KsHsv.z * input.custom.x, input.custom.y);
            #else
                SetUpSpriteInstanceProperties();
                float3 position = UnityFlipSprite(input.positionOS, unity_SpriteProps.xy);
                o.color = input.color * _Color * unity_SpriteColor;
                o.adjust = float4(_KsHsv.xyz, 1);
            #endif
                o.positionCS = TransformObjectToHClip(position);
                o.uv = KsUv(input.uv.xy, o.adjust.w);
                return o;
            }

            half4 UnlitFragment(Varyings input) : SV_Target
            {
                half4 texel = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                texel.rgb = KsAdjust(texel.rgb, input.adjust.xyz);
                return input.color * texel;
            }
            ENDHLSL
        }
    }
}

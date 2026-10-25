// Видео рук (passthrough по маске рук). Гарнитура кладёт картинку с камер ПОД кадр игры
// и смешивает по альфе кадра: где альфа 0 — видно камеру, где 1 — игру. Этот шейдер
// рисует сетку руки альфой ~0, а по краю — мягким светящимся ободком (френель): там
// альфа растёт, и реальная рука плавно переходит в игру, без рваного края.
// Цвет пишется premultiplied (rgb × α) — так его ждёт композитор гарнитуры.
// Очередь 4010 — после AlphaOne (4000), который до этого ставит альфу 1 на весь кадр.
// Лежит в Resources, чтобы попасть в билд (Shader.Find). Вешает ВидеоРук.
Shader "Custom/HandPassthrough"
{
    Properties
    {
        _RimColor ("Rim Color", Color) = (0.8, 0.93, 1, 1)
        _RimPower ("Rim Power", Range(0.5, 8)) = 3
        _RimAlpha ("Rim Alpha", Range(0, 1)) = 0.55
        // Свойства, которые красит подсветка рук XRI (affordance) у родного шейдера руки:
        // без них она сыпала ошибками в консоль. _EdgeColor окрашивает светящийся край.
        _BaseColor ("Affordance Base", Color) = (1, 1, 1, 0)
        _MainColor ("Affordance Main", Color) = (1, 1, 1, 0)
        _EdgeColor ("Affordance Edge", Color) = (1, 1, 1, 0)
        _ThumbColor ("Affordance Thumb", Color) = (1, 1, 1, 0)
        _FingerColor_1 ("Affordance Finger 1", Color) = (1, 1, 1, 0)
        _FingerColor_2 ("Affordance Finger 2", Color) = (1, 1, 1, 0)
        _FingerColor_3 ("Affordance Finger 3", Color) = (1, 1, 1, 0)
        _FingerColor_4 ("Affordance Finger 4", Color) = (1, 1, 1, 0)
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Overlay+10" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _RimColor;
            float _RimPower;
            float _RimAlpha;
            float4 _BaseColor, _MainColor, _EdgeColor, _ThumbColor;
            float4 _FingerColor_1, _FingerColor_2, _FingerColor_3, _FingerColor_4;
        CBUFFER_END

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float3 positionWS : TEXCOORD0;
            float3 normalWS : TEXCOORD1;
            UNITY_VERTEX_OUTPUT_STEREO
        };

        Varyings vert(Attributes i)
        {
            Varyings o;
            UNITY_SETUP_INSTANCE_ID(i);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
            o.positionWS = TransformObjectToWorld(i.positionOS.xyz);
            o.positionCS = TransformWorldToHClip(o.positionWS);
            o.normalWS = TransformObjectToWorldNormal(i.normalOS);
            return o;
        }
        ENDHLSL

        // 1) Глубина: дальние части руки не вырезают «дыры» поверх ближних.
        Pass
        {
            Name "HandPassthroughDepth"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            ZWrite On
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment fragDepth
            #pragma multi_compile_instancing
            half4 fragDepth(Varyings i) : SV_Target { return 0; }
            ENDHLSL
        }

        // 2) Окно в камеру: пишем и цвет, и альфу (без смешивания — рука закрывает то, что за ней).
        Pass
        {
            Name "HandPassthrough"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite Off
            ZTest LEqual
            Blend One Zero
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float3 n = normalize(i.normalWS);
                float3 v = normalize(GetWorldSpaceViewDir(i.positionWS));
                float rim = pow(1.0 - saturate(dot(n, v)), _RimPower);
                float a = saturate(rim * _RimAlpha);
                float3 edgeCol = lerp(_RimColor.rgb, _EdgeColor.rgb, saturate(_EdgeColor.a) * 0.7);
                return half4(edgeCol * a, a);
            }
            ENDHLSL
        }
    }
}

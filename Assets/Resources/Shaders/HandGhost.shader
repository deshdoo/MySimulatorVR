// «Призрачная» рука, как в меню Meta: почти прозрачная заливка и светящийся край
// (френель — чем круче поверхность к взгляду, тем ярче). Сначала проход только в
// буфер глубины: дальние пальцы и внутренние стенки сетки не просвечивают сквозь
// ближние — рука читается как одно целое, а не как стопка стёкол.
// Лежит в Resources, чтобы попасть в билд (ищется через Shader.Find). Вешает РукиПризраки.
Shader "Custom/HandGhost"
{
    Properties
    {
        _FillColor ("Fill Color", Color) = (0.75, 0.82, 0.9, 0.08)
        _RimColor ("Rim Color", Color) = (0.85, 0.95, 1, 0.9)
        _RimPower ("Rim Power", Range(0.5, 8)) = 2.5
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
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent+10" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _FillColor;
            float4 _RimColor;
            float _RimPower;
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

        // 1) Только глубина: ближайшая поверхность руки закрывает остальные её части.
        Pass
        {
            Name "HandDepth"
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

        // 2) Цвет: полупрозрачная заливка + светящийся край.
        Pass
        {
            Name "HandGhost"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite Off
            ZTest LEqual
            Blend SrcAlpha OneMinusSrcAlpha
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
                float3 edgeCol = lerp(_RimColor.rgb, _EdgeColor.rgb, saturate(_EdgeColor.a) * 0.7);
                float3 col = lerp(_FillColor.rgb, edgeCol, rim);
                float a = saturate(_FillColor.a + rim * _RimColor.a);
                return half4(col, a);
            }
            ENDHLSL
        }
    }
}

// Материал для макета повтора: однотонный «освещённый» цвет + обрезка по квадрату
// стола. Всё, что выходит за край макета (например, конец трубы, уходящий за
// пределы рельефа), не рисуется. Квадрат задаётся матрицей мир → макет и
// половиной стороны; свет — фиксированное направление (не зависит от света сцены).
// Лежит в Resources, чтобы попасть в билд (ищется через Shader.Find).
Shader "Custom/MacetClip"
{
    Properties
    {
        _BaseColor ("Color", Color) = (0.63, 0.66, 0.69, 1)
        _ClipHalf ("Clip Half Size", Float) = 0.5
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float _ClipHalf;
            CBUFFER_END
            float4x4 _ClipWorldToLocal;

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

            half4 frag(Varyings i) : SV_Target
            {
                float3 lp = mul(_ClipWorldToLocal, float4(i.positionWS, 1.0)).xyz;
                clip(_ClipHalf - max(abs(lp.x), abs(lp.z)));

                // Полу-ламбертово освещение от фиксированного направления + немного «неба».
                float3 n = normalize(i.normalWS);
                float3 l = normalize(float3(0.45, 0.8, 0.35));
                float halfLambert = dot(n, l) * 0.5 + 0.5;
                half3 c = _BaseColor.rgb * (0.35 + 0.8 * halfLambert * halfLambert);
                return half4(c, 1.0);
            }
            ENDHLSL
        }
    }
    FallBack Off
}

// Альфа кадра = 1 везде, цвет не меняется. Нужен для видео рук: гарнитура показывает
// камеру там, где альфа кадра меньше 1, а полупрозрачные окна, частицы и стёкла её
// уменьшают — без этого сквозь них проступало бы видео комнаты. Рисуется большим
// квадратом прямо перед камерой (ВидеоРук), очередь 4000 — после всей сцены, но до
// рук (Custom/HandPassthrough, 4010).
Shader "Custom/AlphaOne"
{
    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Overlay" }
        Pass
        {
            Name "AlphaOne"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite Off
            ZTest Always
            ColorMask A
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; UNITY_VERTEX_OUTPUT_STEREO };

            Varyings vert(Attributes i)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(i.positionOS.xyz);
                return o;
            }

            half4 frag(Varyings i) : SV_Target { return half4(0, 0, 0, 1); }
            ENDHLSL
        }
    }
}

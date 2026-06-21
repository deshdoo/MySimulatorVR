Shader "Custom/UnderwaterGodRays"
{
    Properties
    {
        _LightColor ("Light Color", Color) = (0.5, 0.9, 1, 1)
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "UnderwaterGodRays"
            ZWrite Off ZTest Always Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float2 _LightPos;    // позиция "солнца" в координатах экрана (0..1)
            float  _Exposure;    // общая яркость луча
            float  _Decay;       // затухание по длине
            float  _Density;     // плотность выборок
            float  _Weight;      // вес одной выборки
            int    _Samples;     // количество шагов
            float4 _LightColor;  // цвет луча солнца

            // Второй источник — собственный прожектор НПА. У него есть
            // настоящая позиция в мире (в отличие от солнца), поэтому
            // экранная точка для него считается напрямую в C#, без трюка
            // "точка далеко по направлению света".
            float  _HasLight2;   // 0 или 1 — включён ли второй источник
            float2 _LightPos2;
            float4 _LightColor2;

            float3 ComputeRay(float2 texCoord, float2 lightPos, float4 lightColor)
            {
                float2 delta = (lightPos - texCoord) * (_Density / _Samples);
                float illuminationDecay = 1.0;
                float3 col = 0;

                for (int s = 0; s < _Samples; s++)
                {
                    texCoord += delta;
                    float4 sample = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, texCoord);
                    float lum = dot(sample.rgb, float3(0.3, 0.59, 0.11));
                    float3 c = lum * lightColor.rgb;
                    c *= illuminationDecay * _Weight;
                    col += c;
                    illuminationDecay *= _Decay;
                }
                return col;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                float3 col = ComputeRay(uv, _LightPos, _LightColor);

                if (_HasLight2 > 0.5)
                    col += ComputeRay(uv, _LightPos2, _LightColor2);

                float4 original = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
                return original + float4(col, 0) * _Exposure;
            }
            ENDHLSL
        }
    }
}

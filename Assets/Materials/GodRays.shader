Shader "Custom/UnderwaterGodRays"
{
    Properties
    {
        _MainTex ("Base (RGB)", 2D) = "white" {}
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Fog { Mode Off }

        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;

            float2 _LightPos;    // позиция "солнца" в координатах экрана (0..1)
            float  _Exposure;    // общая яркость лучей
            float  _Decay;       // затухание по длине
            float  _Density;     // плотность выборок
            float  _Weight;      // вес каждой выборки
            int    _Samples;     // количество шагов
            fixed4 _LightColor;  // цвет лучей

            fixed4 frag (v2f_img i) : SV_Target
            {
                float2 texCoord = i.uv;

                // направление от текущего пикселя к источнику света
                float2 delta = (_LightPos - texCoord) * (_Density / _Samples);

                float illuminationDecay = 1.0;
                fixed4 col = 0;

                // проходимся от пикселя к источнику света и накапливаем "свет"
                for(int s = 0; s < _Samples; s++)
                {
                    texCoord += delta;

                    fixed4 sample = tex2D(_MainTex, texCoord);

                    // берём яркость (luminance) пикселя
                    float lum = dot(sample.rgb, float3(0.3, 0.59, 0.11));

                    sample = lum * _LightColor;           // окрашиваем в цвет света
                    sample *= illuminationDecay * _Weight;

                    col += sample;
                    illuminationDecay *= _Decay;
                }

                fixed4 original = tex2D(_MainTex, i.uv);
                return original + col * _Exposure;       // исходное изображение + лучи
            }
            ENDCG
        }
    }
}
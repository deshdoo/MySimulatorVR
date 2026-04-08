Shader "Custom/SeaweedShake"
{
    Properties
    {
        _Color ("Main Color", Color) = (1,1,1,1)
        _MainTex ("Albedo (RGB)", 2D) = "white" {}
        _Speed   ("Sway Speed",   Range(0, 10)) = 1
        _Amount  ("Sway Amount",  Range(0, 0.3)) = 0.05
        _WaveFreq("Wave Freq",    Range(0, 8)) = 2
        _TipSharp("Tip Sharpness",Range(1, 5)) = 2
        _StrandVariance("Strand Variance", Range(0, 10)) = 2 // Разнообразие между ветками
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 200
        Cull Off

        CGPROGRAM
        #pragma surface surf Standard vertex:vert fullforwardshadows addshadow
        #pragma target 3.0

        sampler2D _MainTex;
        fixed4 _Color;
        float _Speed;
        float _Amount;
        float _WaveFreq;
        float _TipSharp;
        float _StrandVariance;

        struct Input
        {
            float2 uv_MainTex;
        };

        void vert (inout appdata_full v)
        {
            // 0 у основания, 1 у кончика (из Vertex Colors)
            float heightMask = v.color.r;

            // Уникальное смещение для каждой ветки на основе её положения в пространстве.
            // Используем константы (12.43, 7.51), чтобы смещение не было линейным.
            float strandOffset = (v.vertex.x * 12.43 + v.vertex.z * 7.51) * _StrandVariance;

            // Итоговая фаза: Время + Высота (для волны) + Смещение ветки (для разнообразия)
            float phase = _Time.y * _Speed + (v.vertex.y * _WaveFreq) + strandOffset;

            // Сама волна (смешиваем два синуса для хаотичности)
            float wave = sin(phase) * 0.7 + sin(phase * 1.5) * 0.3;

            // Сила изгиба (усиливается к кончику)
            float amp = pow(heightMask, _TipSharp) * _Amount;

            // Смещаем вершины. Косинус в Z делает движение слегка круговым.
            v.vertex.x += wave * amp;
            v.vertex.z += cos(phase * 0.8) * amp * 0.5;
        }

        void surf (Input IN, inout SurfaceOutputStandard o)
        {
            fixed4 c = tex2D (_MainTex, IN.uv_MainTex) * _Color;
            o.Albedo = c.rgb;
            o.Alpha  = c.a;

            o.Emission = c.rgb * 0.3; 
        }
        ENDCG
    }
    FallBack "Diffuse"
}
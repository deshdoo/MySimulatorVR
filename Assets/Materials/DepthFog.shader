Shader "Custom/DepthFog"
{
    Properties
    {
        _FogColor ("Fog Color", Color) = (0.0588, 0.1647, 0.2275, 1)
        _FogDensity ("Fog Density", Range(0, 1)) = 0.15
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "DepthFog"
            ZWrite Off ZTest Always Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            float4 _FogColor;
            float _FogDensity;

            // Заполняются скриптом SpotlightFogClear каждый кадр — луч фонаря
            // "выедает" туман в своём конусе, а не просто светит сквозь него.
            float3 _SpotlightPos;
            float3 _SpotlightDir;
            float _SpotlightRange;
            float _SpotlightCosAngle;
            float _SpotlightClearStrength;
            float _SpotlightSoftness;

            // Цвет/яркость рассеянного в воде света внутри конуса — добавляется
            // поверх картинки там же, где мы "выедаем" туман, имитируя реальное
            // рассеяние луча на взвеси в воде (как на ROV-камерах).
            float3 _SpotlightScatterColor;
            float _SpotlightScatterIntensity;

            // Вторая фара (парная) — если есть, конус считается от обеих сразу,
            // иначе пятно света получается смещённым к одной фаре.
            float _SpotlightHasB;
            float3 _SpotlightPosB;
            float3 _SpotlightDirB;
            float _SpotlightRangeB;
            float _SpotlightCosAngleB;

            float4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                float4 col = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);

                float rawDepth = SampleSceneDepth(uv);
                float eyeDepth = LinearEyeDepth(rawDepth, _ZBufferParams);

                // Экспоненциальный туман по реальной дистанции до камеры.
                // Эффект целиком живёт в этом Renderer Feature, который
                // подключён только к одному Renderer'у URP — другие камеры,
                // использующие другой Renderer, физически не могут его получить.
                float fogFactor = saturate(1.0 - exp(-eyeDepth * _FogDensity));

                // Точка попадания луча взгляда в реальную поверхность (если её нет —
                // эта точка окажется далеко, на дальней плоскости/небе).
                float3 worldPos = ComputeWorldSpacePosition(uv, rawDepth, UNITY_MATRIX_I_VP);
                float3 camPosWS = _WorldSpaceCameraPos;
                float3 viewDir = normalize(worldPos - camPosWS);

                // Промеряем луч взгляда несколькими точками от камеры и до
                // ближайшего из (реальная поверхность / дальность прожектора) —
                // иначе в открытой воде без поверхности глубина "улетает" далеко
                // за пределы конуса, и расчистка/свечение там никогда не сработают.
                float effectiveRange = max(_SpotlightRange, _SpotlightHasB > 0.5 ? _SpotlightRangeB : 0);
                // Реальный свет в воде гаснет не по жёсткой границе Range, а по
                // закону Бугера-Ламберта: I(d) = exp(-k*d). Берём k таким, чтобы
                // на дистанции Range яркость падала до ~5% — то есть Range здесь
                // это "дальность видимости" фонаря, а не радиус с резким обрезом.
                float marchDist = min(eyeDepth, effectiveRange * 2.5);

                const int SPOTLIGHT_STEPS = 8;
                float scatterAccum = 0;
                for (int s = 1; s <= SPOTLIGHT_STEPS; s++)
                {
                    float t = marchDist * (s / (float)SPOTLIGHT_STEPS);
                    float3 samplePos = camPosWS + viewDir * t;

                    float3 toSample = samplePos - _SpotlightPos;
                    float distToLight = length(toSample);
                    float3 dirToSample = toSample / max(distToLight, 1e-4);
                    float cosAngle = dot(dirToSample, _SpotlightDir);

                    float coneMask = smoothstep(_SpotlightCosAngle, _SpotlightCosAngle + _SpotlightSoftness, cosAngle);
                    float k = 3.0 / max(_SpotlightRange, 0.01);
                    float distAtten = exp(-k * distToLight);
                    float contribution = coneMask * distAtten;

                    if (_SpotlightHasB > 0.5)
                    {
                        float3 toSampleB = samplePos - _SpotlightPosB;
                        float distToLightB = length(toSampleB);
                        float3 dirToSampleB = toSampleB / max(distToLightB, 1e-4);
                        float cosAngleB = dot(dirToSampleB, _SpotlightDirB);

                        float coneMaskB = smoothstep(_SpotlightCosAngleB, _SpotlightCosAngleB + _SpotlightSoftness, cosAngleB);
                        float kB = 3.0 / max(_SpotlightRangeB, 0.01);
                        float distAttenB = exp(-kB * distToLightB);
                        contribution = max(contribution, coneMaskB * distAttenB);
                    }

                    scatterAccum += contribution;
                }
                scatterAccum /= SPOTLIGHT_STEPS;

                // Дополнительное смягчение общей кривой — без резкого пятна в центре.
                float clearAmount = saturate(sqrt(scatterAccum) * _SpotlightClearStrength);

                fogFactor = saturate(fogFactor - clearAmount);
                col.rgb = lerp(col.rgb, _FogColor.rgb, fogFactor);

                // Свечение рассеянного света внутри конуса — сильнее ближе к
                // прожектору и к оси луча, ослабевает к краю/дальнему концу.
                col.rgb += clearAmount * _SpotlightScatterColor * _SpotlightScatterIntensity;

                return col;
            }
            ENDHLSL
        }
    }
}

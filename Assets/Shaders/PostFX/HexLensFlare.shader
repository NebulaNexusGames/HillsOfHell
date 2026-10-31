Shader "CarGame/HexLensFlare"
{
    // Шестиугольный блик/боке. От источника света в случайную сторону к краю
    // экрана уходит цепочка призраков: у самого источника мелкие яркие пятна,
    // ближе к краю — крупные тусклые и прозрачные. Полноэкранный проход читает
    // цвет кадра и аддитивно кладёт поверх. Данные источников (_FlareData,
    // _FlareColor, _FlareDir) выставляет LensFlareFeature.
    Properties
    {
        _Intensity ("Intensity", Range(0, 4)) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }

        ZWrite Off
        ZTest Always
        Cull Off

        Pass
        {
            Name "HexLensFlare"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment frag
            #pragma target 3.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/GlobalSamplers.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            #define FLARE_MAX_SOURCES 16
            #define FLARE_GHOSTS 5

            float4 _FlareData[FLARE_MAX_SOURCES];   // xy — экранная позиция (uv), z — яркость, w — радиус
            float4 _FlareColor[FLARE_MAX_SOURCES];  // rgb — цвет источника
            float4 _FlareDir[FLARE_MAX_SOURCES];    // xy — случайное направление следа
            float _FlareCount;
            float _Intensity;

            // Расстояние со знаком до шестиугольника со стороной r.
            float HexagonSdf(float2 p, float r)
            {
                const float3 k = float3(-0.866025404, 0.5, 0.577350269);
                p = abs(p);
                p -= 2.0 * min(dot(k.xy, p), 0.0) * k.xy;
                p -= float2(clamp(p.x, -k.z * r, k.z * r), r);
                return length(p) * sign(p.y);
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv = input.texcoord;
                half4 col = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);

                int count = (int)_FlareCount;
                if (count <= 0)
                    return col;

                float aspect = _ScreenParams.x / max(_ScreenParams.y, 1.0);
                float2 pixel = float2(uv.x * aspect, uv.y);

                // Положение призраков вдоль следа и их размер: у источника мелкие
                // яркие, у края — крупные, тусклые и прозрачные. Идут редко друг
                // от друга.
                const float offsets[FLARE_GHOSTS]  = { 0.14, 0.38, 0.64, 0.88, 1.08 };
                const float sizeMul[FLARE_GHOSTS]  = { 0.45, 0.75, 1.10, 1.48, 1.85 };

                float3 accum = 0.0;

                for (int i = 0; i < count; i++)
                {
                    float4 data = _FlareData[i];
                    float brightness = data.z;
                    if (brightness <= 0.0001)
                        continue;

                    float2 lightPos = float2(data.x * aspect, data.y);
                    float radius = data.w;
                    float3 baseColor = _FlareColor[i].rgb;

                    float2 dir = _FlareDir[i].xy;
                    float dirLen = length(dir);
                    dir = dirLen > 0.0001 ? dir / dirLen : float2(0.0, 1.0);

                    // Расстояние вдоль луча до края экрана.
                    float2 toEdge;
                    toEdge.x = dir.x > 0.0001 ? (aspect - lightPos.x) / dir.x : (dir.x < -0.0001 ? (0.0 - lightPos.x) / dir.x : 1e6);
                    toEdge.y = dir.y > 0.0001 ? (1.0 - lightPos.y) / dir.y : (dir.y < -0.0001 ? (0.0 - lightPos.y) / dir.y : 1e6);
                    float edgeDist = max(0.02, min(toEdge.x, toEdge.y));

                    // У источника — тёплый тон, дальше — зеленоватый.
                    float3 warm = baseColor;
                    float3 cool = lerp(baseColor, float3(0.62, 1.0, 0.45), 0.65);

                    for (int g = 0; g < FLARE_GHOSTS; g++)
                    {
                        float t = offsets[g];
                        float2 ghostPos = lightPos + dir * (edgeDist * t);
                        float2 diff = pixel - ghostPos;

                        float size = max(0.01, radius * sizeMul[g]);
                        float sdf = HexagonSdf(diff, size);
                        float softness = max(0.0005, size * 0.5);
                        float mask = saturate(-sdf / softness);
                        mask = mask * mask * (3.0 - 2.0 * mask);

                        float alpha = 0.75 * pow(max(0.0, 1.0 - t), 1.5);
                        float3 tint = lerp(warm, cool, t);
                        accum += tint * (brightness * alpha * mask);
                    }
                }

                accum = min(accum, 1.0);
                col.rgb += accum * _Intensity;
                return col;
            }
            ENDHLSL
        }
    }
}

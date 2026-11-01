Shader "CarGame/DamageVignettePostEffect"
{
    // Красная виньетка урона, наложенная постобработкой. Цвет, форма и
    // интенсивность приходят глобальными свойствами шейдера, поэтому материал
    // не нужно настраивать: и один общий материал, и рантайм-драйвер видят одно
    // и то же состояние (см. DamageVignette).
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }

        ZWrite Off
        ZTest Always
        Cull Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/GlobalSamplers.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float _DamageVignetteIntensity;
            float4 _DamageVignetteColor;
            float _DamageVignetteInner;
            float _DamageVignetteSoftness;
            float _DamageVignetteSolid;

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;

                half4 col = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);

                float intensity = saturate(_DamageVignetteIntensity);
                if (intensity <= 0.0001)
                    return col;

                float aspect = _ScreenParams.x / max(_ScreenParams.y, 1.0);
                float2 centered = (uv - 0.5) * float2(aspect, 1.0);
                float maxDistance = 0.5 * sqrt(aspect * aspect + 1.0);
                float distance = length(centered) / max(maxDistance, 0.0001);

                float inner = saturate(_DamageVignetteInner);
                float outer = saturate(inner + max(0.01, _DamageVignetteSoftness));
                float t = saturate((distance - inner) / max(0.01, outer - inner));
                t = t * t * (3.0 - 2.0 * t);

                // Полноэкранный режим (кромешная заливка, например белая вспышка
                // и затемнение при смерти): не зависит от расстояния до края.
                float solid = saturate(_DamageVignetteSolid);
                float mix = lerp(t * intensity, intensity, solid);

                col.rgb = lerp(col.rgb, _DamageVignetteColor.rgb, mix);
                return col;
            }
            ENDHLSL
        }
    }
}

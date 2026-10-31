Shader "CarGame/VHSPostEffect"
{
    Properties
    {
        _ScanlineDensity ("Scanline Density (lines/screen)", Float) = 220
        _ScanlineDarkness ("Scanline Darkness", Range(0, 1)) = 0.15
        _ScanlineScroll ("Scanline Scroll", Range(0, 8)) = 0.5
        _BandHeight ("Tracking Band Height (px)", Float) = 90
        _BandSpeed ("Tracking Band Speed", Range(0, 2)) = 0.6
        _BandStrength ("Tracking Band Strength", Range(0, 1)) = 0.2
        _ChromaticAberration ("RGB Shift (px)", Range(0, 8)) = 0.4
        _NoiseStrength ("Noise Strength", Range(0, 1)) = 0.04
        _TrackingStrength ("Tracking Jitter (px)", Range(0, 16)) = 1.5
        _TrackingFrequency ("Tracking Frequency (Hz)", Range(0.1, 60)) = 8
    }

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
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            half _ScanlineDensity;
            half _ScanlineDarkness;
            half _ScanlineScroll;
            half _BandHeight;
            half _BandSpeed;
            half _BandStrength;
            half _ChromaticAberration;
            half _NoiseStrength;
            half _TrackingStrength;
            half _TrackingFrequency;

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;

                float2 pixelCoord = uv * _ScreenParams.xy;
                float h1 = frac(sin(dot(pixelCoord, float2(12.9898, 78.233))) * 43758.5453);
                float h2 = frac(sin(dot(pixelCoord + float2(37.19, 12.7), float2(12.9898, 78.233))) * 43758.5453);

                float trackingPhase = floor(_Time.y * _TrackingFrequency);
                float tracking = frac(h1 * 12.9898 + trackingPhase * 2.317) * 2.0 - 1.0;
                float2 vhsUV = uv;
                vhsUV.x += tracking * _TrackingStrength * _BlitTexture_TexelSize.x;

                half4 col = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, vhsUV);

                float screenHeight = _BlitTexture_TexelSize.w;
                float density = max(1.0, _ScanlineDensity);
                float scan = 0.5 + 0.5 * sin(vhsUV.y * (6.2831853 * density) + _Time.y * _ScanlineScroll);
                col.rgb *= lerp(1.0, 1.0 - _ScanlineDarkness, scan);

                float bandMax = max(1.0, _BandHeight);
                float band = frac(vhsUV.y * (screenHeight / bandMax) - _Time.y * _BandSpeed * 0.25);
                float bandMask = smoothstep(0.0, 0.03, band) * (1.0 - smoothstep(0.03, 0.06, band));
                half4 bandCol = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, vhsUV + float2(_ChromaticAberration * 2.0 * _BlitTexture_TexelSize.x, 0));
                col.rgb = lerp(col.rgb, bandCol.rgb + half3(0.15, 0.15, 0.2), bandMask * _BandStrength);

                half4 chroCol = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, vhsUV);
                half4 chroShifted = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, vhsUV + float2(_ChromaticAberration * _BlitTexture_TexelSize.x, 0));
                col.r = chroShifted.r;
                col.b = chroCol.b;

                float grain = frac(sin(dot(pixelCoord + _Time.y, float2(12.9898, 78.233))) * 43758.5453) * 2.0 - 1.0;
                col.rgb += grain * _NoiseStrength;

                return half4(col.rgb, 1.0);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
Shader "CarGame/PSXPostEffect"
{
    Properties
    {
        _PixelSize ("Pixel Size (1 = full resolution)", Range(1, 16)) = 4
        _QuantizeLevels ("Color Levels Per Channel (0 = off)", Float) = 32
        _DitherAmount ("Dither Amount", Range(0, 4)) = 1
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
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/GlobalSamplers.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            half _QuantizeLevels;
            half _DitherAmount;
            half _PixelSize;

            static const float kBayer[2][2] =
            {
                { 0.0, 2.0 },
                { 3.0, 1.0 }
            };

            float Bayer2x2(float2 pixelCoord)
            {
                int2 idx = int2(floor(fmod(pixelCoord, 2.0)));
                return kBayer[idx.y][idx.x] / 4.0;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;

                float pixelSize = max(_PixelSize, 1.0);
                float2 lowResSize = _ScreenParams.xy / pixelSize;
                float2 lowResPixel = floor(uv * lowResSize);
                float2 lowResUV = (lowResPixel + 0.5) / lowResSize;

                half4 col;
                if (pixelSize > 1.0)
                    col = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, lowResUV);
                else
                    col = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);

                float bayer = Bayer2x2(lowResPixel);
                if (_QuantizeLevels > 0.0)
                {
                    col.rgb = col.rgb * _QuantizeLevels + (bayer - 0.5) * _DitherAmount;
                    col.rgb = floor(col.rgb) / _QuantizeLevels;
                }
                else
                {
                    col.rgb += (bayer - 0.5) * _DitherAmount / 255.0;
                }

                return half4(col.rgb, 1.0);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
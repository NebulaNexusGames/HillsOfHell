Shader "CarGame/Neon"
{
    Properties
    {
        _NeonColor ("Neon Color", Color) = (0.1, 0.95, 1, 1)
        _Alpha ("Alpha", Range(0, 1)) = 1
        _Emission ("Emission Intensity", Range(0, 20)) = 4
        _FresnelPower ("Rim Power", Range(0.5, 12)) = 3
        _FresnelStrength ("Rim Strength", Range(0, 4)) = 1.6
        _Scanline ("Scanline Count", Float) = 40
        _ScanlineStrength ("Scanline Strength", Range(0, 1)) = 0.25
        _ScanlineSpeed ("Scanline Speed", Float) = 1.5
        _Pulse ("Pulse Amount", Range(0, 1)) = 0.18
        _PulseSpeed ("Pulse Speed", Float) = 2
        _Flicker ("Flicker Amount", Range(0, 1)) = 0.08

        [HideInInspector] _Surface ("__surface", Float) = 0.0
        [HideInInspector] _Blend ("__blend", Float) = 0.0
        [HideInInspector] _Cull ("__cull", Float) = 2.0
        [HideInInspector] _SrcBlend ("__src", Float) = 1.0
        [HideInInspector] _DstBlend ("__dst", Float) = 0.0
        [HideInInspector] _SrcBlendAlpha ("__srcA", Float) = 1.0
        [HideInInspector] _DstBlendAlpha ("__dstA", Float) = 0.0
        [HideInInspector] _ZWrite ("__zw", Float) = 1.0
    }

    CustomEditor "CarGame.Editor.Shaders.NeonShaderGUI"

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "UniversalMaterialType" = "Unlit"
            "Queue" = "Geometry"
        }

        Pass
        {
            Name "ForwardUnlit"
            Tags { "LightMode" = "UniversalForward" }

            Blend [_SrcBlend][_DstBlend], [_SrcBlendAlpha][_DstBlendAlpha]
            ZWrite [_ZWrite]
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma shader_feature_local_fragment _SURFACE_TYPE_TRANSPARENT
            #pragma shader_feature_local_fragment _ALPHAPREMULTIPLY_ON

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _NeonColor;
                float _Alpha;
                float _Emission;
                float _FresnelPower;
                float _FresnelStrength;
                float _Scanline;
                float _ScanlineStrength;
                float _ScanlineSpeed;
                float _Pulse;
                float _PulseSpeed;
                float _Flicker;
                float _Surface;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float3 viewDirWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;

                VertexPositionInputs positions = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normals = GetVertexNormalInputs(input.normalOS);

                output.positionCS = positions.positionCS;
                output.normalWS = normals.normalWS;
                output.viewDirWS = GetWorldSpaceViewDir(positions.positionWS);
                output.uv = input.uv;

                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float3 normalWS = normalize(input.normalWS);
                float3 viewDirWS = normalize(input.viewDirWS);

                float fresnel = pow(1.0 - saturate(dot(normalWS, viewDirWS)), _FresnelPower);
                float rim = fresnel * _FresnelStrength;

                float pulse = 1.0 + sin(_Time.y * _PulseSpeed) * _Pulse;
                float scan = sin((input.uv.y * _Scanline) - (_Time.y * _ScanlineSpeed)) * 0.5 + 0.5;
                scan = 1.0 - (scan * _ScanlineStrength);

                float flicker = 1.0 - (frac(sin(_Time.y * 37.0) * 43758.5453) * _Flicker);

                float3 neon = _NeonColor.rgb * (_Emission * pulse * scan * flicker);
                float3 surface = _NeonColor.rgb * 0.12 + neon * (0.25 + rim);

                float alpha = _NeonColor.a * _Alpha;

                #if defined(_SURFACE_TYPE_TRANSPARENT)
                    #if defined(_ALPHAPREMULTIPLY_ON)
                        surface *= alpha;
                    #endif
                    return half4(surface, alpha);
                #else
                    return half4(surface, 1.0);
                #endif
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings ShadowPassVertex(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);

                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, _LightDirection));

                #if UNITY_REVERSED_Z
                    positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif

                output.positionCS = positionCS;
                return output;
            }

            half4 ShadowPassFragment(Varyings input) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Unlit"
}

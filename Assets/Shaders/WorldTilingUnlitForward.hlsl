#ifndef CAR_GAME_WORLD_TILING_UNLIT_FORWARD_INCLUDED
#define CAR_GAME_WORLD_TILING_UNLIT_FORWARD_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Unlit.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SurfaceInput.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/GlobalSamplers.hlsl"
#if defined(LOD_FADE_CROSSFADE)
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/LODCrossFade.hlsl"
#endif

float _TileSize;
float4 _TileOffset;
float _BlendShift;
float4x4 _TilingAnchorMatrix = float4x4(1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1);

struct Attributes
{
    float4 positionOS : POSITION;
    float2 uv : TEXCOORD0;
    float3 normalOS : NORMAL;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct Varyings
{
    float2 uv : TEXCOORD0;
    float fogCoord : TEXCOORD1;
    float4 positionCS : SV_POSITION;
    float3 positionWS : TEXCOORD2;
    float3 normalWS : TEXCOORD3;
    float3 objectPos : TEXCOORD4;
    float3 objectNormal : TEXCOORD5;
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};

Varyings UnlitPassVertex(Attributes input)
{
    Varyings output = (Varyings)0;

    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

    VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
    VertexNormalInputs normalInput = GetVertexNormalInputs(input.normalOS);

    output.positionCS = vertexInput.positionCS;
    output.positionWS = vertexInput.positionWS;
    output.normalWS = normalInput.normalWS;
    output.objectPos = mul(_TilingAnchorMatrix, float4(input.positionOS.xyz, 1.0)).xyz;
    output.objectNormal = normalize(mul((float3x3)_TilingAnchorMatrix, input.normalOS));
    output.uv = TRANSFORM_TEX(input.uv, _BaseMap);

    #if defined(_FOG_FRAGMENT)
    output.fogCoord = vertexInput.positionVS.z;
    #else
    output.fogCoord = ComputeFogFactor(vertexInput.positionCS.z);
    #endif

    return output;
}

half4 SampleAlbedoTiled(Varyings input)
{
    half4 texColor;

    #if defined(_TILING_ON)
    float3 triPosition = input.positionWS;
    float3 triNormal = input.normalWS;

    #if defined(_SPACE_WORLD)
    triPosition = input.positionWS;
    triNormal = input.normalWS;
    #else
    triPosition = input.objectPos;
    triNormal = input.objectNormal;
    #endif

    triNormal = normalize(triNormal);

    float tile = max(_TileSize, 1e-4);
    float2 tileOffset = _TileOffset.xy;

    float3 blend = pow(abs(triNormal), _BlendShift);
    blend.x = max(blend.x, 1e-5);
    blend.y = max(blend.y, 1e-5);
    blend.z = max(blend.z, 1e-5);
    blend /= blend.x + blend.y + blend.z;

    half4 texX = SAMPLE_TEXTURE2D(_BaseMap, sampler_PointRepeat, triPosition.yz / tile + tileOffset);
    half4 texY = SAMPLE_TEXTURE2D(_BaseMap, sampler_PointRepeat, triPosition.xz / tile + tileOffset);
    half4 texZ = SAMPLE_TEXTURE2D(_BaseMap, sampler_PointRepeat, triPosition.xy / tile + tileOffset);

    texColor = texX * blend.x + texY * blend.y + texZ * blend.z;
    #else
    texColor = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
    #endif

    return texColor;
}

void UnlitPassFragment(Varyings input, out half4 outColor : SV_Target0)
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

    half4 texColor = SampleAlbedoTiled(input);

    half3 color = texColor.rgb * _BaseColor.rgb;
    half alpha = texColor.a * _BaseColor.a;

    alpha = AlphaDiscard(alpha, _Cutoff);
    color = AlphaModulate(color, alpha);

    #ifdef LOD_FADE_CROSSFADE
    LODFadeCrossFade(input.positionCS);
    #endif

    InputData inputData;
    inputData = (InputData)0;

    half4 finalColor = UniversalFragmentUnlit(inputData, color, alpha);

    #if defined(_SCREEN_SPACE_OCCLUSION) && !defined(_SURFACE_TYPE_TRANSPARENT)
    float2 normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
    AmbientOcclusionFactor aoFactor = GetScreenSpaceAmbientOcclusion(normalizedScreenSpaceUV);
    finalColor.rgb *= aoFactor.directAmbientOcclusion;
    #endif

    #if defined(_FOG_FRAGMENT)
    #if (defined(FOG_LINEAR) || defined(FOG_EXP) || defined(FOG_EXP2))
    float viewZ = -input.fogCoord;
    float nearToFarZ = max(viewZ - _ProjectionParams.y, 0);
    half fogFactor = ComputeFogFactorZ0ToFar(nearToFarZ);
    #else
    half fogFactor = 0;
    #endif
    #else
    half fogFactor = input.fogCoord;
    #endif

    finalColor.rgb = MixFog(finalColor.rgb, fogFactor);
    finalColor.a = OutputAlpha(finalColor.a, IsSurfaceTypeTransparent(_Surface));

    outColor = finalColor;
}

#endif
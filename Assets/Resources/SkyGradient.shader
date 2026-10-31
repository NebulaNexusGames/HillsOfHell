Shader "CarGame/SkyGradient"
{
    Properties
    {
        _TopColor ("Top Color", Color) = (0.53, 0.81, 0.92, 1)
        _BottomColor ("Bottom Color", Color) = (0.31, 0.55, 0.71, 1)
        _GroundColor ("Ground Color", Color) = (0.1, 0.18, 0.25, 1)
        _HorizonBlend ("Ground Blend", Range(0.001, 1)) = 0.35
        _HorizonSharpness ("Vertical Sharpness", Range(0.1, 8)) = 1.4
        _CloudColor ("Cloud Color", Color) = (1, 1, 1, 1)
        _CloudCoverage ("Cloud Coverage", Range(0, 1)) = 0.45
        _CloudScale ("Cloud Scale", Range(0.1, 24)) = 5
        _CloudSpeed ("Cloud Drift", Range(0, 1)) = 0.03
        _StarsIntensity ("Stars Intensity", Range(0, 2)) = 0
        _StarsDensity ("Stars Density", Range(6, 200)) = 40
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Background"
            "RenderType" = "Background"
            "PreviewType" = "Skybox"
            "IgnoreProjector" = "True"
        }

        Cull Off
        ZWrite Off
        ZTest LEqual

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            fixed4 _TopColor;
            fixed4 _BottomColor;
            fixed4 _GroundColor;
            float _HorizonBlend;
            float _HorizonSharpness;
            fixed4 _CloudColor;
            float _CloudCoverage;
            float _CloudScale;
            float _CloudSpeed;
            float _StarsIntensity;
            float _StarsDensity;

            struct appdata
            {
                float4 vertex : POSITION;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 dir : TEXCOORD0;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.dir = v.vertex.xyz;
                return o;
            }

            // Хэш на целочисленной решётке: из него рождаются и облака, и звёзды.
            float hash13(float3 p)
            {
                p = frac(p * 0.1031);
                p += dot(p, p.yzx + 33.33);
                return frac((p.x + p.y) * p.z);
            }

            float vnoise(float3 p)
            {
                float3 i = floor(p);
                float3 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);

                float n000 = hash13(i + float3(0.0, 0.0, 0.0));
                float n100 = hash13(i + float3(1.0, 0.0, 0.0));
                float n010 = hash13(i + float3(0.0, 1.0, 0.0));
                float n110 = hash13(i + float3(1.0, 1.0, 0.0));
                float n001 = hash13(i + float3(0.0, 0.0, 1.0));
                float n101 = hash13(i + float3(1.0, 0.0, 1.0));
                float n011 = hash13(i + float3(0.0, 1.0, 1.0));
                float n111 = hash13(i + float3(1.0, 1.0, 1.0));

                float nx00 = lerp(n000, n100, f.x);
                float nx10 = lerp(n010, n110, f.x);
                float nx01 = lerp(n001, n101, f.x);
                float nx11 = lerp(n011, n111, f.x);

                float nxy0 = lerp(nx00, nx10, f.y);
                float nxy1 = lerp(nx01, nx11, f.y);
                return lerp(nxy0, nxy1, f.z);
            }

            float fbm(float3 p)
            {
                float sum = 0.0;
                float amp = 0.5;
                for (int i = 0; i < 4; i++)
                {
                    sum += amp * vnoise(p);
                    p = p * 2.03 + 17.0;
                    amp *= 0.5;
                }
                return sum;
            }

            // Мерцающее звёздное поле: в редких ячейках решётки зажигается искра.
            float starfield(float3 dir, float time)
            {
                float3 p = dir * _StarsDensity;
                float3 id = floor(p);
                float3 f = frac(p) - 0.5;

                float h = hash13(id);
                if (h < 0.86) return 0.0;

                float3 offset = float3(
                    hash13(id + 11.7),
                    hash13(id + 23.3),
                    hash13(id + 37.1)) - 0.5;

                float d = length(f - offset * 0.5);
                float core = smoothstep(0.11, 0.0, d);

                float twinkle = 0.55 + 0.45 * sin(time * 2.0 + h * 40.0);
                float brightness = 0.6 + 0.4 * hash13(id + 53.9);
                return core * twinkle * brightness;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 dir = normalize(i.dir);
                float y = dir.y;

                float t = pow(saturate(y), _HorizonSharpness);
                fixed4 sky = lerp(_BottomColor, _TopColor, t);

                float g = saturate(-y / max(0.001, _HorizonBlend));
                fixed4 ground = lerp(_BottomColor, _GroundColor, g);

                fixed4 c = y >= 0.0 ? sky : ground;
                fixed3 col = c.rgb;

                // --- Облака: сферический 3D-шум, без растяжения у горизонта ---
                float cloudMask = 0.0;
                if (y > 0.0)
                {
                    float2 wind = float2(_Time.y, _Time.y * 0.4) * _CloudSpeed;
                    float3 sp = dir * _CloudScale + float3(wind.x, 0.0, wind.y);
                    float n = fbm(sp);
                    cloudMask = smoothstep(_CloudCoverage, _CloudCoverage + 0.22, n);
                    cloudMask *= smoothstep(0.15, 0.55, y); // облака держатся выше горизонта
                }

                col = lerp(col, _CloudColor.rgb, cloudMask);

                // --- Звёзды: гаснут у горизонта и под облаками ---
                float starMask = _StarsIntensity * smoothstep(-0.02, 0.28, y) * (1.0 - cloudMask);
                if (starMask > 0.001)
                {
                    float stars = starfield(dir, _Time.y);
                    col += fixed3(0.90, 0.93, 1.0) * stars * starMask;
                }

                return fixed4(col, 1.0);
            }
            ENDCG
        }
    }

    Fallback Off
}

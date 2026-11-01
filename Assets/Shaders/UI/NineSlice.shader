Shader "CarGame/UI/NineSlice"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _SliceTex ("Slice Texture", 2D) = "white" {}
        _Border ("Border L,B,R,T", Vector) = (6, 6, 6, 6)
        _Color ("Color", Color) = (1, 1, 1, 1)
        _Brightness ("Brightness", Range(0, 4)) = 1
        _TintFrame ("Tint Frame By Vertex Color", Range(0, 1)) = 0

        // Обводка. Настройка независима от заливки: прозрачность обводки
        // задают _FrameAlpha и альфа _FrameColor, а не заливка. Поэтому заливку
        // можно сделать прозрачной, а обводку оставить как есть.
        [Space(8)]
        _FrameColor ("Outline Color", Color) = (1, 1, 1, 1)
        _FrameAlpha ("Outline Opacity", Range(0, 1)) = 1

        [Space(8)]
        _Fill ("Fill", Range(0, 1)) = 1
        _FillAlpha ("Fill Opacity", Range(0, 1)) = 1
        _FillInset ("Fill Inset (px)", Range(0, 16)) = 2
        _FillRadius ("Fill Corner Radius (px)", Range(0, 32)) = 3

        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "NINE_SLICE"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            struct appdata_t
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float2 texcoord : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _SliceTex;
            float4 _SliceTex_TexelSize;
            float4 _Border;
            fixed4 _Color;
            float _Brightness;
            float _Fill;
            float _FillAlpha;
            float _FillInset;
            float _FillRadius;
            float _TintFrame;
            float4 _FrameColor;
            float _FrameAlpha;
            float4 _ClipRect;

            v2f vert(appdata_t v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                o.worldPosition = v.vertex;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.texcoord = v.texcoord;
                o.color = v.color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 uv = i.texcoord;

                // Размер квадрата в пикселях из производных UV: работает и в
                // screen-space, и в world-space (скриншот-подобные канвасы).
                float2 dUv = float2(
                    length(float2(ddx(uv.x), ddy(uv.x))),
                    length(float2(ddx(uv.y), ddy(uv.y))));
                float2 screenSize = 1.0 / max(dUv, 1e-8);
                float2 texSize = float2(_SliceTex_TexelSize.z, _SliceTex_TexelSize.w);

                // Борт рамки берётся из _Border и как размер на экране, и как
                // область в текстуре — так рамка выглядит ровно как нарисована.
                float l = _Border.x / max(screenSize.x, 1e-6);
                float r = _Border.z / max(screenSize.x, 1e-6);
                float b = _Border.y / max(screenSize.y, 1e-6);
                float t = _Border.w / max(screenSize.y, 1e-6);

                // На маленьком квадрате борта не должны наезжать друг на друга.
                float xScale = l + r > 1.0 ? 1.0 / (l + r) : 1.0;
                float yScale = b + t > 1.0 ? 1.0 / (b + t) : 1.0;
                l *= xScale;
                r *= xScale;
                b *= yScale;
                t *= yScale;

                float2 suv;
                // X: углы берём из текстуры, середину растягиваем.
                if (uv.x < l)
                {
                    suv.x = uv.x / max(l, 1e-6) * (_Border.x / texSize.x);
                }
                else if (uv.x > 1.0 - r)
                {
                    suv.x = 1.0 - (1.0 - uv.x) / max(r, 1e-6) * (_Border.z / texSize.x);
                }
                else
                {
                    float span = max(1.0 - l - r, 1e-6);
                    float texSpan = 1.0 - (_Border.x + _Border.z) / texSize.x;
                    suv.x = _Border.x / texSize.x + (uv.x - l) / span * texSpan;
                }

                // Y: углы берём из текстуры, середину растягиваем.
                if (uv.y < b)
                {
                    suv.y = uv.y / max(b, 1e-6) * (_Border.y / texSize.y);
                }
                else if (uv.y > 1.0 - t)
                {
                    suv.y = 1.0 - (1.0 - uv.y) / max(t, 1e-6) * (_Border.w / texSize.y);
                }
                else
                {
                    float span = max(1.0 - b - t, 1e-6);
                    float texSpan = 1.0 - (_Border.y + _Border.w) / texSize.y;
                    suv.y = _Border.y / texSize.y + (uv.y - b) / span * texSpan;
                }

                fixed4 tex = tex2D(_SliceTex, suv);

                // Обводка: цвет текстуры среза, подкрашенный материалом
                // (_Color, _FrameColor) и, при _TintFrame = 1, цветом вершины
                // (состояние кнопки). Прозрачность обводки задаётся отдельно и
                // не зависит от заливки.
                fixed3 frameRGB = tex.rgb * _Color.rgb * _Brightness * _FrameColor.rgb;
                frameRGB *= lerp(fixed3(1.0, 1.0, 1.0), i.color.rgb, _TintFrame);

                // Силуэт заливки повторяет срез углов рамки, чтобы острые углы
                // цветного фона не вылезали за рамку. На саму рамку не влияет.
                float2 p = (uv - 0.5) * screenSize;
                float2 innerHalf = max(0.5 * screenSize - _FillInset, float2(0.0, 0.0));
                float radius = min(_FillRadius, min(innerHalf.x, innerHalf.y));
                float2 q = abs(p) - (innerHalf - radius);
                float dist = length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - radius;
                float aa = max(abs(ddx(dist)) + abs(ddy(dist)), 1e-4);
                float fillMask = 1.0 - smoothstep(-aa, aa, dist);

                // Заливка управляется альфой вершины (состояние кнопки) и
                // _Fill/_FillAlpha. Обводка управляется только _FrameAlpha и
                // альфой _FrameColor. Эти настройки независимы: заливку можно
                // сделать прозрачной, а обводку оставить видимой.
                //
                // Альфа вершины в покое не ровно нуль, а 0.5: у полностью
                // прозрачных мешей CanvasRenderer выбрасывает геометрию, и
                // тогда пропадает и обводка тоже. Порог 0.5 превращает покой
                // (0.5) в нулевую заливку, а наведение/нажатие (1.0) — в
                // полную, так что обводка видна всегда, а заливка только под
                // курсором и при нажатии.
                float fillAmount = smoothstep(0.5, 1.0, i.color.a);

                fixed4 color;
                color.rgb = lerp(i.color.rgb, frameRGB, tex.a);
                float fillAlpha = fillAmount * _Fill * _FillAlpha * fillMask;
                float frameAlpha = _FrameAlpha * _FrameColor.a * tex.a;
                color.a = max(fillAlpha, frameAlpha);

                #ifdef UNITY_UI_CLIP_RECT
                color.a *= UnityGet2DClipping(i.worldPosition.xy, _ClipRect);
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip(color.a - 0.001);
                #endif

                return color;
            }
            ENDCG
        }
    }

    CustomEditor "CarGame.Editor.Shaders.NineSliceShaderGUI"

    Fallback "UI/Default"
}

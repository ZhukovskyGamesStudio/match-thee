// Переход при рестарте комнаты: экран закрывается «пикселями» от краёв к центру и раскрывается обратно,
// по фронту волны идёт голубое свечение. _Progress 0..1, на 0.5 экран закрыт целиком.
Shader "MatchThee/PixelWipe" {
    Properties {
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Color", Color) = (0.094, 0.094, 0.118, 1)
        _Glow ("Glow", Color) = (0.373, 0.616, 0.820, 1)
        _Progress ("Progress", Range(0, 1)) = 0
        _Cells ("Cells", Vector) = (48, 27, 0, 0)
    }
    SubShader {
        Tags { "Queue" = "Overlay" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Cull Off
        ZWrite Off
        ZTest Always
        Blend SrcAlpha OneMinusSrcAlpha

        Pass {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            fixed4 _Color;
            fixed4 _Glow;
            float _Progress;
            float4 _Cells;

            v2f vert(appdata v) {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            float hash(float2 p) {
                return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
            }

            fixed4 frag(v2f i) : SV_Target {
                float2 cell = floor(i.uv * _Cells.xy);
                float2 center = (cell + 0.5) / _Cells.xy;
                float2 aspect = float2(_Cells.x / _Cells.y, 1.0);
                float dist = length((center - 0.5) * aspect) / length(0.5 * aspect); // 0 в центре, 1 в углу
                float noise = hash(cell) * 0.15;
                // 1.2 → пусто, 0 → закрыто (с плато вокруг середины, чтобы центр успел сойтись), снова 1.2 → пусто
                float front = saturate((abs(1.0 - 2.0 * _Progress) - 0.15) / 0.85) * 1.2;
                float depth = dist + noise - front;               // насколько клетка «за» фронтом
                float covered = step(0.0, depth);
                float grow = saturate(depth * 6.0 + smoothstep(0.3, 0.0, front)); // клетка вырастает из точки; у закрытия все целые
                float2 local = abs(frac(i.uv * _Cells.xy) - 0.5) * 2.0;
                float inside = step(max(local.x, local.y), grow);
                float glow = smoothstep(0.18, 0.0, depth) * smoothstep(0.0, 0.15, front); // у полного закрытия свечение гаснет
                fixed4 color = lerp(_Color, _Glow, glow);
                color.a = covered * inside;
                return color;
            }
            ENDCG
        }
    }
}

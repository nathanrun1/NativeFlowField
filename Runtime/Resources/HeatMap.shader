Shader "NativeDijkstraMap/HeatMap"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        MinCost ("Min Cost", Float) = 0
        MaxCost ("Max Cost", Float) = 10
        Alpha ("Alpha", Float) = 1
        FlowSpeed ("Flow Speed", Float) = 0
        FlowCycle ("Flow Cycle", Float) = 1
        [Enum(Inferno,0, Magma,1, Plasma,2, Viridis,3)] Gradient ("Gradient", Float) = 0
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType"="Transparent" }
        Blend SrcAlpha OneMinusSrcAlpha
        Cull Off ZWrite On ZTest Less

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #define FLT_MAX  3.4028235e+38
            #define FLT_MIN -3.4028235e+38

            #include "Gradient.hlsl"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            UNITY_DECLARE_TEX2D(_MainTex);
            float MinCost;
            float MaxCost;
            float Alpha;
            float FlowSpeed;
            float FlowCycle;
            float Gradient;

            fixed4 frag(v2f i) : SV_Target
            {
                float value = UNITY_SAMPLE_TEX2D( _MainTex, i.uv );

                if (value == 0) return float4(gradient(Gradient, 1), Alpha);  // Target
                if (value <= FLT_MIN) return float4(0, 0, 0, 0); // Free
                if (value >= FLT_MAX) return float4(0, 0, 0, 0); // Obstacle
                if (value > MaxCost) return float4(0, 0, 0, 0); // Too far away from target

                value = (value - MaxCost) / (MinCost - MaxCost);    // Scale
                value = FlowCycle*value - _Time.y*FlowSpeed;    // Animate Flow
                if (value < 0) value -= (int)value - 1;  // Modulo
                float normalized = saturate(value); // Normalize
                return float4(gradient(Gradient, normalized), Alpha);  // Gradient
            }
            ENDCG
        }
    }
}
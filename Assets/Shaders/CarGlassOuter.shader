Shader "OpenWheelRacer/CarGlassOuter"
{
    Properties
    {
        _Color ("Color", Color) = (0.005, 0.005, 0.007, 1)
        _Glossiness ("Smoothness", Range(0, 1)) = 0.97
        _Metallic ("Metallic", Range(0, 1)) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        LOD 100
        Cull Front
        Pass
        {
            Tags { "LightMode" = "ForwardBase" }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "Lighting.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 wNormal : TEXCOORD0;
                float3 wPos : TEXCOORD1;
            };

            fixed4 _Color;
            half _Glossiness;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.wNormal = UnityObjectToWorldNormal(v.normal);
                o.wPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 N = normalize(i.wNormal);
                float3 L = normalize(_WorldSpaceLightPos0.xyz);
                float3 V = normalize(_WorldSpaceCameraPos - i.wPos);
                float diff = max(0.0, dot(N, L));
                float3 H = normalize(L + V);
                float spec = pow(max(0.0, dot(N, H)), 48.0) * 0.5;
                float fres = pow(1.0 - abs(dot(N, V)), 3.0) * 0.18;
                float3 amb = UNITY_LIGHTMODEL_AMBIENT.rgb;
                float3 col = _Color.rgb * (amb + _LightColor0.rgb * diff)
                           + (spec + fres) * _LightColor0.rgb;
                return fixed4(col, 1.0);
            }
            ENDCG
        }
    }
    FallBack "Diffuse"
}

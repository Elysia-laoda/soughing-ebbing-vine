Shader "UI/Ring"
{
    Properties
    {
        _Color ("Tint", Color) = (1,1,1,1)
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15

        _CenterUV ("Center UV", Vector) = (0.5,0.5,0,0)
        _RadiusUV ("Radius UV", Range(0,0.5)) = 0.5
        _ThicknessUV ("Thickness UV", Range(0,0.5)) = 0.01
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
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
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float2 uv       : TEXCOORD0;
                fixed4 color    : COLOR;
            };

            struct v2f
            {
                float4 vertex   : SV_POSITION;
                float2 uv       : TEXCOORD0;
                fixed4 color    : COLOR;
            };

            float2 _CenterUV;
            float _RadiusUV;
            float _ThicknessUV;

            v2f vert (appdata_t v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // 以 uv 中心为圆心、_RadiusUV 为半径画一条带宽 _ThicknessUV 的圆环
                float d = distance(i.uv, _CenterUV);
                float band = smoothstep(_RadiusUV - _ThicknessUV, _RadiusUV, d)
                           - smoothstep(_RadiusUV, _RadiusUV + _ThicknessUV, d);
                fixed4 col = i.color;
                col.a *= band;
                return col;
            }
            ENDCG
        }
    }
}

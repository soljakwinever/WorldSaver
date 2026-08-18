Shader "Hidden/WorldSaver/Main Menu Preview Crossfade"
{
    Properties
    {
        [PerRendererData] _MainTex ("Outgoing", 2D) = "black" {}
        _ToTex ("Incoming", 2D) = "black" {}
        _Blend ("Blend", Range(0, 1)) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Overlay"
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
            "CanUseSpriteAtlas" = "True"
        }
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            sampler2D _ToTex;
            float _Blend;

            fixed4 frag(v2f_img input) : SV_Target
            {
                fixed4 outgoing = tex2D(_MainTex, input.uv);
                fixed4 incoming = tex2D(_ToTex, input.uv);
                return lerp(outgoing, incoming, saturate(_Blend));
            }
            ENDHLSL
        }
    }
}

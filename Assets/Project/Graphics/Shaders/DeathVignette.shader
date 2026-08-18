Shader "Hidden/WorldSaver/Death Vignette"
{
    Properties { _Progress ("Progress", Range(0,1)) = 0 }
    SubShader
    {
        Tags { "Queue"="Overlay" "RenderType"="Transparent" }
        Blend SrcAlpha OneMinusSrcAlpha
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"
            float _Progress;
            fixed4 frag(v2f_img input) : SV_Target
            {
                float2 centered = input.uv - 0.5;
                centered.x *= _ScreenParams.x / max(1.0, _ScreenParams.y);
                float radius = lerp(1.25, 0.015, saturate(_Progress));
                float alpha = smoothstep(radius, radius + 0.12,
                    length(centered));
                return fixed4(0, 0, 0, alpha);
            }
            ENDCG
        }
    }
}

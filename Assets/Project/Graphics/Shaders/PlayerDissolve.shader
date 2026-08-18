Shader "WorldSaver/Player Dissolve"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _DissolveAmount ("Dissolve", Range(0,1)) = 0
        _EdgeWidth ("Edge Width", Range(0.001,0.25)) = 0.08
        [HDR] _EdgeColor ("Edge Color", Color) = (2,0.15,0.05,1)
        _EdgeIntensity ("HDR Glow Intensity", Float) = 2
    }
    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "RenderType"="Transparent"
            "RenderPipeline"="UniversalPipeline"
            "CanUseSpriteAtlas"="True"
        }
        Blend SrcAlpha OneMinusSrcAlpha
        Cull Off
        ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            float _DissolveAmount;
            float _EdgeWidth;
            float4 _EdgeColor;
            float _EdgeIntensity;

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            float Hash(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            half4 frag(Varyings input) : SV_Target
            {
                half4 sprite = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                float noise = Hash(floor(input.uv * 48.0));
                clip(noise - _DissolveAmount);
                float edge = 1.0 - smoothstep(
                    _DissolveAmount, _DissolveAmount + _EdgeWidth, noise);
                half3 glow = _EdgeColor.rgb * _EdgeIntensity * edge;
                return half4(glow, sprite.a);
            }
            ENDHLSL
        }
    }
}

Shader "WorldSaver/Portal Particle Mask"
{
    Properties
    {
        _MainTex ("Particle Texture", 2D) = "white" {}
        _Cutoff ("Alpha Cutoff", Range(0, 1)) = 0.05
    }
    SubShader
    {
        Tags { "Queue"="Transparent-10" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Tags { "LightMode"="Universal2D" }
            ZWrite Off
            Cull Off
            ColorMask 0
            Stencil { Ref 1 Comp Always Pass Replace }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS : POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            float4 _MainTex_ST;
            float _Cutoff;
            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.color = input.color;
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                return output;
            }
            half4 frag(Varyings input) : SV_Target
            {
                half alpha = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv).a * input.color.a;
                clip(alpha - _Cutoff);
                return 0;
            }
            ENDHLSL
        }
    }
}

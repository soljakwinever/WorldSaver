Shader "WorldSaver/CoverageParticle"
{
    Properties
    {
        _MainTex ("Particle Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
        [HideInInspector] _CoverageMask ("Coverage Mask", 2D) = "black" {}
        [HideInInspector] _CoveragePreviousMask ("Previous Coverage Mask", 2D) = "black" {}
        [HideInInspector] _CoverageTransition ("Coverage Transition", Range(0, 1)) = 1
        [HideInInspector] _CoverageParticleSlot ("Coverage Slot", Int) = 0
        [HideInInspector] _CoverageParticleBlendMode ("Particle Blend Mode", Float) = 0
        [HideInInspector] _SrcBlend ("Source Blend", Float) = 5
        [HideInInspector] _DstBlend ("Destination Blend", Float) = 10
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "RenderType"="Transparent"
            "RenderPipeline"="UniversalPipeline"
        }
        Blend [_SrcBlend] [_DstBlend]
        Cull Off
        ZWrite Off

        Pass
        {
            Tags { "LightMode"="Universal2D" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                float2 world : TEXCOORD1;
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            TEXTURE2D(_CoverageMask);
            SAMPLER(sampler_CoverageMask);
            TEXTURE2D(_CoveragePreviousMask);
            SAMPLER(sampler_CoveragePreviousMask);
            float4 _MainTex_ST;
            half4 _Color;
            float4 _CoveragePriorities;
            float4 _CoverageAreaOrigin;
            float4 _CoverageAreaSize;
            float4 _CoverageMaskSize;
            float _CoveragePixelsPerUnit;
            float _CoverageNoiseScale;
            float _CoverageDetailScale;
            float _CoverageDetailStrength;
            float _CoverageBlendSoftness;
            float _CoverageAlphaClipThreshold;
            float _CoverageTransition;
            float _CoverageParticleBlendMode;
            int _CoverageParticleSlot;

            float2 CoverageGradient(float2 lattice)
            {
                float2 value = float2(
                    dot(lattice, float2(127.1, 311.7)),
                    dot(lattice, float2(269.5, 183.3)));
                return normalize(frac(sin(value) * 43758.5453) * 2.0 - 1.0);
            }

            float CoveragePerlin(float2 position)
            {
                float2 cell = floor(position);
                float2 local = frac(position);
                float2 blend = local * local * local *
                    (local * (local * 6.0 - 15.0) + 10.0);
                float bottom = lerp(
                    dot(CoverageGradient(cell), local),
                    dot(CoverageGradient(cell + float2(1.0, 0.0)),
                        local - float2(1.0, 0.0)),
                    blend.x);
                float top = lerp(
                    dot(CoverageGradient(cell + float2(0.0, 1.0)),
                        local - float2(0.0, 1.0)),
                    dot(CoverageGradient(cell + 1.0), local - 1.0),
                    blend.x);
                return saturate(lerp(bottom, top, blend.y) * 0.7 + 0.5);
            }

            int SelectCoverageWinner(half4 amounts)
            {
                int winner = -1;
                float priority = -100001.0;
                [unroll]
                for (int i = 0; i < 4; i++)
                {
                    if (amounts[i] > 0.001 &&
                        _CoveragePriorities[i] > priority)
                    {
                        winner = i;
                        priority = _CoveragePriorities[i];
                    }
                }
                return winner;
            }

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.world = TransformObjectToWorld(input.positionOS.xyz).xy;
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                output.color = input.color * _Color;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                half4 particle = SAMPLE_TEXTURE2D(
                    _MainTex, sampler_MainTex, input.uv) * input.color;
                clip(particle.a - 0.001);

                float pixelsPerUnit = max(_CoveragePixelsPerUnit, 1.0);
                float2 pixelWorld =
                    (floor(input.world * pixelsPerUnit) + 0.5) /
                    pixelsPerUnit;
                float2 localPosition = pixelWorld - _CoverageAreaOrigin.xy;
                if (localPosition.x < 0.0 || localPosition.y < 0.0 ||
                    localPosition.x >= _CoverageAreaSize.x ||
                    localPosition.y >= _CoverageAreaSize.y)
                    discard;

                float2 maskUv = (localPosition + 1.0) / _CoverageMaskSize.xy;
                half4 previous = SAMPLE_TEXTURE2D(
                    _CoveragePreviousMask,
                    sampler_CoveragePreviousMask,
                    maskUv);
                half4 target = SAMPLE_TEXTURE2D(
                    _CoverageMask, sampler_CoverageMask, maskUv);
                half4 amounts = lerp(
                    previous, target, saturate(_CoverageTransition));
                int winner = SelectCoverageWinner(amounts);
                if (winner < 0 || winner != _CoverageParticleSlot)
                    discard;

                float largeNoise = CoveragePerlin(
                    pixelWorld * max(_CoverageNoiseScale, 0.0001));
                float detailNoise = CoveragePerlin(
                    pixelWorld * max(_CoverageDetailScale, 0.0001));
                float noise = saturate(largeNoise +
                    (detailNoise - 0.5) * _CoverageDetailStrength);
                float softness = max(_CoverageBlendSoftness, 0.0001);
                float reveal = smoothstep(
                    noise - softness,
                    noise + softness,
                    saturate(amounts[winner]));
                clip(reveal - _CoverageAlphaClipThreshold);
                if (_CoverageParticleBlendMode > 0.5)
                    particle.rgb *= particle.a;
                return particle;
            }
            ENDHLSL
        }
    }
}

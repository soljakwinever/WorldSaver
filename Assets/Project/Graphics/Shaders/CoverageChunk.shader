Shader "WorldSaver/CoverageChunk"
{
    Properties
    {
        _CoverageMask ("Coverage Mask", 2D) = "black" {}
        _CoveragePreviousMask ("Previous Coverage Mask", 2D) = "black" {}
        [HideInInspector] _CoverageTransition ("Coverage Transition", Range(0, 1)) = 1
        _CoverageAtlas ("Coverage Atlas", 2D) = "white" {}
        _CoveragePixelsPerUnit ("Pixels Per Unit", Float) = 16
        _CoverageNoiseScale ("Noise Scale", Float) = 0.25
        _CoverageDetailScale ("Detail Scale", Float) = 1.5
        _CoverageDetailStrength ("Detail Strength", Range(0, 1)) = 0.2
        _CoverageBlendSoftness ("Blend Softness", Range(0.0001, 1)) = 0.1
        _CoverageAlphaClipThreshold ("Alpha Clip", Range(0, 1)) = 0.5
        _CoverageShadowOffsetPixels ("Shadow Offset (Pixels)", Range(0, 4)) = 1
        _CoverageShadowExpansion ("Shadow Expansion", Range(0, 0.5)) = 0.08
        _CoverageShadowBrightness ("Shadow Brightness", Range(0, 1)) = 0.65
        _CoverageShadowSaturation ("Shadow Saturation", Range(0, 1)) = 0.65
        _CoverageMaxLightMultiplier ("Maximum Light Multiplier", Range(1, 2)) = 1.25
    }
    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "RenderType"="Transparent"
            "RenderPipeline"="UniversalPipeline"
        }
        Blend SrcAlpha OneMinusSrcAlpha
        Cull Off
        ZWrite Off

        Pass
        {
            Tags { "LightMode" = "Universal2D" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/ShapeLightShared.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/CombinedShapeLightShared.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float2 world : TEXCOORD1;
                half2 lightingUV : TEXCOORD2;
            };

            TEXTURE2D(_CoverageMask);
            SAMPLER(sampler_CoverageMask);
            TEXTURE2D(_CoveragePreviousMask);
            SAMPLER(sampler_CoveragePreviousMask);
            TEXTURE2D(_CoverageAtlas);
            SAMPLER(sampler_CoverageAtlas);
            float4 _CoverageRects[4];
            float4 _CoverageTilings[4];
            float4 _CoverageColors[4];
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
            float _CoverageShadowOffsetPixels;
            float _CoverageShadowExpansion;
            float _CoverageShadowBrightness;
            float _CoverageShadowSaturation;
            float _CoverageMaxLightMultiplier;

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

            half4 SampleCoverageAmounts(float2 pixelWorld)
            {
                float2 localPosition = pixelWorld - _CoverageAreaOrigin.xy;
                float2 maskUv = (localPosition + 1.0) /
                    _CoverageMaskSize.xy;
                half4 previous = SAMPLE_TEXTURE2D(
                    _CoveragePreviousMask,
                    sampler_CoveragePreviousMask,
                    maskUv);
                half4 target = SAMPLE_TEXTURE2D(
                    _CoverageMask, sampler_CoverageMask, maskUv);
                return lerp(previous, target, saturate(_CoverageTransition));
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

            float EvaluateCoverageReveal(float2 pixelWorld, float amount)
            {
                float largeNoise = CoveragePerlin(
                    pixelWorld * max(_CoverageNoiseScale, 0.0001));
                float detailNoise = CoveragePerlin(
                    pixelWorld * max(_CoverageDetailScale, 0.0001));
                float noise = saturate(largeNoise +
                    (detailNoise - 0.5) * _CoverageDetailStrength);
                float softness = max(_CoverageBlendSoftness, 0.0001);
                return smoothstep(
                    noise - softness, noise + softness, saturate(amount));
            }

            half4 SampleCoverageColor(float2 pixelWorld, int winner)
            {
                float4 rect = _CoverageRects[winner];
                float2 pattern = frac(pixelWorld /
                    max(_CoverageTilings[winner].xy, 0.0001));
                float2 atlasUv = rect.xy + pattern * rect.zw;
                half4 atlas = SAMPLE_TEXTURE2D(
                    _CoverageAtlas, sampler_CoverageAtlas, atlasUv);
                return atlas * (half4)_CoverageColors[winner];
            }

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                output.world = _CoverageAreaOrigin.xy +
                    input.uv * _CoverageAreaSize.xy;
                output.lightingUV = half2(
                    ComputeScreenPos(
                        output.positionCS / output.positionCS.w).xy);
                return output;
            }

            half4 ApplyCoverageLighting(
                half4 color,
                float2 uv,
                half2 lightingUV)
            {
                SurfaceData2D surfaceData;
                InputData2D inputData;
                InitializeSurfaceData(
                    color.rgb,
                    color.a,
                    half4(1.0, 1.0, 1.0, 1.0),
                    surfaceData);
                InitializeInputData(uv, lightingUV, inputData);
                half4 lit = CombinedShapeLightShared(surfaceData, inputData);
                half3 maximumLit = min(
                    color.rgb * max(_CoverageMaxLightMultiplier, 1.0),
                    half3(1.0, 1.0, 1.0));
                lit.rgb = min(lit.rgb, maximumLit);
                lit.a = color.a;
                return lit;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float pixelsPerUnit = max(_CoveragePixelsPerUnit, 1.0);
                float2 pixelWorld =
                    (floor(input.world * pixelsPerUnit) + 0.5) /
                    pixelsPerUnit;
                half4 amounts = SampleCoverageAmounts(pixelWorld);
                int winner = SelectCoverageWinner(amounts);
                if (winner >= 0)
                {
                    float reveal = EvaluateCoverageReveal(
                        pixelWorld, amounts[winner]);
                    half4 color = SampleCoverageColor(pixelWorld, winner);
                    if (reveal >= _CoverageAlphaClipThreshold &&
                        color.a >= _CoverageAlphaClipThreshold)
                    {
                        color.a = _CoverageColors[winner].a;
                        return ApplyCoverageLighting(
                            color, input.uv, input.lightingUV);
                    }
                }

                // Sampling the source above this fragment moves its silhouette
                // downward while retaining the same pixel grid and noise breakup.
                float2 shadowWorld = pixelWorld + float2(
                    0.0, max(_CoverageShadowOffsetPixels, 0.0) /
                    pixelsPerUnit);
                half4 shadowAmounts = SampleCoverageAmounts(shadowWorld);
                int shadowWinner = SelectCoverageWinner(shadowAmounts);
                if (shadowWinner >= 0)
                {
                    float shadowReveal = EvaluateCoverageReveal(
                        shadowWorld,
                        shadowAmounts[shadowWinner] +
                        _CoverageShadowExpansion);
                    half4 shadowColor = SampleCoverageColor(
                        shadowWorld, shadowWinner);
                    if (shadowReveal >= _CoverageAlphaClipThreshold &&
                        shadowColor.a >= _CoverageAlphaClipThreshold)
                    {
                        half luminance = dot(
                            shadowColor.rgb,
                            half3(0.2126, 0.7152, 0.0722));
                        shadowColor.rgb = lerp(
                            luminance.xxx,
                            shadowColor.rgb,
                            saturate(_CoverageShadowSaturation));
                        shadowColor.rgb *= saturate(
                            _CoverageShadowBrightness);
                        shadowColor.a =
                            _CoverageColors[shadowWinner].a;
                        return ApplyCoverageLighting(
                            shadowColor, input.uv, input.lightingUV);
                    }
                }

                discard;
                return half4(0.0, 0.0, 0.0, 0.0);
            }
            ENDHLSL
        }
    }
}

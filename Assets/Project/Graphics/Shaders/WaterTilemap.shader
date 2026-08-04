Shader "WorldSaver/Water Tilemap"
{
    Properties
    {
        [PerRendererData] _MainTex("Sprite Texture", 2D) = "white" {}
        [NoScaleOffset] _MaskTex("Light Mask", 2D) = "white" {}
        [NoScaleOffset] _NormalMap("Normal Map", 2D) = "bump" {}

        [HDR] _Color("Water Color", Color) = (1, 1, 1, 1)
        _WaterHeight("Water Height", Float) = 0.2
        _Depth("Depth Value", Float) = 0.2
        [Toggle] _UseVertexDepth("Use Tile Depth", Float) = 1
        _DeepBrightness("Deep Brightness", Range(0, 1)) = 0.35
        _DepthExponent("Depth Falloff", Range(0.01, 8)) = 1

        [HDR] _EmissionColor("Emission Color", Color) = (0, 0, 0, 1)
        _EmissionStrength("Emission Strength", Range(0, 16)) = 0

        [NoScaleOffset] _WaterTextures("Water Texture Array", 2DArray) = "" {}
        _TextureIndex("Texture Array Index", Float) = 0
        [Toggle] _UseTileTextureIndex("Use Tile Texture Index", Float) = 1
        _TextureInfluence("Texture Influence", Range(0, 1)) = 0
        _TextureTiling("Texture Tiling", Vector) = (1, 1, 0, 0)
        _TextureOffset("Texture Offset", Vector) = (0, 0, 0, 0)
        _WaterScaleA("Layer A Scale", Vector) = (1, 1, 0, 0)
        _WaterScaleB("Layer B Scale", Vector) = (2, 2, 0, 0)
        _WaterScrollA("Layer A Scroll Speed", Vector) = (0.02, 0.01, 0, 0)
        _WaterScrollB("Layer B Scroll Speed", Vector) = (-0.015, 0.025, 0, 0)
        _WaterLayerBlend("Layer B Blend", Range(0, 1)) = 0.5
        _BaseTileAtlasGrid("Base Tile Atlas Grid", Vector) = (32, 32, 0, 0)
        _WaveScaleStrength("Wave Scale Strength", Range(0, 0.95)) = 0.12
        _WaveScaleSpeed("Wave Scale Speed", Float) = 1.5
        _WaveScaleFrequency("Wave Scale Frequency", Float) = 0.75
        _WaveScaleDirection("Wave Scale Direction", Vector) = (1, 0.35, 0, 0)

        [MaterialToggle] _ZWrite("Z Write", Float) = 0

        // Required by SpriteRenderer/TilemapRenderer compatibility paths.
        [HideInInspector] _RendererColor("Renderer Color", Color) = (1, 1, 1, 1)
        [HideInInspector] _AlphaTex("External Alpha", 2D) = "white" {}
        [HideInInspector] _EnableExternalAlpha("Enable External Alpha", Float) = 0
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

    TEXTURE2D_ARRAY(_WaterTextures);
    SAMPLER(sampler_WaterTextures);

    CBUFFER_START(UnityPerMaterial)
        half4 _Color;
        half4 _EmissionColor;
        float4 _TextureTiling;
        float4 _TextureOffset;
        float4 _WaterScaleA;
        float4 _WaterScaleB;
        float4 _WaterScrollA;
        float4 _WaterScrollB;
        float4 _BaseTileAtlasGrid;
        float4 _WaveScaleDirection;
        float4 _WaterEmissionData[64];
        float _WaterHeight;
        float _Depth;
        float _UseVertexDepth;
        float _DeepBrightness;
        float _DepthExponent;
        float _EmissionStrength;
        float _TextureIndex;
        float _UseTileTextureIndex;
        float _TextureInfluence;
        float _WaterLayerBlend;
        float _WaveScaleStrength;
        float _WaveScaleSpeed;
        float _WaveScaleFrequency;
    CBUFFER_END

    half4 GetWaterColor(
        float2 worldPosition,
        half4 tilePayload,
        out float selectedTextureIndex)
    {
        // Two low bits from each RGB channel carry a six-bit texture index.
        // The remaining six bits retain the authored water surface color.
        float3 packedColor = round(saturate(tilePayload.rgb) * 255.0);
        float3 surfaceCode = floor(packedColor / 4.0);
        float3 indexCode = packedColor - surfaceCode * 4.0;
        float useTileTextureIndex = saturate(_UseTileTextureIndex);
        half3 surfaceColor = lerp(
            tilePayload.rgb,
            surfaceCode / 63.0,
            useTileTextureIndex);
        float tileTextureIndex = indexCode.r +
                                 indexCode.g * 4.0 +
                                 indexCode.b * 16.0;

        // Chunk packs full-range normalized depth into alpha to retain Color32
        // precision. The material fallback accepts an unpacked 0.._WaterHeight
        // value for meshes that do not supply tile payloads.
        float materialDepth = saturate(_Depth / max(_WaterHeight, 0.00001));
        float normalizedDepth = lerp(
            materialDepth,
            saturate(tilePayload.a),
            saturate(_UseVertexDepth));

        float depthGradient = pow(
            max(normalizedDepth, 0.00001),
            max(_DepthExponent, 0.00001));
        half brightness = lerp(_DeepBrightness, 1.0h, depthGradient);

        float2 arrayUV = worldPosition * _TextureTiling.xy + _TextureOffset.xy;
        float materialTextureIndex = max(0.0, floor(_TextureIndex + 0.5));
        selectedTextureIndex = lerp(
            materialTextureIndex,
            tileTextureIndex,
            useTileTextureIndex);
        float2 uvA = arrayUV * _WaterScaleA.xy +
                     _Time.y * _WaterScrollA.xy;
        float2 uvB = arrayUV * _WaterScaleB.xy +
                     _Time.y * _WaterScrollB.xy;
        half4 layerA = SAMPLE_TEXTURE2D_ARRAY(
            _WaterTextures,
            sampler_WaterTextures,
            uvA,
            selectedTextureIndex);
        half4 layerB = SAMPLE_TEXTURE2D_ARRAY(
            _WaterTextures,
            sampler_WaterTextures,
            uvB,
            selectedTextureIndex);
        half layerBlend = saturate(_WaterLayerBlend);
        half4 arraySample;
        arraySample.rgb = layerA.rgb + layerB.rgb * layerBlend;
        arraySample.a = layerA.a * lerp(1.0h, layerB.a, layerBlend);
        half textureAmount = saturate(_TextureInfluence);

        half4 water;
        water.rgb = surfaceColor * _Color.rgb * brightness;
        water.rgb *= lerp(half3(1, 1, 1), arraySample.rgb, textureAmount);
        water.a = _Color.a * lerp(1.0h, arraySample.a, textureAmount);
        return water;
    }

    float2 GetWaveScaledSpriteUV(float2 uv, float2 worldPosition)
    {
        float2 waveDirection = _WaveScaleDirection.xy /
            max(length(_WaveScaleDirection.xy), 0.00001);
        float wavePhase = dot(worldPosition, waveDirection) *
                          _WaveScaleFrequency -
                          _Time.y * _WaveScaleSpeed;
        float waveScale = max(
            0.05,
            1.0 + sin(wavePhase) * saturate(_WaveScaleStrength));

        float2 atlasGrid = max(round(_BaseTileAtlasGrid.xy), 1.0);
        float2 atlasPosition = uv * atlasGrid;
        float2 atlasCell = floor(atlasPosition);
        float2 localUV = atlasPosition - atlasCell;
        localUV = (localUV - 0.5) / waveScale + 0.5;
        // Keep the pulsing sample inside its 16px tile to prevent atlas bleed.
        localUV = clamp(localUV, 0.03125, 0.96875);
        return (atlasCell + localUV) / atlasGrid;
    }

    half3 GetEmission(
        half4 spriteSample,
        half4 water,
        float selectedTextureIndex)
    {
        // Transparent blending applies spriteSample.a * water.a once to both
        // the lit base and emission, so emission must remain straight-alpha.
        int emissionIndex = clamp(
            (int)floor(selectedTextureIndex + 0.5),
            0,
            63);
        half4 tileEmission = _WaterEmissionData[emissionIndex];
        half useTileEmission = saturate(_UseTileTextureIndex);
        half3 emissionColor = lerp(
            _EmissionColor.rgb,
            tileEmission.rgb,
            useTileEmission);
        half emissionStrength = lerp(
            _EmissionStrength,
            tileEmission.a,
            useTileEmission);
        return spriteSample.rgb * water.rgb * emissionColor *
               emissionStrength;
    }
    ENDHLSL

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "CanUseSpriteAtlas" = "True"
        }

        Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
        Cull Off
        ZWrite [_ZWrite]

        Pass
        {
            Tags { "LightMode" = "Universal2D" }

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex WaterLitVertex
            #pragma fragment WaterLitFragment
            #pragma multi_compile_instancing
            #pragma multi_compile _ DEBUG_DISPLAY
            #pragma multi_compile _ SKINNED_SPRITE

            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/ShapeLightShared.hlsl"

            struct Attributes
            {
                COMMON_2D_INPUTS
                half4 color : COLOR;
                UNITY_SKINNED_VERTEX_INPUTS
            };

            struct Varyings
            {
                COMMON_2D_LIT_OUTPUTS
                half4 tilePayload : COLOR;
                float2 worldPosition : TEXCOORD4;
            };

            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Lit2DCommon.hlsl"

            Varyings WaterLitVertex(Attributes input)
            {
                UNITY_SKINNED_VERTEX_COMPUTE(input);
                SetUpSpriteInstanceProperties();
                input.positionOS = UnityFlipSprite(input.positionOS, unity_SpriteProps.xy);

                Varyings output = CommonLitVertex(input);
                output.tilePayload = input.color * unity_SpriteColor;
                output.worldPosition = TransformObjectToWorld(input.positionOS).xy;
                return output;
            }

            half4 WaterLitFragment(Varyings input) : SV_Target
            {
                input.uv = GetWaveScaledSpriteUV(
                    input.uv,
                    input.worldPosition);
                float selectedTextureIndex;
                half4 water = GetWaterColor(
                    input.worldPosition,
                    input.tilePayload,
                    selectedTextureIndex);
                half4 result = CommonLitFragment(input, water);
                half4 spriteSample = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                result.rgb += GetEmission(
                    spriteSample,
                    water,
                    selectedTextureIndex);
                return result;
            }
            ENDHLSL
        }

        Pass
        {
            Tags { "LightMode" = "NormalsRendering" }

            HLSLPROGRAM
            #pragma vertex NormalsRenderingVertex
            #pragma fragment NormalsRenderingFragment
            #pragma multi_compile_instancing
            #pragma multi_compile _ SKINNED_SPRITE

            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"

            struct Attributes
            {
                COMMON_2D_NORMALS_INPUTS
                float4 color : COLOR;
                UNITY_SKINNED_VERTEX_INPUTS
            };

            struct Varyings
            {
                COMMON_2D_NORMALS_OUTPUTS
                half4 color : COLOR;
                float2 worldPosition : TEXCOORD4;
            };

            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Normals2DCommon.hlsl"

            Varyings NormalsRenderingVertex(Attributes input)
            {
                UNITY_SKINNED_VERTEX_COMPUTE(input);
                SetUpSpriteInstanceProperties();
                input.positionOS = UnityFlipSprite(input.positionOS, unity_SpriteProps.xy);

                Varyings output = CommonNormalsVertex(input);
                output.color = half4(1, 1, 1, _Color.a) * unity_SpriteColor;
                output.worldPosition = TransformObjectToWorld(input.positionOS).xy;
                return output;
            }

            half4 NormalsRenderingFragment(Varyings input) : SV_Target
            {
                SetUpSpriteInstanceProperties();
                input.uv = GetWaveScaledSpriteUV(
                    input.uv,
                    input.worldPosition);
                return CommonNormalsFragment(input, input.color);
            }
            ENDHLSL
        }

        Pass
        {
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex WaterUnlitVertex
            #pragma fragment WaterUnlitFragment
            #pragma multi_compile_instancing
            #pragma multi_compile _ DEBUG_DISPLAY SKINNED_SPRITE

            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"

            struct Attributes
            {
                COMMON_2D_INPUTS
                half4 color : COLOR;
                UNITY_SKINNED_VERTEX_INPUTS
            };

            struct Varyings
            {
                COMMON_2D_OUTPUTS
                half4 tilePayload : COLOR;
                float2 worldPosition : TEXCOORD4;
            };

            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/2DCommon.hlsl"

            Varyings WaterUnlitVertex(Attributes input)
            {
                UNITY_SKINNED_VERTEX_COMPUTE(input);
                SetUpSpriteInstanceProperties();
                input.positionOS = UnityFlipSprite(input.positionOS, unity_SpriteProps.xy);

                Varyings output = CommonUnlitVertex(input);
                output.tilePayload = input.color * unity_SpriteColor;
                output.worldPosition = TransformObjectToWorld(input.positionOS).xy;
                return output;
            }

            half4 WaterUnlitFragment(Varyings input) : SV_Target
            {
                input.uv = GetWaveScaledSpriteUV(
                    input.uv,
                    input.worldPosition);
                float selectedTextureIndex;
                half4 water = GetWaterColor(
                    input.worldPosition,
                    input.tilePayload,
                    selectedTextureIndex);
                half4 result = CommonUnlitFragment(input, water);
                half4 spriteSample = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                result.rgb += GetEmission(
                    spriteSample,
                    water,
                    selectedTextureIndex);
                return result;
            }
            ENDHLSL
        }
    }

    Fallback "Universal Render Pipeline/2D/Sprite-Lit-Default"
}

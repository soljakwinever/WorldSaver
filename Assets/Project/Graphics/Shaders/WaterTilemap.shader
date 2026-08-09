Shader "WorldSaver/Water Tilemap"
{
    Properties
    {
        [PerRendererData] _MainTex("Sprite Texture", 2D) = "white" {}
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
        _WaterScaleA("Texture Scale", Vector) = (1, 1, 0, 0)
        _WaterScrollA("Scroll Speed", Vector) = (0.02, 0.01, 0, 0)
        _BaseTileAtlasGrid("Base Tile Atlas Grid", Vector) = (32, 32, 0, 0)
        _WaveScaleStrength("Wave Scale Strength", Range(0, 0.95)) = 0.12
        _WaveScaleSpeed("Wave Scale Speed", Float) = 1.5
        _WaveScaleFrequency("Wave Scale Frequency", Float) = 0.75
        _WaveScaleDirection("Wave Scale Direction", Vector) = (1, 0.35, 0, 0)

        [MaterialToggle] _ZWrite("Z Write", Float) = 0

        [HideInInspector] _RendererColor("Renderer Color", Color) = (1, 1, 1, 1)
        [HideInInspector] _AlphaTex("External Alpha", 2D) = "white" {}
        [HideInInspector] _EnableExternalAlpha("Enable External Alpha", Float) = 0
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

    TEXTURE2D_ARRAY(_WaterTextures);
    SAMPLER(sampler_WaterTextures);
    TEXTURE2D(_MainTex);
    SAMPLER(sampler_MainTex);
    half4 _RendererColor;

    CBUFFER_START(UnityPerMaterial)
        half4 _Color;
        half4 _EmissionColor;
        float4 _TextureTiling;
        float4 _TextureOffset;
        float4 _WaterScaleA;
        float4 _WaterScrollA;
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
        float _WaveScaleStrength;
        float _WaveScaleSpeed;
        float _WaveScaleFrequency;
    CBUFFER_END

    half4 GetWaterColor(
        float2 worldPosition,
        half4 tilePayload,
        out float selectedTextureIndex,
        out half4 emission)
    {
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

        float materialDepth = saturate(_Depth / max(_WaterHeight, 0.00001));
        float normalizedDepth = lerp(
            materialDepth,
            saturate(tilePayload.a),
            saturate(_UseVertexDepth));
        float depthGradient = pow(
            max(normalizedDepth, 0.00001),
            max(_DepthExponent, 0.00001));
        half brightness = lerp(_DeepBrightness, 1.0h, depthGradient);

        float materialTextureIndex = max(0.0, floor(_TextureIndex + 0.5));
        selectedTextureIndex = lerp(
            materialTextureIndex,
            tileTextureIndex,
            useTileTextureIndex);
        float2 textureUV =
            (worldPosition * _TextureTiling.xy + _TextureOffset.xy) *
            _WaterScaleA.xy + _Time.y * _WaterScrollA.xy;
        half4 textureSample = SAMPLE_TEXTURE2D_ARRAY(
            _WaterTextures,
            sampler_WaterTextures,
            textureUV,
            selectedTextureIndex);
        half textureAmount = saturate(_TextureInfluence);

        half4 water;
        water.rgb = surfaceColor * _Color.rgb * brightness;
        water.rgb *= lerp(half3(1, 1, 1), textureSample.rgb, textureAmount);
        water.a = _Color.a * lerp(1.0h, textureSample.a, textureAmount);

        int emissionIndex = clamp(
            (int)floor(selectedTextureIndex + 0.5), 0, 63);
        half4 tileEmission = _WaterEmissionData[emissionIndex];
        emission.rgb = lerp(
            _EmissionColor.rgb,
            tileEmission.rgb,
            useTileTextureIndex);
        emission.a = lerp(
            _EmissionStrength,
            tileEmission.a,
            useTileTextureIndex);
        return water;
    }

    float2 GetWaveScaledSpriteUV(float2 uv, float2 worldPosition)
    {
        float2 direction = _WaveScaleDirection.xy /
            max(length(_WaveScaleDirection.xy), 0.00001);
        float phase = dot(worldPosition, direction) * _WaveScaleFrequency -
                      _Time.y * _WaveScaleSpeed;
        float scale = max(
            0.05,
            1.0 + sin(phase) * saturate(_WaveScaleStrength));
        float2 grid = max(round(_BaseTileAtlasGrid.xy), 1.0);
        float2 atlasPosition = uv * grid;
        float2 cell = floor(atlasPosition);
        float2 localUV = frac(atlasPosition);
        localUV = clamp((localUV - 0.5) / scale + 0.5, 0.03125, 0.96875);
        return (cell + localUV) / grid;
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

        // Deliberately unlit: large water bodies no longer allocate 2D light
        // or normals work for every covered pixel.
        Pass
        {
            Tags { "LightMode" = "Universal2D" }

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex WaterVertex
            #pragma fragment WaterFragment
            #pragma multi_compile_instancing
            #pragma multi_compile _ DEBUG_DISPLAY
            #pragma multi_compile _ SKINNED_SPRITE

            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"

            struct Attributes
            {
                COMMON_2D_INPUTS
                half4 color : COLOR;
                UNITY_SKINNED_VERTEX_INPUTS
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half4 tilePayload : COLOR;
                float2 worldPosition : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings WaterVertex(Attributes input)
            {
                UNITY_SKINNED_VERTEX_COMPUTE(input);
                SetUpSpriteInstanceProperties();
                input.positionOS = UnityFlipSprite(
                    input.positionOS,
                    unity_SpriteProps.xy);

                Varyings output;
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS);
                output.uv = input.uv;
                output.tilePayload = input.color * unity_SpriteColor;
                output.worldPosition =
                    TransformObjectToWorld(input.positionOS).xy;
                return output;
            }

            half4 WaterFragment(Varyings input) : SV_Target
            {
                float2 spriteUV = GetWaveScaledSpriteUV(
                    input.uv,
                    input.worldPosition);
                half4 sprite = SAMPLE_TEXTURE2D(
                    _MainTex,
                    sampler_MainTex,
                    spriteUV);
                float textureIndex;
                half4 emission;
                half4 water = GetWaterColor(
                    input.worldPosition,
                    input.tilePayload,
                    textureIndex,
                    emission);

                half4 result = sprite * water * _RendererColor;
                result.rgb += sprite.rgb * water.rgb * emission.rgb * emission.a;
                return result;
            }
            ENDHLSL
        }
    }

    Fallback Off
}

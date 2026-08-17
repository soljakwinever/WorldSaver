using System;
using System.Collections.Generic;
using Project.Scripts;
using Project.Scripts.DataTypes;
using UnityEngine;

internal sealed class CoverageParticlePresenter : IDisposable
{
    private const string ParticleShaderName = "WorldSaver/CoverageParticle";
    private static readonly int SourceBlendId = Shader.PropertyToID("_SrcBlend");
    private static readonly int DestinationBlendId = Shader.PropertyToID("_DstBlend");
    private static readonly int ParticleBlendModeId =
        Shader.PropertyToID("_CoverageParticleBlendMode");

    private sealed class Effect
    {
        public CoverageData Data;
        public GameObject Instance;
        public ParticleSystem[] Systems;
        public ParticleSystemRenderer[] Renderers;
        public Material[] Materials;
        public readonly MaterialPropertyBlock Properties = new();
        public bool Emitting;
    }

    private readonly Transform _player;
    private readonly WorldData _worldData;
    private readonly CoverageAreaRenderer _coverageRenderer;
    private readonly Dictionary<CoverageData, Effect> _effects = new();
    private readonly List<CoverageData> _removals = new();
    private Shader _particleShader;
    private bool _reportedMissingShader;

    public CoverageParticlePresenter(
        Transform player,
        WorldData worldData,
        CoverageAreaRenderer coverageRenderer)
    {
        _player = player;
        _worldData = worldData;
        _coverageRenderer = coverageRenderer;
    }

    public void Tick()
    {
        if (_player == null || _worldData == null || _coverageRenderer == null)
            return;

        SynchronizeEffects();
        CoverageData winner = GetCoverageUnderPlayer(out float amount);
        foreach (Effect effect in _effects.Values)
        {
            bool shouldEmit = amount > 0f && SameCoverage(effect.Data, winner);
            bool needsMask = shouldEmit || effect.Instance.activeSelf;
            effect.Instance.transform.localPosition = Vector3.zero;
            if (needsMask)
            {
                foreach (ParticleSystemRenderer renderer in effect.Renderers)
                {
                    if (!_coverageRenderer.ApplyParticleMaskProperties(
                            renderer, effect.Data, effect.Properties))
                    {
                        shouldEmit = false;
                        break;
                    }
                }
            }

            SetEmitting(effect, shouldEmit);
            DeactivateWhenDrained(effect);
        }
    }

    public void Dispose()
    {
        foreach (Effect effect in _effects.Values)
            DestroyEffect(effect);
        _effects.Clear();
    }

    private void SynchronizeEffects()
    {
        CoverageData[] layers = _worldData.coverageLayers ??
                                Array.Empty<CoverageData>();
        foreach (CoverageData data in layers)
        {
            if (data == null || data.CoverageParticlePrefab == null ||
                _effects.ContainsKey(data) ||
                !_coverageRenderer.HasLoadedCoverage(data))
                continue;

            Effect effect = CreateEffect(data);
            if (effect != null)
                _effects.Add(data, effect);
        }

        _removals.Clear();
        foreach (KeyValuePair<CoverageData, Effect> pair in _effects)
        {
            if (!_coverageRenderer.HasLoadedCoverage(pair.Key) ||
                pair.Key.CoverageParticlePrefab == null)
                _removals.Add(pair.Key);
        }
        foreach (CoverageData data in _removals)
        {
            DestroyEffect(_effects[data]);
            _effects.Remove(data);
        }
    }

    private Effect CreateEffect(CoverageData data)
    {
        _particleShader ??= Shader.Find(ParticleShaderName);
        if (_particleShader == null)
        {
            if (!_reportedMissingShader)
            {
                Debug.LogError($"Coverage particle shader '{ParticleShaderName}' was not found.");
                _reportedMissingShader = true;
            }
            return null;
        }

        GameObject instance = UnityEngine.Object.Instantiate(
            data.CoverageParticlePrefab, _player, false);
        instance.name = $"{data.name} Coverage Particles";
        ParticleSystem[] systems = instance.GetComponentsInChildren<ParticleSystem>(true);
        ParticleSystemRenderer[] renderers =
            instance.GetComponentsInChildren<ParticleSystemRenderer>(true);
        if (systems.Length == 0 || renderers.Length == 0)
        {
            Debug.LogWarning(
                $"Coverage particle prefab '{data.CoverageParticlePrefab.name}' has no particle systems.",
                data.CoverageParticlePrefab);
            DestroyObject(instance);
            return null;
        }

        Material[] materials = new Material[renderers.Length];
        int maximumDepth = 0;
        int[] rendererDepths = new int[renderers.Length];
        for (int i = 0; i < renderers.Length; i++)
        {
            rendererDepths[i] = GetDepthBelow(instance.transform,
                renderers[i].transform);
            maximumDepth = Mathf.Max(maximumDepth, rendererDepths[i]);
        }
        for (int i = 0; i < renderers.Length; i++)
        {
            ParticleSystemRenderer renderer = renderers[i];
            renderer.sortingLayerName = CoverageAreaRenderer.SortingLayerName;
            renderer.sortingOrder = CoverageAreaRenderer.ParticleSortingOrder +
                                    maximumDepth - rendererDepths[i];
            Material source = renderer.sharedMaterial;
            Material material = new(_particleShader)
            {
                name = $"{data.name} Coverage Particle Material",
                hideFlags = HideFlags.DontSave
            };
            CopyParticleAppearance(source, material);
            ConfigureBlendMode(material, data.CoverageParticleBlendMode);
            renderer.sharedMaterial = material;
            materials[i] = material;
        }

        foreach (ParticleSystem system in systems)
        {
            ParticleSystem.MainModule main = system.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
        }

        instance.transform.localPosition = Vector3.zero;
        instance.SetActive(false);
        return new Effect
        {
            Data = data,
            Instance = instance,
            Systems = systems,
            Renderers = renderers,
            Materials = materials
        };
    }

    private CoverageData GetCoverageUnderPlayer(out float amount)
    {
        return _coverageRenderer.TryGetDisplayedCoverage(
                _player.position, out CoverageData data, out amount)
            ? data
            : null;
    }

    private static void SetEmitting(Effect effect, bool emitting)
    {
        if (effect.Emitting == emitting)
            return;

        effect.Emitting = emitting;
        if (emitting)
        {
            effect.Instance.SetActive(true);
            foreach (ParticleSystem system in effect.Systems)
                system.Play(false);
        }
        else
        {
            foreach (ParticleSystem system in effect.Systems)
                system.Stop(
                    false,
                    ParticleSystemStopBehavior.StopEmitting);
        }
    }

    private static void DeactivateWhenDrained(Effect effect)
    {
        if (effect.Emitting || !effect.Instance.activeSelf)
            return;

        foreach (ParticleSystem system in effect.Systems)
        {
            if (system.IsAlive(false))
                return;
        }
        effect.Instance.SetActive(false);
    }

    private static void CopyParticleAppearance(Material source, Material target)
    {
        if (source == null)
            return;

        string textureProperty = source.HasProperty("_MainTex")
            ? "_MainTex"
            : source.HasProperty("_BaseMap") ? "_BaseMap" : null;
        if (textureProperty != null)
        {
            target.SetTexture("_MainTex", source.GetTexture(textureProperty));
            target.SetTextureScale("_MainTex", source.GetTextureScale(textureProperty));
            target.SetTextureOffset("_MainTex", source.GetTextureOffset(textureProperty));
        }

        if (source.HasProperty("_Color"))
            target.SetColor("_Color", source.GetColor("_Color"));
        else if (source.HasProperty("_BaseColor"))
            target.SetColor("_Color", source.GetColor("_BaseColor"));
    }

    private static void ConfigureBlendMode(
        Material material,
        CoverageParticleBlendMode blendMode)
    {
        bool multiply = blendMode == CoverageParticleBlendMode.Multiply;
        material.SetInt(SourceBlendId, (int)(multiply
            ? UnityEngine.Rendering.BlendMode.DstColor
            : UnityEngine.Rendering.BlendMode.SrcAlpha));
        material.SetInt(DestinationBlendId,
            (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        material.SetFloat(ParticleBlendModeId, multiply ? 1f : 0f);
    }

    private static int GetDepthBelow(Transform root, Transform child)
    {
        int depth = 0;
        Transform current = child;
        while (current != null && current != root)
        {
            depth++;
            current = current.parent;
        }
        return depth;
    }

    private static bool SameCoverage(CoverageData left, CoverageData right) =>
        left == right || left != null && right != null &&
        string.Equals(left.CoverageId, right.CoverageId,
            StringComparison.Ordinal);

    private static void DestroyEffect(Effect effect)
    {
        foreach (Material material in effect.Materials)
            DestroyObject(material);
        DestroyObject(effect.Instance);
    }

    private static void DestroyObject(UnityEngine.Object value)
    {
        if (value == null)
            return;
        if (Application.isPlaying)
            UnityEngine.Object.Destroy(value);
        else
            UnityEngine.Object.DestroyImmediate(value);
    }
}

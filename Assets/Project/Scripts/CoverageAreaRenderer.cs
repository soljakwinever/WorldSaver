using System;
using System.Collections.Generic;
using Project.Scripts;
using Project.Scripts.Core;
using Project.Scripts.DataTypes;
using UnityEngine;

/// <summary>
/// Stitches visible per-chunk coverage payloads into one camera-window texture
/// and quad while retaining payloads for the full Chunkloader footprint.
/// </summary>
internal sealed class CoverageAreaRenderer : IDisposable
{
    internal const string SortingLayerName = "Default";
    internal const int CoverageSortingOrder = -3;
    internal const int ParticleSortingOrder = -2;

    private static readonly int CoverageTransitionId =
        Shader.PropertyToID("_CoverageTransition");
    private static readonly int CoverageParticleSlotId =
        Shader.PropertyToID("_CoverageParticleSlot");

    private sealed class Payload
    {
        public readonly Color32[] Pixels = new Color32[
            (ChunkBuildResult.ChunkSize + 2) *
            (ChunkBuildResult.ChunkSize + 2)];
        public readonly CoverageData[] Slots = new CoverageData[4];
        public bool Active;
    }

    private readonly Transform _owner;
    private readonly WorldData _worldData;
    private readonly WorldClock _worldClock;
    private readonly Camera _camera;
    private readonly Dictionary<Vector2Int, Payload> _payloads = new();
    private readonly Dictionary<CoverageData, float> _candidateMaximums = new();
    private readonly List<CoverageData> _candidates = new();
    private readonly CoverageData[] _slots = new CoverageData[4];
    private readonly CoverageData[] _selectedSlots = new CoverageData[4];
    private readonly CoverageData[] _previousSlots = new CoverageData[4];
    private readonly Vector4[] _rects = new Vector4[4];
    private readonly Vector4[] _tilings = new Vector4[4];
    private readonly Vector4[] _colors = new Vector4[4];
    private readonly HashSet<string> _atlasWarnings = new();
    private readonly HashSet<CoverageData> _loadedCoverages = new();

    private Vector2Int _loaderOriginChunk;
    private int _loaderDiameter;
    private Vector2Int _originChunk;
    private int _chunkWidth;
    private int _chunkHeight;
    private int _areaCellsX;
    private int _areaCellsY;
    private Color32[] _combinedPixels;
    private Color32[] _previousPixels;
    private Color32[] _remappedPixels;
    private Texture2D _mask;
    private Texture2D _previousMask;
    private MeshRenderer _renderer;
    private Material _material;
    private MaterialPropertyBlock _properties;
    private Mesh _mesh;
    private GameObject _host;
    private float _transition = 1f;
    private double _transitionStartTime;
    private double _transitionEndTime;
    private bool _dirty;
    private bool _pendingTransition;
    private bool _loadedCoverageDirty = true;

    public CoverageAreaRenderer(
        Transform owner,
        WorldData worldData,
        WorldClock worldClock,
        Camera camera,
        Vector2Int center,
        int loadDistance,
        Material material = null)
    {
        _owner = owner;
        _worldData = worldData;
        _worldClock = worldClock;
        _camera = camera;
        _material = material != null ? new Material(material) : null;
        EnsureObjects();
        SetFootprint(center, loadDistance);
        FlushPending();
    }

    public void SetFootprint(Vector2Int center, int loadDistance)
    {
        int diameter = Mathf.Max(1, loadDistance * 2);
        Vector2Int origin = center - Vector2Int.one * loadDistance;
        bool loaderChanged = _loaderDiameter != diameter ||
                             _loaderOriginChunk != origin;
        _loaderDiameter = diameter;
        _loaderOriginChunk = origin;
        UpdateCameraWindow(loaderChanged);
    }

    public void Submit(
        Vector2Int position,
        Color32[] pixels,
        CoverageData[] slots,
        bool transition = false)
    {
        if (pixels == null || slots == null)
            return;

        if (!_payloads.TryGetValue(position, out Payload payload))
        {
            payload = new Payload();
            _payloads.Add(position, payload);
        }

        Array.Copy(pixels, payload.Pixels,
            Mathf.Min(pixels.Length, payload.Pixels.Length));
        Array.Clear(payload.Slots, 0, payload.Slots.Length);
        Array.Copy(slots, payload.Slots,
            Mathf.Min(slots.Length, payload.Slots.Length));
        _loadedCoverageDirty = true;
        if (payload.Active && Contains(position))
            MarkDirty(transition);
    }

    public void Tick()
    {
        UpdateCameraWindow(force: false);
        RefreshLoadedCoverages();
        FlushPending();

        if (_transition >= 1f || _renderer == null)
            return;

        UpdateTransitionProgress(GetCurrentTickTime());

        _properties.SetFloat(CoverageTransitionId, _transition);
        _renderer.SetPropertyBlock(_properties);
    }

    public void SetActive(Vector2Int position, bool active)
    {
        if (!_payloads.TryGetValue(position, out Payload payload))
            return;
        if (payload.Active == active)
            return;
        payload.Active = active;
        _loadedCoverageDirty = true;
        if (Contains(position))
            MarkDirty(transition: false);
    }

    public void Remove(Vector2Int position)
    {
        bool visible = Contains(position);
        if (!_payloads.Remove(position))
            return;
        _loadedCoverageDirty = true;
        if (visible)
            MarkDirty(transition: false);
    }

    public bool HasLoadedCoverage(CoverageData coverage)
    {
        RefreshLoadedCoverages();
        return coverage != null && _loadedCoverages.Contains(coverage);
    }

    internal void GetVisibleChunkBoundsClamped(
        out Vector2Int minimum,
        out Vector2Int maximumExclusive)
    {
        GetVisibleChunkBounds(out minimum, out maximumExclusive);
        Vector2Int loaderMaximum = _loaderOriginChunk +
                                   Vector2Int.one * _loaderDiameter;
        minimum = Vector2Int.Max(minimum, _loaderOriginChunk);
        maximumExclusive = Vector2Int.Min(
            maximumExclusive, loaderMaximum);
        maximumExclusive = Vector2Int.Max(
            maximumExclusive, minimum);
    }

    public bool TryGetDisplayedCoverage(
        Vector3 worldPosition,
        out CoverageData coverage,
        out float amount)
    {
        coverage = null;
        amount = 0f;
        if (_combinedPixels == null || _previousPixels == null ||
            _areaCellsX <= 0 || _areaCellsY <= 0)
            return false;

        Vector2 origin = (Vector2)_originChunk * ChunkBuildResult.ChunkSize;
        int localX = Mathf.FloorToInt(worldPosition.x - origin.x);
        int localY = Mathf.FloorToInt(worldPosition.y - origin.y);
        if (localX < 0 || localY < 0 ||
            localX >= _areaCellsX || localY >= _areaCellsY)
            return false;

        int index = localX + 1 +
                    (localY + 1) * (_areaCellsX + 2);
        Color32 previous = _previousPixels[index];
        Color32 target = _combinedPixels[index];
        Vector4 priorities = GetPriorities();
        float winningPriority = -100001f;
        for (int slot = 0; slot < _slots.Length; slot++)
        {
            CoverageData candidate = _slots[slot];
            if (candidate == null)
                continue;
            float previousAmount = GetChannel(previous, slot) / 255f;
            float targetAmount = GetChannel(target, slot) / 255f;
            float candidateAmount = Mathf.Lerp(
                previousAmount, targetAmount, _transition);
            if (candidateAmount <= 0.001f ||
                priorities[slot] <= winningPriority)
                continue;
            coverage = candidate;
            amount = candidateAmount;
            winningPriority = priorities[slot];
        }
        return coverage != null;
    }

    public bool ApplyParticleMaskProperties(
        ParticleSystemRenderer renderer,
        CoverageData coverage,
        MaterialPropertyBlock properties)
    {
        if (renderer == null || coverage == null || properties == null ||
            _mask == null || _previousMask == null)
            return false;

        int slot = Array.IndexOf(_slots, coverage);
        if (slot < 0)
        {
            for (int i = 0; i < _slots.Length; i++)
            {
                if (_slots[i] != null && string.Equals(
                        _slots[i].CoverageId,
                        coverage.CoverageId,
                        StringComparison.Ordinal))
                {
                    slot = i;
                    break;
                }
            }
        }
        if (slot < 0)
            return false;

        renderer.GetPropertyBlock(properties);
        properties.SetTexture("_CoverageMask", _mask);
        properties.SetTexture("_CoveragePreviousMask", _previousMask);
        properties.SetFloat(CoverageTransitionId, _transition);
        properties.SetInt(CoverageParticleSlotId, slot);
        properties.SetVector("_CoveragePriorities", GetPriorities());
        Vector2 origin = (Vector2)_originChunk * ChunkBuildResult.ChunkSize;
        properties.SetVector("_CoverageAreaOrigin",
            new Vector4(origin.x, origin.y, 0f, 0f));
        properties.SetVector("_CoverageAreaSize",
            new Vector4(_areaCellsX, _areaCellsY, 0f, 0f));
        properties.SetVector("_CoverageMaskSize",
            new Vector4(_mask.width, _mask.height,
                1f / _mask.width, 1f / _mask.height));
        properties.SetFloat("_CoveragePixelsPerUnit",
            Mathf.Max(1f, _worldData.coveragePixelsPerUnit));
        properties.SetFloat("_CoverageNoiseScale", _worldData.coverageNoiseScale);
        properties.SetFloat("_CoverageDetailScale", _worldData.coverageDetailScale);
        properties.SetFloat("_CoverageDetailStrength", _worldData.coverageDetailStrength);
        properties.SetFloat("_CoverageBlendSoftness", _worldData.coverageBlendSoftness);
        properties.SetFloat("_CoverageAlphaClipThreshold",
            _worldData.coverageAlphaClipThreshold);
        renderer.SetPropertyBlock(properties);
        return true;
    }

    private bool Contains(Vector2Int position) =>
        position.x >= _originChunk.x &&
        position.y >= _originChunk.y &&
        position.x < _originChunk.x + _chunkWidth &&
        position.y < _originChunk.y + _chunkHeight;

    private void UpdateCameraWindow(bool force)
    {
        GetVisibleChunkBounds(
            out Vector2Int visibleMin,
            out Vector2Int visibleMaxExclusive);

        Vector2Int loaderMax = _loaderOriginChunk +
                               Vector2Int.one * _loaderDiameter;
        bool currentValid = _chunkWidth > 0 && _chunkHeight > 0 &&
                            _originChunk.x >= _loaderOriginChunk.x &&
                            _originChunk.y >= _loaderOriginChunk.y &&
                            _originChunk.x + _chunkWidth <= loaderMax.x &&
                            _originChunk.y + _chunkHeight <= loaderMax.y;
        bool visibleContained = currentValid &&
                                visibleMin.x >= _originChunk.x &&
                                visibleMin.y >= _originChunk.y &&
                                visibleMaxExclusive.x <=
                                _originChunk.x + _chunkWidth &&
                                visibleMaxExclusive.y <=
                                _originChunk.y + _chunkHeight;
        if (!force && visibleContained)
            return;

        Vector2Int desiredMin = new(
            Mathf.Max(_loaderOriginChunk.x, visibleMin.x - 1),
            Mathf.Max(_loaderOriginChunk.y, visibleMin.y - 1));
        Vector2Int desiredMax = new(
            Mathf.Min(loaderMax.x, visibleMaxExclusive.x + 1),
            Mathf.Min(loaderMax.y, visibleMaxExclusive.y + 1));

        if (desiredMax.x <= desiredMin.x || desiredMax.y <= desiredMin.y)
        {
            desiredMin = _loaderOriginChunk;
            desiredMax = loaderMax;
        }

        int width = Mathf.Max(1, desiredMax.x - desiredMin.x);
        int height = Mathf.Max(1, desiredMax.y - desiredMin.y);
        if (_originChunk == desiredMin &&
            _chunkWidth == width &&
            _chunkHeight == height)
            return;

        _originChunk = desiredMin;
        _chunkWidth = width;
        _chunkHeight = height;
        _areaCellsX = _chunkWidth * ChunkBuildResult.ChunkSize;
        _areaCellsY = _chunkHeight * ChunkBuildResult.ChunkSize;
        EnsureTexture();
        UpdateTransform();
        MarkDirty(transition: false);
    }

    private void GetVisibleChunkBounds(
        out Vector2Int minimum,
        out Vector2Int maximumExclusive)
    {
        if (_camera == null || !_camera.orthographic)
        {
            minimum = _loaderOriginChunk;
            maximumExclusive = _loaderOriginChunk +
                               Vector2Int.one * _loaderDiameter;
            return;
        }

        float distance = Mathf.Abs(
            _camera.transform.position.z - _host.transform.position.z);
        Vector3 bottomLeft = _camera.ViewportToWorldPoint(
            new Vector3(0f, 0f, distance));
        Vector3 topRight = _camera.ViewportToWorldPoint(
            new Vector3(1f, 1f, distance));
        float minimumX = Mathf.Min(bottomLeft.x, topRight.x);
        float minimumY = Mathf.Min(bottomLeft.y, topRight.y);
        float maximumX = Mathf.Max(bottomLeft.x, topRight.x);
        float maximumY = Mathf.Max(bottomLeft.y, topRight.y);
        float chunkSize = ChunkBuildResult.ChunkSize;
        minimum = new Vector2Int(
            Mathf.FloorToInt(minimumX / chunkSize),
            Mathf.FloorToInt(minimumY / chunkSize));
        maximumExclusive = new Vector2Int(
            Mathf.CeilToInt(maximumX / chunkSize),
            Mathf.CeilToInt(maximumY / chunkSize));
        maximumExclusive = Vector2Int.Max(
            maximumExclusive, minimum + Vector2Int.one);
    }

    private void MarkDirty(bool transition)
    {
        _dirty = true;
        if (transition)
            _pendingTransition = true;
    }

    private void FlushPending()
    {
        if (!_dirty)
            return;

        // A value-bearing transition must win over structural dirty events
        // submitted during the same frame. Otherwise chunk activation can
        // silently turn an accumulation update into an immediate rebuild.
        bool transition = _pendingTransition;
        _dirty = false;
        _pendingTransition = false;
        Rebuild(transition);
    }

    private void Rebuild(bool transition = false)
    {
        if (_combinedPixels == null || _previousPixels == null ||
            _mask == null || _previousMask == null)
            return;

        double currentTime = GetCurrentTickTime();
        UpdateTransitionProgress(currentTime);
        bool continueActiveTransition = !transition && _transition < 1f &&
                                        currentTime < _transitionEndTime;
        double retainedEndTime = _transitionEndTime;
        CaptureCurrentVisual();
        SelectSlots();
        RemapPreviousPixels();
        Array.Clear(_combinedPixels, 0, _combinedPixels.Length);
        int textureWidth = _areaCellsX + 2;
        int textureHeight = _areaCellsY + 2;
        int chunkSize = ChunkBuildResult.ChunkSize;
        foreach (KeyValuePair<Vector2Int, Payload> pair in _payloads)
        {
            if (!pair.Value.Active || !Contains(pair.Key))
                continue;

            int destinationX = (pair.Key.x - _originChunk.x) * chunkSize + 1;
            int destinationY = (pair.Key.y - _originChunk.y) * chunkSize + 1;
            for (int y = 0; y < chunkSize; y++)
            {
                for (int x = 0; x < chunkSize; x++)
                {
                    Color32 source = pair.Value.Pixels[
                        x + 1 + (y + 1) * (chunkSize + 2)];
                    Color32 value = default;
                    value.r = Remap(source, pair.Value.Slots, _slots[0]);
                    value.g = Remap(source, pair.Value.Slots, _slots[1]);
                    value.b = Remap(source, pair.Value.Slots, _slots[2]);
                    value.a = Remap(source, pair.Value.Slots, _slots[3]);
                    _combinedPixels[destinationX + x +
                        (destinationY + y) * textureWidth] = value;
                }
            }
        }

        for (int x = 1; x <= _areaCellsX; x++)
        {
            _combinedPixels[x] = _combinedPixels[x + textureWidth];
            _combinedPixels[x + (textureHeight - 1) * textureWidth] =
                _combinedPixels[x + (textureHeight - 2) * textureWidth];
        }
        for (int y = 0; y < textureHeight; y++)
        {
            _combinedPixels[y * textureWidth] =
                _combinedPixels[1 + y * textureWidth];
            _combinedPixels[textureWidth - 1 + y * textureWidth] =
                _combinedPixels[textureWidth - 2 + y * textureWidth];
        }

        bool shouldTransition = transition || continueActiveTransition;
        if (!shouldTransition)
            Array.Copy(_combinedPixels, _previousPixels, _combinedPixels.Length);

        UploadMask(_previousMask, _previousPixels);
        UploadMask(_mask, _combinedPixels);
        _transition = shouldTransition ? 0f : 1f;
        if (transition)
        {
            _transitionStartTime = currentTime;
            _transitionEndTime = currentTime + Math.Max(
                1L, _worldData.coverageUpdateIntervalTicks);
        }
        else if (continueActiveTransition)
        {
            _transitionStartTime = currentTime;
            _transitionEndTime = retainedEndTime;
        }
        else
        {
            _transitionStartTime = currentTime;
            _transitionEndTime = currentTime;
        }
        ApplyProperties();
    }

    private double GetCurrentTickTime() =>
        _worldClock != null
            ? _worldClock.CurrentTickTime
            : _transitionEndTime;

    private void UpdateTransitionProgress(double currentTime)
    {
        if (_transition >= 1f)
            return;
        double duration = _transitionEndTime - _transitionStartTime;
        if (duration <= 0.0 || currentTime >= _transitionEndTime)
        {
            _transition = 1f;
            return;
        }
        if (currentTime <= _transitionStartTime)
        {
            _transition = 0f;
            return;
        }
        _transition = (float)Math.Min(
            1.0,
            (currentTime - _transitionStartTime) / duration);
    }

    private void CaptureCurrentVisual()
    {
        if (_transition >= 1f)
        {
            Array.Copy(_combinedPixels, _previousPixels, _combinedPixels.Length);
            return;
        }

        float blend = Mathf.Clamp01(_transition);
        for (int i = 0; i < _combinedPixels.Length; i++)
        {
            Color32 previous = _previousPixels[i];
            Color32 target = _combinedPixels[i];
            _previousPixels[i] = new Color32(
                LerpByte(previous.r, target.r, blend),
                LerpByte(previous.g, target.g, blend),
                LerpByte(previous.b, target.b, blend),
                LerpByte(previous.a, target.a, blend));
        }
    }

    private void RemapPreviousPixels()
    {
        bool slotsChanged = false;
        for (int i = 0; i < 4; i++)
            slotsChanged |= _previousSlots[i] != _slots[i];
        if (!slotsChanged)
            return;

        for (int i = 0; i < _previousPixels.Length; i++)
        {
            Color32 source = _previousPixels[i];
            _remappedPixels[i] = new Color32(
                Remap(source, _previousSlots, _slots[0]),
                Remap(source, _previousSlots, _slots[1]),
                Remap(source, _previousSlots, _slots[2]),
                Remap(source, _previousSlots, _slots[3]));
        }
        Array.Copy(_remappedPixels, _previousPixels, _previousPixels.Length);
    }

    private static byte LerpByte(byte from, byte to, float amount) =>
        (byte)Mathf.RoundToInt(Mathf.Lerp(from, to, amount));

    private static void UploadMask(Texture2D texture, Color32[] pixels)
    {
        texture.SetPixels32(pixels);
        texture.Apply(false, false);
    }

    private void RefreshLoadedCoverages()
    {
        if (!_loadedCoverageDirty)
            return;

        _loadedCoverageDirty = false;
        _loadedCoverages.Clear();
        foreach (Payload payload in _payloads.Values)
        {
            if (!payload.Active)
                continue;
            for (int slot = 0; slot < payload.Slots.Length; slot++)
            {
                CoverageData data = payload.Slots[slot];
                if (data != null && GetMaximum(payload.Pixels, slot) > 0f)
                    _loadedCoverages.Add(data);
            }
        }
    }

    private void SelectSlots()
    {
        _candidateMaximums.Clear();
        foreach (KeyValuePair<Vector2Int, Payload> pair in _payloads)
        {
            if (!pair.Value.Active || !Contains(pair.Key))
                continue;
            for (int slot = 0; slot < 4; slot++)
            {
                CoverageData data = pair.Value.Slots[slot];
                if (data == null)
                    continue;
                float maximum = GetMaximum(pair.Value.Pixels, slot);
                if (!_candidateMaximums.TryGetValue(data, out float previous) ||
                    maximum > previous)
                    _candidateMaximums[data] = maximum;
            }
        }

        _candidates.Clear();
        _candidates.AddRange(_candidateMaximums.Keys);
        _candidates.Sort((left, right) =>
        {
            int priority = right.RenderPriority.CompareTo(left.RenderPriority);
            if (priority != 0) return priority;
            int amount = _candidateMaximums[right].CompareTo(
                _candidateMaximums[left]);
            return amount != 0 ? amount : string.CompareOrdinal(
                left.CoverageId, right.CoverageId);
        });

        Array.Clear(_selectedSlots, 0, 4);
        for (int i = 0; i < Mathf.Min(4, _candidates.Count); i++)
            _selectedSlots[i] = _candidates[i];
        Array.Copy(_slots, _previousSlots, 4);
        Array.Clear(_slots, 0, 4);
        for (int slot = 0; slot < 4; slot++)
        {
            CoverageData retained = _previousSlots[slot];
            int selected = Array.IndexOf(_selectedSlots, retained);
            if (retained == null || selected < 0) continue;
            _slots[slot] = retained;
            _selectedSlots[selected] = null;
        }
        for (int i = 0; i < 4; i++)
        {
            if (_selectedSlots[i] == null) continue;
            int empty = Array.IndexOf(_slots, null);
            if (empty >= 0) _slots[empty] = _selectedSlots[i];
        }
    }

    private static float GetMaximum(Color32[] pixels, int channel)
    {
        byte maximum = 0;
        foreach (Color32 pixel in pixels)
        {
            byte value = channel == 0 ? pixel.r : channel == 1 ? pixel.g :
                channel == 2 ? pixel.b : pixel.a;
            if (value > maximum) maximum = value;
        }
        return maximum / 255f;
    }

    private static byte Remap(
        Color32 source,
        CoverageData[] sourceSlots,
        CoverageData target)
    {
        if (target == null) return 0;
        for (int i = 0; i < 4; i++)
        {
            CoverageData candidate = sourceSlots[i];
            if (candidate == target || candidate != null && string.Equals(
                    candidate.CoverageId, target.CoverageId,
                    StringComparison.Ordinal))
                return i == 0 ? source.r : i == 1 ? source.g :
                    i == 2 ? source.b : source.a;
        }
        return 0;
    }

    private static byte GetChannel(Color32 value, int channel) =>
        channel == 0 ? value.r : channel == 1 ? value.g :
        channel == 2 ? value.b : value.a;

    private void ApplyProperties()
    {
        Texture2D atlas = null;
        Vector4 priorities = GetPriorities();
        for (int i = 0; i < 4; i++)
        {
            Sprite sprite = _slots[i]?.CoverageSprite;
            if (atlas == null && sprite != null) atlas = sprite.texture;
        }
        bool visible = false;
        for (int i = 0; i < 4; i++)
        {
            CoverageData data = _slots[i];
            Sprite sprite = data?.CoverageSprite;
            bool compatible = sprite != null && sprite.texture == atlas;
            _rects[i] = compatible ? GetSpriteRect(sprite) : Vector4.zero;
            Vector2 tiling = data?.CoverageTiling ?? Vector2.one;
            _tilings[i] = new Vector4(tiling.x, tiling.y, 0f, 0f);
            _colors[i] = data?.CoverageColor ?? Color.clear;
            if (!compatible)
            {
                if (data != null && sprite != null && atlas != null &&
                    _atlasWarnings.Add(data.CoverageId))
                    Debug.LogError($"Coverage '{data.CoverageId}' does not use " +
                        $"the active atlas '{atlas.name}'.", data);
                continue;
            }
            visible = true;
        }

        _properties ??= new MaterialPropertyBlock();
        _properties.Clear();
        _properties.SetTexture("_CoverageMask", _mask);
        _properties.SetTexture("_CoveragePreviousMask", _previousMask);
        _properties.SetFloat(CoverageTransitionId, _transition);
        _properties.SetTexture("_CoverageAtlas",
            atlas != null ? atlas : Texture2D.whiteTexture);
        _properties.SetVectorArray("_CoverageRects", _rects);
        _properties.SetVectorArray("_CoverageTilings", _tilings);
        _properties.SetVectorArray("_CoverageColors", _colors);
        _properties.SetVector("_CoveragePriorities", priorities);
        Vector2 worldOrigin = (Vector2)_originChunk * ChunkBuildResult.ChunkSize;
        _properties.SetVector("_CoverageAreaOrigin",
            new Vector4(worldOrigin.x, worldOrigin.y, 0f, 0f));
        _properties.SetVector("_CoverageAreaSize",
            new Vector4(_areaCellsX, _areaCellsY, 0f, 0f));
        _properties.SetVector("_CoverageMaskSize",
            new Vector4(_mask.width, _mask.height,
                1f / _mask.width, 1f / _mask.height));
        _properties.SetFloat("_CoveragePixelsPerUnit",
            Mathf.Max(1f, _worldData.coveragePixelsPerUnit));
        _properties.SetFloat("_CoverageNoiseScale", _worldData.coverageNoiseScale);
        _properties.SetFloat("_CoverageDetailScale", _worldData.coverageDetailScale);
        _properties.SetFloat("_CoverageDetailStrength", _worldData.coverageDetailStrength);
        _properties.SetFloat("_CoverageBlendSoftness", _worldData.coverageBlendSoftness);
        _properties.SetFloat("_CoverageAlphaClipThreshold",
            _worldData.coverageAlphaClipThreshold);
        _renderer.SetPropertyBlock(_properties);
        _renderer.enabled = visible;
    }

    private Vector4 GetPriorities()
    {
        Vector4 priorities = new(
            -100000f, -100000f, -100000f, -100000f);
        for (int i = 0; i < _slots.Length; i++)
        {
            CoverageData data = _slots[i];
            if (data == null)
                continue;
            int lexicalRank = 0;
            for (int other = 0; other < _slots.Length; other++)
            {
                if (_slots[other] != null && string.CompareOrdinal(
                        _slots[other].CoverageId, data.CoverageId) < 0)
                    lexicalRank++;
            }
            priorities[i] = data.RenderPriority * 8f + (4 - lexicalRank);
        }
        return priorities;
    }

    private static Vector4 GetSpriteRect(Sprite sprite)
    {
        Rect rect = sprite.textureRect;
        Texture2D texture = sprite.texture;
        return new Vector4(
            (rect.x + 0.5f) / texture.width,
            (rect.y + 0.5f) / texture.height,
            Mathf.Max(0f, rect.width - 1f) / texture.width,
            Mathf.Max(0f, rect.height - 1f) / texture.height);
    }

    private void EnsureObjects()
    {
        _host = new GameObject("Coverage Area");
        _host.transform.SetParent(_owner, true);
        MeshFilter filter = _host.AddComponent<MeshFilter>();
        _renderer = _host.AddComponent<MeshRenderer>();
        _mesh = new Mesh { name = "Coverage Area Quad" };
        _mesh.vertices = new[] { new Vector3(-0.5f, -0.5f),
            new Vector3(0.5f, -0.5f), new Vector3(0.5f, 0.5f),
            new Vector3(-0.5f, 0.5f) };
        _mesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.one,
            Vector2.up };
        _mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
        _mesh.RecalculateBounds();
        filter.sharedMesh = _mesh;
        if (!_material)
        {
            Material template = Resources.Load<Material>("CoverageChunk");
            Shader shader = template != null ? template.shader : Shader.Find("WorldSaver/CoverageChunk");
            _material = template != null ? new Material(template) :
                shader != null ? new Material(shader) : null;
        }

        if (_material == null)
        {
            Debug.LogError("Missing WorldSaver/CoverageChunk shader.");
            _renderer.enabled = false;
            return;
        }
        _material.hideFlags = HideFlags.HideAndDontSave;
        _renderer.sharedMaterial = _material;
        _renderer.sortingLayerName = SortingLayerName;
        _renderer.sortingOrder = CoverageSortingOrder;
    }

    private void EnsureTexture()
    {
        int width = _areaCellsX + 2;
        int height = _areaCellsY + 2;
        if (_mask != null &&
            _mask.width == width &&
            _mask.height == height)
            return;
        DestroyObject(_mask);
        DestroyObject(_previousMask);
        _mask = new Texture2D(
            width, height, TextureFormat.RGBA32, false, true)
        {
            name = "Chunkloader Coverage Mask",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave
        };
        _previousMask = new Texture2D(
            width, height, TextureFormat.RGBA32, false, true)
        {
            name = "Chunkloader Previous Coverage Mask",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave
        };
        int pixelCount = width * height;
        _combinedPixels = new Color32[pixelCount];
        _previousPixels = new Color32[pixelCount];
        _remappedPixels = new Color32[pixelCount];
        _transition = 1f;
    }

    private void UpdateTransform()
    {
        Vector2 origin = (Vector2)_originChunk * ChunkBuildResult.ChunkSize;
        _host.transform.position = new Vector3(
            origin.x + _areaCellsX * 0.5f,
            origin.y + _areaCellsY * 0.5f, 0f);
        _host.transform.localScale = new Vector3(
            _areaCellsX, _areaCellsY, 1f);
    }

    public void Dispose()
    {
        DestroyObject(_mask);
        DestroyObject(_previousMask);
        DestroyObject(_material);
        DestroyObject(_mesh);
        DestroyObject(_host);
    }

    private static void DestroyObject(UnityEngine.Object value)
    {
        if (value == null) return;
        if (Application.isPlaying) UnityEngine.Object.Destroy(value);
        else UnityEngine.Object.DestroyImmediate(value);
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Project.Scripts;
using Project.Scripts.DataTypes;
using UnityEngine;
using UnityEngine.Tilemaps;
using UnityEngine.UI;
using ProjectTileData = Project.Scripts.DataTypes.TileData;

namespace Project.UI.MainMenu
{
    public sealed class MainMenuWorldPreviewController : MonoBehaviour
    {
        [Header("Generation")]
        [SerializeField] private WorldData worldData;
        [SerializeField, Min(2)] private int chunkCount = 4;
        [SerializeField] private Vector2Int coordinateRange = new(-1000, 1000);
        [SerializeField, Min(1)] private int maximumCandidateAttempts = 12;
        [SerializeField, Min(1f)] private float minimumCandidateSeparation = 160f;

        [Header("Candidate Quality")]
        [SerializeField, Min(0)] private int minimumVisibleProps = 3;
        [SerializeField, Range(0f, 1f)] private float minimumWaterFraction = 0.04f;
        [SerializeField, Range(0f, 1f)] private float maximumWaterFraction = 0.88f;
        [SerializeField, Min(0f)] private float minimumHeightRange = 0.07f;

        [Header("Presentation")]
        [SerializeField] private Camera previewCamera;
        [SerializeField] private RawImage backgroundImage;
        [SerializeField] private Material simpleWaterMaterial;
        [SerializeField, Min(0.1f)] private float displaySeconds = 6f;
        [SerializeField, Range(0.05f, 0.95f)]
        private float crossfadeStartNormalized = 0.75f;
        [SerializeField] private Vector2 panOffset = new(4.8f, -3.3f);
        [SerializeField, Min(1f)] private float loadEstimateSafetyMultiplier = 1.15f;
        [SerializeField, Min(0.1f)]
        private float presentationBudgetMilliseconds = 2f;
        [SerializeField, Min(0.1f)]
        private float initialPresentationBudgetMilliseconds = 12f;

        [Header("Ambient Agents")]
        [SerializeField, Min(0)] private int minimumAgents = 3;
        [SerializeField, Min(0)] private int maximumAgents = 6;

        private sealed class PreviewSlot
        {
            public WorldGeneration Generation;
            public Transform Root;
            public Camera Camera;
            public RenderTexture Texture;
            public Vector2Int Position;
            public BoundsInt AgentBounds;
            public Vector3 PanStart;
            public Vector3 PanEnd;
            public float AnimationProgress;
            public LoadTimingSample LoadTiming;
            public readonly Dictionary<Vector2Int, TerrainSample> Terrain = new();
            public bool Ready;
        }

        private readonly struct LoadTimingSample
        {
            public readonly int StartFrame;
            public readonly int EndFrame;
            public readonly double StartSeconds;
            public readonly double EndSeconds;
            public int Frames => Mathf.Max(0, EndFrame - StartFrame);
            public float Seconds => Mathf.Max(0f, (float)(EndSeconds - StartSeconds));

            public LoadTimingSample(int startFrame, double startSeconds,
                int endFrame, double endSeconds)
            {
                StartFrame = startFrame;
                StartSeconds = startSeconds;
                EndFrame = endFrame;
                EndSeconds = endSeconds;
            }
        }

        private readonly PreviewSlot[] _slots = { new(), new() };
        private WorldGenerationPresetData _preset;
        private CancellationTokenSource _cancellation;
        private int _activeIndex;
        private float _incomingAlpha;
        private bool _hasActive;
        private int _textureWidth;
        private int _textureHeight;
        private Material _waterMaterialInstance;
        private Material _crossfadeMaterial;
        [SerializeField] private int latestLoadFrames;
        [SerializeField] private float latestLoadSeconds;
        [SerializeField] private float estimatedLoadSeconds;

        public float BackdropAlpha => _hasActive ? 0.12f : 1f;

        public void Initialize(WorldGenerationPresetData preset)
        {
            _preset = preset;
            previewCamera ??= Camera.main != null
                ? Camera.main
                : FindAnyObjectByType<Camera>();
            if (worldData == null || _preset == null || previewCamera == null)
            {
                Debug.LogError(
                    "Main-menu preview cannot initialize: " +
                    $"WorldData={worldData != null}, preset={_preset != null}, " +
                    $"camera={previewCamera != null}.", this);
                enabled = false;
                return;
            }

            if (simpleWaterMaterial != null)
                _waterMaterialInstance = new Material(simpleWaterMaterial);
            Shader crossfadeShader = Shader.Find(
                "Hidden/WorldSaver/Main Menu Preview Crossfade");
            if (crossfadeShader != null)
                _crossfadeMaterial = new Material(crossfadeShader);
            else
                Debug.LogError("Main-menu preview crossfade shader was not found.", this);
            previewCamera.enabled = false;
            if (backgroundImage != null)
            {
                backgroundImage.raycastTarget = false;
                backgroundImage.material = _crossfadeMaterial;
                backgroundImage.color = Color.white;
            }
            else
                Debug.LogError("Main-menu preview RawImage is not assigned.", this);
            _cancellation = new CancellationTokenSource();
            EnsureRenderTargets();
            UpdateBackgroundImage();
            StartCoroutine(PreviewLoop());
        }

        private void LateUpdate()
        {
            UpdateBackgroundImage();
        }

        private void UpdateBackgroundImage()
        {
            if (backgroundImage == null || !_hasActive)
                return;
            PreviewSlot active = _slots[_activeIndex];
            PreviewSlot incoming = _slots[1 - _activeIndex];
            if (active.Texture == null)
                return;
            backgroundImage.texture = active.Texture;
            if (_crossfadeMaterial == null)
                return;
            _crossfadeMaterial.SetTexture(
                "_ToTex",
                incoming.Texture != null ? incoming.Texture : active.Texture);
            _crossfadeMaterial.SetFloat("_Blend", _incomingAlpha);
        }

        private IEnumerator PreviewLoop()
        {
            yield return PrepareSlot(0, null, useInitialBudget: true);
            if (!_slots[0].Ready)
                yield break;
            _activeIndex = 0;
            _hasActive = true;

            while (enabled)
            {
                EnsureRenderTargets();
                int incomingIndex = 1 - _activeIndex;
                StartCoroutine(PrepareSlot(
                    incomingIndex,
                    _slots[_activeIndex],
                    useInitialBudget: false));

                PreviewSlot active = _slots[_activeIndex];
                active.AnimationProgress = 0f;
                float threshold = Mathf.Clamp(crossfadeStartNormalized, 0.05f, 0.95f);
                float predictedLoad = Mathf.Max(latestLoadSeconds, estimatedLoadSeconds);
                float movementDuration = Mathf.Max(
                    Mathf.Max(0.1f, displaySeconds),
                    predictedLoad * Mathf.Max(1f, loadEstimateSafetyMultiplier) / threshold);

                while (active.AnimationProgress < 1f)
                {
                    float delta = Mathf.Max(0f, Time.unscaledDeltaTime);
                    float next = active.AnimationProgress + delta / movementDuration;
                    if (!_slots[incomingIndex].Ready && next >= threshold)
                    {
                        // An unexpectedly slow load approaches the fade boundary
                        // asymptotically instead of visibly stopping there.
                        float limit = threshold - 0.0001f;
                        float blend = 1f - Mathf.Exp(-2f * delta);
                        next = Mathf.Lerp(active.AnimationProgress, limit, blend);
                    }

                    active.AnimationProgress = Mathf.Clamp01(next);
                    SetPanProgress(active, active.AnimationProgress);
                    _incomingAlpha = _slots[incomingIndex].Ready
                        ? Mathf.Clamp01((active.AnimationProgress - threshold) /
                                        Mathf.Max(0.0001f, 1f - threshold))
                        : 0f;
                    yield return null;
                }

                SetPanProgress(active, 1f);
                _incomingAlpha = 1f;
                yield return null;

                int oldIndex = _activeIndex;
                _activeIndex = incomingIndex;
                _incomingAlpha = 0f;
                ClearSlot(_slots[oldIndex], keepCamera: true);
            }
        }

        private IEnumerator PrepareSlot(
            int index,
            PreviewSlot current,
            bool useInitialBudget)
        {
            int startFrame = Time.frameCount;
            double startSeconds = Time.realtimeSinceStartupAsDouble;
            PreviewSlot slot = _slots[index];
            ClearSlot(slot, keepCamera: true);
            Candidate best = default;
            int attempts = Mathf.Max(1, maximumCandidateAttempts);
            for (int attempt = 0; attempt < attempts; attempt++)
            {
                Vector2Int position = RandomPosition(current);
                int seed = UnityEngine.Random.Range(0, int.MaxValue);
                WorldGeneration generation = new(
                    worldData, new WorldGenerationSelection(seed, _preset), null);
                Task<List<ChunkBuildResult>> build = BuildAreaAsync(
                    position, generation, Mathf.Max(2, chunkCount),
                    _cancellation.Token);
                while (!build.IsCompleted)
                    yield return null;
                if (build.IsCanceled || _cancellation.IsCancellationRequested)
                    yield break;
                if (build.IsFaulted)
                {
                    Debug.LogException(build.Exception?.GetBaseException() ?? build.Exception, this);
                    continue;
                }

                Candidate candidate = ScoreCandidate(position, generation, build.Result);
                if (!best.HasValue || candidate.Score > best.Score)
                    best = candidate;
                if (candidate.Accepted)
                {
                    yield return BuildSlot(
                        slot,
                        candidate,
                        useInitialBudget
                            ? initialPresentationBudgetMilliseconds
                            : presentationBudgetMilliseconds);
                    RecordLoadTiming(slot, startFrame, startSeconds);
                    yield break;
                }
            }

            if (best.HasValue)
                yield return BuildSlot(
                    slot,
                    best,
                    useInitialBudget
                        ? initialPresentationBudgetMilliseconds
                        : presentationBudgetMilliseconds);
            if (slot.Ready)
                RecordLoadTiming(slot, startFrame, startSeconds);
            else
                Debug.LogError("Main-menu preview could not build any candidate.", this);
        }

        private readonly struct Candidate
        {
            public readonly Vector2Int Position;
            public readonly WorldGeneration Generation;
            public readonly List<ChunkBuildResult> Chunks;
            public readonly float Score;
            public readonly bool Accepted;
            public bool HasValue => Chunks != null;

            public Candidate(Vector2Int position, WorldGeneration generation,
                List<ChunkBuildResult> chunks, float score, bool accepted)
            {
                Position = position;
                Generation = generation;
                Chunks = chunks;
                Score = score;
                Accepted = accepted;
            }
        }

        private Candidate ScoreCandidate(Vector2Int position,
            WorldGeneration generation, List<ChunkBuildResult> chunks)
        {
            float halfHeight = previewCamera.orthographicSize +
                               Mathf.Abs(panOffset.y);
            float halfWidth = halfHeight * Mathf.Max(0.1f, previewCamera.aspect) +
                              Mathf.Abs(panOffset.x);
            Rect visible = new(position.x - halfWidth, position.y - halfHeight,
                halfWidth * 2f, halfHeight * 2f);
            int cells = 0, water = 0, special = 0, props = 0;
            float minHeight = float.MaxValue, maxHeight = float.MinValue;
            HashSet<BiomeData> biomes = new();
            foreach (ChunkBuildResult chunk in chunks)
            {
                int startX = chunk.chunkPosition.x * ChunkBuildResult.ChunkSize;
                int startY = chunk.chunkPosition.y * ChunkBuildResult.ChunkSize;
                for (int y = 0; y < ChunkBuildResult.ChunkSize; y++)
                for (int x = 0; x < ChunkBuildResult.ChunkSize; x++)
                {
                    int wx = startX + x, wy = startY + y;
                    if (!visible.Contains(new Vector2(wx + 0.5f, wy + 0.5f)))
                        continue;
                    int cell = chunk.GetTileIndex(x, y);
                    cells++;
                    float height = chunk.heights[cell];
                    minHeight = Mathf.Min(minHeight, height);
                    maxHeight = Mathf.Max(maxHeight, height);
                    if (chunk.terrainKinds[cell] == TerrainKind.Floor &&
                        height <= generation.Elevation.waterHeight) water++;
                    if (chunk.terrainKinds[cell] != TerrainKind.Floor ||
                        chunk.isCliff[cell] || chunk.isRoad[cell]) special++;
                    biomes.Add(chunk.biomeData[cell].dominantBiome);
                }
                props += chunk.props.Count(prop => visible.Contains(prop.position));
            }

            float waterFraction = cells > 0 ? water / (float)cells : 1f;
            float heightRange = cells > 0 ? maxHeight - minHeight : 0f;
            bool varied = special > Mathf.Max(3, cells / 100) ||
                          biomes.Count > 1 || heightRange >= minimumHeightRange ||
                          waterFraction >= minimumWaterFraction &&
                          waterFraction <= maximumWaterFraction;
            bool accepted = waterFraction <= maximumWaterFraction &&
                            (props >= minimumVisibleProps || varied);
            float score = props * 8f + special * 0.05f + biomes.Count * 2f +
                          heightRange * 20f -
                          (waterFraction > maximumWaterFraction ? 100f : 0f);
            return new Candidate(position, generation, chunks, score, accepted);
        }

        private Vector2Int RandomPosition(PreviewSlot current)
        {
            int min = Mathf.Min(coordinateRange.x, coordinateRange.y);
            int max = Mathf.Max(coordinateRange.x, coordinateRange.y);
            Vector2Int candidate;
            int guard = 0;
            do
            {
                candidate = new Vector2Int(
                    UnityEngine.Random.Range(min, max + 1),
                    UnityEngine.Random.Range(min, max + 1));
                guard++;
            } while (current?.Ready == true && guard < 32 &&
                     Vector2.Distance(candidate, current.Position) < minimumCandidateSeparation);
            return candidate;
        }

        private static Task<List<ChunkBuildResult>> BuildAreaAsync(
            Vector2Int position, WorldGeneration generation, int count,
            CancellationToken cancellationToken)
        {
            Vector2Int center = new(
                Mathf.FloorToInt(position.x / (float)ChunkBuildResult.ChunkSize),
                Mathf.FloorToInt(position.y / (float)ChunkBuildResult.ChunkSize));
            return Task.Run(() =>
            {
                int minimum = -count / 2;
                int maximum = minimum + count;
                List<ChunkBuildResult> results = new(count * count);
                for (int y = minimum; y < maximum; y++)
                for (int x = minimum; x < maximum; x++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    Vector2Int chunk = center + new Vector2Int(x, y);
                    PropSpawnRule[] rules = generation.PropSpawnRules
                        .ShuffleXY(chunk.x, chunk.y).ToArray();
                    results.Add(ChunkGenerator.BuildChunk(
                        chunk, generation, rules, cancellationToken));
                }
                return results;
            }, cancellationToken);
        }

        private IEnumerator BuildSlot(
            PreviewSlot slot,
            Candidate candidate,
            float budgetMilliseconds)
        {
            slot.Generation = candidate.Generation;
            slot.Position = candidate.Position;
            slot.AnimationProgress = 0f;
            slot.PanStart = new Vector3(candidate.Position.x, candidate.Position.y, -10f);
            slot.PanEnd = slot.PanStart + new Vector3(panOffset.x, panOffset.y, 0f);
            slot.Root = new GameObject($"Preview {candidate.Position}").transform;
            Grid grid = slot.Root.gameObject.AddComponent<Grid>();
            Tilemap ground = CreateTilemap(grid.transform, "Ground", 0, null);
            Tilemap water = CreateTilemap(grid.transform, "Water", 1, _waterMaterialInstance);
            Tilemap walls = CreateTilemap(grid.transform, "Walls", 2, null);
            yield return Render(
                slot,
                candidate.Chunks,
                ground,
                water,
                walls,
                budgetMilliseconds);
            ConfigureSlotCamera(slot);
            slot.Ready = true;
        }

        private static Tilemap CreateTilemap(Transform parent, string name,
            int order, Material material)
        {
            GameObject child = new(name);
            child.transform.SetParent(parent, false);
            Tilemap map = child.AddComponent<Tilemap>();
            TilemapRenderer renderer = child.AddComponent<TilemapRenderer>();
            renderer.sortingOrder = order;
            if (material != null) renderer.sharedMaterial = material;
            return map;
        }

        private IEnumerator Render(
            PreviewSlot slot,
            List<ChunkBuildResult> chunks,
            Tilemap ground,
            Tilemap water,
            Tilemap walls,
            float budgetMilliseconds)
        {
            Dictionary<string, NodeData> props = slot.Generation.PropSpawnRules
                .Where(rule => rule != null && !string.IsNullOrEmpty(rule.name))
                .GroupBy(rule => rule.name)
                .ToDictionary(group => group.Key, group => group.First().nodeData);
            int minX = int.MaxValue, minY = int.MaxValue;
            int maxX = int.MinValue, maxY = int.MinValue;
            double deadline = NextPresentationDeadline(budgetMilliseconds);
            foreach (ChunkBuildResult chunk in chunks)
            {
                int sx = chunk.chunkPosition.x * ChunkBuildResult.ChunkSize;
                int sy = chunk.chunkPosition.y * ChunkBuildResult.ChunkSize;
                minX = Mathf.Min(minX, sx); minY = Mathf.Min(minY, sy);
                maxX = Mathf.Max(maxX, sx + ChunkBuildResult.ChunkSize);
                maxY = Mathf.Max(maxY, sy + ChunkBuildResult.ChunkSize);
                for (int y = 0; y < ChunkBuildResult.ChunkSize; y++)
                for (int x = 0; x < ChunkBuildResult.ChunkSize; x++)
                {
                    RenderCell(slot, chunk, x, y, sx, sy, ground, water, walls);
                    if (Time.realtimeSinceStartupAsDouble >= deadline)
                    {
                        yield return null;
                        deadline = NextPresentationDeadline(budgetMilliseconds);
                    }
                }
                foreach (PropSpawnData spawn in chunk.props)
                {
                    NodeData data = spawn.nodeData;
                    if (data == null && !string.IsNullOrEmpty(spawn.propName))
                        props.TryGetValue(spawn.propName, out data);
                    if (data != null)
                    {
                        CreateProp(slot, spawn, data);
                        if (Time.realtimeSinceStartupAsDouble >= deadline)
                        {
                            yield return null;
                            deadline = NextPresentationDeadline(budgetMilliseconds);
                        }
                    }
                }
            }
            slot.AgentBounds = new BoundsInt(minX + 3, minY + 3, 0,
                maxX - minX - 6, maxY - minY - 6, 1);
            SpawnAgents(slot, UnityEngine.Random.Range(
                Mathf.Max(0, minimumAgents),
                Mathf.Max(minimumAgents, maximumAgents) + 1));
            yield return null;
        }

        private static double NextPresentationDeadline(float budgetMilliseconds) =>
            Time.realtimeSinceStartupAsDouble +
            Mathf.Max(0.1f, budgetMilliseconds) / 1000d;

        private void RenderCell(PreviewSlot slot, ChunkBuildResult chunk,
            int x, int y, int sx, int sy, Tilemap ground, Tilemap water, Tilemap walls)
        {
            int index = chunk.GetTileIndex(x, y);
            int wx = sx + x, wy = sy + y;
            Vector3Int cell = new(wx, wy, 0);
            TerrainSample sample = chunk.GetTerrainSample(x, y);
            slot.Terrain[new Vector2Int(wx, wy)] = sample;
            BiomeBlend biome = chunk.biomeData[index];
            ProjectTileData tile = chunk.floorTiles[index] ?? SelectTerrainTile(slot, sample, biome);
            bool isWater = sample.terrainKind == TerrainKind.Floor &&
                           sample.height <= slot.Generation.Elevation.waterHeight;
            bool isWall = sample.terrainKind != TerrainKind.Floor || sample.isCliff;
            if (tile != null)
                SetTile(isWall ? walls : ground, cell, tile,
                    SelectTerrainColor(sample, biome) * tile.Color);
            if (isWater)
            {
                ProjectTileData waterTile = biome.dominantBiome.overrideWaterTile ?? FindTile("Water");
                if (waterTile != null)
                {
                    float depth = Mathf.InverseLerp(0f,
                        slot.Generation.Elevation.waterHeight, sample.height);
                    Color payload = WaterTilePayload.Encode(
                        biome.waterColor * waterTile.Color, depth,
                        waterTile.waterTextureIndex);
                    SetTile(water, cell, waterTile, payload);
                }
            }
        }

        private ProjectTileData SelectTerrainTile(PreviewSlot slot,
            TerrainSample sample, BiomeBlend biome)
        {
            if (sample.height < slot.Generation.Elevation.beachHeight)
                return biome.dominantBiome.overrideBeachTile ?? FindTile("Beach");
            if (sample.isCliff || sample.terrainKind != TerrainKind.Floor)
                return biome.dominantBiome.overrideCliffTile ?? FindTile("Wall");
            if (sample.isRoad)
                return biome.dominantBiome.overridePathTile ?? FindTile("Path");
            return biome.dominantBiome.overrideGroundTile ?? FindTile("Grass");
        }

        private static Color SelectTerrainColor(TerrainSample sample, BiomeBlend biome)
        {
            if (sample.isRoad) return biome.pathColor;
            if (sample.isCliff || sample.terrainKind != TerrainKind.Floor) return biome.cliffColor;
            if (sample.isWater) return biome.beachColor;
            return biome.groundColor;
        }

        private ProjectTileData FindTile(string name) =>
            (worldData.tiles ?? Array.Empty<ProjectTileData>()).FirstOrDefault(tile =>
                tile != null && tile.name.IndexOf(name,
                    StringComparison.OrdinalIgnoreCase) >= 0);

        private static void SetTile(Tilemap map, Vector3Int cell,
            ProjectTileData data, Color color)
        {
            if (data?.TileBase == null) return;
            map.SetTile(cell, data.TileBase);
            map.SetTileFlags(cell, TileFlags.None);
            map.SetColor(cell, color);
        }

        private void CreateProp(PreviewSlot slot, PropSpawnData spawn, NodeData data)
        {
            GameObject visual;
            if (data.overrideVisual != null)
            {
                visual = Instantiate(data.overrideVisual, slot.Root);
                StripGameplay(visual);
            }
            else
            {
                visual = new GameObject(data.name);
                visual.transform.SetParent(slot.Root, false);
                SpriteRenderer renderer = visual.AddComponent<SpriteRenderer>();
                renderer.sortingOrder = 3;
                renderer.sprite = SelectSprite(data, spawn.worldPosition);
                renderer.flipX = spawn.flipX;
                if (data.overrideMaterial != null) renderer.material = data.overrideMaterial;
            }
            visual.transform.position = spawn.position;
            visual.transform.localScale = Vector3.one * spawn.scale;
        }

        private static Sprite SelectSprite(NodeData data, Vector2Int cell)
        {
            if (data.sprites == null || data.sprites.Length == 0) return data.sprite;
            int hash = unchecked(cell.x * 73856093 ^ cell.y * 19349663);
            return data.sprites[(hash & int.MaxValue) % data.sprites.Length];
        }

        private void SpawnAgents(PreviewSlot slot, int desired)
        {
            List<EnemySpawnRule> rules = EnumerateRules(slot.Generation)
                .Where(rule => rule?.enemyData?.visual != null ||
                    rule?.variations?.Any(v => v?.enemyData?.visual != null) == true)
                .ToList();
            List<Vector2Int> cells = slot.Terrain.Where(pair =>
                slot.AgentBounds.Contains(new Vector3Int(pair.Key.x, pair.Key.y, 0)) &&
                pair.Value.IsWalkable).Select(pair => pair.Key).ToList();
            for (int i = 0; i < desired && cells.Count > 0 && rules.Count > 0; i++)
            {
                Vector2Int cell = cells[UnityEngine.Random.Range(0, cells.Count)];
                BiomeData biome = slot.Terrain[cell].biome;
                List<EnemySpawnRule> eligible = rules.Where(rule =>
                    (rule.allowedBiomes == null || rule.allowedBiomes.Length == 0 ||
                     Array.IndexOf(rule.allowedBiomes, biome) >= 0) &&
                    (rule.restrictedBiomes == null ||
                     Array.IndexOf(rule.restrictedBiomes, biome) < 0)).ToList();
                if (eligible.Count == 0) continue;
                EnemyData enemy = eligible[UnityEngine.Random.Range(0, eligible.Count)]
                    .SelectEnemyData(UnityEngine.Random.value);
                if (enemy?.visual == null) continue;
                GameObject visual = Instantiate(enemy.visual, slot.Root);
                visual.transform.position = new Vector3(cell.x + 0.5f, cell.y + 0.5f, 0f);
                StripGameplay(visual);
                MainMenuPreviewAgent agent = visual.AddComponent<MainMenuPreviewAgent>();
                agent.Initialize(c => slot.Terrain.TryGetValue(c, out TerrainSample s) && s.IsWalkable,
                    slot.AgentBounds, enemy.movementSpeed * 0.55f);
            }
        }

        private static IEnumerable<EnemySpawnRule> EnumerateRules(WorldGeneration generation)
        {
            HashSet<EnemySpawnRule> seen = new();
            Stack<EnemySpawnRule> pending = new(generation.EnemySpawnRules.Reverse());
            while (pending.Count > 0)
            {
                EnemySpawnRule rule = pending.Pop();
                if (rule == null || !seen.Add(rule)) continue;
                yield return rule;
                foreach (EnemySpawnRule child in
                         (rule.rules ?? Array.Empty<EnemySpawnRule>()).Reverse())
                    pending.Push(child);
            }
        }

        private void ConfigureSlotCamera(PreviewSlot slot)
        {
            if (slot.Camera == null)
            {
                GameObject go = new("Main Menu Preview Camera");
                slot.Camera = go.AddComponent<Camera>();
                slot.Camera.orthographic = true;
                slot.Camera.clearFlags = CameraClearFlags.SolidColor;
                Color background = previewCamera.backgroundColor;
                background.a = 1f;
                slot.Camera.backgroundColor = background;
                slot.Camera.cullingMask = previewCamera.cullingMask;
                slot.Camera.orthographicSize = previewCamera.orthographicSize;
            }
            slot.Camera.targetTexture = slot.Texture;
            slot.Camera.aspect = _textureWidth / (float)Mathf.Max(1, _textureHeight);
            slot.Camera.transform.position = slot.PanStart;
            if (_waterMaterialInstance != null)
                _waterMaterialInstance.SetFloat("_WaterHeight",
                    slot.Generation.Elevation.waterHeight);
        }

        private static void SetPanProgress(PreviewSlot slot, float progress)
        {
            if (slot?.Ready != true || slot.Camera == null) return;
            float smoothed = Mathf.Lerp(0f, 1f, Mathf.Clamp01(progress));
            slot.Camera.transform.position = Vector3.LerpUnclamped(
                slot.PanStart, slot.PanEnd, smoothed);
        }

        private void RecordLoadTiming(PreviewSlot slot, int startFrame,
            double startSeconds)
        {
            slot.LoadTiming = new LoadTimingSample(startFrame, startSeconds,
                Time.frameCount, Time.realtimeSinceStartupAsDouble);
            latestLoadFrames = slot.LoadTiming.Frames;
            latestLoadSeconds = slot.LoadTiming.Seconds;
            estimatedLoadSeconds = estimatedLoadSeconds <= 0f
                ? latestLoadSeconds
                : Mathf.Lerp(estimatedLoadSeconds, latestLoadSeconds, 0.35f);
        }

        private void EnsureRenderTargets()
        {
            int width = Mathf.Max(320, Screen.width);
            int height = Mathf.Max(180, Screen.height);
            if (width == _textureWidth && height == _textureHeight &&
                _slots.All(slot => slot.Texture != null)) return;
            _textureWidth = width; _textureHeight = height;
            foreach (PreviewSlot slot in _slots)
            {
                if (slot.Texture != null)
                {
                    if (slot.Camera != null) slot.Camera.targetTexture = null;
                    slot.Texture.Release();
                    Destroy(slot.Texture);
                }
                slot.Texture = new RenderTexture(width, height, 16,
                    RenderTextureFormat.ARGB32)
                { name = "Main Menu Preview" };
                slot.Texture.Create();
                if (slot.Camera != null)
                {
                    slot.Camera.targetTexture = slot.Texture;
                    slot.Camera.aspect = width / (float)height;
                }
            }
            UpdateBackgroundImage();
        }

        private static void StripGameplay(GameObject visual)
        {
            foreach (MonoBehaviour component in
                     visual.GetComponentsInChildren<MonoBehaviour>(true))
                component.enabled = false;
            foreach (Collider2D collider in
                     visual.GetComponentsInChildren<Collider2D>(true))
                collider.enabled = false;
            foreach (Rigidbody2D body in
                     visual.GetComponentsInChildren<Rigidbody2D>(true))
                body.simulated = false;
            foreach (AudioSource audio in
                     visual.GetComponentsInChildren<AudioSource>(true))
                audio.enabled = false;
        }

        private void ClearSlot(PreviewSlot slot, bool keepCamera)
        {
            slot.Ready = false;
            slot.AnimationProgress = 0f;
            slot.Terrain.Clear();
            if (slot.Root != null) Destroy(slot.Root.gameObject);
            slot.Root = null;
            slot.Generation = null;
            if (!keepCamera && slot.Camera != null)
            {
                Destroy(slot.Camera.gameObject);
                slot.Camera = null;
            }
        }

        private void OnDestroy()
        {
            _cancellation?.Cancel();
            _cancellation?.Dispose();
            foreach (PreviewSlot slot in _slots)
            {
                ClearSlot(slot, keepCamera: false);
                if (slot.Texture != null)
                {
                    slot.Texture.Release();
                    Destroy(slot.Texture);
                }
            }
            if (_waterMaterialInstance != null) Destroy(_waterMaterialInstance);
            if (_crossfadeMaterial != null) Destroy(_crossfadeMaterial);
            if (backgroundImage != null)
            {
                backgroundImage.texture = null;
                backgroundImage.material = null;
            }
        }
    }
}

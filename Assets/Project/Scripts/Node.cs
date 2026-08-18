using System;
using System.Collections.Generic;
using Project.Scripts;
using Project.Scripts.Core;
using Project.Scripts.DataTypes;
using Project.Scripts.DataTypes.SaveData;
using Project.Scripts.Gameplay;
using Project.Scripts.Interface;
using Project.Scripts.Interface.Decorator;
using Project.Scripts.Persistence;
using Project.Scripts.Utility;
using UnityEngine;
using Zenject;

[RequireComponent(typeof(PersistentEntity))]
public class Node : MonoBehaviour, INode
{
    [SerializeField]
    private SpriteRenderer spriteRenderer;

    [SerializeField] private Collider2D _collider2D;

    [SerializeField] private Material _defaultMaterial;
    
    [Inject] private DiContainer _container;
    [Inject] private IFactory<ItemData, ItemData.Rarity, float, ItemStack>
        _generatedEquipmentFactory;
    
    private PersistentEntity _persistentEntity;

    private PersistentComponentHost _persistentComponentHost;
    
    private NodeData _nodeData;
    public NodeData NodeData => _nodeData;
    public bool ClearReservedAreaCoverage { get; private set; }

    private GameObject _overrideVisual;
    private MaterialPropertyBlock _propertyBlock;
    private EntityDamageReceiver _damageReceiver;
    private EntityDamageVisual _damageVisual;
    private readonly List<Renderer> _rendererScratch = new();
    private readonly List<Renderer> _initializationHiddenRenderers = new();
    
    public void Initialize(NodeId nodeId, PropSpawnData spawnData, NodeData nodeData, TerrainSample terrainSample,
        Chunk chunk, int archetypeId = 0)
    {
        SetInitializationHidden(false);
        ClearOverrideVisual();
        ClearPersistentComponents();
        
        _nodeData = nodeData;
        ClearReservedAreaCoverage = spawnData.clearReservedAreaCoverage;
        _persistentEntity.SetNodeData(nodeData);
        
        _persistentEntity.Initialize(nodeId, spawnData.persistenceKind, archetypeId);
        
        name = $"{nodeData.name} ({nodeId})";
        
        transform.position = SpaceReservationUtility.GetEntityPosition(
            nodeData,
            spawnData.position);
        transform.localScale = new Vector3(spawnData.scale, spawnData.scale, 1);

        // A pooled Node may previously have represented a sprite-based prop or
        // an override-visual entity. Reset both presentation paths before
        // selecting the one used by the new NodeData.
        spriteRenderer.sprite = null;
        spriteRenderer.enabled = nodeData.overrideVisual == null;

        if(nodeData.overrideVisual)
        {
            _overrideVisual = Instantiate(nodeData.overrideVisual, transform);
        }
        else
        {
            InitializeSpriteAppearance();
        }
        
        _collider2D.isTrigger = nodeData.isTrigger;

        InstallPersistentComponents(
            nodeData,
            chunk,
            spawnData.persistenceKind,
            spawnData.accessIdentity,
            spawnData.usesVillageDoorAccess);

        InitializeTreasure(
            spawnData.treasureLoot,
            nodeId,
            terrainSample.biomeBlend.dominantBiome ?? terrainSample.biome);

        PersistentHealth health =
            _persistentComponentHost.GetComponentInChildren<PersistentHealth>(
                includeInactive: true);
        _damageReceiver ??= GetComponent<EntityDamageReceiver>();
        _damageReceiver ??=
            _container.InstantiateComponent<EntityDamageReceiver>(gameObject);
        _damageReceiver.Initialize(
            nodeData,
            _persistentEntity,
            health);
        _damageReceiver.SetDamageImmune(spawnData.damageImmune);

        _damageVisual ??= GetComponent<EntityDamageVisual>();
        _damageVisual ??=
            _container.InstantiateComponent<EntityDamageVisual>(gameObject);
        _damageVisual.Initialize(health);
        
        // var persistentTransform = GetComponent<Project.Scripts.Gameplay.PersistentTransform>();
        // if (persistentTransform == null)
        //     persistentTransform = gameObject.AddComponent<Project.Scripts.Gameplay.PersistentTransform>();
        // persistentTransform.Initialize(spawnData.persistenceKind == EntityPersistenceKind.RuntimeSpawned);

        void InitializeSpriteAppearance()
        {
            if (nodeData.sprites == null || nodeData.sprites.Length == 0)
            {
                spriteRenderer.sprite = nodeData.sprite;
            }
            else
            {
                spriteRenderer.sprite = nodeData.sprites.SelectHashed(spawnData.worldPosition.x, spawnData.worldPosition.y);
            }
            
            spriteRenderer.flipX = spawnData.flipX;
    
            if (nodeData.overrideMaterial)
            {
                spriteRenderer.material = nodeData.overrideMaterial;
                spriteRenderer.color = terrainSample.biomeBlend.groundColor;
                
                _propertyBlock ??= new MaterialPropertyBlock();
                _propertyBlock.Clear();
                spriteRenderer.GetPropertyBlock(_propertyBlock);
                
                if (nodeData.overrideMaterial.HasColor("_TintColor"))
                {
    
                    _propertyBlock.SetColor("_TintColor", nodeData.tintColor);
                }
    
                if (nodeData.overrideMaterial.HasProperty("_Lightness"))
                {
                    _propertyBlock.SetFloat("_Lightness", nodeData.lightness);
                    _propertyBlock.SetFloat("_LightnessVariance", nodeData.lightnessVariance);
                }
    
                if (nodeData.overrideMaterial.HasProperty("_Seed"))
                {
                    _propertyBlock.SetVector("_Seed", transform.position);
                }
                
                spriteRenderer.SetPropertyBlock(_propertyBlock);
            }
            else
            {
                spriteRenderer.material = _defaultMaterial;
                spriteRenderer.color = Color.white;
                spriteRenderer.SetPropertyBlock(null);
            }
        }
    }

    private void InitializeTreasure(
        TreasureLootConfiguration configuration,
        NodeId nodeId,
        BiomeData biome)
    {
        if (!configuration.IsConfigured)
            return;

        PersistentInventory inventory =
            _persistentComponentHost.GetComponentInChildren<PersistentInventory>(true);
        if (inventory == null)
        {
            Debug.LogError(
                $"Treasure entity '{_nodeData.name}' requires a PersistentInventory.",
                _nodeData);
            return;
        }

        System.Random random = new(unchecked(
            (int)(nodeId.value ^ (nodeId.value >> 32))));
        TreasureItemCatalogData catalog = TreasureLootCatalogSelector.Select(
            configuration,
            biome,
            random);
        if (catalog == null)
        {
            Debug.LogWarning(
                $"Treasure entity '{_nodeData.name}' has no catalog for " +
                $"biome '{(biome != null ? biome.name : "None")}'.",
                _nodeData);
            return;
        }

        int stackCount = random.Next(
            configuration.minimumStacks,
            configuration.maximumStacks + 1);
        for (int i = 0; i < stackCount; i++)
        {
            TreasureItemCatalogEntry entry = TreasureLootItemSelector.SelectEntry(catalog, random);
            ItemData item = entry?.item;
            if (item == null || item.maxStack < 1)
                continue;

            int desiredCount = random.Next(
                configuration.minimumItemsPerStack,
                configuration.maximumItemsPerStack + 1);
            int count = Mathf.Min(desiredCount, item.maxStack);
            ItemData.Rarity rarity = ItemRarityUtility.Generate(
                (float)random.NextDouble());
            rarity = (ItemData.Rarity)Mathf.Min(
                (int)rarity,
                (int)configuration.maximumQuality);

            try
            {
                if (entry.generateModifiers && item is EquipableItemData)
                    inventory.TryAdd(_generatedEquipmentFactory.Create(
                        item, rarity, entry.uniqueNameChance), out _);
                else
                    inventory.TryAdd(item, count, out _, rarity);
            }
            catch (ArgumentException exception)
            {
                Debug.LogError(
                    $"Treasure catalog item '{item.name}' is not valid for " +
                    $"container '{_nodeData.name}': {exception.Message}",
                    _nodeData);
            }
        }
    }

    public void SetInitializationHidden(bool hidden)
    {
        if (!hidden)
        {
            foreach (Renderer renderer in _initializationHiddenRenderers)
            {
                if (renderer != null)
                    renderer.enabled = true;
            }
            _initializationHiddenRenderers.Clear();
            return;
        }

        _rendererScratch.Clear();
        GetComponentsInChildren(true, _rendererScratch);
        foreach (Renderer renderer in _rendererScratch)
        {
            if (renderer != null && renderer.enabled)
            {
                renderer.enabled = false;
                _initializationHiddenRenderers.Add(renderer);
            }
        }
    }

    private void InstallPersistentComponents(
        NodeData nodeData,
        Chunk chunk,
        EntityPersistenceKind persistenceKind,
        AccessIdentity accessIdentity,
        bool usesVillageDoorAccess)
    {
        var hostObject = new GameObject("Persistent Components");
        _persistentComponentHost = hostObject.AddComponent<PersistentComponentHost>();

        _persistentComponentHost.transform.SetParent(
            transform,
            worldPositionStays: false);

        var context = new NodeComponentSpawnContext(
            this,
            chunk,
            persistenceKind,
            accessIdentity,
            usesVillageDoorAccess);

        if (nodeData.persistentComponents != null)
        {
            foreach (ComponentDefinitionData data
                     in nodeData.persistentComponents)
            {
                if (data == null)
                    continue;

                NodeComponentDefinition definition =
                    data.ComponentDefinition;
                if (definition == null)
                {
                    Debug.LogError(
                        $"{nodeData.name} has component data without a definition.",
                        nodeData);
                    continue;
                }

                definition.Install(
                    _persistentComponentHost.gameObject,
                    _container,
                    context,
                    data);
            }
        }

        // Register the host only after all component definitions have installed
        // their behaviours, so IEntityComponent instances receive their entity.
        _persistentEntity.SetComponentHost(_persistentComponentHost);
    }

    public class Pool : MonoMemoryPool<Project.Scripts.DataTypes.SaveData.NodeId, PropSpawnData, NodeData, TerrainSample, Chunk, Node>
    {
        protected override void Reinitialize(Project.Scripts.DataTypes.SaveData.NodeId nodeId, PropSpawnData spawnData, NodeData nodeData, TerrainSample terrainSample, Chunk chunk, Node item)
        {
            item.Initialize(nodeId, spawnData, nodeData, terrainSample, chunk);
        }

        protected override void OnCreated(Node item)
        {
            item.name = "Empty";
            base.OnCreated(item);
        }

        protected override void OnDespawned(Node item)
        {
            item.CleanUp();
            base.OnDespawned(item);
        }
    }

    private void ClearPersistentComponents()
    {
        if (_persistentComponentHost == null)
            return;

        // Detach first so the old host cannot be rediscovered while Unity's
        // deferred Destroy is pending.
        _persistentComponentHost.transform.SetParent(null);
        _persistentComponentHost.gameObject.SetActive(false);
        _persistentEntity.SetComponentHost(null);
        Destroy(_persistentComponentHost.gameObject);
        _persistentComponentHost = null;
    }

    private void CleanUp()
    {
        SetInitializationHidden(false);
        name = "Empty";
        ClearOverrideVisual();
        ClearPersistentComponents();
    }

    private void ClearOverrideVisual()
    {
        if (!_overrideVisual)
            return;

        // Destroy is deferred until the end of the frame. Disable the old
        // pooled visual first so it cannot reappear if this Node is spawned
        // again before Unity processes the destruction.
        _overrideVisual.SetActive(false);
        Destroy(_overrideVisual);
        _overrideVisual = null;
    }

    private void Awake()
    {
        if(!spriteRenderer)
            spriteRenderer = GetComponent<SpriteRenderer>();
        if(!_collider2D)
            _collider2D = GetComponent<Collider2D>();
        
        _persistentEntity = GetComponent<PersistentEntity>();
        if(!_persistentEntity)
            _persistentEntity = gameObject.AddComponent<PersistentEntity>();
    }
}

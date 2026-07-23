using System;
using Project.Scripts;
using Project.Scripts.Core;
using Project.Scripts.DataTypes.SaveData;
using Project.Scripts.Gameplay;
using Project.Scripts.Interface.Decorator;
using UnityEngine;
using Zenject;

[RequireComponent(typeof(PersistentEntity))]
public class Node : MonoBehaviour, IInteractable
{
    [SerializeField]
    private SpriteRenderer spriteRenderer;

    [SerializeField] private Collider2D _collider2D;
    
    [SerializeField] private Material _defaultMaterial;
    
    private PersistentEntity _persistentEntity;
    
    private NodeData _nodeData;

    private GameObject _overrideVisual;
    
    public void Initialize(NodeId nodeId, PropSpawnData spawnData, NodeData nodeData, TerrainSample terrainSample,
        Chunk chunk, int archetypeId = 0)
    {
        ClearOverrideVisual();
        _nodeData = nodeData;
        
        _persistentEntity.Initialize(nodeId, spawnData.persistenceKind, archetypeId);
        
        name = $"{nodeData.name} ({nodeId})";
        
        transform.position = spawnData.position;
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

        var persistentTransform = GetComponent<Project.Scripts.Gameplay.PersistentTransform>();
        if (persistentTransform == null)
            persistentTransform = gameObject.AddComponent<Project.Scripts.Gameplay.PersistentTransform>();
        persistentTransform.Initialize(spawnData.persistenceKind == EntityPersistenceKind.RuntimeSpawned);

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
                
                var propertyBlock = new MaterialPropertyBlock();
                spriteRenderer.GetPropertyBlock(propertyBlock);
                
                if (nodeData.overrideMaterial.HasColor("_TintColor"))
                {
    
                    propertyBlock.SetColor("_TintColor", nodeData.tintColor);
                }
    
                if (nodeData.overrideMaterial.HasProperty("_Lightness"))
                {
                    propertyBlock.SetFloat("_Lightness", nodeData.lightness);
                    propertyBlock.SetFloat("_LightnessVariance", nodeData.lightnessVariance);
                }
    
                if (nodeData.overrideMaterial.HasProperty("_Seed"))
                {
                    propertyBlock.SetVector("_Seed", transform.position);
                }
                
                spriteRenderer.SetPropertyBlock(propertyBlock);
            }
            else
            {
                spriteRenderer.material = _defaultMaterial;
                spriteRenderer.color = Color.white;
            }
        }
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

    private void CleanUp()
    {
        name = "Empty";
        ClearOverrideVisual();
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

    public Vector3 GetPosition()
    {
        return transform.position;
    }
    
    public bool CanInteract(InteractionContext context)
    {
        return context.interactionType == InteractionType.Direct;
    }

    public void Interact(InteractionContext context)
    {
        Debug.Log("Interact");
        if (!TryGetComponent<PersistentEntity>(out var entity))
        {
            return;
        }
        
        entity.RemoveFromWorld();
    }

    public string GetInteractionPrompt(InteractionContext context)
    {
        return $"Touch {_nodeData.name}";
    }
}

using System;
using Project.Scripts;
using Project.Scripts.Core;
using Project.Scripts.DataTypes.SaveData;
using UnityEngine;
using Zenject;

[RequireComponent(typeof(PersistentEntity))]
public class Node : MonoBehaviour
{
    [SerializeField]
    private SpriteRenderer spriteRenderer;

    [SerializeField] private Collider2D _collider2D;
    
    [SerializeField] private Material _defaultMaterial;
    
    private PersistentEntity _persistentEntity;
    
    private NodeData _nodeData;

    private GameObject _overrideVisual;
    
    public void Initialize(NodeId nodeId, PropSpawnData spawnData, NodeData nodeData, TerrainSample terrainSample)
    {
        _nodeData = nodeData;
        
        _persistentEntity.Initialize(nodeId, spawnData.persistenceKind);
        
        name = $"{nodeData.name} ({nodeId})";
        
        transform.position = spawnData.position;
        transform.localScale = new Vector3(spawnData.scale, spawnData.scale, 1);
        
        
        if(nodeData.overrideVisual)
        {
            _overrideVisual = Instantiate(nodeData.overrideVisual, transform);
        }
        else
        {
            InitializeSpriteAppearance();
        }
        
        _collider2D.isTrigger = nodeData.isTrigger;

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

    public class Pool : MonoMemoryPool<Project.Scripts.DataTypes.SaveData.NodeId, PropSpawnData, NodeData, TerrainSample, Node>
    {
        protected override void Reinitialize(Project.Scripts.DataTypes.SaveData.NodeId nodeId, PropSpawnData spawnData, NodeData nodeData, TerrainSample terrainSample, Node item)
        {
            item.Initialize(nodeId, spawnData, nodeData, terrainSample);
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
        if(_overrideVisual)
            Destroy(_overrideVisual);
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

using Project.Scripts;
using UnityEngine;
using Zenject;

public class Node : MonoBehaviour
{
    [SerializeField]
    private SpriteRenderer spriteRenderer;
    
    
    
    public void Initialize(ChunkGenerator.PropSpawnData spawnData, NodeData nodeData)
    {
        spriteRenderer.sprite = nodeData.sprite;
        spriteRenderer.flipX = spawnData.flipX;
        transform.position = spawnData.position;
        transform.localScale = new Vector3(spawnData.scale, spawnData.scale, 1);
    }

    public class Pool : MonoMemoryPool<ChunkGenerator.PropSpawnData, NodeData, Node>
    {
        protected override void Reinitialize(ChunkGenerator.PropSpawnData spawnData, NodeData nodeData, Node item)
        {
            item.Initialize(spawnData, nodeData);
        }
    }
}

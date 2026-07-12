using System;
using UnityEngine;
using Zenject;

namespace Project.Scripts
{
    public class TopDownMovementController : MonoBehaviour
    {
        private Inputs inputs;
        
        public float speed = 10;
    
        private float currentSpeed;
        
        [Inject] private WorldGeneration worldGeneration;
        [Inject] private WorldData worldData;
        
        [Inject] private Chunkloader chunkloader;
        private Grid grid;

        private Rigidbody2D _rigidbody2D;
        
        private void Awake()
        {
            grid = FindFirstObjectByType<Grid>();
            
            _rigidbody2D = GetComponent<Rigidbody2D>();
            
            inputs = new Inputs();

            inputs.Player.Interact.started += ctx =>
            {
                Debug.Log("Reload chunks");
                chunkloader.ReloadChunks();
            };

            var position = worldGeneration.FindSafeSpawnPosition(minHeight:worldData.beachHeight+0.1f, maxHeight:worldData.mountainHeight);

            transform.position = grid.CellToWorld(new Vector3Int(position.x, position.y, 0));
        }
        

        private void OnEnable()
        {
            inputs.Enable();
        }
        
        private void OnDisable()
        {
            inputs.Disable();
        }

        private void Update()
        {
            var input = inputs.Player.Move.ReadValue<Vector2>() * (speed * Time.deltaTime);
            
            if(inputs.Player.Interact.WasPressedThisFrame())
                chunkloader.ReloadChunks();
            _rigidbody2D.AddForce(input);
        }
    }
}
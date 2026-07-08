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
        private Grid grid;

        private Rigidbody2D _rigidbody2D;
        
        private void Awake()
        {
            grid = FindFirstObjectByType<Grid>();
            
            _rigidbody2D = GetComponent<Rigidbody2D>();
            
            inputs = new Inputs();
            var position = worldGeneration.FindSafeSpawnPosition(minHeight:worldData.beachHeight, maxHeight:worldData.mountainHeight);

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
            _rigidbody2D.AddForce(input);
        }
    }
}
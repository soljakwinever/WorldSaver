using System;
using Project.Scripts.Gameplay;
using Project.Scripts.Interface;
using UnityEngine;
using Zenject;

namespace Project.Scripts
{
    public class TopDownMovementController : MonoBehaviour
    {
        public float speed = 10;
    
        private float currentSpeed;
        
        [Inject] private WorldGeneration worldGeneration;
        
        private IInputManager inputManager;
        private bool _subscribedToInput = false;
        
        [Inject] private Chunkloader chunkloader;
        private Grid grid;

        private Rigidbody2D _rigidbody2D;
        private Vector2 moveInput;
        
        [Inject] private PlayerDataController playerDataController;

        [Inject]
        public void Construct(IInputManager inputManager)
        {
            UnsubscribeFromInput();
            this.inputManager = inputManager;

            if (isActiveAndEnabled)
                SubscribeToInput();
        }
        
        private void Awake()
        {
            grid = FindFirstObjectByType<Grid>();
            
            _rigidbody2D = GetComponent<Rigidbody2D>();
            
            Vector2Int position = worldGeneration.WorldSpawnPosition;

            transform.position = grid.CellToWorld(new Vector3Int(position.x, position.y, 0));
        }

        private void OnEnable()
        {
            SubscribeToInput();
        }
        
        private void OnDisable()
        {
            UnsubscribeFromInput();
        }

        private void Update()
        {
            var input = moveInput * (speed * Time.deltaTime);

            playerDataController.SetWalking(input.magnitude > 0.1f);
            
            _rigidbody2D.AddForce(input);
        }
        
        private void InputManagerOnInputPerformed(InputContext context)
        {
            moveInput = context.Movement;
        }
        
        private void SubscribeToInput()
        {
            if(_subscribedToInput || inputManager == null) return;
            
            inputManager.InputPerformed += InputManagerOnInputPerformed;
            _subscribedToInput = true;
        }

        private void UnsubscribeFromInput()
        {
            if(!_subscribedToInput || inputManager == null) return;
            
            inputManager.InputPerformed -= InputManagerOnInputPerformed;
            _subscribedToInput = false;
        }

    }
}

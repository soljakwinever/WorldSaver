using System;
using Project.Scripts.DataTypes;
using Project.Scripts.Gameplay;
using Project.Scripts.Interface;
using UnityEngine;
using Zenject;

namespace Project.Scripts
{
    public class TopDownMovementController : MonoBehaviour, ISkillFacing
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
        private Vector2 _facingDirection = Vector2.down;
        private TileData _activeDamagingTile;
        private float _hazardExposureSeconds;
        public Vector2 FacingDirection => _facingDirection;

        public Vector3 ResolveLaunchOrigin(Vector2 offset)
        {
            Vector2 facing = _facingDirection.normalized;
            if (facing.sqrMagnitude <= Mathf.Epsilon)
                return transform.position + (Vector3)offset;

            Vector2 perpendicular = new(-facing.y, facing.x);
            return transform.position +
                   (Vector3)(facing * offset.x + perpendicular * offset.y);
        }
        
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
            bool movementLocked =
                GetComponentInChildren<IMovementLock>()?.IsMovementLocked == true;
            float skillSpeedMultiplier =
                GetComponent<SkillRuntime>()?.GetMovementSpeedMultiplier() ?? 1f;
            var input = movementLocked
                ? Vector2.zero
                : moveInput * (speed * skillSpeedMultiplier * Time.deltaTime);

            if (movementLocked && _rigidbody2D != null)
                _rigidbody2D.linearVelocity = Vector2.zero;

            playerDataController.SetWalking(input.magnitude > 0.1f);
            
            _rigidbody2D.AddForce(input);
            ApplyTileDamage(Time.deltaTime);
        }

        private void ApplyTileDamage(float deltaTime)
        {
            if (grid == null ||
                chunkloader == null ||
                playerDataController == null ||
                playerDataController.Health <= 0 ||
                playerDataController.IsDeathInProgress)
            {
                ResetTileDamage();
                return;
            }

            Vector3Int worldCell = grid.WorldToCell(transform.position);
            if (!chunkloader.TryGetLoadedChunk(worldCell, out Chunk chunk) ||
                !chunk.TryGetDamagingTile(worldCell, out TileData tile))
            {
                ResetTileDamage();
                return;
            }

            if (_activeDamagingTile != tile)
            {
                _activeDamagingTile = tile;
                _hazardExposureSeconds = 0f;
            }

            _hazardExposureSeconds += Mathf.Max(0f, deltaTime);
            int elapsedSeconds = Mathf.FloorToInt(_hazardExposureSeconds);
            if (elapsedSeconds <= 0)
                return;

            _hazardExposureSeconds -= elapsedSeconds;
            int damage = checked(tile.damagePerSecond * elapsedSeconds);
            PersistentHealth health = GetComponent<PersistentHealth>();
            if (health != null && damage > 0)
            {
                health.TakeDamage(new AttackContext(
                    gameObject,
                    null,
                    damage,
                    EntityDamageSource.Environment));
            }
        }

        private void ResetTileDamage()
        {
            _activeDamagingTile = null;
            _hazardExposureSeconds = 0f;
        }
        
        private void InputManagerOnInputPerformed(InputContext context)
        {
            moveInput = context.Movement;
            if (moveInput.sqrMagnitude > 0.0001f)
                _facingDirection = moveInput.normalized;
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

using Project.Scripts.Bus;
using Project.Scripts.Interface;
using Project.Scripts.Interface.Decorator;
using UnityEngine;
using Zenject;

namespace Project.Scripts.Gameplay
{
    public class PlayerInteractionController : MonoBehaviour
    {
        [SerializeField] private float interactRadius = 1.5f;
        [SerializeField] private LayerMask interactableMask;
        [SerializeField] private Transform facingPoint;
        
        [Inject] private PlayerBus _playerBus;
        
        private bool _inputSubscribed;
        
        private IInputManager inputManager;
        
        private IInteractable focusedInteractable;

        [Inject]
        public void Construct(IInputManager inputManager)
        {
            UnsubscribeFromInput();
            this.inputManager = inputManager;

            if (isActiveAndEnabled)
                SubscribeToInput();
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
            UpdateFocusedInteractable();
        }

        private void UpdateFocusedInteractable()
        {
            Vector2 interactionCenter = facingPoint != null
                ? facingPoint.position
                : transform.position;
            InteractionContext context = CreateDirectInteractionContext();

            focusedInteractable = null;
            float closestDistanceSquared = float.PositiveInfinity;

            Collider2D[] colliders = Physics2D.OverlapCircleAll(
                interactionCenter,
                interactRadius,
                interactableMask);

            foreach (Collider2D candidateCollider in colliders)
            {
                IInteractable candidate = candidateCollider.GetComponentInParent<IInteractable>();
                if (candidate == null || !candidate.CanInteract(context))
                    continue;

                Vector2 closestPoint = candidateCollider.ClosestPoint(interactionCenter);
                float distanceSquared = (closestPoint - interactionCenter).sqrMagnitude;
                if (distanceSquared >= closestDistanceSquared)
                    continue;

                closestDistanceSquared = distanceSquared;
                focusedInteractable = candidate;
            }

            _playerBus.RaiseInteractableHovered(focusedInteractable, context);
        }
        
        private void TryDirectInteract()
        {
            if (focusedInteractable == null)
                return;

            InteractionContext context = CreateDirectInteractionContext();
            if (focusedInteractable.CanInteract(context))
                focusedInteractable.Interact(context);
        }

        private InteractionContext CreateDirectInteractionContext()
        {
            return new InteractionContext(gameObject, InteractionType.Direct, null);
        }
        
        private void InputManagerOnInputPerformed(InputContext context)
        {
            if (context.InteractionPressed)
            {
                TryDirectInteract();
            }
        }
        
        
        private void SubscribeToInput()
        {
            if (_inputSubscribed || inputManager == null) return;
            
            inputManager.InputPerformed += InputManagerOnInputPerformed;
            _inputSubscribed = true;
        }

        private void UnsubscribeFromInput()
        {
            if(!_inputSubscribed  || inputManager == null) return;
            
            inputManager.InputPerformed -= InputManagerOnInputPerformed;
            _inputSubscribed = false;
        }
    }
}

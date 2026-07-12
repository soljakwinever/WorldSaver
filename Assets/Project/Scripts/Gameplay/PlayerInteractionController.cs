using System;
using Project.Scripts.Interface.Decorator;
using UnityEngine;

namespace Project.Scripts.Gameplay
{
    public class PlayerInteractionController : MonoBehaviour
    {
        [SerializeField] private float interactRadius = 1.5f;
        [SerializeField] private LayerMask interactableMask;
        [SerializeField] private Transform facingPoint;
        
        private IInteractable focusedInteractable;

        private void Awake()
        {
            throw new NotImplementedException();
        }

        private void Update()
        {
            UpdateFocusedInteractable();
        }

        private void UpdateFocusedInteractable()
        {
            
        }
        
        private void TryDirectInteract()
        {
            if (focusedInteractable == null) return;
            
            
        }
    }
}
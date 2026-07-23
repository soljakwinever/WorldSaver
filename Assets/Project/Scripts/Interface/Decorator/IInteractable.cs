using Project.Scripts.Gameplay;
using UnityEngine;

namespace Project.Scripts.Interface.Decorator
{
    public interface IInteractable
    {
        Vector3 GetPosition();
        bool CanInteract(InteractionContext context);
        void Interact(InteractionContext context);
        string GetInteractionPrompt(InteractionContext context);
    }
}
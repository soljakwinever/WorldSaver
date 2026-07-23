using Project.Scripts.Gameplay;
using Project.Scripts.Interface.Decorator;

namespace Project.Scripts.Bus
{
    public delegate void InteractableHovered(IInteractable interactable, InteractionContext interactionContext);
    
    public class PlayerBus
    {
        public event InteractableHovered interactableHovered;

        public void RaiseInteractableHovered(IInteractable interactable, InteractionContext interactionContext)
        {
            interactableHovered?.Invoke(interactable, interactionContext);
        }
    }
}
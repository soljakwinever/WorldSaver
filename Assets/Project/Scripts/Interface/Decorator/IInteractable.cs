using Project.Scripts.Gameplay;

namespace Project.Scripts.Interface.Decorator
{
    public interface IInteractable
    {
        bool CanInteract(InteractionContext context);
        void Interact(InteractionContext context);
        string GetInteractionPrompt(InteractionContext context);
    }
}
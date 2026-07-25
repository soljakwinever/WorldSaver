using Project.Scripts.Gameplay;
using Project.Scripts.Interface;
using Project.Scripts.Interface.Decorator;

namespace Project.Scripts.Bus
{
    public delegate void InteractableHovered(IInteractable interactable, InteractionContext interactionContext);
    public delegate void HotbarIndexChanged(int index, IHotbarAction[] hotbarActions);

    public delegate void HotbarActionSet(int index, IHotbarAction action);
    
    public class PlayerBus
    {
        public event InteractableHovered interactableHovered;
        public event HotbarIndexChanged hotbarIndexChanged;
        public event HotbarActionSet hotbarActionSet;

        public void RaiseInteractableHovered(IInteractable interactable, InteractionContext interactionContext)
        {
            interactableHovered?.Invoke(interactable, interactionContext);
        }

        public void RaiseHotbarIndexChanged(int index, IHotbarAction[] hotbarActions)
        {
            hotbarIndexChanged?.Invoke(index, hotbarActions);
        }

        public void RaiseHotbarActionSet(int index, IHotbarAction action)
        {
            hotbarActionSet?.Invoke(index, action);
        }
    }
}
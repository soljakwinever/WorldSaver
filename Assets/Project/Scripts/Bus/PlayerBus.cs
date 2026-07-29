using Project.Scripts.Gameplay;
using Project.Scripts.Interface;
using Project.Scripts.Interface.Decorator;

namespace Project.Scripts.Bus
{
    public delegate void InteractableHovered(IInteractable interactable, InteractionContext interactionContext);
    public delegate void HotbarIndexChanged(int index, IHotbarAction[] hotbarActions);

    public delegate void HotbarActionSet(int index, IHotbarAction action);
    public delegate void LevelUpHandler(int newLevel, int statPointsAwarded);
    public delegate void ExperienceChangedHandler(
        int level,
        int experience,
        int experienceToNextLevel);
    
    public class PlayerBus
    {
        public event InteractableHovered interactableHovered;
        public event HotbarIndexChanged hotbarIndexChanged;
        public event HotbarActionSet hotbarActionSet;
        public event LevelUpHandler OnLevelUp;
        public event ExperienceChangedHandler OnExperienceChanged;
        public event System.Action OnStatsChanged;

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

        public void RaiseLevelUp(int newLevel, int statPointsAwarded)
        {
            OnLevelUp?.Invoke(newLevel, statPointsAwarded);
        }

        public void RaiseExperienceChanged(
            int level,
            int experience,
            int experienceToNextLevel)
        {
            OnExperienceChanged?.Invoke(
                level,
                experience,
                experienceToNextLevel);
        }

        public void RaiseStatsChanged()
        {
            OnStatsChanged?.Invoke();
        }
    }
}

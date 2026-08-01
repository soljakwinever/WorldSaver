using UnityEngine;

namespace Project.Scripts.Interface
{
    public interface ISkillStamina
    {
        float CurrentStamina { get; }
        float MaximumStamina { get; }
        bool TrySpendStamina(float amount);
    }

    public interface ISkillAnimationPlayer
    {
        void PlaySkillAnimation(AnimationClip clip);
    }
}

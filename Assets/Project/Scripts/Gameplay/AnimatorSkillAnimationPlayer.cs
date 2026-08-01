using Project.Scripts.Interface;
using UnityEngine;

namespace Project.Scripts.Gameplay
{
    [DisallowMultipleComponent]
    public sealed class AnimatorSkillAnimationPlayer : MonoBehaviour, ISkillAnimationPlayer
    {
        [SerializeField] private Animator animator;

        private void Awake() => animator ??= GetComponentInChildren<Animator>();

        public void PlaySkillAnimation(AnimationClip clip)
        {
            if (animator == null || clip == null)
                return;
            int state = Animator.StringToHash(clip.name);
            if (animator.HasState(0, state))
                animator.CrossFade(state, 0.05f, 0);
        }
    }
}

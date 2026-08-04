using Project.Scripts.Interface;
using UnityEngine;

namespace Project.Scripts.Gameplay
{
    [DisallowMultipleComponent]
    public sealed class SkillRuntimeProjectileBridge : MonoBehaviour
    {
        public IProjectileService ProjectileService { get; private set; }

        public void Initialize(IProjectileService projectileService) =>
            ProjectileService = projectileService;
    }
}

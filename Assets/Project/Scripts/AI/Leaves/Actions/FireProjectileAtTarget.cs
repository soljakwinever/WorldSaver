using System;
using Project.Scripts.AI.GraphEditor;
using Project.Scripts.DataTypes;
using Project.Scripts.Interface;
using UnityEngine;

namespace Project.Scripts.AI.Leaves.Actions
{
    [Serializable, AiNode(
        Name = "Fire Projectile At Target",
        Group = "Actions")]
    public sealed class FireProjectileAtTarget : AiNode
    {
        [SerializeField, InputPort("Target")]
        private AiKeys.Key targetKey = AiKeys.Key.Target;

        [SerializeField, InputPort("Projectile")]
        private ProjectileData projectile;

        [SerializeField, Min(0), InputPort("Force")]
        private int force = 1;

        [SerializeField, InputPort("Weapon")]
        private ToolData weapon;

        [SerializeField, Min(0f), InputPort("Maximum Range")]
        private float maximumRange = 6f;

        [SerializeField, InputPort("Launch Offset")]
        private Vector2 launchOffset;

        [SerializeField, InputPort("Predict Target Movement")]
        private bool predictTargetMovement = true;

        [SerializeField, Min(0f), InputPort("Maximum Prediction Time")]
        private float maximumPredictionTime = 2f;

        protected override NodeState OnTick()
        {
            GameObject attacker = Blackboard.GetOrDefault(AiKeys.Self);
            if (attacker == null || projectile == null ||
                !Blackboard.TryGetValue(
                    AiKeys.Resolve(targetKey),
                    out object targetValue) ||
                !Blackboard.TryGet(
                    AiKeys.ProjectileService,
                    out IProjectileService projectileService) ||
                projectileService == null)
            {
                return NodeState.Failure;
            }

            Transform target = AttackTarget.ResolveTransform(targetValue);
            if (target == null)
                return NodeState.Failure;

            Vector2 initialDirection = predictTargetMovement
                ? ProjectileAim.PredictDirection(
                    attacker.transform.position,
                    target,
                    projectile.speed,
                    maximumPredictionTime)
                : (target.position - attacker.transform.position).normalized;
            Vector3 origin = ProjectileAim.ResolveDirectionalOrigin(
                attacker.transform.position,
                initialDirection,
                launchOffset);
            Vector2 direction = predictTargetMovement
                ? ProjectileAim.PredictDirection(
                    origin,
                    target,
                    projectile.speed,
                    maximumPredictionTime)
                : (target.position - origin).normalized;
            float range = Mathf.Max(0f, maximumRange);
            Vector2 targetDisplacement = target.position - origin;
            if (direction.sqrMagnitude <= Mathf.Epsilon ||
                targetDisplacement.sqrMagnitude > range * range)
            {
                return NodeState.Failure;
            }

            float accuracy = attacker
                .GetComponentInParent<IProjectileAccuracy>()?
                .ProjectileAccuracy ?? 1f;
            direction = ProjectileAim.ApplyAccuracy(direction, accuracy);

            ProjectileLaunchContext context = new(
                projectile,
                new AttackContext(
                    attacker,
                    weapon,
                    Mathf.Max(0, force),
                    EntityDamageSource.Enemy,
                    attacker.GetComponentInParent<IEntityDamageSource>()?
                        .DamageTags),
                origin,
                direction);
            return projectileService.TryLaunch(context)
                ? NodeState.Success
                : NodeState.Failure;
        }
    }
}

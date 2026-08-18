using System;
using Project.Scripts.DataTypes;
using Project.Scripts.TimeAndWeather;
using UnityEngine;

namespace Project.Scripts.Actions
{
    [CreateAssetMenu(fileName = "New Eruption Skill Action",
        menuName = "Data/Skill Actions/Eruption Pattern")]
    public sealed class EruptionSkillAction : SkillAction
    {
        public override Type DataType =>
            typeof(EruptionPatternSkillActionData);

        public override bool CanPerform(
            SkillActionContext context,
            SkillActionData data) =>
            context.User != null &&
            data is EruptionPatternSkillActionData eruption &&
            eruption.pattern?.eruption != null;

        public override void Perform(
            SkillActionContext context,
            SkillActionData data)
        {
            EruptionPatternData pattern =
                ((EruptionPatternSkillActionData)data).pattern;
            EruptionService service =
                UnityEngine.Object.FindFirstObjectByType<EruptionService>();
            if (service == null)
            {
                Debug.LogError("EruptionService is not installed.");
                return;
            }
            service.SpawnPattern(context, pattern, context.TargetPosition);
        }
    }
}

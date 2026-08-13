using Project.Scripts.Interface;
using UnityEngine;
using Zenject;

namespace Project.Scripts.Gameplay
{
    public sealed class VillagerNightService : MonoBehaviour
    {
        private ITimeController _time;
        private float _nextEvaluation;

        [Inject]
        public void Construct(ITimeController time) => _time = time;

        private void Update()
        {
            if (_time == null || Time.unscaledTime < _nextEvaluation) return;
            _nextEvaluation = Time.unscaledTime + 1f;
            bool night = _time.Hour >= 20 || _time.Hour < 7;
            foreach (VillagerEntityBridge villager in VillagerEntityBridge.All)
            {
                if (villager == null) continue;
                if (night) villager.TryBeginNightRest();
                else villager.WakeFromBed();
            }
        }
    }
}

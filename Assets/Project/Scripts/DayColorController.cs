using Project.Scripts.GameTime;
using Project.Scripts.DataTypes;
using Project.Scripts.Interface;
using Project.Scripts.TimeAndWeather;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Zenject;

namespace Project.Scripts
{
    public class DayColorController : MonoBehaviour
    {
        [SerializeField] private Light2D globalLight;
        
        private ITimeController timeController;
        [Inject] private WorldData worldData;
        [Inject] private IRegionalWeatherService weatherService;
        [Inject] private Chunkloader chunkloader;

        private void Awake()
        {
            timeController = GetComponent<TimeController>();
        }

        private void Update()
        {
            PlaneData plane = chunkloader.CurrentPlane;
            Color ambientColor = plane == null ||
                                 plane.ParticipatesInDayNightCycle
                ? worldData.dayColorGradient.Evaluate(
                    timeController.DayProgress)
                : plane.AmbientColor;
            Color weatherTint = Color.white;

            if (chunkloader.track != null)
            {
                weatherTint = weatherService
                    .Sample(chunkloader.track.position)
                    .AmbientColorTint;
            }

            Color combinedColor = ambientColor * weatherTint;
            combinedColor.a = ambientColor.a;
            globalLight.color = combinedColor;
        }
    }
}

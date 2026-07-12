using System;
using Project.Scripts.GameTime;
using Project.Scripts.Interface;
using UnityEngine;
using Zenject;

namespace Project.Scripts
{
    public class DayColorController : MonoBehaviour
    {
        private static readonly int DayColor = Shader.PropertyToID("_DayColor");
        
        private ITimeController timeController;
        [Inject] private WorldData worldData;

        private void Awake()
        {
            timeController = GetComponent<TimeController>();
            Shader.SetGlobalColor(DayColor, Color.white);       
        }

        private void OnDisable()
        {
            Shader.SetGlobalColor(DayColor, Color.white);       
        }

        void Update()
        {
            Shader.SetGlobalColor(DayColor, worldData.dayColorGradient.Evaluate(timeController.DayProgress));       
        }
    }
}
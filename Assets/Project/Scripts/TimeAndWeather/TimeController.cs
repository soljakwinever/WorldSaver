using System;
using Project.Scripts.Bus;
using Project.Scripts.DataTypes;
using Project.Scripts.Enums;
using Project.Scripts.Interface;
using UnityEngine;
using Zenject;

namespace Project.Scripts.GameTime
{
    public class TimeController : MonoBehaviour, ITimeController, ITimeSkipController
    {
        [Inject] private WorldData _worldData;
        [Inject] private TimeSignalBus _timeSignalBus;
        
        private float dayTime;
        
        private int dayInMonth; 
        private Season season;
        private int _year;

        private int _lastHour;

        private int secondsPerDay;
        private bool _initialized;
        public string timeString;

        public float DayProgress => dayTime / secondsPerDay;

        public const int HoursInDay = 24;
        public const int SecondsPerMinute = 60;
        
        private void Start()
        {
            Initialize();
        }

        private void Initialize()
        {
            if (_initialized)
                return;

            secondsPerDay = Mathf.FloorToInt(_worldData.minutesPerDay * SecondsPerMinute);
            
            season = _worldData.startSeason;
            dayInMonth = 1;
            dayTime = (_worldData.startHour /24f) * secondsPerDay;
            _lastHour = Hour;
            _initialized = true;
        }

        private void Update()
        {
            dayTime += Time.deltaTime;
            
            if (Hour != _lastHour)
            {
                _lastHour = Hour;
                _timeSignalBus.TriggerHourChanged(CreateTimeChangedArgs());
                if (Hour >= 24)
                {
                    AdvanceDay();
                }
            }
            
            timeString = $"{Season} {DayInMonth} {Year:0000}\n{Hour:00}:{Minute:00}";
        }

        public int DayInMonth => dayInMonth;

        public Season Season => season;

        public int Year => _year;

        public int Hour => (int)(DayProgress * HoursInDay);
        public int Minute => Mathf.FloorToInt((dayTime / SecondsPerHour) * SecondsPerMinute) % SecondsPerMinute;
        
        private float MinutesPerHour => _worldData.minutesPerDay / HoursInDay;
        private float SecondsPerHour => (MinutesPerHour * SecondsPerMinute);

        public void RestoreTime(
            int restoredDayInMonth,
            Season restoredSeason,
            int restoredYear,
            float restoredDayProgress)
        {
            Initialize();

            dayInMonth = Mathf.Clamp(
                restoredDayInMonth,
                1,
                _worldData.daysInMonth);
            season = restoredSeason;
            _year = Mathf.Max(0, restoredYear);
            dayTime = Mathf.Clamp01(restoredDayProgress) * secondsPerDay;
            _lastHour = Mathf.Clamp(Hour,0, HoursInDay);
        }

        private TimeChangedArgs CreateTimeChangedArgs()
        {
            return new TimeChangedArgs(Hour, Minute, dayInMonth, season, _year, DayProgress);
        }
        
        public void AdvanceDay()
        {
            dayTime = 0;
            dayInMonth++;
            
            _timeSignalBus.TriggerDayChanged(CreateTimeChangedArgs());
            
            if (dayInMonth > _worldData.daysInMonth)
            {
                AdvanceMonth();
            }
        }

        public void AdvanceToNextMorning(int morningHour = 7)
        {
            Initialize();
            morningHour = Mathf.Clamp(morningHour, 0, HoursInDay - 1);
            if (Hour >= morningHour)
                AdvanceDay();

            dayTime = morningHour / (float)HoursInDay * secondsPerDay;
            _lastHour = morningHour;
            _timeSignalBus.TriggerHourChanged(CreateTimeChangedArgs());
        }

        public void AdvanceMonth()
        {
            dayInMonth = 1;
            Season lastSeason = season; 
            season = (Season)(((int)season + 1) % 4);
            
            _timeSignalBus.TriggerMonthChanged(CreateTimeChangedArgs());
            
            if (lastSeason == Season.Winter)
            {
                AdvanceYear();
            }
        }

        public void AdvanceYear()
        {
            _year++;
            dayTime = 0;
            dayInMonth = 1;
            
            _timeSignalBus.TriggerYearChanged(CreateTimeChangedArgs());
        }
    }
}

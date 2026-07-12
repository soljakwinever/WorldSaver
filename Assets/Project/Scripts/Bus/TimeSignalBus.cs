using Project.Scripts.DataTypes;
using Project.Scripts.Enums;

namespace Project.Scripts.Bus
{
    public delegate void TimeChangeHandler(TimeChangedArgs args);
    
    public class TimeSignalBus
    {
        public event TimeChangeHandler HourChanged;
        public event TimeChangeHandler DayChanged;
        public event TimeChangeHandler MonthChanged;
        public event TimeChangeHandler YearChanged;
        
        public void TriggerHourChanged(TimeChangedArgs args)
        {
            HourChanged?.Invoke(args);
        }
        
        public void TriggerDayChanged(TimeChangedArgs args)
        {
            DayChanged?.Invoke(args);
        }
        
        public void TriggerMonthChanged(TimeChangedArgs args)
        {
            MonthChanged?.Invoke(args);
        }
        
        public void TriggerYearChanged(TimeChangedArgs args)
        {
            YearChanged?.Invoke(args);
        }
    }
}
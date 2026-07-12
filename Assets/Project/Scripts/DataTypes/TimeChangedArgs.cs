using Project.Scripts.Enums;

namespace Project.Scripts.DataTypes
{
    public readonly struct TimeChangedArgs
    {
        public readonly int Hour;
        public readonly int Minute;
        
        public readonly int DayInMonth;
        public readonly Season Season;
        public readonly int Year;
        
        public readonly float DayProgress;
        
        public TimeChangedArgs(int hour, int minute, int dayInMonth, Season season, int year, float dayProgress)
        {
            Hour = hour;
            Minute = minute;
            DayInMonth = dayInMonth;
            Season = season;
            Year = year;
            DayProgress = dayProgress;
        }
    }
}
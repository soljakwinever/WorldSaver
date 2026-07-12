using Project.Scripts.Enums;

namespace Project.Scripts.Interface
{
    public interface ITimeController
    {
        public int DayInMonth { get; }
        public Season Season { get; }
        public int Year { get; }
        
        public int Hour { get; }
        public int Minute { get; }
        public float DayProgress { get; }
        
        public void AdvanceDay();
        public void AdvanceMonth();
        public void AdvanceYear();
    }
    
    
}
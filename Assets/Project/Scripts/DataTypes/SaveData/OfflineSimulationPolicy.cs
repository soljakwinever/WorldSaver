namespace Project.Scripts.DataTypes.SaveData
{
    public enum OfflineSimulationPolicy : byte
    {
        None,
        
        //State is derived from timestamps
        Derived,
        
        //Run a direct catch-up calculation
        CatchUp,
        
        //Simulated by a chunk or region system
        Regional
    }
}
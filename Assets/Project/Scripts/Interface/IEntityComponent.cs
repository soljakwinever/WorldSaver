namespace Project.Scripts.Interface
{
    public interface IEntityComponent
    {
        public IPersistentEntity PersistentEntity { get; set; }
    }

    /// <summary>
    /// Receives cleanup notification when an entity is explicitly removed
    /// from the world. This is not invoked when a chunk is merely unloaded.
    /// </summary>
    public interface IEntityRemovalHandler
    {
        void OnRemovedFromWorld();
    }
}

namespace Project.Scripts.Interface.Decorator
{
    public interface IHasHealth
    {
        public int Health { get; }
        
        public void TakeDamage(int damage);
        public void Heal(int amount);
    }
}
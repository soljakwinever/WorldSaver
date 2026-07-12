using Project.Scripts.Interface.Decorator;

namespace Project.Scripts.Gameplay
{
    public class PlayerDataController : IHasHealth
    {
        private int _health;

        public int Health => _health;

        public void TakeDamage(int damage)
        {
            throw new System.NotImplementedException();
        }

        public void Heal(int amount)
        {
            throw new System.NotImplementedException();
        }
    }
}
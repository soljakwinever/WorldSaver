using System;
using Project.Scripts.Interface.Decorator;
using UnityEngine;
using Zenject;

namespace Project.Scripts.Gameplay
{
    public class PlayerDataController : MonoBehaviour, IHasHealth, IHasNeeds, IHasStats
    {
        private int _health;
        public float _hunger;
        public float _energy;
        private float _energyDrainRate = 0.005f;
        private float _hungerEnergyRegenerationRate = 0.0175f;
        private float _hungerDrainRate = 0.0025f;

        private float currentEnergyDrainMultiplier = 1.0f;

        [Inject] private WorldData _worldData;
        
        public int Health => _health;

        private void Awake()
        {
            _hunger = 1.0f;
            _energy = 1.0f;
        }

        void Update()
        {
            float deltaTime = Time.deltaTime;

            if (_hunger > 0f && _energy < 1f)
            {
                _hunger -= _hungerDrainRate * _worldData.playerSettings.hungerRate * deltaTime;
                _energy = Mathf.Clamp01(_energy + _hungerEnergyRegenerationRate * deltaTime);
            }
            Debug.Log(currentEnergyDrainMultiplier);
            _energy -= (_energyDrainRate * currentEnergyDrainMultiplier) * _worldData.playerSettings.energyRate * deltaTime;
        }

        public void TakeDamage(int damage)
        {
            throw new System.NotImplementedException();
        }

        public void Heal(int amount)
        {
            throw new System.NotImplementedException();
        }

        public float Hunger
        {
            get => _hunger;
            set => _hunger = value;
        }

        public float Energy
        {
            get => _energy;
            set => _energy = value;
        }

        public float EnergyDrainRate
        {
            get => _energyDrainRate;
            set => _energyDrainRate = value;
        }   

        public float HungerEnergyRegenerationRate
        {
            get => _hungerEnergyRegenerationRate;
            set => _hungerEnergyRegenerationRate = value;
        }

        public float HungerDrainRate
        {
            get => _hungerDrainRate;
            set => _hungerDrainRate = value;
        }

        public void SetWalking(bool moving)
        {
            currentEnergyDrainMultiplier = moving ? _worldData.playerSettings.movementEnergyMulpiplier : 1.0f;
        }
    }
}
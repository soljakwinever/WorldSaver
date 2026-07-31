#if UNITY_INCLUDE_TESTS
using NUnit.Framework;
using Project.Scripts;
using Project.Scripts.Gameplay;
using UnityEngine;

namespace Project.Tests.EditMode
{
    public sealed class PlayerNeedsControllerTests
    {
        [Test]
        public void HungerAndEnergyDrainOverTime()
        {
            WorldData worldData = CreateWorldData(
                hungerRate: 1f,
                energyRate: 1f);
            GameObject playerObject = new("Player");
            playerObject.SetActive(false);
            try
            {
                PlayerDataController player =
                    playerObject.AddComponent<PlayerDataController>();
                PlayerNeedsController needs =
                    playerObject.AddComponent<PlayerNeedsController>();
                needs.Construct(worldData);
                playerObject.SetActive(true);

                player.Hunger = 0.5f;
                player.Energy = 0.5f;
                needs.SimulateNeeds(10f);

                Assert.That(player.Hunger, Is.EqualTo(0.475f).Within(0.0001f));
                Assert.That(player.Energy, Is.EqualTo(0.45f).Within(0.0001f));
            }
            finally
            {
                Object.DestroyImmediate(playerObject);
                Object.DestroyImmediate(worldData);
            }
        }

        [Test]
        public void FullHungerSlowlyRegeneratesHealth()
        {
            WorldData worldData = CreateWorldData(
                hungerRate: 0f,
                energyRate: 0f);
            GameObject playerObject = new("Player");
            playerObject.SetActive(false);
            try
            {
                PlayerDataController player =
                    playerObject.AddComponent<PlayerDataController>();
                PlayerNeedsController needs =
                    playerObject.AddComponent<PlayerNeedsController>();
                needs.Construct(worldData);
                needs.ConfigureHealthRegeneration(
                    healthPerSecond: 2f,
                    hungerThreshold: 0.99f);
                playerObject.SetActive(true);

                PersistentHealth health =
                    playerObject.GetComponent<PersistentHealth>();
                health.TakeDamage(10);
                player.Hunger = 1f;
                needs.SimulateNeeds(1f);

                Assert.That(health.Health, Is.EqualTo(92));

                player.Hunger = 0.5f;
                needs.SimulateNeeds(1f);
                Assert.That(health.Health, Is.EqualTo(92));
            }
            finally
            {
                Object.DestroyImmediate(playerObject);
                Object.DestroyImmediate(worldData);
            }
        }

        private static WorldData CreateWorldData(
            float hungerRate,
            float energyRate)
        {
            WorldData data = ScriptableObject.CreateInstance<WorldData>();
            data.playerSettings = new WorldData.PlayerSettings
            {
                hungerRate = hungerRate,
                energyRate = energyRate,
                movementEnergyMulpiplier = 2f
            };
            return data;
        }
    }
}
#endif

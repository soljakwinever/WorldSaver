#if UNITY_INCLUDE_TESTS
using System.IO;
using NUnit.Framework;
using Project.Scripts.DataTypes;
using Project.Scripts.Gameplay;
using Project.Scripts.Interface;
using UnityEngine;

namespace Project.Tests.EditMode
{
    public sealed class TownCoreInteractionTests
    {
        [Test]
        public void OfferingTransferMovesTheCompleteStack()
        {
            ItemData item = ScriptableObject.CreateInstance<ItemData>();
            item.maxStack = 20;
            try
            {
                var player = new Inventory(4);
                var offerings = new Inventory(4);
                player.TryAdd(item, 7, out _);

                IItemStack stack = player.Stacks[0];
                Assert.That(
                    TownCoreWindowSection.TryTransfer(
                        player,
                        offerings,
                        stack),
                    Is.True);
                Assert.That(player.OccupiedSlots, Is.Zero);
                Assert.That(
                    offerings.GetCount(
                        item,
                        ItemData.Rarity.Common),
                    Is.EqualTo(7));
            }
            finally
            {
                Object.DestroyImmediate(item);
            }
        }

        [Test]
        public void PlayerSpawnPointPersistsAndCanBeUsed()
        {
            GameObject sourceObject = new("Source Player");
            GameObject restoredObject = new("Restored Player");
            sourceObject.SetActive(false);
            restoredObject.SetActive(false);
            try
            {
                PlayerDataController source =
                    sourceObject.AddComponent<PlayerDataController>();
                Vector3 expected = new(12.5f, -3.25f, 0f);
                source.SetSpawnPoint(expected);

                byte[] state;
                using (var stream = new MemoryStream())
                {
                    using (var writer = new BinaryWriter(
                               stream,
                               System.Text.Encoding.UTF8,
                               true))
                        source.WriteState(writer);
                    state = stream.ToArray();
                }

                PlayerDataController restored =
                    restoredObject.AddComponent<PlayerDataController>();
                using (var stream = new MemoryStream(
                           state,
                           writable: false))
                using (var reader = new BinaryReader(stream))
                    restored.ReadState(reader, source.PersistentVersion);

                Assert.That(
                    restored.TryGetSpawnPoint(out Vector3 point),
                    Is.True);
                Assert.That(point, Is.EqualTo(expected));
                Assert.That(restored.RespawnAtSpawnPoint(), Is.True);
                Assert.That(restoredObject.transform.position,
                    Is.EqualTo(expected));
            }
            finally
            {
                Object.DestroyImmediate(sourceObject);
                Object.DestroyImmediate(restoredObject);
            }
        }

        [Test]
        public void TownSpawnIsUsedOnDeathWithoutRegistryLookup()
        {
            GameObject playerObject = new("Player");
            GameObject townObject = new("Town");
            try
            {
                PlayerDataController player =
                    playerObject.AddComponent<PlayerDataController>();
                townObject.transform.position =
                    new Vector3(18f, -7f, 0f);
                TownCore town = townObject.AddComponent<TownCore>();
                Vector3 expected = town.SpawnPoint;

                player.SetSpawnTown(town);
                Assert.That(player.IsSpawnTown(town), Is.True);

                playerObject.transform.position =
                    new Vector3(-25f, 30f, 0f);
                player.TakeDamage(player.MaxHealth);

                Assert.That(
                    playerObject.transform.position,
                    Is.EqualTo(expected));
            }
            finally
            {
                Object.DestroyImmediate(playerObject);
                Object.DestroyImmediate(townObject);
            }
        }
    }
}
#endif

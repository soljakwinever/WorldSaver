#if UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using NUnit.Framework;
using Project.Scripts.Core;
using Project.Scripts.DataTypes;
using Project.Scripts.Gameplay;
using Project.Scripts.Interface;
using UnityEngine;

namespace Project.Tests.EditMode
{
    public sealed class PlayerDeathTests
    {
        [Test]
        public void PersistentHealthRaisesDeathOnceWhenHealthReachesZero()
        {
            GameObject host = new("Health");
            try
            {
                PersistentHealth health =
                    host.AddComponent<PersistentHealth>();
                int deaths = 0;
                health.Died += () => deaths++;

                health.TakeDamage(100);
                health.TakeDamage(1);

                Assert.That(health.Health, Is.Zero);
                Assert.That(deaths, Is.EqualTo(1));
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void PlayerDeathKeepsToolbarItemsAndDropsEverythingElse()
        {
            ItemData toolbarItem = CreateItem("toolbar-item");
            ItemData carriedItem = CreateItem("carried-item");
            GameObject playerObject = new("Player");
            try
            {
                PlayerDataController player =
                    playerObject.AddComponent<PlayerDataController>();
                PersistentInventory inventory =
                    playerObject.GetComponent<PersistentInventory>();
                inventory.Configure(4, new[] { toolbarItem, carriedItem });
                inventory.TryAdd(toolbarItem, 3, out _);
                inventory.TryAdd(
                    carriedItem,
                    7,
                    out _,
                    ItemData.Rarity.Rare);

                PlayerToolbarController toolbar =
                    playerObject.GetComponent<PlayerToolbarController>();
                toolbar.SetHotbarAction(
                    0,
                    new ItemActionBinding(toolbarItem));
                playerObject.transform.position = new Vector3(8f, 4f, 0f);

                player.TakeDamage(player.MaxHealth);

                Assert.That(player.Health, Is.EqualTo(20));
                Assert.That(player.Hunger, Is.EqualTo(0.25f));
                Assert.That(player.Energy, Is.EqualTo(1f));
                Assert.That(playerObject.transform.position, Is.EqualTo(Vector3.zero));
                Assert.That(
                    inventory.GetCount(toolbarItem),
                    Is.EqualTo(3));
                Assert.That(
                    inventory.GetCount(
                        carriedItem,
                        ItemData.Rarity.Rare),
                    Is.Zero);
                Assert.That(player.LastDeathDrop, Is.Not.Null);
                Assert.That(
                    player.LastDeathDrop.Inventory.GetCount(
                        carriedItem,
                        ItemData.Rarity.Rare),
                    Is.EqualTo(7));
            }
            finally
            {
                if (playerObject != null)
                {
                    DeathDropContainer drop =
                        playerObject.GetComponent<PlayerDataController>()?
                            .LastDeathDrop;
                    if (drop != null)
                        Object.DestroyImmediate(drop.gameObject);
                    Object.DestroyImmediate(playerObject);
                }
                Object.DestroyImmediate(toolbarItem);
                Object.DestroyImmediate(carriedItem);
            }
        }

        [Test]
        public void DeathDropExpiresWhenEmptyOrAtConfiguredTick()
        {
            ItemData item = CreateItem("drop-item");
            var clock = new FakeWorldClock { CurrentTickValue = 10 };
            DeathDropContainer timed = null;
            DeathDropContainer empty = null;
            try
            {
                timed = DeathDropContainer.Create(
                    Vector3.zero,
                    new IItemStack[] { new ItemStack(item, 1) },
                    5,
                    clock,
                    null);

                Assert.That(timed.EvaluateLifetime(14), Is.False);
                Assert.That(timed.EvaluateLifetime(15), Is.True);

                empty = DeathDropContainer.Create(
                    Vector3.zero,
                    new IItemStack[] { new ItemStack(item, 1) },
                    5,
                    clock,
                    null);
                empty.Inventory.Clear();
                Assert.That(empty.EvaluateLifetime(10), Is.True);
            }
            finally
            {
                if (timed != null)
                    Object.DestroyImmediate(timed.gameObject);
                if (empty != null)
                    Object.DestroyImmediate(empty.gameObject);
                Object.DestroyImmediate(item);
            }
        }

        private static ItemData CreateItem(string id)
        {
            ItemData item = ScriptableObject.CreateInstance<ItemData>();
            item.persistentId = id;
            item.maxStack = 20;
            return item;
        }

        private sealed class FakeWorldClock : IWorldClock
        {
            public long CurrentTickValue;
            public long CurrentTick => CurrentTickValue;
            public void Save()
            {
            }
        }
    }
}
#endif

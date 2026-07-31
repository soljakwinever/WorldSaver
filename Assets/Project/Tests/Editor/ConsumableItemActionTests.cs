#if UNITY_INCLUDE_TESTS
using System.Reflection;
using NUnit.Framework;
using Project.Scripts.Actions;
using Project.Scripts.DataTypes;
using Project.Scripts.Gameplay;
using UnityEngine;

namespace Project.Tests.EditMode
{
    public sealed class ConsumableItemActionTests
    {
        [Test]
        public void IncreaseNeedsRestoresConfiguredValuesAndClampsToFull()
        {
            GameObject user = new("Needs User");
            user.SetActive(false);
            ItemData item = CreateItem(
                new IncreaseNeedsActionData
                {
                    hunger = 0.25f,
                    energy = 0.5f
                });
            IncreaseNeedsItemAction action =
                ScriptableObject.CreateInstance<IncreaseNeedsItemAction>();
            try
            {
                PlayerDataController player =
                    user.AddComponent<PlayerDataController>();
                player.Hunger = 0.8f;
                player.Energy = 0.6f;
                ActionContext context =
                    new(user, Vector3.zero, item);

                Assert.That(action.ConsumesItem, Is.True);
                Assert.That(action.CanPerform(context), Is.True);
                Assert.That(action.Perform(context), Is.True);
                Assert.That(player.Hunger, Is.EqualTo(1f));
                Assert.That(player.Energy, Is.EqualTo(1f));
                Assert.That(action.CanPerform(context), Is.False);
            }
            finally
            {
                Object.DestroyImmediate(action);
                Object.DestroyImmediate(item);
                Object.DestroyImmediate(user);
            }
        }

        [Test]
        public void IncreaseHealthManaRestoresPointsAndRejectsFullUser()
        {
            GameObject user = new("Vitals User");
            user.SetActive(false);
            ItemData item = CreateItem(
                new IncreaseHealthManaActionData
                {
                    health = 15,
                    mana = 10
                });
            IncreaseHealthManaItemAction action =
                ScriptableObject.CreateInstance<IncreaseHealthManaItemAction>();
            try
            {
                PlayerDataController player =
                    user.AddComponent<PlayerDataController>();
                user.SetActive(true);
                player.TakeDamage(20);
                player.Mana = 0.5f;
                ActionContext context =
                    new(user, Vector3.zero, item);

                Assert.That(action.ConsumesItem, Is.True);
                Assert.That(action.CanPerform(context), Is.True);
                Assert.That(action.Perform(context), Is.True);
                Assert.That(player.Health, Is.EqualTo(95));
                Assert.That(player.CurrentMana, Is.EqualTo(35));

                player.Heal(player.MaxHealth);
                player.Mana = 1f;
                Assert.That(action.CanPerform(context), Is.False);
                Assert.That(action.Perform(context), Is.False);
            }
            finally
            {
                Object.DestroyImmediate(action);
                Object.DestroyImmediate(item);
                Object.DestroyImmediate(user);
            }
        }

        private static ItemData CreateItem(ItemActionData actionData)
        {
            ItemData item = ScriptableObject.CreateInstance<ItemData>();
            typeof(ItemData)
                .GetField(
                    "actionData",
                    BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(item, new[] { actionData });
            return item;
        }
    }
}
#endif

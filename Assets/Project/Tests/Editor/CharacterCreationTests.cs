#if UNITY_INCLUDE_TESTS
using NUnit.Framework;
using Project.Scripts.Gameplay;
using UnityEngine;

namespace Project.Tests.Editor
{
    public sealed class CharacterCreationTests
    {
        [Test]
        public void ShippedCatalogContainsPlayableWarriorAndMage()
        {
            CharacterCreationCatalogData catalog =
                Resources.Load<CharacterCreationCatalogData>(
                    CharacterCreationCatalogData.ResourcePath);

            Assert.That(catalog, Is.Not.Null);
            Assert.That(catalog.TryGetSpecies("human", out _), Is.True);
            Assert.That(catalog.TryGetSpecies("dragon", out _), Is.True);
            Assert.That(catalog.TryGetSpecies("faerie", out _), Is.True);
            Assert.That(catalog.TryGetClass("warrior", out CharacterClassDefinition warrior), Is.True);
            Assert.That(warrior.available, Is.True);
            Assert.That(warrior.starterItems, Has.Length.EqualTo(1));
            Assert.That(warrior.starterItems[0].item.persistentId, Is.EqualTo("weapons.woodensword"));
            Assert.That(catalog.TryGetClass("mage", out CharacterClassDefinition mage), Is.True);
            Assert.That(mage.available, Is.True);
            Assert.That(mage.starterSkills, Has.Length.EqualTo(1));
            Assert.That(catalog.GetGrowth(StatGrowthRank.S), Is.EqualTo(1f));
        }

        [Test]
        public void ProfileJsonPreservesIdentityAppearanceAndGrowthRanks()
        {
            CharacterProfile source = new()
            {
                id = "test-character",
                name = "Test Character",
                gender = CharacterGender.Other,
                speciesId = "dragon",
                classId = "warrior",
                growthRanks = CharacterGrowthRanks.All(StatGrowthRank.S),
                appearance = new CharacterAppearance
                {
                    horns = "curved",
                    wings = "broad",
                    eyeColor = Color.green
                }
            };

            CharacterProfile restored =
                JsonUtility.FromJson<CharacterProfile>(
                    JsonUtility.ToJson(source));

            Assert.That(restored.id, Is.EqualTo(source.id));
            Assert.That(restored.gender, Is.EqualTo(CharacterGender.Other));
            Assert.That(restored.appearance.horns, Is.EqualTo("curved"));
            Assert.That(restored.growthRanks.Get(PlayerStat.Luck),
                Is.EqualTo(StatGrowthRank.S));
        }
    }
}
#endif

using System;
using NUnit.Framework;
using Project.Scripts.DataTypes;
using Project.Scripts.Gameplay;
using Project.Scripts.Persistence;
using UnityEngine;
using Zenject;

namespace Project.Tests.EditMode
{
    public sealed class ComponentDefinitionDataTests
    {
        private GameObject _host;
        private PersistentHealthDefinition _definition;

        [SetUp]
        public void SetUp()
        {
            _host = new GameObject("Component Definition Host");
            _definition =
                ScriptableObject.CreateInstance<PersistentHealthDefinition>();
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(_host);
            UnityEngine.Object.DestroyImmediate(_definition);
        }

        [Test]
        public void DefinitionInstallsUsingInlineNodeData()
        {
            var data = new PersistentHealthData
            {
                componentDefinition = _definition,
                maximumHealth = 175
            };

            _definition.Install(
                _host,
                new DiContainer(),
                default,
                data);

            PersistentHealth health =
                _host.GetComponent<PersistentHealth>();
            Assert.That(health, Is.Not.Null);
            Assert.That(health.MaxHealth, Is.EqualTo(175));
        }

        [Test]
        public void DefinitionRejectsMismatchedData()
        {
            ComponentDefinitionData data =
                new PersistentInventoryData
                {
                    componentDefinition = _definition
                };

            Assert.Throws<InvalidOperationException>(() =>
                _definition.Install(
                    _host,
                    new DiContainer(),
                    default,
                    data));
        }
    }
}

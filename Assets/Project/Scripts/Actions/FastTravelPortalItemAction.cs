using System;
using System.Collections.Generic;
using System.Linq;
using Project.Scripts.DataTypes;
using Project.Scripts.Gameplay;
using Project.Scripts.Interface;
using Project.Scripts.UI;
using UnityEngine;

namespace Project.Scripts.Actions
{
    [CreateAssetMenu(fileName = "New Fast Travel Portal Action", menuName = "Data/Item Actions/Fast Travel Portal")]
    public sealed class FastTravelPortalItemAction : ItemAction
    {
        public override bool CanPerform(ActionContext context) =>
            context.User != null &&
            context.Item != null &&
            context.Item.TryGetActionData<FastTravelPortalActionData>(out _);

        public override bool Perform(ActionContext context)
        {
            if (!CanPerform(context))
                return false;
            PlayerDataController player = context.User.GetComponentInParent<PlayerDataController>();
            IComponentWindowService window = FindAnyObjectByType<ComponentWindowService>();
            IChunkLoader loader = FindChunkLoader();
            if (player == null || window == null || loader == null)
                return false;

            List<Destination> destinations = BuildDestinations(player, loader.WorldSpawnPosition);
            window.Open(new ComponentWindowRequest(
                "Fast Travel",
                new Vector2(440f, 420f),
                new DelegateComponentWindowSection(ui =>
                {
                    GUILayout.Label("Choose a destination");
                    GUILayout.Space(8f);
                    foreach (Destination destination in destinations)
                    {
                        if (!GUILayout.Button(destination.Name, GUILayout.Height(42f)))
                            continue;
                        CreatePortal(context, destination.Position, loader);
                        ui.Close();
                        break;
                    }
                })));
            return true;
        }

        private static List<Destination> BuildDestinations(PlayerDataController player, Vector2Int worldSpawn)
        {
            List<Destination> result = new();
            if (player.TryGetSpawnPoint(out Vector3 spawn))
                result.Add(new Destination("Player Spawn Point", spawn));
            foreach (PlayerDataController.VisitedTown town in player.VisitedTowns)
                result.Add(new Destination(string.IsNullOrWhiteSpace(town.Name) ? "Visited Town" : town.Name, town.SpawnPoint));
            Vector2Int cell = worldSpawn;
            result.Add(new Destination("World Spawn", new Vector3(cell.x + 0.5f, cell.y + 0.5f)));
            return result;
        }

        private static void CreatePortal(ActionContext context, Vector3 destination, IChunkLoader loader)
        {
            if (!context.Item.TryGetActionData<FastTravelPortalActionData>(out var data))
                return;
            Transform player = context.User.transform;
            GameObject prefab = data.portalPrefab != null ? data.portalPrefab : Resources.Load<GameObject>("Portal");
            if (loader == null || prefab == null)
                return;
            GameObject instance = Instantiate(prefab, player.position + (Vector3)data.entranceOffset, Quaternion.identity);
            EventPortalEffect effect = new()
            {
                renderTexture = data.renderTexture,
                destinationWorldPosition = destination,
                textureSize = data.textureSize,
                activationDistance = data.activationDistance,
                transitionDuration = data.transitionDuration,
                creationDuration = data.creationDuration,
                particleSizeMultiplier = data.particleSizeMultiplier,
                startOneShot = data.startOneShot,
                readyOneShot = data.readyOneShot,
                outerRimEffect = data.outerRimEffect
            };
            FastTravelPortal controller = instance.GetComponent<FastTravelPortal>() ??
                                          instance.AddComponent<FastTravelPortal>();
            controller.Initialize(effect, player, loader);
        }

        private static IChunkLoader FindChunkLoader() =>
            FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Exclude)
                .OfType<IChunkLoader>()
                .FirstOrDefault();

        private readonly struct Destination
        {
            public readonly string Name;
            public readonly Vector3 Position;
            public Destination(string name, Vector3 position) { Name = name; Position = position; }
        }
    }
}

using System;
using Project.Scripts.DataTypes;
using Project.Scripts.Interface;
using Project.Scripts.Interface.Decorator;
using UnityEngine;
using Zenject;

namespace Project.Scripts.Gameplay
{
    [DisallowMultipleComponent]
    public sealed class PlaneEntranceComponent :
        MonoBehaviour,
        IInteractable,
        IEntityComponent
    {
        [SerializeField] private string destinationPlaneId = "underground";
        [SerializeField] private string interactionPrompt = "Descend underground";
        [SerializeField, Min(0)] private int destinationSearchRadius = 64;
        [SerializeField, Min(0)] private int destinationClearanceRadius = 2;

        private WorldData _worldData;
        private IWorldGenerator _worldGenerator;
        private PlaneSelection _planeSelection;
        private PlaneData _destinationPlane;
        private Vector2Int _cachedEntranceCell;
        private Vector2Int _cachedDestinationCell;
        private bool _hasCachedDestination;
        private bool _travelling;

        public IPersistentEntity PersistentEntity { get; set; }

        [Inject]
        public void Construct(
            WorldData worldData,
            IWorldGenerator worldGenerator,
            [InjectOptional] PlaneSelection planeSelection)
        {
            _worldData = worldData;
            _worldGenerator = worldGenerator;
            _planeSelection = planeSelection;
        }

        public void Initialize(
            string planeId,
            string prompt,
            int searchRadius,
            int clearanceRadius)
        {
            destinationPlaneId = string.IsNullOrWhiteSpace(planeId)
                ? "underground"
                : planeId.Trim();
            interactionPrompt = string.IsNullOrWhiteSpace(prompt)
                ? "Descend underground"
                : prompt.Trim();
            destinationSearchRadius = Mathf.Max(0, searchRadius);
            destinationClearanceRadius = Mathf.Max(0, clearanceRadius);
            _destinationPlane = null;
            _hasCachedDestination = false;
            _travelling = false;
        }

        public Vector3 GetPosition() => transform.position;

        public bool CanInteract(InteractionContext context)
        {
            return !_travelling &&
                   context.interactionType == InteractionType.Direct &&
                   context.user != null &&
                   context.user.GetComponentInParent<PlayerDataController>() != null &&
                   TryResolveDestination(out _, out _);
        }

        public async void Interact(InteractionContext context)
        {
            if (!CanInteract(context) ||
                !TryResolveDestination(
                    out PlaneData plane,
                    out Vector3 destination))
            {
                return;
            }

            PlayerDataController player =
                context.user.GetComponentInParent<PlayerDataController>();
            _travelling = true;
            try
            {
                await player.TravelToPlaneAsync(plane, destination);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
            }
            finally
            {
                _travelling = false;
            }
        }

        public string GetInteractionPrompt(InteractionContext context) =>
            context.interactionType == InteractionType.Direct
                ? interactionPrompt
                : string.Empty;

        public bool TryResolveDestination(
            out PlaneData plane,
            out Vector3 destination)
        {
            plane = null;
            destination = default;
            if (_worldData == null ||
                _worldGenerator == null ||
                string.IsNullOrWhiteSpace(destinationPlaneId) ||
                !_worldData.TryGetPlane(destinationPlaneId, out plane) ||
                plane.generationPreset == null ||
                string.Equals(
                    _planeSelection?.PlaneId,
                    plane.PersistentId,
                    StringComparison.Ordinal))
            {
                return false;
            }

            if (_destinationPlane != plane)
            {
                _destinationPlane = plane;
                _hasCachedDestination = false;
            }

            Vector2Int entranceCell =
                Vector2Int.FloorToInt(transform.position);
            if (!_hasCachedDestination ||
                entranceCell != _cachedEntranceCell)
            {
                _cachedEntranceCell = entranceCell;
                _hasCachedDestination =
                    _worldGenerator.TryFindSafePortalPosition(
                        plane,
                        entranceCell,
                        out _cachedDestinationCell,
                        destinationSearchRadius,
                        destinationClearanceRadius);
            }

            if (!_hasCachedDestination)
                return false;

            destination = new Vector3(
                _cachedDestinationCell.x + 0.5f,
                _cachedDestinationCell.y + 0.5f,
                0f);
            return true;
        }
    }
}

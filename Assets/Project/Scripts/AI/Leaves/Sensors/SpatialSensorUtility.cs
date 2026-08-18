using UnityEngine;

namespace Project.Scripts.AI.Leaves.Sensors
{
    internal static class SpatialSensorUtility
    {
        public static bool TryGetTransform(
            Blackboard blackboard,
            AiKeys.Key key,
            out Transform transform)
        {
            transform = null;
            return blackboard != null &&
                   blackboard.TryGetValue(
                       AiKeys.Resolve(key), out object value) &&
                   (transform = value switch
                   {
                       Transform candidate when candidate != null => candidate,
                       GameObject gameObject when gameObject != null =>
                           gameObject.transform,
                       Component component when component != null =>
                           component.transform,
                       _ => null
                   }) != null;
        }

        public static bool TryGetPosition(
            Blackboard blackboard,
            AiKeys.Key key,
            out Vector3 position)
        {
            if (blackboard == null ||
                !blackboard.TryGetValue(AiKeys.Resolve(key), out object value))
            {
                position = default;
                return false;
            }

            switch (value)
            {
                case Vector3 vector:
                    position = vector;
                    return true;
                case Vector2 vector:
                    position = vector;
                    return true;
                case Transform transform when transform != null:
                    position = transform.position;
                    return true;
                case GameObject gameObject when gameObject != null:
                    position = gameObject.transform.position;
                    return true;
                case Component component when component != null:
                    position = component.transform.position;
                    return true;
                default:
                    position = default;
                    return false;
            }
        }
    }
}

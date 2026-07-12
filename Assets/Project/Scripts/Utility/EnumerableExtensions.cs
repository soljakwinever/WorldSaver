using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Project.Scripts
{
    public static class EnumerableExtensions
    {
        public static T SelectRandom<T>(this IEnumerable<T> collection)
        {
            var enumerable = collection as T[] ?? collection.ToArray();
            int randomIndex = Random.Range(0, enumerable.Count());
            return enumerable.ElementAt(randomIndex);
        }
        
        public static T SelectHashed<T>(this IEnumerable<T> collection, int x, int y)
        {
            var enumerable = collection as T[] ?? collection.ToArray();
            int randomIndex = Mathf.FloorToInt(Util.Hash01(x, y, 666444) * collection.Count());
            return enumerable.ElementAt(randomIndex);
        }
        
        public static IEnumerable<T> ShuffleXY<T>(this IEnumerable<T> collection, int x, int y){
            return collection.OrderBy(r => Util.Hash01(x, y, r.GetHashCode()));
        }
    }
}
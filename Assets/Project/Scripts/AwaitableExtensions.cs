using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Project.Scripts
{
    public static class AwaitableExtensions
    {
        public static void Forget(this Awaitable awaitable)
        {
            var awaiter = awaitable.GetAwaiter();

            if (awaitable.IsCompleted)
            {
                try
                {
                    awaiter.GetResult();
                }
                catch (Exception ex) when (ex is OperationCanceledException)
                {
                    // Silently swallow OperationCanceledExceptions since this is expected.
                    // Everything else will throw.
                }
            }
            else
            {
                awaiter.OnCompleted(() =>
                {
                    try
                    {
                        awaiter.GetResult();
                    }
                    catch (Exception ex) when (ex is OperationCanceledException)
                    {
                        // Silently swallow OperationCanceledExceptions since this is expected.
                        // Everything else will throw.
                    }
                });
            }
        }

        public static T SelectRandom<T>(this IEnumerable<T> collection)
        {
            var enumerable = collection as T[] ?? collection.ToArray();
            int randomIndex = Random.Range(0, enumerable.Count());
            return enumerable.ElementAt(randomIndex);
        }
    }
}
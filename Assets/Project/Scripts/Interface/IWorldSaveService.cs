using UnityEngine;

namespace Project.Scripts.Interface
{
    public interface IWorldSaveService
    {
        Awaitable SaveAsync();
    }
}

using System;
using UnityEngine;

namespace Project.Scripts.Interface
{
    public interface IInputManager
    {
        InputContext Context { get; }
        Vector2 MousePosition { get; }
        event Action<InputContext> InputPerformed;
    }
}

using System;

namespace Project.Scripts.Interface
{
    public interface IInputManager
    {
        InputContext Context { get; }
        event Action<InputContext> InputPerformed;
    }
}

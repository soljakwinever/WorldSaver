using Project.Scripts.Bus;
using Project.Scripts.DataTypes;
using Project.Scripts.Interface;
using UnityEngine;
using Zenject;

namespace Project.Scripts.Gameplay
{
    public class PlayerToolbarController : MonoBehaviour
    {
        //Injected components
        private IInputManager _inputManager;
        private PlayerBus _playerBus;
        
        private IHotbarAction[] _hotbarActions = new IHotbarAction[10];
        private int _hotbarIndex = 0;
        public IHotbarAction SelectedItemAction => _hotbarActions[_hotbarIndex];
        
        [Inject]
        public void Constract(IInputManager inputManager, PlayerBus playerBus)
        {
            _inputManager = inputManager;
            _inputManager.InputPerformed += InputManagerOnInputPerformed;
            
            _playerBus = playerBus;
        }

        public void SetHotbarAction(int hotbarIndex, IHotbarAction action)
        {
            _hotbarActions[hotbarIndex] = action;
            _playerBus.RaiseHotbarActionSet(hotbarIndex, action);
        }

        private void InputManagerOnInputPerformed(InputContext obj)
        {
            if (obj.HotBarPressed != InputContext.NoHotbarKeyPressed)
            {
                HandleHotbar(obj.HotBarPressed);
            }
        }

        private void HandleHotbar(int objHotBarPressed)
        {
            _hotbarIndex = objHotBarPressed;
            _playerBus.RaiseHotbarIndexChanged(_hotbarIndex, _hotbarActions);
        }
    }
}
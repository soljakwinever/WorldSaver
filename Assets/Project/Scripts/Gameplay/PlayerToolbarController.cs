using Project.Scripts.DataTypes;
using Project.Scripts.Interface;
using UnityEngine;
using Zenject;

namespace Project.Scripts.Gameplay
{
    public class PlayerToolbarController : MonoBehaviour
    {
        private IInputManager _inputManager;
        
        public 
        public ItemAction SelectedItemAction { get; set; }
        
        [Inject]
        public void Constract(IInputManager inputManager)
        {
            _inputManager = inputManager;
            
            _inputManager.InputPerformed += InputManagerOnInputPerformed;
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
            
        }
    }
}
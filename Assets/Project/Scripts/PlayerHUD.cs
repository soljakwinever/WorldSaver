using Project.Scripts.Core;
using Project.Scripts.Gameplay;
using Project.Scripts.Interface;
using UnityEngine;
using UnityEngine.UIElements;
using Zenject;

namespace Project.Scripts
{
    [RequireComponent(typeof(PanelRenderer))]
    public class PlayerHUD : MonoBehaviour
    {
        private PanelRenderer _uiDocument;
    
        [Inject] private PlayerDataController playerDataController;
        private IInputManager inputManager;

        [Inject]
        public void Construct([Inject] IInputManager inputManager)
        {
            this.inputManager = inputManager;
            inputManager.InputPerformed += InputManagerOnInputPerformed;
        }

        private void InputManagerOnInputPerformed(InputContext context)
        {
            if (context.HotBarPressed >= 0)
            {
                
            }
        }

        private void HandleHotBar(int hotbarPressed)
        {
            
        }

        private void Awake()
        {
            _uiDocument = GetComponent<PanelRenderer>();
        }

        // Start is called once before the first execution of Update after the MonoBehaviour is created
        void Start()
        {
            _uiDocument.RegisterUIReloadCallback(ReloadCallback);
        }

        private void ReloadCallback(PanelRenderer panel, VisualElement root)
        {
            root.Q("NeedsDisplay").dataSource = playerDataController;
        }
    }
}

using Project.Scripts.Bus;
using Project.Scripts.Core;
using Project.Scripts.Gameplay;
using Project.Scripts.Interface;
using Project.Scripts.UI;
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
        private PlayerBus playerBus;
        
        private HotbarSlot[] hotbarSlots = new HotbarSlot[10];

        [Inject]
        public void Construct([Inject] PlayerBus playerBus)
        {
            playerBus.hotbarIndexChanged += PlayerBusOnhotbarIndexChanged;
            playerBus.hotbarActionSet += PlayerBusOnhotbarActionSet;
        }

        private void PlayerBusOnhotbarActionSet(int index, IHotbarAction action)
        {
            hotbarSlots[index].Action = action;
        }

        private void PlayerBusOnhotbarIndexChanged(int index, IHotbarAction[] hotbarActions)
        {
            HandleHotBar(index);
        }

        private void HandleHotBar(int hotbarPressed)
        {
            for(int i = 0; i < hotbarSlots.Length; i++)
            {
                hotbarSlots[i].Active = hotbarPressed == i;
            }
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

            var hotbar = root.Q("Toolbar");
            hotbar.Clear();
            
            for (int i = 0; i < hotbarSlots.Length; i++)
            {
                var slot = new HotbarSlot();
                slot.Action = null;
                
                hotbarSlots[i] = slot;

                hotbar.Add(slot);
            }
        }
    }
}

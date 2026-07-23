using System;
using Project.Scripts.Bus;
using UnityEngine;
using UnityEngine.UI;
using Zenject;

namespace Project.Scripts
{
    public class InteractionUiLabel : MonoBehaviour
    {
        [Inject(Id = "WorldUI")] private RectTransform _worldUi;

        [SerializeField] private Text _label;
        
        [Inject] private PlayerBus _playerBus;
        
        private void Awake()
        {
            _playerBus.interactableHovered += (interactable, context) =>
            {
                this.enabled = interactable != null;
                this._label.text = interactable?.GetInteractionPrompt(context) ?? "";
                transform.position = interactable?.GetPosition() ?? Vector3.zero;
            };
        }

        private void OnEnable()
        {
            _label.enabled = true;
        }

        private void OnDisable()
        {
            _label.enabled = false;
        }
    }
}
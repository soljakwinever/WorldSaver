using System;
using Project.Scripts.Gameplay;
using UnityEngine;
using UnityEngine.UIElements;
using Zenject;

[RequireComponent(typeof(UIDocument))]
public class PlayerHUD : MonoBehaviour
{
    private UIDocument _uiDocument;
    
    [Inject] private PlayerDataController playerDataController;
    
    private void Awake()
    {
        _uiDocument = GetComponent<UIDocument>();
    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        _uiDocument.rootVisualElement.Q("NeedsDisplay").dataSource = playerDataController;    
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}

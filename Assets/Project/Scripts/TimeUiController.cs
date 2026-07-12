using System;
using Project.Scripts.GameTime;
using UnityEngine;
using UnityEngine.UIElements;

[RequireComponent(typeof(TimeController), typeof(UIDocument))]
public class TimeUiController : MonoBehaviour
{
    private TimeController _timeController;
    private UIDocument _uiDocument;
    
    private void Awake()
    {
        _timeController = GetComponent<TimeController>();
        _uiDocument = GetComponent<UIDocument>();
    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        _uiDocument.rootVisualElement.Q("TimeUI").dataSource = _timeController;
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}

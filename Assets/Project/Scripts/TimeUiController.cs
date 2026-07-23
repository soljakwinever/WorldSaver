using System;
using Project.Scripts.GameTime;
using UnityEngine;
using UnityEngine.UIElements;

[RequireComponent(typeof(TimeController), typeof(PanelRenderer))]
public class TimeUiController : MonoBehaviour
{
    private TimeController _timeController;
    private PanelRenderer _uiDocument;
    
    private void Awake()
    {
        _timeController = GetComponent<TimeController>();
        _uiDocument = GetComponent<PanelRenderer>();
        
        _uiDocument.RegisterUIReloadCallback(ReloadCallback);
    }

    private void ReloadCallback(PanelRenderer panel, VisualElement root)
    {
        root.Q("TimeUI").dataSource = _timeController;
    }
    
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {

    }

    // Update is called once per frame
    void Update()
    {
        
    }
}

using System;
using UnityEngine;

public class CameraMoverSimple : MonoBehaviour
{
    private Inputs inputs;

    public float speed = 10;
    
    private float currentSpeed;
    
    private void Awake()
    {
        inputs = new Inputs();
    }

    private void OnEnable()
    {
        inputs.Enable();
    }

    private void OnDisable()
    {
        inputs.Disable();
    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        var input = inputs.Player.Move.ReadValue<Vector2>();
        
        transform.Translate(input.x * speed * Time.deltaTime, input.y * speed * Time.deltaTime, 0);
    }
}

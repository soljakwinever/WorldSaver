using System;
using UnityEngine;
using System.Collections;

public class Fps : MonoBehaviour
{

    [SerializeField] private float pollingTime = 0.5f;

    private int frameCount;
    private float time;
    
    int frameRate;
    
    private void Start()
    {
        GUI.depth = 2;
    }
    
    private void OnGUI()
    {
        if (PlayerPrefs.GetInt("WorldSaver.ShowFps", 1) == 0)
            return;

        GUI.Label(new Rect(320, 40, 100, 25), "FPS: " + Mathf.Round(frameRate));
    }

    private void Update()
    {
        // Use unscaledDeltaTime so the counter works even if Time.timeScale is 0
        time += Time.unscaledDeltaTime;
        frameCount++;

        if (time >= pollingTime)
        {
            frameRate = Mathf.RoundToInt(frameCount / time);

            time -= pollingTime;
            frameCount = 0;
        }
    }
}

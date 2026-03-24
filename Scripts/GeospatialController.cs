using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using Google.XR.ARCoreExtensions;
using TMPro; // If you want to use TextMeshPro, otherwise use GUI

public class GeospatialController : MonoBehaviour
{
    public AREarthManager EarthManager;
    public ARSession ARSession;
    private string debugLog = "Initializing...";

    void Update()
    {
        // 1. Check if the session is tracking correctly
        if (ARSession.state != ARSessionState.SessionTracking) return;

        // 2. Check Geospatial tracking state
        var earthTrackingState = EarthManager.EarthTrackingState;
        var pose = EarthManager.CameraGeospatialPose;

        if (earthTrackingState == TrackingState.Tracking)
        {
            debugLog = $"Lat: {pose.Latitude:F6}\n" +
                       $"Lon: {pose.Longitude:F6}\n" +
                       $"Alt: {pose.Altitude:F2}m\n" +
                       $"Accuracy: {pose.HorizontalAccuracy:F2}m";
        }
        else
        {
            debugLog = $"Status: {earthTrackingState} (Move phone to localize)";
        }
    }

    // Quick On-Screen Debugger (No UI setup required)
    void OnGUI()
    {
        GUIStyle style = new GUIStyle();
        style.fontSize = 40;
        style.normal.textColor = Color.white;
        GUI.Label(new Rect(50, 50, 800, 400), debugLog, style);
    }
}
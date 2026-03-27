using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using Google.XR.ARCoreExtensions;

public class GeospatialController : MonoBehaviour
{
    [Header("AR System Components")]
    public AREarthManager EarthManager;
    public ARAnchorManager AnchorManager;
    public ARSession ARSession;
    public GameObject AnchorPrefab; 

    [Header("Target Coordinates (C V Raman Center)")]
    public double TargetLatitude = 22.292246; 
    public double TargetLongitude = 73.363148;
    public double TargetAltitude = 1.73; 
    public float Heading = 0; 

    private ARGeospatialAnchor _resolvedAnchor;
    private string _debugLog = "Initializing...";
    private float _limitedStateTimer = 0f;

    void Update()
    {
        if (ARSession.state != ARSessionState.SessionTracking) return;

        var earthTrackingState = EarthManager.EarthTrackingState;
        var pose = EarthManager.CameraGeospatialPose;

        if (earthTrackingState == TrackingState.Tracking)
        {
            _limitedStateTimer = 0f; 
            _debugLog = $"Lat: {pose.Latitude:F6}\n" +
                       $"Lon: {pose.Longitude:F6}\n" +
                       $"Alt: {pose.Altitude:F2}m\n" +
                       $"Accuracy: {pose.HorizontalAccuracy:F2}m";

            // Only place the anchor once
            if (_resolvedAnchor == null)
            {
                PlaceGeospatialAnchor();
            }
        }
        else
        {
            _debugLog = $"Status: {earthTrackingState} (Move phone to localize)";
            
            // Auto-Reset if stuck in Limited (Indoor to Outdoor transition fix)
            _limitedStateTimer += Time.deltaTime;
            if (_limitedStateTimer >= 10f)
            {
                ManualReset();
            }
        }
    }

    public void PlaceGeospatialAnchor()
    {
        // Spawning 1 meter above recorded ground to ensure visibility
        double spawnAltitude = TargetAltitude + 1.0;
        Quaternion rotation = Quaternion.AngleAxis(Heading, Vector3.up);
        
        _resolvedAnchor = AnchorManager.AddAnchor(TargetLatitude, TargetLongitude, spawnAltitude, rotation);

        if (_resolvedAnchor != null)
        {
            Instantiate(AnchorPrefab, _resolvedAnchor.transform);
            _debugLog = "SUCCESS: ANCHOR PLACED!";
        }
    }

    // This single function handles both your Button and the Auto-Reset
    public void ManualReset()
    {
        if (ARSession != null)
        {
            ARSession.Reset();
            _resolvedAnchor = null; 
            _limitedStateTimer = 0f;
            Debug.Log("AR Session Resetting...");
        }
    }

    void OnGUI()
    {
        GUIStyle style = new GUIStyle();
        style.fontSize = 45;
        style.normal.textColor = Color.red;
        GUI.Label(new Rect(50, 150, 800, 500), _debugLog, style);
    }
} 
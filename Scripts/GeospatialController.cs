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
    public double TargetLatitude  = 22.292246;
    public double TargetLongitude = 73.363148;
    public double TargetAltitude  = 1.73;   // WGS84 ellipsoid altitude (from your GPS reading)
    public float  Heading         = 0f;

    [Header("Spawn Settings")]
    [Tooltip("Extra height above TargetAltitude so the object is clearly visible")]
    public float AltitudeOffset = 1.5f;

    [Tooltip("Minimum horizontal accuracy (metres) required before placing anchor")]
    public float RequiredAccuracy = 8f;

    [Tooltip("Seconds in Limited/None state before auto-reset")]
    public float AutoResetTimeout = 10f;

    // ── private state ────────────────────────────────────────────────────────
    private ARGeospatialAnchor _resolvedAnchor;
    private GameObject         _spawnedObject;
    private string             _debugLog        = "Initializing...";
    private float              _limitedStateTimer = 0f;
    private bool               _anchorFailed    = false;

    // ── Update ───────────────────────────────────────────────────────────────
    void Update()
    {
        // Wait until AR session is fully tracking
        if (ARSession.state != ARSessionState.SessionTracking)
        {
            _debugLog = $"AR Session: {ARSession.state}\n(Waiting for tracking...)";
            return;
        }

        var earthState = EarthManager.EarthTrackingState;
        var pose       = EarthManager.CameraGeospatialPose;

        if (earthState == TrackingState.Tracking)
        {
            _limitedStateTimer = 0f;
            _anchorFailed      = false;

            _debugLog =
                $"Lat:      {pose.Latitude:F6}\n"  +
                $"Lon:      {pose.Longitude:F6}\n" +
                $"Alt:      {pose.Altitude:F2} m\n"  +
                $"H-Acc:    {pose.HorizontalAccuracy:F2} m\n" +
                $"Heading: {pose.EunRotation.eulerAngles.y:F1}°\n"   +
                $"Anchor:   {(_resolvedAnchor != null ? "PLACED ✓" : "Waiting...")}";

            // Only place anchor once, and only when accuracy is good enough
            if (_resolvedAnchor == null && !_anchorFailed)
            {
                if (pose.HorizontalAccuracy <= RequiredAccuracy)
                {
                    PlaceGeospatialAnchor();
                }
                else
                {
                    _debugLog += $"\n\n⚠ Accuracy too low ({pose.HorizontalAccuracy:F1}m)\nMove phone around to improve...";
                }
            }
        }
        else
        {
            _debugLog = $"Earth State: {earthState}\n(Move phone to localize — go outdoors)";

            _limitedStateTimer += Time.deltaTime;
            if (_limitedStateTimer >= AutoResetTimeout)
            {
                _debugLog = "Auto-resetting session...";
                ManualReset();
            }
        }
    }

    // ── Place Anchor ─────────────────────────────────────────────────────────
    public void PlaceGeospatialAnchor()
    {
        if (AnchorPrefab == null)
        {
            _debugLog = "❌ ERROR: AnchorPrefab is not assigned in Inspector!";
            Debug.LogError("GeospatialController: AnchorPrefab is null!");
            return;
        }

        // FIX 1: Use camera's real-time altitude + offset
        //        so the object appears above your current position reliably.
        double spawnAltitude = EarthManager.CameraGeospatialPose.Altitude + AltitudeOffset;

        // FIX 2: Build rotation from Heading
        Quaternion rotation = Quaternion.AngleAxis(Heading, Vector3.up);

        Debug.Log($"[Geospatial] Placing anchor at Lat:{TargetLatitude} Lon:{TargetLongitude} Alt:{spawnAltitude:F2}");

        _resolvedAnchor = AnchorManager.AddAnchor(
            TargetLatitude, TargetLongitude, spawnAltitude, rotation);

        if (_resolvedAnchor != null)
        {
            // FIX 3: Keep reference to spawned object; ensure scale is (1,1,1)
            _spawnedObject = Instantiate(AnchorPrefab, _resolvedAnchor.transform);
            _spawnedObject.transform.localPosition = Vector3.zero;
            _spawnedObject.transform.localRotation = Quaternion.identity;
            _spawnedObject.transform.localScale    = Vector3.one;

            _debugLog = "✅ SUCCESS: ANCHOR PLACED!\n" +
                        $"Spawn alt: {spawnAltitude:F2} m";

            Debug.Log("[Geospatial] Anchor placed successfully.");
        }
        else
        {
            _anchorFailed = true;
            _debugLog = "❌ ANCHOR FAILED (returned null)\n" +
                        "Press Reset and try again outdoors.";
            Debug.LogError("[Geospatial] AnchorManager.AddAnchor() returned null!");
        }
    }

    // ── Manual / Auto Reset ──────────────────────────────────────────────────
    public void ManualReset()
    {
        // Destroy the old spawned object so it doesn't float around
        if (_spawnedObject != null)
        {
            Destroy(_spawnedObject);
            _spawnedObject = null;
        }

        _resolvedAnchor    = null;
        _anchorFailed      = false;
        _limitedStateTimer = 0f;
        _debugLog          = "Session reset — reinitializing...";

        if (ARSession != null)
        {
            ARSession.Reset();
            Debug.Log("[Geospatial] AR Session reset.");
        }
    }

    // ── On-Screen Debug HUD ──────────────────────────────────────────────────
    void OnGUI()
    {
        // Semi-transparent background for readability
        GUI.color = new Color(0f, 0f, 0f, 0.55f);
        GUI.DrawTexture(new Rect(40, 140, 680, 320), Texture2D.whiteTexture);
        GUI.color = Color.white;

        GUIStyle style = new GUIStyle
        {
            fontSize = 38,
            wordWrap = true
        };
        style.normal.textColor = Color.green;
        GUI.Label(new Rect(55, 155, 660, 310), _debugLog, style);

        // Reset button
        GUIStyle btnStyle = new GUIStyle(GUI.skin.button) { fontSize = 36 };
        if (GUI.Button(new Rect(40, 480, 300, 80), "RESET SESSION", btnStyle))
        {
            ManualReset();
        }
    }
}
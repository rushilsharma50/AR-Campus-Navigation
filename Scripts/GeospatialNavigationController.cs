using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using Google.XR.ARCoreExtensions;
using System.Collections.Generic;

// ─────────────────────────────────────────────────────────────────────────────
// GeospatialNavigationController — NFS-style AR Navigation with 3D Arrow
//
// SETUP CHECKLIST:
//   1. Attach this script to a GameObject (e.g. "NavigationManager")
//   2. Drag AR components:
//        EarthManager  → XR Origin (has AREarthManager)
//        AnchorManager → AR Session (has ARAnchorManager)
//        ARSession     → AR Session object
//        ARCamera      → the Camera inside XR Origin
//   3. Create your 3D Arrow prefab (see ARROW PREFAB section below)
//      and assign it to Arrow3DPrefab
//   4. Optionally assign ArrivalUI (a Canvas panel for "You arrived!")
//   5. Fill in Waypoints with real GPS coordinates
//
// ARROW PREFAB — how to make it in Unity:
//   Option A (quick): GameObject > 3D Object > Cylinder
//     - Scale: (0.05, 0.4, 0.05)  ← thin vertical stick
//     - Add a Cone child on top   ← arrowhead
//     - Apply a bright Unlit material (e.g. bright blue or yellow)
//
//   Option B (better): Import a low-poly arrow .fbx from the Asset Store
//     or create one in Blender. The arrow should point along its LOCAL +Y axis
//     so the rotation math below works correctly.
//
//   Option C (easiest code-side): The script can AUTO-CREATE a primitive arrow
//     if you leave Arrow3DPrefab empty — set UseBuiltInArrow = true.
// ─────────────────────────────────────────────────────────────────────────────

[System.Serializable]
public class GPSWaypoint
{
    public string Label;
    public double Latitude;
    public double Longitude;
}

public class GeospatialNavigationController : MonoBehaviour
{
    // ── Inspector: AR Components ──────────────────────────────────────────────
    [Header("AR System Components")]
    public AREarthManager  EarthManager;
    public ARAnchorManager AnchorManager;
    public ARSession       ARSession;
    [Tooltip("Drag the Camera from inside XR Origin here")]
    public Camera          ARCamera;

    // ── Inspector: 3D Arrow ───────────────────────────────────────────────────
    [Header("3D Navigation Arrow")]
    [Tooltip("Your 3D arrow prefab. Must point along local +Y axis. Leave empty to use built-in arrow.")]
    public GameObject Arrow3DPrefab;

    [Tooltip("If true, auto-creates a simple arrow when Arrow3DPrefab is empty")]
    public bool UseBuiltInArrow = true;

    [Tooltip("Distance in front of camera where the arrow floats (metres)")]
    public float ArrowDistance = 1.2f;

    [Tooltip("How far below camera center the arrow sits (metres)")]
    public float ArrowVerticalOffset = -0.3f;

    [Tooltip("Scale of the arrow in the scene")]
    public float ArrowScale = 0.18f;

    [Tooltip("How fast the arrow rotates toward the target (smoothing)")]
    public float ArrowRotationSpeed = 8f;

    // ── Inspector: Waypoints ──────────────────────────────────────────────────
    [Header("Navigation Route")]
    [Tooltip("Add waypoints in order. Last one = destination.")]
    public List<GPSWaypoint> Waypoints = new List<GPSWaypoint>
    {
        new GPSWaypoint
        {
            Label     = "Turn west at main junction",
            Latitude  = 0.0,   // REPLACE: long-press on Google Maps
            Longitude = 0.0,
        },
        new GPSWaypoint
        {
            Label     = "Turn north into campus path",
            Latitude  = 0.0,   // REPLACE: long-press on Google Maps
            Longitude = 0.0,
        },
        new GPSWaypoint
        {
            Label     = "C V Raman Center",
            Latitude  = 22.292246,
            Longitude = 73.363148,
        },
    };

    [Tooltip("Distance in metres to auto-advance to next waypoint")]
    public float WaypointReachRadius = 15f;

    // ── Inspector: Destination Anchor ─────────────────────────────────────────
    [Header("Destination Anchor")]
    public GameObject DestinationPrefab;
    public float      AnchorAltitudeOffset = 1.5f;
    public float      RequiredAccuracy     = 8f;

    // ── Inspector: UI ─────────────────────────────────────────────────────────
    [Header("UI")]
    [Tooltip("Optional: a Canvas panel shown on arrival")]
    public GameObject ArrivalUI;

    // ── Private State ─────────────────────────────────────────────────────────
    private GameObject         _arrow3D;
    private int                _currentWaypointIndex = 0;
    private bool               _navigationActive     = false;
    private bool               _arrived              = false;
    private ARGeospatialAnchor _destinationAnchor;
    private GameObject         _destinationObject;
    private string             _debugLog             = "Initializing...";
    private float              _limitedStateTimer    = 0f;

    // Target bearing we want the arrow to face (smoothed)
    private float _targetArrowBearing = 0f;

    // ─────────────────────────────────────────────────────────────────────────
    void Start()
    {
        Input.compass.enabled = true;
        Input.location.Start();

        if (ArrivalUI != null) ArrivalUI.SetActive(false);

        // Auto-find AR Camera if not assigned
        if (ARCamera == null)
            ARCamera = Camera.main;

        // Build the 3D arrow (hidden until navigation starts)
        BuildArrow();
        if (_arrow3D != null) _arrow3D.SetActive(false);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Arrow Construction
    // ─────────────────────────────────────────────────────────────────────────

    void BuildArrow()
    {
        if (Arrow3DPrefab != null)
        {
            _arrow3D = Instantiate(Arrow3DPrefab);
            _arrow3D.name = "NavArrow3D";
            _arrow3D.transform.localScale = Vector3.one * ArrowScale;
        }
        else if (UseBuiltInArrow)
        {
            _arrow3D = CreateBuiltInArrow();
        }
    }

    /// <summary>
    /// Programmatically builds a simple cylinder + cone arrow
    /// so you don't need an external prefab to get started.
    /// Arrow points along local +Y axis.
    /// </summary>
    GameObject CreateBuiltInArrow()
    {
        // Root
        GameObject root = new GameObject("NavArrow3D");
        root.transform.localScale = Vector3.one * ArrowScale;

        // Bright unlit material — visible even without lighting
        Material mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        if (mat.shader.name == "Hidden/InternalErrorShader")
            mat = new Material(Shader.Find("Unlit/Color")); // fallback for non-URP
        mat.color = new Color(0.1f, 0.6f, 1f); // bright blue

        // Shaft (cylinder, local Y = up)
        GameObject shaft = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        shaft.name = "Shaft";
        shaft.transform.SetParent(root.transform, false);
        shaft.transform.localPosition = new Vector3(0f, 0f, 0f);
        shaft.transform.localScale    = new Vector3(0.18f, 0.5f, 0.18f);
        shaft.GetComponent<Renderer>().material = mat;
        Destroy(shaft.GetComponent<Collider>());

        // Arrowhead (use a rotated cylinder scaled flat = cheap cone substitute)
        GameObject head = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        head.name = "Head";
        head.transform.SetParent(root.transform, false);
        head.transform.localPosition = new Vector3(0f, 0.72f, 0f);
        head.transform.localScale    = new Vector3(0.36f, 0.28f, 0.36f);
        head.GetComponent<Renderer>().material = mat;
        Destroy(head.GetComponent<Collider>());

        // Tail indicator (small flat disk)
        GameObject tail = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        tail.name = "Tail";
        tail.transform.SetParent(root.transform, false);
        tail.transform.localPosition = new Vector3(0f, -0.55f, 0f);
        tail.transform.localScale    = new Vector3(0.25f, 0.05f, 0.25f);
        Material tailMat = new Material(mat);
        tailMat.color = new Color(1f, 0.85f, 0.1f); // yellow tail
        tail.GetComponent<Renderer>().material = tailMat;
        Destroy(tail.GetComponent<Collider>());

        return root;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Public API
    // ─────────────────────────────────────────────────────────────────────────
    public void StartNavigation()
    {
        if (Waypoints == null || Waypoints.Count == 0)
        {
            _debugLog = "No waypoints set!\nAdd GPS coords in Inspector.";
            return;
        }
        for (int i = 0; i < Waypoints.Count; i++)
        {
            if (Waypoints[i].Latitude == 0.0 || Waypoints[i].Longitude == 0.0)
            {
                _debugLog = $"WP {i} ({Waypoints[i].Label})\nhas placeholder 0,0 coords!\nLong-press on Google Maps to get real values.";
                return;
            }
        }

        _currentWaypointIndex = 0;
        _navigationActive     = true;
        _arrived              = false;
        _destinationAnchor    = null;

        if (ArrivalUI != null) ArrivalUI.SetActive(false);
        if (_arrow3D  != null) _arrow3D.SetActive(true);

        _debugLog = $"Navigation started!\nGo to: {Waypoints[0].Label}";
    }

    public void StopNavigation()
    {
        _navigationActive = false;
        if (_arrow3D          != null) _arrow3D.SetActive(false);
        if (ArrivalUI         != null) ArrivalUI.SetActive(false);
        if (_destinationObject != null) Destroy(_destinationObject);
        _destinationAnchor = null;
        _debugLog = "Navigation stopped.";
    }

    public void ManualReset()
    {
        StopNavigation();
        _limitedStateTimer = 0f;
        if (ARSession != null) ARSession.Reset();
        _debugLog = "Session reset...";
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Update
    // ─────────────────────────────────────────────────────────────────────────
    void Update()
    {
        if (ARSession.state != ARSessionState.SessionTracking)
        {
            _debugLog = $"AR Session: {ARSession.state}";
            return;
        }

        var earthState = EarthManager.EarthTrackingState;
        var pose       = EarthManager.CameraGeospatialPose;

        if (earthState != TrackingState.Tracking)
        {
            _debugLog = $"Earth State: {earthState}\n(Go outdoors)";
            _limitedStateTimer += Time.deltaTime;
            if (_limitedStateTimer >= 10f) ManualReset();
            return;
        }

        _limitedStateTimer = 0f;

        double userLat  = pose.Latitude;
        double userLon  = pose.Longitude;
        float  userAlt  = (float)pose.Altitude;
        float  acc      = (float)pose.HorizontalAccuracy;

        _debugLog =
            $"Lat:  {userLat:F6}\n"   +
            $"Lon:  {userLon:F6}\n"   +
            $"Alt:  {userAlt:F1} m\n" +
            $"Acc:  {acc:F1} m\n";

        // Always update 3D arrow position (keeps it locked in front of camera)
        if (_navigationActive && !_arrived)
        {
            GPSWaypoint target          = Waypoints[_currentWaypointIndex];
            bool        isFinalWaypoint = (_currentWaypointIndex == Waypoints.Count - 1);
            double      dist            = HaversineDistance(userLat, userLon, target.Latitude, target.Longitude);
            double      bearing         = CalculateBearing(userLat, userLon, target.Latitude, target.Longitude);

            _debugLog +=
                $"\n-- Navigation --\n"  +
                $"To: {target.Label}\n" +
                $"Dist: {dist:F0} m\n"  +
                $"WP: {_currentWaypointIndex + 1}/{Waypoints.Count}";

            // Update 3D arrow
            Update3DArrow((float)bearing);

            // Waypoint reached?
            if (dist <= WaypointReachRadius)
            {
                if (isFinalWaypoint)
                    OnArrived(pose);
                else
                {
                    _currentWaypointIndex++;
                    _debugLog = $"Waypoint reached!\nNow: {Waypoints[_currentWaypointIndex].Label}";
                }
            }

            // Place destination anchor when close
            if (isFinalWaypoint && _destinationAnchor == null && dist < 50f && acc <= RequiredAccuracy)
                PlaceDestinationAnchor(pose);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 3D Arrow Positioning & Rotation
    // ─────────────────────────────────────────────────────────────────────────

    void Update3DArrow(float bearingDegrees)
    {
        if (_arrow3D == null || ARCamera == null) return;

        // ── Position: lock to a fixed spot in front of the camera ────────────
        // We use camera forward projected onto the horizontal plane
        // so the arrow doesn't tilt when you look up/down
        Vector3 camPos     = ARCamera.transform.position;
        Vector3 camForward = ARCamera.transform.forward;
        camForward.y = 0f;
        if (camForward.sqrMagnitude < 0.001f)
            camForward = ARCamera.transform.up; // fallback when looking straight up/down
        camForward.Normalize();

        Vector3 arrowPos = camPos
            + camForward          * ArrowDistance
            + Vector3.up          * ArrowVerticalOffset;

        _arrow3D.transform.position = arrowPos;

        // ── Rotation: point arrow toward the GPS bearing ──────────────────────
        // bearingDegrees is a compass bearing: 0=North, 90=East, 180=South, 270=West
        // In Unity world space: North = +Z (typically), East = +X
        // We convert the compass bearing to a Unity world-space Y rotation:
        //   Unity Y rotation 0   = facing +Z (forward = north)
        //   Unity Y rotation 90  = facing +X (east)
        //   Unity Y rotation 180 = facing -Z (south)
        //   Unity Y rotation 270 = facing -X (west)
        // Compass bearing maps directly: bearing 90 (East) = Unity Y 90
        // So: targetYRotation = bearingDegrees  (they share the same convention)

        float targetYRotation = bearingDegrees;

        // Smooth rotation so the arrow doesn't snap
        _targetArrowBearing = Mathf.LerpAngle(_targetArrowBearing, targetYRotation, Time.deltaTime * ArrowRotationSpeed);

        // The arrow prefab points along its local +Y axis upward by default.
        // We want it to point horizontally toward the bearing, so we tilt it:
        //   - Rotate around world Y by the bearing (compass direction)
        //   - Then tilt 90 degrees so it lays horizontal (pointing forward not up)
        // Final rotation = Y bearing * X -90 tilt
        _arrow3D.transform.rotation = Quaternion.Euler(-90f, _targetArrowBearing, 0f);
    }

    // ─────────────────────────────────────────────────────────────────────────
    void OnArrived(GeospatialPose pose)
    {
        _arrived          = true;
        _navigationActive = false;
        if (_arrow3D  != null) _arrow3D.SetActive(false);
        if (ArrivalUI != null) ArrivalUI.SetActive(true);
        _debugLog = "YOU HAVE ARRIVED!\nC V Raman Center";
        if (_destinationAnchor == null) PlaceDestinationAnchor(pose);
    }

    void PlaceDestinationAnchor(GeospatialPose pose)
    {
        if (DestinationPrefab == null || AnchorManager == null) return;
        GPSWaypoint dest   = Waypoints[Waypoints.Count - 1];
        double      spawnAlt = pose.Altitude + AnchorAltitudeOffset;
        _destinationAnchor = AnchorManager.AddAnchor(
            dest.Latitude, dest.Longitude, spawnAlt, Quaternion.identity);
        if (_destinationAnchor != null)
        {
            _destinationObject = Instantiate(DestinationPrefab, _destinationAnchor.transform);
            _destinationObject.transform.localPosition = Vector3.zero;
            _destinationObject.transform.localScale    = Vector3.one;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GPS Math
    // ─────────────────────────────────────────────────────────────────────────
    public static double HaversineDistance(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6371000;
        double dLat = ToRad(lat2 - lat1);
        double dLon = ToRad(lon2 - lon1);
        double a = Mathf.Sin((float)(dLat / 2)) * Mathf.Sin((float)(dLat / 2)) +
                   Mathf.Cos((float)ToRad(lat1)) * Mathf.Cos((float)ToRad(lat2)) *
                   Mathf.Sin((float)(dLon / 2)) * Mathf.Sin((float)(dLon / 2));
        double c = 2 * Mathf.Atan2(Mathf.Sqrt((float)a), Mathf.Sqrt((float)(1 - a)));
        return R * c;
    }

    public static double CalculateBearing(double lat1, double lon1, double lat2, double lon2)
    {
        double dLon  = ToRad(lon2 - lon1);
        double lat1R = ToRad(lat1);
        double lat2R = ToRad(lat2);
        double y     = Mathf.Sin((float)dLon) * Mathf.Cos((float)lat2R);
        double x     = Mathf.Cos((float)lat1R) * Mathf.Sin((float)lat2R) -
                       Mathf.Sin((float)lat1R) * Mathf.Cos((float)lat2R) * Mathf.Cos((float)dLon);
        return ((Mathf.Atan2((float)y, (float)x) * Mathf.Rad2Deg) + 360.0) % 360.0;
    }

    static double ToRad(double deg) => deg * Mathf.Deg2Rad;

    // ─────────────────────────────────────────────────────────────────────────
    // Debug HUD
    // ─────────────────────────────────────────────────────────────────────────
    void OnGUI()
    {
        GUI.color = new Color(0f, 0f, 0f, 0.55f);
        GUI.DrawTexture(new Rect(40, 140, 680, 350), Texture2D.whiteTexture);
        GUI.color = Color.white;

        GUIStyle style = new GUIStyle { fontSize = 34, wordWrap = true };
        style.normal.textColor = Color.green;
        GUI.Label(new Rect(55, 150, 660, 335), _debugLog, style);

        GUIStyle btn = new GUIStyle(GUI.skin.button) { fontSize = 32 };

        if (!_navigationActive && !_arrived)
        {
            if (GUI.Button(new Rect(40, 510, 300, 70), "START NAV", btn))
                StartNavigation();
        }
        else
        {
            if (GUI.Button(new Rect(40, 510, 300, 70), "STOP NAV", btn))
                StopNavigation();
        }

        if (GUI.Button(new Rect(360, 510, 260, 70), "RESET", btn))
            ManualReset();
    }
}
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using Google.XR.ARCoreExtensions;
using System.Collections;
using System.Collections.Generic;

#if UNITY_ANDROID
using UnityEngine.Android;
#endif

// =============================================================================
//  GeospatialNavigationController  —  Modular AR Navigation (OnePlus 11 5G)
//
//  ARCHITECTURE MODULES
//  ┌─────────────────────────────────────────────────────────────────────────┐
//  │  M1  PermissionModule      – runtime Camera + FineLocation requests     │
//  │  M2  HardwareInitModule    – ARSession.Ready guard + GeospatialMode     │
//  │  M3  CoordinateModule      – EUN-based bearing, Haversine distance       │
//  │  M4  ArrowModule           – standalone world-space arrow, Slerp smooth │
//  │  M5  RouteModule           – waypoint list, auto-advance, arrival        │
//  │  M6  AnchorModule          – destination geospatial anchor              │
//  │  M7  HUDModule             – OnGUI overlay: lat/lon/alt/acc + buttons   │
//  │  M8  GPSPollModule         – 0.5-sec throttle for GPS bearing calc      │
//  │  M9  SessionResetModule    – ARSession.Reset + anchor cleanup           │
//  └─────────────────────────────────────────────────────────────────────────┘
//
//  FIXES vs previous version
//  • Black camera  → ARSessionState.Ready guard before nav + explicit perms
//  • Back-right arrow → Arrow is a STANDALONE world object (not camera child)
//                       Bearing = GPS bearing − pose.EunRotation.eulerAngles.y
//  • GPS laziness  → bearing/distance recalculated every 0.5 s (M8)
//  • Jitter        → Quaternion.Slerp every frame (M4)
//  • HUD           → Lat / Lon / Alt / HorizontalAccuracy always visible (M7)
//  • Reset         → ResetGPSSession() clears anchors + calls ARSession.Reset
// =============================================================================

// ── Shared data types ─────────────────────────────────────────────────────────
[System.Serializable]
public class GPSWaypoint
{
    public string Label;
    public double Latitude;
    public double Longitude;
}

// =============================================================================
public class GeospatialNavigationController : MonoBehaviour
{
    // =========================================================================
    //  INSPECTOR FIELDS
    // =========================================================================

    [Header("─── M2 · AR Hardware ───────────────────────────────")]
    [Tooltip("AREarthManager from the XR Origin hierarchy")]
    public AREarthManager EarthManager;

    [Tooltip("ARAnchorManager from the XR Origin hierarchy")]
    public ARAnchorManager AnchorManager;

    [Tooltip("ARSession object in the scene")]
    public ARSession ARSession;

    [Tooltip("The ARCoreExtensionsConfig asset — GeospatialMode must be Enabled")]
    public ARCoreExtensionsConfig ARCoreConfig;

    [Tooltip("Main camera inside XR Origin")]
    public Camera ARCamera;

    // ── M4 · Arrow ────────────────────────────────────────────────────────────
    [Header("─── M4 · Navigation Arrow ──────────────────────────")]
    [Tooltip("Optional custom prefab. Leave empty to use the built-in chevron.")]
    public GameObject Arrow3DPrefab;
    public bool UseBuiltInArrow = true;

    [Tooltip("Metres in front of camera the arrow floats")]
    public float ArrowDistance = 1.5f;

    [Tooltip("Vertical offset (negative = below camera centre)")]
    public float ArrowVerticalOffset = -0.25f;

    [Tooltip("World-space scale (metres)")]
    public float ArrowScale = 0.25f;

    [Tooltip("Slerp speed — higher = snappier, NFS feel")]
    public float ArrowRotationSpeed = 7f;

    [Tooltip("Rotation offset to fix prefab baked orientation.\n"
           + "If prefab tip points UP (+Y) → set X = -90\n"
           + "If prefab tip points DOWN (-Y) → set X = 90\n"
           + "Built-in chevron auto-sets this to 0,0,0.")]
    public Vector3 ArrowRotationOffset = new Vector3(-90f, 0f, 0f);

    // ── M5 · Route ────────────────────────────────────────────────────────────
    [Header("─── M5 · Route Waypoints ───────────────────────────")]
    public List<GPSWaypoint> Waypoints = new List<GPSWaypoint>
    {
        new GPSWaypoint { Label = "Main Gate",        Latitude = 22.287688, Longitude = 73.364648 },
        new GPSWaypoint { Label = "PU Circle",        Latitude = 22.289500, Longitude = 73.363500 },
        new GPSWaypoint { Label = "C V Raman Center", Latitude = 22.292246, Longitude = 73.363148 },
    };

    [Tooltip("Reach radius in metres — auto-advance to next waypoint")]
    public float WaypointReachRadius = 15f;

    // ── M6 · Anchor ───────────────────────────────────────────────────────────
    [Header("─── M6 · Destination Anchor ────────────────────────")]
    public GameObject DestinationPrefab;
    public float      AnchorAltitudeOffset = 1.5f;
    public float      RequiredAccuracy     = 8f;

    // ── M7 · HUD ──────────────────────────────────────────────────────────────
    [Header("─── M7 · UI / HUD ──────────────────────────────────")]
    public GameObject ArrivalUI;
    public bool       ShowGPSHUD = true;

    // ── M8 · GPS Poll ─────────────────────────────────────────────────────────
    [Header("─── M8 · GPS Polling ───────────────────────────────")]
    [Tooltip("Seconds between GPS bearing/distance recalculation")]
    public float GPSPollInterval = 0.5f;

    // =========================================================================
    //  PRIVATE STATE
    // =========================================================================

    // M1 – permissions
    private bool   _permOK          = false;

    // M2 – hardware init
    private bool   _arReady         = false;
    private float  _noTrackTimer    = 0f;
    private const float NoTrackTimeout = 15f;

    // M3 – coordinate / bearing cache (updated by M8)
    private double _cachedBearing   = 0.0;
    private double _cachedDistance  = 0.0;

    // M4 – arrow
    private GameObject _arrow3D;
    private Quaternion _smoothRot   = Quaternion.identity;

    // M5 – route
    private int    _waypointIdx     = 0;
    private bool   _navActive       = false;
    private bool   _arrived         = false;

    // M6 – anchor
    private ARGeospatialAnchor _destAnchor;
    private GameObject         _destObject;

    // M7 – HUD log
    private string _hudLog          = "Requesting permissions...";

    // M8 – GPS poll timer
    private float  _pollTimer       = 0f;

    // Last known geospatial pose (updated each valid frame)
    private GeospatialPose _lastPose;
    private bool           _hasPose = false;

    // =========================================================================
    //  M1  PERMISSION MODULE
    // =========================================================================
    void Start()
    {
        if (ArrivalUI != null) ArrivalUI.SetActive(false);
        if (ARCamera  == null) ARCamera = Camera.main;
        StartCoroutine(M1_RequestPermissionsAndInit());
    }

    IEnumerator M1_RequestPermissionsAndInit()
    {
#if UNITY_ANDROID
        // ── Camera ────────────────────────────────────────────────────────────
        if (!Permission.HasUserAuthorizedPermission(Permission.Camera))
        {
            _hudLog = "Requesting CAMERA permission…";

            // Use PermissionCallbacks for Android 12+ graceful fallback
            var camCallbacks = new PermissionCallbacks();
            camCallbacks.PermissionGranted          += _ => {};
            camCallbacks.PermissionDenied           += _ => {};
            camCallbacks.PermissionDeniedAndDontAskAgain += _ => {};
            Permission.RequestUserPermission(Permission.Camera, camCallbacks);
            yield return new WaitForSeconds(2f);        // wait for dialog
        }

        // ── Fine Location ─────────────────────────────────────────────────────
        if (!Permission.HasUserAuthorizedPermission(Permission.FineLocation))
        {
            _hudLog = "Requesting LOCATION permission…";
            var locCallbacks = new PermissionCallbacks();
            locCallbacks.PermissionGranted          += _ => {};
            locCallbacks.PermissionDenied           += _ => {};
            locCallbacks.PermissionDeniedAndDontAskAgain += _ => {};
            Permission.RequestUserPermission(Permission.FineLocation, locCallbacks);
            yield return new WaitForSeconds(2f);
        }

        bool camOK = Permission.HasUserAuthorizedPermission(Permission.Camera);
        bool locOK = Permission.HasUserAuthorizedPermission(Permission.FineLocation);

        if (!camOK || !locOK)
        {
            _hudLog = $"PERMISSIONS DENIED!\n" +
                      $"Camera:{camOK}  Location:{locOK}\n" +
                      $"Settings › Apps › AR Navigation\n" +
                      $"› Permissions › Allow Camera + Location";
            yield break;
        }
#endif
        _permOK = true;

        // Start Android sensors only after permissions are confirmed
        Input.compass.enabled = true;
        yield return new WaitForSeconds(0.3f);
        Input.location.Start(1f, 0.5f);

        // M2 – now wait for AR to become ready
        yield return M2_WaitForARReady();
    }

    // =========================================================================
    //  M2  HARDWARE INIT MODULE
    // =========================================================================
    IEnumerator M2_WaitForARReady()
    {
        _hudLog = "Waiting for ARSession.Ready…\n(go outdoors, point at surroundings)";

        // ── Guard: GeospatialMode must be Enabled ─────────────────────────────
        if (ARCoreConfig != null)
        {
            if (ARCoreConfig.GeospatialMode != GeospatialMode.Enabled)
            {
                _hudLog = "ERROR: GeospatialMode is NOT Enabled!\n" +
                          "Open your ARCoreExtensionsConfig asset\n" +
                          "and set GeospatialMode = Enabled.";
                yield break;
            }
        }
        else
        {
            Debug.LogWarning("[M2] ARCoreConfig reference not set — cannot verify GeospatialMode.");
        }

        // ── Wait for ARSessionState.Ready ─────────────────────────────────────
        float timeout = 0f;
        while (ARSession.state != ARSessionState.Ready &&
               ARSession.state != ARSessionState.SessionTracking)
        {
            _hudLog = $"AR Session: {ARSession.state}\n(Point camera at outdoor surroundings)";
            timeout += Time.deltaTime;
            if (timeout > 30f)
            {
                _hudLog = "AR Session timed out.\nTap RESET or restart outdoors.";
                yield break;
            }
            yield return null;
        }

        _arReady = true;
        _hudLog  = "AR Ready. Initializing Earth tracking…";

        // Build the arrow now — it will float as a standalone world object
        M4_BuildArrow();
        if (_arrow3D != null) _arrow3D.SetActive(false);
    }

    // =========================================================================
    //  M3  COORDINATE SPACE MODULE
    // =========================================================================
    /// <summary>
    /// Returns the GPS bearing from (lat1, lon1) to (lat2, lon2) in [0, 360).
    /// 0° = North, 90° = East  (clockwise, same as compass).
    /// </summary>
    public static double CalculateBearing(double lat1, double lon1,
                                          double lat2, double lon2)
    {
        double dLon = ToRad(lon2 - lon1);
        double y = System.Math.Sin(dLon) * System.Math.Cos(ToRad(lat2));
        double x = System.Math.Cos(ToRad(lat1)) * System.Math.Sin(ToRad(lat2))
                 - System.Math.Sin(ToRad(lat1)) * System.Math.Cos(ToRad(lat2))
                                                 * System.Math.Cos(dLon);
        double brng = System.Math.Atan2(y, x) * Mathf.Rad2Deg;
        return (brng + 360.0) % 360.0;
    }

    /// <summary>Haversine great-circle distance in metres.</summary>
    public static double HaversineDistance(double lat1, double lon1,
                                           double lat2, double lon2)
    {
        const double R    = 6_371_000.0;
        double       dLat = ToRad(lat2 - lat1);
        double       dLon = ToRad(lon2 - lon1);
        double       a    = System.Math.Sin(dLat / 2) * System.Math.Sin(dLat / 2)
                          + System.Math.Cos(ToRad(lat1)) * System.Math.Cos(ToRad(lat2))
                          * System.Math.Sin(dLon / 2)  * System.Math.Sin(dLon / 2);
        return R * 2.0 * System.Math.Atan2(System.Math.Sqrt(a), System.Math.Sqrt(1.0 - a));
    }

    static double ToRad(double d) => d * Mathf.Deg2Rad;

    // =========================================================================
    //  M4  ARROW MODULE
    // =========================================================================
    void M4_BuildArrow()
    {
        if (Arrow3DPrefab != null)
        {
            _arrow3D = Instantiate(Arrow3DPrefab);
            _arrow3D.name = "NavArrow3D";
            _arrow3D.transform.localScale = Vector3.one * ArrowScale;
            // Custom prefab: use inspector ArrowRotationOffset (default -90,0,0 for Y-up models)
        }
        else if (UseBuiltInArrow)
        {
            _arrow3D = M4_MakeChevronArrow();
            // Built-in chevron already points along +Z — no offset needed
            ArrowRotationOffset = Vector3.zero;
        }

        // CRITICAL: arrow lives in WORLD SPACE — NOT parented to camera
        if (_arrow3D != null)
            _arrow3D.transform.SetParent(null);
    }

    /// <summary>
    /// Builds a flat chevron (›) pointing along local +Z.
    /// Uses URP Unlit if available, falls back to Legacy Unlit/Color.
    /// </summary>
    GameObject M4_MakeChevronArrow()
    {
        GameObject root = new GameObject("NavArrow3D");
        root.transform.localScale = Vector3.one * ArrowScale;

        Material blue = new Material(
            Shader.Find("Universal Render Pipeline/Unlit") ??
            Shader.Find("Unlit/Color"));
        blue.color = new Color(0.15f, 0.55f, 1f);   // vivid blue

        Material yellow = new Material(blue);
        yellow.color = new Color(1f, 0.85f, 0.1f);  // accent yellow

        // Left wing  ─ tilted -45° to form the left arm of the chevron
        GameObject left = GameObject.CreatePrimitive(PrimitiveType.Cube);
        left.transform.SetParent(root.transform, false);
        left.transform.localPosition = new Vector3(-0.35f, 0f, -0.15f);
        left.transform.localRotation = Quaternion.Euler(0f, -45f, 0f);
        left.transform.localScale    = new Vector3(0.12f, 0.12f, 0.85f);
        left.GetComponent<Renderer>().material = blue;
        Destroy(left.GetComponent<Collider>());

        // Right wing
        GameObject right = GameObject.CreatePrimitive(PrimitiveType.Cube);
        right.transform.SetParent(root.transform, false);
        right.transform.localPosition = new Vector3(0.35f, 0f, -0.15f);
        right.transform.localRotation = Quaternion.Euler(0f, 45f, 0f);
        right.transform.localScale    = new Vector3(0.12f, 0.12f, 0.85f);
        right.GetComponent<Renderer>().material = blue;
        Destroy(right.GetComponent<Collider>());

        // Tip cap (small yellow cube at the front of +Z)
        GameObject tip = GameObject.CreatePrimitive(PrimitiveType.Cube);
        tip.transform.SetParent(root.transform, false);
        tip.transform.localPosition = new Vector3(0f, 0f, 0.5f);
        tip.transform.localScale    = new Vector3(0.18f, 0.18f, 0.18f);
        tip.GetComponent<Renderer>().material = yellow;
        Destroy(tip.GetComponent<Collider>());

        return root;
    }

    /// <summary>
    /// M4 core — updates arrow position (in front of camera) and heading
    /// using EUN-space bearing to get real-world alignment.
    ///
    /// ARCore EUN (East-Up-North) coordinate system:
    ///   +X = East,  +Y = Up,  +Z = North
    ///
    /// Unity's Y-axis euler rotation convention (left-handed):
    ///   Positive Y rotation goes from +Z toward +X (clockwise from above).
    ///   So:  eulerY = 0°  → facing +Z → facing North
    ///        eulerY = 90° → facing +X → facing East
    ///   This is ALREADY compass heading (degrees CW from North).
    ///   No conversion formula needed.
    ///
    /// relativeAngle = gpsBearing - eunY
    ///   → how far to rotate from phone-forward to reach the target.
    /// </summary>
    void M4_UpdateArrow(GeospatialPose pose)
    {
        if (_arrow3D == null || ARCamera == null) return;

        // ── Position: float in front of camera (horizontal plane) ─────────────
        Vector3 camPos      = ARCamera.transform.position;
        Vector3 camForwardH = ARCamera.transform.forward;
        camForwardH.y = 0f;
        if (camForwardH.sqrMagnitude < 0.001f) camForwardH = Vector3.forward;
        camForwardH.Normalize();

        _arrow3D.transform.position =
            camPos
            + camForwardH * ArrowDistance
            + Vector3.up  * ArrowVerticalOffset;

        // ── Rotation: EUN-based real-world pointing ────────────────────────────
        //  eunY IS the compass heading — no conversion needed.
        //  0° = North, 90° = East, 180° = South, 270° = West
        float eunY       = pose.EunRotation.eulerAngles.y;

        // GPS bearing to next waypoint (same convention: 0=N, 90=E, CW)
        float gpsBearing = (float)_cachedBearing;

        // How far to rotate from phone-forward to reach the target
        float relativeAngle = gpsBearing - eunY;

        // Build world-space direction: rotate the horizontal camera-forward
        // by the relative angle around the world-up axis.
        Vector3 camRight = ARCamera.transform.right;
        camRight.y = 0f;
        if (camRight.sqrMagnitude < 0.001f) camRight = Vector3.right;
        camRight.Normalize();

        float rad      = relativeAngle * Mathf.Deg2Rad;
        Vector3 dirWorld =
            camForwardH * Mathf.Cos(rad) +
            camRight    * Mathf.Sin(rad);
        dirWorld.y = 0f;

        if (dirWorld.sqrMagnitude < 0.001f) return;
        dirWorld.Normalize();

        // LookRotation → +Z points at the horizontal target direction.
        // Post-multiply ArrowRotationOffset to compensate for the prefab's
        // baked model orientation (e.g. a model whose tip points along +Y
        // needs a 90° X offset to lay it flat in the horizontal plane).
        Quaternion targetRot = Quaternion.LookRotation(dirWorld, Vector3.up)
                             * Quaternion.Euler(ArrowRotationOffset);

        // M4 Slerp: smooth, NFS-style rotation every frame
        _smoothRot = Quaternion.Slerp(
            _smoothRot,
            targetRot,
            Time.deltaTime * ArrowRotationSpeed);

        _arrow3D.transform.rotation = _smoothRot;
    }

    // =========================================================================
    //  M5  ROUTE MODULE
    // =========================================================================
    public void StartNavigation()
    {
        if (!_permOK || !_arReady)
        {
            _hudLog = _permOK ? "AR not ready yet — wait for tracking." : "Permissions not granted.";
            return;
        }
        if (Waypoints == null || Waypoints.Count == 0)
        {
            _hudLog = "No waypoints configured!";
            return;
        }

        _waypointIdx = 0;
        _navActive   = true;
        _arrived     = false;
        _destAnchor  = null;
        _pollTimer   = GPSPollInterval;  // trigger first calc immediately

        if (ArrivalUI  != null) ArrivalUI.SetActive(false);
        if (_arrow3D   != null) _arrow3D.SetActive(true);

        _hudLog = $"Navigation started!\nTo: {Waypoints[0].Label}";
    }

    public void StopNavigation()
    {
        _navActive = false;
        if (_arrow3D   != null) _arrow3D.SetActive(false);
        if (ArrivalUI  != null) ArrivalUI.SetActive(false);
        if (_destObject != null) { Destroy(_destObject); _destObject = null; }
        _destAnchor = null;
        _hudLog = "Navigation stopped.";
    }

    void M5_CheckWaypointAdvance(GeospatialPose pose)
    {
        GPSWaypoint wp      = Waypoints[_waypointIdx];
        bool        isFinal = (_waypointIdx == Waypoints.Count - 1);

        if (_cachedDistance <= WaypointReachRadius)
        {
            if (isFinal)
            {
                M6_OnArrived(pose);
            }
            else
            {
                _waypointIdx++;
                _pollTimer = GPSPollInterval;  // force immediate recalc for new target
                _hudLog    = $"Waypoint reached!\nNow heading to: {Waypoints[_waypointIdx].Label}";
            }
        }

        // Place destination anchor when close enough and accuracy is good
        if (isFinal && _destAnchor == null &&
            _cachedDistance < 50.0 &&
            (float)pose.HorizontalAccuracy <= RequiredAccuracy)
        {
            M6_PlaceDestinationAnchor(pose);
        }
    }

    // =========================================================================
    //  M6  ANCHOR MODULE
    // =========================================================================
    void M6_OnArrived(GeospatialPose pose)
    {
        _arrived   = true;
        _navActive = false;
        if (_arrow3D  != null) _arrow3D.SetActive(false);
        if (ArrivalUI != null) ArrivalUI.SetActive(true);
        _hudLog = $"YOU HAVE ARRIVED!\n{Waypoints[Waypoints.Count - 1].Label}";
        if (_destAnchor == null) M6_PlaceDestinationAnchor(pose);
    }

    void M6_PlaceDestinationAnchor(GeospatialPose pose)
    {
        if (DestinationPrefab == null || AnchorManager == null) return;
        GPSWaypoint dest     = Waypoints[Waypoints.Count - 1];
        double      spawnAlt = pose.Altitude + AnchorAltitudeOffset;
        _destAnchor = AnchorManager.AddAnchor(
            dest.Latitude, dest.Longitude, spawnAlt, Quaternion.identity);

        if (_destAnchor != null)
        {
            _destObject = Instantiate(DestinationPrefab, _destAnchor.transform);
            _destObject.transform.localPosition = Vector3.zero;
            _destObject.transform.localScale    = Vector3.one;
        }
    }

    // =========================================================================
    //  M7  HUD MODULE  (OnGUI overlay)
    // =========================================================================
    void OnGUI()
    {
        // ── Background panel ─────────────────────────────────────────────────
        GUI.color = new Color(0f, 0f, 0f, 0.72f);
        GUI.DrawTexture(new Rect(10, 10, 540, 330), Texture2D.whiteTexture);
        GUI.color = Color.white;

        GUIStyle logStyle = new GUIStyle { fontSize = 30, wordWrap = true };
        logStyle.normal.textColor = Color.cyan;
        GUI.Label(new Rect(18, 16, 520, 318), _hudLog, logStyle);

        // ── Action buttons ───────────────────────────────────────────────────
        GUIStyle btn = new GUIStyle(GUI.skin.button) { fontSize = 28 };

        float btnY = 350f;

        if (!_navActive && !_arrived)
        {
            if (GUI.Button(new Rect(10,  btnY, 240, 65), "START NAV", btn))
                StartNavigation();
        }
        else
        {
            if (GUI.Button(new Rect(10, btnY, 240, 65), "STOP NAV", btn))
                StopNavigation();
        }

        if (GUI.Button(new Rect(265, btnY, 200, 65), "RESET GPS", btn))
            M9_ResetGPSSession();
    }

    // =========================================================================
    //  M8  GPS POLL MODULE
    // =========================================================================
    /// <summary>
    /// Recalculates bearing and distance to the active waypoint every
    /// GPSPollInterval seconds (default 0.5 s) to handle GPS laziness
    /// without hammering the CPU every frame.
    /// </summary>
    void M8_PollGPS(GeospatialPose pose)
    {
        _pollTimer += Time.deltaTime;
        if (_pollTimer < GPSPollInterval) return;
        _pollTimer = 0f;

        GPSWaypoint wp = Waypoints[_waypointIdx];
        _cachedBearing  = CalculateBearing(
            pose.Latitude, pose.Longitude, wp.Latitude, wp.Longitude);
        _cachedDistance = HaversineDistance(
            pose.Latitude, pose.Longitude, wp.Latitude, wp.Longitude);
    }

    // =========================================================================
    //  M9  SESSION RESET MODULE
    // =========================================================================
    /// <summary>
    /// Public API — call from UI button or CampusMapManager.
    /// Destroys all anchors, resets the ARSession, and clears nav state
    /// so Geospatial localization can re-converge cleanly.
    /// </summary>
    public void M9_ResetGPSSession()
    {
        // Stop navigation and destroy existing anchor objects
        StopNavigation();

        // Destroy tracked anchor GameObject if any
        if (_destObject != null) { Destroy(_destObject); _destObject = null; }
        _destAnchor = null;

        // Reset the ARSession — forces full re-localization
        if (ARSession != null) ARSession.Reset();

        // Re-arm the hardware-ready gate so M2 can re-run
        _arReady      = false;
        _hasPose      = false;
        _noTrackTimer = 0f;

        _hudLog = "GPS Session Reset.\nAR re-initializing…";

        // Re-enter the hardware init coroutine
        StartCoroutine(M2_WaitForARReady());
    }

    // =========================================================================
    //  MAIN UPDATE LOOP
    // =========================================================================
    void Update()
    {
        // M1 guard
        if (!_permOK)  return;
        // M2 guard — do not run nav logic until AR is ready
        if (!_arReady) return;

        // ── Wait for session tracking ─────────────────────────────────────────
        if (ARSession.state != ARSessionState.SessionTracking)
        {
            _hudLog = $"AR Session: {ARSession.state}\n(Point camera at surroundings)";
            return;
        }

        // ── Wait for Earth tracking ───────────────────────────────────────────
        if (EarthManager.EarthTrackingState != TrackingState.Tracking)
        {
            _hudLog = $"Earth: {EarthManager.EarthTrackingState}\n" +
                      $"Walk outdoors and sweep phone slowly.";
            _noTrackTimer += Time.deltaTime;
            if (_noTrackTimer >= NoTrackTimeout)
            {
                _noTrackTimer = 0f;
                M9_ResetGPSSession();
            }
            return;
        }
        _noTrackTimer = 0f;

        // ── Capture valid geospatial pose ─────────────────────────────────────
        GeospatialPose pose = EarthManager.CameraGeospatialPose;
        _lastPose = pose;
        _hasPose  = true;

        // ── M7 · HUD — always show GPS telemetry ─────────────────────────────
        if (ShowGPSHUD)
        {
            _hudLog = $"Lat:  {pose.Latitude:F6}\n" +
                      $"Lon:  {pose.Longitude:F6}\n" +
                      $"Alt:  {pose.Altitude:F1} m\n" +
                      $"Acc:  ±{pose.HorizontalAccuracy:F1} m";
        }

        // ── Navigation logic ──────────────────────────────────────────────────
        if (!_navActive || _arrived) return;

        // M8 · throttled GPS math
        M8_PollGPS(pose);

        // Append nav info to HUD
        GPSWaypoint wp = Waypoints[_waypointIdx];
        _hudLog += $"\n── Navigation ──\n" +
                   $"To: {wp.Label}\n" +
                   $"Dist:  {_cachedDistance:F0} m\n" +
                   $"Bearing: {_cachedBearing:F0}°\n" +
                   $"WP {_waypointIdx + 1}/{Waypoints.Count}";

        // M4 · update arrow using EUN-corrected heading
        M4_UpdateArrow(pose);

        // M5 · check if we've reached the waypoint
        M5_CheckWaypointAdvance(pose);
    }
}
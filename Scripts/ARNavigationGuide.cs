using UnityEngine;
using Google.XR.ARCoreExtensions;
using UnityEngine.XR.ARFoundation;

public class ARNavigationGuide : MonoBehaviour
{
    public AREarthManager EarthManager;
    public Transform NavigationArrow; // Your 3D Arrow prefab
    
    [Header("Current Target Node")]
    public double TargetLat = 22.292424; 
    public double TargetLon = 73.362735;
    
    void Update()
    {
        if (EarthManager.EarthTrackingState != UnityEngine.XR.ARSubsystems.TrackingState.Tracking) return;

        var pose = EarthManager.CameraGeospatialPose;

        // 1. Calculate the geographic bearing to the target node
        float targetBearing = GetBearing(pose.Latitude, pose.Longitude, TargetLat, TargetLon);

        // 2. Use EunRotation instead of the deprecated Heading [Fix for CS0618]
        // This converts the full 3D Earth-centered rotation to a 2D Y-axis compass heading
        float currentHeading = pose.EunRotation.eulerAngles.y;

        // 3. Calculate the relative rotation for the arrow
        float relativeRotation = targetBearing - currentHeading;

        // Apply rotation so the arrow points toward the next intersection
        NavigationArrow.localRotation = Quaternion.Euler(0, relativeRotation, 0);
    }

    private float GetBearing(double lat1, double lon1, double lat2, double lon2)
    {
        double dLon = (lon2 - lon1) * Mathf.Deg2Rad;
        double y = System.Math.Sin(dLon) * System.Math.Cos(lat2 * Mathf.Deg2Rad);
        double x = System.Math.Cos(lat1 * Mathf.Deg2Rad) * System.Math.Sin(lat2 * Mathf.Deg2Rad) -
                   System.Math.Sin(lat1 * Mathf.Deg2Rad) * System.Math.Cos(lat2 * Mathf.Deg2Rad) * System.Math.Cos(dLon);
        
        float bearing = Mathf.Atan2((float)y, (float)x) * Mathf.Rad2Deg;
        return (bearing + 360) % 360;
    }
}   
using UnityEngine;
using System.Collections.Generic;
using Google.XR.ARCoreExtensions;

public class CampusMapManager : MonoBehaviour
{
    public AREarthManager EarthManager;
    public List<CampusNode> AllCampusNodes; // Drag all your Node assets here
    public GeospatialNavigationController NavDriver; // Link to Script 3

    public void CalculateNewRoute(string destinationName)
{
    var pose = EarthManager.CameraGeospatialPose;
    CampusNode start = FindNearestNode(pose.Latitude, pose.Longitude);
    CampusNode end = AllCampusNodes.Find(n => n.nodeName == destinationName);

    if (start != null && end != null)
    {
        // Clear the old path and add your new start/end nodes to the controller's list
        NavDriver.Waypoints.Clear();
        
        NavDriver.Waypoints.Add(new GPSWaypoint { Label = start.nodeName, Latitude = start.latitude, Longitude = start.longitude });
        NavDriver.Waypoints.Add(new GPSWaypoint { Label = end.nodeName, Latitude = end.latitude, Longitude = end.longitude });

        // Call the correct function name found in GeospatialNavigationController
        NavDriver.StartNavigation(); 
    }
}

    private CampusNode FindNearestNode(double lat, double lon)
    {
        CampusNode nearest = null;
        double minDistance = double.MaxValue;

        foreach (var node in AllCampusNodes)
        {
            double dLat = node.latitude - lat;
            double dLon = node.longitude - lon;
            double distSq = dLat * dLat + dLon * dLon;

            if (distSq < minDistance)
            {
                minDistance = distSq;
                nearest = node;
            }
        }
        return nearest;
    }
}
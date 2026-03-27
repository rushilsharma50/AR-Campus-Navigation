using UnityEngine;
using System.Collections.Generic;
using Google.XR.ARCoreExtensions;

public class CampusMapManager : MonoBehaviour
{
    public AREarthManager EarthManager;
    public List<CampusNode> allCampusNodes; // Your full database
    private List<CampusNode> currentActivePath;

    public void CalculateNewRoute(string destinationName)
    {
        var pose = EarthManager.CameraGeospatialPose;
        CampusNode startNode = FindNearestNode(pose.Latitude, pose.Longitude);
        CampusNode endNode = allCampusNodes.Find(n => n.nodeName == destinationName);

        // Run Dijkstra/A* algorithm here
        currentActivePath = Pathfinding.GetPath(startNode, endNode);
    }

    void Update()
    {
        if (currentActivePath == null || currentActivePath.Count == 0) return;

        // Logic to point the arrow at currentActivePath[0]
        // If distance < 5m, currentActivePath.RemoveAt(0)
    }

    private CampusNode FindNearestNode(double lat, double lon)
    {
        CampusNode nearest = null;
        double minDistance = double.MaxValue;

        foreach (var node in allCampusNodes)
        {
            // Simple Pythagorean distance for nearby points
            double dLat = node.latitude - lat;
            double dLon = node.longitude - lon;
            double distanceSq = dLat * dLat + dLon * dLon;

            if (distanceSq < minDistance)
            {
                minDistance = distanceSq;
                nearest = node;
            }
        }

        return nearest;
    }
}
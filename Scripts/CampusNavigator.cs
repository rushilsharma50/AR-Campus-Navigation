using UnityEngine;
using System.Collections.Generic;

public class CampusNavigator : MonoBehaviour
{
    public List<Transform> CampusNodes; // All intersection waypoints
    public Transform CurrentTargetNode;
    public Transform NavigationArrow;

    public void SetDestination(string buildingName)
    {
        // 1. Find the destination node by name
        // 2. Run Dijkstra to get a List<Transform> of nodes to follow
        // 3. Set CurrentTargetNode to the first node in that path
    }

    void Update()
    {
        if (CurrentTargetNode == null) return;

        // Point the arrow toward the next intersection
        Vector3 direction = CurrentTargetNode.position - transform.position;
        NavigationArrow.rotation = Quaternion.LookRotation(direction);

        // Switch to the next node when close (e.g., within 5 meters)
        if (Vector3.Distance(transform.position, CurrentTargetNode.position) < 5f)
        {
            // Update CurrentTargetNode to the next one in the path list
        }
    }
}
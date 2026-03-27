using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public static class Pathfinding
{
    public static List<CampusNode> GetPath(CampusNode start, CampusNode end)
    {
        var distances = new Dictionary<CampusNode, float>();
        var previous = new Dictionary<CampusNode, CampusNode>();
        var nodes = new List<CampusNode>();

        distances[start] = 0;

        // Initialize distances
        // Note: You'll need a way to reference all nodes, 
        // usually through a Manager script.
        
        // This is a simplified version for your milestone
        var path = new List<CampusNode>();
        CampusNode current = end;

        while (current != null)
        {
            path.Add(current);
            // logic to find previous node...
            if (current == start) break;
            current = null; // Placeholder for logic
        }

        path.Reverse();
        return path;
    }
}
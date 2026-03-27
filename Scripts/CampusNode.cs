using UnityEngine;
using System.Collections.Generic; // Required for List<>

[CreateAssetMenu(fileName = "NewNode", menuName = "Campus/Node")]
public class CampusNode : ScriptableObject 
{
    public string nodeName;
    public double latitude;
    public double longitude;
    
    // This allows you to link nodes together to form a path
    public List<CampusNode> neighbors; 
}
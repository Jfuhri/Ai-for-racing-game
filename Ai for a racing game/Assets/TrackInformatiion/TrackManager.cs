using System.Collections.Generic;
using UnityEngine;

public class TrackManager : MonoBehaviour
{
    [Header("Track Settings")]
    public LayerMask trackLayer; // Layer mask for track
    public List<TrackSector> sectors = new List<TrackSector>();
    public Transform startLine;
    public Transform finishLine;

    [Header("Track Conditions")]
    public float gripLevel = 1f; // 0-1
    public float rubberLevel = 0.5f; // 0-1
    public bool isRaining = false;
    public float debrisLevel = 0f; // 0-1

    [Header("Boost Settings")]
    public float boostMultiplier = 1.2f; // how much boost increases speed

    // Cache boost zones
    private List<Collider> boostZones = new List<Collider>();

    private void Awake()
    {
        // Automatically find boost zones on track
        FindBoostZones();
    }

    private void FindBoostZones()
    {
        boostZones.Clear();
        GameObject[] allBoosts = GameObject.FindGameObjectsWithTag("Boost");
        foreach (GameObject boost in allBoosts)
        {
            if (((1 << boost.layer) & trackLayer) != 0)
            {
                Collider col = boost.GetComponent<Collider>();
                if (col != null)
                    boostZones.Add(col);
            }
        }
    }

    // Check if a position is inside a boost zone
    public bool IsInBoostZone(Vector3 position)
    {
        foreach (Collider col in boostZones)
        {
            if (col.bounds.Contains(position))
                return true;
        }
        return false;
    }

    // Example: Get sector a position belongs to
    public TrackSector GetSector(Vector3 position)
    {
        foreach (TrackSector sector in sectors)
        {
            if (sector.bounds.Contains(position))
                return sector;
        }
        return null;
    }
}

// Represents a sector of the track
[System.Serializable]
public class TrackSector
{
    public string sectorName;
    public Bounds bounds; // Could also be a collider
    public bool hasBoost = false;
}

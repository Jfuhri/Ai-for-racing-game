using System.Collections.Generic;
using UnityEngine;
using UnityEngine.ProBuilder;
using UnityEngine.ProBuilder.MeshOperations;

[ExecuteInEditMode]
public class FullProBuilderTrackGenerator : MonoBehaviour
{
    [Header("Track Parameters")]
    public int numSegments = 50;
    public int numStraights = 15;
    public int numFastCorners = 15;
    public int numMediumCorners = 15;
    public int numHairpins = 5;

    public float segmentLengthMin = 20f;
    public float segmentLengthMax = 60f;
    public float cornerAngleMin = 30f;
    public float cornerAngleMax = 120f;

    [Header("Elevation")]
    public float maxElevation = 10f;
    public float minElevation = -5f;

    [Header("Track Width")]
    public float trackWidth = 5f;

    [Header("Sectors")]
    public int sectorsPerSegment = 2;

    [Header("Boost Settings")]
    public GameObject boostPrefab;
    public float boostZoneLength = 10f;

    [Header("Start/Finish")]
    public Transform startLine;

    private Vector3 currentPosition;
    private Vector3 currentDirection = Vector3.forward;

    private List<Segment> trackSegments = new List<Segment>();

    private enum SegmentType { Straight, FastCorner, MediumCorner, Hairpin }

    private class Segment
    {
        public SegmentType type;
        public Vector3 startPosition;
        public Vector3 direction;
        public float length;
        public float turnAngle;
        public float elevation;
        public GameObject segmentGO;
        public Vector3 bezierControl; // for smooth curves
    }

#if UNITY_EDITOR
    [ContextMenu("Reset Track")]
    public void ResetTrack()
    {
        foreach (Transform child in transform)
            DestroyImmediate(child.gameObject);

        trackSegments.Clear();
        currentPosition = startLine != null ? startLine.position : Vector3.zero;
        currentDirection = startLine != null ? startLine.forward : Vector3.forward;
        Debug.Log("Track Reset Complete!");
    }

    [ContextMenu("Generate Full Track")]
    public void GenerateTrack()
    {
        if (startLine == null)
        {
            Debug.LogError("Start line not assigned!");
            return;
        }

        // Reset before generating
        ResetTrack();

        int straightsLeft = numStraights;
        int fastCornersLeft = numFastCorners;
        int mediumCornersLeft = numMediumCorners;
        int hairpinsLeft = numHairpins;

        for (int i = 0; i < numSegments; i++)
        {
            SegmentType type = PickSegmentType(straightsLeft, fastCornersLeft, mediumCornersLeft, hairpinsLeft);
            switch (type)
            {
                case SegmentType.Straight: straightsLeft--; break;
                case SegmentType.FastCorner: fastCornersLeft--; break;
                case SegmentType.MediumCorner: mediumCornersLeft--; break;
                case SegmentType.Hairpin: hairpinsLeft--; break;
            }

            float length = Random.Range(segmentLengthMin, segmentLengthMax);
            float elevation = Random.Range(minElevation, maxElevation);
            float angle = (type != SegmentType.Straight) ? Random.Range(cornerAngleMin, cornerAngleMax) : 0f;

            GameObject segmentGO = new GameObject("Segment_" + i + "_" + type);
            segmentGO.transform.parent = transform;

            Segment segment = new Segment
            {
                type = type,
                startPosition = currentPosition,
                direction = currentDirection,
                length = length,
                elevation = elevation,
                turnAngle = angle,
                segmentGO = segmentGO
            };

            if (type != SegmentType.Straight)
            {
                // Bezier control point for smooth curve
                Vector3 mid = currentPosition + currentDirection * (length / 2f);
                Vector3 rotatedDir = Quaternion.Euler(0, angle / 2f, 0) * currentDirection;
                segment.bezierControl = mid + rotatedDir * (length / 2f);
            }

            trackSegments.Add(segment);

            // Create mesh
            ProBuilderMesh pbMesh = CreateSegmentMesh(segment);
            pbMesh.transform.parent = segmentGO.transform;

            // Create sectors
            CreateSectors(segment);

            // Boost zones on corners leading into straights
            if (type != SegmentType.Straight && i < numSegments - 1)
            {
                SegmentType nextType = PickNextSegmentType(i, straightsLeft, fastCornersLeft, mediumCornersLeft, hairpinsLeft);
                if (nextType == SegmentType.Straight)
                    CreateBoostZone(segmentGO, segment);
            }

            UpdatePositionAndDirection(segment);
        }

        // Loop the last segment to start line
        LoopTrackToStart();

        Debug.Log("Full ProBuilder Track Generated!");
    }
#endif

    private SegmentType PickSegmentType(int straightsLeft, int fastCornersLeft, int mediumCornersLeft, int hairpinsLeft)
    {
        List<SegmentType> options = new List<SegmentType>();
        if (straightsLeft > 0) options.Add(SegmentType.Straight);
        if (fastCornersLeft > 0) options.Add(SegmentType.FastCorner);
        if (mediumCornersLeft > 0) options.Add(SegmentType.MediumCorner);
        if (hairpinsLeft > 0) options.Add(SegmentType.Hairpin);
        return options[Random.Range(0, options.Count)];
    }

    private SegmentType PickNextSegmentType(int currentIndex, int straightsLeft, int fastCornersLeft, int mediumCornersLeft, int hairpinsLeft)
    {
        return PickSegmentType(straightsLeft, fastCornersLeft, mediumCornersLeft, hairpinsLeft);
    }

    private void LoopTrackToStart()
    {
        if (trackSegments.Count < 1) return;

        Segment lastSegment = trackSegments[trackSegments.Count - 1];
        Vector3 delta = startLine.position - lastSegment.startPosition;

        lastSegment.direction = delta.normalized;
        lastSegment.length = delta.magnitude;
        lastSegment.elevation = startLine.position.y - lastSegment.startPosition.y;

        // Recreate mesh for last segment
        DestroyImmediate(lastSegment.segmentGO.GetComponentInChildren<ProBuilderMesh>().gameObject);
        ProBuilderMesh pbMesh = CreateSegmentMesh(lastSegment);
        pbMesh.transform.parent = lastSegment.segmentGO.transform;
    }

    private ProBuilderMesh CreateSegmentMesh(Segment segment)
    {
        int steps = 8; // Bezier steps
        List<Vertex> vertices = new List<Vertex>();
        List<Face> faces = new List<Face>();
        List<SharedVertex> sharedVertices = new List<SharedVertex>();

        for (int i = 0; i <= steps; i++)
        {
            float t = i / (float)steps;
            Vector3 centerPos = segment.type == SegmentType.Straight
                ? segment.startPosition + segment.direction * (segment.length * t)
                : GetBezierPoint(segment.startPosition, segment.bezierControl, segment.startPosition + segment.direction * segment.length, t);

            Vector3 right = Vector3.Cross(Vector3.up, segment.direction).normalized * (trackWidth / 2f);
            Vertex leftVert = new Vertex { position = centerPos - right };
            Vertex rightVert = new Vertex { position = centerPos + right };

            vertices.Add(leftVert);
            vertices.Add(rightVert);

            sharedVertices.Add(new SharedVertex(new int[] { vertices.Count - 2 }));
            sharedVertices.Add(new SharedVertex(new int[] { vertices.Count - 1 }));

            if (i > 0)
            {
                int idx = vertices.Count - 4;
                faces.Add(new Face(new int[] { idx, idx + 1, idx + 3 }));
                faces.Add(new Face(new int[] { idx, idx + 3, idx + 2 }));
            }
        }

        // Create the ProBuilder mesh
        ProBuilderMesh pbMesh = ProBuilderMesh.Create(vertices, faces, sharedVertices);
        pbMesh.Refresh();

        // Make it visible in the Scene by adding MeshRenderer and default material
        MeshRenderer mr = pbMesh.GetComponent<MeshRenderer>();
        if (mr == null) mr = pbMesh.gameObject.AddComponent<MeshRenderer>();

        // Assign a simple gray material if none exists
        if (mr.sharedMaterial == null)
        {
            Material mat = new Material(Shader.Find("Standard"));
            mat.color = Color.gray;
            mr.sharedMaterial = mat;
        }

        return pbMesh;
    }

    private Vector3 GetBezierPoint(Vector3 p0, Vector3 p1, Vector3 p2, float t)
    {
        return (1 - t) * (1 - t) * p0 + 2 * (1 - t) * t * p1 + t * t * p2;
    }

    private void CreateSectors(Segment segment)
    {
        float sectorLength = segment.length / sectorsPerSegment;
        for (int i = 0; i < sectorsPerSegment; i++)
        {
            GameObject sectorGO = new GameObject("Sector_" + i);
            sectorGO.transform.parent = segment.segmentGO.transform;

            Vector3 pos = segment.startPosition + segment.direction * sectorLength * i;
            pos.y += segment.elevation * ((float)i / sectorsPerSegment);
            sectorGO.transform.position = pos;

            BoxCollider collider = sectorGO.AddComponent<BoxCollider>();
            collider.size = new Vector3(trackWidth, 1f, sectorLength);
            collider.isTrigger = true;
        }
    }

    private void CreateBoostZone(GameObject parent, Segment segment)
    {
        if (boostPrefab == null) return;

        GameObject boost = Instantiate(boostPrefab, parent.transform);
        boost.name = "BoostZone";
        boost.transform.position = segment.startPosition + segment.direction * (segment.length / 2);
        boost.transform.rotation = Quaternion.LookRotation(segment.direction);
        boost.transform.localScale = new Vector3(trackWidth, 1f, boostZoneLength);
        boost.tag = "Boost";

        BoxCollider col = boost.GetComponent<BoxCollider>();
        if (col == null) col = boost.AddComponent<BoxCollider>();
        col.isTrigger = true;
    }

    private void UpdatePositionAndDirection(Segment segment)
    {
        if (segment.type == SegmentType.Straight)
            currentPosition += segment.direction * segment.length;
        else
            currentDirection = Quaternion.Euler(0, segment.turnAngle, 0) * currentDirection;

        currentPosition.y += segment.elevation;
    }
}

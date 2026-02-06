using UnityEngine;
using System.Collections.Generic;

#if UNITY_EDITOR
using UnityEditor;
#endif

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class F1TrackGenerator : MonoBehaviour
{
    public enum SegmentType
    {
        Straight,
        Sweep,
        Hairpin,
        Chicane,
        Esses,
        HeavyCorner
    }

    [System.Serializable]
    public struct TrackSegment
    {
        public SegmentType type;
        public float length;
        public float curveRadius;
        public bool curveLeft;
        public float intensity;
    }

    [Header("Balanced Procedural Settings")]
    public bool randomizeTrack = true;
    public int seed = 0;

    [Header("Lap Length Bounds (meters)")]
    public float minLapLength = 4000f;
    public float maxLapLength = 7000f;

    [Header("Sector Counts")]
    public int minSectors = 5;
    public int maxSectors = 7;

    [Header("Road Visuals")]
    public float roadWidth = 12f;
    public Material roadMaterial;

    private List<Transform> trackPoints = new List<Transform>();
    private GameObject trackRoot;
    private MeshFilter meshFilter;

    // Internal
    private float currentLapLength = 0f;
    private List<TrackSegment> segments = new List<TrackSegment>();
    private List<Vector2> planarPoints = new List<Vector2>();

    public void GenerateTrack()
    {
        ClearOldTrack();

        if (randomizeTrack)
        {
            seed = System.DateTime.Now.Millisecond;
            Random.InitState(seed);
        }
        else
        {
            Random.InitState(seed);
        }

        BuildLapSectors();
        BuildTrackPath();

        ConnectBackToStart();
        BuildRoadMesh();
        ApplyMaterial();

        Debug.Log("Generated F1?style track!");
    }

    public void ClearOldTrack()
    {
        if (trackRoot != null)
        {
#if UNITY_EDITOR
            Undo.DestroyObjectImmediate(trackRoot);
#else
            Destroy(trackRoot);
#endif
        }

        trackPoints.Clear();
        planarPoints.Clear();
        segments.Clear();

        var mf = GetComponent<MeshFilter>();
        if (mf != null) mf.sharedMesh = null;
    }

    private void BuildLapSectors()
    {
        segments.Clear();
        currentLapLength = 0f;

        int sectorCount = Random.Range(minSectors, maxSectors + 1);

        // Define candidate sector templates
        List<System.Action> templates = new List<System.Action>()
        {
            BuildHighSpeedSector,
            BuildBrakingSector,
            BuildTechnicalSector,
            BuildChicaneSector,
            BuildEssesSector
        };

        // Shuffle and pick mixed sector patterns
        for (int i = 0; i < sectorCount; i++)
        {
            int index = Random.Range(0, templates.Count);
            templates[index].Invoke();
        }

        // After preliminary assembly, ensure total length is within bounds
        float lapLen = 0f;
        foreach (var s in segments) lapLen += s.length;

        // If too short, add a final long straight or sweeper
        while (lapLen < minLapLength)
        {
            AddStraightSegment(Random.Range(500f, 1500f));
            lapLen += segments[segments.Count - 1].length;
        }

        // Prevent overly long lap
        if (lapLen > maxLapLength)
        {
            // We can trim last segments proportionally
            float over = lapLen - maxLapLength;
            TrackSegment last = segments[segments.Count - 1];
            last.length = Mathf.Max(100f, last.length - over);
            segments[segments.Count - 1] = last;
        }
    }

    private void AddStraightSegment(float len)
    {
        segments.Add(new TrackSegment
        {
            type = SegmentType.Straight,
            length = len,
            curveRadius = 0f
        });
    }

    private void BuildHighSpeedSector()
    {
        AddStraightSegment(Random.Range(600f, 1200f));
        segments.Add(new TrackSegment
        {
            type = SegmentType.Sweep,
            length = Random.Range(250f, 600f),
            curveRadius = Random.Range(100f, 200f),
            curveLeft = Random.value > 0.5f
        });
    }

    private void BuildBrakingSector()
    {
        AddStraightSegment(Random.Range(400f, 900f));
        segments.Add(new TrackSegment
        {
            type = SegmentType.HeavyCorner,
            length = Random.Range(60f, 120f),
            curveRadius = Random.Range(30f, 60f),
            curveLeft = Random.value > 0.5f
        });
    }

    private void BuildTechnicalSector()
    {
        segments.Add(new TrackSegment
        {
            type = SegmentType.Esses,
            length = Random.Range(200f, 400f),
            curveRadius = Random.Range(30f, 60f),
            curveLeft = Random.value > 0.5f
        });
        segments.Add(new TrackSegment
        {
            type = SegmentType.Hairpin,
            length = Random.Range(60f, 90f),
            curveRadius = Random.Range(20f, 40f),
            curveLeft = Random.value > 0.5f
        });
    }

    private void BuildChicaneSector()
    {
        segments.Add(new TrackSegment
        {
            type = SegmentType.Chicane,
            length = Random.Range(120f, 240f),
            intensity = Random.Range(30f, 60f),
            curveLeft = Random.value > 0.5f
        });
    }

    private void BuildEssesSector()
    {
        segments.Add(new TrackSegment
        {
            type = SegmentType.Esses,
            length = Random.Range(250f, 500f),
            curveRadius = Random.Range(40f, 80f),
            curveLeft = Random.value > 0.5f
        });
    }

    private void BuildTrackPath()
    {
        trackRoot = new GameObject("TrackRoot");
        trackRoot.transform.parent = transform;

        Vector3 pos = transform.position;
        float heading = 0f;
        float totalDist = 0f;

        AddPoint(pos);

        foreach (var seg in segments)
        {
            StepSegment(ref pos, ref heading, seg, ref totalDist);
        }
    }

    private void StepSegment(ref Vector3 pos, ref float heading, TrackSegment seg, ref float total)
    {
        float remain = seg.length;
        while (remain > 0)
        {
            float step = Mathf.Min(1f, remain);

            // Turn logic
            if (seg.type != SegmentType.Straight)
            {
                float radius = (seg.type == SegmentType.HeavyCorner ? seg.curveRadius / 2f : seg.curveRadius);
                if (radius > 0f)
                {
                    float ang = (step / radius) * Mathf.Rad2Deg;
                    heading += seg.curveLeft ? -ang : ang;
                }
            }

            Vector3 fwd = Quaternion.Euler(0, heading, 0) * Vector3.forward;
            Vector3 next = pos + fwd * step;

            pos = next;
            AddPoint(next);

            remain -= step;
            total += step;
        }
    }

    private void ConnectBackToStart()
    {
        if (trackPoints.Count < 2) return;

        Vector3 start = trackPoints[0].position;
        Vector3 end = trackPoints[trackPoints.Count - 1].position;

        if (Vector3.Distance(start, end) < 0.01f) return;

        Vector3 beforeLast = trackPoints[trackPoints.Count - 2].position;
        Vector3 dir = (end - beforeLast).normalized;
        Vector3 startDir = (trackPoints.Count > 2) ?
            (trackPoints[1].position - trackPoints[0].position).normalized :
            Vector3.forward;

        float cd = Vector3.Distance(start, end) * 0.5f;
        Vector3 p0 = end;
        Vector3 p1 = end + dir * cd;
        Vector3 p2 = start - startDir * cd;
        Vector3 p3 = start;

        int steps = Mathf.CeilToInt(Vector3.Distance(start, end));
        for (int i = 1; i <= steps; i++)
        {
            float t = i / (float)steps;
            Vector3 pt = BezierPoint(p0, p1, p2, p3, t);
            AddPoint(pt);
        }
    }

    private Vector3 BezierPoint(Vector3 p0, Vector3 p1, VectorVector p2, VectorVector p3, float t)
    {
        float u = 1 - t;
        return u * u * u * p0 + 3u * u * t * p1 + 3u * t * t * p2 + t * t * t * p3;
    }

    private void AddPoint(Vector3 p)
    {
        GameObject pt = new GameObject("Point_" + trackPoints.Count);
        pt.transform.position = p;
        pt.transform.parent = trackRoot.transform;
        trackPoints.Add(pt.transform);
    }

    private void BuildRoadMesh()
    {
        meshFilter = meshFilter ? meshFilter : GetComponent<MeshFilter>();
        Mesh mesh = new Mesh();
        int count = trackPoints.Count;
        if (count < 2) return;

        Vector3[] verts = new Vector3[count * 2];
        int[] tris = new int[(count - 1) * 6];

        for (int i = 0; i < count; i++)
        {
            Vector3 pos = trackPoints[i].position;
            Transform next = (i < count - 1) ? trackPoints[i + 1] : trackPoints[i];
            Vector3 forward = (next.position - pos).normalized;
            Vector3 side = Vector3.Cross(forward, Vector3.up);

            verts[i * 2] = pos + side * (roadWidth * 0.5f);
            verts[i * 2 + 1] = pos - side * (roadWidth * 0.5f);
        }

        int idx = 0;
        for (int i = 0; i < count - 1; i++)
        {
            int v = i * 2;
            tris[idx++] = v; tris[idx++] = v + 2; tris[idx++] = v + 1;
            tris[idx++] = v + 1; tris[idx++] = v + 2; tris[idx++] = v + 3;
        }

        mesh.vertices = verts;
        mesh.triangles = tris;
        mesh.RecalculateNormals();
        meshFilter.sharedMesh = mesh;
    }

    private void ApplyMaterial()
    {
        if (roadMaterial != null)
            GetComponent<MeshRenderer>().sharedMaterial = roadMaterial;
    }
}

#if UNITY_EDITOR
[CustomEditor(typeof(F1TrackGenerator))]
public class F1TrackGeneratorEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        F1TrackGenerator g = (F1TrackGenerator)target;

        GUILayout.Space(8);
        if (GUILayout.Button("Generate Track")) { Undo.RecordObject(g, "Generate Track"); g.GenerateTrack(); }
        if (GUILayout.Button("Clear Track")) { Undo.RecordObject(g, "Clear Track"); g.ClearOldTrack(); }
    }
}
#endif

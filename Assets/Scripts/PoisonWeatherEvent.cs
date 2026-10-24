using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public class PoisonWeatherEvent : WeatherEventBase
{
    [Header("Cloud")]
    [SerializeField, Min(0.5f)] private float CloudRadius = 5f;
    [SerializeField, Range(8, 32)] private int LongitudeSegments = 20;
    [SerializeField, Range(6, 24)] private int LatitudeSegments = 12;
    [SerializeField, Min(0f)] private float GroundOffset = 0.2f;
    [SerializeField] private Material PoisonMaterial;

    [Header("Fade In")]
    [SerializeField, Min(0f)] private float FadeInDuration = 2f;
    [SerializeField, Range(0f, 1f)] private float MaximumOpacity = 0.65f;

    [Header("Poison Damage")]
    [SerializeField, Min(0.1f)] private float DamageInterval = 1f;
    [SerializeField, Min(0f)] private float DamagePerTick = 5f;
    [SerializeField] private LayerMask DamageLayers = ~0;

    private GameObject CloudObject;
    private Mesh CloudMesh;
    private MeshRenderer CloudRenderer;
    private SphereCollider CloudCollider;
    private MaterialPropertyBlock PropertyBlock;

    private Coroutine DamageRoutine;
    private Coroutine FadeRoutine;
    private Vector3 CloudCenter;
    private bool EventActive;

    private readonly HashSet<Health> HitTargets = new HashSet<Health>();
    private readonly List<Health> TargetsToDamage = new List<Health>();

    private static readonly int OpacityId = Shader.PropertyToID("_Opacity");

    public override void BeginEvent(int WaveNumber)
    {
        EndEvent();

        ProceduralFloor Floor = ProceduralFloor.Instance;

        if (Floor == null) return;

        if (PoisonMaterial == null) return;

        EnsureCloudCreated();
        ChooseCloudPosition(Floor);
        CloudObject.transform.position = CloudCenter;
        CloudObject.transform.rotation = Quaternion.identity;
        CloudObject.transform.localScale = Vector3.one;

        CloudCollider.radius = CloudRadius;
        CloudCollider.center = Vector3.zero;
        CloudCollider.enabled = true;

        CloudRenderer.enabled = true;
        CloudObject.SetActive(true);
        EventActive = true;

        SetOpacity(0f);

        if (FadeInDuration > 0f)
            FadeRoutine = StartCoroutine(FadeIn());
        else
            SetOpacity(MaximumOpacity);

        DamageRoutine = StartCoroutine(DamageLoop());
    }

    public override void EndEvent()
    {
        EventActive = false;

        if (DamageRoutine != null)
        {
            StopCoroutine(DamageRoutine);
            DamageRoutine = null;
        }

        if (FadeRoutine != null)
        {
            StopCoroutine(FadeRoutine);
            FadeRoutine = null;
        }

        if (CloudObject != null)
            CloudObject.SetActive(false);

        HitTargets.Clear();
        TargetsToDamage.Clear();
    }

    private void EnsureCloudCreated()
    {
        if (CloudObject != null) return;

        CloudObject = new GameObject("ProceduralPoisonCloud");

        MeshFilter Filter = CloudObject.AddComponent<MeshFilter>();
        CloudRenderer = CloudObject.AddComponent<MeshRenderer>();
        CloudCollider = CloudObject.AddComponent<SphereCollider>();

        CloudCollider.isTrigger = true;
        CloudCollider.radius = CloudRadius;

        CloudRenderer.sharedMaterial = PoisonMaterial;
        CloudRenderer.shadowCastingMode = ShadowCastingMode.Off;
        CloudRenderer.receiveShadows = false;

        CloudMesh = GenerateDiscMesh();
        Filter.sharedMesh = CloudMesh;

        PropertyBlock = new MaterialPropertyBlock();
    }

    private void ChooseCloudPosition(ProceduralFloor Floor)
    {
        float Extent = Floor.MapExtent;
        float Margin = Mathf.Min(CloudRadius, Extent * 0.45f);

        float Min = Mathf.Min(Margin, Extent * 0.5f);
        float Max = Mathf.Max(Min, Extent - Margin);

        float x = Random.Range(Min, Max);
        float z = Random.Range(Min, Max);

        Vector3 World = Floor.transform.TransformPoint(new Vector3(x, 0f, z));
        float GroundHeight = Floor.SampleHeight(World);

        // Keep the sphere partly above ground, rather than placing its centre underground.
        CloudCenter = new Vector3(World.x, GroundHeight + GroundOffset, World.z);
    }

    private Mesh GenerateDiscMesh()       // we build a disk like a spider web so that we can keep it above the ground at each vertex, so no clipping occurs
    {
        ProceduralFloor Floor = ProceduralFloor.Instance;

        int Segments = Mathf.Max(12, LongitudeSegments);
        int Rings = Mathf.Max(2, LatitudeSegments);

        int VertexCount = 1 + Segments * Rings;
        Vector3[] Vertices = new Vector3[VertexCount];
        Vector3[] Normals = new Vector3[VertexCount];
        Vector2[] Uvs = new Vector2[VertexCount];

        int[] Triangles = new int[Segments * 3 + (Rings - 1) * Segments * 6];

        // Centre vertex in the cloud object's local space.
        Vertices[0] = Vector3.zero;
        Normals[0] = Vector3.up;
        Uvs[0] = new Vector2(0.5f, 0.5f);

        // Sample each Ring against the actual terrain.
        for (int Ring = 0; Ring < Rings; Ring++)
        {
            float Radius = CloudRadius * (Ring + 1) / Rings;

            for (int Segment = 0; Segment < Segments; Segment++)
            {
                float Angle = Segment * Mathf.PI * 2f / Segments;

                float localX = Mathf.Cos(Angle) * Radius;
                float localZ = Mathf.Sin(Angle) * Radius;

                int Index = 1 + Ring * Segments + Segment;

                Vector3 WorldPosition = CloudCenter + CloudObject.transform.right * localX + CloudObject.transform.forward * localZ;

                float TerrainHeight = Floor.SampleHeight(WorldPosition);

                // Raise the vertex above the sampled terrain.
                WorldPosition.y = TerrainHeight + GroundOffset;

                Vertices[Index] = CloudObject.transform.InverseTransformPoint(WorldPosition);

                Normals[Index] = Vector3.up;

                Uvs[Index] = new Vector2(localX / (CloudRadius * 2f) + 0.5f, localZ / (CloudRadius * 2f) + 0.5f);
            }
        }

        int t = 0;

        // Centre fan to the innermost Ring.
        for (int Segment = 0; Segment < Segments; Segment++)
        {
            int Current = 1 + Segment;
            int Next = 1 + (Segment + 1) % Segments;

            Triangles[t++] = 0;
            Triangles[t++] = Next;
            Triangles[t++] = Current;
        }

        // Connect each successive Ring.
        for (int Ring = 0; Ring < Rings - 1; Ring++)
        {
            int InnerStart = 1 + Ring * Segments;
            int OuterStart = InnerStart + Segments;

            for (int Segment = 0; Segment < Segments; Segment++)
            {
                int nextSegment = (Segment + 1) % Segments;

                int a = InnerStart + Segment;
                int b = InnerStart + nextSegment;
                int c = OuterStart + Segment;
                int d = OuterStart + nextSegment;

                Triangles[t++] = a;
                Triangles[t++] = b;
                Triangles[t++] = c;

                Triangles[t++] = b;
                Triangles[t++] = d;
                Triangles[t++] = c;
            }
        }

        Mesh mesh = new Mesh
        {
            name = "Terrain-Conforming Poison Fog Disc"
        };

        mesh.vertices = Vertices;
        mesh.normals = Normals;
        mesh.uv = Uvs;
        mesh.triangles = Triangles;
        mesh.RecalculateBounds();

        return mesh;
    }

    private IEnumerator FadeIn()
    {
        float Elapsed = 0f;

        while (Elapsed < FadeInDuration && EventActive)
        {
            Elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(Elapsed / FadeInDuration);
            SetOpacity(Mathf.Lerp(0f, MaximumOpacity, t));
            yield return null;
        }

        if (EventActive)
            SetOpacity(MaximumOpacity);

        FadeRoutine = null;
    }

    private void SetOpacity(float Opacity)
    {
        if (CloudRenderer == null) return;

        if (PropertyBlock == null)
            PropertyBlock = new MaterialPropertyBlock();

        CloudRenderer.GetPropertyBlock(PropertyBlock);
        PropertyBlock.SetFloat(OpacityId, Opacity);
        CloudRenderer.SetPropertyBlock(PropertyBlock);
    }

    private IEnumerator DamageLoop()
    {
        while (EventActive)
        {
            yield return new WaitForSeconds(DamageInterval);

            if (EventActive)
                DamageTargets();
        }

        DamageRoutine = null;
    }

    private void DamageTargets()
    {
        HitTargets.Clear();
        TargetsToDamage.Clear();

        // Use the collider's actual world-space centre and radius.
        Vector3 Center = CloudObject.transform.TransformPoint(CloudCollider.center);
        float Radius = CloudCollider.radius * CloudObject.transform.lossyScale.x;

        Collider[] hits = Physics.OverlapSphere(Center, Radius, DamageLayers, QueryTriggerInteraction.Ignore);

        foreach (Collider hit in hits)
        {
            if (hit == null) continue;

            Health health = hit.GetComponentInParent<Health>();

            if (health == null || !health.gameObject.activeInHierarchy || health.IsDead)
                continue;

            // Prevent multi-collider enemies from receiving duplicate damage per tick.
            if (HitTargets.Add(health))
                TargetsToDamage.Add(health);
        }

        foreach (Health health in TargetsToDamage)
        {
            if (health == null || !health.gameObject.activeInHierarchy || health.IsDead)
                continue;

            UpgradeableObject Upgradeable = health.GetComponentInParent<UpgradeableObject>();

            if (Upgradeable != null && Upgradeable.IsPoisonImmune) continue;

            health.TakeDamage(DamagePerTick);
        }
    }

    private void OnDestroy()
    {
        if (CloudMesh != null)
            Destroy(CloudMesh);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(CloudCenter, CloudRadius);
    }
}

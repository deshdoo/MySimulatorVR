using UnityEngine;

// «Обтягивает» пятно дефекта по поверхности трубы. Строит сетку-пластину и в Start
// (после того как спавнер поставил и развернул дефект нормалью наружу) сажает каждую
// вершину лучом на реальную поверхность под ней — пятно повторяет кривизну трубы,
// как декаль, но без Depth Texture / Decal-фич (которые на этом рендере не работают).
//
// Ставится на префаб дефекта вместе с MeshRenderer (материал с текстурой трещины/
// ржавчины, лучше Unlit + Render Face = Both) и PipelineDefect. Меш строится в Awake,
// поэтому в редакторе (без Play) пятно не видно — это норма.
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
[DisallowMultipleComponent]
public class SurfaceConformer : MonoBehaviour
{
    [Tooltip("Сторона пятна, м.")]
    public float Размер = 0.3f;
    [Tooltip("Плотность сетки NxN — больше = глаже обтягивает, но чуть дороже.")]
    [Range(1, 24)] public int Подразбиение = 8;
    [Tooltip("Насколько приподнять пятно над поверхностью, м (чтобы не мерцало).")]
    public float ВысотаНадПоверхностью = 0.006f;
    [Tooltip("Слой(и) трубы, на которую сажать пятно. Если ложится на грунт — ограничь слоем трубы.")]
    public LayerMask Поверхность = ~0;

    void Awake()
    {
        GetComponent<MeshFilter>().mesh = ПостроитьСетку();
    }

    void Start()
    {
        Обтянуть();
    }

    // Плоская сетка в локальной плоскости XY, нормаль по +Z (спавнер ставит +Z = наружу).
    Mesh ПостроитьСетку()
    {
        int n = Mathf.Max(1, Подразбиение);
        var vs = new Vector3[(n + 1) * (n + 1)];
        var uv = new Vector2[vs.Length];
        var tris = new int[n * n * 6];

        int vi = 0;
        for (int y = 0; y <= n; y++)
            for (int x = 0; x <= n; x++)
            {
                float fx = (float)x / n, fy = (float)y / n;
                vs[vi] = new Vector3((fx - 0.5f) * Размер, (fy - 0.5f) * Размер, 0f);
                uv[vi] = new Vector2(fx, fy);
                vi++;
            }

        int ti = 0;
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                int i = y * (n + 1) + x;
                tris[ti++] = i;         tris[ti++] = i + n + 1; tris[ti++] = i + 1;
                tris[ti++] = i + 1;     tris[ti++] = i + n + 1; tris[ti++] = i + n + 2;
            }

        var m = new Mesh { name = "DefectPatch" };
        m.vertices = vs; m.uv = uv; m.triangles = tris;
        m.RecalculateNormals(); m.RecalculateBounds();
        return m;
    }

    // Сажает каждую вершину лучом на поверхность (луч из точки над вершиной внутрь трубы).
    // Обтягиваем ТОЛЬКО тот коллайдер, на который сел центр пятна (труба), чтобы край,
    // свисающий мимо трубы, не тянуло на грунт.
    void Обтянуть()
    {
        var mf = GetComponent<MeshFilter>();
        var m = mf.mesh;
        var vs = m.vertices;
        Vector3 внутрь = -transform.forward;   // forward = наружу, значит внутрь трубы = -forward

        // Целевая поверхность — та, куда смотрит центр дефекта (сама труба).
        Collider цель = null;
        if (Physics.Raycast(transform.position + transform.forward * Размер, внутрь,
                out RaycastHit ц, Размер * 3f, Поверхность, QueryTriggerInteraction.Ignore))
            цель = ц.collider;

        for (int i = 0; i < vs.Length; i++)
        {
            Vector3 мир = transform.TransformPoint(vs[i]);
            Vector3 из = мир + transform.forward * Размер;   // приподняли над поверхностью
            if (Physics.Raycast(из, внутрь, out RaycastHit hit, Размер * 2f, Поверхность, QueryTriggerInteraction.Ignore)
                && (цель == null || hit.collider == цель))
            {
                Vector3 наПоверхности = hit.point + hit.normal * ВысотаНадПоверхностью;
                vs[i] = transform.InverseTransformPoint(наПоверхности);
            }
            // луч не попал в трубу (свисает мимо) — вершину не трогаем (остаётся у касательной, не уходит на грунт)
        }

        m.vertices = vs; m.RecalculateNormals(); m.RecalculateBounds();
        mf.mesh = m;
    }
}

using UnityEngine;

[ExecuteAlways]
[RequireComponent(typeof(Camera))]
public class UnderwaterGodRays : MonoBehaviour
{
    public Light sunLight;          // основной Directional Light
    public Material effectMaterial; // M_GodRays — тот же материал, что висит
                                     // на Full Screen Pass Renderer Feature

    [Range(0.1f, 3f)] public float exposure = 1.0f;
    [Range(0.5f, 1f)] public float decay = 0.95f;
    [Range(0.0f, 2f)] public float density = 0.8f;
    [Range(0.0f, 2f)] public float weight = 0.6f;
    [Range(16, 128)] public int samples = 64;
    public Color lightColor = new Color(0.5f, 0.9f, 1f, 1f); // подводный оттенок

    [Header("Прожектор НПА (необязательно)")]
    [Tooltip("Если назначен и включён — добавляет второй световой луч от настоящей позиции прожектора, " +
             "а не от условной точки 'далеко по направлению', как у солнца. Луч виден только пока light.enabled = true.")]
    public Light localLight;
    public Color localLightColor = new Color(1f, 0.95f, 0.85f, 1f);

    Camera cam;

    void Awake()
    {
        cam = GetComponent<Camera>();
    }

    // Раньше расчёт и сам блит делались здесь же через OnRenderImage —
    // но этот колбэк не вызывается в URP под Render Graph (Unity 6).
    // Теперь Update() только обновляет свойства материала, а реальный
    // полноэкранный проход выполняет Full Screen Pass Renderer Feature.
    void Update()
    {
        if (cam == null) cam = GetComponent<Camera>();
        if (cam == null || sunLight == null || effectMaterial == null) return;

        // Для directional light считаем "точку солнца" далеко по направлению света
        Vector3 sunDir = -sunLight.transform.forward;
        Vector3 sunWorldPos = cam.transform.position + sunDir * 1000f;
        Vector3 vp = cam.WorldToViewportPoint(sunWorldPos);

        effectMaterial.SetVector("_LightPos", new Vector2(vp.x, vp.y));
        effectMaterial.SetFloat("_Exposure", exposure);
        effectMaterial.SetFloat("_Decay", decay);
        effectMaterial.SetFloat("_Density", density);
        effectMaterial.SetFloat("_Weight", weight);
        effectMaterial.SetInt("_Samples", samples);
        effectMaterial.SetColor("_LightColor", lightColor);

        bool hasLocal = localLight != null && localLight.enabled && localLight.intensity > 0f;
        effectMaterial.SetFloat("_HasLight2", hasLocal ? 1f : 0f);
        if (hasLocal)
        {
            Vector3 vp2 = cam.WorldToViewportPoint(localLight.transform.position);
            effectMaterial.SetVector("_LightPos2", new Vector2(vp2.x, vp2.y));
            effectMaterial.SetColor("_LightColor2", localLightColor);
        }
    }
}

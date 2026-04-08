using UnityEngine;

[ExecuteInEditMode]
[RequireComponent(typeof(Camera))]
public class UnderwaterGodRays : MonoBehaviour
{
    public Light sunLight;          // твой Directional Light
    public Material effectMaterial; // M_GodRays

    [Range(0.1f, 3f)] public float exposure = 1.0f;
    [Range(0.5f, 1f)] public float decay = 0.95f;
    [Range(0.0f, 2f)] public float density = 0.8f;
    [Range(0.0f, 2f)] public float weight = 0.6f;
    [Range(16, 128)] public int samples = 64;
    public Color lightColor = new Color(0.5f, 0.9f, 1f, 1f); // голубоватые лучи

    Camera cam;

    void Awake()
    {
        cam = GetComponent<Camera>();
    }

    void OnRenderImage(RenderTexture src, RenderTexture dest)
    {
        if (sunLight == null || effectMaterial == null)
        {
            Graphics.Blit(src, dest);
            return;
        }

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

        Graphics.Blit(src, dest, effectMaterial);
    }
}
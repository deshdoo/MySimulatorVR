using UnityEngine;

[ExecuteAlways]
public class SpotlightFogClear : MonoBehaviour
{
    public Light spotlight;
    [Tooltip("Второй прожектор (например, парная фара) — если назначен, конус считается от обеих фар сразу.")]
    public Light spotlightB;

    [Range(0f, 1f)] public float clearStrength = 0.8f;
    [Tooltip("Мягкость края конуса (в единицах косинуса угла).")]
    [Range(0.01f, 0.6f)] public float coneSoftness = 0.35f;

    [Header("Рассеянное свечение в конусе")]
    public Color scatterColor = new Color(0.6f, 0.85f, 0.9f, 1f);
    [Tooltip("Яркость свечения, которое луч добавляет в воду по своему пути.")]
    [Range(0f, 3f)] public float scatterIntensity = 0.6f;

    static readonly int PosID = Shader.PropertyToID("_SpotlightPos");
    static readonly int DirID = Shader.PropertyToID("_SpotlightDir");
    static readonly int RangeID = Shader.PropertyToID("_SpotlightRange");
    static readonly int CosAngleID = Shader.PropertyToID("_SpotlightCosAngle");
    static readonly int StrengthID = Shader.PropertyToID("_SpotlightClearStrength");
    static readonly int SoftnessID = Shader.PropertyToID("_SpotlightSoftness");
    static readonly int ScatterColorID = Shader.PropertyToID("_SpotlightScatterColor");
    static readonly int ScatterIntensityID = Shader.PropertyToID("_SpotlightScatterIntensity");

    static readonly int PosBID = Shader.PropertyToID("_SpotlightPosB");
    static readonly int DirBID = Shader.PropertyToID("_SpotlightDirB");
    static readonly int RangeBID = Shader.PropertyToID("_SpotlightRangeB");
    static readonly int CosAngleBID = Shader.PropertyToID("_SpotlightCosAngleB");
    static readonly int HasBID = Shader.PropertyToID("_SpotlightHasB");

    void Update()
    {
        if (spotlight == null) return;

        // Эффект должен полностью пропадать, когда фонарь выключен —
        // не просто гаснуть по яркости, а реально отключаться.
        bool isOn = spotlight.enabled && spotlight.intensity > 0f;
        float effectiveStrength = isOn ? clearStrength : 0f;
        float effectiveScatter = isOn ? scatterIntensity : 0f;

        Shader.SetGlobalVector(PosID, spotlight.transform.position);
        Shader.SetGlobalVector(DirID, spotlight.transform.forward);
        Shader.SetGlobalFloat(RangeID, spotlight.range);
        Shader.SetGlobalFloat(CosAngleID, Mathf.Cos(spotlight.spotAngle * 0.5f * Mathf.Deg2Rad));
        Shader.SetGlobalFloat(StrengthID, effectiveStrength);
        Shader.SetGlobalFloat(SoftnessID, coneSoftness);
        Shader.SetGlobalColor(ScatterColorID, scatterColor);
        Shader.SetGlobalFloat(ScatterIntensityID, effectiveScatter);

        bool hasB = spotlightB != null && spotlightB.enabled && spotlightB.intensity > 0f;
        Shader.SetGlobalFloat(HasBID, hasB ? 1f : 0f);
        if (hasB)
        {
            Shader.SetGlobalVector(PosBID, spotlightB.transform.position);
            Shader.SetGlobalVector(DirBID, spotlightB.transform.forward);
            Shader.SetGlobalFloat(RangeBID, spotlightB.range);
            Shader.SetGlobalFloat(CosAngleBID, Mathf.Cos(spotlightB.spotAngle * 0.5f * Mathf.Deg2Rad));
        }
    }
}

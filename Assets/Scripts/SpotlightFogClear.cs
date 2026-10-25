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

    [Header("Две фары — один луч")]
    [Tooltip("Считать пару фар одним общим лучом из середины между ними: иначе в открытой воде " +
             "светились два отдельных молочных круга рядом.")]
    public bool одинЛуч = true;
    [Tooltip("Множитель расчистки мути в луче: в луче видно лучше и дальше (итог не больше 1).")]
    public float усилениеРасчистки = 1.25f;
    [Tooltip("Множитель молочного свечения в луче: меньше — в пятне света видно дно, а не белую пелену.")]
    public float доляСвечения = 0.7f;
    [Tooltip("Насколько дальше дальности фары луч расчищает муть (свет гаснет плавно, а не обрывается на Range).")]
    public float дальностьВМути = 1.4f;

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
        float effectiveStrength = isOn ? Mathf.Clamp01(clearStrength * усилениеРасчистки) : 0f;
        float effectiveScatter = isOn ? scatterIntensity * доляСвечения : 0f;
        bool hasB = spotlightB != null && spotlightB.enabled && spotlightB.intensity > 0f;

        // Пара фар → один луч: из середины между фарами, по средней оси, угол — чтобы
        // накрыть оба конуса. В мути от сведённых фар и правда видно одно пятно.
        Vector3 pos = spotlight.transform.position, dir = spotlight.transform.forward;
        float угол = spotlight.spotAngle, дальность = spotlight.range;
        if (hasB && одинЛуч)
        {
            pos = (pos + spotlightB.transform.position) * 0.5f;
            dir = (dir + spotlightB.transform.forward).normalized;
            угол = Mathf.Max(угол, spotlightB.spotAngle) + Vector3.Angle(spotlight.transform.forward, spotlightB.transform.forward);
            дальность = Mathf.Max(дальность, spotlightB.range);
        }
        дальность *= дальностьВМути;

        Shader.SetGlobalVector(PosID, pos);
        Shader.SetGlobalVector(DirID, dir);
        Shader.SetGlobalFloat(RangeID, дальность);
        Shader.SetGlobalFloat(CosAngleID, Mathf.Cos(Mathf.Min(угол, 170f) * 0.5f * Mathf.Deg2Rad));
        Shader.SetGlobalFloat(StrengthID, effectiveStrength);
        Shader.SetGlobalFloat(SoftnessID, coneSoftness);
        Shader.SetGlobalColor(ScatterColorID, scatterColor);
        Shader.SetGlobalFloat(ScatterIntensityID, effectiveScatter);

        bool второйКонус = hasB && !одинЛуч;
        Shader.SetGlobalFloat(HasBID, второйКонус ? 1f : 0f);
        if (второйКонус)
        {
            Shader.SetGlobalVector(PosBID, spotlightB.transform.position);
            Shader.SetGlobalVector(DirBID, spotlightB.transform.forward);
            Shader.SetGlobalFloat(RangeBID, spotlightB.range * дальностьВМути);
            Shader.SetGlobalFloat(CosAngleBID, Mathf.Cos(spotlightB.spotAngle * 0.5f * Mathf.Deg2Rad));
        }
    }
}

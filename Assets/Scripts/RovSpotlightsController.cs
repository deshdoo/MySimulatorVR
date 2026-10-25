using UnityEngine;

// Включает/выключает реальные прожекторы НПА (Light-компоненты) синхронно
// с RovSystems.lightsOn. Раньше этот флаг влиял только на иконку на панели
// "Прожекторы" и на расход тока (RovSystemsSimulator), но в самой сцене
// никакого реального источника света не было.
//
// Переключение света теперь делает физическая кнопка RovPokeButton
// (Action = ToggleLights, клавиша-тест L). Этот скрипт лишь применяет
// итоговый флаг к реальным Light-компонентам каждый кадр — собственной
// клавиши у него больше нет, чтобы не было двойного переключения по L.
//
// При старте лучи двух фар сводятся в одну точку впереди и чуть расширяются: в сцене
// фары стояли узкими (30°) и разведёнными в стороны, и на дне и в мути светились
// два отдельных пятна с тёмной полосой посередине. Так ставят свет и на настоящих
// аппаратах — пятна перекрываются там, куда смотрит камера.
public class RovSpotlightsController : MonoBehaviour
{
    [Tooltip("Прожекторы, которые включаются/выключаются вместе")]
    public Light[] Прожекторы;

    [Header("Сведение лучей (при старте)")]
    [Tooltip("На каком расстоянии впереди сходятся оси двух фар, м. 0 — не трогать направление.")]
    public float СведениеНа_м = 4f;
    [Tooltip("Угол луча не меньше этого, град.")]
    public float УголЛуча_град = 50f;
    [Tooltip("Дальность света не меньше этой, м: в густой мути 6 м было мало.")]
    public float ДальностьСвета_м = 9f;
    [Tooltip("Множитель яркости фар от заданной в сцене: на светлом песке 18 слепило и давало жёлтое пятно.")]
    public float МножительЯркости = 0.65f;
    [Tooltip("Оттенок света: чуть холоднее белого, как у светодиодных фар ~6000 К, — песок не желтит.")]
    public Color Оттенок = new Color(0.9f, 0.96f, 1f);

    void Start() => Свести();

    void Свести()
    {
        int n = 0;
        Vector3 центр = Vector3.zero, вперёд = Vector3.zero;
        foreach (var l in Прожекторы)
            if (l != null && l.type == LightType.Spot) { центр += l.transform.position; вперёд += l.transform.forward; n++; }
        if (n == 0) return;
        центр /= n;
        вперёд.Normalize();   // средняя ось фар ≈ вперёд аппарата
        Vector3 точка = центр + вперёд * СведениеНа_м;
        foreach (var l in Прожекторы)
        {
            if (l == null || l.type != LightType.Spot) continue;
            if (n > 1 && СведениеНа_м > 0f)
                l.transform.rotation = Quaternion.LookRotation(точка - l.transform.position, l.transform.up);
            l.spotAngle = Mathf.Max(l.spotAngle, УголЛуча_град);
            l.innerSpotAngle = l.spotAngle * 0.6f;
            l.range = Mathf.Max(l.range, ДальностьСвета_м);
            l.intensity *= МножительЯркости;
            l.color = Оттенок;
        }
    }

    void Update()
    {
        for (int i = 0; i < Прожекторы.Length; i++)
            if (Прожекторы[i] != null)
                Прожекторы[i].enabled = RovSystems.lightsOn;
    }
}

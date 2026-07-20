using UnityEngine;

// Учёт повреждений НПА при столкновениях.
// Критерий — полная кинетическая энергия удара, как в реальном ударном
// взаимодействии (зависит от массы аппарата, а не только от скорости):
//   E = ½ * m * v_rel²   [Дж]
// Масса берётся из Rigidbody (см. m_Mass в ROV.prefab, сейчас условно 10 кг,
// в дальнейшем можно уточнить под реальную массу конкретного аппарата —
// пороги ниже подобраны так, чтобы при массе 10 кг соответствовать
// конкретным относительным скоростям удара).
//
// Реальные ROV рассчитаны на штатный контакт с грунтом (посадка на дно —
// обычная рабочая операция, а не авария), поэтому пороги подняты так, чтобы
// обычное касание дна при манёврах не засчитывалось как повреждение —
// урон должен идти только от настоящего жёсткого столкновения (стена, скала,
// удар на скорости), а не от любой просадки на пару метров.
// Пороги урона (Дж, при массе 10 кг ~ эквивалент относительной скорости v_rel):
//   E < 50    (v < 3.2 м/с)   — нет урона (обычная посадка/касание дна)
//   50 ≤ E < 200  (3.2-6.3 м/с) — лёгкий (с вероятностью)
//   200 ≤ E < 600 (6.3-11 м/с)  — средний (гарантированный сбой одной системы)
//   E ≥ 600       (>11 м/с)    — тяжёлый (две системы)
public class RovHealthMonitor : MonoBehaviour
{
    [Header("Пороги энергии удара (Дж), зависят от массы Rigidbody")]
    public float ПорогЛёгкогоУрона = 50f;
    public float ПорогСреднегоУрона = 200f;
    public float ПорогТяжёлогоУрона = 600f;

    [Header("Вероятность лёгкого урона")]
    [Range(0f, 1f)] public float ВероятностьЛёгкого = 0.2f;

    [Header("Логирование")]
    public bool ЛогироватьУдары = true;

    private Rigidbody _rb;

    void Awake() { _rb = GetComponent<Rigidbody>(); }

    void OnCollisionEnter(Collision collision)
    {
        if (_rb == null) return;

        Vector3 v_rel = collision.relativeVelocity;
        float E = 0.5f * _rb.mass * v_rel.sqrMagnitude; // Дж, зависит от массы аппарата

        if (ЛогироватьУдары)
            Debug.Log($"[Удар] v={v_rel.magnitude:F2} м/с, m={_rb.mass:F1} кг, E={E:F2} Дж, объект: {collision.gameObject.name}");

        if (E < ПорогЛёгкогоУрона) return;

        if (E < ПорогСреднегоУрона)
        {
            if (Random.value < ВероятностьЛёгкого) ПовредитьСлучайнуюСистему();
        }
        else if (E < ПорогТяжёлогоУрона)
        {
            ПовредитьСлучайнуюСистему();
        }
        else
        {
            ПовредитьСлучайнуюСистему();
            ПовредитьСлучайнуюСистему();
        }
    }

    void ПовредитьСлучайнуюСистему()
    {
        int n = Random.Range(0, 5);
        switch (n)
        {
            case 0:
                RovSystems.thrusterDamage = Mathf.Min(2, RovSystems.thrusterDamage + 1);
                Debug.Log($"[Урон] ДВИЖИТЕЛИ повреждены (уровень {RovSystems.thrusterDamage})");
                break;
            case 1:
                RovSystems.cameraDamage = Mathf.Min(2, RovSystems.cameraDamage + 1);
                Debug.Log($"[Урон] КАМЕРА повреждена (уровень {RovSystems.cameraDamage})");
                break;
            case 2:
                RovSystems.manipulatorDamage = Mathf.Min(2, RovSystems.manipulatorDamage + 1);
                Debug.Log($"[Урон] МАНИПУЛЯТОР повреждён (уровень {RovSystems.manipulatorDamage})");
                break;
            case 3:
                RovSystems.communicationDamage = Mathf.Min(2, RovSystems.communicationDamage + 1);
                Debug.Log($"[Урон] СВЯЗЬ повреждена (уровень {RovSystems.communicationDamage})");
                break;
            case 4:
                RovSystems.lightsDamage = Mathf.Min(2, RovSystems.lightsDamage + 1);
                Debug.Log($"[Урон] ПРОЖЕКТОРЫ повреждены (уровень {RovSystems.lightsDamage})");
                break;
        }
    }
}

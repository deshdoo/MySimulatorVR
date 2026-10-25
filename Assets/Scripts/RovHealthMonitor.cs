using System.Collections.Generic;
using UnityEngine;

// Учёт повреждений НПА при столкновениях.
// Критерий — полная кинетическая энергия удара, как в реальном ударном
// взаимодействии (зависит от массы аппарата, а не только от скорости):
//   E = ½ * m * v_rel²   [Дж]
// Масса берётся из Rigidbody — её задаёт MarlinSpec (60 кг, с лагом 63 кг).
//
// Реальные ROV рассчитаны на штатный контакт с грунтом (посадка на дно —
// обычная рабочая операция, а не авария), поэтому обычное касание дна при
// манёврах не засчитывается как повреждение — урон идёт только от удара на ходу.
// Пороги под «Марлин» 60 кг (паспорт: 1 м/с вперёд, 0,5 м/с по вертикали):
//   E < 10 Дж      (v < 0,58 м/с)      — нет урона: посадка на дно даже полным ходом вниз
//                                         (0,5 м/с → 7,5 Дж), касание трубы на подходе;
//   10 ≤ E < 25    (0,58–0,91 м/с)     — лёгкий (с вероятностью);
//   25 ≤ E < 45    (0,91–1,22 м/с)     — средний: полный ход в трубу (1 м/с → 30 Дж);
//   E ≥ 45         (> 1,22 м/с)        — тяжёлый: полный ход по течению.
// Раньше пороги были 50/200/600 Дж под условный аппарат 10 кг (3,2/6,3/11 м/с) — у
// «Марлина» с его 1 м/с урон был недостижим.
//
// Что ломается — по МЕСТУ удара: ближайший к точке контакта узел аппарата (движители
// по углам рамы, купола камер, светильники, схват, скоба кабеля — через неё идёт
// связь). Удар рамой вдали от узлов (дальше РадиусУзла_м) на лёгком уровне ничего не
// ломает, на среднем и тяжёлом достаётся ближайшему узлу. Тяжёлый ломает два
// ближайших разных узла. Узлов в модели нет — как раньше, случайная система.
public class RovHealthMonitor : MonoBehaviour
{
    [Header("Пороги энергии удара (Дж), зависят от массы Rigidbody")]
    public float ПорогЛёгкогоУрона = 10f;
    public float ПорогСреднегоУрона = 25f;
    public float ПорогТяжёлогоУрона = 45f;

    [Header("Вероятность лёгкого урона")]
    [Range(0f, 1f)] public float ВероятностьЛёгкого = 0.2f;

    [Header("Место удара")]
    [Tooltip("Узел дальше этого от точки контакта считается не задетым (удар пришёлся в раму), м.")]
    public float РадиусУзла_м = 0.25f;

    [Header("Логирование")]
    public bool ЛогироватьУдары = true;

    private Rigidbody _rb;

    enum Система { Движители, Камера, Манипулятор, Связь, Прожекторы }
    static readonly string[] Названия = { "ДВИЖИТЕЛИ повреждены", "КАМЕРА повреждена", "МАНИПУЛЯТОР повреждён", "СВЯЗЬ повреждена", "ПРОЖЕКТОРЫ повреждены" };
    struct Узел { public Transform t; public Система с; }
    readonly List<Узел> _узлы = new List<Узел>();
    bool _узлыНайдены;

    // Каждый удар выше ПорогЛёгкогоУрона: энергия, Дж, и с чем столкнулись.
    // Слушает MissionStats — для разбора миссии (число и сила ударов).
    public static event System.Action<float, string> ПриУдаре;

    void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        // Пороги — из кода, поверх сцены: там остались 50/200/600 Дж от аппарата 10 кг.
        ПорогЛёгкогоУрона = 10f; ПорогСреднегоУрона = 25f; ПорогТяжёлогоУрона = 45f;
    }

    // Узлы ищутся при первом ударе: модель «Марлина» и светильники к Awake могут быть
    // ещё не собраны.
    void НайтиУзлы()
    {
        _узлыНайдены = true;
        foreach (var t in GetComponentsInChildren<Transform>(true))
        {
            string n = t.name;
            if (n.EndsWith("Винт")) _узлы.Add(new Узел { t = t, с = Система.Движители });
            else if (n.StartsWith("Купол")) _узлы.Add(new Узел { t = t, с = Система.Камера });
            else if (n == "Узел_схват" || n == "Узел_захват") _узлы.Add(new Узел { t = t, с = Система.Манипулятор });
            else if (n == "Скоба") _узлы.Add(new Узел { t = t, с = Система.Связь });
        }
        foreach (var l in GetComponentsInChildren<Light>(true))
            if (l.type == LightType.Spot) _узлы.Add(new Узел { t = l.transform, с = Система.Прожекторы });
    }

    void OnCollisionEnter(Collision collision)
    {
        if (_rb == null) return;

        Vector3 v_rel = collision.relativeVelocity;
        float E = 0.5f * _rb.mass * v_rel.sqrMagnitude; // Дж, зависит от массы аппарата

        if (ЛогироватьУдары)
            Debug.Log($"[Удар] v={v_rel.magnitude:F2} м/с, m={_rb.mass:F1} кг, E={E:F2} Дж, объект: {collision.gameObject.name}");

        if (E < ПорогЛёгкогоУрона) return;
        ПриУдаре?.Invoke(E, collision.gameObject.name);

        if (!_узлыНайдены) НайтиУзлы();
        Vector3 точка = Vector3.zero;
        int n = collision.contactCount;
        for (int i = 0; i < n; i++) точка += collision.GetContact(i).point;
        точка = n > 0 ? точка / n : transform.position;

        if (E < ПорогСреднегоУрона)
        {
            if (Random.value < ВероятностьЛёгкого) Повредить(точка, 1, true);
        }
        else if (E < ПорогТяжёлогоУрона)
        {
            Повредить(точка, 1, false);
        }
        else
        {
            Повредить(точка, 2, false);
        }
    }

    // Повредить сколько разных систем — ближайших к точке удара. толькоРядом: если
    // ближайший узел дальше РадиусУзла_м, удар пришёлся в раму и ничего не ломает.
    void Повредить(Vector3 точка, int сколько, bool толькоРядом)
    {
        if (_узлы.Count == 0)
        {
            for (int i = 0; i < сколько; i++) ПовредитьСистему((Система)Random.Range(0, 5), "");
            return;
        }
        _узлы.RemoveAll(у => у.t == null);
        var порядок = new List<Узел>(_узлы);
        порядок.Sort((a, b) => (a.t.position - точка).sqrMagnitude.CompareTo((b.t.position - точка).sqrMagnitude));
        if (порядок.Count == 0) return;
        if (толькоРядом && Vector3.Distance(порядок[0].t.position, точка) > РадиусУзла_м)
        {
            Debug.Log("[Урон] удар пришёлся в раму, узлы целы");
            return;
        }
        var задеты = new List<Система>();
        foreach (var у in порядок)
        {
            if (задеты.Contains(у.с)) continue;
            задеты.Add(у.с);
            ПовредитьСистему(у.с, $" (удар у «{у.t.name}»)");
            if (задеты.Count >= сколько) break;
        }
    }

    void ПовредитьСистему(Система с, string где)
    {
        int уровень = 0;
        switch (с)
        {
            case Система.Движители:   уровень = RovSystems.thrusterDamage      = Mathf.Min(2, RovSystems.thrusterDamage + 1); break;
            case Система.Камера:      уровень = RovSystems.cameraDamage        = Mathf.Min(2, RovSystems.cameraDamage + 1); break;
            case Система.Манипулятор: уровень = RovSystems.manipulatorDamage   = Mathf.Min(2, RovSystems.manipulatorDamage + 1); break;
            case Система.Связь:       уровень = RovSystems.communicationDamage = Mathf.Min(2, RovSystems.communicationDamage + 1); break;
            case Система.Прожекторы:  уровень = RovSystems.lightsDamage        = Mathf.Min(2, RovSystems.lightsDamage + 1); break;
        }
        Debug.Log($"[Урон] {Названия[(int)с]} (уровень {уровень}){где}");
    }
}

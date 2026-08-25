using System.Collections.Generic;
using UnityEngine;

// Спавнит дефекты трубопровода в случайных точках НА ПОВЕРХНОСТИ трубы при старте —
// для динамичности: каждый заезд аномалии в новых местах, разных типов, в разном числе.
//
// Основной режим — по КОЛЛАЙДЕРАМ трубы: дефект сажается лучом на реальный
// коллайдер (луч сквозь случайную точку габаритов → первая поверхность трубы).
// Это надёжно ловит поверхность даже у трубы из многих сегментов, с вентилем и
// болтами — в отличие от прикидки по габаритам. Нужны коллайдеры на трубе.
//
// Запасной режим — по линии (габариты мешей или заданные ТочкиТрассы), если
// коллайдеров нет.
//
// Работает в Awake СПЕЦИАЛЬНО: RovScanner ищет дефекты в Start (FindObjectsByType),
// а Unity гарантированно выполняет все Awake раньше любого Start — сканер увидит
// уже созданные дефекты и сам подстроит счётчик (ТребуетсяОбнаружить = 0 = «все»).
//
// Настройка: пустой объект + этот компонент; в «Труба» перетащи объект трубы
// (напр. pipel1ne), в «ПрефабыДефектов» — префаб(ы) с компонентом PipelineDefect.
[DisallowMultipleComponent]
public class PipelineDefectSpawner : MonoBehaviour
{
    [Header("ПРОСТОЙ режим — просто перетащи трубу")]
    [Tooltip("Перетащи сюда объект трубы (напр. pipel1ne целиком). Дефекты сядут лучом " +
             "на её коллайдеры. Точки трассы ниже НЕ нужны.")]
    public Transform Труба;

    [Header("Где на трубе спавнить")]
    [Tooltip("Спавнить только на сегментах, в имени которых есть эта подстрока (напр. «Труба»). " +
             "Так дефекты не сядут на фланцы/болты. Пусто = на любых частях.")]
    public string ФильтрИмениСегмента = "Труба";
    [Tooltip("Спавнить ТОЛЬКО на верхней части трубы (чтобы дефект и пузыри были видны сверху, а не из-под трубы).")]
    public bool ТолькоВерхняяЧасть = true;
    [Range(0f, 1f)]
    [Tooltip("Насколько «вверх» должна смотреть поверхность: 0 = вся верхняя половина, 0.6 ≈ верхняя треть, 1 = только макушка. Выше = строже к макушке.")]
    public float ПорогВерха = 0.6f;

    [Header("Точный режим (для изгибов) — необязательно")]
    [Tooltip("Точки вдоль оси трубы по порядку (≥2). Заполняй ТОЛЬКО если у трубы нет " +
             "коллайдеров. Если заданы — используются вместо коллайдеров.")]
    public Transform[] ТочкиТрассы;
    [Tooltip("Радиус трубы, м (только для режима по точкам/габаритам). 0 = авто.")]
    public float РадиусТрубы = 0f;

    [Header("Что спавнить")]
    [Tooltip("Префабы дефектов (с компонентом PipelineDefect). Для каждого берётся случайный из списка.")]
    public GameObject[] ПрефабыДефектов;
    [Tooltip("Минимальное число дефектов за заезд.")]
    public int Минимум = 3;
    [Tooltip("Максимальное число дефектов за заезд (= Минимум, чтобы фиксировать число).")]
    public int Максимум = 5;
    [Tooltip("Минимальное расстояние между дефектами, м (чтобы не кучковались).")]
    public float МинРасстояние = 2f;
    [Tooltip("Приподнять дефект над поверхностью, м — чтобы плоский меш (квад) не мерцал. ~0.01.")]
    public float ПоднятьНадПоверхностью = 0.01f;

    [Header("Повторяемость")]
    [Tooltip("Зерно ГСЧ. 0 = каждый запуск разный; фикс. значение — воспроизводимая раскладка.")]
    public int Seed = 0;
    [Tooltip("Ориентировать дефект нормалью наружу от трубы.")]
    public bool ОриентироватьНаружу = true;

    [Header("Пузыри утечки")]
    [Tooltip("Автоматически вешать эмиттер пузырей на созданные дефекты Утечка/Трещина " +
             "(если его ещё нет на префабе). Пузыри всплывают над сколом и служат подсказкой, где течь.")]
    public bool ПузыриУтечки = true;

    // Режим по коллайдерам (основной)
    private Collider[] _коллайдеры;
    private Bounds _габариты;
    // Режим по линии (запасной)
    private Vector3[] _путь;
    private float _радиус;
    // Слой рендерера трубы — на него сажаем дефекты, чтобы их видела камера вида НПА
    // (монитор через RenderTexture рендерит обычно только рабочий слой, напр. Underwater).
    private int _слойТрубы = -1;

    void Awake()
    {
        if (!Подготовить())
        {
            Debug.LogWarning("[PipelineDefectSpawner] Не задана труба (с коллайдерами) и нет ≥2 точек трассы — спавн пропущен.");
            return;
        }
        if (!ЕстьПрефаб())
        {
            Debug.LogWarning("[PipelineDefectSpawner] Не заданы префабы дефектов (список пуст или только None) — спавн пропущен.");
            return;
        }

        var rng = Seed != 0 ? new System.Random(Seed) : new System.Random();
        int lo = Mathf.Max(0, Mathf.Min(Минимум, Максимум));
        int hi = Mathf.Max(Минимум, Максимум);
        int количество = rng.Next(lo, hi + 1);

        var занято = new List<Vector3>();
        int страховка = количество * 60;   // рейкасты иногда мажут мимо — даём запас попыток

        while (занято.Count < количество && страховка-- > 0)
        {
            if (!СлучайнаяТочка(rng, out Vector3 поз, out Vector3 наружу, out int слой)) continue;

            bool близко = false;
            foreach (var p in занято)
                if (Vector3.Distance(p, поз) < МинРасстояние) { близко = true; break; }
            if (близко) continue;

            занято.Add(поз);
            GameObject префаб = ВыбратьПрефаб(rng);
            if (префаб == null) continue;

            Quaternion пов = (ОриентироватьНаружу && наружу.sqrMagnitude > 1e-4f)
                ? Quaternion.LookRotation(наружу) : Quaternion.identity;
            Vector3 место = поз + наружу * ПоднятьНадПоверхностью;
            GameObject экземпляр = Instantiate(префаб, место, пов);

            // ВАЖНО: сажаем дефект на ТОТ ЖЕ слой, что и труба, куда он сел. Камера
            // вида НПА (что рендерит монитор через RenderTexture) обычно показывает
            // только рабочий слой (напр. Underwater), а префаб дефекта лежит на
            // Default — иначе ни трещина, ни пузыри на мониторе не видны.
            if (слой >= 0) УстановитьСлойРекурсивно(экземпляр, слой);

            // Пузыри утечки: эмиттер сам решит по типу дефекта (Утечка/Трещина —
            // пузырит, Коррозия/Провис — выключается) и построит ParticleSystem в Start.
            // Слой дефекта уже выставлен выше — эмиттер наследует его для пузырей.
            if (ПузыриУтечки && экземпляр.GetComponentInChildren<PipelineLeakEmitter>() == null)
                экземпляр.AddComponent<PipelineLeakEmitter>();
        }

        if (занято.Count == 0)
            Debug.LogWarning("[PipelineDefectSpawner] Не размещено ни одного дефекта — проверь, что на трубе есть коллайдеры.");
        else if (занято.Count < количество)
            Debug.LogWarning($"[PipelineDefectSpawner] Размещено {занято.Count} из {количество} (мало места при заданном МинРасстояние).");
    }

    // Готовит режим спавна. Приоритет: точки трассы → коллайдеры трубы → габариты мешей.
    bool Подготовить()
    {
        // Точный режим по точкам
        if (ТочкиТрассы != null && ТочкиТрассы.Length >= 2)
        {
            var список = new List<Vector3>();
            foreach (var т in ТочкиТрассы) if (т != null) список.Add(т.position);
            if (список.Count >= 2)
            {
                _путь = список.ToArray();
                _радиус = РадиусТрубы > 0f ? РадиусТрубы : 0.5f;
                return true;
            }
        }

        if (Труба == null) return false;

        // Слой, на котором труба РЕНДЕРИТСЯ (не коллайдера — тот мог остаться на Default,
        // если капсулы добавляли вручную новыми объектами). Именно этот слой видит монитор.
        var анкерРендер = Труба.GetComponentInChildren<Renderer>();
        _слойТрубы = анкерРендер != null ? анкерРендер.gameObject.layer : Труба.gameObject.layer;

        // Основной режим: коллайдеры трубы
        var cols = new List<Collider>();
        foreach (var c in Труба.GetComponentsInChildren<Collider>())
            if (c != null && !c.isTrigger) cols.Add(c);
        if (cols.Count > 0)
        {
            _коллайдеры = cols.ToArray();
            _габариты = _коллайдеры[0].bounds;
            for (int i = 1; i < _коллайдеры.Length; i++) _габариты.Encapsulate(_коллайдеры[i].bounds);
            return true;
        }

        // Запасной режим: линия из габаритов мешей (коллайдеров нет)
        var меши = Труба.GetComponentsInChildren<Renderer>();
        if (меши.Length == 0) return false;
        Bounds b = меши[0].bounds;
        for (int i = 1; i < меши.Length; i++) b.Encapsulate(меши[i].bounds);
        Vector3 s = b.size;
        Vector3 dir; float half; float d1, d2;
        if (s.x >= s.y && s.x >= s.z) { dir = Vector3.right;   half = s.x * 0.5f; d1 = s.y; d2 = s.z; }
        else if (s.y >= s.z)          { dir = Vector3.up;      half = s.y * 0.5f; d1 = s.x; d2 = s.z; }
        else                          { dir = Vector3.forward; half = s.z * 0.5f; d1 = s.x; d2 = s.y; }
        _путь = new[] { b.center - dir * half * 0.9f, b.center + dir * half * 0.9f };
        _радиус = РадиусТрубы > 0f ? РадиусТрубы : Mathf.Min(d1, d2) * 0.5f;
        Debug.LogWarning("[PipelineDefectSpawner] У трубы нет коллайдеров — дефекты размещаются по габаритам " +
                         "(может быть неточно). Добавь Mesh Collider на сегменты трубы для точной посадки.");
        return true;
    }

    bool СлучайнаяТочка(System.Random rng, out Vector3 поз, out Vector3 наружу, out int слой)
    {
        if (_коллайдеры != null) return ТочкаНаКоллайдере(rng, out поз, out наружу, out слой);
        return ТочкаНаЛинии(rng, out поз, out наружу, out слой);
    }

    // Луч сквозь случайную точку габаритов трубы → первая поверхность коллайдера
    // трубы. Даёт точку РОВНО на трубе с корректной нормалью, при любой геометрии.
    // Возвращает и слой сегмента трубы, куда попал луч (для рендера дефекта).
    bool ТочкаНаКоллайдере(System.Random rng, out Vector3 поз, out Vector3 наружу, out int слой)
    {
        поз = Vector3.zero; наружу = Vector3.up; слой = -1;

        Vector3 цель = new Vector3(
            Mathf.Lerp(_габариты.min.x, _габариты.max.x, (float)rng.NextDouble()),
            Mathf.Lerp(_габариты.min.y, _габариты.max.y, (float)rng.NextDouble()),
            Mathf.Lerp(_габариты.min.z, _габариты.max.z, (float)rng.NextDouble()));

        Vector3 dir = СлучНаправление(rng);
        float L = _габариты.extents.magnitude + 1f;
        Vector3 из = цель - dir * L;

        if (Physics.Raycast(из, dir, out RaycastHit hit, L * 2f, ~0, QueryTriggerInteraction.Ignore))
        {
            // принимаем, только если попали в саму трубу (а не в грунт/аппарат)
            if (hit.collider.transform == Труба || hit.collider.transform.IsChildOf(Труба))
            {
                // фильтр по имени сегмента (тело трубы, а не фланцы/болты)
                if (!string.IsNullOrEmpty(ФильтрИмениСегмента) &&
                    hit.collider.name.IndexOf(ФильтрИмениСегмента, System.StringComparison.OrdinalIgnoreCase) < 0)
                    return false;
                // фильтр «только верх» — поверхность должна смотреть достаточно вверх
                if (ТолькоВерхняяЧасть && Vector3.Dot(hit.normal, Vector3.up) < ПорогВерха)
                    return false;

                поз = hit.point;
                наружу = hit.normal;
                слой = _слойТрубы;   // на слой рендера трубы — чтобы дефект был виден на мониторе
                return true;
            }
        }
        return false;
    }

    // Запасной режим: случайная точка вдоль линии + случайный угол вокруг оси.
    bool ТочкаНаЛинии(System.Random rng, out Vector3 поз, out Vector3 наружу, out int слой)
    {
        слой = _слойТрубы;   // задаётся в Подготовить из рендерера трубы (−1, если трубы нет)
        int сег = rng.Next(_путь.Length - 1);
        Vector3 a = _путь[сег], b = _путь[сег + 1];
        float t = (float)rng.NextDouble();
        Vector3 центр = Vector3.Lerp(a, b, t);

        Vector3 ось = (b - a).normalized;
        if (ось.sqrMagnitude < 1e-4f) ось = Vector3.forward;
        Vector3 опора = Mathf.Abs(Vector3.Dot(ось, Vector3.up)) > 0.95f ? Vector3.right : Vector3.up;
        Vector3 перп = Vector3.Cross(ось, опора).normalized;
        float угол = (float)(rng.NextDouble() * 360.0);
        наружу = Quaternion.AngleAxis(угол, ось) * перп;
        поз = центр + наружу * _радиус;
        return true;
    }

    // Равномерное случайное направление на сфере.
    Vector3 СлучНаправление(System.Random rng)
    {
        float u = (float)(rng.NextDouble() * 2.0 - 1.0);
        float th = (float)(rng.NextDouble() * System.Math.PI * 2.0);
        float s = Mathf.Sqrt(Mathf.Max(0f, 1f - u * u));
        return new Vector3(s * Mathf.Cos(th), s * Mathf.Sin(th), u);
    }

    bool ЕстьПрефаб()
    {
        if (ПрефабыДефектов == null) return false;
        foreach (var p in ПрефабыДефектов) if (p != null) return true;
        return false;
    }

    GameObject ВыбратьПрефаб(System.Random rng)
    {
        for (int t = 0; t < 8; t++)
        {
            var p = ПрефабыДефектов[rng.Next(ПрефабыДефектов.Length)];
            if (p != null) return p;
        }
        return null;
    }

    // Ставит слой объекту и всем его потомкам (Unity не делает это за нас).
    static void УстановитьСлойРекурсивно(GameObject go, int слой)
    {
        go.layer = слой;
        foreach (Transform ч in go.transform)
            УстановитьСлойРекурсивно(ч.gameObject, слой);
    }

    void OnDrawGizmosSelected()
    {
        if (_путь != null && _путь.Length >= 2)
        {
            Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.9f);
            for (int i = 0; i < _путь.Length - 1; i++)
                Gizmos.DrawLine(_путь[i], _путь[i + 1]);
        }
    }
}

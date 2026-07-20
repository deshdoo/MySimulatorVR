using System.Collections.Generic;
using UnityEngine;

// Физическая модель умбиликального кабеля НПА в подводной среде.
//
// Трос строится в рантайме как цепочка твёрдых тел (Rigidbody) с капсульными
// коллайдерами, соединённых суставами (ConfigurableJoint): линейное движение
// в суставе заблокировано (звенья держатся вместе), вращение свободно —
// поэтому трос гибкий и подвижный, а не жёсткий «отрезками». Один конец
// прикреплён суставом к Rigidbody корпуса НПА, второй — к кинематическому
// якорю в точке управления (RovSystems.basePosition, куда пишет
// ControlPointMarker). Связь двусторонняя: трос физически висит на аппарате
// и тянет его через сустав, как реальный кабель.
//
// Коллайдеры звеньев сталкиваются с дном сами (обычная физика Unity), поэтому
// трос ложится на рельеф и не проваливается под Terrain — без рейкаст-костылей.
//
// Подводные силы, приложенные к каждому звену каждый FixedUpdate:
//   • Сила Архимеда (плавучесть):      F_b = ρ_воды · V_звена · g   (вверх)
//   • Вес:                             F_g = m · g                  (вниз, штатная гравитация)
//     → знак равнодействующей задаётся плотностью кабеля относительно воды:
//       плотнее воды — тонет и ложится на дно, легче — всплывает.
//   • Квадратичное сопротивление воды:  F_d = −½ · ρ_воды · Cd · A · v · |v|
//     (та же модель формы-сопротивления, что и у корпуса НПА в _RovController_).
//
// Фактическая длина троса (по звеньям, не по прямой) отдаётся расчёту связи
// в RovSystemsSimulator — провисающий кабель длиннее прямой линии между концами.
public class UmbilicalCable : MonoBehaviour
{
    // RovSystemsSimulator читает текущую длину троса через этот статический доступ.
    public static UmbilicalCable Instance { get; private set; }

    [Header("Крепление на НПА")]
    [Tooltip("Transform корпуса НПА, к которому крепится кабель (на нём или в родителе должен быть Rigidbody)")]
    public Transform rov;
    [Tooltip("Точка выхода кабеля в локальных координатах корпуса (например, корма/верх)")]
    public Vector3 localAnchorOffset = new Vector3(0f, 0.1f, -0.6f);
    [Tooltip("Тянуть аппарат натяжением троса (двусторонняя связь). Выкл — трос висит на дроне, но не двигает его")]
    public bool pullRov = false;

    [Header("Второй конец (база)")]
    [Tooltip("Крепить дальний конец к базе. Выкл — трос свободно тянется за аппаратом и лежит на дне")]
    public bool attachToBase = true;
    [Tooltip("Точка закрепления дальнего конца (станция/выход кабеля на дне). Если пусто — берётся RovSystems.basePosition, иначе точка старта дрона")]
    public Transform baseTransform;

    [Header("Геометрия троса")]
    [Tooltip("Число звеньев цепочки. Больше — плавнее и гибче, дороже по физике")]
    public int segmentCount = 20;
    [Tooltip("Суммарная длина кабеля, м. Должна быть не меньше макс. удаления дрона от базы, иначе трос натянется в струну")]
    public float totalCableLength_m = 40f;
    [Tooltip("Радиус кабеля, м (для коллайдера, сопротивления и массы)")]
    public float cableRadius_m = 0.03f;

    [Header("Подводная физика (модель Морисона)")]
    [Tooltip("Плотность материала кабеля, кг/м³ (масса звена + вес в воде)")]
    public float cableDensity = 1300f;
    [Tooltip("Плотность воды, кг/м³ (сила Архимеда + сопротивление)")]
    public float waterDensity = 1025f;
    [Tooltip("Коэффициент сопротивления поперёк оси кабеля (Cd нормальный, обычно ≈1.2)")]
    public float dragCoefNormal = 1.2f;
    [Tooltip("Коэффициент сопротивления вдоль оси кабеля (Cd тангенциальный, трение, обычно ≈0.01–0.03)")]
    public float dragCoefTangential = 0.02f;
    [Tooltip("Малое линейное демпфирование для численной устойчивости")]
    public float extraLinearDamping = 0.3f;
    [Tooltip("Угловое демпфирование звеньев")]
    public float angularDamping = 2.0f;

    [Header("Сустав")]
    [Tooltip("Ограничить излом троса в суставе (0 = абсолютно гибкий, 180 = без ограничения)")]
    [Range(0f, 180f)] public float bendLimitDeg = 120f;

    [Header("Рендер")]
    [Tooltip("LineRenderer для отрисовки троса (если пусто — берётся с этого объекта)")]
    public LineRenderer line;
    [Tooltip("Сглаживание: сколько точек рисовать между соседними звеньями (1 = ломаная по звеньям, 6+ = плавная кривая)")]
    [Range(1, 12)] public int renderSubdivisions = 6;

    Rigidbody[] _segments;
    Rigidbody _baseAnchor;         // кинематический якорь в точке управления (если attachToBase)
    Rigidbody _rovAnchor;          // кинематический якорь, следующий за НПА (если не pullRov)
    Rigidbody _rovBody;
    float _segmentLength;
    Vector3 _fixedBasePoint;       // запасная точка крепления базы (на дне под стартом дрона), если база не задана явно
    Vector3[] _controlPoints;      // опорные точки для сглаживающего сплайна (концы + центры звеньев)

    // Текущая фактическая длина троса (по звеньям), м.
    public float CableLength { get; private set; }

    void Awake()
    {
        Instance = this;
        if (line == null) line = GetComponent<LineRenderer>();
        _segmentLength = totalCableLength_m / Mathf.Max(1, segmentCount);
    }

    void Start()
    {
        if (rov != null)
            _rovBody = rov.GetComponent<Rigidbody>() ?? rov.GetComponentInParent<Rigidbody>();

        BuildChain();
    }

    Vector3 AnchorOnRov()
    {
        return rov != null ? rov.TransformPoint(localAnchorOffset) : transform.position;
    }

    // Мировая точка, за которую держится дальний конец троса.
    // Приоритет: явно назначенный baseTransform → точка управления (кабина,
    // через ControlPointMarker) → запасная точка старта дрона на дне.
    Vector3 BaseWorldPoint()
    {
        if (baseTransform != null) return baseTransform.position;
        if (RovSystems.basePosition != Vector3.zero) return RovSystems.basePosition;
        return _fixedBasePoint;
    }

    void BuildChain()
    {
        Vector3 startP = AnchorOnRov();

        // Запасная точка базы — на дне под точкой старта дрона (а не в плавающей
        // точке в толще воды): рейкаст вниз до рельефа, пропуская коллайдеры
        // самого НПА. Так трос закреплён на грунте, если явная база не задана.
        _fixedBasePoint = FindSeabedBelow(startP);

        Vector3 endP = BaseWorldPoint();
        if ((endP - startP).sqrMagnitude < 1e-4f)
            endP = startP + Vector3.down * totalCableLength_m; // база совпала со стартом — раскладываем трос вниз

        Vector3 dir = (endP - startP);
        dir = dir.sqrMagnitude > 1e-4f ? dir.normalized : Vector3.down;
        Quaternion rot = Quaternion.FromToRotation(Vector3.up, dir);

        float V = Mathf.PI * cableRadius_m * cableRadius_m * _segmentLength; // объём звена, м³
        float mass = Mathf.Max(0.001f, cableDensity * V);

        _segments = new Rigidbody[segmentCount];
        var colliders = new List<Collider>();

        // Верхний конец: либо жёстко к Rigidbody НПА (pullRov — тянет аппарат),
        // либо к кинематическому якорю, который просто следует за точкой
        // крепления на корпусе (трос висит на дроне, но не двигает его).
        Rigidbody prev;
        if (pullRov && _rovBody != null)
        {
            prev = _rovBody;
        }
        else
        {
            _rovAnchor = MakeKinematicAnchor("UmbilicalRovAnchor", startP);
            prev = _rovAnchor;
        }

        for (int i = 0; i < segmentCount; i++)
        {
            var go = new GameObject($"UmbilicalSegment_{i}");
            go.transform.SetParent(transform, false);
            // Раскладываем звенья РАВНОМЕРНО между дроном (startP) и базой (endP),
            // а не по прямой фиксированной длины: иначе при длине троса больше
            // расстояния между концами прямая перелетала бы за базу (шпиль вверх
            // над кораблём). При такой раскладке лишняя длина уходит в провисание
            // вниз под действием веса в воде, как и должно быть у кабеля.
            go.transform.position = Vector3.Lerp(startP, endP, (i + 0.5f) / segmentCount);
            go.transform.rotation = rot;

            var col = go.AddComponent<CapsuleCollider>();
            col.direction = 1;                       // вдоль локальной оси Y
            col.radius = cableRadius_m;
            col.height = _segmentLength + cableRadius_m * 2f;
            colliders.Add(col);

            var rb = go.AddComponent<Rigidbody>();
            rb.mass = mass;
            rb.useGravity = false; // как у корпуса НПА — вертикальную силу (вес в воде) задаём сами, не через гравитацию проекта
            rb.linearDamping = extraLinearDamping;
            rb.angularDamping = angularDamping;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            _segments[i] = rb;

            // Сустав крепит текущее звено к предыдущему телу (или к корпусу НПА
            // у первого звена). Локальная +Y капсулы направлена к базе, значит
            // конец, обращённый к предыдущему телу (в сторону НПА) — это −Y.
            // autoConfigureConnectedAnchor сам вычислит точку стыка в системе
            // координат предыдущего тела по текущему взаимному положению —
            // звенья построены встык, поэтому стык считается корректно.
            var joint = go.AddComponent<ConfigurableJoint>();
            joint.connectedBody = prev;                 // у первого звена = верхний якорь (корпус НПА или кинематический якорь)
            joint.anchor = new Vector3(0f, -_segmentLength * 0.5f, 0f); // конец звена в сторону НПА
            joint.autoConfigureConnectedAnchor = true;
            ConfigureJoint(joint);

            prev = rb;
        }

        // Дальний конец: либо крепим к якорю в точке управления (attachToBase),
        // либо оставляем свободным — тогда трос просто тянется за аппаратом.
        if (attachToBase)
        {
            _baseAnchor = MakeKinematicAnchor("UmbilicalBaseAnchor", endP);
            var tailJoint = _segments[segmentCount - 1].gameObject.AddComponent<ConfigurableJoint>();
            tailJoint.connectedBody = _baseAnchor;
            tailJoint.anchor = new Vector3(0f, _segmentLength * 0.5f, 0f);
            tailJoint.autoConfigureConnectedAnchor = true;
            ConfigureJoint(tailJoint);
        }

        // Отключаем взаимные столкновения звеньев между собой и с корпусом НПА —
        // иначе цепочка «дрожит» сама об себя и об аппарат. С дном/рельефом
        // коллайдеры звеньев продолжают сталкиваться нормально.
        for (int i = 0; i < colliders.Count; i++)
            for (int j = i + 1; j < colliders.Count; j++)
                Physics.IgnoreCollision(colliders[i], colliders[j]);

        if (rov != null)
        {
            foreach (var rovCol in rov.GetComponentsInChildren<Collider>())
                foreach (var c in colliders)
                    Physics.IgnoreCollision(c, rovCol);
        }

        // Отключаем столкновения троса с самим кораблём-базой (мачта, кран,
        // антенны и т.п.), иначе трос у точки крепления цепляется за оснастку
        // и лезет поверх неё — виден «горб» вверх у основания. Берём весь
        // объект корабля целиком (корень иерархии baseTransform).
        if (baseTransform != null)
        {
            foreach (var baseCol in baseTransform.root.GetComponentsInChildren<Collider>())
                foreach (var c in colliders)
                    Physics.IgnoreCollision(c, baseCol);
        }

        // Опорные точки сплайна: крепёж на НПА + центры звеньев (+ база, если есть).
        _controlPoints = new Vector3[segmentCount + (attachToBase ? 2 : 1)];
    }

    // Ищет дно под точкой from, пропуская коллайдеры самого НПА (иначе рейкаст
    // упёрся бы в корпус дрона). Возвращает точку на грунте либо запасную точку
    // ниже, если дна под дроном не нашлось.
    Vector3 FindSeabedBelow(Vector3 from)
    {
        var hits = Physics.RaycastAll(from + Vector3.up * 0.5f, Vector3.down, 1000f);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (var h in hits)
        {
            if (rov != null && h.collider.transform.IsChildOf(rov)) continue;
            return h.point;
        }
        return from + Vector3.down * totalCableLength_m;
    }

    Rigidbody MakeKinematicAnchor(string anchorName, Vector3 worldPos)
    {
        var go = new GameObject(anchorName);
        go.transform.SetParent(transform, false);
        go.transform.position = worldPos;
        var rb = go.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;
        return rb;
    }

    // Общая настройка сустава звена: линейное движение заблокировано (звенья
    // держатся встык), вращение свободно либо ограничено углом излома троса.
    void ConfigureJoint(ConfigurableJoint joint)
    {
        joint.xMotion = joint.yMotion = joint.zMotion = ConfigurableJointMotion.Locked;

        if (bendLimitDeg >= 179f)
        {
            joint.angularXMotion = joint.angularYMotion = joint.angularZMotion = ConfigurableJointMotion.Free;
        }
        else
        {
            joint.angularXMotion = joint.angularYMotion = joint.angularZMotion = ConfigurableJointMotion.Limited;
            var lowX = joint.lowAngularXLimit; lowX.limit = -bendLimitDeg; joint.lowAngularXLimit = lowX;
            var highX = joint.highAngularXLimit; highX.limit = bendLimitDeg; joint.highAngularXLimit = highX;
            var limY = joint.angularYLimit; limY.limit = bendLimitDeg; joint.angularYLimit = limY;
            var limZ = joint.angularZLimit; limZ.limit = bendLimitDeg; joint.angularZLimit = limZ;
        }

        joint.enablePreprocessing = false; // стабильнее для длинных цепочек
    }

    void FixedUpdate()
    {
        if (_segments == null) return;

        // Кинематический якорь на дроне следует за точкой крепления на корпусе —
        // трос висит на аппарате и повторяет его движение, но не толкает его.
        if (_rovAnchor != null)
            _rovAnchor.MovePosition(AnchorOnRov());

        // Якорь базы всегда в актуальной точке крепления (baseTransform /
        // точка управления / запасная точка старта).
        if (_baseAnchor != null)
            _baseAnchor.MovePosition(BaseWorldPoint());

        ApplyUnderwaterForces();
    }

    // Гидродинамика звена по модели Морисона (каноническая модель нагрузки на
    // тонкий цилиндр в потоке). Все формулы — в Docs/Отчет_Физика_НПА.md, 2.14.
    void ApplyUnderwaterForces()
    {
        const float g = 9.81f;                                  // стандартное ускорение свободного падения, м/с²
        float D = 2f * cableRadius_m;                           // диаметр кабеля, м
        float L = _segmentLength;                               // длина звена, м
        float V = Mathf.PI * cableRadius_m * cableRadius_m * L; // объём звена, м³

        // Вес в воде = вес − сила Архимеда = (ρ_кабеля − ρ_воды)·V·g.
        // При ρ_кабеля > ρ_воды результат положительный → трос тонет и ложится
        // на дно. Выведено из плотностей, а не подобрано вручную.
        float submergedWeight = (cableDensity - waterDensity) * V * g;

        foreach (var rb in _segments)
        {
            if (rb == null) continue;

            rb.AddForce(Vector3.down * submergedWeight, ForceMode.Force);

            // Сопротивление по Морисону: скорость раскладывается на нормальную
            // (поперёк оси кабеля) и тангенциальную (вдоль оси) составляющие —
            // у цилиндра это принципиально разные режимы обтекания с разными
            // коэффициентами и разной опорной площадью.
            Vector3 axis = rb.transform.up;                    // локальная +Y капсулы = ось звена
            Vector3 v = rb.linearVelocity;
            Vector3 vT = Vector3.Dot(v, axis) * axis;          // вдоль оси
            Vector3 vN = v - vT;                                // поперёк оси

            // Нормальная составляющая: опорная площадь = проекция цилиндра = D·L.
            float sN = vN.magnitude;
            if (sN > 1e-4f)
                rb.AddForce(-0.5f * waterDensity * dragCoefNormal * (D * L) * sN * vN, ForceMode.Force);

            // Тангенциальная (трение): опорная площадь = боковая поверхность = π·D·L.
            float sT = vT.magnitude;
            if (sT > 1e-4f)
                rb.AddForce(-0.5f * waterDensity * dragCoefTangential * (Mathf.PI * D * L) * sT * vT, ForceMode.Force);
        }
    }

    void LateUpdate()
    {
        if (_segments == null || line == null || _controlPoints == null) return;

        // Собираем опорные точки: крепёж на НПА, центры звеньев (+ база, если есть).
        int cc = _controlPoints.Length;
        _controlPoints[0] = AnchorOnRov();
        for (int i = 0; i < segmentCount; i++)
            _controlPoints[i + 1] = _segments[i].position;
        if (_baseAnchor != null)
            _controlPoints[cc - 1] = _baseAnchor.position;

        // Фактическая длина троса — по опорным точкам (по звеньям, не по сплайну).
        float len = 0f;
        for (int i = 0; i < cc - 1; i++)
            len += Vector3.Distance(_controlPoints[i], _controlPoints[i + 1]);
        CableLength = len;

        // Рисуем сглаженную кривую Catmull-Rom через опорные точки, чтобы трос
        // выглядел как гибкий деформирующийся кабель, а не ломаная из отрезков.
        int sub = Mathf.Max(1, renderSubdivisions);
        int totalPoints = (cc - 1) * sub + 1;
        if (line.positionCount != totalPoints) line.positionCount = totalPoints;

        int idx = 0;
        for (int seg = 0; seg < cc - 1; seg++)
        {
            Vector3 p0 = _controlPoints[Mathf.Max(seg - 1, 0)];
            Vector3 p1 = _controlPoints[seg];
            Vector3 p2 = _controlPoints[seg + 1];
            Vector3 p3 = _controlPoints[Mathf.Min(seg + 2, cc - 1)];

            for (int s = 0; s < sub; s++)
            {
                float u = (float)s / sub;
                line.SetPosition(idx++, CentripetalCatmullRom(p0, p1, p2, p3, u));
            }
        }
        line.SetPosition(idx, _controlPoints[cc - 1]); // замыкающая точка
    }

    // Центростремительный сплайн Катмулла-Рома (α = 0.5). Проходит точно через
    // опорные точки, но, в отличие от равномерного варианта, математически не
    // даёт петель и острых «клювов» (cusp) на резких изломах и при неравномерно
    // расположенных точках — именно эти выбросы портили вид на концах троса.
    static Vector3 CentripetalCatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float u)
    {
        const float alpha = 0.5f;
        float t0 = 0f;
        float t1 = t0 + Mathf.Pow((p1 - p0).magnitude, alpha);
        float t2 = t1 + Mathf.Pow((p2 - p1).magnitude, alpha);
        float t3 = t2 + Mathf.Pow((p3 - p2).magnitude, alpha);

        // Защита от совпадающих точек (нулевые интервалы → деление на ноль).
        if (t1 <= t0) t1 = t0 + 1e-4f;
        if (t2 <= t1) t2 = t1 + 1e-4f;
        if (t3 <= t2) t3 = t2 + 1e-4f;

        float t = Mathf.Lerp(t1, t2, u);

        Vector3 a1 = (t1 - t) / (t1 - t0) * p0 + (t - t0) / (t1 - t0) * p1;
        Vector3 a2 = (t2 - t) / (t2 - t1) * p1 + (t - t1) / (t2 - t1) * p2;
        Vector3 a3 = (t3 - t) / (t3 - t2) * p2 + (t - t2) / (t3 - t2) * p3;
        Vector3 b1 = (t2 - t) / (t2 - t0) * a1 + (t - t0) / (t2 - t0) * a2;
        Vector3 b2 = (t3 - t) / (t3 - t1) * a2 + (t - t1) / (t3 - t1) * a3;
        return (t2 - t) / (t2 - t1) * b1 + (t - t1) / (t2 - t1) * b2;
    }
}

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
    [Tooltip("Точка заделки кабеля на модели аппарата. Пусто — скоба подвеса «Скоба» (кабель идёт вместе с грузовым тросом), иначе «ТочкаЗаделкиКабеля» " +
             "(или «ЗаделкаКабеля») среди дочерних объектов НПА; не нашлась — берётся localAnchorOffset.")]
    public Transform точкаЗаделки;
    [Tooltip("Тянуть аппарат натяжением троса (двусторонняя связь). Выкл — трос висит на дроне, но не двигает его")]
    public bool pullRov = false;

    [Tooltip("Натяжение троса действует на аппарат: сила в первом суставе (у заделки) прикладывается " +
             "к корпусу в точке заделки. В отличие от pullRov, связь через силу, а не жёстким суставом — " +
             "устойчиво при любом соотношении масс троса и аппарата.")]
    public bool натяжениеНаАппарат = true;
    [Tooltip("Предел силы натяжения на корпус, Н (страховка от рывков при старте/переносе аппарата).")]
    public float предельНатяжения_Н = 400f;
    [Tooltip("Натяжение, выше которого оператору выдаётся предупреждение «кабель натянут», Н.")]
    public float порогПредупреждения_Н = 120f;

    [Header("Паспорт «Марлин-350»")]
    [Tooltip("Взять параметры кабеля по классу «Марлин-350»: оптоэлектрический кабель Ø15 мм, " +
             "плотность чуть выше воды (≈1080 кг/м³ — слабо отрицательная плавучесть, в воде весит ≈10 г/м). " +
             "Перекрывает радиус и плотность из инспектора.")]
    public bool параметрыМарлин350 = true;

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
    [Tooltip("Цвет кабеля (у «Марлина-350» — жёлтый оптоэлектрический кабель).")]
    public Color цветКабеля = new Color(0.95f, 0.62f, 0.02f);
    [Tooltip("Рисовать кабель объёмной трубкой с освещённым материалом (блик, тень, реальный диаметр) " +
             "вместо плоской линии. Выкл — прежний LineRenderer.")]
    public bool объёмныйКабель = true;
    [Tooltip("Граней по окружности трубки.")]
    [Range(4, 16)] public int граней = 8;

    Rigidbody[] _segments;
    Rigidbody _baseAnchor;         // кинематический якорь в точке управления (если attachToBase)
    Rigidbody _rovAnchor;          // кинематический якорь, следующий за НПА (если не pullRov)
    Rigidbody _rovBody;
    float _segmentLength;
    Vector3 _fixedBasePoint;       // запасная точка крепления базы (на дне под стартом дрона), если база не задана явно
    Vector3[] _controlPoints;      // опорные точки для сглаживающего сплайна (концы + центры звеньев)

    WaterCurrent _течение;
    Vector3 _натяжение;            // сглаженная сила троса на корпус, Н
    float _времяСтарта;
    bool _предупреждён;

    // Текущее натяжение троса у заделки, Н (для телеметрии и предупреждений).
    public float Натяжение_Н => _натяжение.magnitude;

    // Текущая фактическая длина троса (по звеньям), м.
    public float CableLength { get; private set; }

    void Awake()
    {
        Instance = this;
        if (line == null) line = GetComponent<LineRenderer>();
        if (параметрыМарлин350)
        {
            cableRadius_m = 0.0075f;
            cableDensity = 1080f;
        }
        if (line != null)
        {
            // Толщина линии = реальный диаметр кабеля (не тоньше 1,5 см, чтобы был виден в шлеме).
            line.widthCurve = AnimationCurve.Constant(0f, 1f, 1f);
            line.widthMultiplier = Mathf.Max(2f * cableRadius_m, 0.015f);
            line.startColor = line.endColor = цветКабеля;
            var m = line.material;   // свой экземпляр материала, общий ассет не трогаем
            if (m != null)
            {
                if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", цветКабеля);
                if (m.HasProperty("_Color")) m.SetColor("_Color", цветКабеля);
            }
        }
        _segmentLength = totalCableLength_m / Mathf.Max(1, segmentCount);
    }

    void Start()
    {
        if (rov != null)
        {
            _rovBody = rov.GetComponent<Rigidbody>() ?? rov.GetComponentInParent<Rigidbody>();
            var rc = rov.GetComponentInParent<RovController>();
            if (rc != null) _течение = rc.Течение;
            if (точкаЗаделки == null)
                foreach (var t in rov.GetComponentsInChildren<Transform>(true))
                    if (t.name == "Скоба" || (точкаЗаделки == null && (t.name == "ТочкаЗаделкиКабеля" || t.name == "ЗаделкаКабеля")))
                        точкаЗаделки = t;   // кабель-трос с грузовым тросом приходят к скобе подвеса
        }

        BuildChain();
        _времяСтарта = Time.time;
    }

    Vector3 AnchorOnRov()
    {
        if (точкаЗаделки != null) return точкаЗаделки.position;
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

    // Испытательные манёвры (RovManeuverTests) переносят аппарат в исходную точку
    // перед каждым опытом. Чтобы трос не дёргало через всю сцену и он не мешал
    // столкновениями, на время испытаний звенья выключаются целиком.
    public void ЗаморозитьДляИспытаний(bool заморозить)
    {
        if (_segments != null)
            foreach (var s in _segments) if (s != null) s.gameObject.SetActive(!заморозить);
        if (_rovAnchor != null) _rovAnchor.gameObject.SetActive(!заморозить);
        if (line != null) line.enabled = !заморозить;
        if (_трубка != null) _трубка.gameObject.SetActive(!заморозить);
        enabled = !заморозить;
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
        ПриложитьНатяжение();
    }

    // Натяжение троса → корпус. Первый сустав держит звено 0 у заделки; сила, с которой
    // он действует на звено (Joint.currentForce, мировые оси), по третьему закону
    // Ньютона с обратным знаком действует на точку заделки на аппарате. Так снос троса
    // течением, его вес и зацеп за трубу реально тянут аппарат (раньше трос только
    // висел на нём). Сглаживание ~0,2 с убирает шум цепочки суставов, первые 2 с после
    // старта сила плавно нарастает (трос раскладывается и успокаивается).
    void ПриложитьНатяжение()
    {
        if (!натяжениеНаАппарат || pullRov || _rovBody == null || _segments == null || _segments.Length == 0) return;
        var j = _segments[0].GetComponent<ConfigurableJoint>();
        if (j == null) return;

        Vector3 F = -j.currentForce;
        float dt = Time.fixedDeltaTime;
        _натяжение = Vector3.Lerp(_натяжение, F, 1f - Mathf.Exp(-dt / 0.2f));
        float разгон = Mathf.Clamp01((Time.time - _времяСтарта) / 2f);
        Vector3 Fк = Vector3.ClampMagnitude(_натяжение, предельНатяжения_Н) * разгон;
        _rovBody.AddForceAtPosition(Fк, AnchorOnRov(), ForceMode.Force);

        bool натянут = Натяжение_Н > порогПредупреждения_Н;
        if (натянут && !_предупреждён) Debug.Log($"[Кабель] Натянут: {Натяжение_Н:F0} Н — потравить кабель или вернуться к базе");
        _предупреждён = натянут;
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
            // Сопротивление — от скорости ОТНОСИТЕЛЬНО ВОДЫ (как у корпуса): течение
            // сносит трос, а снесённый трос тянет аппарат (см. ПриложитьНатяжение).
            Vector3 v = rb.linearVelocity - (_течение != null ? _течение.Скорость(rb.position) : Vector3.zero);
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
        if (_точки == null || _точки.Length != totalPoints) _точки = new Vector3[totalPoints];

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
                _точки[idx++] = CentripetalCatmullRom(p0, p1, p2, p3, u);
            }
        }
        _точки[idx] = _controlPoints[cc - 1]; // замыкающая точка

        if (объёмныйКабель)
        {
            line.enabled = false;
            Трубка(_точки);
        }
        else
        {
            if (_трубка != null) _трубка.gameObject.SetActive(false);
            line.enabled = true;
            if (line.positionCount != totalPoints) line.positionCount = totalPoints;
            line.SetPositions(_точки);
        }
    }

    // ---- объёмная трубка по сплайну ----
    // Кабель-трос «Марлина» идёт в паре с грузовым тросом: две линии вплотную, стянутые
    // хомутами через равные промежутки, обе приходят к скобе подвеса на аппарате. Физика
    // одна (это одна связка), рисуются три меша: жёлтый кабель, серый трос и хомуты.
    Vector3[] _точки;
    ТрубкаМеш _кабель, _трос, _хомуты;
    Transform _трубка;   // общий родитель мешей (для включения/выключения)
    Vector3[] _кас, _норм, _бин;
    readonly System.Collections.Generic.List<int> _гдеХомуты = new System.Collections.Generic.List<int>();

    class ТрубкаМеш
    {
        public MeshFilter mf; public Mesh меш;
        public Vector3[] в, н; int[] тр; int n, m, кусков;

        public ТрубкаМеш(Transform родитель, string имя, Color цвет, float гладкость, int слой)
        {
            var go = new GameObject(имя) { layer = слой };
            go.transform.SetParent(родитель, false);
            mf = go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var мат = new Material(sh) { name = имя + " (авто)" };
            if (мат.HasProperty("_BaseColor")) мат.SetColor("_BaseColor", цвет);
            if (мат.HasProperty("_Color")) мат.SetColor("_Color", цвет);
            if (мат.HasProperty("_Smoothness")) мат.SetFloat("_Smoothness", гладкость);
            if (мат.HasProperty("_Metallic")) мат.SetFloat("_Metallic", гладкость > 0.6f ? 0.9f : 0f);
            mr.sharedMaterial = мат;
            меш = new Mesh { name = имя }; меш.MarkDynamic(); mf.sharedMesh = меш;
        }

        // Сетка «кусков» по n колец из m вершин (каждый кусок — отдельная трубка).
        public void Сетка(int колец, int граней, int кусков_)
        {
            if (в != null && n == колец && m == граней && кусков == кусков_) return;
            n = колец; m = граней; кусков = кусков_;
            в = new Vector3[кусков * n * m]; н = new Vector3[в.Length];
            тр = new int[кусков * (n - 1) * m * 6];
            int t = 0;
            for (int k = 0; k < кусков; k++)
                for (int i = 0; i < n - 1; i++)
                    for (int j = 0; j < m; j++)
                    {
                        int o = k * n * m;
                        int a = o + i * m + j, b = o + i * m + (j + 1) % m, c = a + m, d = b + m;
                        тр[t++] = a; тр[t++] = c; тр[t++] = b; тр[t++] = b; тр[t++] = c; тр[t++] = d;
                    }
            меш.Clear(); меш.vertices = в; меш.triangles = тр;
        }

        public void Кольцо(int кусок, int i, Vector3 центр, Vector3 нрм, Vector3 бн, float r)
        {
            for (int j = 0; j < m; j++)
            {
                float ф = j * Mathf.PI * 2f / m;
                Vector3 рад = нрм * Mathf.Cos(ф) + бн * Mathf.Sin(ф);
                int idx = кусок * n * m + i * m + j;
                в[idx] = центр + рад * r; н[idx] = рад;
            }
        }

        public void Применить() { меш.vertices = в; меш.normals = н; меш.RecalculateBounds(); }
    }

    [Header("Грузовой трос рядом с кабелем")]
    [Tooltip("Рисовать стальной грузовой трос вплотную к кабелю, стянутый хомутами.")]
    public bool тросРядом = true;
    [Tooltip("Радиус грузового троса, м.")]
    public float радиусТроса_м = 0.005f;
    [Tooltip("Шаг хомутов вдоль кабеля, м.")]
    public float шагХомутов_м = 2.5f;

    void Трубка(Vector3[] pts)
    {
        int n = pts.Length, m = Mathf.Max(4, граней);
        // После перекомпиляции во время Play обычные классы (ТрубкаМеш) обнуляются, а объект
        // остаётся — пересоздаём всё заново.
        if (_трубка != null && (_кабель == null || _трос == null || _хомуты == null)) { Destroy(_трубка.gameObject); _трубка = null; }
        if (_трубка == null)
        {
            var go = new GameObject("Кабель-трос (меш)") { layer = line.gameObject.layer };
            go.transform.SetParent(transform, false);
            _трубка = go.transform;
            _кабель = new ТрубкаМеш(_трубка, "Кабель", цветКабеля, 0.45f, go.layer);
            _трос = new ТрубкаМеш(_трубка, "Грузовой трос", new Color(0.55f, 0.56f, 0.58f), 0.7f, go.layer);
            _хомуты = new ТрубкаМеш(_трубка, "Хомуты", new Color(0.04f, 0.04f, 0.045f), 0.3f, go.layer);
        }
        _трубка.gameObject.SetActive(true);
        // Вершины — в мировых координатах: держим объект в начале мира без поворота и масштаба.
        _трубка.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        Vector3 ls = transform.lossyScale;
        _трубка.localScale = new Vector3(1f / Mathf.Max(1e-4f, ls.x), 1f / Mathf.Max(1e-4f, ls.y), 1f / Mathf.Max(1e-4f, ls.z));

        // Рамки вдоль кривой — параллельным переносом (кольца не перекручиваются).
        if (_кас == null || _кас.Length != n) { _кас = new Vector3[n]; _норм = new Vector3[n]; _бин = new Vector3[n]; }
        Vector3 касПрев = (pts[Mathf.Min(1, n - 1)] - pts[0]).normalized;
        if (касПрев.sqrMagnitude < 1e-6f) касПрев = Vector3.down;
        Vector3 нормаль = Vector3.Cross(касПрев, Mathf.Abs(касПрев.y) > 0.9f ? Vector3.right : Vector3.up).normalized;
        for (int i = 0; i < n; i++)
        {
            Vector3 кас = pts[Mathf.Min(i + 1, n - 1)] - pts[Mathf.Max(i - 1, 0)];
            if (кас.sqrMagnitude < 1e-8f) кас = касПрев; else кас.Normalize();
            нормаль = (Quaternion.FromToRotation(касПрев, кас) * нормаль).normalized;
            _кас[i] = кас; _норм[i] = нормаль; _бин[i] = Vector3.Cross(кас, нормаль);
            касПрев = кас;
        }

        float rК = Mathf.Max(cableRadius_m, 0.006f);
        float rТ = Mathf.Max(радиусТроса_м, 0.003f);
        float сдвиг = rК + rТ;   // трос вплотную к кабелю

        _кабель.Сетка(n, m, 1);
        for (int i = 0; i < n; i++) _кабель.Кольцо(0, i, pts[i], _норм[i], _бин[i], rК);
        _кабель.Применить();

        _трос.mf.gameObject.SetActive(тросРядом);
        _хомуты.mf.gameObject.SetActive(тросРядом);
        if (!тросРядом) return;

        _трос.Сетка(n, 6, 1);
        for (int i = 0; i < n; i++) _трос.Кольцо(0, i, pts[i] + _норм[i] * сдвиг, _норм[i], _бин[i], rТ);
        _трос.Применить();

        // Хомуты — короткие широкие кольца, охватывающие оба, через шагХомутов_м по длине.
        var где = _гдеХомуты; где.Clear();
        float пройдено = 0f, след = 0.4f;   // первый хомут — у самой скобы
        for (int i = 1; i < n; i++)
        {
            пройдено += Vector3.Distance(pts[i - 1], pts[i]);
            if (пройдено >= след) { где.Add(i); след += Mathf.Max(0.3f, шагХомутов_м); }
        }
        int хом = Mathf.Max(1, где.Count);
        _хомуты.Сетка(2, 10, хом);
        float rХ = сдвиг * 0.5f + rК + 0.004f;   // охватывает пару
        for (int k = 0; k < хом; k++)
        {
            int i = где.Count > 0 ? где[k] : 0;
            Vector3 ц = pts[i] + _норм[i] * сдвиг * 0.5f;
            _хомуты.Кольцо(k, 0, ц - _кас[i] * 0.02f, _норм[i], _бин[i], rХ);
            _хомуты.Кольцо(k, 1, ц + _кас[i] * 0.02f, _норм[i], _бин[i], rХ);
        }
        _хомуты.Применить();
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

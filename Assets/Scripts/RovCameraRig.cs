using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;

// Три камеры «Марлина-350» (как у прототипа: камеры на наклонной платформе + рабочие):
//   1. КУРСОВАЯ — верхняя, смотрит вперёд; её видео всегда на большом экране кабины
//      (бортовая камера, что пишет в текстуру монитора);
//   2. ЗАХВАТ — нижняя, смотрит на клешню; своё видео → на Monitor3;
//   3. КОННЕКТОР — нижняя, смотрит на головку инструмента; своё видео → на Monitor2.
// Камеры — стёкла-купола на модели («Купол…»): их перекладывают в редакторе как угодно,
// скрипт сам находит включённые и раздаёт роли: самая верхняя — курсовая, ближайшая к
// клешне — захват, ближайшая к инструменту — коннектор. Направления жёстко привязаны к
// корпусу (без поворотов) — цель всегда в одном месте кадра.
//
// Видео рабочих камер на мониторах включает и выключает оператор: голубая кнопка пульта
// (действие ToggleCameras) или клавиша V. Коннектор показывается только в комплектации
// с инструментом.
// Ставится на корпус НПА (RovController.Start). Мониторы в кабине — CabinCameraFeed.
public class RovCameraRig : MonoBehaviour
{
    public static RovCameraRig Instance { get; private set; }

    // Бортовая (курсовая) камера — её видео на главном экране. Рабочие камеры (захват,
    // коннектор) и камеры учебных показов тоже пишут в текстуры, поэтому «первая камера с
    // targetTexture» больше не годится: искать бортовую — только через эту функцию.
    public static Camera Бортовая()
    {
        if (Instance != null && Instance._курсовая != null) return Instance._курсовая;
        foreach (var c in FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (c.targetTexture != null && ЭтоБортовая(c)) return c;
        return null;
    }
    static bool ЭтоБортовая(Camera c) =>
        !c.name.StartsWith("Камера захвата") && !c.name.StartsWith("Камера коннектора") && !c.name.StartsWith("Камера наблюдения");

    // Видео рабочих камер на мониторах: вкл/выкл по нажатию.
    public static bool ВидеоВключено { get; private set; }
    public static void Переключить()
    {
        ВидеоВключено = !ВидеоВключено;
        Debug.Log($"[Камеры] видео захвата и коннектора на мониторах: {(ВидеоВключено ? "ВКЛ" : "ВЫКЛ")}");
    }

    public Key клавиша = Key.V;
    [Tooltip("Ближняя плоскость отсечения, м.")]
    public float БлижняяПлоскость = 0.02f;
    [Tooltip("Наклон курсовой камеры вниз, град.")]
    public float НаклонКурсовой = 10f;
    [Tooltip("Сдвиг точки съёмки вперёд от центра купола, м (чтобы не видеть свой купол изнутри).")]
    public float ВыносИзКупола = 0.035f;
    [Tooltip("Разрешение видео рабочих камер.")]
    public Vector2Int Разрешение = new Vector2Int(640, 400);
    [Tooltip("Дальность видимости рабочих камер, м: они смотрят на клешню и коннектор вблизи — " +
             "дальний план не нужен, а рисовать его дорого.")]
    public float ДальностьРабочих_м = 25f;
    [Tooltip("Рабочие камеры обновляются раз в столько кадров (2 — через кадр: вдвое дешевле, глазу незаметно).")]
    [Range(1, 4)] public int КадровНаОбновление = 2;

    [Header("Камера коннектора")]
    [Tooltip("Сдвиг камеры коннектора вбок от инструмента к центру аппарата, м.")]
    public float КоннекторВправо_м = 0.20f;
    [Tooltip("Сдвиг камеры коннектора вверх, м.")]
    public float КоннекторВверх_м = 0.18f;
    [Tooltip("Куда смотрит камера коннектора: на точку впереди головки инструмента, м — видно и головку, и стакан крана.")]
    public float КоннекторВзглядВперёд_м = 0.06f;
    [Tooltip("Насколько камера коннектора позади головки инструмента, м (ракурс три четверти, как у захвата).")]
    public float КоннекторДистанция_м = 0.30f;

    [Header("Камера захвата")]
    [Tooltip("Насколько камера захвата позади челюстей клешни, м (ракурс три четверти, как у коннектора).")]
    public float ЗахватДистанция_м = 0.22f;
    [Tooltip("Сдвиг камеры захвата вбок от клешни к центру аппарата, м.")]
    public float ЗахватВправо_м = 0.16f;
    [Tooltip("Сдвиг камеры захвата вверх, м — сверху видно, как сходятся челюсти.")]
    public float ЗахватВверх_м = 0.14f;
    [Tooltip("Угол обзора рабочих камер, град (было 60/55, +20 %).")]
    public float УголЗахват = 72f, УголКоннектор = 66f;

    // Видео рабочих камер и нужно ли его сейчас показывать (читает CabinCameraFeed).
    public RenderTexture ВидеоЗахват { get; private set; }
    public RenderTexture ВидеоКоннектор { get; private set; }
    public bool ПоказатьЗахват { get; private set; }
    public bool ПоказатьКоннектор { get; private set; }

    Camera _курсовая, _захват, _коннектор;
    Transform _местоКурс, _местоЗахват, _местоКоннектор;
    Transform _цельКоннектор, _цельЗахват, _шарнирЗахвата;

    void Awake() { Instance = this; }
    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (ВидеоЗахват != null) ВидеоЗахват.Release();
        if (ВидеоКоннектор != null) ВидеоКоннектор.Release();
    }

    void Start()
    {
        foreach (var c in GetComponentsInChildren<Camera>(true))
            if (c.targetTexture != null) { _курсовая = c; break; }
        if (_курсовая == null) { enabled = false; return; }
        _курсовая.nearClipPlane = БлижняяПлоскость;
        _курсовая.fieldOfView = 70f;

        foreach (var t in GetComponentsInChildren<Transform>(true))
        {
            if (t.name == "Носик") _цельКоннектор = t;
            else if (t.name == "Схват_Кончик" || (_цельЗахват == null && t.name == "МеткаЗахвата")) _цельЗахват = t;
            else if (t.name == "Схват_Шарнир") _шарнирЗахвата = t;
        }
        // Рабочая рука rov_gripper (если включена) — камера захвата смотрит на её клешню.
        foreach (var рука in GetComponentsInChildren<RovSim.Rov.Grabber>(false))
        {
            float лучш = float.NegativeInfinity;
            foreach (var r in рука.GetComponentsInChildren<Renderer>(false))
            {
                float вперёд = transform.InverseTransformPoint(r.bounds.center).z;
                if (вперёд > лучш) { лучш = вперёд; _цельЗахват = r.transform; }
            }
            break;
        }

        РаздатьРоли();
        ВидеоЗахват = НовоеВидео("Видео: захват");
        ВидеоКоннектор = НовоеВидео("Видео: коннектор");
        _захват = НоваяКамера("Камера захвата", ВидеоЗахват, УголЗахват);
        _коннектор = НоваяКамера("Камера коннектора", ВидеоКоннектор, УголКоннектор);
    }

    RenderTexture НовоеВидео(string имя)
    {
        var rt = new RenderTexture(Разрешение.x, Разрешение.y, 24) { name = имя };
        rt.Create();
        return rt;
    }

    // Рабочие камеры — копии курсовой (те же слои, фон, туман воды), каждая в свою текстуру.
    Camera НоваяКамера(string имя, RenderTexture rt, float угол)
    {
        var go = new GameObject(имя);
        go.transform.SetParent(transform, false);
        var cam = go.AddComponent<Camera>();
        cam.CopyFrom(_курсовая);
        cam.targetTexture = rt;
        cam.fieldOfView = угол;
        cam.nearClipPlane = БлижняяПлоскость;
        cam.farClipPlane = Mathf.Min(_курсовая.farClipPlane, ДальностьРабочих_м);
        cam.enabled = false;
        // Постобработка как у курсовой (цвет воды, туман, эффекты повреждения камеры):
        // CopyFrom переносит только настройки Camera, а URP хранит их в отдельном компоненте.
        var из = _курсовая.GetUniversalAdditionalCameraData();
        var в = cam.GetUniversalAdditionalCameraData();
        if (из != null && в != null)
        {
            в.renderPostProcessing = из.renderPostProcessing;
            в.volumeLayerMask = из.volumeLayerMask;
            в.volumeTrigger = cam.transform;
            в.antialiasing = из.antialiasing;
            в.antialiasingQuality = из.antialiasingQuality;
            в.stopNaN = из.stopNaN;
            в.dithering = из.dithering;
            в.requiresDepthTexture = из.requiresDepthTexture;
            в.renderShadows = false;   // тени на крупных планах клешни/коннектора не нужны, а дороги
            // Подводный вид (туман DepthFog, мутность, полноэкранные эффекты) — это отдельный
            // рендерер URP «UnderwaterRenderer», выбранный у бортовой камеры. Номер рендерера
            // URP наружу не отдаёт — берём из её настроек и ставим рабочим камерам тот же.
            var поле = typeof(UniversalAdditionalCameraData).GetField("m_RendererIndex",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (поле != null) в.SetRenderer((int)поле.GetValue(из));
        }
        return cam;
    }

    void РаздатьРоли()
    {
        var купола = new System.Collections.Generic.List<Transform>();
        foreach (var t in GetComponentsInChildren<Transform>(false))
            if (t.name.StartsWith("Купол")) купола.Add(t);
        Transform метка = null;
        foreach (var t in GetComponentsInChildren<Transform>(true)) if (t.name == "МеткаКамеры") { метка = t; break; }

        Transform Ближайший(Transform цель)
        {
            Transform лучший = null;
            if (цель == null) return null;
            foreach (var k in купола)
                if (лучший == null || Vector3.Distance(k.position, цель.position) < Vector3.Distance(лучший.position, цель.position)) лучший = k;
            return лучший;
        }

        // Курсовая — самая верхняя.
        foreach (var k in купола)
            if (_местоКурс == null || transform.InverseTransformPoint(k.position).y > transform.InverseTransformPoint(_местоКурс.position).y) _местоКурс = k;
        if (_местоКурс != null) купола.Remove(_местоКурс); else _местоКурс = метка;

        _местоЗахват = Ближайший(_цельЗахват);
        if (_местоЗахват != null) купола.Remove(_местоЗахват);
        _местоКоннектор = Ближайший(_цельКоннектор);
        if (_местоЗахват == null) _местоЗахват = _местоКурс;
        if (_местоКоннектор == null) _местоКоннектор = _местоКурс;

        Debug.Log($"[Камеры] курсовая: {Имя(_местоКурс)}, захват: {Имя(_местоЗахват)}, коннектор: {Имя(_местоКоннектор)}. " +
                  "V — видео рабочих камер: авто / всегда / скрыто.");
    }

    static string Имя(Transform t) => t != null ? t.name : "—";

    void Update()
    {
        if (_курсовая == null) return;
        if (клавиша != Key.None && Keyboard.current != null && Keyboard.current[KeyBindings.K(клавиша)].wasPressedThisFrame)
            Переключить();

        ПоказатьЗахват = ВидеоВключено;
        ПоказатьКоннектор = ВидеоВключено && RovSystems.toolInstalled;
        // Камеры, чьё видео не показывается, не рендерят; показанные — раз в КадровНаОбновление
        // кадров, по очереди (в один кадр рисует не больше одной рабочей камеры).
        int k = Mathf.Max(1, КадровНаОбновление), f = Time.frameCount;
        if (_захват != null) _захват.enabled = ПоказатьЗахват && f % k == 0;
        if (_коннектор != null) _коннектор.enabled = ПоказатьКоннектор && (f + k / 2) % k == 0;
    }

    // Каждый кадр — после движения корпуса: камеры в точках куполов, направления по ролям.
    void LateUpdate()
    {
        if (_курсовая == null) return;
        Поставить(_курсовая, _местоКурс, Quaternion.AngleAxis(НаклонКурсовой, transform.right) * transform.forward);
        if (_захват != null && _захват.enabled && _цельЗахват != null)
        {
            // Середина клешни (между шарниром и кончиком) — обе челюсти в центре кадра.
            Vector3 центр = _шарнирЗахвата != null ? (_шарнирЗахвата.position + _цельЗахват.position) * 0.5f : _цельЗахват.position;
            ТриЧетверти(_захват, центр, центр + transform.forward * 0.03f, ЗахватДистанция_м, ЗахватВправо_м, ЗахватВверх_м, УголЗахват);
        }
        if (_коннектор != null && _коннектор.enabled) ПоставитьКоннектор();
    }

    // Коннектор: камера сбоку-сверху от своего купола, взгляд — на точку впереди головки
    // инструмента (видно головку и то, куда она входит).
    // Коннектор — ракурс «три четверти», как у камеры захвата: камера сзади-сбоку-сверху от
    // головки инструмента, со стороны центра аппарата (оттуда не мешает корпус инструмента),
    // взгляд — на головку. Видно объёмно: корпус, реактивный воротник, квадрат и что впереди.
    void ПоставитьКоннектор()
    {
        if (_цельКоннектор == null) return;
        ТриЧетверти(_коннектор, _цельКоннектор.position, _цельКоннектор.position + transform.forward * КоннекторВзглядВперёд_м,
                    КоннекторДистанция_м, КоннекторВправо_м, КоннекторВверх_м, УголКоннектор);
    }

    // Ракурс «три четверти» на рабочий прибор: камера позади-сбоку-сверху от точки прибора,
    // сбоку — со стороны центра аппарата (оттуда не заслоняет сам прибор), взгляд — на цель.
    // Всё в осях корпуса: прибор всегда в одном месте кадра, видно объёмно.
    void ТриЧетверти(Camera cam, Vector3 прибор, Vector3 цель, float назад, float кЦентру, float вверх, float угол)
    {
        float сторона = transform.InverseTransformPoint(прибор).x >= 0f ? -1f : 1f;
        Vector3 поз = прибор - transform.forward * назад + transform.right * (сторона * кЦентру) + transform.up * вверх;
        cam.transform.SetPositionAndRotation(поз, Quaternion.LookRotation((цель - поз).normalized, transform.up));
        cam.fieldOfView = угол;
    }

    Vector3 На(Transform откуда, Transform цель) =>
        откуда != null && цель != null ? (цель.position - откуда.position).normalized : transform.forward;

    void Поставить(Camera cam, Transform место, Vector3 взгляд)
    {
        if (место == null) return;
        cam.transform.SetPositionAndRotation(место.position + взгляд * ВыносИзКупола, Quaternion.LookRotation(взгляд, transform.up));
    }
}

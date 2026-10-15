// =============================================================================
// DopplerInstrumentsVisualizer.cs
// -----------------------------------------------------------------------------
// F3 — учебная анимация доплеровского лага (DVL) НА ДАННЫХ РАБОЧЕГО ДАТЧИКА.
//
// Два слоя, намеренно разных:
//   • АНИМАЦИЯ — показательная: звук замедлен (реальные 1500 м/с глазом не видны),
//     доплеровское сжатие/растяжение волны утрировано (УсилениеДоплера). Она
//     объясняет, КАК прибор приходит к числу.
//   • ВСЕ ЧИСЛА, ЛУЧИ И СТРЕЛКИ — настоящие: берутся из RovSpeedSensor, того
//     самого датчика, что кормит монитор и графики. Дальности лучей до дна,
//     скорости вдоль лучей (по ним окрашено эхо), решение «над дном/в воде»,
//     течение — ровно то, что намерил датчик, со всеми его ошибками.
//
// Цикл одного показательного «пинга»:
//   1. 4 луча посылают звук вниз;
//   2. первым приходит слабое эхо от взвеси в ячейке воды (ближе);
//   3. позже — сильное эхо от дна;
//   4. по тону каждого эха — скорость вдоль луча; 4 луча → вектор (DvlJanus).
// Эхо синее и гуще — луч сближается с отражателем, красное и реже — удаляется.
// Цветные стрелки у прибора — скорость вдоль каждого луча (сырое измерение).
//
// Клавиши: F3 — вкл/выкл, F6 — вид со стороны/с борта, F7 — пауза анимации,
// F8 — скорость анимации 1× / ½× / ¼×.
// (ADCP был на F4 — убран; отдельный режим «рабочий датчик» на F4 слит сюда.)
//
// Все объекты создаются в слое камеры монитора (Underwater), иначе их не видно.
// Компонент добавляется автоматически из RovController (Start).
// =============================================================================

using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
public class DopplerInstrumentsVisualizer : MonoBehaviour, IДемонстрацияПрибора
{
    // ---- учебный показ в окне «?» → «Работа приборов» (IДемонстрацияПрибора) ----
    public string Название => "Доплеровский лаг";
    public string Описание => "как прибор измеряет скорость над дном и течение (эффект Доплера, 4 луча)";
    public int Порядок => 10;
    public bool Показано => Показывать;
    public string Состояние => "скорость " +
                               (_множитель > 0.9f ? "1×" : _множитель > 0.4f ? "½×" : "¼×") + (_пауза ? " · пауза" : "");
    public void СменитьВид() { if (Показывать) ВидСбоку(!_видСбоку); }
    public void Пауза() => _пауза = !_пауза;
    public void СменитьСкорость() => _множитель = _множитель > 0.9f ? 0.5f : _множитель > 0.4f ? 0.25f : 1f;

    [Header("Клавиши")]
    public Key клавишаDVL = Key.F3;
    public Key клавишаВида = Key.F6;
    public Key клавишаПаузы = Key.F7;
    public Key клавишаСкорости = Key.F8;
    public bool Показывать = false;

    [Header("Анимация (показательная)")]
    [Tooltip("Во сколько раз утрировать доплеровское изменение длины волны на экране: " +
             "λ_эха = λ·(1 − Усиление·v_луча). Реальное 2v/c слишком мало, чтобы увидеть.")]
    public float УсилениеДоплера = 2.5f;
    [Tooltip("Длина волны звука на анимации, м")]
    public float ДлинаВолныВизуально = 0.35f;
    [Tooltip("Скорость звука НА АНИМАЦИИ, м/с. Реальная ~1500 м/с — глазом не увидеть.")]
    public float СкоростьЗвукаВизуально = 8f;
    [Tooltip("Пауза между показательными пингами, с")]
    public float ПаузаМеждуПингами_с = 1.2f;

    [Header("Вид")]
    [Tooltip("Автоматически переключать монитор на вид со стороны при включении")]
    public bool ВидСоСтороныАвто = true;
    [Tooltip("Размер шрифта панели статистики (мировые единицы на расстоянии 1 м)")]
    public float РазмерШрифтаПанели = 0.36f;
    [Tooltip("Масштаб кадра камеры наблюдения: 1 — все лучи, стрелки и подписи впритык (с запасом 12%), больше — дальше")]
    public float МасштабКадра = 1f;
    [Range(0.2f, 0.6f)]
    [Tooltip("Ширина колонки с текстом — доля ширины кадра (колонка по левому краю во всю высоту)")]
    public float ДоляШириныПанели = 0.36f;
    [Tooltip("Размер подписей у объектов (на расстоянии 1 м от камеры)")]
    public float РазмерПодписей = 0.3f;

    // --- собственные часы анимации (пауза/замедление) ---
    private float _время;
    private bool _пауза;
    private float _множитель = 1f;

    // --- ссылки ---
    private RovController _rov;
    private Rigidbody _rb;
    private int _слой;
    private Transform _корень;
    private Material _мат;
    private TMP_FontAsset _шрифт;

    // --- камеры ---
    private Camera _камМонитора, _камСбоку;
    private bool _видСбоку;

    // --- панель ---
    private TextMeshPro _панель;
    private Transform _подложка;
    private readonly List<(TextMeshPro текст, Vector3 где)> _метки = new List<(TextMeshPro, Vector3)>();
    private int _метокЗанято;
    private float _следТекст;   // панель обновляем 4 раза/с — иначе цифры не успеть прочитать

    // --- графика ---
    private readonly LineRenderer[] _луч = new LineRenderer[4];
    private readonly LineRenderer[] _импульс = new LineRenderer[4];
    private readonly LineRenderer[] _эхо = new LineRenderer[4];
    private readonly LineRenderer[] _эхоВода = new LineRenderer[4];   // слабое эхо от взвеси — приходит раньше дна
    private readonly LineRenderer[] _пятно = new LineRenderer[4];
    private readonly LineRenderer[] _проекция = new LineRenderer[4];
    private LineRenderer _стрелкаДно, _стрелкаВода, _стрелкаТечение, _кольцоВоды;

    // --- показательный пинг: геометрия лучей берётся у датчика в момент старта ---
    private float _стартПинга = -999f;
    private readonly float[] _дальность = new float[4];
    private readonly bool[] _захват = new bool[4];
    private float _дальностьПинга;

    void Awake()
    {
        _rov = GetComponent<RovController>();
        _rb = GetComponent<Rigidbody>();
    }

    void OnDisable() { ВидСбоку(false); }
    void OnDestroy() { ВидСбоку(false); if (_корень != null) Destroy(_корень.gameObject); }

    // ======================================================== управление
    void Update()
    {
        var kb = Keyboard.current;
        if (kb != null)
        {
            if (клавишаDVL != Key.None && kb[KeyBindings.K(клавишаDVL)].wasPressedThisFrame) Переключить();
            if (клавишаВида != Key.None && kb[KeyBindings.K(клавишаВида)].wasPressedThisFrame && Показывать) ВидСбоку(!_видСбоку);
            if (клавишаПаузы != Key.None && kb[KeyBindings.K(клавишаПаузы)].wasPressedThisFrame) _пауза = !_пауза;
            if (клавишаСкорости != Key.None && kb[KeyBindings.K(клавишаСкорости)].wasPressedThisFrame)
                _множитель = _множитель > 0.9f ? 0.5f : _множитель > 0.4f ? 0.25f : 1f;
        }
        if (!Показывать) return;
        _время += _пауза ? 0f : Time.deltaTime * _множитель;

        if (_корень == null) Построить();
        Кадр();
    }

    public void Переключить()
    {
        Показывать = !Показывать;
        if (_корень == null && Показывать) Построить();
        if (_корень != null) _корень.gameObject.SetActive(Показывать);
        _стартПинга = -999f;
        _кадрГотов = false;
        _следТекст = 0f;
        ВидСбоку(Показывать && ВидСоСтороныАвто);
        Debug.Log($"[DVL] анимация {(Показывать ? "ВКЛ" : "ВЫКЛ")}. F6 — вид, F7 — пауза, F8 — медленнее.");
    }

    // ================================================================== кадр
    void Кадр()
    {
        var д = RovSpeedSensor.Instance;
        if (д == null)
        {
            СкрытьГрафику();
            ПоказатьПанель("<b>Датчик скорости не найден</b>\n<color=#aab6bf>На аппарате нет компонента RovSpeedSensor.</color>");
            return;
        }

        float c = Mathf.Max(1f, СкоростьЗвукаВизуально);
        float θ = д.УголЛуча;
        float W = д.ЯчейкаВоды_м;
        Transform тр = д.transform;
        Vector3 o = тр.position;

        // Новый показательный пинг: снимок дальностей лучей у датчика.
        float t = _время - _стартПинга;
        if (t > 2f * Mathf.Max(_дальностьПинга, W) / c + ПаузаМеждуПингами_с)
        {
            _стартПинга = _время; t = 0f;
            _дальностьПинга = 0f;
            for (int i = 0; i < 4; i++)
            {
                _захват[i] = д.ЭхоОтДна[i];
                _дальность[i] = д.Дальность_м[i];
                _дальностьПинга = Mathf.Max(_дальностьПинга, _дальность[i]);
            }
        }

        float r = c * t;   // где сейчас фронт импульса
        for (int i = 0; i < 4; i++)
        {
            Vector3 d = тр.TransformDirection(DvlJanus.Луч(i, θ, true));
            float R = _дальность[i];
            Линия(_луч[i], o, o + d * R, new Color(0.35f, 0.65f, 1f, _захват[i] ? 0.35f : 0.15f));

            // Скорости вдоль лучей — ИЗ ДАТЧИКА (последнее измерение): ими окрашено эхо.
            float vrДно = д.ЭхоОтДна[i] ? д.РадиальнаяДно[i] : 0f;
            float vrВода = д.РадиальнаяВода[i];

            // 1. Импульс вниз.
            if (r < R) Волна(_импульс[i], o, d, r, ДлинаВолныВизуально, new Color(0.75f, 0.9f, 1f, 1f));
            else _импульс[i].enabled = false;

            // 2. Слабое эхо от воды — приходит первым (ячейка ближе дна).
            float rВода = 2f * W - r;
            if (r >= W && rВода > 0f)
            {
                var слойВ = _rov != null && _rov.Течение != null ? _rov.Течение.СлойВТочке(o + d * W) : null;
                Color цвВ = Color.Lerp(слойВ != null ? слойВ.Цвет : new Color(0.4f, 1f, 0.8f), ЦветСдвига(vrВода), 0.5f);
                цвВ.a = 0.7f;
                Волна(_эхоВода[i], o, d, rВода, ДлинаЭха(vrВода), цвВ);
            }
            else _эхоВода[i].enabled = false;

            // 3. Сильное эхо от дна — приходит позже.
            float rЭхо = 2f * R - r;
            if (_захват[i] && r >= R && rЭхо > 0f) Волна(_эхо[i], o, d, rЭхо, ДлинаЭха(vrДно), ЦветСдвига(vrДно));
            else _эхо[i].enabled = false;

            // Пятно на дне — вспыхивает в момент отражения и тает.
            if (_захват[i] && r >= R)
            {
                float возраст = (r - R) / c;
                Окружность(_пятно[i], o + d * R, Vector3.up, 0.25f + возраст * 0.8f, new Color(0.6f, 0.85f, 1f, Mathf.Clamp01(1f - возраст / 1.2f)));
            }
            else _пятно[i].enabled = false;

            // Стрелка вдоль луча у прибора — что намерил этот луч.
            Стрелка(_проекция[i], o + d * 1.2f, d * vrДно * 8f, ЦветСдвига(vrДно));
        }

        // Кольцо ячейки воды — вспыхивает, когда приходит её эхо.
        {
            Vector3 центр = o + Vector3.down * W * Mathf.Cos(θ * Mathf.Deg2Rad);
            var слой = _rov != null && _rov.Течение != null ? _rov.Течение.СлойВТочке(центр) : null;
            Color цв = слой != null ? слой.Цвет : new Color(0.4f, 1f, 0.8f);
            float tВода = 2f * W / c;
            float вспышка = t >= tВода ? Mathf.Clamp01(1f - (t - tВода)) : 0f;
            цв.a = 0.25f + 0.75f * вспышка;
            Окружность(_кольцоВоды, центр, Vector3.up, W * Mathf.Tan(θ * Mathf.Deg2Rad) * 1.05f, цв);
        }

        // Итоговые стрелки — усреднённые показания датчика (как на пульте).
        Vector3 верх = o + Vector3.up * 0.8f;
        Стрелка(_стрелкаДно, верх, д.ЗахватДна ? тр.TransformDirection(д.СкоростьНадДномСредн) * 6f : Vector3.zero, new Color(1f, 1f, 1f, 0.95f));
        Стрелка(_стрелкаВода, верх + Vector3.up * 0.25f, д.ЕстьВода ? тр.TransformDirection(д.СкоростьВВодеСредн) * 6f : Vector3.zero, new Color(0.45f, 0.75f, 1f, 0.95f));
        // Течение датчик отдаёт в земных осях (x — восток, z — север) — рисуем в мире как есть.
        Стрелка(_стрелкаТечение, верх + Vector3.up * 0.5f, д.ЗахватДна && д.ЕстьВода && д.ТечениеНадёжно ? д.ТечениеЗемля * 6f : Vector3.zero, new Color(0.4f, 1f, 0.55f, 0.95f));

        // Подписи у объектов сцены.
        НачатьМетки();
        int лучДна = -1;
        for (int i = 0; i < 4; i++) if (_захват[i]) { лучДна = i; break; }
        Vector3 dДна = лучДна >= 0 ? тр.TransformDirection(DvlJanus.Луч(лучДна, θ, true)) : Vector3.down;
        if (лучДна >= 0 && r >= _дальность[лучДна])
            Метка(o + dДна * _дальность[лучДна] + Vector3.up * 0.4f, "отражение от дна", new Color(0.7f, 0.88f, 1f));
        else if (r < _дальностьПинга)
            Метка(o + Vector3.down * r * Mathf.Cos(θ * Mathf.Deg2Rad), "импульс", new Color(0.7f, 0.88f, 1f));
        Метка(o + Vector3.down * W * Mathf.Cos(θ * Mathf.Deg2Rad), "эхо от воды (" + W.ToString("F0") + " м)", new Color(0.55f, 0.8f, 1f));
        Метка(верх + Vector3.up * 0.9f, "скорость и течение", Color.white);
        ЗакончитьМетки();

        if (Time.unscaledTime >= _следТекст)
        {
            _следТекст = Time.unscaledTime + 0.25f;
            Панель(д, t, c, W);
        }
    }

    void СкрытьГрафику()
    {
        if (_корень == null) return;
        for (int i = 0; i < 4; i++)
            _луч[i].enabled = _импульс[i].enabled = _эхо[i].enabled = _эхоВода[i].enabled = _пятно[i].enabled = _проекция[i].enabled = false;
        _кольцоВоды.enabled = _стрелкаДно.enabled = _стрелкаВода.enabled = _стрелкаТечение.enabled = false;
    }

    // Текущий шаг просто загорается жёлтым, остальные — тускло.
    static void Шаги(StringBuilder sb, int активный, params string[] шаги)
    {
        for (int i = 0; i < шаги.Length; i++)
            sb.Append(i == активный ? "<color=#ffd75e>" : "<color=#aab6bf>").Append(i + 1).Append(". ").Append(шаги[i]).Append("</color>\n");
    }

    void Панель(RovSpeedSensor д, float t, float c, float W)
    {
        // Порядок событий показательного пинга: звук вниз → эхо воды → эхо дна → расчёт.
        float Wt = W / c, Rt = _дальностьПинга / c;
        int шаг = (t >= Wt && t < 2f * Wt) ? 1 : t < Rt ? 0 : t < 2f * Rt ? 2 : 3;

        var sb = new StringBuilder(768);
        sb.Append("<size=120%><b>Скорость аппарата над дном</b></size>\n");
        sb.Append("<color=#aab6bf>прибор: доплеровский лаг (DVL — Doppler Velocity Log)</color>\n\n");
        Шаги(sb, шаг, "Посылаем звук вниз",
            "Первым приходит слабое эхо от воды (" + W.ToString("F0") + " м)",
            "Позже — сильное эхо от дна",
            "По тону каждого эха считаем скорость");

        int сЭхом = 0; for (int i = 0; i < 4; i++) if (д.ЭхоОтДна[i]) сЭхом++;
        sb.Append("\nЛучей видят дно: ").Append(сЭхом).Append(" из 4 · высота ").Append(д.Высота_м.ToString("F1")).Append(" м\n");
        sb.Append("Измерений в секунду: ").Append(д.ТекущаяЧастота_Гц.ToString("F0"))
          .Append(д.АдаптивнаяЧастота ? " (чем выше над дном — тем реже)" : "").Append("\n");

        sb.Append("\n<b>Результат</b> <color=#aab6bf>(рабочий датчик)</color>\n");
        if (д.ЗахватДна) sb.Append("Над дном: ").Append(Направление(д.СкоростьНадДномСредн)).Append("\n");
        else sb.Append("<color=#f77>Дно потеряно — на мониторе застыло последнее значение</color>\n");
        sb.Append("<color=#8cf>В воде: ").Append(д.ЕстьВода ? Направление(д.СкоростьВВодеСредн) : "—").Append("</color>\n");
        sb.Append("<color=#6f8>Течение: ");
        if (д.ЗахватДна && д.ЕстьВода)
        {
            float модуль = new Vector2(д.ТечениеЗемля.x, д.ТечениеЗемля.z).magnitude;
            if (д.ТечениеНадёжно)
                sb.Append(модуль.ToString("F2")).Append(" м/с, курс ").Append(д.КурсТечения.ToString("F0")).Append("°");
            else
                sb.Append("слабое (").Append(модуль.ToString("F2")).Append(" м/с) — направление не определить");
        }
        else sb.Append("—");
        sb.Append("</color>\n");
        sb.Append("На мониторе «Скорость»: ").Append(RovSystems.speed_mps.ToString("F2")).Append(" м/с (")
          .Append((RovSystems.speed_mps * 1.9438f).ToString("F2")).Append(" уз)\n");

        // Для проверки: что на самом деле делает вода у аппарата (истина из движка).
        if (_rov != null && _rov.Течение != null)
        {
            Vector3 vИст = _rov.Течение.Скорость(д.transform.position + Vector3.down * W);
            Vector3 hИст = new Vector3(vИст.x, 0f, vИст.z);
            sb.Append("<color=#aab6bf>Истинное течение (проверка): ");
            if (!_rov.Течение.включено) sb.Append("выключено (клавиша O)");
            else sb.Append(hИст.magnitude.ToString("F2")).Append(" м/с, курс ").Append(DvlJanus.Курс(hИст).ToString("F0")).Append("°");
            sb.Append("</color>\n");
        }

        sb.Append("\n<color=#aab6bf>Стрелки: белая — над дном, <color=#8cf>синяя</color> — в воде, <color=#6f8>зелёная</color> — течение\n");
        sb.Append("Эхо <color=#8cf>синее</color> — сближаемся, <color=#f96>красное</color> — удаляемся\n");
        sb.Append("Звук замедлен для наглядности, числа — настоящие</color>\n");
        if (_пауза) sb.Append("<color=#ffd75e>пауза</color>");
        ПоказатьПанель(sb.ToString());
    }

    static string Направление(Vector3 vЛок)
    {
        var части = new List<string>();
        if (Mathf.Abs(vЛок.z) >= 0.005f) части.Add(Mathf.Abs(vЛок.z).ToString("F2") + " м/с " + (vЛок.z > 0f ? "вперёд" : "назад"));
        if (Mathf.Abs(vЛок.x) >= 0.005f) части.Add(Mathf.Abs(vЛок.x).ToString("F2") + " м/с " + (vЛок.x > 0f ? "вправо" : "влево"));
        if (Mathf.Abs(vЛок.y) >= 0.05f)  части.Add(Mathf.Abs(vЛок.y).ToString("F2") + " м/с " + (vЛок.y > 0f ? "вверх" : "вниз"));
        return части.Count == 0 ? "стоим" : string.Join(", ", части);
    }

    // =========================================================== камера и панель
    void ВидСбоку(bool вкл)
    {
        if (_камМонитора == null) _камМонитора = RovCameraRig.Бортовая();
        if (_камМонитора == null) { _видСбоку = false; return; }

        if (вкл && _камСбоку == null)
        {
            var go = new GameObject("Камера наблюдения (доплер)");
            _камСбоку = go.AddComponent<Camera>();
            _камСбоку.CopyFrom(_камМонитора);   // та же текстура монитора, маска слоёв, FOV
            go.transform.SetPositionAndRotation(_камМонитора.transform.position, _камМонитора.transform.rotation);
            _камСбоку.enabled = false;
            if (_корень != null) go.transform.SetParent(_корень, true);
        }
        _видСбоку = вкл;
        if (_камСбоку != null) _камСбоку.enabled = вкл;
        _камМонитора.enabled = !вкл;
    }

    void LateUpdate()
    {
        if (!Показывать) return;

        if (_видСбоку && _камСбоку != null) ВестиКамеру();

        // Панель — «на стекле» активной камеры, колонка по левому краю во всю высоту.
        // Висит вплотную к ближней плоскости отсечения: иначе её перекрывают объекты
        // сцены, оказавшиеся между камерой и панелью (труба, скала).
        var кам = _видСбоку && _камСбоку != null ? _камСбоку : _камМонитора;
        if (_панель != null && кам != null)
        {
            // Вся раскладка считается «как на расстоянии 1 м», а сам объект панели
            // уменьшен в дист раз — выглядит точно так же, но ближе к камере.
            float дист = кам.nearClipPlane + 0.02f;
            float полВыс = Mathf.Tan(кам.fieldOfView * 0.5f * Mathf.Deg2Rad);   // на 1 м
            float полШир = полВыс * Mathf.Max(0.5f, кам.aspect);
            var tr = _панель.transform;
            tr.localScale = Vector3.one * дист;
            tr.position = кам.transform.position + (кам.transform.forward
                        - кам.transform.right * полШир + кам.transform.up * полВыс) * дист;   // левый верхний угол кадра
            tr.rotation = кам.transform.rotation;
            _панель.fontSizeMax = РазмерШрифтаПанели;
            _панель.fontSizeMin = РазмерШрифтаПанели * 0.4f;
            float ширина = полШир * 2f * ДоляШириныПанели, высота = полВыс * 2f;
            _панель.rectTransform.sizeDelta = new Vector2(ширина, высота);
            float поле = РазмерШрифтаПанели * 0.06f;
            _панель.margin = new Vector4(поле, поле, поле, поле);
            if (_подложка != null)
            {
                _подложка.localPosition = new Vector3(ширина * 0.5f, -высота * 0.5f, 0.002f);
                _подложка.localScale = new Vector3(ширина, высота, 1f);
            }

            // Подписи: смотрят в камеру, размер растёт с расстоянием — на экране постоянный.
            for (int i = 0; i < _метки.Count; i++)
            {
                var (tm, где) = _метки[i];
                if (!tm.gameObject.activeSelf) continue;
                float d = Vector3.Distance(кам.transform.position, где);
                tm.transform.position = где + кам.transform.right * d * 0.02f;
                tm.transform.rotation = кам.transform.rotation;
                tm.fontSize = РазмерПодписей * d;
            }
        }
    }

    // ------------------------------------------------ камера наблюдения (F6)
    private readonly List<Vector3> _точкиКадра = new List<Vector3>();
    private int _ракурс;   // индекс выбранного ракурса облёта (липкий — не прыгает без причины)
    // Сглаженные параметры кадра: центр, расстояние, направление «от центра к камере».
    private Vector3 _C, _назад;
    private float _D;
    private bool _кадрГотов;
    static readonly float[] Ракурсы = { 0f, 50f, -50f, 110f, -110f, 180f };   // сдвиг азимута от «сзади-справа», град

    // Точки, которые обязаны попасть в кадр: концы всех видимых лучей и стрелок, подписи.
    void СобратьТочкиКадра(Camera кам)
    {
        _точкиКадра.Clear();
        _точкиКадра.Add(transform.position);
        void Линия(LineRenderer lr)
        {
            if (lr == null || !lr.enabled || lr.positionCount == 0) return;
            _точкиКадра.Add(lr.GetPosition(0));
            _точкиКадра.Add(lr.GetPosition(lr.positionCount - 1));
        }
        for (int i = 0; i < 4; i++) { Линия(_луч[i]); Линия(_проекция[i]); }
        Линия(_стрелкаДно); Линия(_стрелкаВода); Линия(_стрелкаТечение);
        // Бегающие и мигающие подписи («импульс», «отражение») в расчёт НЕ берём —
        // из-за них кадр «дышал». Учитываем место под подпись над стрелками.
        float d0 = _D > 0f ? _D : 10f;
        Vector3 подпись = transform.position + Vector3.up * 1.7f;
        _точкиКадра.Add(подпись + Vector3.up * РазмерПодписей * d0 * 0.12f);
        _точкиКадра.Add(подпись + кам.transform.right * (18f * РазмерПодписей * d0 * 0.06f));
    }

    void ВестиКамеру()
    {
        var кам = _камСбоку;
        СобратьТочкиКадра(кам);

        // Центр — середина габаритов всех точек.
        Vector3 мин = _точкиКадра[0], макс = _точкиКадра[0];
        foreach (var т0 in _точкиКадра) { мин = Vector3.Min(мин, т0); макс = Vector3.Max(макс, т0); }
        Vector3 C = (мин + макс) * 0.5f;

        Vector3 вперёдАп = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
        if (вперёдАп.sqrMagnitude < 1e-4f) вперёдАп = Vector3.forward;
        Vector3 базовый = (-вперёдАп * 0.6f + Vector3.Cross(Vector3.up, вперёдАп) * 0.8f).normalized;   // сзади-справа
        float наклон = 4f * Mathf.Deg2Rad;

        float tanV = Mathf.Tan(кам.fieldOfView * 0.5f * Mathf.Deg2Rad);
        float tanH = tanV * Mathf.Max(0.5f, кам.aspect);
        float p = ДоляШириныПанели;          // доля кадра под панелью
        const float запас = 0.88f;           // точки не ближе 12% к краю

        // Облёт: если дрон заслонён (труба, скала), пробуем другие ракурсы. Текущий держим, пока он чист.
        Vector3 поз = Vector3.zero, назад = Vector3.zero, правоКам = Vector3.zero; float D = 0f;
        for (int попытка = 0; попытка < Ракурсы.Length; попытка++)
        {
            int idx = (_ракурс + попытка) % Ракурсы.Length;
            Vector3 откуда = Quaternion.AngleAxis(Ракурсы[idx], Vector3.up) * базовый;
            назад = (откуда * Mathf.Cos(наклон) + Vector3.up * Mathf.Sin(наклон)).normalized;
            Vector3 fwd = -назад;
            правоКам = Vector3.Cross(Vector3.up, fwd).normalized;
            Vector3 верхКам = Vector3.Cross(fwd, правоКам);

            // Расстояние, при котором каждая точка влезает: по вертикали — в весь кадр,
            // по горизонтали — в свободную часть (центр в ndc = p, полуширина 1 − p).
            D = 4f;
            foreach (var т in _точкиКадра)
            {
                Vector3 r = т - C;
                float x = Vector3.Dot(r, правоКам), y = Vector3.Dot(r, верхКам), z = Vector3.Dot(r, fwd);
                D = Mathf.Max(D, Mathf.Abs(y) / (tanV * запас) - z);
                D = Mathf.Max(D, Mathf.Abs(x) / (tanH * (1f - p) * запас) - z);
            }
            D *= МасштабКадра;
            поз = C + назад * D;

            Vector3 кКамере = поз - transform.position;
            bool заслонено = DvlJanus.ЛучДоДна(transform, _rb, transform.position, кКамере.normalized, кКамере.magnitude, out _);
            if (!заслонено) { _ракурс = idx; break; }
        }

        // Сглаживание: отъезжаем быстро (чтобы ничего не обрезать), подъезжаем
        // медленно и не реагируем на мелочь (±4%) — кадр не «дышит» на каждом пинге.
        float dt = Time.deltaTime;
        if (!_кадрГотов) { _C = C; _D = D; _назад = назад; _кадрГотов = true; }
        _C = Vector3.Lerp(_C, C, 1f - Mathf.Exp(-2.5f * dt));
        float отн = (D - _D) / Mathf.Max(0.1f, _D);
        float темп = отн > 0f ? 3f : (отн < -0.04f ? 0.6f : 0f);
        _D = Mathf.Lerp(_D, D, 1f - Mathf.Exp(-темп * dt));
        _назад = Vector3.Slerp(_назад, назад, 1f - Mathf.Exp(-1.5f * dt)).normalized;

        // Ставим камеру прямо из сглаженных параметров. Смотрим левее центра:
        // он встаёт в середину свободной (правой) части кадра.
        Vector3 правоС = Vector3.Cross(Vector3.up, -_назад).normalized;
        Vector3 позС = _C + _назад * _D;
        Vector3 точкаВзгляда = _C - правоС * (_D * tanH * p);
        кам.transform.SetPositionAndRotation(позС, Quaternion.LookRotation(точкаВзгляда - позС, Vector3.up));
    }

    void ПоказатьПанель(string текст)
    {
        if (_панель == null) return;
        _панель.text = текст;

    }

    void НачатьМетки() { _метокЗанято = 0; }

    void Метка(Vector3 где, string текст, Color цвет)
    {
        if (_метокЗанято >= _метки.Count)
        {
            var go = new GameObject("Подпись");
            go.layer = _слой;
            go.transform.SetParent(_корень, false);
            var tm = go.AddComponent<TextMeshPro>();
            if (_шрифт != null) tm.font = _шрифт;
            tm.alignment = TextAlignmentOptions.Left;
            tm.textWrappingMode = TextWrappingModes.NoWrap;
            tm.rectTransform.pivot = new Vector2(0f, 0.5f);
            tm.fontStyle = FontStyles.Bold;
            ПоверхСцены(tm.fontMaterial, 4000);   // подписи не прячутся за рельефом и трубой
            _метки.Add((tm, где));
        }
        var м = _метки[_метокЗанято];
        м.текст.gameObject.SetActive(true);
        м.текст.text = текст;
        м.текст.color = цвет;
        _метки[_метокЗанято] = (м.текст, где);
        _метокЗанято++;
    }

    void ЗакончитьМетки()
    {
        for (int i = _метокЗанято; i < _метки.Count; i++) _метки[i].текст.gameObject.SetActive(false);
    }

    // ============================================================ построение
    void Построить()
    {
        // Слой камеры монитора: объекты вне её маски на мониторе не видны.
        _слой = LayerMask.NameToLayer("Underwater");
        if (_слой < 0) _слой = 0;
        var c = RovCameraRig.Бортовая();
        if (c != null && (c.cullingMask & (1 << _слой)) == 0)
            for (int b = 0; b < 32; b++) if ((c.cullingMask & (1 << b)) != 0) { _слой = b; break; }

        // Шрифт — тот же, что на мониторах пульта (с кириллицей), если найдётся.
        foreach (var t in FindObjectsByType<TMP_Text>(FindObjectsSortMode.None))
            if (t.font != null) { _шрифт = t.font; if (t.font.name.Contains("ofont")) break; }

        _корень = new GameObject("Доплеровский лаг (F3)").transform;
        _корень.gameObject.layer = _слой;
        _мат = new Material(Shader.Find("Sprites/Default")) { name = "DopplerViz (auto)" };
        for (int i = 0; i < 4; i++)
        {
            _луч[i] = НоваяЛиния("Луч " + (i + 1), 0.02f);
            _импульс[i] = НоваяЛиния("Импульс " + (i + 1), 0.07f);
            _эхо[i] = НоваяЛиния("Эхо " + (i + 1), 0.06f);
            _пятно[i] = НоваяЛиния("Пятно на дне " + (i + 1), 0.03f);
            _проекция[i] = НоваяЛиния("Скорость вдоль луча " + (i + 1), 0.045f);
            _импульс[i].widthMultiplier = 0.04f;
            _эхо[i].widthMultiplier = 0.04f;
            _эхоВода[i] = НоваяЛиния("Эхо от воды " + (i + 1), 0.022f);
        }
        _кольцоВоды = НоваяЛиния("Ячейка воды", 0.03f);
        _стрелкаДно = НоваяЛиния("Скорость над дном", 0.05f);
        _стрелкаВода = НоваяЛиния("Скорость отн. воды", 0.05f);
        _стрелкаТечение = НоваяЛиния("Течение", 0.05f);

        var go = new GameObject("Панель прибора");
        go.layer = _слой;
        go.transform.SetParent(_корень, false);
        _панель = go.AddComponent<TextMeshPro>();
        if (_шрифт != null) _панель.font = _шрифт;
        _панель.alignment = TextAlignmentOptions.TopLeft;
        _панель.rectTransform.pivot = new Vector2(0f, 1f);
        _панель.textWrappingMode = TextWrappingModes.Normal;
        _панель.color = Color.white;
        _панель.lineSpacing = 8f;
        _панель.enableAutoSizing = true;
        // Полупрозрачное Unity сортирует по расстоянию до центра объекта — центр
        // подложки ближе к камере, и она рисовалась ПОВЕРХ текста, притеняя его.
        // Задаём порядок явно: подложка 0, текст 10.
        _панель.GetComponent<MeshRenderer>().sortingOrder = 10;
        ПоверхСцены(_панель.fontMaterial, 4002);   // панель — поверх всего, в т.ч. подписей

        var фон = new GameObject("Подложка панели");
        фон.layer = _слой;
        фон.transform.SetParent(go.transform, false);
        var mf = фон.AddComponent<MeshFilter>();
        mf.sharedMesh = КвадратМеш();
        var mr = фон.AddComponent<MeshRenderer>();
        mr.sharedMaterial = new Material(_мат) { name = "DopplerPanelBg (auto)" };
        mr.sharedMaterial.color = new Color(0.02f, 0.05f, 0.08f, 0.93f);
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.sortingOrder = 0;
        ПоверхСцены(mr.sharedMaterial, 4001);
        mr.receiveShadows = false;
        _подложка = фон.transform;
    }

    // Рисовать материал поверх геометрии сцены: без проверки глубины и позже неё
    // (очередь Overlay 4000+). TMP-шейдеры берут ZTest из unity_GUIZTestMode —
    // значение, заданное в материале, перекрывает глобальное. Порядок между
    // подписями (4000), подложкой панели (4001) и текстом панели (4002) задаёт очередь.
    static void ПоверхСцены(Material m, int очередь)
    {
        if (m == null) return;
        m.SetInt("unity_GUIZTestMode", (int)UnityEngine.Rendering.CompareFunction.Always);
        m.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
        m.renderQueue = очередь;
    }

    // Единичный квадрат с центром в нуле (плоскость XY) — подложка панели.
    static Mesh КвадратМеш()
    {
        var m = new Mesh { name = "Quad (auto)" };
        m.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f) };
        m.uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up };
        m.triangles = new[] { 0, 2, 1, 0, 3, 2 };
        m.RecalculateNormals();
        return m;
    }

    LineRenderer НоваяЛиния(string имя, float толщина)
    {
        var go = new GameObject(имя);
        go.layer = _слой;
        go.transform.SetParent(_корень, false);
        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.sharedMaterial = _мат;
        lr.widthMultiplier = толщина;
        lr.numCapVertices = 2;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        lr.enabled = false;
        return lr;
    }

    static void Линия(LineRenderer lr, Vector3 a, Vector3 b, Color c)
    {
        lr.enabled = true; lr.positionCount = 2;
        lr.SetPosition(0, a); lr.SetPosition(1, b);
        lr.startColor = lr.endColor = c;
    }

    // Волновой пакет на луче вокруг дальности r: синусоида поперёк луча под
    // огибающей Ханна (плавно появляется и гаснет по краям). Плоскость колебаний
    // повёрнута к активной камере — волну видно при любом ракурсе.
    void Волна(LineRenderer lr, Vector3 o, Vector3 d, float r, float λ, Color c)
    {
        const int N = 64;
        float L = 5f * ДлинаВолныВизуально;              // длина пакета — ~5 исходных волн
        float A = ДлинаВолныВизуально * 0.35f;           // амплитуда
        var кам = _видСбоку && _камСбоку != null ? _камСбоку : _камМонитора;
        Vector3 взгляд = кам != null ? кам.transform.forward : Vector3.forward;
        Vector3 поперёк = Vector3.Cross(d, взгляд);
        if (поперёк.sqrMagnitude < 1e-4f) поперёк = Vector3.Cross(d, Vector3.right);
        поперёк.Normalize();
        lr.enabled = true;
        lr.positionCount = N;
        for (int j = 0; j < N; j++)
        {
            float u = j / (N - 1f);                      // 0..1 вдоль пакета
            float s = (u - 0.5f) * L;
            float огиб = 0.5f - 0.5f * Mathf.Cos(2f * Mathf.PI * u);
            float y = Mathf.Sin(2f * Mathf.PI * s / Mathf.Max(0.02f, λ)) * A * огиб;
            lr.SetPosition(j, o + d * Mathf.Max(0f, r + s) + поперёк * y);
        }
        lr.startColor = lr.endColor = c;
    }

    // Длина волны эха: сближение (v > 0) — короче (тон выше), удаление — длиннее.
    float ДлинаЭха(float vr) => ДлинаВолныВизуально * Mathf.Clamp(1f - УсилениеДоплера * vr, 0.45f, 1.7f);

    // Цвет сдвига: сближение — синее, удаление — краснее, ноль — белый.
    static Color ЦветСдвига(float vr)
    {
        float k = Mathf.Clamp01(Mathf.Abs(vr) / 0.25f);
        Color к = vr >= 0f ? new Color(0.35f, 0.55f, 1f) : new Color(1f, 0.45f, 0.25f);
        Color c = Color.Lerp(Color.white, к, k); c.a = 1f;
        return c;
    }

    static void Окружность(LineRenderer lr, Vector3 центр, Vector3 нормаль, float радиус, Color c)
    {
        const int N = 24;
        lr.enabled = true; lr.positionCount = N + 1;
        Vector3 a = Vector3.Cross(нормаль, Mathf.Abs(нормаль.y) > 0.9f ? Vector3.right : Vector3.up).normalized;
        Vector3 b = Vector3.Cross(нормаль, a);
        for (int i = 0; i <= N; i++)
        {
            float ф = i * Mathf.PI * 2f / N;
            lr.SetPosition(i, центр + (a * Mathf.Cos(ф) + b * Mathf.Sin(ф)) * радиус);
        }
        lr.startColor = lr.endColor = c;
    }

    // Стрелка: основание → остриё → крыло → остриё → крыло.
    static void Стрелка(LineRenderer lr, Vector3 p, Vector3 вектор, Color c)
    {
        if (вектор.sqrMagnitude < 1e-4f) { lr.enabled = false; return; }
        Vector3 конец = p + вектор;
        Vector3 dir = вектор.normalized;
        Vector3 бок = Vector3.Cross(Mathf.Abs(dir.y) > 0.9f ? Vector3.right : Vector3.up, dir).normalized;
        float к = Mathf.Clamp(вектор.magnitude * 0.25f, 0.12f, 0.5f);
        lr.enabled = true; lr.positionCount = 5;
        lr.SetPosition(0, p); lr.SetPosition(1, конец);
        lr.SetPosition(2, конец - dir * к + бок * к * 0.6f); lr.SetPosition(3, конец);
        lr.SetPosition(4, конец - dir * к - бок * к * 0.6f);
        lr.startColor = lr.endColor = c;
    }
}

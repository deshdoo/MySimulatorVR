using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.Events;

// Сборщик показателей миссии для разбора (дебрифинга) после её завершения.
//
// Метрики подобраны по практике реальных обследований трубопроводов ТНПА:
//   • KP (kilometre point) — положение вдоль трассы, км. Каждая аномалия в
//     журнале инспекции привязывается к KP, а не к «где-то на трубе»;
//   • покрытие трассы — какая доля длины трубы реально попала в осмотр
//     (аппарат рядом + свет включён). Непросмотренный участок = брак съёмки;
//   • скорость осмотра — при визуальном осмотре держат ~0.5 уз и ниже, быстрее
//     видео смазывается и дефект можно пропустить;
//   • дистанция до трубы (stand-off) — для видео обычно 1–2 м;
//   • высота над дном — ниже ~0.5 м аппарат взмучивает ил и слепнет;
//   • плюс безопасность (удары, повреждения), энергия, кабель, устойчивость.
// Всё копится во время миссии из RovSystems и событий (удар, обнаружение
// дефекта), по завершении замораживается — окно MissionDebriefPanel читает числа.
//
// Оценка — прозрачная 100-балльная сумма критериев (веса в инспекторе), чтобы
// на защите можно было показать формулу, а не «чёрный ящик».
//
// Ставится сам из MissionBootstrap; вручную — на любой объект в сцене Main.
[DisallowMultipleComponent]
public class MissionStats : MonoBehaviour
{
    public static MissionStats Instance { get; private set; }

    [Tooltip("Корпус НПА — по нему считается путь и дистанция до трубы. Пусто — объект с RovController.")]
    public Transform НПА;

    [Header("Нормативы (практика осмотра трубопроводов)")]
    [Tooltip("Норматив времени миссии, с: уложился — полный балл, 2× норматива — ноль.")]
    public float НормативВремени_с = 480f;
    [Tooltip("Рекомендуемая максимальная скорость при осмотре, уз.")]
    public float МаксСкоростьОсмотра_уз = 0.5f;
    [Tooltip("Рекомендуемая дистанция до трубы при осмотре, м (верхняя граница).")]
    public float МаксДистанцияОсмотра_м = 2.5f;
    [Tooltip("До какой дистанции от трубы аппарат считается «у трубы», м. 0 — дальность сканера осмотра.")]
    public float ЗонаОсмотра_м = 0f;
    [Tooltip("Ниже этой высоты над дном — взмучивание грунта, м.")]
    public float МинБезопаснаяВысота_м = 0.5f;
    [Tooltip("Допустимый расход заряда АКБ за миссию, %: уложился — полный балл, 3× — ноль.")]
    public float НормаРасходаЗаряда_проц = 10f;
    [Tooltip("СКО наклона, град, при котором пилотирование идеально / балл = 0.")]
    public float СКОНаклонаОтлично_град = 3f;
    public float СКОНаклонаПлохо_град = 15f;
    [Tooltip("Штраф за каждый удар и за каждый уровень повреждения (баллов из «Безопасности»).")]
    public float ШтрафЗаУдар = 4f;
    public float ШтрафЗаПовреждение = 8f;

    [Header("Веса критериев (сумма = 100)")]
    public float ВесВыполнение = 30f;
    public float ВесКачествоОсмотра = 20f;
    public float ВесБезопасность = 25f;
    public float ВесВремя = 10f;
    public float ВесПилотирование = 10f;
    public float ВесЭнергия = 5f;

    // ---------- Накопленные показатели ----------
    public struct ЗаписьДефекта
    {
        public string Метка;
        public bool Обнаружен;
        public float Время_с;
        public float Глубина_м;
        public float KP_м;          // положение вдоль трубы, м от начала (−1 — трубы нет)
        public Vector3 Позиция;
        public Quaternion Поворот;  // ориентация на трубе — для продолжения сохранённой миссии
        public PipelineDefect.ТипДефекта Тип;
        public Texture2D Снимок;    // кадр бортовой камеры в момент осмотра (фотофиксация)
        public DateTime ВремяСнимка;// астрономическое время снимка
    }
    public struct ЗаписьУдара { public float Время_с; public float Энергия_Дж; public string Объект; }

    public bool Заморожено { get; private set; }
    public float ВремяУчёта_с { get; private set; }
    public float Путь_м { get; private set; }
    public float МаксГлубина_м { get; private set; }
    public float МаксКабель_м { get; private set; }
    public float МаксТемпература_С { get; private set; }
    public float МинВысотаНадДном_м { get; private set; } = float.PositiveInfinity;
    public float ВремяСоСветом_с { get; private set; }
    public float ВремяНаАвтопилоте_с { get; private set; }
    public float ВремяУТрубы_с { get; private set; }
    // Сколько секунд камера была в мути, поднятой движителями (RovSiltStirring).
    public float ВремяВМути_с { get; private set; }
    [Tooltip("Мутность у камеры (0..1), с которой считается, что камера «в мути».")]
    [Range(0f, 1f)] public float ПорогМутности = 0.3f;
    public float МаксНаклон_град { get; private set; }
    public float ЗарядНаСтарте_проц { get; private set; }

    public float СКОНаклона_град => ВремяУчёта_с > 0f ? Mathf.Sqrt(_сумКвНаклона / ВремяУчёта_с) : 0f;
    public float СредняяСкорость_мс => ВремяУчёта_с > 0f ? Путь_м / ВремяУчёта_с : 0f;
    public float СкоростьОсмотра_уз => ВремяУТрубы_с > 0f ? _сумСкоростиУТрубы / ВремяУТрубы_с * МвСвУзлах : 0f;
    public float СредняяДистанция_м => ВремяУТрубы_с > 0f ? _сумДистанции / ВремяУТрубы_с : 0f;
    public float РасходЗаряда_проц => Mathf.Max(0f, ЗарядНаСтарте_проц - _зарядКонец_проц);
    public int ПолученоПовреждений => _поврКонец - _поврСтарт;

    public readonly List<ЗаписьУдара> Удары = new List<ЗаписьУдара>();
    public readonly List<ЗаписьДефекта> Дефекты = new List<ЗаписьДефекта>();   // по KP, как в журнале инспекции
    public readonly List<float> ДлительностиЦелей = new List<float>();
    // Профиль глубины: один отсчёт в ШагПрофиля_с секунд (истинная глубина, м).
    public readonly List<float> ПрофильГлубины = new List<float>();
    public const float ШагПрофиля_с = 1f;

    // Труба и покрытие трассы: ячейки по 1 м вдоль оси, true — участок осмотрен.
    public bool ЕстьТруба { get; private set; }
    public float ДлинаТрубы_м { get; private set; }
    public bool[] Покрытие { get; private set; }
    public float ДоляПокрытия
    {
        get
        {
            if (Покрытие == null || Покрытие.Length == 0) return 0f;
            int n = 0; foreach (var b in Покрытие) if (b) n++;
            return (float)n / Покрытие.Length;
        }
    }

    const float МвСвУзлах = 1.943844f;   // 1 м/с = 1.944 уз
    const float ЯчейкаПокрытия_м = 1f;

    float _сумКвНаклона, _сумСкоростиУТрубы, _сумДистанции;
    float _зарядКонец_проц;
    int _поврСтарт, _поврКонец;
    Vector3 _прошлаяПозиция;
    bool _естьПозиция, _стартЗаписан;
    float _следПрофиль;
    Vector3 _трубаНачало, _трубаОсь;
    float _трубаРадиус;
    readonly Dictionary<PipelineDefect, int> _индексДефекта = new Dictionary<PipelineDefect, int>();
    Camera _камераОсмотра;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    void OnEnable()  { RovHealthMonitor.ПриУдаре += НаУдар; }
    void OnDisable() { RovHealthMonitor.ПриУдаре -= НаУдар; }
    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        // Снимки дефектов — текстуры в памяти GPU/CPU; при перезапуске сцены их
        // надо освободить явно, иначе каждая миссия оставляла бы их висеть.
        foreach (var д in Дефекты) if (д.Снимок != null) Destroy(д.Снимок);
    }

    void Start()
    {
        if (НПА == null)
        {
            var rc = FindFirstObjectByType<RovController>();
            if (rc != null) НПА = rc.transform;
        }

        var спавнер = FindFirstObjectByType<PipelineDefectSpawner>();
        if (спавнер != null && спавнер.ГеометрияТрубы(out Vector3 a, out Vector3 b, out float r) && (b - a).magnitude > 0.5f)
        {
            ЕстьТруба = true;
            _трубаНачало = a;
            _трубаОсь = (b - a).normalized;
            ДлинаТрубы_м = (b - a).magnitude;
            _трубаРадиус = r;
            Покрытие = new bool[Mathf.Max(1, Mathf.CeilToInt(ДлинаТрубы_м / ЯчейкаПокрытия_м))];
        }
        // Камера бортового вида (рендерит в RenderTexture монитора) — с неё фотофиксация.
        if (RovScanner.Instance != null && RovScanner.Instance.Камера != null)
            _камераОсмотра = RovScanner.Instance.Камера.GetComponent<Camera>();
        if (_камераОсмотра == null)
            foreach (var c in FindObjectsByType<Camera>(FindObjectsSortMode.None))
                if (c.targetTexture != null) { _камераОсмотра = c; break; }

        if (ЗонаОсмотра_м <= 0f)
            ЗонаОсмотра_м = RovScanner.Instance != null ? RovScanner.Instance.ДальностьСканирования : 4f;

        // Дефекты расставляет спавнер в Awake — к Start они уже есть. Журнал ведём
        // по возрастанию KP — как в реальном отчёте инспекции.
        var найденные = new List<PipelineDefect>(FindObjectsByType<PipelineDefect>(FindObjectsSortMode.None));
        if (ЕстьТруба) найденные.Sort((x, y) => KP(x.transform.position).CompareTo(KP(y.transform.position)));
        foreach (var d in найденные)
        {
            var дефект = d;
            _индексДефекта[дефект] = Дефекты.Count;
            Дефекты.Add(new ЗаписьДефекта
            {
                Метка = дефект.Метка, Обнаружен = дефект.Обнаружен, Время_с = -1f,
                Позиция = дефект.transform.position, Поворот = дефект.transform.rotation, Тип = дефект.Тип,
                KP_м = ЕстьТруба ? KP(дефект.transform.position) : -1f
            });
            if (дефект.ПриОбнаружении == null) дефект.ПриОбнаружении = new UnityEvent();
            дефект.ПриОбнаружении.AddListener(() => НаОбнаружение(дефект));
        }

        var m = MissionManager.Instance;
        if (m != null) m.МиссияЗавершена += Заморозить;
    }

    // Положение точки вдоль трубы, м от её начала.
    public float KP(Vector3 p) => Vector3.Dot(p - _трубаНачало, _трубаОсь);

    // Дистанция от точки до поверхности трубы (труба ≈ цилиндр вдоль оси).
    float ДистанцияДоТрубы(Vector3 p)
    {
        float t = Mathf.Clamp(KP(p), 0f, ДлинаТрубы_м);
        Vector3 наОси = _трубаНачало + _трубаОсь * t;
        return Mathf.Max(0f, Vector3.Distance(p, наОси) - _трубаРадиус);
    }

    // Стартовые значения берём, когда телеметрия впервые валидна (до этого в RovSystems нули).
    void ЗаписатьСтарт()
    {
        ЗарядНаСтарте_проц = _зарядКонец_проц = RovSystems.batteryPercent;
        _поврСтарт = _поврКонец = СуммаПовреждений();
        _стартЗаписан = true;
    }

    void Update()
    {
        if (Заморожено || !RovSystems.telemetryValid) return;
        if (!_стартЗаписан) ЗаписатьСтарт();

        float dt = Time.deltaTime;
        if (dt <= 0f) return;   // пауза
        ВремяУчёта_с += dt;

        if (НПА != null)
        {
            Vector3 p = НПА.position;
            if (_естьПозиция)
            {
                float шаг = Vector3.Distance(p, _прошлаяПозиция);
                if (шаг < 5f) Путь_м += шаг;   // телепорт/респавн не считаем путём
            }
            _прошлаяПозиция = p;
            _естьПозиция = true;

            if (ЕстьТруба)
            {
                float дист = ДистанцияДоТрубы(p);
                float kp = KP(p);
                if (дист <= ЗонаОсмотра_м && kp >= 0f && kp <= ДлинаТрубы_м)
                {
                    ВремяУТрубы_с += dt;
                    _сумСкоростиУТрубы += RovSystems.speedTrue_mps * dt;
                    _сумДистанции += дист * dt;
                    // Участок засчитан осмотренным, только если было светло.
                    if (RovSystems.lightsOn)
                        Покрытие[Mathf.Clamp((int)(kp / ЯчейкаПокрытия_м), 0, Покрытие.Length - 1)] = true;
                }
            }
        }

        МаксГлубина_м = Mathf.Max(МаксГлубина_м, RovSystems.depthTrue_m);
        МаксКабель_м = Mathf.Max(МаксКабель_м, RovSystems.communicationDistance_m);
        МаксТемпература_С = Mathf.Max(МаксТемпература_С, RovSystems.thrusterTemp_C);
        if (RovSystems.altitudeTrue_m > 0f) МинВысотаНадДном_м = Mathf.Min(МинВысотаНадДном_м, RovSystems.altitudeTrue_m);
        if (RovSystems.lightsOn) ВремяСоСветом_с += dt;
        if (RovSystems.turbidity > ПорогМутности) ВремяВМути_с += dt;
        if (RovSystems.depthHoldActive) ВремяНаАвтопилоте_с += dt;

        float наклон = Mathf.Sqrt(RovSystems.pitchTrue_deg * RovSystems.pitchTrue_deg +
                                  RovSystems.rollTrue_deg * RovSystems.rollTrue_deg);
        МаксНаклон_град = Mathf.Max(МаксНаклон_град, наклон);
        _сумКвНаклона += наклон * наклон * dt;

        if (ВремяУчёта_с >= _следПрофиль)
        {
            ПрофильГлубины.Add(RovSystems.depthTrue_m);
            _следПрофиль += ШагПрофиля_с;
        }

        _зарядКонец_проц = RovSystems.batteryPercent;
        _поврКонец = СуммаПовреждений();
    }

    void НаУдар(float энергия, string объект)
    {
        if (Заморожено) return;
        Удары.Add(new ЗаписьУдара { Время_с = ВремяМиссии(), Энергия_Дж = энергия, Объект = объект });
    }

    void НаОбнаружение(PipelineDefect d)
    {
        if (Заморожено || !_индексДефекта.TryGetValue(d, out int i)) return;
        var з = Дефекты[i];
        if (з.Обнаружен && з.Время_с >= 0f) return;
        з.Обнаружен = true;
        з.Время_с = ВремяМиссии();
        з.Глубина_м = RovSystems.depthTrue_m;
        Дефекты[i] = з;
        StartCoroutine(СнятьКадр(i));
    }

    // Фотофиксация: в конце кадра (камера уже отрисовала RenderTexture) копируем
    // её в Texture2D. Так на снимке ровно то, что оператор видел на мониторе.
    IEnumerator СнятьКадр(int i)
    {
        yield return new WaitForEndOfFrame();
        if (_подавитьСнимок.Contains(i)) yield break;
        var rt = _камераОсмотра != null ? _камераОсмотра.targetTexture : null;
        if (rt == null) yield break;
        var прежняя = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        tex.Apply();
        RenderTexture.active = прежняя;
        var з = Дефекты[i];
        з.Снимок = tex;
        з.ВремяСнимка = DateTime.Now;
        Дефекты[i] = з;
    }

    // ---------- Продолжение сохранённой миссии ----------
    // Внутренние накопители (для сохранения и восстановления).
    public float СуммаКвНаклона => _сумКвНаклона;
    public float СуммаСкоростиУТрубы => _сумСкоростиУТрубы;
    public float СуммаДистанции => _сумДистанции;
    public int ПовреждённостьНаСтарте => _поврСтарт;

    // Вернуть накопленное до сохранения: путь, максимумы, времена, покрытие, профиль,
    // удары; найденные дефекты получают СОХРАНЁННЫЕ время, глубину и снимок
    // (сканер при восстановлении «находит» их заново — время там было бы текущим).
    public void Восстановить(MissionArchive.Запись з, Dictionary<PipelineDefect, MissionArchive.Дефект> найденные)
    {
        Путь_м = з.путь_м; МаксГлубина_м = з.максГлубина_м; МаксКабель_м = з.максКабель_м;
        МаксТемпература_С = з.максТемп_С; МинВысотаНадДном_м = з.минВысота_м >= 0f ? з.минВысота_м : float.PositiveInfinity;
        МаксНаклон_град = з.максНаклон_град; ВремяУчёта_с = з.времяУчёта_с;
        ВремяСоСветом_с = з.времяСоСветом_с; ВремяНаАвтопилоте_с = з.времяНаАвтопилоте_с;
        ВремяУТрубы_с = з.времяУТрубы_с; ВремяВМути_с = з.времяВМути_с;
        _сумКвНаклона = з.сумКвНаклона; _сумСкоростиУТрубы = з.сумСкорости; _сумДистанции = з.сумДистанции;
        ЗарядНаСтарте_проц = з.зарядНаСтарте_проц;
        _поврСтарт = з.повреждённостьНаСтарте;
        _зарядКонец_проц = RovSystems.batteryPercent;
        _поврКонец = СуммаПовреждений();
        _стартЗаписан = true;
        _следПрофиль = ВремяУчёта_с;
        ПрофильГлубины.Clear();
        if (з.профильГлубины != null) ПрофильГлубины.AddRange(з.профильГлубины);
        if (Покрытие != null && !string.IsNullOrEmpty(з.покрытиеСтрока))
            for (int i = 0; i < Покрытие.Length && i < з.покрытиеСтрока.Length; i++) Покрытие[i] = з.покрытиеСтрока[i] == '1';
        Удары.Clear();
        foreach (var у in з.удары) Удары.Add(new ЗаписьУдара { Время_с = у.время_с, Энергия_Дж = у.энергия_Дж, Объект = у.объект });

        foreach (var пара in найденные)
        {
            if (!_индексДефекта.TryGetValue(пара.Key, out int i)) continue;
            var д = Дефекты[i];
            if (д.Снимок != null) Destroy(д.Снимок);   // снимок «повторного нахождения» не нужен
            д.Обнаружен = true;
            д.Время_с = пара.Value.время_с;
            д.Глубина_м = пара.Value.глубина_м;
            д.Снимок = MissionArchive.ЗагрузитьСнимок(з.id, пара.Value.снимок);
            if (DateTime.TryParse(пара.Value.времяСнимка, out var т)) д.ВремяСнимка = т;
            Дефекты[i] = д;
            _подавитьСнимок.Add(i);
        }
    }

    // Индексы дефектов, которым при восстановлении НЕ надо снимать кадр (их кадр
    // загружен из архива; корутина СнятьКадр, запущенная «нахождением», отменяется).
    readonly HashSet<int> _подавитьСнимок = new HashSet<int>();

    // Миссия закрыта — фиксируем числа, дальше они не меняются.
    public void Заморозить()
    {
        if (Заморожено) return;
        Заморожено = true;

        ДлительностиЦелей.Clear();
        var m = MissionManager.Instance;
        if (m == null) return;
        float пред = 0f;
        foreach (var ц in m.Цели)
        {
            if (ц.ВремяЗавершения_с < 0f) { ДлительностиЦелей.Add(-1f); continue; }
            ДлительностиЦелей.Add(ц.ВремяЗавершения_с - пред);
            пред = ц.ВремяЗавершения_с;
        }
    }

    // ---------- Оценка ----------
    public struct Критерий { public string Название; public float Балл; public float Макс; public string Пояснение; }

    public List<Критерий> Оценить()
    {
        var список = new List<Критерий>();
        var m = MissionManager.Instance;

        // Выполнение: 3/4 — закрытые цели, 1/4 — найдено дефектов из всех.
        int целей = m != null ? m.Цели.Count : 0, закрыто = 0;
        if (m != null) foreach (var ц in m.Цели) if (ц.Состояние == MissionObjectiveState.Completed) закрыто++;
        int найдено = ЧислоНайденных();
        float долЦелей = целей > 0 ? (float)закрыто / целей : 0f;
        float долДеф = Дефекты.Count > 0 ? (float)найдено / Дефекты.Count : 1f;
        Добавить("Выполнение задания", ВесВыполнение, 0.75f * долЦелей + 0.25f * долДеф,
                 $"цели {закрыто}/{целей}, дефекты {найдено}/{Дефекты.Count}");

        // Качество осмотра: покрытие трассы (60%) + скорость в норме (40%).
        float долПокр = ЕстьТруба ? ДоляПокрытия : 1f;
        float долСкор = 1f - Mathf.Clamp01((СкоростьОсмотра_уз - МаксСкоростьОсмотра_уз) / Mathf.Max(0.05f, МаксСкоростьОсмотра_уз));
        Добавить("Качество осмотра", ВесКачествоОсмотра, 0.6f * долПокр + 0.4f * долСкор,
                 Р($"покрытие {долПокр * 100f:0}%, скорость {СкоростьОсмотра_уз:0.0} уз"));

        float безоп = 1f - (ШтрафЗаУдар * Удары.Count + ШтрафЗаПовреждение * ПолученоПовреждений) / Mathf.Max(1f, ВесБезопасность);
        Добавить("Безопасность", ВесБезопасность, безоп, $"ударов {Удары.Count}, повреждений {ПолученоПовреждений}");

        float t = m != null ? m.ВремяМиссии_с : ВремяУчёта_с;
        Добавить("Время", ВесВремя, 1f - Mathf.Clamp01((t - НормативВремени_с) / Mathf.Max(1f, НормативВремени_с)),
                 $"{ФорматВремени(t)} при нормативе {ФорматВремени(НормативВремени_с)}");

        Добавить("Устойчивость", ВесПилотирование,
                 1f - Mathf.Clamp01((СКОНаклона_град - СКОНаклонаОтлично_град) / Mathf.Max(0.1f, СКОНаклонаПлохо_град - СКОНаклонаОтлично_град)),
                 Р($"СКО наклона {СКОНаклона_град:0.0}°"));

        float норма = Mathf.Max(0.1f, НормаРасходаЗаряда_проц);
        Добавить("Энергия", ВесЭнергия, 1f - Mathf.Clamp01((РасходЗаряда_проц - норма) / (2f * норма)),
                 Р($"расход {РасходЗаряда_проц:0.0}%"));

        return список;

        void Добавить(string имя, float вес, float доля, string пояснение) =>
            список.Add(new Критерий { Название = имя, Макс = вес, Балл = вес * Mathf.Clamp01(доля), Пояснение = пояснение });
    }

    public float ИтогПроцент(out float сумма, out float макс)
    {
        сумма = макс = 0f;
        foreach (var к in Оценить()) { сумма += к.Балл; макс += к.Макс; }
        return макс > 0f ? сумма / макс * 100f : 0f;
    }

    // ---------- Замечания инструктора ----------
    // Уровень 0 — критично (красное), 1 — внимание (жёлтое). Критичные идут первыми.
    public struct Замечание { public int Уровень; public string Текст; }

    public List<Замечание> Замечания()
    {
        var z = new List<Замечание>();
        int пропущено = Дефекты.Count - ЧислоНайденных();
        if (пропущено > 0)
            z.Add(new Замечание { Уровень = 0, Текст = $"Не выявлено дефектов: {пропущено} из {Дефекты.Count} (см. журнал осмотра)" });
        var m = MissionManager.Instance;
        if (m != null && m.Досрочно)
            z.Add(new Замечание { Уровень = 0, Текст = "Миссия завершена оператором досрочно" });
        if (ПолученоПовреждений > 0)
            z.Add(new Замечание { Уровень = 0, Текст = $"Повреждены системы аппарата ({ПолученоПовреждений} ур.)" });
        if (Удары.Count > 0)
        {
            var с = Удары[0]; foreach (var у in Удары) if (у.Энергия_Дж > с.Энергия_Дж) с = у;
            z.Add(new Замечание { Уровень = 1, Текст = Р($"Столкновений: {Удары.Count}, сильнейшее — {с.Энергия_Дж:0} Дж") });
        }
        if (ЕстьТруба && ДоляПокрытия < 0.8f)
            z.Add(new Замечание { Уровень = 1, Текст = Р($"Осмотрено {ДоляПокрытия * 100f:0}% трассы — есть пропуски") });
        if (СкоростьОсмотра_уз > МаксСкоростьОсмотра_уз)
            z.Add(new Замечание { Уровень = 1, Текст = Р($"Скорость осмотра {СкоростьОсмотра_уз:0.0} уз (норма до {МаксСкоростьОсмотра_уз:0.0})") });
        if (ВремяУТрубы_с > 0f && СредняяДистанция_м > МаксДистанцияОсмотра_м)
            z.Add(new Замечание { Уровень = 1, Текст = Р($"Далеко от трубы: в среднем {СредняяДистанция_м:0.0} м") });
        if (ВремяВМути_с >= 1f)
            z.Add(new Замечание { Уровень = 1, Текст = Р($"Поднята муть со дна: камера ничего не видела {ВремяВМути_с:0} с") });
        else if (МинВысотаНадДном_м < МинБезопаснаяВысота_м)
            z.Add(new Замечание { Уровень = 1, Текст = Р($"Опасно близко ко дну ({МинВысотаНадДном_м:0.0} м)") });
        return z;
    }

    // ---------- Утилиты ----------
    // Числа в окне и отчёте — по-русски, с десятичной запятой, независимо от
    // языка системы (на Quest культура обычно английская).
    public static readonly NumberFormatInfo RU = new NumberFormatInfo { NumberDecimalSeparator = ",", NumberGroupSeparator = " " };
    public static string Р(FormattableString f) => f.ToString(RU);

    // Перевод интегрального балла в пятибалльную шкалу.
    public static string Отметка(float процент) =>
        процент >= 90f ? "5 (отлично)" :
        процент >= 75f ? "4 (хорошо)" :
        процент >= 60f ? "3 (удовлетворительно)" : "2 (неудовлетворительно)";

    public int ЧислоНайденных()
    {
        int n = 0;
        foreach (var д in Дефекты) if (д.Обнаружен) n++;
        return n;
    }

    // KP в формате инспекционных отчётов: километры с тремя знаками (KP 0.034 = 34 м).
    public static string ФорматKP(float м) =>
        "KP " + (Mathf.Max(0f, м) / 1000f).ToString("0.000", RU);

    // «Трещина: над сварным швом» → «Трещина» (для компактных строк).
    public static string КороткаяМетка(string метка)
    {
        int i = метка.IndexOf(':');
        return i > 0 ? метка.Substring(0, i) : метка;
    }

    public static string ФорматВремени(float с)
    {
        int s = Mathf.Max(0, Mathf.RoundToInt(с));
        return $"{s / 60:00}:{s % 60:00}";
    }

    static float ВремяМиссии() => MissionManager.Instance != null ? MissionManager.Instance.ВремяМиссии_с : Time.time;

    static int СуммаПовреждений() =>
        RovSystems.thrusterDamage + RovSystems.cameraDamage + RovSystems.manipulatorDamage +
        RovSystems.communicationDamage + RovSystems.lightsDamage;
}

using System.Collections.Generic;
using UnityEngine;

// Магистральный морской газопровод в бетонной оболочке — по образцу «Северного потока».
// Строится в коде при старте сцены: прямая труба лежит на дне, а не стоит на ножках.
//
// Реальные данные, на которые опирается модель:
//   • сталь Ø1153 мм, поверх — антикоррозийная изоляция ~4 мм и бетонная (утяжеляющая)
//     оболочка 60–110 мм; секция 12 м весит ~24 т — бетон удваивает вес, чтобы труба
//     лежала на дне и не всплывала (ing.dk, Kallanish/ЧТПЗ, Nord Stream 2010);
//   • секции свариваются на трубоукладчике; на месте сварки бетона нет — стык
//     закрывается манжетой и заливкой, по виду это полоса другого цвета через ~12 м;
//   • браслетные аноды на трубе в бетоне ставят заподлицо с бетоном (Offshore Magazine,
//     J. Britton); шаг — по расчёту катодной защиты, обычно 100–300 м;
//   • труба лежит на грунте, частично врезавшись в него.
//
// Всё строится само при запуске, в редакторе ничего нажимать не нужно: сначала
// естественное дно (ПостроительДна), потом труба.
//
// Труба — прямая, через всё дно от края до края (концов не видно), на пологих буграх
// дна: где-то чуть занесена илом, где-то над ложбиной немного висит. Если зазор
// заметный (от 15 см на 5 м и больше) — это свободный пролёт, дефект «Провис».
//
// Повреждения — часть формы трубы, а не наклейки: каждый заезд набор и места новые.
// Что ищут при осмотре трубы в бетоне (Rigzone, Sensors 2020): повреждения покрытия
// и стыков, состояние анодов, посторонние предметы и снасти, следы якорей, провисы.
//
// Работает в Awake раньше PipelineDefectSpawner и сканера — метки дефектов к их
// Start уже стоят.
[DefaultExecutionOrder(-300)]
[DisallowMultipleComponent]
public class МагистральныйТрубопровод : MonoBehaviour
{
    [Header("Трасса (высота точек не важна — труба ложится на дно)")]
    [Tooltip("Задают середину и направление трассы; длина — ДлинаТрассы_м.")]
    public Vector3 Начало;
    public Vector3 Конец = new Vector3(100f, 0f, 0f);
    [Tooltip("Длина обследуемого участка, м: на нём участки трассы и повреждения. Кабель «Марлина» 150 м — " +
             "участок должен быть досягаем из точки спуска. Сама труба идёт через всё дно.")]
    public float ДлинаТрассы_м = 120f;

    [Header("Труба — как у «Северного потока»")]
    [Tooltip("Наружный диаметр стальной трубы, м (Северный поток — 1,153 м).")]
    public float ДиаметрСтали_м = 1.153f;
    [Tooltip("Бетонная утяжеляющая оболочка, м (реально 0,06–0,11 м).")]
    public float ТолщинаБетона_м = 0.08f;
    [Tooltip("Длина одной трубной секции, м (стандарт — 12,2 м).")]
    public float ДлинаСекции_м = 12.2f;
    [Tooltip("Ширина полосы сварного стыка без бетона, м.")]
    public float ШиринаСтыка_м = 0.7f;
    [Tooltip("Насколько заливка стыка утоплена относительно бетона, м.")]
    public float УтоплениеСтыка_м = 0.025f;
    [Tooltip("Браслетный анод на каждой N-й секции (8 × 12,2 ≈ 100 м — нижняя граница реального шага).")]
    public int АнодКаждыеСекций = 8;
    [Tooltip("Длина браслета анода вдоль трубы, м.")]
    public float ДлинаАнода_м = 0.6f;

    [Header("Как труба лежит на дне")]
    [Tooltip("На сколько труба врезалась в грунт, доля диаметра.")]
    [Range(0f, 0.6f)] public float Врезание = 0.25f;

    [Header("Повреждения (каждый заезд — новые)")]
    [Tooltip("Сколько случайных повреждений (провисы — сверх этого, где их даст дно).")]
    public int ПовреждённыхМин = 4;
    public int ПовреждённыхМакс = 6;
    [Tooltip("Зерно. 0 — каждый заезд по-новому, иначе одна и та же раскладка.")]
    public int Seed = 0;

    [Tooltip("Сегментов по окружности трубы (больше — ровнее края сколов).")]
    public int СегментовПоОкружности = 96;

    public struct Пролёт
    {
        public Vector3 Середина;    // нижняя точка трубы в месте наибольшего зазора
        public float KP_м;
        public float Длина_м;
        public float Зазор_м;
        public bool Подпёрт;        // допустимый (мелкий) пролёт — не дефект
        public float ОтS, ДоS;      // где труба отрывается от грунта и где ложится снова, м по трассе
    }

    public class Повреждение
    {
        public PipelineDefect.ТипДефекта Тип;
        public float S;             // положение по трассе, м
        public float Угол;          // угол вокруг оси от верха трубы, рад
        public float Размер = 1f;
        public int Seed;
        public PipelineDefect Метка;
    }

    public readonly List<Пролёт> Пролёты = new List<Пролёт>();
    public readonly List<Повреждение> Повреждения = new List<Повреждение>();
    public bool Построен { get; private set; }
    public float Радиус => ДиаметрСтали_м * 0.5f + ТолщинаБетона_м;
    float РадиусИзоляции => ДиаметрСтали_м * 0.5f + 0.004f;
    // Обследуемый участок — по нему считаются KP и покрытие осмотром.
    public Vector3 НачалоОсмотра { get; private set; }
    public Vector3 КонецОсмотра { get; private set; }

    const float Шаг_м = 0.5f;          // шаг профиля трассы
    const float ТайлТекстуры_м = 3f;   // текстура бетона повторяется вдоль трубы

    ТрассаДна.Трасса _т;
    Vector3 _начало, _ось, _поперёк;
    float _длина, _ds;
    float _осьY;                       // высота оси прямой трубы
    float _от, _до;                    // где труба начинается и кончается (по трассе, м; от < 0 < длина < до)
    float[] _грунт;                    // дно под трубой на обследуемом участке
    readonly List<float> _стыки = new List<float>();
    readonly List<Vector2> _аноды = new List<Vector2>();   // (начало, конец) браслета
    readonly List<PipelineDefect> _меткиПролётов = new List<PipelineDefect>();
    Material[] _мат;
    GameObject _детали;
    bool _восстановление;

    [Header("Снимки дефектов (для проверки)")]
    [Tooltip("При запуске в редакторе снять каждое повреждение отдельной камерой и сохранить " +
             "PNG в Docs/Снимки_дефектов (папка перезаписывается).")]
    public bool СнимкиДефектов = true;

    void Awake() => Построить();

    void Start()
    {
#if UNITY_EDITOR
        if (СнимкиДефектов && Построен) StartCoroutine(СнятьДефекты());
#endif
    }

    public void Построить()
    {
        if (Построен) return;
        if (!ПодготовитьТрассу()) return;
        gameObject.layer = Слой;
        ПостроительДна.Построить(_т, gameObject);
        ДекорДна.Расставить(_т, Слой, предпросмотр: false);
        if (GetComponent<АтмосфераВоды>() == null) gameObject.AddComponent<АтмосфераВоды>();
        РассчитатьТрубу();

        var mf = GetComponent<MeshFilter>(); if (mf == null) mf = gameObject.AddComponent<MeshFilter>();
        var mr = GetComponent<MeshRenderer>(); if (mr == null) mr = gameObject.AddComponent<MeshRenderer>();
        if (GetComponent<MeshCollider>() == null) gameObject.AddComponent<MeshCollider>();
        // Меш строится в мировых координатах — объект трубы ставим в ноль.
        transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        transform.localScale = Vector3.one;
        _мат = Материалы();
        mr.sharedMaterials = _мат;
        Построен = true;

        ВыбратьПовреждения(Seed);
        Пересобрать();

        var сводка = new System.Text.StringBuilder($"[Трубопровод] Построен: прямая труба {_до - _от:0} м, обследуемый участок {_длина:0} м, Ø{Радиус * 2f:0.00} м с бетоном");
        foreach (var п in Пролёты)
            сводка.Append($"\n  провис KP {п.KP_м:0} м: длина {п.Длина_м:0.0} м, зазор {п.Зазор_м:0.00} м{(п.Подпёрт ? " (небольшой)" : " — дефект")}");
        foreach (var п in Повреждения)
            сводка.Append($"\n  {PipelineDefect.Название(п.Тип)} — KP {п.S:0} м");
        Debug.Log(сводка.ToString());
    }

    // Предпросмотр для режима сцены (зовёт ПредпросмотрДна в редакторе): та же труба с
    // примером повреждений, скрытым объектом — в файл сцены не сохраняется.
    public GameObject ПостроитьПредпросмотр()
    {
        if (!ПодготовитьТрассу()) return null;
        РассчитатьТрубу();
        ВыбратьПовреждения(12345);
        var go = new GameObject("Предпросмотр трубы (не сохраняется)");
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, gameObject.scene);   // в сцену трубы, а не в активную
        go.layer = Слой;
        go.AddComponent<MeshFilter>().sharedMesh = МешТрубы.Построить(this);
        go.AddComponent<MeshRenderer>().sharedMaterials = Материалы();
        var детали = new GameObject("Детали");
        детали.layer = Слой;
        детали.transform.SetParent(go.transform, false);
        МешТрубы.Детали(this, детали.transform);
        foreach (var t in go.GetComponentsInChildren<Transform>(true))
            t.gameObject.hideFlags = HideFlags.DontSave | HideFlags.NotEditable;
        Повреждения.Clear();
        return go;
    }

    // Труба — на слое Underwater, как дно: его рисует камера монитора аппарата и не
    // рисует камера кабины в меню (фон меню — чистая вода). Слой старой модели трубы
    // для этого не годится: её первый меш лежит на Default.
    int Слой
    {
        get { int l = LayerMask.NameToLayer("Underwater"); return l >= 0 ? l : gameObject.layer; }
    }

    static Material[] Материалы() => new[]
    {
        ПроцедурныеМатериалы.Бетон(), ПроцедурныеМатериалы.Стык(), ПроцедурныеМатериалы.Анод(),
        ПроцедурныеМатериалы.АнодСработан(),
    };

    // Трасса нужной длины с той же серединой и направлением (поля Начало/Конец не меняются).
    bool ПодготовитьТрассу()
    {
        Vector3 напр = Конец - Начало; напр.y = 0f;
        if (напр.sqrMagnitude < 1e-4f) напр = Vector3.right;
        напр.Normalize();
        Vector3 середина = (Начало + Конец) * 0.5f;
        _т = new ТрассаДна.Трасса(середина - напр * ДлинаТрассы_м * 0.5f, середина + напр * ДлинаТрассы_м * 0.5f);
        _длина = _т.Длина;
        if (_длина < 2f) { Debug.LogWarning("[Трубопровод] Трасса короче 2 м — не строю."); return false; }
        _начало = new Vector3(_т.Начало.x, 0f, _т.Начало.y);
        _ось = new Vector3(_т.Ось.x, 0f, _т.Ось.y);
        _поперёк = new Vector3(_т.Поперёк.x, 0f, _т.Поперёк.y);
        return true;
    }

    // Где труба кончается, на какой высоте лежит, где провисы, стыки и аноды.
    void РассчитатьТрубу()
    {
        Vector3 середина = _начало + _ось * (_длина * 0.5f);
        if (ПостроительДна.ПролётПоДну(середина, _ось, out float от, out float до))
        { _от = от + _длина * 0.5f; _до = до + _длина * 0.5f; }
        else
        { _от = 0f; _до = _длина; }

        ЗамеритьДно();
        // Высота прямой трубы: низ — на уровне нижней пятой части дна вдоль неё, плюс
        // врезание. Так почти везде труба лежит в грунте, а висит только над ложбинами.
        var уровни = new List<float>();
        for (float s = _от; s <= _до; s += 1f) уровни.Add(ВысотаГрунта(ТочкаТрассы(s)));
        уровни.Sort();
        float низ = уровни.Count > 0 ? уровни[уровни.Count / 5] : ВысотаГрунта(ТочкаТрассы(0f));
        _осьY = низ - Врезание * 2f * Радиус + Радиус;
        НайтиПролёты();
        Разметка();
        НачалоОсмотра = ЦентрНа(0f);
        КонецОсмотра  = ЦентрНа(_длина);
    }

    // ── Профиль трассы ──────────────────────────────────────────────────────────

    Vector3 ТочкаТрассы(float s) => _начало + _ось * s;

    // Дно под трубой на обследуемом участке.
    void ЗамеритьДно()
    {
        int n = Mathf.CeilToInt(_длина / Шаг_м) + 1;
        _ds = _длина / (n - 1);
        _грунт = new float[n];
        for (int i = 0; i < n; i++)
        {
            Vector3 p = ТочкаТрассы(i * _ds);
            _грунт[i] = ВысотаГрунта(p);
        }
    }

    // Свободный пролёт — участок, где низ трубы выше дна (зазор > 5 см), длиннее 4 м.
    // «Подпёрт» здесь значит «допустимый»: мелкий, дефектом не считается.
    void НайтиПролёты()
    {
        Пролёты.Clear();
        float R = Радиус;
        int n = _грунт.Length, начало = -1;
        for (int i = 0; i <= n; i++)
        {
            bool висит = i < n && _осьY - R - _грунт[i] > 0.05f;
            if (висит && начало < 0) начало = i;
            if (висит || начало < 0) continue;

            int конец = i - 1;
            float длина = (конец - начало) * _ds;
            if (длина >= 4f)
            {
                int макс = начало;
                for (int k = начало; k <= конец; k++)
                    if (_грунт[k] < _грунт[макс]) макс = k;
                float s = макс * _ds;
                // Мелкий провис (до 15 см или короче 5 м) — допустимый, не дефект.
                bool подпёрт = длина < 5f || _осьY - R - _грунт[макс] < 0.15f;
                Пролёты.Add(new Пролёт
                {
                    Середина = ЦентрНа(s) - Vector3.up * R,
                    KP_м = s,
                    Длина_м = длина,
                    Зазор_м = _осьY - R - _грунт[макс],
                    Подпёрт = подпёрт,
                    ОтS = начало * _ds,
                    ДоS = конец * _ds,
                });
            }
            начало = -1;
        }
    }

    float ГрунтНа(float s)
    {
        float t = Mathf.Clamp(s / _ds, 0f, _грунт.Length - 1);
        int i = Mathf.Min((int)t, _грунт.Length - 2);
        return Mathf.Lerp(_грунт[i], _грунт[i + 1], t - i);
    }

    Vector3 ЦентрНа(float s)
    {
        Vector3 p = ТочкаТрассы(s);
        p.y = _осьY;
        return p;
    }

    // Центр сечения и оси: t — вдоль трубы, «вверх» — мировая вертикаль.
    void Рамка(float s, out Vector3 c, out Vector3 t, out Vector3 вверх, out Vector3 бок)
    {
        c = ЦентрНа(s);
        t = _ось;
        вверх = Vector3.up;
        бок = Vector3.Cross(t, вверх);
    }

    Vector3 ТочкаНаТрубе(float s, float угол, float r, out Vector3 наружу)
    {
        Рамка(s, out Vector3 c, out _, out Vector3 вверх, out Vector3 бок);
        наружу = вверх * Mathf.Cos(угол) + бок * Mathf.Sin(угол);
        return c + наружу * r;
    }

    // Стыки и аноды по всей трубе.
    void Разметка()
    {
        _стыки.Clear(); _аноды.Clear();
        float w = ШиринаСтыка_м * 0.5f;
        int k0 = Mathf.FloorToInt(_от / ДлинаСекции_м - 0.37f);
        for (int k = k0; ДлинаСекции_м * (0.37f + k) < _до; k++)
        {
            float c = ДлинаСекции_м * (0.37f + k);
            if (c < _от) continue;
            _стыки.Add(c);
            // Браслет ставят на заводе у торца секции — рядом со стыком.
            if (АнодКаждыеСекций > 0 && ((k % АнодКаждыеСекций) + АнодКаждыеСекций) % АнодКаждыеСекций == 1)
            {
                float a0 = c + w + 0.07f;
                _аноды.Add(new Vector2(a0, a0 + ДлинаАнода_м));
            }
        }
    }

    // ── Повреждения ─────────────────────────────────────────────────────────────

    // Место на обследуемом участке подходит для повреждения: труба там не занесена илом
    // выше середины (иначе повреждение снизу не увидеть).
    bool МестоОткрыто(float s)
    {
        if (s < 3f || s > _длина - 3f) return false;
        return ГрунтНа(s) < _осьY - 0.2f;
    }

    void ВыбратьПовреждения(int seed)
    {
        Повреждения.Clear();
        var rng = seed != 0 ? new System.Random(seed) : new System.Random();
        float рнд(float a, float b) => Mathf.Lerp(a, b, (float)rng.NextDouble());

        var пул = new List<PipelineDefect.ТипДефекта>
        {
            PipelineDefect.ТипДефекта.СколБетона, PipelineDefect.ТипДефекта.СколБетона,
            PipelineDefect.ТипДефекта.Трещина, PipelineDefect.ТипДефекта.Трещина,
            PipelineDefect.ТипДефекта.ПовреждениеСтыка, PipelineDefect.ТипДефекта.Анод,
            PipelineDefect.ТипДефекта.ПостороннийПредмет, PipelineDefect.ТипДефекта.Утечка,
        };
        int нужно = rng.Next(Mathf.Min(ПовреждённыхМин, ПовреждённыхМакс), Mathf.Max(ПовреждённыхМин, ПовреждённыхМакс) + 1);
        int страховка = 400;
        while (нужно > 0 && пул.Count > 0 && страховка-- > 0)
        {
            int и = rng.Next(пул.Count);
            var тип = пул[и];
            float s = тип switch
            {
                PipelineDefect.ТипДефекта.ПовреждениеСтыка => _стыки.Count > 0 ? _стыки[rng.Next(_стыки.Count)] : -1f,
                PipelineDefect.ТипДефекта.Анод => _аноды.Count > 0 ? (_аноды[rng.Next(_аноды.Count)].x + ДлинаАнода_м * 0.5f) : -1f,
                _ => рнд(0f, _длина),
            };
            if (s < 0f || !МестоОткрыто(s) || ЗанятоРядом(s, 6f)) continue;
            // Трещина и скол — на бетоне, не на стыке.
            if ((тип == PipelineDefect.ТипДефекта.Трещина || тип == PipelineDefect.ТипДефекта.СколБетона ||
                 тип == PipelineDefect.ТипДефекта.Утечка) && ДоСтыкаИлиАнода(s) < 0.9f) continue;

            // Угол от верха трубы: сколы и стыки — по всей видимой части, анод и сеть — сверху.
            float угол = тип switch
            {
                PipelineDefect.ТипДефекта.СколБетона => рнд(-1.3f, 1.3f),
                PipelineDefect.ТипДефекта.ПовреждениеСтыка => рнд(-1f, 1f),
                PipelineDefect.ТипДефекта.Анод => 0f,
                PipelineDefect.ТипДефекта.ПостороннийПредмет => 0f,
                _ => рнд(-0.6f, 0.6f),
            };
            Повреждения.Add(new Повреждение { Тип = тип, S = s, Угол = угол, Размер = рнд(0.8f, 1.3f), Seed = rng.Next(1000) });
            пул.RemoveAt(и);
            нужно--;
        }
    }

    bool ЗанятоРядом(float s, float мин)
    {
        foreach (var п in Повреждения) if (Mathf.Abs(п.S - s) < мин) return true;
        foreach (var п in Пролёты) if (Mathf.Abs(п.KP_м - s) < мин) return true;
        return false;
    }

    float ДоСтыкаИлиАнода(float s)
    {
        float мин = float.MaxValue;
        foreach (float c in _стыки) мин = Mathf.Min(мин, Mathf.Abs(s - c));
        foreach (var a in _аноды) мин = Mathf.Min(мин, Mathf.Abs(s - (a.x + a.y) * 0.5f));
        return мин;
    }

    // Продолжение сохранённой миссии: спавнер стирает все метки и ставит сохранённые.
    public void СброситьПовреждения()
    {
        _восстановление = true;
        Повреждения.Clear();
        Пересобрать();
    }

    public PipelineDefect ВосстановитьПовреждение(PipelineDefect.ТипДефекта тип, Vector3 место)
    {
        if (!Построен) return null;
        if (тип == PipelineDefect.ТипДефекта.Провис) return МеткаПровиса(место);
        float s = Mathf.Clamp(Vector3.Dot(место - ТочкаТрассы(0f), _ось), 0f, _длина);
        Рамка(s, out Vector3 c, out _, out Vector3 вверх, out Vector3 бок);
        Vector3 к = место - c;
        var п = new Повреждение
        {
            Тип = тип, S = s, Угол = Mathf.Atan2(Vector3.Dot(к, бок), Vector3.Dot(к, вверх)),
            Seed = Mathf.Abs(Mathf.RoundToInt(s * 97f)) % 1000,
        };
        Повреждения.Add(п);
        Пересобрать();
        return п.Метка;
    }

    // Меш, детали повреждений (арматура, трещины, сеть) и метки для сканера.
    void Пересобрать()
    {
        var меш = МешТрубы.Построить(this);
        GetComponent<MeshFilter>().sharedMesh = меш;
        GetComponent<MeshCollider>().sharedMesh = меш;

        if (_детали != null) Destroy(_детали);
        _детали = new GameObject("Детали повреждений");
        _детали.layer = Слой;
        _детали.transform.SetParent(transform, false);
        МешТрубы.Детали(this, _детали.transform);

        foreach (var п in Повреждения)
            if (п.Метка == null) п.Метка = СоздатьМетку(п);
        if (!_восстановление && _меткиПролётов.Count == 0)
            foreach (var п in Пролёты)
                if (!п.Подпёрт) _меткиПролётов.Add(МеткаПровиса(п));
    }

    PipelineDefect СоздатьМетку(Повреждение п)
    {
        float r = п.Тип == PipelineDefect.ТипДефекта.ПовреждениеСтыка ? РадиусИзоляции : Радиус;
        Vector3 поз = ТочкаНаТрубе(п.S, п.Угол, r, out Vector3 наружу);
        var go = new GameObject($"Дефект_{п.Тип}_KP{п.S:0}");
        go.layer = Слой;
        go.transform.SetParent(transform, false);
        go.transform.SetPositionAndRotation(поз, Quaternion.LookRotation(наружу));
        var d = go.AddComponent<PipelineDefect>();
        d.Тип = п.Тип;
        d.Описание = п.Тип switch
        {
            PipelineDefect.ТипДефекта.СколБетона => "выкрошен бетон, видна арматурная сетка",
            PipelineDefect.ТипДефекта.Трещина => "кольцевая трещина в бетонной оболочке",
            PipelineDefect.ТипДефекта.ПовреждениеСтыка => "выкрошилась заливка, видна сталь со следами коррозии",
            PipelineDefect.ТипДефекта.Анод => "анод сработан, браслет изъеден",
            PipelineDefect.ТипДефекта.ПостороннийПредмет => "на трубе обрывок рыболовной сети",
            PipelineDefect.ТипДефекта.Утечка => "из трубы идут пузыри газа",
            PipelineDefect.ТипДефекта.СледЯкоря => "оболочка смята, сталь вдавлена, на дне борозда от якоря",
            _ => "",
        };
        if (п.Тип == PipelineDefect.ТипДефекта.Утечка || п.Тип == PipelineDefect.ТипДефекта.СледЯкоря)
            go.AddComponent<PipelineLeakEmitter>();
        КонтурПовреждения(d, п);
        return d;
    }

    // ── Контур повреждения ─────────────────────────────────────────────────────
    // Светящаяся линия по границе повреждённого места, лежит на поверхности трубы (в
    // 1–3 см над ней). Вспыхивает при снимке (PipelineDefect.Контур) — видно, что
    // именно засчитано и где кончается повреждение. Размеры — те же, что у формы
    // повреждения в МешТрубы, с небольшим запасом наружу.

    static Material _матКонтура;
    const float Над_м = 0.012f;

    void КонтурПовреждения(PipelineDefect d, Повреждение п)
    {
        float R = Радиус, к = п.Размер;
        var линии = new List<Vector3[]>();
        switch (п.Тип)
        {
            case PipelineDefect.ТипДефекта.СколБетона: линии.Add(Овал(п.S, п.Угол, 0.3f * к * 1.2f, 0.4f * к * 1.2f, 2f, R + Над_м)); break;
            case PipelineDefect.ТипДефекта.Утечка: линии.Add(Овал(п.S, п.Угол, 0.3f, 0.3f, 2f, R + Над_м)); break;
            case PipelineDefect.ТипДефекта.СледЯкоря: линии.Add(Овал(п.S, п.Угол, 0.9f * 1.15f, 0.75f * 1.15f, 2f, R + Над_м)); break;
            case PipelineDefect.ТипДефекта.ПовреждениеСтыка:
                линии.Add(Овал(п.S, п.Угол, ШиринаСтыка_м * 0.5f + 0.06f, 0.45f * к * 1.2f, 4f, R + Над_м)); break;
            case PipelineDefect.ТипДефекта.Трещина:   // трещина идёт по окружности на ±1,1·размер рад
                линии.Add(Овал(п.S, п.Угол, 0.12f, (1.1f * к + 0.15f) * R, 2f, R + Над_м)); break;
            case PipelineDefect.ТипДефекта.ПостороннийПредмет:   // сеть накрывает верх и свисает по бокам
                линии.Add(Овал(п.S, 0f, 1.1f * к + 0.15f, (Mathf.PI * 0.5f + 0.2f) * R, 4f, R + 0.035f)); break;
            case PipelineDefect.ТипДефекта.Анод:   // браслет целиком: два кольца по его краям
            {
                float пол = ДлинаАнода_м * 0.5f + 0.03f;
                линии.Add(Кольцо(п.S - пол, R + Над_м));
                линии.Add(Кольцо(п.S + пол, R + Над_м));
                break;
            }
        }
        ПрикрепитьКонтур(d, линии);
    }

    // Провис: контур по низу трубы над ямой — от места отрыва от грунта до места, где ложится снова.
    void КонтурПровиса(PipelineDefect d, Пролёт п)
    {
        if (п.ДоS <= п.ОтS) return;
        float R = Радиус;
        ПрикрепитьКонтур(d, new List<Vector3[]> { Овал((п.ОтS + п.ДоS) * 0.5f, Mathf.PI, (п.ДоS - п.ОтS) * 0.5f + 0.2f, 1.25f * R, 4f, R + Над_м) });
    }

    // Суперэллипс на поверхности трубы: полуоси вдоль (м) и по окружности (м дуги),
    // степень 2 — овал, 4 — прямоугольник со скруглёнными углами.
    Vector3[] Овал(float s0, float угол0, float a, float b, float степень, float r)
    {
        const int N = 96;
        var т = new Vector3[N];
        for (int i = 0; i < N; i++)
        {
            float φ = i * Mathf.PI * 2f / N, cs = Mathf.Cos(φ), sn = Mathf.Sin(φ);
            float ds = a * Mathf.Sign(cs) * Mathf.Pow(Mathf.Abs(cs), 2f / степень);
            float dt = b * Mathf.Sign(sn) * Mathf.Pow(Mathf.Abs(sn), 2f / степень);
            т[i] = ТочкаНаТрубе(s0 + ds, угол0 + dt / Радиус, r, out _);
        }
        return т;
    }

    Vector3[] Кольцо(float s, float r)
    {
        const int N = 64;
        var т = new Vector3[N];
        for (int i = 0; i < N; i++) т[i] = ТочкаНаТрубе(s, i * Mathf.PI * 2f / N, r, out _);
        return т;
    }

    // Каждая линия — сердцевина и ореол (шире и прозрачнее): так она «светится».
    void ПрикрепитьКонтур(PipelineDefect d, List<Vector3[]> линии)
    {
        if (линии.Count == 0) return;
        if (_матКонтура == null)
        {
            var sh = Shader.Find("Sprites/Default");
            if (sh == null) return;
            _матКонтура = new Material(sh) { name = "Контур дефекта (auto)" };
        }
        var все = new List<LineRenderer>();
        for (int i = 0; i < линии.Count; i++)
        {
            все.Add(ЛинияКонтура(d.transform, "Контур " + i, линии[i], 0.02f));
            все.Add(ЛинияКонтура(d.transform, "Контур " + i + " ореол", линии[i], 0.07f));
        }
        d.Контур = все.ToArray();
        d.ВремяВспышки = Mathf.Max(d.ВремяВспышки, 3f);   // контур держится подольше, чтобы успеть разглядеть
    }

    LineRenderer ЛинияКонтура(Transform родитель, string имя, Vector3[] точки, float ширина)
    {
        var go = new GameObject(имя) { layer = Слой };
        go.transform.SetParent(родитель, false);
        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.loop = true;
        lr.positionCount = точки.Length;
        lr.SetPositions(точки);
        lr.widthMultiplier = ширина;
        lr.sharedMaterial = _матКонтура;
        lr.numCornerVertices = 2;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        lr.enabled = false;
        return lr;
    }

    // Неподпёртый провис — дефект для осмотра. Метка без меша: сам провис виден по
    // геометрии (труба над ямой), подсвечивать нечего.
    PipelineDefect МеткаПровиса(Пролёт п)
    {
        var go = new GameObject($"Провис_KP{п.KP_м:0}");
        go.layer = Слой;
        go.transform.SetParent(transform, false);
        go.transform.position = п.Середина;
        var d = go.AddComponent<PipelineDefect>();
        d.Тип = PipelineDefect.ТипДефекта.Провис;
        d.Описание = $"свободный пролёт {п.Длина_м:0} м, зазор до дна {п.Зазор_м:0.0} м";
        d.РадиусОбнаружения = Mathf.Max(3f, п.Длина_м * 0.5f);
        КонтурПровиса(d, п);
        return d;
    }

    // Для продолжения миссии: метка провиса в сохранённой точке (описание — от
    // ближайшего пролёта).
    public PipelineDefect МеткаПровиса(Vector3 место)
    {
        Пролёт лучший = new Пролёт { Середина = место };
        float мин = float.MaxValue;
        foreach (var п in Пролёты)
        {
            float d = (п.Середина - место).sqrMagnitude;
            if (d < мин) { мин = d; лучший = п; }
        }
        лучший.Середина = место;
        return МеткаПровиса(лучший);
    }

    // ── Доступ для построителя меша (МешТрубы) ─────────────────────────────────

    internal float Длина => _длина;
    internal float От => _от;
    internal float До => _до;
    internal IReadOnlyList<float> Стыки => _стыки;
    internal IReadOnlyList<Vector2> Аноды => _аноды;
    internal float РадиусСтали => ДиаметрСтали_м * 0.5f;
    internal float РадиусИзол => РадиусИзоляции;
    internal float Тайл => ТайлТекстуры_м;
    internal float ШагПрофиля => Шаг_м;
    internal void РамкаНа(float s, out Vector3 c, out Vector3 t, out Vector3 вверх, out Vector3 бок) => Рамка(s, out c, out t, out вверх, out бок);
    internal float ГрунтПодТрубой(float s) => ГрунтНа(s);

    // ── Снимки дефектов ─────────────────────────────────────────────────────────

#if UNITY_EDITOR
    // Отдельная камера с фонарём подлетает к каждому повреждению (с 1,2 м, сбоку-сверху)
    // и сохраняет кадр; плюс общий вид трубы на дне. Для проверки того, как это выглядит.
    System.Collections.IEnumerator СнятьДефекты()
    {
        yield return null;
        string папка = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", "Docs", "Снимки_дефектов"));
        if (System.IO.Directory.Exists(папка))
            foreach (var f in System.IO.Directory.GetFiles(папка, "*.png")) System.IO.File.Delete(f);
        System.IO.Directory.CreateDirectory(папка);

        var камGo = new GameObject("Камера снимков дефектов");
        var кам = камGo.AddComponent<Camera>();
        кам.fieldOfView = 55f; кам.nearClipPlane = 0.05f; кам.farClipPlane = 200f;
        кам.cullingMask = ~(1 << LayerMask.NameToLayer("UI"));
        кам.enabled = false;
        var свет = new GameObject("Фонарь").AddComponent<Light>();
        свет.type = LightType.Spot; свет.spotAngle = 70f; свет.range = 12f; свет.intensity = 3f;
        свет.transform.SetParent(камGo.transform, false);
        var rt = new RenderTexture(1280, 720, 24);
        var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);

        void Снять(string имя, Vector3 цель, Vector3 откуда)
        {
            камGo.transform.position = откуда;
            камGo.transform.LookAt(цель);
            кам.targetTexture = rt;
            кам.Render();
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(папка, имя + ".png"), tex.EncodeToPNG());
        }

        int n = 0;
        foreach (var п in Повреждения)
        {
            Vector3 точка = ТочкаНаТрубе(п.S, п.Угол, Радиус, out Vector3 наружу);
            Vector3 откуда = точка + (наружу + Vector3.up * 0.6f).normalized * 1.2f + _ось * 0.3f;
            if (п.Тип == PipelineDefect.ТипДефекта.ПостороннийПредмет) откуда = точка + (_поперёк * 1.6f + Vector3.up * 1.2f) + _ось * 1.2f;
            Снять($"{++n:00}_{п.Тип}_KP{п.S:0}", точка, откуда);
        }
        foreach (var п in Пролёты)
            if (!п.Подпёрт)
                Снять($"{++n:00}_Провис_KP{п.KP_м:0}", п.Середина, п.Середина + _поперёк * 4f + Vector3.up * 0.4f);
        Vector3 обзор = ЦентрНа(_длина * 0.5f);
        Снять($"{++n:00}_Общий_вид", обзор + _ось * 6f, обзор + _поперёк * 6f + Vector3.up * 3f - _ось * 4f);

        Destroy(камGo); rt.Release(); Destroy(rt); Destroy(tex);
        Debug.Log($"[Трубопровод] Снимки дефектов: {n} шт. в {папка}");
    }
#endif

    // ── Рельеф: высота дна ─────────────────────────────────────────────────────

    static Terrain РельефПод(Vector3 p)
    {
        foreach (var t in Terrain.activeTerrains)
            if (ВнутриРельефа(t, p)) return t;
        foreach (var t in FindObjectsByType<Terrain>(FindObjectsSortMode.None))
            if (ВнутриРельефа(t, p)) return t;
        return null;
    }

    static bool ВнутриРельефа(Terrain t, Vector3 p)
    {
        if (t == null || t.terrainData == null) return false;
        Vector3 o = t.GetPosition(), size = t.terrainData.size;
        return p.x >= o.x && p.x <= o.x + size.x && p.z >= o.z && p.z <= o.z + size.z;
    }

    static float ВысотаГрунта(Vector3 p)
    {
        var t = РельефПод(p);
        if (t != null) return t.SampleHeight(p) + t.GetPosition().y;
        if (Physics.Raycast(new Vector3(p.x, 500f, p.z), Vector3.down, out RaycastHit hit, 1000f, ~0, QueryTriggerInteraction.Ignore))
            return hit.point.y;
        return 0f;
    }

    void OnDrawGizmosSelected()
    {
        if (Построен) return;
        Gizmos.color = new Color(0.6f, 0.6f, 0.55f, 0.9f);
        Gizmos.DrawLine(Начало, Конец);
    }
}

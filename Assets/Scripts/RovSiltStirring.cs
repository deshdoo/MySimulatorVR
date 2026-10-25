using UnityEngine;
using UnityEngine.SceneManagement;

// Взмучивание грунта струёй движителей.
//
// Физика явления: струя от винта у дна разгоняет воду вдоль грунта; когда
// придонная скорость превышает порог срыва частиц ила/песка, они поднимаются
// во взвесь и образуют облако. Мелкая фракция оседает медленно (сантиметры в
// секунду), поэтому облако держится десятки секунд и сносится течением. Камера
// внутри облака «слепнет» — это одна из главных практических причин, по которой
// операторы держат аппарат не ниже ~0.5 м от дна.
//
// Модель (без CFD — упрощённо, но с правильными зависимостями):
//   интенсивность = f(высота) · f(тяга):
//     f(высота) — линейно растёт от 0 (на ВысотаНачала_м) до 1 (на ВысотаМакс_м):
//                 струя затухает с расстоянием, выше порога до дна не достаёт;
//     f(тяга)   — вертикальные движители бьют прямо в грунт (вес 1), маршевые/
//                 лаговые/курсовые — вскользь (ВесГоризонтальной).
//   Клубы рождаются на грунте под аппаратом и расползаются в основном ВДОЛЬ дна
//   (струя растекается по грунту), слегка поднимаясь; дальше их скорость
//   релаксирует к скорости воды (WaterCurrent) + оседание.
//
// Картинка — два слоя:
//   1) клубы-частицы: мягкая «облачная» текстура с шумом (4 варианта), полупрозрачное
//      смешивание, мягкое затухание у дна и у объектива (soft particles / camera fade),
//      медленное вращение и расплывание — видно, как облако ползёт и тает;
//   2) помутнение самой воды у камеры: RovSystems.turbidity → глобальный
//      _SiltTurbidity в шейдере тумана камеры аппарата (DepthFog): видимость падает
//      с расстоянием, прожектор подсвечивает взвесь.
//
// Мутность у камеры (0..1) = плотность клубов в сфере вокруг бортовой камеры
// (сглаженная). Её же читают сканер осмотра (в мути дефект не разглядеть) и
// разбор миссии (сколько секунд камера была в мути).
//
// Компонент ставит на аппарат RovController сам; настраивать ничего не надо.
[DisallowMultipleComponent]
public class RovSiltStirring : MonoBehaviour
{
    // Сколько клубов мути сейчас в воде (для счётчика нагрузки FrameHitchLogger).
    public static int ЖивыхКлубов { get; private set; }

    [Header("Когда поднимается муть")]
    [Tooltip("Выше этой высоты над дном струя до грунта не достаёт, м.")]
    public float ВысотаНачала_м = 0.8f;
    [Tooltip("На этой высоте и ниже взмучивание максимальное, м.")]
    public float ВысотаМакс_м = 0.2f;
    [Tooltip("Вклад горизонтальных движителей относительно вертикальных (струя идёт вскользь по дну).")]
    [Range(0f, 1f)] public float ВесГоризонтальной = 0.3f;
    [Tooltip("Клубов в секунду при максимальной тяге у самого дна.")]
    public float МаксКлубовВСекунду = 12f;
    [Tooltip("Над трубой, камнями и прочим не-грунтом — доля от взмучивания над грунтом: на бетоне " +
             "лишь тонкий налёт ила. Труба лежит на дне, и вдоль неё аппарат часто идёт низко над ней.")]
    [Range(0f, 1f)] public float НадТвёрдым = 0.1f;

    [Header("Облако")]
    [Tooltip("Скорость оседания ила, м/с (мелкий ил 0.005–0.03).")]
    public float СкоростьОседания = 0.02f;
    [Tooltip("За сколько секунд клуб теряет начальный разлёт и идёт с водой. Больше — шире расползается.")]
    public float ВремяРелаксации_с = 2.5f;
    public Vector2 ВремяЖизни_с = new Vector2(14f, 24f);
    public Vector2 Размер_м = new Vector2(0.8f, 1.6f);
    [Tooltip("Во сколько раз клуб расплывается к концу жизни.")]
    public float Расплывание = 2.6f;
    [Tooltip("Цвет ила (под цвет грунта). Альфа — плотность одного клуба: мало, плотность набирается наложением.")]
    public Color ЦветМути = new Color(0.60f, 0.53f, 0.40f, 0.20f);

    [Header("Помутнение воды у камеры")]
    [Tooltip("Радиус сферы вокруг камеры, в которой считается плотность мути, м.")]
    public float РадиусОценки_м = 2.0f;
    [Tooltip("Чувствительность: меньше — быстрее «слепнет».")]
    public float НасыщениеМутности = 1.8f;
    [Tooltip("За сколько секунд вода мутнеет, когда облако накрыло камеру.")]
    public float Помутнение_с = 1.0f;
    [Tooltip("За сколько секунд проясняется, когда облако ушло.")]
    public float Прояснение_с = 3.0f;
    [Tooltip("Цвет подсвеченной взвеси в шейдере тумана (обычно чуть светлее цвета ила).")]
    public Color ЦветВзвеси = new Color(0.55f, 0.50f, 0.38f);

    static readonly int SiltTurbidityID = Shader.PropertyToID("_SiltTurbidity");
    static readonly int SiltColorID = Shader.PropertyToID("_SiltColor");

    ParticleSystem _ps;
    ParticleSystem.Particle[] _буфер = new ParticleSystem.Particle[0];
    RovController _rov;
    Rigidbody _rb;
    Transform _камера;
    float _накоплено;   // дробная часть клубов между кадрами
    float _мутность;    // сглаженная

    void Start()
    {
        _rov = GetComponent<RovController>();
        _rb = GetComponent<Rigidbody>();
        СоздатьСистему();
    }

    void OnDestroy()
    {
        if (_ps != null) Destroy(_ps.gameObject);
        RovSystems.turbidity = 0f;
        Shader.SetGlobalFloat(SiltTurbidityID, 0f);
    }

    void СоздатьСистему()
    {
        // Отдельный объект в мире (не дочерний аппарату): облако остаётся там, где поднято.
        var go = new GameObject("МутьСоДна");
        // В сцену аппарата (Main), а не в активную (кабину): окно разбора замораживает
        // сцену Main целиком — вместе с ней должна вставать и муть.
        SceneManager.MoveGameObjectToScene(go, gameObject.scene);
        int слой = LayerMask.NameToLayer("Underwater");   // камера монитора видит только этот слой
        go.layer = слой >= 0 ? слой : gameObject.layer;

        _ps = go.AddComponent<ParticleSystem>();
        _ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = _ps.main;
        main.playOnAwake = false;
        main.loop = true;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 500;
        main.startSpeed = 0f;
        main.gravityModifier = 0f;
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        var em = _ps.emission; em.enabled = false;      // рождаем вручную через Emit
        var shape = _ps.shape; shape.enabled = false;

        // Медленное проявление → держится → долго тает.
        var col = _ps.colorOverLifetime; col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.12f),
                          new GradientAlphaKey(0.75f, 0.55f), new GradientAlphaKey(0f, 1f) });
        col.color = g;
        // Расплывание (диффузия облака): быстро в начале, потом медленнее.
        var sol = _ps.sizeOverLifetime; sol.enabled = true;
        var кривая = new AnimationCurve(new Keyframe(0f, 0.5f, 0f, 3f), new Keyframe(0.35f, 0.5f + 0.5f * (Расплывание - 0.5f)),
                                        new Keyframe(1f, Расплывание));
        sol.size = new ParticleSystem.MinMaxCurve(1f, кривая);
        // Лёгкое вращение клубов — облако «живое», а не набор наклеек.
        var rol = _ps.rotationOverLifetime; rol.enabled = true;
        rol.z = new ParticleSystem.MinMaxCurve(-0.25f, 0.25f);
        // 4 разных клуба в атласе 2×2 — без повторяющегося рисунка.
        var tsa = _ps.textureSheetAnimation; tsa.enabled = true;
        tsa.numTilesX = 2; tsa.numTilesY = 2;
        // Кадр задаётся нормированно (0..1): случайная константа на клуб = случайный из 4.
        tsa.frameOverTime = new ParticleSystem.MinMaxCurve(0f, 0.999f);

        var r = _ps.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Billboard;
        r.sortMode = ParticleSystemSortMode.Distance;
        r.material = СоздатьМатериал();
        _ps.Play();
    }

    // Материал: URP Particles/Unlit в прозрачном режиме с мягкими частицами (клуб
    // плавно тает там, где касается грунта, — без резкой линии пересечения) и
    // затуханием у камеры (клуб не лепится во весь объектив). Запасной вариант —
    // Sprites/Default (обычное альфа-смешивание, есть в любом билде).
    Material СоздатьМатериал()
    {
        var tex = АтласКлубов(128);
        var sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (sh != null)
        {
            var m = new Material(sh);
            m.SetFloat("_Surface", 1f);                       // Transparent
            m.SetFloat("_Blend", 0f);                         // Alpha
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;

            // Мягкие частицы: затухание в пределах 0.6 м от поверхности за клубом.
            m.SetFloat("_SoftParticlesEnabled", 1f);
            m.SetFloat("_SoftParticlesNearFadeDistance", 0f);
            m.SetFloat("_SoftParticlesFarFadeDistance", 0.6f);
            m.SetVector("_SoftParticleFadeParams", new Vector4(0f, 1f / 0.6f, 0f, 0f));
            m.EnableKeyword("_SOFTPARTICLES_ON");
            // Затухание у камеры: ближе 0.25 м — невидим, к 1.2 м — полностью.
            m.SetFloat("_CameraFadingEnabled", 1f);
            m.SetFloat("_CameraNearFadeDistance", 0.25f);
            m.SetFloat("_CameraFarFadeDistance", 1.2f);
            m.SetVector("_CameraFadeParams", new Vector4(0.25f, 1f / (1.2f - 0.25f), 0f, 0f));
            m.EnableKeyword("_FADING_ON");

            m.SetColor("_BaseColor", Color.white);
            m.SetTexture("_BaseMap", tex);
            return m;
        }
        var sp = Shader.Find("Sprites/Default");
        if (sp == null) return null;
        var ms = new Material(sp);
        ms.mainTexture = tex;
        return ms;
    }

    // Атлас 2×2 «облачных» клубов: мягкий радиальный спад × фрактальный шум
    // (несколько октав Перлина) — край рваный и пушистый, середина неоднородная.
    static Texture2D АтласКлубов(int клетка)
    {
        int n = клетка * 2;
        var t = new Texture2D(n, n, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[n * n];
        for (int k = 0; k < 4; k++)
        {
            int ox = (k % 2) * клетка, oy = (k / 2) * клетка;
            float сдвиг = 17.3f * (k + 1);
            for (int y = 0; y < клетка; y++)
                for (int x = 0; x < клетка; x++)
                {
                    float u = (x + 0.5f) / клетка * 2f - 1f, v = (y + 0.5f) / клетка * 2f - 1f;
                    float r = Mathf.Sqrt(u * u + v * v);
                    float шум = 0f, амп = 0.5f, част = 2.2f;
                    for (int o = 0; o < 4; o++)
                    {
                        шум += амп * Mathf.PerlinNoise(u * част + сдвиг, v * част + сдвиг * 0.7f);
                        амп *= 0.5f; част *= 2.1f;
                    }
                    // Край «откусывается» шумом — рваный контур вместо круга.
                    float край = Mathf.Clamp01(1f - (r + (шум - 0.45f) * 0.55f));
                    float a = Mathf.SmoothStep(0f, 1f, край) * Mathf.Lerp(0.55f, 1f, шум);
                    px[(oy + y) * n + ox + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a) * 255f));
                }
        }
        t.SetPixels32(px);
        t.Apply(true);
        return t;
    }

    void Update()
    {
        if (_ps == null) return;
        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        Испускать(dt);
        float цель = ПереноситьИОценить(dt);
        // Сглаживание: мутнеет быстрее, чем проясняется (облако уходит, взвесь ещё висит).
        float τ = цель > _мутность ? Помутнение_с : Прояснение_с;
        _мутность += (цель - _мутность) * (1f - Mathf.Exp(-dt / Mathf.Max(0.05f, τ)));
        RovSystems.turbidity = _мутность;
        Shader.SetGlobalFloat(SiltTurbidityID, _мутность);
        Shader.SetGlobalColor(SiltColorID, ЦветВзвеси);
    }

    void Испускать(float dt)
    {
        // Высота над дном — тем же фильтрованным лучом, что у эхолота (не цепляем свой кабель).
        if (!DvlJanus.ЛучДоДна(transform, _rb, transform.position, Vector3.down, ВысотаНачала_м + 0.5f, out float h, out Collider подНами)) return;
        float поВысоте = Mathf.Clamp01((ВысотаНачала_м - h) / Mathf.Max(0.01f, ВысотаНачала_м - ВысотаМакс_м));
        if (!(подНами is TerrainCollider)) поВысоте *= НадТвёрдым;   // под нами труба или камень, а не ил
        if (поВысоте <= 0f) return;

        float гориз = Mathf.Max(Mathf.Abs(DroneInput.forward), Mathf.Abs(DroneInput.lateral), Mathf.Abs(DroneInput.yaw));
        float тяга = Mathf.Clamp01(Mathf.Abs(DroneInput.vertical) + ВесГоризонтальной * гориз);
        float интенсивность = поВысоте * тяга;
        if (интенсивность <= 0.01f) return;

        _накоплено += МаксКлубовВСекунду * интенсивность * dt;
        int сколько = Mathf.FloorToInt(_накоплено);
        if (сколько <= 0) return;
        _накоплено -= сколько;

        Vector3 дно = transform.position + Vector3.down * h;
        var ep = new ParticleSystem.EmitParams();
        for (int i = 0; i < сколько; i++)
        {
            Vector2 круг = Random.insideUnitCircle;
            Vector3 вбок = new Vector3(круг.x, 0f, круг.y);
            ep.position = дно + вбок * 0.4f + Vector3.up * Random.Range(0.1f, 0.3f);
            // Струя растекается по грунту: разлёт в стороны больше подъёма.
            ep.velocity = (вбок.normalized * Random.Range(0.3f, 0.8f) + Vector3.up * Random.Range(0.05f, 0.3f))
                          * (0.4f + интенсивность);
            ep.startSize = Random.Range(Размер_м.x, Размер_м.y);
            ep.startLifetime = Random.Range(ВремяЖизни_с.x, ВремяЖизни_с.y);
            Color c = ЦветМути;
            float тон = Random.Range(0.85f, 1.1f);          // лёгкий разброс оттенка
            c.r *= тон; c.g *= тон; c.b *= тон;
            c.a *= Random.Range(0.6f, 1f);
            ep.startColor = c;
            _ps.Emit(ep, 1);
        }
    }

    // Скорость клубов релаксирует к скорости воды + оседание: облако дрейфует с
    // течением и медленно ложится на грунт. Заодно считаем плотность мути у
    // камеры (сумма вкладов клубов в сфере: ближе и плотнее — больше), сжатую в
    // 0..1 экспонентой насыщения.
    float ПереноситьИОценить(float dt)
    {
        int n = _ps.particleCount;
        ЖивыхКлубов = n;
        if (n == 0) return 0f;
        if (_буфер.Length < _ps.main.maxParticles) _буфер = new ParticleSystem.Particle[_ps.main.maxParticles];
        n = _ps.GetParticles(_буфер);

        if (_камера == null)
        {
            var sc = RovScanner.Instance;
            _камера = sc != null && sc.Камера != null ? sc.Камера : transform;
        }
        Vector3 cam = _камера.position;
        float R = Mathf.Max(0.1f, РадиусОценки_м), сумма = 0f;

        var поле = _rov != null ? _rov.Течение : null;
        bool течение = поле != null && поле.включено;
        float s = 1f - Mathf.Exp(-dt / Mathf.Max(0.05f, ВремяРелаксации_с));
        Vector3 оседание = Vector3.down * СкоростьОседания;
        for (int i = 0; i < n; i++)
        {
            Vector3 vВоды = (течение ? поле.Скорость(_буфер[i].position) : Vector3.zero) + оседание;
            _буфер[i].velocity += (vВоды - _буфер[i].velocity) * s;

            float d = Vector3.Distance(_буфер[i].position, cam);
            if (d < R)
            {
                float k = 1f - d / R;
                // Вклад растёт с размером клуба (расплылся — занимает больше воды).
                float размер = _буфер[i].GetCurrentSize(_ps);
                сумма += k * k * (_буфер[i].GetCurrentColor(_ps).a / 255f) * Mathf.Clamp(размер, 0.5f, 3f);
            }
        }
        _ps.SetParticles(_буфер, n);
        return 1f - Mathf.Exp(-сумма / Mathf.Max(0.1f, НасыщениеМутности));
    }
}

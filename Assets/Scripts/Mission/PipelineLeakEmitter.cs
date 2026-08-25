using UnityEngine;

// Пузыри утечки над сколом/дефектом трубопровода.
//
// Зачем: во-первых, оживляет статичную сцену; во-вторых (и это важнее для миссии),
// струйка пузырей — реалистичная ПОИСКОВАЯ ПОДСКАЗКА. Утечку газа из подводного
// трубопровода операторы ROV на практике ищут именно по поднимающимся пузырям —
// так что видимая струйка помогает найти дефект, а сам факт «обнаружения» всё равно
// ставит сканер (близость + линия визирования + свет + выдержка, см. RovScanner).
//
// Пузыри поднимаются в МИРОВОЙ верх независимо от ориентации трубы/скола (через
// velocityOverLifetime в мировом пространстве), слегка виляя (шум — имитация
// турбулентности воды), полупрозрачные, с бликом-ободком (мягкая процедурная
// текстура). ParticleSystem, материал и текстура строятся в коде — как и остальное
// в проекте (UmbilicalCable, спавнер), без ручных ассетов.
//
// Тип потока берётся из соседнего PipelineDefect: пузыри — для Утечки и Трещины;
// для Коррозии/Провиса эмиттер сам себя выключает (пузыри там неуместны).
//
// Настройка: вешается на объект дефекта. Обычно вручную трогать не нужно —
// PipelineDefectSpawner добавляет этот компонент на случайно созданные дефекты сам.
[DisallowMultipleComponent]
public class PipelineLeakEmitter : MonoBehaviour
{
    [Header("Когда пузырить")]
    [Tooltip("Только для дефектов типа Утечка/Трещина (для Коррозии/Провиса пузырей нет). " +
             "Если рядом нет PipelineDefect — пузыри включаются всегда.")]
    public bool ТолькоУтечкиИТрещины = true;
    [Tooltip("Пузырить только ПОСЛЕ обнаружения дефекта сканером. " +
             "Выкл (по умолчанию) — пузыри видны сразу и служат подсказкой, где искать течь.")]
    public bool ТолькоПослеОбнаружения = false;

    [Header("Поток пузырей")]
    [Tooltip("Пузырей в секунду.")]
    public float Интенсивность = 14f;
    [Tooltip("Скорость всплытия, м/с. Реальные мелкие пузыри поднимаются ~0.2-0.3 м/с.")]
    public float СкоростьВсплытия = 0.28f;
    [Range(0f, 1f)]
    [Tooltip("Разброс скорости всплытия, доля от средней.")]
    public float РазбросСкорости = 0.35f;
    [Tooltip("Диаметр пузыря, м — минимальный и максимальный.")]
    public float РазмерМин = 0.02f;
    public float РазмерМакс = 0.06f;
    [Tooltip("Сколько живёт пузырь, с (примерно = высота всплытия / скорость).")]
    public float ВремяЖизни = 5f;
    [Tooltip("Радиус зоны выхода пузырей у поверхности скола, м.")]
    public float РадиусИсточника = 0.03f;
    [Tooltip("Смещение источника наружу от поверхности трубы, м — чтобы пузыри рождались над сколом, а не внутри меша.")]
    public float СмещениеНаружу = 0.02f;
    [Tooltip("Виляние пузырей при всплытии (турбулентность воды). 0 — строго вверх.")]
    public float Виляние = 0.08f;
    [Tooltip("Цвет и прозрачность пузырей (альфа — общая непрозрачность потока).")]
    public Color ЦветПузырей = new Color(0.75f, 0.9f, 1f, 0.6f);

    [Header("Материал (необязательно)")]
    [Tooltip("Свой материал для пузырей. Пусто — построится прозрачный URP-материал с мягким кружком-бликом.")]
    public Material МатериалПузырей;

    // Общие для всех эмиттеров текстура и материал по умолчанию (строятся один раз).
    private static Texture2D _texКэш;
    private static Material _matКэш;

    private ParticleSystem _ps;
    private PipelineDefect _defect;
    private bool _пузырит;
    private bool _погашено;

    void Start()
    {
        _defect = GetComponentInChildren<PipelineDefect>();

        // Пузыри осмысленны только для течи/трещины. Коррозия и провис — без пузырей.
        if (ТолькоУтечкиИТрещины && _defect != null &&
            _defect.Тип != PipelineDefect.ТипДефекта.Утечка &&
            _defect.Тип != PipelineDefect.ТипДефекта.Трещина)
        {
            enabled = false;
            return;
        }

        Построить();

        // Если ждём обнаружения (и дефект реально есть) — стартуем выключенными.
        bool сразу = !(ТолькоПослеОбнаружения && _defect != null);
        Пузырить(сразу);
    }

    void Update()
    {
        if (_погашено) return;
        if (!ТолькоПослеОбнаружения || _defect == null) return;
        bool надо = _defect.Обнаружен;
        if (надо != _пузырит) Пузырить(надо);
    }

    void Пузырить(bool вкл)
    {
        _пузырит = вкл;
        if (_ps == null) return;
        var emission = _ps.emission;
        emission.enabled = вкл;
        if (вкл && !_ps.isPlaying) _ps.Play();
    }

    // Погасить утечку (закрыли вентиль): новые пузыри не рождаются, уже всплывающие
    // спокойно дожинают и исчезают сами.
    public void Погасить()
    {
        _погашено = true;
        Пузырить(false);
    }

    // Погасить ВСЕ утечки в сцене — вызывается при закрытии аварийного вентиля.
    public static void ПогаситьВсе()
    {
        foreach (var e in FindObjectsByType<PipelineLeakEmitter>(FindObjectsSortMode.None))
            e.Погасить();
    }

    // Создаёт дочерний объект с ParticleSystem и настраивает все модули под пузыри.
    void Построить()
    {
        var go = new GameObject("LeakBubbles");
        go.transform.SetParent(transform, false);
        // Наследуем слой дефекта: камера вида НПА (монитор через RenderTexture) обычно
        // рендерит только рабочий слой (Underwater). Новый GameObject иначе попал бы на
        // Default (0), и пузыри на мониторе были бы не видны. Спавнер уже посадил сам
        // дефект на слой трубы — берём его.
        go.layer = gameObject.layer;
        // forward у дефекта = внешняя нормаль (спавнер ориентирует его наружу),
        // поэтому смещение вперёд поднимает источник над поверхностью скола.
        go.transform.localPosition = Vector3.forward * СмещениеНаружу;
        go.transform.localRotation = Quaternion.identity;

        _ps = go.AddComponent<ParticleSystem>();
        _ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        float рМин = Mathf.Min(РазмерМин, РазмерМакс);
        float рМакс = Mathf.Max(РазмерМин, РазмерМакс);

        var main = _ps.main;
        main.loop = true;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;   // всплывают в мировой верх, не тянутся за трубой
        main.startLifetime = new ParticleSystem.MinMaxCurve(ВремяЖизни * 0.8f, ВремяЖизни);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0f, СкоростьВсплытия * 0.15f); // лёгкий «выхлоп» из скола; подъём — через velocityOverLifetime
        main.startSize = new ParticleSystem.MinMaxCurve(рМин, рМакс);
        main.startColor = ЦветПузырей;
        main.gravityModifier = 0f;
        main.maxParticles = Mathf.CeilToInt(Интенсивность * ВремяЖизни) + 16;

        var emission = _ps.emission;
        emission.rateOverTime = Интенсивность;

        // Маленькая сфера у поверхности скола — пузыри рождаются из точки, чуть расходясь.
        var shape = _ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = РадиусИсточника;
        shape.radiusThickness = 1f;

        // Всплытие — постоянная скорость вверх в МИРОВЫХ осях (терминальная скорость пузыря),
        // с разбросом по частицам. Ориентация трубы на направление подъёма не влияет.
        var vol = _ps.velocityOverLifetime;
        vol.enabled = true;
        vol.space = ParticleSystemSimulationSpace.World;
        // ВАЖНО: все три оси должны быть в ОДНОМ режиме MinMaxCurve, иначе Unity сыплет
        // «Particle Velocity curves must all be in the same mode». y задаём диапазоном
        // (TwoConstants), поэтому x и z тоже диапазоном (0,0), а не одиночным числом.
        vol.x = new ParticleSystem.MinMaxCurve(0f, 0f);
        vol.z = new ParticleSystem.MinMaxCurve(0f, 0f);
        vol.y = new ParticleSystem.MinMaxCurve(
            СкоростьВсплытия * (1f - РазбросСкорости),
            СкоростьВсплытия * (1f + РазбросСкорости));

        // Виляние — шум (турбулентность): пузырь покачивается вбок, всплывая.
        var noise = _ps.noise;
        noise.enabled = Виляние > 0.0001f;
        noise.strength = Виляние;
        noise.frequency = 0.4f;
        noise.scrollSpeed = 0.3f;
        noise.quality = ParticleSystemNoiseQuality.Medium;
        noise.damping = true;

        // Пузырь чуть подрастает по мере всплытия (падает давление) — от 0.7 к 1.1.
        var sol = _ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f, 0.7f), new Keyframe(1f, 1.1f)));

        // Плавно проявляется в начале и гаснет к концу жизни (умножается на startColor).
        var col = _ps.colorOverLifetime;
        col.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[]
            {
                new GradientAlphaKey(0f, 0f),
                new GradientAlphaKey(1f, 0.15f),
                new GradientAlphaKey(1f, 0.7f),
                new GradientAlphaKey(0f, 1f)
            });
        col.color = grad;

        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.alignment = ParticleSystemRenderSpace.View;   // корректно смотрит на камеру, в т.ч. в VR
        renderer.sortMode = ParticleSystemSortMode.Distance;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.material = МатериалПузырей != null ? МатериалПузырей : МатериалПоУмолчанию();
    }

    // Прозрачный URP-материал с процедурной текстурой мягкого пузыря. Кэшируется —
    // цвет каждого потока задаётся вершинным цветом частиц (startColor), поэтому
    // один материал годится для пузырей любого оттенка.
    static Material МатериалПоУмолчанию()
    {
        if (_matКэш != null) return _matКэш;   // в Unity уничтоженный объект == null — переживёт стоп Play Mode

        Shader sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (sh == null) sh = Shader.Find("Particles/Standard Unlit");
        if (sh == null) sh = Shader.Find("Sprites/Default");

        var m = new Material(sh) { name = "LeakBubble (auto)" };
        Texture2D tex = ТекстураПузыря();
        if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
        if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", tex);
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Color.white);

        // Настройка на прозрачность в стиле URP (свойства проверяем — их набор зависит от версии шейдера).
        if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f);          // Transparent
        if (m.HasProperty("_Blend")) m.SetFloat("_Blend", 0f);              // Alpha
        if (m.HasProperty("_SrcBlend")) m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        if (m.HasProperty("_DstBlend")) m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        if (m.HasProperty("_ZWrite")) m.SetFloat("_ZWrite", 0f);
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;

        _matКэш = m;
        return m;
    }

    // Мягкий кружок с ярким ободком-бликом — читается как прозрачный пузырёк.
    static Texture2D ТекстураПузыря()
    {
        if (_texКэш != null) return _texКэш;

        const int S = 64;
        var t = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        Vector2 c = new Vector2(S * 0.5f, S * 0.5f);
        float R = S * 0.5f;

        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), c) / R;   // 0 в центре .. 1 на краю
                float тело = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(1f - d)) * 0.45f;  // мягкая полупрозрачная заливка
                float ободок = Mathf.Exp(-38f * (d - 0.82f) * (d - 0.82f));            // светлое кольцо у края (блик)
                float alpha = Mathf.Clamp01(тело + ободок);
                t.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }

        t.Apply();
        _texКэш = t;
        return t;
    }
}

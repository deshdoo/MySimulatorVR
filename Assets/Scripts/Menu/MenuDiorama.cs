using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

// Подводная диорама главного меню.
//
// Оператор (XR-риг) переносится из кабины прямо на дно, рядом с трубопроводом
// миссии — используется та же подводная сцена Main (грунт, труба, свет), поэтому
// отдельная сцена меню не нужна. Сцена при этом заморожена (SceneFreeze), а вокруг
// строится «живое» окружение:
//   • туман цвета морской воды (RenderSettings.fog) + фон камеры того же цвета —
//     даль тонет в синеве, как под водой;
//   • лучи солнца сквозь толщу (god rays) — параллельные наклонные полосы с мягкими
//     концами (без видимых краёв), медленно дышат по яркости;
//   • взвесь вокруг оператора — медленно плывущие светлые точки;
//   • «призрак» аппарата — копия мешей настоящего НПА (без скриптов и физики),
//     с включёнными фарами медленно облетает трубу по эллипсу;
//   • «киношная» камера летит за призраком и рендерит в текстуру — живая картинка
//     миссии для карточки в меню;
//   • логотип над панелью. Звука в меню нет (только щелчки кнопок) — звук борта
//     включается вместе с миссией (RovAudio).
// Всё созданное удаляется в Убрать(), настройки рендера возвращаются как были.
public class MenuDiorama : MonoBehaviour
{
    public static readonly Color ЦветВоды = new Color(0.035f, 0.16f, 0.21f);

    public Vector3 Глаз { get; private set; }          // где стоит голова оператора
    public Vector3 Вперёд { get; private set; }        // куда он смотрит (горизонтально)
    public Vector3 ЦентрТрубы { get; private set; }
    public RenderTexture Кадр { get; private set; }    // снимок места работ для карточки меню
    int _кадровСнимка;

    readonly List<Object> _созданное = new List<Object>();
    readonly List<(Transform t, float фаза, Material m, float базА)> _лучи = new List<(Transform, float, Material, float)>();
    Transform _призрак, _логотип, _кино;
    Vector3 _осьТрубы, _поперёк, _осьЛуча;
    float _радиусОрбиты = 5f, _угол;
    Vector3 _логотипБаза;
    Transform _камера;

    // Сохранённые настройки рендера — вернуть при выходе из меню.
    bool _былТуман; Color _былЦветТумана; FogMode _былРежим; float _былаПлотность;
    CameraClearFlags _былФлаг; Color _былФон; float _былаДальность; int _былаМаска;
    Camera _cam;

    public void Построить(Transform камера, TMP_FontAsset шрифт)
    {
        _камера = камера;
        _cam = камера != null ? камера.GetComponent<Camera>() : null;
        ВыбратьТочку();
        НастроитьВоду();
        СоздатьЛучи();
        СоздатьВзвесь();
        СоздатьЛоготип(шрифт);
    }

    // Точка обзора: сбоку от середины трубы, на грунте, лицом к трубе.
    void ВыбратьТочку()
    {
        Vector3 a, b; float r;
        var спавнер = FindFirstObjectByType<PipelineDefectSpawner>();
        if (спавнер != null && спавнер.ГеометрияТрубы(out a, out b, out r))
        {
            ЦентрТрубы = (a + b) * 0.5f;
            _осьТрубы = (b - a).normalized;
            _радиусОрбиты = Mathf.Clamp((b - a).magnitude * 0.25f, 3f, 8f);
        }
        else
        {
            var rov = FindFirstObjectByType<RovController>();
            ЦентрТрубы = rov != null ? rov.transform.position + rov.transform.forward * 4f : Vector3.zero;
            _осьТрубы = Vector3.right;
        }
        _поперёк = Vector3.Cross(_осьТрубы, Vector3.up).normalized;
        if (_поперёк.sqrMagnitude < 0.1f) _поперёк = Vector3.forward;

        Vector3 ноги = ЦентрТрубы + _поперёк * 4.5f;
        ноги.y = Грунт(ноги, ЦентрТрубы.y);
        Глаз = ноги + Vector3.up * 1.6f;
        Vector3 f = ЦентрТрубы - ноги; f.y = 0f;
        Вперёд = f.sqrMagnitude > 0.01f ? f.normalized : Vector3.forward;
    }

    // Грунт под точкой: луч вниз начинается чуть выше трубы, а не с 30 м — иначе
    // он цеплялся за что-то над дном (поверхность, объёмы), и оператора ставило
    // в толщу воды, где кроме тумана ничего не видно. Берём ближайшее попадание
    // ниже уровня трубы, не триггер, не сам аппарат.
    static float Грунт(Vector3 p, float уровеньТрубы)
    {
        var hits = Physics.RaycastAll(new Vector3(p.x, уровеньТрубы + 3f, p.z), Vector3.down, 40f, ~0, QueryTriggerInteraction.Ignore);
        float лучший = float.NegativeInfinity;
        foreach (var h in hits)
        {
            if (h.collider.GetComponentInParent<RovController>() != null) continue;
            if (h.point.y > уровеньТрубы + 0.5f) continue;
            if (h.point.y > лучший) лучший = h.point.y;
        }
        return float.IsNegativeInfinity(лучший) ? уровеньТрубы - 0.5f : лучший;
    }

    void НастроитьВоду()
    {
        _былТуман = RenderSettings.fog; _былЦветТумана = RenderSettings.fogColor;
        _былРежим = RenderSettings.fogMode; _былаПлотность = RenderSettings.fogDensity;
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogColor = ЦветВоды;
        RenderSettings.fogDensity = 0.06f;
        if (_cam != null)
        {
            _былФлаг = _cam.clearFlags; _былФон = _cam.backgroundColor; _былаДальность = _cam.farClipPlane;
            // Слой Underwater (дно, труба) VR-камера кабины не рисует — и в меню это
            // оставлено намеренно: фон меню — «чистая» вода с лучами, без декораций.
            // Подводную сцену видит только камера снимка для карточки.
            _былаМаска = _cam.cullingMask;
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = ЦветВоды;
            _cam.farClipPlane = Mathf.Max(_cam.farClipPlane, 150f);   // кабинная камера может видеть недалеко
        }
    }

    // Лучи: все параллельны (солнце одно), наклонены на ~12°; каждый — вытянутая
    // полоса, повёрнутая к камере вокруг своей оси. Текстура гаснет к обоим
    // концам — ни верхний, ни нижний край не виден.
    void СоздатьЛучи()
    {
        var tex = Процедуры.ГрадиентЛуча(64, 256);
        _созданное.Add(tex);
        _осьЛуча = (Quaternion.AngleAxis(12f, _осьТрубы) * Vector3.up).normalized;
        for (int i = 0; i < 9; i++)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(go.GetComponent<Collider>());
            go.name = "ЛучСвета";
            В(go);
            float ширина = Random.Range(0.7f, 1.8f), высота = Random.Range(14f, 20f);
            go.transform.localScale = new Vector3(ширина, высота, 1f);
            // Вокруг оператора, но не вплотную (ближе 3 м луч «режет» обзор).
            Vector2 к = Random.insideUnitCircle.normalized * Random.Range(3f, 11f);
            Vector3 низ = new Vector3(Глаз.x + к.x, Глаз.y - 2f, Глаз.z + к.y);
            go.transform.position = низ + _осьЛуча * (высота * 0.5f);
            var m = Процедуры.Материал(true, tex);
            float a = Random.Range(0.05f, 0.1f);
            m.SetColor("_BaseColor", new Color(0.6f, 0.9f, 1f, a));
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = m;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _созданное.Add(m);
            _лучи.Add((go.transform, Random.value * 10f, m, a));
        }
    }

    void СоздатьВзвесь()
    {
        var go = new GameObject("ВзвесьМеню");
        В(go);
        go.transform.position = Глаз;
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = true; main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(6f, 10f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.02f, 0.08f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.015f, 0.04f);
        main.startColor = new Color(0.8f, 0.95f, 1f, 0.4f);
        main.maxParticles = 400;
        main.prewarm = true;
        var em = ps.emission; em.rateOverTime = 40f;
        var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(9f, 4f, 9f);
        var col = ps.colorOverLifetime; col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.25f), new GradientAlphaKey(1f, 0.75f), new GradientAlphaKey(0f, 1f) });
        col.color = g;
        var noise = ps.noise; noise.enabled = true; noise.strength = 0.05f; noise.frequency = 0.3f;
        var tex = Процедуры.Пятно(32);
        var m = Процедуры.Материал(true, tex);
        _созданное.Add(tex); _созданное.Add(m);
        ps.GetComponent<ParticleSystemRenderer>().sharedMaterial = m;
        ps.Play();
    }

    void СоздатьПризрак()
    {
        var rov = FindFirstObjectByType<RovController>();
        if (rov == null) return;
        _призрак = Призрак.Создать(rov.transform, gameObject.scene, true).transform;
        _созданное.Add(_призрак.gameObject);
    }

    // Снимок для карточки меню — ОДИН кадр, как постер: ракурс у вентиля вдоль
    // трубы, аппарат с фарами впереди, дымка пожиже, чем в меню. Камера рендерит
    // пару кадров и выключается — постоянной нагрузки нет.
    void СоздатьКиноКамеру()
    {
        Кадр = new RenderTexture(768, 432, 24) { name = "СнимокМеню" };
        Кадр.Create();
        _созданное.Add(Кадр);
        var go = new GameObject("КамераСнимка");
        В(go);
        var c = go.AddComponent<Camera>();
        c.targetTexture = Кадр;
        c.fieldOfView = 52f;
        c.nearClipPlane = 0.05f; c.farClipPlane = 120f;
        c.clearFlags = CameraClearFlags.SolidColor;
        c.backgroundColor = ЦветВоды;
        int ui = LayerMask.NameToLayer("UI");
        c.cullingMask = ~0;
        if (ui >= 0) c.cullingMask &= ~(1 << ui);
        c.depth = -50;
        _кино = go.transform;

        // Ракурс: от вентиля (если есть) назад и вбок, взгляд вдоль трубы.
        var вентиль = FindFirstObjectByType<InterventionTarget>();
        Vector3 опора = вентиль != null ? вентиль.transform.position : ЦентрТрубы;
        Vector3 поз = опора - _осьТрубы * 2.2f + _поперёк * 1.4f + Vector3.up * 0.6f;
        Vector3 цель = опора + _осьТрубы * 4f - Vector3.up * 0.2f;
        _кино.SetPositionAndRotation(поз, Quaternion.LookRotation(цель - поз, Vector3.up));
        // Аппарат — в кадре впереди, носом к камере, с включёнными фарами.
        if (_призрак != null)
        {
            Vector3 п = опора + _осьТрубы * 3.2f + _поперёк * 0.4f + Vector3.up * 1.0f;
            _призрак.SetPositionAndRotation(п, Quaternion.LookRotation(поз - п, Vector3.up));
        }
        _кадровСнимка = 0;
    }

    void СоздатьЛоготип(TMP_FontAsset шрифт)
    {
        var go = new GameObject("Логотип");
        В(go);
        int ui = LayerMask.NameToLayer("UI");
        if (ui >= 0) go.layer = ui;
        var t = go.AddComponent<TextMeshPro>();
        if (шрифт != null) t.font = шрифт;
        t.text = "<b>СИМУЛЯТОР ТНПА</b>\n<size=38%><color=#9FB4C4>тренажёр оператора подводного аппарата</color></size>";
        t.alignment = TextAlignmentOptions.Bottom;
        t.fontSize = 1.0f;   // ~0.1 м высота строки: заголовок не шире рамки меню
        t.color = new Color(0.45f, 0.9f, 1f);
        t.enableWordWrapping = false;
        t.rectTransform.pivot = new Vector2(0.5f, 0f);   // низ текста — у точки привязки (над рамкой меню)
        _логотип = go.transform;
    }

    // Логотип ставит меню — низом над верхним краем своей панели.
    public void ПоставитьЛоготип(Vector3 поз, Quaternion пов)
    {
        if (_логотип == null) return;
        _логотипБаза = поз;
        _логотип.SetPositionAndRotation(поз, пов);
    }

    void Update()
    {
        float t = Time.unscaledTime;

        // Лучи: поворот к камере вокруг СВОЕЙ оси (полоса всегда видна плашмя), дыхание яркости.
        foreach (var (tr, фаза, m, базА) in _лучи)
        {
            if (tr == null) continue;
            if (_камера != null)
            {
                Vector3 кКамере = Vector3.ProjectOnPlane(_камера.position - tr.position, _осьЛуча);
                if (кКамере.sqrMagnitude > 0.01f)
                    tr.rotation = Quaternion.LookRotation(-кКамере.normalized, _осьЛуча);
            }
            float a = базА * (0.55f + 0.45f * Mathf.PerlinNoise(t * 0.12f, фаза));
            m.SetColor("_BaseColor", new Color(0.6f, 0.9f, 1f, a));
        }

        // Снимок: камера живёт 3 кадра (с пожиже дымкой) и выключается.
        if (_кино != null && _кино.gameObject.activeSelf)
        {
            if (_кадровСнимка == 0) RenderSettings.fogDensity = 0.02f;
            if (++_кадровСнимка >= 3)
            {
                RenderSettings.fogDensity = 0.06f;
                _кино.gameObject.SetActive(false);
            }
            return;   // пока снимаем — призрак стоит в позе для кадра
        }

        // Призрак облетает трубу по эллипсу, нос — по касательной.
        if (_призрак != null)
        {
            _угол += Time.unscaledDeltaTime * 0.35f / Mathf.Max(1f, _радиусОрбиты);
            Vector3 p = ТочкаОрбиты(_угол), p2 = ТочкаОрбиты(_угол + 0.05f);
            _призрак.position = p;
            if ((p2 - p).sqrMagnitude > 1e-6f) _призрак.rotation = Quaternion.LookRotation(p2 - p, Vector3.up);

        }

        if (_логотип != null)
            _логотип.position = _логотипБаза + Vector3.up * Mathf.Sin(t * 0.8f) * 0.01f;
    }

    Vector3 ТочкаОрбиты(float у) => ЦентрТрубы + _осьТрубы * Mathf.Cos(у) * _радиусОрбиты
                                   + _поперёк * Mathf.Sin(у) * (_радиусОрбиты * 0.45f)
                                   + Vector3.up * (1.6f + Mathf.Sin(у * 2f) * 0.3f);

    void В(GameObject go)
    {
        SceneManager.MoveGameObjectToScene(go, gameObject.scene);
        go.transform.SetParent(transform, true);
    }

    public void Убрать()
    {
        RenderSettings.fog = _былТуман; RenderSettings.fogColor = _былЦветТумана;
        RenderSettings.fogMode = _былРежим; RenderSettings.fogDensity = _былаПлотность;
        if (_cam != null) { _cam.clearFlags = _былФлаг; _cam.backgroundColor = _былФон; _cam.farClipPlane = _былаДальность; _cam.cullingMask = _былаМаска; }
        if (_кино != null) _кино.GetComponent<Camera>().targetTexture = null;
        foreach (var o in _созданное)
        {
            if (o is RenderTexture rt) rt.Release();
            if (o != null) Destroy(o);
        }
        _созданное.Clear();
        _лучи.Clear();
        Destroy(gameObject);
    }
}

// Копия аппарата «для показа»: только меши и фары, без скриптов, коллайдеров и
// физики (Instantiate настоящего НПА запустил бы его Awake/синглтоны).
public static class Призрак
{
    public static GameObject Создать(Transform источник, Scene сцена, bool сФарами)
    {
        var корень = new GameObject("ПризракНПА");
        SceneManager.MoveGameObjectToScene(корень, сцена);
        корень.transform.SetPositionAndRotation(источник.position, источник.rotation);
        foreach (var mr in источник.GetComponentsInChildren<MeshRenderer>(true))
        {
            var mf = mr.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) continue;
            // Копируем только то, что у аппарата видно в игре: меню прячет его на время,
            // поэтому смотрим исходную видимость (иначе в копию попадали служебные
            // выключенные меши — розовый квадрат на модели).
            if (!MainMenu.БылВиден(mr)) continue;
            if (СломанныйМатериал(mr)) continue;
            var go = new GameObject(mr.name);
            go.layer = mr.gameObject.layer;
            go.transform.SetParent(корень.transform, false);
            go.transform.position = mr.transform.position;
            go.transform.rotation = mr.transform.rotation;
            go.transform.localScale = Делить(mr.transform.lossyScale, корень.transform.lossyScale);
            go.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterials = mr.sharedMaterials;
        }
        if (сФарами)
            foreach (var l in источник.GetComponentsInChildren<Light>(true))
            {
                if (l.type != LightType.Spot) continue;
                var go = new GameObject("Фара");
                go.transform.SetParent(корень.transform, false);
                go.transform.SetPositionAndRotation(l.transform.position, l.transform.rotation);
                var nl = go.AddComponent<Light>();
                nl.type = LightType.Spot; nl.color = l.color; nl.range = l.range;
                nl.spotAngle = l.spotAngle; nl.innerSpotAngle = l.innerSpotAngle;
                nl.intensity = Mathf.Max(l.intensity, 1f); nl.shadows = LightShadows.None;
            }
        return корень;
    }

    static Vector3 Делить(Vector3 a, Vector3 b) => new Vector3(a.x / b.x, a.y / b.y, a.z / b.z);

    // Материал без шейдера или с шейдером-ошибкой Unity рисует розовым — такие не копируем.
    public static bool СломанныйМатериал(Renderer r)
    {
        foreach (var m in r.sharedMaterials)
            if (m == null || m.shader == null || !m.shader.isSupported || m.shader.name.Contains("InternalErrorShader"))
                return true;
        return false;
    }

    // Переложить копию на заданный слой (вместе с детьми).
    public static void НаСлой(GameObject go, int слой)
    {
        foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = слой;
    }

    // Габарит (наибольший размер) для масштабирования мини-копии.
    public static float Габарит(GameObject корень)
    {
        var rs = корень.GetComponentsInChildren<Renderer>();
        if (rs.Length == 0) return 1f;
        var b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);
        return Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
    }
}

// Процедурные текстуры и материалы для эффектов меню (без файлов-ассетов).
public static class Процедуры
{
    // Материал частиц/эффектов: URP Particles/Unlit, прозрачный; аддитивный — для
    // света (лучи, точки взвеси светятся), обычный — для всего остального.
    public static Material Материал(bool аддитивный, Texture tex)
    {
        var sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        Material m;
        if (sh != null)
        {
            m = new Material(sh);
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", аддитивный ? 2f : 0f);
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)(аддитивный ? UnityEngine.Rendering.BlendMode.One : UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha));
            m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            m.SetTexture("_BaseMap", tex);
        }
        else
        {
            m = new Material(Shader.Find("Sprites/Default")) { mainTexture = tex };
        }
        return m;
    }

    // Непрозрачный освещаемый материал (макет повтора: рельеф дна).
    public static Material Освещаемый(Color цвет)
    {
        var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        var m = new Material(sh);
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", цвет);
        m.color = цвет;
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.15f);
        return m;
    }

    public static Texture2D Пятно(int n)
    {
        var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                float a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                px[y * n + x] = new Color32(255, 255, 255, (byte)(a * a * 255f));
            }
        t.SetPixels32(px); t.Apply();
        return t;
    }

    // Луч: плавно проявляется сверху, ярче всего в верхней трети и полностью гаснет
    // к низу; края по ширине мягкие. Ни один край полосы не читается.
    public static Texture2D ГрадиентЛуча(int w, int h)
    {
        var t = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float u = (x + 0.5f) / w * 2f - 1f, v = (y + 0.5f) / h;
                float бок = Mathf.SmoothStep(0f, 1f, 1f - Mathf.Abs(u));
                float верх = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1f, 0.8f, v));   // гаснет к самому верху
                float низ = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, 0.7f, v));    // и долго гаснет к низу
                float a = бок * бок * верх * низ;
                px[y * w + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        t.SetPixels32(px); t.Apply();
        return t;
    }
}

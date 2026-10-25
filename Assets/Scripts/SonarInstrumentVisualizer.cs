// =============================================================================
// SonarInstrumentVisualizer.cs
// -----------------------------------------------------------------------------
// F2 — учебная анимация гидролокатора кругового обзора НА ДАННЫХ РАБОЧЕГО ПРИБОРА
// (RovSonar). По образцу F3 (доплеровский лаг):
//   • АНИМАЦИЯ — показательная: звук замедлен (1500 м/с глазом не видны), один
//     «показательный» импульс раз в пару секунд — чтобы успеть рассмотреть.
//   • ЛУЧИ, ТОЧКИ ОТРАЖЕНИЯ И ЧИСЛА — настоящие: геометрия веера, где каждый луч
//     отразился, измеренная дальность и сила эха — из последнего импульса прибора.
//     Яркие столбики на дне — точки, из которых прибор за последний оборот собрал
//     картинку на мониторе (по одной, самой сильной, на каждый шаг антенны).
//
// Цикл показательного импульса:
//   1. антенна повёрнута на очередной шаг (1,8°);
//   2. веер звука уходит вперёд-вниз;
//   3. звук отражается: от металла сильно, от грунта слабо; за целью — тень;
//   4. эхо возвращается: дальность r = c·t/2, сила эха → яркость точки на экране.
//
// Клавиши: F2 — вкл/выкл, F6 — вид со стороны/с борта, F7 — пауза, F8 — медленнее.
// Все объекты — в слое камеры монитора (Underwater). Добавляется из RovController.Start.
// =============================================================================

using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
public class SonarInstrumentVisualizer : MonoBehaviour, IДемонстрацияПрибора
{
    // ---- учебный показ в окне «?» → «Работа приборов» (IДемонстрацияПрибора) ----
    public string Название => "Гидролокатор кругового обзора";
    public string Описание => "как из эха вращающейся головки строится картинка вокруг аппарата";
    public int Порядок => 20;
    public bool Показано => Показывать;
    public string Состояние => "скорость " +
                               (_множитель > 0.9f ? "1×" : _множитель > 0.4f ? "½×" : "¼×") + (_пауза ? " — пауза" : "");
    public void СменитьВид() { if (Показывать) ВидСбоку(!_видСбоку); }
    public void Пауза() => _пауза = !_пауза;
    public void СменитьСкорость() => _множитель = _множитель > 0.9f ? 0.5f : _множитель > 0.4f ? 0.25f : 1f;

    [Header("Клавиши")]
    public Key клавиша = Key.F2;
    public Key клавишаВида = Key.F6;
    public Key клавишаПаузы = Key.F7;
    public Key клавишаСкорости = Key.F8;
    public bool Показывать = false;

    [Header("Анимация (показательная)")]
    [Tooltip("Скорость звука НА АНИМАЦИИ, м/с. Реальная ~1500 м/с — глазом не увидеть.")]
    public float СкоростьЗвукаВизуально = 12f;
    [Tooltip("Пауза между показательными импульсами, с")]
    public float ПаузаМеждуИмпульсами_с = 0.8f;
    [Tooltip("Сколько точек картинки (шагов антенны) держать на дне — около оборота.")]
    public int ТочекКартинки = 200;

    [Header("Вид")]
    public float РазмерШрифтаПанели = 0.36f;
    [Range(0.2f, 0.6f)] public float ДоляШириныПанели = 0.36f;
    public float РазмерПодписей = 0.3f;

    float _время, _множитель = 1f; bool _пауза;
    RovSonar _прибор;
    int _слой;
    Transform _корень;
    Material _мат;
    TMP_FontAsset _шрифт;
    Camera _камМонитора, _камСбоку;
    bool _видСбоку, _кадрГотов;
    Vector3 _позКам; Quaternion _поворотКам;   // _позКам — смещение камеры от аппарата
    TextMeshPro _панель; Transform _подложка;
    readonly List<(TextMeshPro текст, Vector3 где)> _метки = new List<(TextMeshPro, Vector3)>();
    int _метокЗанято;
    float _следТекст;

    // показательный импульс — снимок последнего импульса прибора
    float _старт = -999f, _длинаИмпульса;
    Vector3 _источник; Vector3[] _лучи = new Vector3[0]; RovSonar.Отражение[] _отр = new RovSonar.Отражение[0];
    float _угол;
    LineRenderer[] _линииЛучей = new LineRenderer[0], _эхо = new LineRenderer[0], _вспышки = new LineRenderer[0];
    LineRenderer _фронт, _антенна;

    // точки картинки за последний оборот
    struct Точка { public Vector3 p; public float сила, время; }
    readonly Queue<Точка> _картинка = new Queue<Точка>();
    LineRenderer[] _столбики = new LineRenderer[0];
    float _последнийУгол = -1f;

    void Awake() { }
    void OnDisable() { ВидСбоку(false); }
    void OnDestroy() { ВидСбоку(false); if (_корень != null) Destroy(_корень.gameObject); }

    void Update()
    {
        var kb = Keyboard.current;
        if (kb != null)
        {
            if (клавиша != Key.None && kb[KeyBindings.K(клавиша)].wasPressedThisFrame) Переключить();
            if (Показывать)
            {
                if (клавишаВида != Key.None && kb[KeyBindings.K(клавишаВида)].wasPressedThisFrame) ВидСбоку(!_видСбоку);
                if (клавишаПаузы != Key.None && kb[KeyBindings.K(клавишаПаузы)].wasPressedThisFrame) _пауза = !_пауза;
                if (клавишаСкорости != Key.None && kb[KeyBindings.K(клавишаСкорости)].wasPressedThisFrame)
                    _множитель = _множитель > 0.9f ? 0.5f : _множитель > 0.4f ? 0.25f : 1f;
            }
        }
        if (_прибор == null) _прибор = GetComponent<RovSonar>();
        // Точки картинки копим всегда, пока включено — как прибор строит изображение.
        if (Показывать && _прибор != null) Копить();
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
        _старт = -999f; _кадрГотов = false; _следТекст = 0f; _картинка.Clear();
        ВидСбоку(Показывать);
        Debug.Log($"[Гидролокатор] анимация {(Показывать ? "ВКЛ" : "ВЫКЛ")}. F6 — вид, F7 — пауза, F8 — медленнее.");
    }

    void Копить()
    {
        if (Mathf.Approximately(_прибор.УголИмпульса_град, _последнийУгол)) return;
        _последнийУгол = _прибор.УголИмпульса_град;
        var отр = _прибор.ОтраженияИмпульса;
        int лучший = -1;
        for (int i = 0; i < отр.Length; i++) if (отр[i].есть && (лучший < 0 || отр[i].сила > отр[лучший].сила)) лучший = i;
        if (лучший >= 0) _картинка.Enqueue(new Точка { p = отр[лучший].точка, сила = отр[лучший].сила, время = Time.time });
        while (_картинка.Count > ТочекКартинки) _картинка.Dequeue();
    }

    // ================================================================== кадр
    void Кадр()
    {
        if (_прибор == null || _прибор.НаправленияЛучей.Length == 0)
        {
            ПоказатьПанель("<b>Гидролокатор не найден</b>\n<color=#aab6bf>На аппарате нет компонента RovSonar.</color>");
            return;
        }
        float c = Mathf.Max(1f, СкоростьЗвукаВизуально);
        float R = _прибор.Дальность_м;

        // Новый показательный импульс — снимок последнего настоящего импульса прибора.
        float t = _время - _старт;
        if (t > 2f * _длинаИмпульса / c + ПаузаМеждуИмпульсами_с)
        {
            _старт = _время; t = 0f;
            _источник = _прибор.ИсточникИмпульса; _угол = _прибор.УголИмпульса_град;
            _лучи = (Vector3[])_прибор.НаправленияЛучей.Clone();
            _отр = (RovSonar.Отражение[])_прибор.ОтраженияИмпульса.Clone();
            // Длина показательного импульса — до самого дальнего отражения (или до шкалы).
            _длинаИмпульса = 0f;
            for (int i = 0; i < _отр.Length; i++) _длинаИмпульса = Mathf.Max(_длинаИмпульса, _отр[i].есть ? Vector3.Distance(_источник, _отр[i].точка) : R);
            ГотовыЛинии(_лучи.Length);
        }
        float r = c * t;   // фронт импульса, м (в мире)

        // Веер лучей и фронт импульса.
        var фронт = new List<Vector3>();
        for (int i = 0; i < _лучи.Length; i++)
        {
            float D = _отр[i].есть ? Vector3.Distance(_источник, _отр[i].точка) : R;
            Линия(_линииЛучей[i], _источник, _источник + _лучи[i] * D, new Color(1f, 0.75f, 0.3f, _отр[i].есть ? 0.30f : 0.10f));
            if (r < D) фронт.Add(_источник + _лучи[i] * r);

            // Эхо возвращается к антенне: яркость — сила эха.
            float rЭхо = 2f * D - r;
            if (_отр[i].есть && r >= D && rЭхо > 0f)
            {
                Vector3 p = _источник + _лучи[i] * rЭхо;
                Линия(_эхо[i], p, p + _лучи[i] * Mathf.Min(1.2f, rЭхо), ЦветЭха(_отр[i].сила));
            }
            else _эхо[i].enabled = false;

            if (_отр[i].есть && r >= D)
            {
                float возраст = (r - D) / c;
                Окружность(_вспышки[i], _отр[i].точка, Vector3.up, 0.2f + возраст * 0.6f, ЦветЭха(_отр[i].сила) * new Color(1f, 1f, 1f, Mathf.Clamp01(1f - возраст)));
            }
            else _вспышки[i].enabled = false;
        }
        if (фронт.Count >= 2) { _фронт.enabled = true; _фронт.positionCount = фронт.Count; _фронт.SetPositions(фронт.ToArray()); _фронт.startColor = _фронт.endColor = new Color(0.85f, 0.95f, 1f, 1f); }
        else _фронт.enabled = false;

        // Текущее направление антенны (как зелёная линия на мониторе).
        Vector3 гор = Vector3.ProjectOnPlane(transform.rotation * (Quaternion.Euler(0f, _прибор.УголАнтенны_град, 0f) * Vector3.forward), Vector3.up).normalized;
        Линия(_антенна, _прибор.ИсточникИмпульса, _прибор.ИсточникИмпульса + гор * R, new Color(0.4f, 1f, 0.5f, 0.8f));

        // Картинка за оборот: столбики на дне, яркость — сила эха, старые блекнут.
        int k = 0; float оборот = Mathf.Max(1f, _прибор.ВремяОборота_с);
        foreach (var т in _картинка)
        {
            if (k >= _столбики.Length) break;
            float свеж = Mathf.Clamp01(1f - (Time.time - т.время) / оборот);
            var цв = ЦветЭха(т.сила); цв.a = 0.25f + 0.75f * свеж;
            Линия(_столбики[k], т.p, т.p + Vector3.up * (0.15f + 0.6f * т.сила), цв);
            k++;
        }
        for (; k < _столбики.Length; k++) _столбики[k].enabled = false;

        // Подписи.
        НачатьМетки();
        // Живая антенна (зелёная линия) крутится непрерывно — подпись у её конца с текущим углом.
        // Веер — снимок одного импульса (замедленный показ), у него свой угол.
        Метка(_прибор.ИсточникИмпульса + гор * Mathf.Min(R, 12f) + Vector3.up * 0.3f,
              "головка гидролокатора " + _прибор.УголАнтенны_град.ToString("F0") + "°", new Color(0.6f, 1f, 0.65f));
        Метка(_источник + Vector3.up * 0.5f, "показан импульс на " + _угол.ToString("F0") + "°", new Color(1f, 0.8f, 0.45f));
        int лучший = Лучший();
        if (лучший >= 0)
        {
            float D = Vector3.Distance(_источник, _отр[лучший].точка);
            if (r >= D) Метка(_отр[лучший].точка + Vector3.up * 0.7f, "эхо: " + _отр[лучший].что, ЦветЭха(_отр[лучший].сила));
            else if (фронт.Count > 0) Метка(фронт[фронт.Count / 2] + Vector3.up * 0.4f, "импульс", new Color(0.8f, 0.92f, 1f));
        }
        ЗакончитьМетки();

        if (Time.unscaledTime >= _следТекст) { _следТекст = Time.unscaledTime + 0.25f; Панель(t, c); }
    }

    int Лучший()
    {
        int л = -1;
        for (int i = 0; i < _отр.Length; i++) if (_отр[i].есть && (л < 0 || _отр[i].сила > _отр[л].сила)) л = i;
        return л;
    }

    static void Шаги(StringBuilder sb, int активный, params string[] шаги)
    {
        for (int i = 0; i < шаги.Length; i++)
            sb.Append(i == активный ? "<color=#ffd75e>" : "<color=#aab6bf>").Append(i + 1).Append(". ").Append(шаги[i]).Append("</color>\n");
    }

    void Панель(float t, float c)
    {
        int л = Лучший();
        float D = л >= 0 ? Vector3.Distance(_источник, _отр[л].точка) : _длинаИмпульса;
        int шаг = t < 0.25f ? 0 : t < D / c ? 1 : t < 2f * D / c ? 2 : 3;
        var п = _прибор;

        var sb = new StringBuilder(900);
        sb.Append("<size=120%><b>Что вокруг аппарата</b></size>\n");
        sb.Append("<color=#aab6bf>прибор: гидролокатор кругового обзора, 900 кГц</color>\n\n");
        Шаги(sb, шаг, "Мотор поворачивает головку гидролокатора на " + п.Шаг_град.ToString("F1") + "°",
            "Импульс звука веером вперёд-вниз",
            "Звук отражается: металл — сильно, грунт — слабо, за целью тень",
            "Эхо вернулось: дальность r = c·t/2, сила → яркость точки на экране");

        sb.Append("\nШкала ").Append(п.Дальность_м.ToString("F0")).Append(" м — шаг ")
          .Append((п.ВремяШага_с * 1000f).ToString("F0")).Append(" мс — оборот ").Append(п.ВремяОборота_с.ToString("F0")).Append(" с\n");
        sb.Append("<color=#aab6bf>Чем больше шкала — тем дольше ждать эхо и медленнее оборот</color>\n");

        int сЭхом = 0; for (int i = 0; i < _отр.Length; i++) if (_отр[i].есть) сЭхом++;
        sb.Append("\n<b>Результат</b> <color=#aab6bf>(рабочий прибор)</color>\n");
        sb.Append("Лучей с эхом: ").Append(сЭхом).Append(" из ").Append(_отр.Length).Append("\n");
        if (л >= 0)
            sb.Append("Самое сильное эхо: ").Append(_отр[л].что).Append(", ").Append(_отр[л].дальность.ToString("F1"))
              .Append(" м, сила ").Append((_отр[л].сила * 100f).ToString("F0")).Append("%\n");
        else sb.Append("<color=#f77>Эха нет — в секторе пусто до ").Append(п.Дальность_м.ToString("F0")).Append(" м</color>\n");

        float ош = (п.СкоростьЗвукаВПриборе_мс / Mathf.Max(1f, п.СкоростьЗвукаФакт_мс) - 1f) * 100f;
        sb.Append("Скорость звука: в воде ").Append(п.СкоростьЗвукаФакт_мс.ToString("F0")).Append(", в приборе ")
          .Append(п.СкоростьЗвукаВПриборе_мс.ToString("F0")).Append(" м/с");
        if (Mathf.Abs(ош) > 0.05f) sb.Append(" → дальности ").Append(ош > 0f ? "завышены" : "занижены").Append(" на ").Append(Mathf.Abs(ош).ToString("F1")).Append("%");
        sb.Append("\n");

        sb.Append("\n<color=#aab6bf>Оранжевый веер — луч прибора, белая дуга — импульс\n");
        sb.Append("Столбики на дне — точки картинки за оборот: выше и ярче — сильнее эхо\n");
        sb.Append("Зелёная линия — куда сейчас повёрнута головка гидролокатора (как на мониторе)\n");
        sb.Append("Звук замедлен для наглядности, числа — настоящие</color>\n");
        if (_пауза) sb.Append("<color=#ffd75e>пауза</color>");
        ПоказатьПанель(sb.ToString());
    }

    static Color ЦветЭха(float сила)
    {
        float v = Mathf.Clamp01(сила);
        return new Color(Mathf.Clamp01(0.4f + v), Mathf.Clamp01(0.15f + v * 0.85f), Mathf.Clamp01(v * 0.35f), 1f);
    }

    // =========================================================== камера и панель
    void ВидСбоку(bool вкл)
    {
        if (_камМонитора == null) _камМонитора = RovCameraRig.Бортовая();
        if (_камМонитора == null) { _видСбоку = false; return; }
        if (вкл && _камСбоку == null)
        {
            var go = new GameObject("Камера наблюдения (гидролокатор)");
            _камСбоку = go.AddComponent<Camera>();
            _камСбоку.CopyFrom(_камМонитора);
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
        var кам = _видСбоку && _камСбоку != null ? _камСбоку : _камМонитора;
        if (_панель == null || кам == null) return;

        float дист = кам.nearClipPlane + 0.02f;
        float полВыс = Mathf.Tan(кам.fieldOfView * 0.5f * Mathf.Deg2Rad);
        float полШир = полВыс * Mathf.Max(0.5f, кам.aspect);
        var tr = _панель.transform;
        tr.localScale = Vector3.one * дист;
        tr.position = кам.transform.position + (кам.transform.forward - кам.transform.right * полШир + кам.transform.up * полВыс) * дист;
        tr.rotation = кам.transform.rotation;
        _панель.fontSizeMax = РазмерШрифтаПанели; _панель.fontSizeMin = РазмерШрифтаПанели * 0.4f;
        float ширина = полШир * 2f * ДоляШириныПанели, высота = полВыс * 2f;
        _панель.rectTransform.sizeDelta = new Vector2(ширина, высота);
        float поле = РазмерШрифтаПанели * 0.06f;
        _панель.margin = new Vector4(поле, поле, поле, поле);
        if (_подложка != null)
        {
            _подложка.localPosition = new Vector3(ширина * 0.5f, -высота * 0.5f, 0.002f);
            _подложка.localScale = new Vector3(ширина, высота, 1f);
        }
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

    // Вид сверху-сзади с динамическим фокусом: камера идёт за аппаратом жёстко (без
    // отставания), а приближение подстраивается под то, что прибор реально видит —
    // охват берётся по точкам картинки и эхам текущего веера (80-й процентиль дальностей),
    // а не по шкале: при шкале 30 м и трубе в 6 м камера подойдёт к трубе, края карты
    // в кадр не попадут. На ходу камера чуть отдаляется и смотрит вперёд по скорости.
    float _охват = -1f;
    readonly List<float> _дальности = new List<float>(256);
    Rigidbody _тело;

    void ВестиКамеру()
    {
        float R = _прибор != null ? _прибор.Дальность_м : 20f;
        Vector3 о = transform.position;
        _дальности.Clear();
        foreach (var т in _картинка) _дальности.Add(Гор(т.p - о));
        for (int i = 0; i < _отр.Length; i++) if (_отр[i].есть) _дальности.Add(Гор(_отр[i].точка - о));
        float нужно = 5f;
        if (_дальности.Count > 0)
        {
            _дальности.Sort();
            нужно = _дальности[Mathf.Min(_дальности.Count - 1, (int)(_дальности.Count * 0.8f))] * 1.1f;
        }
        if (_тело == null) _тело = GetComponent<Rigidbody>();
        Vector3 v = _тело != null ? Vector3.ProjectOnPlane(_тело.linearVelocity, Vector3.up) : Vector3.zero;
        нужно = Mathf.Clamp(нужно + v.magnitude * 3f, 4f, Mathf.Min(R, 30f));
        _охват = _охват < 0f || !_кадрГотов ? нужно : Mathf.Lerp(_охват, нужно, 1f - Mathf.Exp(-1.2f * Time.deltaTime));

        Vector3 вперёд = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
        if (вперёд.sqrMagnitude < 1e-4f) вперёд = Vector3.forward;
        Vector3 вправо = Vector3.Cross(Vector3.up, вперёд);
        Vector3 откуда = (-вперёд * 0.45f + Vector3.up * 0.9f).normalized;   // круче сверху — меньше горизонта
        float дист = _охват * 1.05f + 2f;
        // Смещение камеры от аппарата (сглаживается поворот вида, сама привязка — жёсткая).
        Vector3 вперёдЦели = вперёд * _охват * 0.15f + v * 1.2f;
        Vector3 сдвигВлево = -вправо * _охват * 0.3f;   // слева панель — аппарат в правой части кадра
        Vector3 смещ = вперёдЦели + сдвигВлево + откуда * дист;
        Quaternion пов = Quaternion.LookRotation(вперёдЦели + сдвигВлево - смещ, Vector3.up);
        if (!_кадрГотов) { _позКам = смещ; _поворотКам = пов; _кадрГотов = true; }
        float s = 1f - Mathf.Exp(-3f * Time.deltaTime);
        _позКам = Vector3.Lerp(_позКам, смещ, s); _поворотКам = Quaternion.Slerp(_поворотКам, пов, s);
        _камСбоку.transform.SetPositionAndRotation(о + _позКам, _поворотКам);
    }

    static float Гор(Vector3 d) { d.y = 0f; return d.magnitude; }

    void ПоказатьПанель(string текст) { if (_панель != null) _панель.text = текст; }
    void НачатьМетки() { _метокЗанято = 0; }

    void Метка(Vector3 где, string текст, Color цвет)
    {
        if (_метокЗанято >= _метки.Count)
        {
            var go = new GameObject("Подпись"); go.layer = _слой; go.transform.SetParent(_корень, false);
            var tm = go.AddComponent<TextMeshPro>();
            if (_шрифт != null) tm.font = _шрифт;
            tm.alignment = TextAlignmentOptions.Left; tm.textWrappingMode = TextWrappingModes.NoWrap;
            tm.rectTransform.pivot = new Vector2(0f, 0.5f); tm.fontStyle = FontStyles.Bold;
            ПоверхСцены(tm.fontMaterial, 4000);
            _метки.Add((tm, где));
        }
        var м = _метки[_метокЗанято];
        м.текст.gameObject.SetActive(true); м.текст.text = текст; м.текст.color = цвет;
        _метки[_метокЗанято] = (м.текст, где);
        _метокЗанято++;
    }

    void ЗакончитьМетки() { for (int i = _метокЗанято; i < _метки.Count; i++) _метки[i].текст.gameObject.SetActive(false); }

    // ============================================================ построение
    void Построить()
    {
        _слой = LayerMask.NameToLayer("Underwater");
        if (_слой < 0) _слой = 0;
        var c = RovCameraRig.Бортовая();
        if (c != null && (c.cullingMask & (1 << _слой)) == 0)
            for (int b = 0; b < 32; b++) if ((c.cullingMask & (1 << b)) != 0) { _слой = b; break; }
        foreach (var t in FindObjectsByType<TMP_Text>(FindObjectsSortMode.None))
            if (t.font != null) { _шрифт = t.font; if (t.font.name.Contains("ofont")) break; }

        _корень = new GameObject("Гидролокатор (F2)").transform;
        _корень.gameObject.layer = _слой;
        _мат = new Material(Shader.Find("Sprites/Default")) { name = "SonarViz (auto)" };
        _фронт = НоваяЛиния("Фронт импульса", 0.06f);
        _антенна = НоваяЛиния("Направление антенны", 0.05f);
        _столбики = new LineRenderer[ТочекКартинки];
        for (int i = 0; i < _столбики.Length; i++) _столбики[i] = НоваяЛиния("Точка картинки", 0.12f);

        var go = new GameObject("Панель прибора"); go.layer = _слой; go.transform.SetParent(_корень, false);
        _панель = go.AddComponent<TextMeshPro>();
        if (_шрифт != null) _панель.font = _шрифт;
        _панель.alignment = TextAlignmentOptions.TopLeft;
        _панель.rectTransform.pivot = new Vector2(0f, 1f);
        _панель.textWrappingMode = TextWrappingModes.Normal;
        _панель.color = Color.white; _панель.lineSpacing = 8f; _панель.enableAutoSizing = true;
        _панель.GetComponent<MeshRenderer>().sortingOrder = 10;
        ПоверхСцены(_панель.fontMaterial, 4002);

        var фон = new GameObject("Подложка панели"); фон.layer = _слой; фон.transform.SetParent(go.transform, false);
        фон.AddComponent<MeshFilter>().sharedMesh = КвадратМеш();
        var mr = фон.AddComponent<MeshRenderer>();
        mr.sharedMaterial = new Material(_мат) { name = "SonarPanelBg (auto)" };
        mr.sharedMaterial.color = new Color(0.02f, 0.05f, 0.08f, 0.93f);
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false; mr.sortingOrder = 0;
        ПоверхСцены(mr.sharedMaterial, 4001);
        _подложка = фон.transform;
    }

    void ГотовыЛинии(int n)
    {
        if (_линииЛучей.Length == n) return;
        _линииЛучей = new LineRenderer[n]; _эхо = new LineRenderer[n]; _вспышки = new LineRenderer[n];
        for (int i = 0; i < n; i++)
        {
            _линииЛучей[i] = НоваяЛиния("Луч веера " + (i + 1), 0.025f);
            _эхо[i] = НоваяЛиния("Эхо " + (i + 1), 0.09f);
            _вспышки[i] = НоваяЛиния("Отражение " + (i + 1), 0.04f);
        }
    }

    static void ПоверхСцены(Material m, int очередь)
    {
        if (m == null) return;
        m.SetInt("unity_GUIZTestMode", (int)UnityEngine.Rendering.CompareFunction.Always);
        m.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
        m.renderQueue = очередь;
    }

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
        var go = new GameObject(имя); go.layer = _слой; go.transform.SetParent(_корень, false);
        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = true; lr.sharedMaterial = _мат; lr.widthMultiplier = толщина; lr.numCapVertices = 2;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; lr.receiveShadows = false; lr.enabled = false;
        return lr;
    }

    static void Линия(LineRenderer lr, Vector3 a, Vector3 b, Color c)
    {
        lr.enabled = true; lr.positionCount = 2; lr.SetPosition(0, a); lr.SetPosition(1, b);
        lr.startColor = lr.endColor = c;
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
}

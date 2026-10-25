using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using TMPro;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

// Окно разбора миссии в VR — компактная панель со страницами. Два режима:
//
//   ПРОМЕЖУТОЧНЫЕ ИТОГИ (F10 во время миссии): ход выполнения без оценки —
//     ставить балл незаконченной работе некорректно. Кнопки: «×» — продолжить,
//     «ЗАВЕРШИТЬ МИССИЮ» (с подтверждением) — досрочно закрыть миссию и
//     перейти к итоговому разбору.
//   ИТОГИ МИССИИ (после закрытия последней цели или досрочного завершения):
//     конечное окно — не закрывается, добавляется страница «Интегральная
//     оценка». Кнопки: «ЗАНОВО» (сразу новая миссия), «В МЕНЮ», «ВЫХОД».
//
// Пока окно открыто, подводная сцена заморожена (см. Заморозить): аппарат не
// дрейфует, время миссии и метрики не идут, мониторы застывают. Кабина и XR-риг
// живут как обычно — руки двигаются, кнопки окна жмутся.
//
// Страницы (без повторов: каждое число показано в одном месте):
//   1. Сводка               — этапы задания со временем, общее время, замечания;
//   2. Результаты осмотра   — схема трассы (покрытие + дефекты по KP), показатели
//                              качества осмотра, журнал дефектов;
//   3. Фотофиксация         — снимки бортовой камеры в момент выявления дефектов;
//   4. Пилотирование        — профиль глубины и показатели управления;
//   5. Интегральная оценка  — критерии с весами и итог (только в итоговом режиме).
//
// Как появляется в VR: World Space Canvas в ~0.9 м перед головой (камера
// XROrigin), повёрнут к оператору, мягко догоняет взгляд; рисуется поверх
// кабины. Кнопки — коллайдер + XRSimpleInteractable: палец (poke), луч с курком
// (T в эмуляторе), щипок. Клавиатура: F10 — окно, PageUp/PageDown — листать.
//
// Всё строится кодом; MissionBootstrap вешает компонент сам. Данные — MissionStats.
// Итоговый отчёт (.txt) и снимки (.png) сохраняются в persistentDataPath/Отчёты.
[DisallowMultipleComponent]
public class MissionDebriefPanel : MonoBehaviour
{
    [Header("Появление")]
    [Tooltip("Через сколько секунд после завершения миссии показать итоговое окно.")]
    public float ЗадержкаПоказа_с = 2.5f;
    public float Расстояние_м = 0.9f;
    [Tooltip("Сдвиг панели по высоте относительно глаз, м (минус — ниже).")]
    public float СмещениеПоВысоте_м = -0.05f;
    public float Ширина_м = 0.8f;
    [Tooltip("Если взгляд ушёл от панели дальше этого угла — панель плавно догоняет.")]
    public float УголДогонки_град = 40f;
    [Tooltip("Рисовать поверх геометрии кабины (иначе пульт может перекрыть панель).")]
    public bool ПоверхГеометрии = true;
    [Tooltip("Замораживать сцену, пока окно открыто.")]
    public bool ЗамораживатьСцену = true;
    public Key клавишаОкна = Key.F10;
    public Key клавишаДалее = Key.PageDown;
    public Key клавишаНазад = Key.PageUp;

    [Header("Вид")]
    [Tooltip("Шрифт. Пусто — возьмётся шрифт HUD миссии (MissionHudDisplay), иначе дефолтный TMP.")]
    public TMP_FontAsset шрифт;
    public Color ЦветФона = new Color(0.04f, 0.07f, 0.10f, 0.96f);
    public Color ЦветТекста = new Color(0.90f, 0.94f, 0.97f);
    public Color ЦветВторостепенный = new Color(0.52f, 0.60f, 0.68f);
    public Color ЦветАкцент = new Color(0.30f, 0.85f, 1.00f);
    public Color ЦветХорошо = new Color(0.35f, 0.95f, 0.50f);
    public Color ЦветСредне = new Color(1.00f, 0.78f, 0.25f);
    public Color ЦветПлохо = new Color(1.00f, 0.38f, 0.32f);

    [Header("Отчёт")]
    public bool СохранятьОтчёт = true;

    const float W = 1000f, H = 640f;
    enum Стр { Сводка, Осмотр, Фото, Пилотирование, Оценка }
    static readonly Dictionary<Стр, string> Названия = new Dictionary<Стр, string>
    {
        { Стр.Сводка, "Сводка" }, { Стр.Осмотр, "Результаты осмотра" }, { Стр.Фото, "Фотофиксация дефектов" },
        { Стр.Пилотирование, "Пилотирование" }, { Стр.Оценка, "Итоговая оценка" },
    };

    GameObject _корень;
    CanvasGroup _группа;
    Transform _голова;
    TMP_Text _заголовок, _надписьДалее, _надписьЗавершить;
    GameObject _кнопкаНазад, _кнопкаДалее;
    readonly List<Image> _точки = new List<Image>();
    class КнопкаОкна { public Image Фон, Обводка; public XRSimpleInteractable И; public Color Цвет; public float Уровень; }
    readonly List<КнопкаОкна> _кнопки = new List<КнопкаОкна>();
    readonly List<GameObject> _страницы = new List<GameObject>();
    readonly List<Стр> _порядок = new List<Стр>();
    Material _матUI;

    bool _итоговое;          // режим итогового (конечного) окна
    int _страница;
    bool _видно;
    float _альфа;
    float _показатьВ = -1f;
    bool _догоняем;
    float _следНажатие;
    float _подтверждениеДо = -1f;
    readonly SceneFreeze _заморозка = new SceneFreeze();
    string _путьОтчёта;
    // Итоговый отчёт (и состояние для «Продолжить») записан в архив — можно уходить в меню.
    public bool ОтчётСохранён => _путьОтчёта != null;
    public bool Видно => _видно;
    public bool Итоговое => _итоговое;   // конечное окно миссии — не закрывается

    static MissionStats S => MissionStats.Instance;

    void Start()
    {
        var m = MissionManager.Instance;
        if (m != null) m.МиссияЗавершена += НаЗавершение;
    }

    void OnDestroy() => Разморозить();

    void НаЗавершение()
    {
        S?.Заморозить();   // числа фиксируем до построения итогового окна
        // Завершили из окна (досрочно) — сразу перестраиваем его в итоговое;
        // иначе даём HUD пару секунд показать «выполнено».
        if (_видно) Показать();
        else _показатьВ = Time.unscaledTime + ЗадержкаПоказа_с;
    }

    void Update()
    {
        var kb = Keyboard.current;
        if (kb != null)
        {
            if (клавишаОкна != Key.None && kb[KeyBindings.K(клавишаОкна)].wasPressedThisFrame)
            {
                if (!_видно) Показать();
                else if (!_итоговое) Скрыть();   // итоговое окно конечное — не закрывается
            }
            if (_видно && клавишаДалее != Key.None && kb[KeyBindings.K(клавишаДалее)].wasPressedThisFrame) Далее();
            if (_видно && клавишаНазад != Key.None && kb[KeyBindings.K(клавишаНазад)].wasPressedThisFrame) Назад();
        }
        // С контроллера окно открывают из «Меню» (МенюНаЛадони: кнопка Menu / B) и там же
        // закрывают той же кнопкой. Листают кнопками ДАЛЕЕ / НАЗАД в самом окне.

        if (_показатьВ > 0f && Time.unscaledTime >= _показатьВ) { _показатьВ = -1f; Показать(); }

        if (_корень == null) return;

        _альфа = Mathf.MoveTowards(_альфа, _видно ? 1f : 0f, Time.unscaledDeltaTime / 0.3f);
        _группа.alpha = _альфа;
        _корень.transform.localScale = Vector3.one * (Ширина_м / W) * Mathf.Lerp(0.94f, 1f, Mathf.SmoothStep(0f, 1f, _альфа));
        if (!_видно && _альфа <= 0f) _корень.SetActive(false);
        if (_видно) Догонять();

        // Подсветка наведения — плавная, как в главном меню (VrPanel).
        foreach (var к in _кнопки)
        {
            if (к.Фон == null) continue;
            к.Уровень = Mathf.MoveTowards(к.Уровень, к.И.isHovered || НаведениеПальцем.Наведён(к.И) ? 1f : 0f, Time.unscaledDeltaTime / 0.1f);
            Покрасить(к);
        }

        if (_подтверждениеДо > 0f && Time.unscaledTime > _подтверждениеДо)
        {
            _подтверждениеДо = -1f;
            if (_надписьЗавершить != null) _надписьЗавершить.text = "<b>ЗАВЕРШИТЬ МИССИЮ</b>";
        }
    }

    // ---------------- Показ / режимы ----------------

    public void Показать()
    {
        var m = MissionManager.Instance;
        _итоговое = m != null && m.Завершена;

        if (_итоговое && СохранятьОтчёт && S != null && _путьОтчёта == null) СохранитьОтчёт();

        // Окно каждый раз строится заново: у режимов разный набор страниц и кнопок.
        bool былоВидно = _корень != null && _корень.activeSelf;
        Vector3 поз = былоВидно ? _корень.transform.position : Vector3.zero;
        Quaternion пов = былоВидно ? _корень.transform.rotation : Quaternion.identity;
        if (_корень != null) Destroy(_корень);
        _кнопки.Clear(); _точки.Clear(); _страницы.Clear(); _порядок.Clear();

        _порядок.AddRange(new[] { Стр.Сводка, Стр.Осмотр, Стр.Фото, Стр.Пилотирование });
        if (_итоговое) _порядок.Add(Стр.Оценка);

        Построить();
        _страница = 0;
        ПоказатьСтраницу();
        _корень.SetActive(true);
        if (былоВидно) { _корень.transform.SetPositionAndRotation(поз, пов); _альфа = 1f; }
        else Поставить(true);
        _видно = true;
        Заморозить();
    }

    public void Скрыть()
    {
        if (_итоговое) return;
        _видно = false;
        Разморозить();
    }

    void Далее()
    {
        if (_страница >= _порядок.Count - 1) { Скрыть(); return; }
        _страница++;
        ПоказатьСтраницу();
    }

    void Назад()
    {
        if (_страница <= 0) return;
        _страница--;
        ПоказатьСтраницу();
    }

    void ПоказатьСтраницу()
    {
        for (int i = 0; i < _страницы.Count; i++) _страницы[i].SetActive(i == _страница);
        for (int i = 0; i < _точки.Count; i++)
            _точки[i].color = i == _страница ? ЦветАкцент : new Color(1f, 1f, 1f, 0.18f);
        _заголовок.text = Названия[_порядок[_страница]];
        _кнопкаНазад.SetActive(_страница > 0);
        bool последняя = _страница == _порядок.Count - 1;
        // В итоговом окне с последней страницы «дальше» некуда; в промежуточном — вернуться к миссии.
        _кнопкаДалее.SetActive(!(последняя && _итоговое));
        _надписьДалее.text = последняя ? "<b>ПРОДОЛЖИТЬ</b>" : "<b>ДАЛЕЕ</b>";
    }

    void ЗавершитьМиссию()
    {
        // Двойное нажатие: первое — просьба подтвердить, второе в течение 3 с — завершение.
        if (_подтверждениеДо < 0f)
        {
            _подтверждениеДо = Time.unscaledTime + 3f;
            _надписьЗавершить.text = "<b>НАЖМИТЕ ЕЩЁ РАЗ</b>";
            return;
        }
        _подтверждениеДо = -1f;
        MissionManager.Instance?.ЗавершитьДосрочно();   // → НаЗавершение → итоговое окно
    }

    void НачатьЗаново()
    {
        Разморозить();
        MainMenu.ПропуститьМенюОдинРаз = true;   // сразу в миссию, без заставки
        SceneManager.LoadScene(0);   // Cabin; Main догрузит SceneLoader, симулятор сбросит состояние в Awake
    }

    void ВМеню()
    {
        Разморозить();
        SceneManager.LoadScene(0);   // перезагрузка → главное меню
    }

    void Выход()
    {
        Разморозить();
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // Заморозка подводной сцены (Main) — общий SceneFreeze: кабина с XR-ригом живёт,
    // руки двигаются и кнопки окна жмутся. Звук — на паузу.
    void Заморозить()
    {
        if (!ЗамораживатьСцену || _заморозка.Активна) return;
        _заморозка.Заморозить(gameObject.scene, this, _корень);
        AudioListener.pause = true;
    }

    void Разморозить()
    {
        if (!_заморозка.Активна) return;
        _заморозка.Разморозить();
        AudioListener.pause = false;
    }

    // ---------------- Размещение перед головой ----------------

    Transform Голова()
    {
        if (_голова != null && _голова.gameObject.activeInHierarchy) return _голова;
        // Камера VR-рига (кабина). Camera.main ненадёжна: в Main тоже есть камеры с тегом MainCamera.
        var origin = FindFirstObjectByType<XROrigin>();
        if (origin != null && origin.Camera != null) return _голова = origin.Camera.transform;
        foreach (var c in FindObjectsByType<Camera>(FindObjectsSortMode.None))
            if (c.targetTexture == null && c.stereoTargetEye != StereoTargetEyeMask.None && c.isActiveAndEnabled)
                return _голова = c.transform;
        return _голова = Camera.main != null ? Camera.main.transform : null;
    }

    void Поставить(bool мгновенно)
    {
        var г = Голова();
        if (г == null) return;
        Vector3 вперёд = Vector3.ProjectOnPlane(г.forward, Vector3.up);
        if (вперёд.sqrMagnitude < 1e-4f) вперёд = Vector3.ProjectOnPlane(г.up, Vector3.up);
        вперёд.Normalize();
        Vector3 p = г.position + вперёд * Расстояние_м + Vector3.up * СмещениеПоВысоте_м;
        Quaternion r = Quaternion.LookRotation(p - г.position, Vector3.up);
        if (мгновенно) { _корень.transform.SetPositionAndRotation(p, r); return; }
        float k = 1f - Mathf.Exp(-4f * Time.unscaledDeltaTime);
        _корень.transform.SetPositionAndRotation(Vector3.Lerp(_корень.transform.position, p, k),
                                                 Quaternion.Slerp(_корень.transform.rotation, r, k));
    }

    void Догонять()
    {
        var г = Голова();
        if (г == null) return;
        float угол = Vector3.Angle(Vector3.ProjectOnPlane(_корень.transform.position - г.position, Vector3.up),
                                   Vector3.ProjectOnPlane(г.forward, Vector3.up));
        if (угол > УголДогонки_град) _догоняем = true;
        if (_догоняем) { Поставить(false); if (угол < 3f) _догоняем = false; }
    }

    // ---------------- Построение ----------------

    void Построить()
    {
        if (шрифт == null)
        {
            var hud = FindFirstObjectByType<MissionHudDisplay>();
            if (hud != null) шрифт = hud.шрифт;
        }

        _корень = new GameObject("MissionDebrief", typeof(RectTransform));
        if (ПоверхГеометрии) КонтроллерыНадОкнами.Окно(_корень);
        var г = Голова();
        _корень.layer = ПодходящийСлой(г != null ? г.GetComponent<Camera>() : null);
        var canvas = _корень.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = 100;
        _корень.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 3f;
        _группа = _корень.AddComponent<CanvasGroup>();
        _группа.alpha = _альфа;
        var rt = (RectTransform)_корень.transform;
        rt.sizeDelta = new Vector2(W, H);
        rt.localScale = Vector3.one * (Ширина_м / W);
        УпорРук.Окно(rt);

        if (ПоверхГеометрии && _матUI == null)
        {
            // Трюк для world-space UI: шейдеры UI и TMP берут ZTest из unity_GUIZTestMode.
            _матUI = new Material(Canvas.GetDefaultCanvasMaterial());
            _матUI.SetInt("unity_GUIZTestMode", (int)CompareFunction.Always);
        }

        // Подложка: мягкая тень, скруглённый фон, кромка, полоса сверху (жёлтая — «миссия идёт»).
        const float размытие = 36f;
        UiStyle.Тень(Прямоугольник(rt, -размытие, -размытие + 10f, W + 2 * размытие, H + 2 * размытие, new Color(0f, 0f, 0f, 0.45f)), размытие);
        Прямоугольник(rt, 0, 0, W, H, ЦветФона, VrPanel.РадиусОкна);
        UiStyle.Обвести(Прямоугольник(rt, 0, 0, W, H, new Color(1f, 1f, 1f, 0.07f)), VrPanel.РадиусОкна, 1.5f);
        float отступ = VrPanel.РадиусОкна + 14f;
        Прямоугольник(rt, отступ, 0, W - 2 * отступ, 3, _итоговое ? ЦветАкцент : ЦветСредне, 1.5f);

        // Шапка
        var m = MissionManager.Instance;
        // Одна строка без переноса: длинная строка режима раньше заворачивалась на заголовок.
        // «Миссия не завершена» не пишем — это видно по жёлтой полосе и подсказке внизу.
        string режим = !_итоговое ? "ПРОМЕЖУТОЧНЫЕ ИТОГИ"
                     : (m != null && m.Досрочно) ? "ИТОГИ МИССИИ (досрочно)" : "ИТОГИ МИССИИ";
        // Ширина — до кнопок справа (у итогового окна их три, у промежуточного одна).
        float ширинаШапки = _итоговое ? 470f : 580f;
        var строкаРежима = Текст(rt, 36, 18, ширинаШапки, 24, 17, _итоговое ? ЦветВторостепенный : ЦветСредне);
        строкаРежима.enableWordWrapping = false;
        строкаРежима.overflowMode = TextOverflowModes.Ellipsis;
        строкаРежима.text = $"<cspace=4>{режим}</cspace>   {(m != null ? m.НазваниеМиссии : "Миссия")}";
        // Заголовок страницы в одну строку: «Фотофиксация дефектов» переносилась на разделитель.
        _заголовок = Текст(rt, 36, 44, ширинаШапки, 44, 34, ЦветТекста);
        _заголовок.fontStyle = FontStyles.Bold;
        _заголовок.enableWordWrapping = false;
        _заголовок.fontSizeMax = 34f; _заголовок.fontSizeMin = 24f;
        _заголовок.enableAutoSizing = true;

        if (_итоговое)
        {
            Кнопка(rt, W - 486, 26, 170, 50, "<b>ЗАНОВО</b>", ЦветАкцент, НачатьЗаново).fontSize = 22;
            Кнопка(rt, W - 306, 26, 150, 50, "<b>В МЕНЮ</b>", ЦветАкцент, ВМеню).fontSize = 22;
            Кнопка(rt, W - 146, 26, 110, 50, "<b>ВЫХОД</b>", ЦветВторостепенный, Выход).fontSize = 20;
        }
        else
        {
            _надписьЗавершить = Кнопка(rt, W - 376, 26, 270, 50, "<b>ЗАВЕРШИТЬ МИССИЮ</b>", ЦветПлохо, ЗавершитьМиссию);
            _надписьЗавершить.fontSize = 22;
            Кнопка(rt, W - 86, 26, 50, 50, "×", ЦветВторостепенный, Скрыть).fontSize = 34;
        }
        Прямоугольник(rt, 36, 100, W - 72, 1, new Color(1f, 1f, 1f, 0.08f));

        // Навигация
        _кнопкаНазад = Кнопка(rt, 36, H - 76, 200, 52, "<b>НАЗАД</b>", ЦветВторостепенный, Назад).transform.parent.gameObject;
        _надписьДалее = Кнопка(rt, W - 236, H - 76, 200, 52, "<b>ДАЛЕЕ</b>", ЦветАкцент, Далее);
        _кнопкаДалее = _надписьДалее.transform.parent.gameObject;
        float x0 = W / 2f - (_порядок.Count * 26f) / 2f;
        for (int i = 0; i < _порядок.Count; i++)
            _точки.Add(Прямоугольник(rt, x0 + i * 26f + 7f, H - 56, 12, 12, Color.white, 6f));

        foreach (var стр in _порядок)
        {
            var go = new GameObject(стр.ToString(), typeof(RectTransform));
            go.layer = _корень.layer;
            var prt = (RectTransform)go.transform;
            prt.SetParent(rt, false);
            Разместить(prt, 0, 0, W, H);
            _страницы.Add(go);
            if (S == null) { Текст(prt, 36, 130, W - 72, 40, 24, ЦветПлохо).text = "Нет данных: MissionStats не найден."; continue; }
            switch (стр)
            {
                case Стр.Сводка:        СтраницаСводка(prt, m, S); break;
                case Стр.Осмотр:        СтраницаОсмотр(prt, S); break;
                case Стр.Фото:          СтраницаФото(prt, S); break;
                case Стр.Пилотирование: СтраницаПилотирование(prt, S); break;
                case Стр.Оценка:        СтраницаОценка(prt, S); break;
            }
        }
    }

    // ---------------- Страницы ----------------

    void СтраницаСводка(RectTransform rt, MissionManager m, MissionStats s)
    {
        Подпись(rt, 36, 116, "ЭТАПЫ ЗАДАНИЯ");
        float y = 148;
        if (m != null)
        {
            for (int i = 0; i < m.Цели.Count && i < 4; i++)
            {
                var ц = m.Цели[i];
                Color цв; string статус;
                float длит = i < s.ДлительностиЦелей.Count ? s.ДлительностиЦелей[i] : -1f;
                switch (ц.Состояние)
                {
                    case MissionObjectiveState.Completed:
                        цв = ЦветХорошо;
                        статус = длит >= 0f ? MissionStats.ФорматВремени(длит) : "выполнен";
                        if (длит < 0f && ц.ВремяЗавершения_с >= 0f) статус = "выполнен в " + MissionStats.ФорматВремени(ц.ВремяЗавершения_с);
                        break;
                    case MissionObjectiveState.Active: цв = ЦветАкцент; статус = "в работе"; break;
                    case MissionObjectiveState.Failed: цв = ЦветПлохо; статус = "не выполнен"; break;
                    default:                           цв = ЦветВторостепенный; статус = "ожидает"; break;
                }
                Прямоугольник(rt, 36, y + 8, 10, 10, цв);
                Текст(rt, 60, y, 640, 30, 24, ЦветТекста).text = ц.Заголовок;
                Текст(rt, W - 336, y, 300, 30, 24, цв, TextAlignmentOptions.TopRight).text = статус;
                y += 40;
            }
        }
        float t = m != null ? m.ВремяМиссии_с : s.ВремяУчёта_с;
        Прямоугольник(rt, 36, y + 4, W - 72, 1, new Color(1f, 1f, 1f, 0.08f));
        Текст(rt, 60, y + 14, 600, 30, 24, ЦветВторостепенный).text = "Общее время";
        Текст(rt, W - 336, y + 14, 300, 30, 24, t <= s.НормативВремени_с ? ЦветТекста : ЦветСредне, TextAlignmentOptions.TopRight).text =
            $"<b>{MissionStats.ФорматВремени(t)}</b>  <size=75%><color=#{Hex(ЦветВторостепенный)}>норматив {MissionStats.ФорматВремени(s.НормативВремени_с)}</color></size>";

        y += 76;
        Подпись(rt, 36, y, "ЗАМЕЧАНИЯ");
        var z = s.Замечания();
        var sb = new StringBuilder();
        if (z.Count == 0) sb.Append($"<color=#{Hex(ЦветХорошо)}>•</color>  Замечаний нет.");
        for (int i = 0; i < z.Count && i < 5; i++)
            sb.Append($"<color=#{Hex(z[i].Уровень == 0 ? ЦветПлохо : ЦветСредне)}>•</color>  {z[i].Текст}\n");
        var зам = Текст(rt, 36, y + 32, W - 72, H - 90 - (y + 32), 22, ЦветТекста);
        зам.lineSpacing = 14f;
        зам.text = sb.ToString();

        if (!_итоговое)
            Текст(rt, 36, H - 118, W - 72, 26, 17, ЦветВторостепенный).text =
                "Интегральная оценка выставляется после завершения миссии.";
    }

    // Порядок нумерации дефектов на экране и в отчёте: найденные — по времени
    // обнаружения (первый осмотренный = «Трещина 1», второй = «Трещина 2», как на
    // снимках), ненайденные — следом. Возвращает индексы в s.Дефекты по порядку номеров.
    static List<int> ПоНомерам(MissionStats s)
    {
        var порядок = new List<int>();
        for (int i = 0; i < s.Дефекты.Count; i++) порядок.Add(i);
        порядок.Sort((a, b) =>
        {
            var да = s.Дефекты[a]; var дб = s.Дефекты[b];
            if (да.Обнаружен != дб.Обнаружен) return да.Обнаружен ? -1 : 1;
            return да.Обнаружен ? да.Время_с.CompareTo(дб.Время_с) : a.CompareTo(b);
        });
        return порядок;
    }

    // Без номера: номера в журнале, на схеме и под снимками только путали.
    static string Имя(MissionStats.ЗаписьДефекта д, int номер) => MissionStats.КороткаяМетка(д.Метка);

    void СтраницаОсмотр(RectTransform rt, MissionStats s)
    {
        if (s.ЕстьТруба && s.Покрытие != null)
        {
            // Схема: полоса = вся труба от начала до конца. Голубым — где аппарат
            // прошёл рядом со включённым светом; риски — дефекты (зелёный — найден,
            // красный — нет).
            const float x = 36f, y = 168f, w = W - 72f, h = 20f;
            Подпись(rt, 36, 114, "ТРУБА: что осмотрено и где дефекты");
            Прямоугольник(rt, x, y, w, h, new Color(1f, 1f, 1f, 0.08f));
            int n = s.Покрытие.Length;
            for (int i = 0; i < n;)
            {
                if (!s.Покрытие[i]) { i++; continue; }
                int j = i; while (j < n && s.Покрытие[j]) j++;   // склеиваем подряд идущие ячейки
                Прямоугольник(rt, x + w * i / n, y, w * (j - i) / n, h, Прозр(ЦветАкцент, 0.65f));
                i = j;
            }
            foreach (var д in s.Дефекты)
            {
                float px = x + w * Mathf.Clamp01(д.KP_м / Mathf.Max(0.01f, s.ДлинаТрубы_м));
                Прямоугольник(rt, px - 1.5f, y - 6, 3, h + 12, д.Обнаружен ? ЦветХорошо : ЦветПлохо);
            }
            Текст(rt, x, y + 26, 200, 22, 15, ЦветВторостепенный).text = "начало";
            Текст(rt, x + w - 200, y + 26, 200, 22, 15, ЦветВторостепенный, TextAlignmentOptions.TopRight).text = "конец";
            Прямоугольник(rt, x + w / 2f - 170, y + 30, 12, 12, Прозр(ЦветАкцент, 0.65f));
            Текст(rt, x + w / 2f - 152, y + 26, 150, 22, 15, ЦветВторостепенный).text = "осмотрено";
            Прямоугольник(rt, x + w / 2f - 30, y + 30, 12, 12, new Color(1f, 1f, 1f, 0.15f));
            Текст(rt, x + w / 2f - 12, y + 26, 200, 22, 15, ЦветВторостепенный).text = "не осмотрено";
        }
        else Текст(rt, 36, 130, W - 72, 30, 20, ЦветВторостепенный).text = "Схема недоступна: труба не найдена.";

        bool былУТрубы = s.ВремяУТрубы_с > 0f;
        const string неБыл = "не осматривал трубу ближе 4 м со светом";
        Плитка(rt, 36, 228, 300, 96, "Осмотрено трубы",
               s.ЕстьТруба ? MissionStats.Р($"{s.ДоляПокрытия * 100f:0} %") : "—",
               s.ЕстьТруба ? ЦветПоДоле(s.ДоляПокрытия) : ЦветВторостепенный,
               "рядом с трубой и со светом");
        Плитка(rt, 350, 228, 300, 96, "Скорость при осмотре",
               былУТрубы ? MissionStats.Р($"{s.СкоростьОсмотра_уз:0.00} уз") : "—",
               !былУТрубы ? ЦветВторостепенный : s.СкоростьОсмотра_уз <= s.МаксСкоростьОсмотра_уз ? ЦветХорошо : ЦветСредне,
               былУТрубы ? MissionStats.Р($"норма: не быстрее {s.МаксСкоростьОсмотра_уз:0.0} уз") : неБыл);
        Плитка(rt, 664, 228, 300, 96, "Расстояние до трубы",
               былУТрубы ? MissionStats.Р($"{s.СредняяДистанция_м:0.0} м") : "—",
               !былУТрубы ? ЦветВторостепенный : s.СредняяДистанция_м <= s.МаксДистанцияОсмотра_м ? ЦветХорошо : ЦветСредне,
               былУТрубы ? MissionStats.Р($"норма: не дальше {s.МаксДистанцияОсмотра_м:0.#} м") : неБыл);

        // Журнал дефектов. Шапка и строки — одно поле одной ширины, чтобы колонки <pos> совпадали.
        Подпись(rt, 36, 342, $"ЖУРНАЛ ДЕФЕКТОВ  —  найдено {s.ЧислоНайденных()} из {s.Дефекты.Count}");
        string вт = Hex(ЦветВторостепенный);
        var sb = new StringBuilder();
        sb.Append($"<size=75%><color=#{вт}>ДЕФЕКТ<pos=40%>ГЛУБИНА<pos=65%>НАЙДЕН</color></size>\n");
        if (s.Дефекты.Count == 0) sb.Append($"<color=#{вт}>Дефекты на трубе не размещены.</color>");
        var порядок = ПоНомерам(s);
        for (int k = 0; k < порядок.Count && k < 5; k++)
        {
            var д = s.Дефекты[порядок[k]];
            string цв = Hex(д.Обнаружен ? ЦветХорошо : ЦветПлохо);
            string глуб = д.Обнаружен ? MissionStats.Р($"{д.Глубина_м:0.0} м") : "—";
            string когда = д.Обнаружен ? "на " + MissionStats.ФорматВремени(д.Время_с) + " миссии" : "нет";
            sb.Append($"<color=#{цв}><b>{Имя(д, k + 1)}</b></color><pos=40%>{глуб}<pos=65%><color=#{цв}>{когда}</color>\n");
        }
        if (s.Дефекты.Count > 5) sb.Append($"<color=#{вт}>… ещё {s.Дефекты.Count - 5} — в текстовом отчёте</color>");
        var ж = Текст(rt, 36, 372, W - 72, 186, 22, ЦветТекста);
        ж.lineSpacing = 6f;
        ж.text = sb.ToString();
    }

    void СтраницаФото(RectTransform rt, MissionStats s)
    {
        // Порядок = порядок обнаружения, номер совпадает с журналом и схемой.
        var снимки = new List<(int номер, MissionStats.ЗаписьДефекта д)>();
        var порядок = ПоНомерам(s);
        for (int k = 0; k < порядок.Count; k++)
            if (s.Дефекты[порядок[k]].Снимок != null) снимки.Add((k + 1, s.Дефекты[порядок[k]]));

        if (снимки.Count == 0)
        {
            Текст(rt, 36, 130, W - 72, 60, 22, ЦветВторостепенный).text =
                "Снимков пока нет.\nКогда вы сфотографируете дефект красной кнопкой «ФОТО», сюда попадёт кадр с камеры аппарата.";
            return;
        }

        // Сетка 3×2.
        const float cw = 296f, gap = 20f;
        for (int k = 0; k < снимки.Count && k < 6; k++)
        {
            var (номер, д) = снимки[k];
            float x = 36 + (k % 3) * (cw + gap);
            float y = 118 + (k / 3) * 222;
            float ch = Mathf.Min(168f, cw * д.Снимок.height / Mathf.Max(1f, д.Снимок.width));

            var фото = Картинка(rt, x, y, cw, ch, д.Снимок);
            // Строка поверх кадра — как экранная индикация видео инспекции.
            Прямоугольник((RectTransform)фото.transform, 0, ch - 24, cw, 24, new Color(0f, 0f, 0f, 0.6f));
            Текст((RectTransform)фото.transform, 8, ch - 23, cw - 16, 22, 15, Color.white).text =
                MissionStats.Р($"глубина {д.Глубина_м:0.0} м") + "   " + MissionStats.ФорматВремени(д.Время_с);
            Текст(rt, x, y + ch + 6, cw, 24, 18, ЦветТекста).text =
                $"<b>{Имя(д, номер)}</b>";
            Текст(rt, x, y + ch + 28, cw, 22, 15, ЦветВторостепенный).text =
                $"снимок {д.ВремяСнимка:HH:mm:ss}";
        }
        if (снимки.Count > 6)
            Текст(rt, 36, H - 112, W - 72, 24, 16, ЦветВторостепенный).text = $"Ещё {снимки.Count - 6} снимков — в папке отчёта.";
    }

    void СтраницаПилотирование(RectTransform rt, MissionStats s)
    {
        // График «глубина во времени»: время вправо, вниз — глубже (как на эхограмме).
        // Масштаб по фактическому диапазону глубин (с запасом), а не от поверхности:
        // иначе при работе на 98 м линия прилипает к низу. На коротком отрезке
        // (меньше 20 с) график ни о чём не говорит — вместо него подсказка.
        const float x = 36f, y = 146f, w = W - 72f, h = 150f;
        Подпись(rt, 36, 114, "ГЛУБИНА ПО ВРЕМЕНИ");
        Прямоугольник(rt, x, y, w, h, new Color(1f, 1f, 1f, 0.04f), 8f);
        var p = s.ПрофильГлубины;
        float длит = Mathf.Max(0, p.Count - 1) * MissionStats.ШагПрофиля_с;
        if (длит >= 20f)
        {
            float мин = float.MaxValue, макс = float.MinValue;
            foreach (var v in p) { мин = Mathf.Min(мин, v); макс = Mathf.Max(макс, v); }
            float запас = Mathf.Max(0.5f, (макс - мин) * 0.15f);
            мин -= запас; макс += запас;
            int шаг = Mathf.Max(1, p.Count / 120);   // больше ~120 точек глаз не различит
            Vector2 Точка(int i) => new Vector2(x + w * i / (p.Count - 1f), y + h * Mathf.InverseLerp(мин, макс, p[i]));
            Vector2 пред = Точка(0);
            for (int i = шаг; i < p.Count; i += шаг) { var тек = Точка(i); Линия(rt, пред, тек, 3f, ЦветАкцент); пред = тек; }
            Линия(rt, пред, Точка(p.Count - 1), 3f, ЦветАкцент);

            foreach (var д in s.Дефекты)
                if (д.Обнаружен && д.Время_с >= 0f)
                    Прямоугольник(rt, x + w * Mathf.Clamp01(д.Время_с / длит) - 1f, y, 2, h, Прозр(ЦветХорошо, 0.6f));

            Текст(rt, x + 8, y + 4, 240, 20, 15, ЦветВторостепенный).text = MissionStats.Р($"{мин:0.0} м  (мельче)");
            Текст(rt, x + 8, y + h - 24, 240, 20, 15, ЦветВторостепенный).text = MissionStats.Р($"{макс:0.0} м  (глубже)");
            Текст(rt, x, y + h + 4, 100, 20, 15, ЦветВторостепенный).text = "00:00";
            Текст(rt, x + w - 100, y + h + 4, 100, 20, 15, ЦветВторостепенный, TextAlignmentOptions.TopRight).text = MissionStats.ФорматВремени(длит);
            Текст(rt, x + w - 420, 114, 420, 24, 15, ЦветВторостепенный, TextAlignmentOptions.TopRight).text =
                $"<color=#{Hex(ЦветХорошо)}>|</color>  момент, когда найден дефект";
        }
        else Текст(rt, x + 16, y + h / 2f - 14, w - 32, 28, 20, ЦветВторостепенный, TextAlignmentOptions.Top).text =
            "График появится после 20 секунд работы.";

        bool естьВысота = !float.IsInfinity(s.МинВысотаНадДном_м);
        Плитка(rt, 36, 330, 300, 96, "Пройдено аппаратом",
               s.Путь_м < 10f ? MissionStats.Р($"{s.Путь_м:0.0} м") : MissionStats.Р($"{s.Путь_м:0} м"), ЦветТекста,
               MissionStats.Р($"в среднем {s.СредняяСкорость_мс * 1.943844f:0.00} уз"));
        Плитка(rt, 350, 330, 300, 96, "Ближе всего ко дну",
               естьВысота ? MissionStats.Р($"{s.МинВысотаНадДном_м:0.0} м") : "—",
               !естьВысота ? ЦветВторостепенный : s.МинВысотаНадДном_м < s.МинБезопаснаяВысота_м ? ЦветСредне : ЦветХорошо,
               s.ВремяВМути_с >= 1f ? MissionStats.Р($"подняли муть: не видно {s.ВремяВМути_с:0} с")
                                    : MissionStats.Р($"ниже {s.МинБезопаснаяВысота_м:0.0} м винты поднимают муть"));
        Плитка(rt, 664, 330, 300, 96, "Раскачка аппарата",
               MissionStats.Р($"{s.СКОНаклона_град:0.0}°"),
               s.СКОНаклона_град <= s.СКОНаклонаОтлично_град ? ЦветХорошо : ЦветСредне,
               "насколько кренился и клевал носом");
        Плитка(rt, 36, 436, 300, 96, "Отошли от пульта",
               MissionStats.Р($"{s.МаксКабель_м:0} м"), ЦветТекста,
               "столько кабеля было вытравлено");
        Плитка(rt, 350, 436, 300, 96, "Израсходовано батареи",
               MissionStats.Р($"{s.РасходЗаряда_проц:0.0} %"),
               s.РасходЗаряда_проц <= s.НормаРасходаЗаряда_проц ? ЦветХорошо : ЦветСредне,
               MissionStats.Р($"норма: до {s.НормаРасходаЗаряда_проц:0} % за миссию"));
        Плитка(rt, 664, 436, 300, 96, "Столкновения",
               s.Удары.Count.ToString(), s.Удары.Count == 0 ? ЦветХорошо : ЦветСредне,
               s.ПолученоПовреждений > 0 ? $"повреждено систем: {s.ПолученоПовреждений}" : "без повреждений");
    }

    void СтраницаОценка(RectTransform rt, MissionStats s)
    {
        // Итоговая (в терминах ВКР — интегральная) оценка = сумма баллов по критериям.
        // У каждого критерия свой максимум — его вес: важное (выполнение, безопасность)
        // даёт больше баллов, второстепенное (расход батареи) — меньше.
        Текст(rt, 36, 112, W - 72, 24, 16, ЦветВторостепенный).text =
            "Каждый критерий даёт баллы до своего максимума — чем важнее критерий, тем выше максимум. Итог — сумма.";
        var к = s.Оценить();
        for (int i = 0; i < к.Count; i++)
        {
            float y = 146 + i * 56;
            float доля = к[i].Макс > 0f ? к[i].Балл / к[i].Макс : 0f;
            Текст(rt, 36, y, 700, 30, 23, ЦветТекста).text =
                $"{к[i].Название}  <size=72%><color=#{Hex(ЦветВторостепенный)}>{к[i].Пояснение}</color></size>";
            Текст(rt, W - 236, y, 200, 30, 23, ЦветТекста, TextAlignmentOptions.TopRight).text =
                $"<b>{к[i].Балл:0}</b><color=#{Hex(ЦветВторостепенный)}> из {к[i].Макс:0}</color>";
            Прямоугольник(rt, 36, y + 34, W - 72, 8, new Color(1f, 1f, 1f, 0.08f), 4f);
            Прямоугольник(rt, 36, y + 34, (W - 72) * Mathf.Clamp01(доля), 8, ЦветПоДоле(доля), 4f);
        }
        float процент = s.ИтогПроцент(out float сумма, out float макс);
        Color цв = ЦветПоДоле(процент / 100f);
        Прямоугольник(rt, 36, 494, W - 72, 1, new Color(1f, 1f, 1f, 0.08f));
        Текст(rt, 36, 504, 500, 36, 24, ЦветВторостепенный).text = "Итого";
        Текст(rt, W - 636, 500, 600, 40, 30, цв, TextAlignmentOptions.TopRight).text =
            $"<b>{сумма:0} из {макс:0}</b>  <size=80%>— оценка {MissionStats.Отметка(процент)}</size>";
        if (_путьОтчёта != null)
            Текст(rt, 36, 540, W - 72, 22, 14, ЦветВторостепенный).text = "Результат сохранён — его можно открыть в главном меню, раздел «Результаты».";
    }

    // ---------------- Строительные блоки ----------------

    void Подпись(RectTransform родитель, float x, float y, string текст) =>
        Текст(родитель, x, y, W - 72, 24, 17, ЦветВторостепенный).text = текст;

    // Плитка: подпись сверху, крупное значение, под ним строка-пояснение
    // («что это / какая норма»), чтобы число читалось без знания терминов.
    void Плитка(RectTransform родитель, float x, float y, float w, float h, string подпись, string значение, Color цвет,
                string пояснение = null)
    {
        Прямоугольник(родитель, x, y, w, h, new Color(1f, 1f, 1f, 0.045f), 8f);
        UiStyle.Обвести(Прямоугольник(родитель, x, y, w, h, new Color(1f, 1f, 1f, 0.07f)), 8f, 1.2f);
        Прямоугольник(родитель, x + 6, y + 10, 4, h - 20, цвет, 2f);   // цветная кромка = статус
        БезПереноса(Текст(родитель, x + 18, y + 8, w - 26, 22, 17, ЦветВторостепенный)).text = подпись;
        var v = БезПереноса(Текст(родитель, x + 18, y + 30, w - 26, 38, 30, цвет));
        v.fontStyle = FontStyles.Bold;
        v.text = значение;
        if (!string.IsNullOrEmpty(пояснение))
            БезПереноса(Текст(родитель, x + 18, y + h - 26, w - 26, 20, 14, ЦветВторостепенный)).text = пояснение;
    }

    static TMP_Text БезПереноса(TMP_Text t)
    {
        t.enableWordWrapping = false;
        t.overflowMode = TextOverflowModes.Ellipsis;
        return t;
    }

    TMP_Text Кнопка(RectTransform родитель, float x, float y, float w, float h, string надпись, Color цвет, Action действие)
    {
        float радиус = Mathf.Min(VrPanel.РадиусКнопки, Mathf.Min(w, h) * 0.3f);
        var фон = Прямоугольник(родитель, x, y, w, h, цвет, радиус);
        var t = Текст((RectTransform)фон.transform, 0, 0, w, h, 26, Color.white, TextAlignmentOptions.Center);
        t.text = надпись;
        var обводка = Прямоугольник((RectTransform)фон.transform, 0, 0, w, h, цвет);
        UiStyle.Обвести(обводка, радиус, 1.5f);

        // Коллайдер в единицах канваса: масштаб корня переведёт его в метры.
        var go = фон.gameObject;
        var box = go.AddComponent<BoxCollider>();
        // Тонкая зона у самой поверхности (2 мм перед окном, 8 мм за ним): палец жмёт там,
        // где рука-призрак упирается в окно (УпорРук), а не за сантиметры до него; лучу
        // толщина не важна.
        box.center = new Vector3(w / 2f, -h / 2f, 3f);   // пивот прямоугольника — верхний левый угол
        box.size = new Vector3(w, h, 10f);
        var кнопка = go.AddComponent<XRSimpleInteractable>();
        go.AddComponent<UnityEngine.XR.Interaction.Toolkit.Filtering.XRPokeFilter>();   // иначе палец не замечает (см. VrPanel)
        НаведениеПальцем.Добавить(кнопка);
        кнопка.selectEntered.AddListener(a => { Вибро.Импульс(a.interactorObject, Вибро.Кнопка); Нажать(действие); });
        // Касание пальцем жмёт сразу, как кнопки пульта (RovPokeButton.нажиматьПриКасании).
        кнопка.hoverEntered.AddListener(a => { if (a.interactorObject is XRPokeInteractor) { Вибро.Импульс(a.interactorObject, Вибро.Кнопка); Нажать(действие); } });
        var к = new КнопкаОкна { Фон = фон, Обводка = обводка, И = кнопка, Цвет = цвет };
        Покрасить(к);
        _кнопки.Add(к);
        return t;
    }

    static void Покрасить(КнопкаОкна к)
    {
        Color c = к.Цвет;
        к.Фон.color = Прозр(c, Mathf.Lerp(0.12f, 0.62f, к.Уровень));
        к.Обводка.color = Прозр(c, Mathf.Lerp(0.32f, 1f, к.Уровень));
    }

    // Антидребезг: касание и продавливание одним пальцем не должны сработать дважды.
    void Нажать(Action действие)
    {
        if (!_видно || Time.unscaledTime < _следНажатие) return;
        _следНажатие = Time.unscaledTime + 0.35f;
        действие();
    }

    Image Прямоугольник(RectTransform родитель, float x, float y, float w, float h, Color цвет, float радиус = 0f)
    {
        var go = new GameObject("Rect", typeof(RectTransform));
        go.layer = _корень.layer;
        var rt = (RectTransform)go.transform;
        rt.SetParent(родитель, false);
        Разместить(rt, x, y, w, h);
        var img = go.AddComponent<Image>();
        img.color = цвет;
        img.raycastTarget = false;
        if (_матUI != null) img.material = _матUI;
        UiStyle.Скруглить(img, радиус);
        return img;
    }

    RawImage Картинка(RectTransform родитель, float x, float y, float w, float h, Texture tex)
    {
        var go = new GameObject("Photo", typeof(RectTransform));
        go.layer = _корень.layer;
        var rt = (RectTransform)go.transform;
        rt.SetParent(родитель, false);
        Разместить(rt, x, y, w, h);
        var img = go.AddComponent<RawImage>();
        img.texture = tex;
        img.raycastTarget = false;
        if (_матUI != null) img.material = _матUI;
        return img;
    }

    // Отрезок из точки a в b (координаты от верхнего левого угла, y вниз).
    void Линия(RectTransform родитель, Vector2 a, Vector2 b, float толщина, Color цвет)
    {
        var rt = Прямоугольник(родитель, a.x, a.y, 1, толщина, цвет).rectTransform;
        Vector2 d = new Vector2(b.x - a.x, -(b.y - a.y));
        rt.pivot = new Vector2(0f, 0.5f);
        rt.anchoredPosition = new Vector2(a.x, -a.y);
        rt.sizeDelta = new Vector2(d.magnitude + толщина * 0.5f, толщина);
        rt.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
    }

    TMP_Text Текст(RectTransform родитель, float x, float y, float w, float h, float кегль, Color цвет,
                   TextAlignmentOptions выравн = TextAlignmentOptions.TopLeft)
    {
        var go = new GameObject("Text", typeof(RectTransform));
        go.layer = _корень.layer;
        var rt = (RectTransform)go.transform;
        rt.SetParent(родитель, false);
        Разместить(rt, x, y, w, h);
        var t = go.AddComponent<TextMeshProUGUI>();
        if (шрифт != null) t.font = шрифт;
        t.fontSize = кегль;
        t.color = цвет;
        t.alignment = выравн;
        t.enableWordWrapping = true;
        t.overflowMode = TextOverflowModes.Overflow;
        t.raycastTarget = false;
        // fontMaterial — копия материала шрифта для этого поля; поднимаем ZTest там.
        if (ПоверхГеометрии) t.fontMaterial.SetFloat("unity_GUIZTestMode", (float)CompareFunction.Always);
        return t;
    }

    // Позиции задаём от верхнего левого угла родителя (x вправо, y вниз).
    static void Разместить(RectTransform rt, float x, float y, float w, float h)
    {
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(x, -y);
        rt.sizeDelta = new Vector2(w, h);
    }

    static int ПодходящийСлой(Camera cam)
    {
        int ui = LayerMask.NameToLayer("UI");
        if (cam == null) return 0;
        if (ui >= 0 && (cam.cullingMask & (1 << ui)) != 0) return ui;
        return 0;
    }

    // ---------------- Текстовый отчёт + снимки ----------------

    void СохранитьОтчёт()
    {
        var m = MissionManager.Instance;
        var s = S;
        if (s == null) return;
        try
        {
            string Ч(FormattableString f) => MissionStats.Р(f);

            float процент = s.ИтогПроцент(out float сумма, out float макс);
            var sb = new StringBuilder();
            sb.AppendLine($"ИТОГИ МИССИИ: {(m != null ? m.НазваниеМиссии : "—")}{(m != null && m.Досрочно ? " (завершена досрочно)" : "")}");
            sb.AppendLine($"Дата: {DateTime.Now:dd.MM.yyyy HH:mm}");
            sb.AppendLine($"Время миссии: {MissionStats.ФорматВремени(m != null ? m.ВремяМиссии_с : s.ВремяУчёта_с)}");
            sb.AppendLine($"Интегральная оценка: {сумма:0} из {макс:0} — {MissionStats.Отметка(процент)}");
            sb.AppendLine();
            sb.AppendLine("Критерии:");
            foreach (var к in s.Оценить()) sb.AppendLine(Ч($"  {к.Название}: {к.Балл:0.0} из {к.Макс:0} ({к.Пояснение})"));
            sb.AppendLine();
            sb.AppendLine("Замечания:");
            var z = s.Замечания();
            if (z.Count == 0) sb.AppendLine("  нет");
            foreach (var з in z) sb.AppendLine($"  [{(з.Уровень == 0 ? "!" : "-")}] {з.Текст}");
            sb.AppendLine();
            sb.AppendLine("Осмотр:");
            if (s.ЕстьТруба) sb.AppendLine(Ч($"  Длина трассы: {s.ДлинаТрубы_м:0.0} м; покрытие: {s.ДоляПокрытия * 100f:0} %"));
            sb.AppendLine(Ч($"  Время у трубы: {s.ВремяУТрубы_с:0} с; скорость осмотра: {s.СкоростьОсмотра_уз:0.00} уз; дистанция: {s.СредняяДистанция_м:0.0} м"));
            sb.AppendLine();
            sb.AppendLine("Пилотирование:");
            sb.AppendLine(Ч($"  Путь: {s.Путь_м:0.0} м; средняя скорость: {s.СредняяСкорость_мс * 1.943844f:0.00} уз"));
            sb.AppendLine(Ч($"  Глубина: макс. {s.МаксГлубина_м:0.0} м"));
            if (!float.IsInfinity(s.МинВысотаНадДном_м)) sb.AppendLine(Ч($"  Мин. высота над дном: {s.МинВысотаНадДном_м:0.0} м"));
            sb.AppendLine(Ч($"  СКО наклона: {s.СКОНаклона_град:0.0}°; макс. наклон: {s.МаксНаклон_град:0}°"));
            sb.AppendLine(Ч($"  Макс. длина кабеля: {s.МаксКабель_м:0.0} м; расход АКБ: {s.РасходЗаряда_проц:0.0} %"));
            sb.AppendLine(Ч($"  Макс. температура движителей: {s.МаксТемпература_С:0} °C"));
            sb.AppendLine($"  Доля времени со светом / на автопилоте: {Доля(s.ВремяСоСветом_с, s.ВремяУчёта_с)} % / {Доля(s.ВремяНаАвтопилоте_с, s.ВремяУчёта_с)} %");
            sb.AppendLine($"  Получено повреждений (уровней): {s.ПолученоПовреждений}");
            sb.AppendLine(Ч($"  Время в мути (поднята движителями со дна): {s.ВремяВМути_с:0} с"));
            sb.AppendLine();
            sb.AppendLine($"Столкновения ({s.Удары.Count}):");
            foreach (var у in s.Удары) sb.AppendLine(Ч($"  {MissionStats.ФорматВремени(у.Время_с)}  {у.Энергия_Дж:0} Дж — {у.Объект}"));
            sb.AppendLine();
            sb.AppendLine($"Журнал дефектов (выявлено {s.ЧислоНайденных()} из {s.Дефекты.Count}):");
            var порядок = ПоНомерам(s);
            for (int k = 0; k < порядок.Count; k++)
            {
                int i = порядок[k];
                var д = s.Дефекты[i];
                string kp = д.KP_м >= 0f ? MissionStats.ФорматKP(д.KP_м) : "KP —";
                string фото = д.Снимок != null ? $"; снимок {д.ВремяСнимка:HH:mm:ss}, файл снимок_{k + 1}.jpg" : "";
                sb.AppendLine(д.Обнаружен
                    ? Ч($"  {k + 1}. [+] {д.Метка}; {kp}; выявлен {MissionStats.ФорматВремени(д.Время_с)}; глубина {д.Глубина_м:0.0} м{фото}")
                    : $"  {k + 1}. [-] {д.Метка}; {kp}; НЕ ВЫЯВЛЕН");
            }

            // Всё — в архив миссий (итоги.json + отчёт.txt + снимки + повтор): его
            // показывает главное меню «Результаты», там же — удаление.
            var повтор = MissionReplayRecorder.Instance != null ? MissionReplayRecorder.Instance.ВБайты() : null;
            // Продолжение сохранённой миссии — результат заменяет исходную запись.
            _путьОтчёта = MissionArchive.Сохранить(m, s, повтор, sb.ToString(), MissionResume.ИдЗаменить);
            MissionResume.Сбросить();
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[Разбор] Не удалось сохранить отчёт: {e.Message}");
        }
    }

    // ---------------- Утилиты ----------------

    Color ЦветПоДоле(float доля) => доля >= 0.85f ? ЦветХорошо : доля >= 0.6f ? ЦветСредне : ЦветПлохо;
    static Color Прозр(Color c, float a) => new Color(c.r, c.g, c.b, a);
    static string Доля(float часть, float всего) => всего > 0f ? (часть / всего * 100f).ToString("0") : "0";
    static string Hex(Color c) => ColorUtility.ToHtmlStringRGB(c);
}

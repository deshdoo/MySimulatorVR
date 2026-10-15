using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using TMPro;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.SceneManagement;

// Испытательные манёвры — проверка адекватности математической модели НПА.
//
// Запускаются из главного меню («ИСПЫТАНИЯ»), без миссии: миссия, метрики, повтор
// и повреждения на время испытаний выключены, в архив результатов ничего не пишется.
// Каждый опыт начинается из одной и той же исходной точки в толще воды (выше дна,
// ниже поверхности, направление без препятствий), с нулевой скоростью и ровным
// корпусом. Трос на время испытаний снят, течение выключено (кроме опыта на снос).
//
// Идея проверки: симулятор (дискретная физика Unity: шаг 0.02 с, явная
// присоединённая масса, решатель PhysX) сравнивается с ЭТАЛОНОМ — решением того же
// уравнения движения по одной оси, проинтегрированным отдельно мелким шагом (РК4):
//     m'·dv/dt = F(t) + F₀ − k·v·|v| − d·m'·v,
// где m' = m·(1 + kₐ) — масса с присоединённой массой воды, k = ½·ρ·Cx·S —
// квадратичное сопротивление, F₀ — постоянная сила (плавучесть), d — линейное
// демпфирование Unity (только у вращения). Для установившейся скорости есть и
// точное решение: v∞ = √(F/k). Совпадение кривых и чисел показывает, что движок
// реализует заложенные уравнения без численных искажений; расхождение — что их
// искажает (шаг интегрирования, явная схема, контакты).
//
// Опыты:
//   1. Разгон вперёд и выбег     — v∞, время 63 %, путь выбега
//   2. Боковой ход и выбег
//   3. Всплытие на тяге и выбег
//   4. Погружение на тяге и выбег
//   5. Пассивное всплытие          — скорость всплытия от одной плавучести
//   6. Разворот (рыскание)         — ω∞, время 63 %, угол выбега
//   7. Ступенька глубины автопилотом +2 м — перерегулирование, время нарастания и
//      установления, установившаяся ошибка (показатели качества САУ)
//   8. Снос течением               — скорость относительно дна → скорость воды
//
// Результаты: <persistentDataPath>/Испытания/<дата>/ — CSV каждого опыта (время,
// команда, модель, эталон), сводка.csv, параметры.txt (с какой физикой шли
// опыты) и PNG-графики; плюс панель с таблицей и графиками в кабине.
// Графики для текста ВКР: Tools/plot_maneuvers.py (подписи осей, легенда).
//
// Второй режим — «Настройка автопилота»: стенд подбора регулятора глубины, как в
// SimInTech. Оператор сам двигает ползунки Kp, Ki, Kd, предела тяги и включает
// защиту от интегрального насыщения, жмёт «Ступенька +2 м» и видит переходную
// характеристику поверх прошлой и таблицу прогонов (перерегулирование, время
// нарастания и установления, ошибка). «ЗАПОМНИТЬ» — автопилот работает с этой
// настройкой во всех миссиях (RovAutopilot.ЗапомнитьНастройку).
[DefaultExecutionOrder(-50)]   // команда пишется в DroneInput ДО RovController.FixedUpdate
public class RovManeuverTests : MonoBehaviour
{
    public static bool Идут { get; private set; }

    enum Ось { Продольная, Боковая, Вертикальная, Рыскание }

    class Показатель
    {
        public string Имя, Ед; public float Модель, Эталон;
        public float Расхождение => float.IsNaN(Эталон) || Mathf.Abs(Эталон) < 1e-6f ? float.NaN : (Модель - Эталон) / Mathf.Abs(Эталон) * 100f;
    }

    class Опыт
    {
        public string Имя, Файл, Величина, Ед, ИмяЭталона = "эталон", ИмяДоп, ИмяДоп2;
        public readonly List<float> t = new List<float>(), модель = new List<float>(), эталон = new List<float>(),
                                    команда = new List<float>(), доп = new List<float>(), доп2 = new List<float>();
        public float Начало;   // значение в момент команды (для графика «приращение от старта»)
        public readonly List<Показатель> Показатели = new List<Показатель>();
        public bool Касание;
        public string Примечание;
        public Texture2D График;
        public void П(string имя, float модель, float эталон, string ед) =>
            Показатели.Add(new Показатель { Имя = имя, Модель = модель, Эталон = эталон, Ед = ед });
    }

    const float ВысотаНадДном = 8f, ЗапасДоПоверхности = 6f;
    const float ШагГлубины = 2f;
    static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");

    RovController _rov;
    Rigidbody _rb;
    RovAutopilot _ап;
    Vector3 _T0; Quaternion _R0;
    Vector4? _команда;   // forward, vertical, lateral, yaw; null — командует автопилот
    readonly List<Опыт> _опыты = new List<Опыт>();
    string _папка;
    int _номер, _всего;
    string _текущий = "";
    float _времяОпыта;
    bool _готово, _точкаЕсть;

    VrPanel _панель;
    RectTransform _экран;
    TMP_Text _статус;
    bool _страницаГрафиков = true;

    // Настройка автопилота: прогоны ступеньки с разными коэффициентами.
    class Прогон { public Опыт О; public float Kp, Ki, Kd, Предел; public bool Защита; }
    readonly List<Прогон> _прогоны = new List<Прогон>();
    bool _идётПрогон;
    string _папкаПИД, _сообщениеПИД;
    Texture2D _графикПИД;
    float _исхKp, _исхKi, _исхKd, _исхПредел; bool _исхЗащита;
    int _стрТаблицы;   // таблица по 4 опыта на страницу

    // ---------------- Жизненный цикл ----------------

    void Start()
    {
        _rov = GetComponent<RovController>();
        _rb = GetComponent<Rigidbody>();
        _ап = RovAutopilot.Instance;
        Идут = true;
        Выключить<MissionManager>(); Выключить<MissionStats>(); Выключить<MissionReplayRecorder>();
        Выключить<RovScanner>(); Выключить<MissionDebriefPanel>(); Выключить<RovHealthMonitor>();
        if (UmbilicalCable.Instance != null) UmbilicalCable.Instance.ЗаморозитьДляИспытаний(true);
        StartCoroutine(Подготовка());
    }

    IEnumerator Подготовка()
    {
        yield return null; yield return null;   // дать отработать Start'ам сцены
        ВыбратьТочку(); _точкаЕсть = true;
        if (_ап != null)
        {
            _исхKp = _ап.depthPid.kp; _исхKi = _ап.depthPid.ki; _исхKd = _ап.depthPid.kd;
            _исхЗащита = _ап.depthPid.антиНасыщение; _исхПредел = _ап.maxAuthority;
        }
        ПостроитьПанель();
        _готово = true;
        ЭкранВыбора();
    }

    static void Выключить<T>() where T : Behaviour
    {
        foreach (var b in FindObjectsByType<T>(FindObjectsSortMode.None)) b.enabled = false;
    }

    void OnDestroy() { Идут = false; _панель?.Уничтожить(); }

    void FixedUpdate()
    {
        if (!_команда.HasValue) return;
        var c = _команда.Value;
        DroneInput.forward = c.x; DroneInput.vertical = c.y; DroneInput.lateral = c.z; DroneInput.yaw = c.w;
    }

    void Update()
    {
        if (_панель == null) return;
        _панель.Обновить();
        if (!_готово && _статус != null)
            _статус.text = $"Опыт {_номер} из {_всего}: <b>{_текущий}</b>   <color=#{VrPanel.Hex(_панель.ЦветВтор)}>{_времяОпыта:0.0} с</color>";
    }

    IEnumerator Серия()
    {
        _готово = false;
        _опыты.Clear();
        _папка = Path.Combine(Application.persistentDataPath, "Испытания", DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss"));
        Directory.CreateDirectory(_папка);
        ЗаписатьПараметры();
        ЭкранХода();

        bool естьТечение = _rov.Течение != null && _rov.Течение.Множитель > 0.01f;
        _всего = 7 + (естьТечение ? 1 : 0);
        _номер = 0;

        yield return Шаговый("Разгон вперёд и выбег", "01_вперёд", Ось.Продольная, 1f, 6f, 6f);
        yield return Шаговый("Боковой ход и выбег", "02_лаг", Ось.Боковая, 1f, 6f, 5f);
        yield return Шаговый("Всплытие на тяге и выбег", "03_вверх", Ось.Вертикальная, 1f, 5f, 4f);
        yield return Шаговый("Погружение на тяге и выбег", "04_вниз", Ось.Вертикальная, -1f, 5f, 4f);
        yield return Шаговый(_rov.ЧистаяПоложительнаяПлавучесть_Н >= 0f ? "Пассивное всплытие" : "Пассивное погружение",
                             "05_плавучесть", Ось.Вертикальная, 0f, 20f, 0f);
        yield return Шаговый("Разворот и выбег", "06_рыскание", Ось.Рыскание, 1f, 6f, 5f);
        yield return СтупенькаГлубины("Ступенька глубины +2 м (автопилот)", "07_автопилот");
        if (естьТечение) yield return Снос("Снос ровным течением", "08_течение");

        _команда = Vector4.zero;
        СохранитьСводку();
        foreach (var о in _опыты)
        {
            о.График = НарисоватьГрафик(о, 600, 300);
            try { File.WriteAllBytes(Path.Combine(_папка, о.Файл + ".png"), о.График.EncodeToPNG()); } catch { }
        }
        _готово = true;
        Debug.Log($"[Испытания] Готово: {_опыты.Count} опытов, результаты в {_папка}");
        ЭкранИтогов();
    }

    // ---------------- Исходная точка и сброс ----------------

    // Над текущим местом аппарата: на ВысотаНадДном выше дна и не ближе
    // ЗапасДоПоверхности к поверхности; направление — первое свободное по кругу.
    void ВыбратьТочку()
    {
        Vector3 p = _rb.position;
        float дно = p.y - 50f;
        foreach (var h in Physics.RaycastAll(p + Vector3.up * 200f, Vector3.down, 1000f, ~0, QueryTriggerInteraction.Ignore))
            if (!h.collider.transform.IsChildOf(transform) && h.collider.gameObject.scene == gameObject.scene && h.point.y < p.y + 0.5f)
                дно = Mathf.Max(дно, h.point.y);
        float поверхность = RovSystems.уровеньПоверхности_Y;
        float y = дно + ВысотаНадДном;
        if (поверхность > дно && y > поверхность - ЗапасДоПоверхности) y = (дно + поверхность) * 0.5f;
        _T0 = new Vector3(p.x, Mathf.Max(y, p.y), p.z);
        if (поверхность > дно) _T0.y = Mathf.Min(_T0.y, поверхность - ЗапасДоПоверхности);

        float курс = transform.eulerAngles.y;
        for (int i = 0; i < 8; i++)
        {
            var r = Quaternion.Euler(0f, курс + i * 45f, 0f);
            if (Свободно(r * Vector3.forward, 14f) && Свободно(r * Vector3.right, 10f)) { курс += i * 45f; break; }
        }
        _R0 = Quaternion.Euler(0f, курс, 0f);
        Debug.Log($"[Испытания] Исходная точка {_T0} (дно {дно:F1}, поверхность {поверхность:F1}), курс {курс:F0}°.");
    }

    bool Свободно(Vector3 dir, float L)
    {
        foreach (var h in Physics.SphereCastAll(_T0, 0.6f, dir, L, ~0, QueryTriggerInteraction.Ignore))
            if (!h.collider.transform.IsChildOf(transform) && h.collider.gameObject.scene == gameObject.scene) return false;
        return true;
    }

    void Сброс()
    {
        _команда = Vector4.zero;
        if (_ап != null && _ап.Engaged) _ап.Disengage();
        _rov.ЗаморозкаСноса = false;
        _rb.position = _T0; _rb.rotation = _R0;
        transform.SetPositionAndRotation(_T0, _R0);
        _rb.linearVelocity = Vector3.zero; _rb.angularVelocity = Vector3.zero;
        Physics.SyncTransforms();
        _rov.СброситьИсториюСкорости();
        if (_ап != null) _ап.СброситьОценку();   // фильтр глубины не должен «помнить» прошлое место
    }

    // ---------------- Модель одной оси ----------------

    struct ПараметрыОси { public float F1, F0, k, m, d; public bool градусы; }

    ПараметрыОси Параметры(Ось ось)
    {
        float ρ = _rov.ПлотностьВоды, m = _rb.mass;
        switch (ось)
        {
            case Ось.Продольная:
                return new ПараметрыОси { F1 = _rov.thrustForward, k = 0.5f * ρ * _rov.CxНос * _rov.ПлощадьНос, m = m * (1f + _rov.ПрисМассаПродольная) };
            case Ось.Боковая:
                return new ПараметрыОси { F1 = _rov.thrustLateral, k = 0.5f * ρ * _rov.CxБорт * _rov.ПлощадьБорт, m = m * (1f + _rov.ПрисМассаБоковая) };
            case Ось.Вертикальная:
                return new ПараметрыОси { F1 = _rov.thrustVertical, F0 = _rov.ЧистаяПоложительнаяПлавучесть_Н,
                                          k = 0.5f * ρ * _rov.CxВерх * _rov.ПлощадьВерх, m = m * (1f + _rov.ПрисМассаВертикальная) };
            default:
                return new ПараметрыОси { F1 = _rov.torqueYaw, k = _rov.КоэффСопротивленияВращению, m = МоментИнерцииY(),
                                          d = _rb.angularDamping, градусы = true };
        }
    }

    float МоментИнерцииY() => RovController.МоментИнерции(_rb, Vector3.up);

    // Направление оси в мире (для пути выбега): у вертикали — ось корпуса «вверх».
    Vector3 ОсьМира(Ось ось)
    {
        switch (ось)
        {
            case Ось.Продольная: return transform.forward;
            case Ось.Боковая:    return transform.right;
            default:             return transform.up;
        }
    }

    float Скорость(Ось ось)
    {
        switch (ось)
        {
            case Ось.Продольная:   return transform.InverseTransformDirection(_rb.linearVelocity).z;
            case Ось.Боковая:      return transform.InverseTransformDirection(_rb.linearVelocity).x;
            case Ось.Вертикальная: return transform.InverseTransformDirection(_rb.linearVelocity).y;
            default:               return transform.InverseTransformDirection(_rb.angularVelocity).y;
        }
    }

    static Vector4 Команда(Ось ось, float u)
    {
        switch (ось)
        {
            case Ось.Продольная:   return new Vector4(u, 0f, 0f, 0f);
            case Ось.Вертикальная: return new Vector4(0f, u, 0f, 0f);
            case Ось.Боковая:      return new Vector4(0f, 0f, u, 0f);
            default:               return new Vector4(0f, 0f, 0f, u);
        }
    }

    static float Ускорение(in ПараметрыОси п, float F, float v) => (F + п.F0 - п.k * v * Mathf.Abs(v)) / п.m - п.d * v;

    // Точная установившаяся скорость: F + F₀ − k·v|v| − d·m·v = 0.
    static float Установившаяся(in ПараметрыОси п, float F)
    {
        float Fс = F + п.F0;
        if (Mathf.Abs(Fс) < 1e-6f) return 0f;
        float s = Mathf.Sign(Fс), a = Mathf.Abs(Fс), b = п.d * п.m;
        return s * (-b + Mathf.Sqrt(b * b + 4f * п.k * a)) / (2f * п.k);
    }

    // ---------------- Опыты ----------------

    IEnumerator Шаговый(string имя, string файл, Ось ось, float u, float tТяги, float tВыбега)
    {
        _номер++; _текущий = имя; _времяОпыта = 0f;
        var течение = _rov.Течение; bool былоТечение = течение != null && течение.включено;
        if (течение != null) течение.включено = false;

        Сброс();
        yield return new WaitForFixedUpdate();
        Сброс();

        var п = Параметры(ось);
        float к = п.градусы ? Mathf.Rad2Deg : 1f;
        var о = new Опыт { Имя = имя, Файл = файл, Величина = п.градусы ? "угловая скорость" : "скорость", Ед = п.градусы ? "град/с" : "м/с", ИмяЭталона = "теория" };
        float dt = Time.fixedDeltaTime;
        int N = Mathf.RoundToInt((tТяги + tВыбега) / dt);
        float v0 = Скорость(ось);
        о.t.Add(0f); о.модель.Add(v0 * к); о.команда.Add(tТяги > 0f ? u : 0f);

        int iКонца = Mathf.RoundToInt(tТяги / dt);
        Vector3 позВыбега = _rb.position, осьВыбега = Vector3.zero; float курсВыбега = 0f;
        for (int i = 1; i <= N; i++)
        {
            float uk = i <= iКонца ? u : 0f;   // целыми шагами: (i−1)·0,02 < 6 из-за округления давало лишний шаг тяги
            _команда = Команда(ось, uk);
            yield return new WaitForFixedUpdate();
            if (_rov.Касается) о.Касание = true;
            о.t.Add(i * dt); о.модель.Add(Скорость(ось) * к); о.команда.Add(uk);
            _времяОпыта = i * dt;
            if (i == iКонца) { позВыбега = _rb.position; осьВыбега = ОсьМира(ось); курсВыбега = _rb.rotation.eulerAngles.y; }
        }
        _команда = Vector4.zero;
        // Путь/угол выбега модели — по фактическому перемещению аппарата, а не сложением
        // отсчётов скорости: у быстрых процессов (рыскание гаснет за ~0,01 с) трапеции
        // по сетке 0,02 с заметно завышают площадь.
        float выбегМодели = п.градусы ? Mathf.DeltaAngle(курсВыбега, _rb.rotation.eulerAngles.y)
                                      : Vector3.Dot(_rb.position - позВыбега, осьВыбега);

        // Эталон: РК4 с шагом dt/10 от той же начальной скорости; путь выбега копится
        // на том же мелком шаге.
        float v = v0, h = dt / 10f, выбегЭталона = 0f;
        о.эталон.Add(v0 * к);
        for (int i = 1; i <= N; i++)
        {
            float F = i <= iКонца ? u * п.F1 : 0f;
            for (int j = 0; j < 10; j++)
            {
                float k1 = Ускорение(п, F, v), k2 = Ускорение(п, F, v + 0.5f * h * k1);
                float k3 = Ускорение(п, F, v + 0.5f * h * k2), k4 = Ускорение(п, F, v + h * k3);
                float vНов = v + h / 6f * (k1 + 2f * k2 + 2f * k3 + k4);
                if (i > iКонца) выбегЭталона += 0.5f * (v + vНов) * h;
                v = vНов;
            }
            о.эталон.Add(v * к);
        }
        выбегЭталона *= к;

        // Показатели.
        int конецТяги = Mathf.Min(о.t.Count - 1, Mathf.RoundToInt(tТяги / dt));
        string ед = о.Ед;
        if (tТяги > 0f && (u != 0f || п.F0 != 0f))
        {
            float средн = Среднее(о.модель, конецТяги - Mathf.RoundToInt((u != 0f ? 0.5f : 3f) / dt), конецТяги);
            float точн = Установившаяся(п, u * п.F1) * к;
            о.П("Установившаяся скорость", средн, точн, ед);
            float t63м = ВремяДоли(о.t, о.модель, 0.632f * средн, конецТяги);
            float t63э = ВремяДоли(о.t, о.эталон, 0.632f * точн, конецТяги);
            // Переход короче двух шагов физики дискретная модель разрешить не может —
            // сравнение было бы сравнением с шагом сетки, а не с физикой.
            if (t63э < 2f * dt)
            {
                о.П("Время до 63 % (быстрее шага физики — не измеряется)", t63м, float.NaN, "с");
                о.Примечание ??= $"переход быстрее шага физики {dt:0.00} с — время разгона не разрешается";
            }
            else о.П("Время до 63 % скорости", t63м, t63э, "с");
        }
        if (tВыбега > 0f)
        {
            string едП = п.градусы ? "град" : "м";
            о.П(п.градусы ? "Угол выбега" : "Путь выбега", выбегМодели, выбегЭталона, едП);
        }
        float масштаб = 1e-6f; foreach (var x in о.эталон) масштаб = Mathf.Max(масштаб, Mathf.Abs(x));
        о.П("СКО расхождения кривых (от макс.)", СКО(о.модель, о.эталон) / масштаб * 100f, float.NaN, "%");
        if (о.Касание) о.Примечание = "было касание — опыт недостоверен";

        if (течение != null) течение.включено = былоТечение;
        СохранитьОпыт(о);
        _опыты.Add(о);
    }

    IEnumerator СтупенькаГлубины(string имя, string файл, List<Опыт> куда = null, string папка = null)
    {
        куда = куда ?? _опыты;
        _номер++; _текущий = имя; _времяОпыта = 0f;
        var о = new Опыт { Имя = имя, Файл = файл, Величина = "глубина", Ед = "м", ИмяЭталона = "уставка", ИмяДоп = "глубина по датчику, м", ИмяДоп2 = "глубина в автопилоте (после фильтра), м" };
        if (_ап == null) { о.Примечание = "нет автопилота на аппарате"; куда.Add(о); yield break; }

        var течение = _rov.Течение; bool былоТечение = течение != null && течение.включено;
        if (течение != null) течение.включено = false;
        Сброс();
        // Сначала 3 с без тяги: после переноса фильтр глубины автопилота должен
        // догнать новое показание, иначе уставка взялась бы по старому месту.
        for (float t = 0f; t < 3f; t += Time.fixedDeltaTime) { _времяОпыта = t - 11f; yield return new WaitForFixedUpdate(); }
        _команда = null;           // глубину держит автопилот
        _ап.Engage();
        for (float t = 0f; t < 8f; t += Time.fixedDeltaTime) { _времяОпыта = t - 8f; yield return new WaitForFixedUpdate(); }

        float g0 = RovSystems.depthTrue_m;
        о.Начало = g0;
        float цель = _ап.TargetDepth_m + ШагГлубины;
        _ап.ЗадатьГлубину(цель);
        float dt = Time.fixedDeltaTime;
        int N = Mathf.RoundToInt(30f / dt);
        for (int i = 0; i <= N; i++)
        {
            if (i > 0) yield return new WaitForFixedUpdate();
            if (_rov.Касается) о.Касание = true;
            о.t.Add(i * dt); о.модель.Add(RovSystems.depthTrue_m); о.эталон.Add(цель);
            о.доп.Add(RovSystems.depth_m); о.доп2.Add(_ап.ГлубинаРегулятора_m); о.команда.Add(_ап.VerticalCommand);
            _времяОпыта = i * dt;
        }
        _ап.Disengage();
        _команда = Vector4.zero;
        if (течение != null) течение.включено = былоТечение;

        // Показатели качества переходного процесса (по истинной глубине).
        float Δ = цель - g0, макс = float.MinValue;
        float t10 = float.NaN, t90 = float.NaN, tУст = 0f;
        for (int i = 0; i < о.t.Count; i++)
        {
            float x = (о.модель[i] - g0) / Δ;
            макс = Mathf.Max(макс, о.модель[i]);
            if (float.IsNaN(t10) && x >= 0.1f) t10 = о.t[i];
            if (float.IsNaN(t90) && x >= 0.9f) t90 = о.t[i];
            if (Mathf.Abs(о.модель[i] - цель) > 0.05f * Mathf.Abs(Δ)) tУст = о.t[i];
        }
        int n5 = Mathf.RoundToInt(5f / dt);
        float ошибка = цель - Среднее(о.модель, о.модель.Count - n5, о.модель.Count - 1);
        о.П("Перерегулирование", Mathf.Max(0f, (макс - цель) / Δ * 100f), float.NaN, "%");
        о.П("Время нарастания 10–90 %", t90 - t10, float.NaN, "с");
        о.П("Время установления (±5 %)", tУст >= о.t[о.t.Count - 1] ? float.NaN : tУст, float.NaN, "с");
        о.П("Установившаяся ошибка", ошибка, float.NaN, "м");
        if (tУст >= о.t[о.t.Count - 1]) о.Примечание = "за 30 с не вошла в трубку ±5 %";
        if (о.Касание) о.Примечание = "было касание — опыт недостоверен";
        СохранитьОпыт(о, папка);
        куда.Add(о);
    }

    IEnumerator Снос(string имя, string файл)
    {
        _номер++; _текущий = имя; _времяОпыта = 0f;
        var о = new Опыт { Имя = имя, Файл = файл, Величина = "скорость относительно дна", Ед = "м/с", ИмяЭталона = "скорость воды" };
        var течение = _rov.Течение;
        течение.включено = true;
        // Ровный поток: без пульсаций и вихрей. Со случайным потоком среднее за 10 с
        // каждый раз разное (серии давали от −11 до +4 %) — это не проверка модели.
        float былаАмплитуда = течение.АмплитудаПульсаций, былРазброс = течение.РазбросНаправления;
        течение.АмплитудаПульсаций = 0f; течение.РазбросНаправления = 0f; течение.БезВихрей = true;
        Сброс();
        float dt = Time.fixedDeltaTime;
        int N = Mathf.RoundToInt(30f / dt);
        for (int i = 0; i <= N; i++)
        {
            if (i > 0) { _команда = Vector4.zero; yield return new WaitForFixedUpdate(); }
            if (_rov.Касается) о.Касание = true;
            Vector3 v = _rb.linearVelocity, w = течение.Скорость(_rb.position);
            о.t.Add(i * dt); о.модель.Add(new Vector2(v.x, v.z).magnitude); о.эталон.Add(new Vector2(w.x, w.z).magnitude);
            о.команда.Add(0f);
            _времяОпыта = i * dt;
        }
        течение.АмплитудаПульсаций = былаАмплитуда; течение.РазбросНаправления = былРазброс; течение.БезВихрей = false;
        int n10 = Mathf.RoundToInt(10f / dt);
        о.П("Средняя скорость за последние 10 с", Среднее(о.модель, о.модель.Count - n10, о.модель.Count - 1),
                                                   Среднее(о.эталон, о.эталон.Count - n10, о.эталон.Count - 1), "м/с");
        о.П("Время до 63 % скорости воды", ВремяДоли(о.t, о.модель, 0.632f * Среднее(о.эталон, 0, о.эталон.Count - 1), о.t.Count - 1), float.NaN, "с");
        if (о.Касание) о.Примечание = "было касание — опыт недостоверен";
        СохранитьОпыт(о);
        _опыты.Add(о);
    }

    // ---------------- Обработка ----------------

    static float Среднее(List<float> x, int от, int до)
    {
        от = Mathf.Clamp(от, 0, x.Count - 1); до = Mathf.Clamp(до, 0, x.Count - 1);
        float s = 0f; int n = 0;
        for (int i = от; i <= до; i++) { s += x[i]; n++; }
        return n > 0 ? s / n : float.NaN;
    }

    // Первое время, когда |x| достиг |порог| (в пределах до индекса «до»), с линейной
    // интерполяцией между отсчётами: разгон длится всего несколько шагов физики,
    // и без неё сама дискретность давала бы ошибку 10–15 %.
    static float ВремяДоли(List<float> t, List<float> x, float порог, int до)
    {
        float п = Mathf.Abs(порог);
        if (п < 1e-6f) return float.NaN;
        for (int i = 0; i <= до && i < x.Count; i++)
        {
            if (Mathf.Abs(x[i]) < п) continue;
            if (i == 0) return t[0];
            float a = Mathf.Abs(x[i - 1]), b = Mathf.Abs(x[i]);
            return Mathf.Lerp(t[i - 1], t[i], b > a ? (п - a) / (b - a) : 1f);
        }
        return float.NaN;
    }

    // Путь (интеграл скорости) от индекса «от» до конца, трапециями.
    static float Интеграл(List<float> t, List<float> x, int от)
    {
        float s = 0f;
        for (int i = Mathf.Max(1, от + 1); i < x.Count; i++) s += 0.5f * (x[i] + x[i - 1]) * (t[i] - t[i - 1]);
        return s;
    }

    static float СКО(List<float> a, List<float> b)
    {
        int n = Mathf.Min(a.Count, b.Count); if (n == 0) return float.NaN;
        double s = 0; for (int i = 0; i < n; i++) { double d = a[i] - b[i]; s += d * d; }
        return (float)Math.Sqrt(s / n);
    }

    static string Ч(float v, string ф = "0.###") => float.IsNaN(v) || float.IsInfinity(v) ? "" : v.ToString(ф, Ru);

    void СохранитьОпыт(Опыт о, string папка = null)
    {
        папка = папка ?? _папка;
        try
        {
            var sb = new StringBuilder();
            sb.Append($"время_с;команда;{о.Величина} (модель), {о.Ед};{о.ИмяЭталона}, {о.Ед}");
            if (о.ИмяДоп != null) sb.Append(';').Append(о.ИмяДоп);
            if (о.ИмяДоп2 != null) sb.Append(';').Append(о.ИмяДоп2);
            sb.AppendLine();
            for (int i = 0; i < о.t.Count; i++)
            {
                sb.Append(Ч(о.t[i])).Append(';').Append(i < о.команда.Count ? Ч(о.команда[i]) : "").Append(';')
                  .Append(Ч(о.модель[i], "0.#####")).Append(';').Append(i < о.эталон.Count ? Ч(о.эталон[i], "0.#####") : "");
                if (о.ИмяДоп != null) sb.Append(';').Append(i < о.доп.Count ? Ч(о.доп[i], "0.#####") : "");
                if (о.ИмяДоп2 != null) sb.Append(';').Append(i < о.доп2.Count ? Ч(о.доп2[i], "0.#####") : "");
                sb.AppendLine();
            }
            File.WriteAllText(Path.Combine(папка, о.Файл + ".csv"), sb.ToString(), new UTF8Encoding(true));
        }
        catch (Exception e) { Debug.LogWarning($"[Испытания] Не удалось записать {о.Файл}: {e.Message}"); }
    }

    void СохранитьСводку()
    {
        try
        {
            var sb = new StringBuilder("манёвр;показатель;модель;эталон;расхождение_проц;ед;примечание\n");
            foreach (var о in _опыты)
                foreach (var п in о.Показатели)
                    sb.Append(о.Имя).Append(';').Append(п.Имя).Append(';').Append(Ч(п.Модель, "0.####")).Append(';')
                      .Append(Ч(п.Эталон, "0.####")).Append(';').Append(Ч(п.Расхождение, "0.##")).Append(';')
                      .Append(п.Ед).Append(';').Append(о.Примечание ?? "").Append('\n');
            File.WriteAllText(Path.Combine(_папка, "сводка.csv"), sb.ToString(), new UTF8Encoding(true));
        }
        catch (Exception e) { Debug.LogWarning($"[Испытания] Не удалось записать сводку: {e.Message}"); }
    }

    void ЗаписатьПараметры()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Испытательные манёвры НПА, {DateTime.Now:dd.MM.yyyy HH:mm}");
        sb.AppendLine($"Шаг физики Δt = {Time.fixedDeltaTime:0.###} с");
        sb.AppendLine($"Масса m = {_rb.mass:0.##} кг; момент инерции Iyy = {МоментИнерцииY():0.###} кг·м²");
        sb.AppendLine($"Тензор инерции (главные) {_rb.inertiaTensor}, поворот главных осей {_rb.inertiaTensorRotation.eulerAngles}");
        sb.AppendLine($"Тяга: вперёд {_rov.thrustForward:0.#} Н, вертикаль {_rov.thrustVertical:0.#} Н, лаг {_rov.thrustLateral:0.#} Н, рыскание {_rov.torqueYaw:0.#} Н·м");
        sb.AppendLine($"Плотность воды ρ = {_rov.ПлотностьВоды:0.#} кг/м³; чистая плавучесть {_rov.ЧистаяПоложительнаяПлавучесть_Н:0.##} Н");
        sb.AppendLine($"Cx: нос {_rov.CxНос}, верх {_rov.CxВерх}, борт {_rov.CxБорт}; S: нос {_rov.ПлощадьНос} м², верх {_rov.ПлощадьВерх} м², борт {_rov.ПлощадьБорт} м²");
        sb.AppendLine($"Присоединённая масса (доли m): продольная {_rov.ПрисМассаПродольная}, вертикальная {_rov.ПрисМассаВертикальная}, боковая {_rov.ПрисМассаБоковая}");
        sb.AppendLine($"Сопротивление вращению c = {_rov.КоэффСопротивленияВращению} Н·м·с²; линейное угловое демпфирование Unity d = {_rb.angularDamping}");
        sb.AppendLine($"Течение: {(_rov.Течение != null ? $"×{_rov.Течение.Множитель:0.##}" : "нет")}");
        if (_ап != null)
            sb.AppendLine($"Автопилот глубины: Kp = {_ап.depthPid.kp}, Ki = {_ап.depthPid.ki}, Kd = {_ап.depthPid.kd}, " +
                          $"защита от насыщения {(_ап.depthPid.антиНасыщение ? "вкл" : "выкл")}, предел тяги {_ап.maxAuthority:0.##}");
        sb.AppendLine("Настройки «Физики» меню: " + string.Join(", ", Array.ConvertAll(PhysicsSettings.Все, п => $"{п.Название} = {п.Текст(п.Значение)}")));
        sb.AppendLine($"Исходная точка {_T0}, курс {_R0.eulerAngles.y:0}°");
        try { File.WriteAllText(Path.Combine(_папка, "параметры.txt"), sb.ToString(), new UTF8Encoding(true)); } catch { }
    }

    // ---------------- График в текстуре ----------------

    static readonly Color ЦветМодели = new Color(0.30f, 0.85f, 1.00f), ЦветЭталона = new Color(1.00f, 0.62f, 0.25f);

    Texture2D НарисоватьГрафик(Опыт о, int W, int H) =>
        НарисоватьЛинии(W, H, о.Величина != "глубина",
            (о.t, о.эталон, ЦветЭталона, 4), (о.t, о.модель, ЦветМодели, 2));

    // Линии (время, значения, цвет, толщина) в одной системе координат; последняя — сверху.
    static Texture2D НарисоватьЛинии(int W, int H, bool сНулём, params (List<float> t, List<float> v, Color c, int толщина)[] линии)
    {
        var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
        var px = new Color32[W * H];
        var фон = new Color32(10, 18, 26, 255);
        for (int i = 0; i < px.Length; i++) px[i] = фон;

        float мин = сНулём ? 0f : float.MaxValue, макс = сНулём ? 0f : float.MinValue, T = 0f;
        foreach (var л in линии)
        {
            if (л.t == null || л.v == null) continue;
            foreach (var v in л.v) if (!float.IsNaN(v)) { мин = Mathf.Min(мин, v); макс = Mathf.Max(макс, v); }
            if (л.t.Count > 0) T = Mathf.Max(T, л.t[л.t.Count - 1]);
        }
        if (T <= 0f || мин > макс) { tex.SetPixels32(px); tex.Apply(); return tex; }
        float запас = Mathf.Max(1e-3f, (макс - мин) * 0.08f); мин -= запас; макс += запас;
        int X(float t) => Mathf.Clamp(Mathf.RoundToInt(t / T * (W - 1)), 0, W - 1);
        int Y(float v) => Mathf.Clamp(Mathf.RoundToInt((v - мин) / (макс - мин) * (H - 1)), 0, H - 1);

        var сетка = new Color32(40, 56, 70, 255);
        for (int k = 1; k < 5; k++) { int y = k * (H - 1) / 5; for (int x = 0; x < W; x++) px[y * W + x] = сетка; }
        for (int k = 1; k < 6; k++) { int x = k * (W - 1) / 6; for (int y = 0; y < H; y++) px[y * W + x] = сетка; }
        if (мин < 0f && макс > 0f) { int y0 = Y(0f); for (int x = 0; x < W; x++) px[y0 * W + x] = new Color32(90, 110, 125, 255); }

        void Линия(List<float> tt, List<float> v, Color c, int толщина)
        {
            var c32 = (Color32)c;
            for (int i = 1; i < v.Count && i < tt.Count; i++)
            {
                if (float.IsNaN(v[i]) || float.IsNaN(v[i - 1])) continue;
                int x0 = X(tt[i - 1]), y0 = Y(v[i - 1]), x1 = X(tt[i]), y1 = Y(v[i]);
                int шагов = Mathf.Max(Mathf.Abs(x1 - x0), Mathf.Abs(y1 - y0), 1);
                for (int s = 0; s <= шагов; s++)
                {
                    int x = x0 + (x1 - x0) * s / шагов, y = y0 + (y1 - y0) * s / шагов;
                    for (int dx = 0; dx < толщина; dx++) for (int dy = 0; dy < толщина; dy++)
                    {
                        int xx = Mathf.Min(W - 1, x + dx), yy = Mathf.Min(H - 1, y + dy);
                        px[yy * W + xx] = c32;
                    }
                }
            }
        }
        foreach (var л in линии) if (л.t != null && л.v != null) Линия(л.t, л.v, л.c, л.толщина);
        tex.SetPixels32(px); tex.Apply();
        return tex;
    }

    // ---------------- Панель в кабине ----------------

    void ПостроитьПанель()
    {
        var риг = FindFirstObjectByType<XROrigin>();
        Transform голова = риг != null && риг.Camera != null ? риг.Camera.transform : Camera.main != null ? Camera.main.transform : null;
        var hud = FindFirstObjectByType<MissionHudDisplay>(FindObjectsInactive.Include);
        _панель = new VrPanel("ПанельИспытаний", 1000, 640, 0.85f, gameObject.scene, hud != null ? hud.шрифт : null, false);
        if (!ПоставитьУМониторов() && голова != null)
        {
            Vector3 f = Vector3.ProjectOnPlane(голова.forward, Vector3.up).normalized;
            if (f.sqrMagnitude < 0.01f) f = Vector3.forward;
            Vector3 вправо = Vector3.Cross(Vector3.up, f);
            // Запасной вариант (мониторы не найдены): левее взгляда, повёрнута к оператору.
            Vector3 поз = голова.position + f * 1.0f - вправо * 0.85f + Vector3.up * 0.1f;
            _панель.Корень.transform.SetPositionAndRotation(поз, Quaternion.LookRotation(поз - голова.position, Vector3.up));
        }
        _панель.Прямоугольник(_панель.Rt, 0, 0, 1000, 640, _панель.ЦветФона);
        _панель.Прямоугольник(_панель.Rt, 0, 0, 1000, 4, _панель.ЦветАкцент);
        _экран = _панель.Контейнер(_панель.Rt, "Экран");
    }

    // Постоянное место панели — по пульту, а не по голове: слева от левого нижнего монитора
    // («Состояние НПА»), правым краем у монитора, повёрнута в пол-оборота (45°) к креслу.
    // Низ панели — на уровне низа монитора (над столом). Если до левой стены мало места —
    // панель уменьшается, чтобы не входить в стену.
    bool ПоставитьУМониторов()
    {
        const float Зазор_м = 0.67f, НазадОтЭкрана_м = -0.15f, Наклон_град = 60f, ОтступОтСтены_м = 0.08f, МинШирина_м = 0.55f;
        var дисплей = FindFirstObjectByType<SystemStatusDisplay>(FindObjectsInactive.Include);
        var холст = дисплей != null ? дисплей.GetComponentInParent<Canvas>(true) : null;
        var риг = FindFirstObjectByType<XROrigin>();
        if (холст == null || риг == null) return false;

        // Стороны — от кресла пилота (точка рига), а не по осям холста: холст монитора
        // в сцене может быть развёрнут или отзеркален, и его «лево» тогда не левое.
        var углы = new Vector3[4];
        ((RectTransform)холст.transform).GetWorldCorners(углы);
        Vector3 центрЭкрана = (углы[0] + углы[1] + углы[2] + углы[3]) * 0.25f;
        Vector3 кПилоту = Vector3.ProjectOnPlane(риг.transform.position - центрЭкрана, Vector3.up).normalized;
        if (кПилоту.sqrMagnitude < 0.01f) return false;
        Vector3 вправо = Vector3.Cross(Vector3.up, -кПилоту);   // вправо, как видит пилот
        float левыйКрай = float.PositiveInfinity, низ = float.PositiveInfinity;
        foreach (var у in углы) { левыйКрай = Mathf.Min(левыйКрай, Vector3.Dot(у - центрЭкрана, вправо)); низ = Mathf.Min(низ, у.y); }
        Vector3 петля = центрЭкрана + вправо * (левыйКрай - Зазор_м) - кПилоту * НазадОтЭкрана_м;   // правый край панели
        петля.y = низ + 0.13f;

        // Сторона панели (как видит оператор): уходит влево и к оператору.
        Vector3 вдоль = Quaternion.AngleAxis(-Наклон_град, Vector3.up) * -вправо;
        float ширина = 0.85f;
        float доСтены = float.PositiveInfinity;
        // Самое дальнее попадание влево в пределах 2.5 м — стена (колонки и стойки на пути не в счёт).
        foreach (var h in Physics.RaycastAll(петля + Vector3.up * 0.3f, -вправо, 2.5f, ~0, QueryTriggerInteraction.Ignore))
            доСтены = float.IsPositiveInfinity(доСтены) ? h.distance : Mathf.Max(доСтены, h.distance);
        if (!float.IsPositiveInfinity(доСтены))
        {
            float влезает = (доСтены - ОтступОтСтены_м) / Vector3.Dot(вдоль, -вправо);
            ширина = Mathf.Clamp(влезает, МинШирина_м, ширина);
        }
        _панель.Rt.localScale = Vector3.one * (ширина / _панель.W);

        float высота = ширина * _панель.H / _панель.W;
        Vector3 центр = петля + вдоль * (ширина * 0.5f) + Vector3.up * (высота * 0.5f);
        // Лицо панели — к оператору: «вперёд» холста направлено от зрителя.
        Vector3 лицо = Vector3.Cross(Vector3.up, вдоль);   // вперёд холста, от оператора
        _панель.Корень.transform.SetPositionAndRotation(центр, Quaternion.LookRotation(лицо, Vector3.up));
        return true;
    }

    void Шапка(string подзаголовок)
    {
        var p = _панель;
        p.Текст(_экран, 32, 16, 900, 24, 17, p.ЦветВтор, перенос: false).text = подзаголовок;
        var t = p.Текст(_экран, 32, 40, 900, 40, 32, p.ЦветТекста, перенос: false);
        t.fontStyle = FontStyles.Bold; t.text = "Испытательные манёвры";
    }

    void ЭкранХода()
    {
        VrPanel.Очистить(_экран);
        Шапка("ПРОВЕРКА АДЕКВАТНОСТИ МОДЕЛИ · ИДУТ ОПЫТЫ");
        var p = _панель;
        _статус = p.Текст(_экран, 32, 110, 936, 36, 24, p.ЦветТекста, перенос: false);
        p.Текст(_экран, 32, 170, 936, 300, 19, p.ЦветВтор).text =
            "Аппарат каждый раз ставится в одну и ту же точку в толще воды и выполняет манёвр по программе: " +
            "полный ход по одной оси, затем выбег без тяги. Рядом считается эталон — то же уравнение движения, " +
            "решённое отдельно точным методом. Совпадение кривых значит, что симулятор не искажает заложенную физику.\n\n" +
            "Течение выключено (кроме опыта на снос), трос снят. Смотрите на монитор камеры и графики пульта. " +
            "Рычаги на время опытов не действуют.";
        p.Кнопка(_экран, 1000 - 232, 640 - 76, 200, 52, "<b>В МЕНЮ</b>", p.ЦветВтор, ВМеню, кегль: 22);
    }

    void ЭкранИтогов()
    {
        VrPanel.Очистить(_экран);
        _статус = null;
        var p = _панель;
        Шапка($"ГОТОВО · {_опыты.Count} ОПЫТОВ · СИНИЙ — МОДЕЛЬ, ОРАНЖЕВЫЙ — ЭТАЛОН");
        p.Кнопка(_экран, 32, 96, 180, 44, "<b>ГРАФИКИ</b>", p.ЦветАкцент, () => { _страницаГрафиков = true; ЭкранИтогов(); }, () => _страницаГрафиков, 20);
        p.Кнопка(_экран, 222, 96, 180, 44, "<b>ТАБЛИЦА</b>", p.ЦветАкцент, () => { _страницаГрафиков = false; ЭкранИтогов(); }, () => !_страницаГрафиков, 20);

        p.Кнопка(_экран, 1000 - 252, 96, 220, 44, "<b>НАСТРОЙКА ПИД</b>", p.ЦветСредне, ЭкранПИД, кегль: 18);
        if (_страницаГрафиков) СеткаГрафиков();
        else
        {
            int страниц = Mathf.Max(1, (_опыты.Count + 3) / 4);
            _стрТаблицы = Mathf.Clamp(_стрТаблицы, 0, страниц - 1);
            for (int i = 0; i < страниц; i++)
            {
                int с = i;
                string надпись = $"<b>{с * 4 + 1}–{Mathf.Min(_опыты.Count, с * 4 + 4)}</b>";
                p.Кнопка(_экран, 432 + i * 110, 96, 100, 44, надпись, p.ЦветВтор, () => { _стрТаблицы = с; ЭкранИтогов(); }, () => _стрТаблицы == с, 20);
            }
            Таблица();
        }

        float ny = 640 - 76;
        p.Текст(_экран, 32, ny + 4, 520, 50, 13, p.ЦветВтор).text = "Файлы: " + _папка.Replace('\\', '/');
        p.Кнопка(_экран, 1000 - 452, ny, 200, 52, "<b>ПОВТОРИТЬ</b>", p.ЦветАкцент, () => { if (_готово) { ЭкранХода(); StartCoroutine(Серия()); } }, кегль: 22);
        p.Кнопка(_экран, 1000 - 232, ny, 200, 52, "<b>В МЕНЮ</b>", p.ЦветВтор, ВМеню, кегль: 22);
    }

    // Короткие названия для сетки графиков (ячейка ~230 px) — полные в таблице.
    static string Коротко(Опыт о)
    {
        switch (о.Файл.Substring(0, Mathf.Min(2, о.Файл.Length)))
        {
            case "01": return "Вперёд и выбег";
            case "02": return "Вбок и выбег";
            case "03": return "Вверх и выбег";
            case "04": return "Вниз и выбег";
            case "05": return о.Имя.Contains("погруж") ? "Без тяги: погружение" : "Без тяги: всплытие";
            case "06": return "Разворот и выбег";
            case "07": return "Ступенька +2 м";
            case "08": return "Снос течением";
            default:   return о.Имя;
        }
    }

    static string КороткийПоказатель(string имя)
    {
        if (имя.StartsWith("Установившаяся")) return "v уст.";
        if (имя.StartsWith("Перерегулирование")) return "проскок";
        if (имя.StartsWith("Средняя скорость")) return "v ср.";
        return имя;
    }

    void СеткаГрафиков()
    {
        var p = _панель;
        string вт = VrPanel.Hex(p.ЦветВтор), тек = VrPanel.Hex(p.ЦветТекста);
        for (int i = 0; i < _опыты.Count && i < 8; i++)
        {
            var о = _опыты[i];
            float x = 32 + (i % 4) * 236, y = 152 + (i / 4) * 196, w = 228;
            p.Текст(_экран, x, y, w, 20, 15, о.Касание ? p.ЦветПлохо : p.ЦветТекста, перенос: false).text = $"<b>{i + 1}. {Коротко(о)}</b>";
            if (о.График != null) p.Картинка(_экран, x, y + 22, w, 112, о.График);
            // Две строки фиксированной высоты, без переноса: главный показатель и шкала.
            var гл = о.Показатели.Count > 0 ? о.Показатели[0] : null;
            string стр1 = гл == null ? (о.Примечание ?? "") :
                $"{КороткийПоказатель(гл.Имя)}: <color=#{тек}><b>{Ч(гл.Модель, "0.##")}</b></color>" +
                (float.IsNaN(гл.Эталон) ? "" : $" / {Ч(гл.Эталон, "0.##")}") + $" {гл.Ед}";
            p.Текст(_экран, x, y + 138, w, 18, 13, p.ЦветВтор, перенос: false).text = стр1;
            p.Текст(_экран, x, y + 156, w, 18, 12, p.ЦветВтор, перенос: false).text =
                $"<color=#{вт}>0…{Ч(о.t.Count > 0 ? о.t[о.t.Count - 1] : 0f, "0")} с · {о.Ед}</color>";
        }
    }

    void Таблица()
    {
        var p = _панель;
        string вт = VrPanel.Hex(p.ЦветВтор);
        p.Текст(_экран, 32, 150, 936, 20, 14, p.ЦветВтор, перенос: false).text =
            "<pos=0%>ОПЫТ / ПОКАЗАТЕЛЬ<pos=58%>МОДЕЛЬ<pos=72%>ЭТАЛОН<pos=86%>РАСХОЖД.";
        float y = 174;
        for (int k = _стрТаблицы * 4; k < _опыты.Count && k < _стрТаблицы * 4 + 4; k++)
        {
            var о = _опыты[k];
            if (y > 540) break;
            p.Текст(_экран, 32, y, 936, 20, 15, о.Касание ? p.ЦветПлохо : p.ЦветТекста, перенос: false).text =
                $"<b>{k + 1}. {о.Имя}</b>" + (о.Примечание != null ? $"  <color=#{VrPanel.Hex(p.ЦветСредне)}>{о.Примечание}</color>" : "");
            y += 20;
            foreach (var п in о.Показатели)
            {
                if (y > 548) break;
                float r = п.Расхождение;
                Color c = float.IsNaN(r) ? p.ЦветВтор : Mathf.Abs(r) <= 2f ? p.ЦветХорошо : Mathf.Abs(r) <= 5f ? p.ЦветСредне : p.ЦветПлохо;
                p.Текст(_экран, 32, y, 936, 18, 13, p.ЦветВтор, перенос: false).text =
                    $"<pos=2%>{п.Имя}, {п.Ед}<pos=58%><color=#{VrPanel.Hex(p.ЦветТекста)}>{Ч(п.Модель, "0.###")}</color>" +
                    $"<pos=72%>{(float.IsNaN(п.Эталон) ? "—" : Ч(п.Эталон, "0.###"))}" +
                    $"<pos=86%><color=#{VrPanel.Hex(c)}>{(float.IsNaN(r) ? "—" : Ч(r, "+0.0;−0.0;0.0") + " %")}</color>";
                y += 18;
            }
            y += 4;
        }
    }

    // ---------------- Выбор режима ----------------

    void ЭкранВыбора()
    {
        VrPanel.Очистить(_экран);
        _статус = null;
        var p = _панель;
        Шапка("СТЕНД ИСПЫТАНИЙ · ВЫБЕРИТЕ РЕЖИМ");
        p.Кнопка(_экран, 32, 120, 460, 120, "<b>ВСЕ ОПЫТЫ</b>\n<size=60%>8 манёвров, ~3 мин · проверка адекватности модели</size>",
                 p.ЦветАкцент, () => StartCoroutine(Серия()), кегль: 28);
        p.Кнопка(_экран, 508, 120, 460, 120, "<b>НАСТРОЙКА АВТОПИЛОТА</b>\n<size=60%>подбор Kp, Ki, Kd ползунками · ступенька глубины</size>",
                 p.ЦветСредне, ЭкранПИД, кегль: 28);
        p.Текст(_экран, 32, 270, 936, 260, 18, p.ЦветВтор).text =
            "<b>Все опыты</b> — аппарат сам выполняет манёвры, а рядом считается эталон по формуле. Совпадение кривых — " +
            "доказательство, что симулятор не искажает заложенную физику (глава «проверка адекватности»).\n\n" +
            "<b>Настройка автопилота</b> — подбор регулятора глубины. Двигаете ползунки Kp, Ki, Kd, нажимаете «Ступенька» — " +
            "аппарат получает команду уйти на 2 м глубже, а график показывает, как он туда идёт: проскакивает ли, " +
            "как быстро успокаивается. Добейтесь малого проскока и быстрого успокоения и нажмите «Запомнить» — " +
            "автопилот будет работать с этой настройкой во всех миссиях.";
        p.Кнопка(_экран, 1000 - 232, 640 - 76, 200, 52, "<b>В МЕНЮ</b>", p.ЦветВтор, ВМеню, кегль: 22);
    }

    // ---------------- Настройка автопилота (стенд подбора ПИД) ----------------

    void ЭкранПИД()
    {
        if (!_готово && !_идётПрогон) return;   // идёт серия опытов — не мешаем
        VrPanel.Очистить(_экран);
        var p = _панель;
        Шапка("НАСТРОЙКА АВТОПИЛОТА ГЛУБИНЫ · СТУПЕНЬКА +2 м");
        if (_ап == null)
        {
            p.Текст(_экран, 32, 120, 936, 40, 20, p.ЦветПлохо).text = "На аппарате нет автопилота.";
            p.Кнопка(_экран, 1000 - 232, 640 - 76, 200, 52, "<b>В МЕНЮ</b>", p.ЦветВтор, ВМеню, кегль: 22);
            return;
        }
        var pid = _ап.depthPid;
        float x = 32, w = 430;
        p.Регулятор(_экран, x, 100, w, "Kp — тянет к уставке", 0f, 4f, 0.1f,
                    () => pid.kp, v => pid.kp = v, v => v.ToString("0.0#", Ru));
        p.Регулятор(_экран, x, 180, w, "Ki — добирает остаток", 0f, 1f, 0.05f,
                    () => pid.ki, v => pid.ki = v, v => v.ToString("0.0#", Ru));
        p.Регулятор(_экран, x, 260, w, "Kd — тормозит подход", 0f, 3f, 0.1f,
                    () => pid.kd, v => pid.kd = v, v => v.ToString("0.0#", Ru));
        p.Регулятор(_экран, x, 340, w, "Предел тяги автопилота", 0.2f, 1f, 0.05f,
                    () => _ап.maxAuthority, v => _ап.maxAuthority = v, v => Mathf.RoundToInt(v * 100f) + " %");
        p.Кнопка(_экран, x, 424, w, 44, $"<b>ЗАЩИТА ОТ НАСЫЩЕНИЯ: {(pid.антиНасыщение ? "ВКЛ" : "ВЫКЛ")}</b>", p.ЦветАкцент,
                 () => { pid.антиНасыщение = !pid.антиНасыщение; ЭкранПИД(); }, () => pid.антиНасыщение, 18);
        p.Кнопка(_экран, x, 480, w, 60, _идётПрогон ? "<b>ИДЁТ ПРОГОН…</b>" : "<b>СТУПЕНЬКА +2 м</b>", p.ЦветХорошо,
                 () => { if (!_идётПрогон) StartCoroutine(ПрогонПИД()); }, кегль: 24);

        // Справа: график (сейчас / прошлый / уставка) и таблица прогонов.
        float rx = 490, rw = 478;
        p.Текст(_экран, rx, 100, rw, 22, 15, p.ЦветВтор, перенос: false).text =
            $"<color=#{VrPanel.Hex(ЦветМодели)}>— последний</color>   <color=#888888>— прошлый</color>   " +
            $"<color=#{VrPanel.Hex(ЦветЭталона)}>— уставка</color>";
        if (_графикПИД != null) p.Картинка(_экран, rx, 124, rw, 200, _графикПИД);
        else p.Текст(_экран, rx, 200, rw, 40, 17, p.ЦветВтор, TextAlignmentOptions.Center).text = "Нажмите «Ступенька», чтобы увидеть переходную характеристику";
        p.Текст(_экран, rx, 328, rw, 18, 13, p.ЦветВтор, перенос: false).text = "0…30 с после команды · погружение, м (цель 2)";

        p.Текст(_экран, rx, 352, rw, 18, 13, p.ЦветВтор, перенос: false).text =
            "<pos=0%>Kp / Ki / Kd<pos=32%>защ.<pos=42%>пред.<pos=55%>проскок<pos=71%>t уст.<pos=86%>ошибка";
        int n = _прогоны.Count;
        for (int i = 0; i < 7 && i < n; i++)
        {
            var пр = _прогоны[n - 1 - i];
            var пк = пр.О.Показатели;
            float σ = пк.Count > 0 ? пк[0].Модель : float.NaN, tу = пк.Count > 2 ? пк[2].Модель : float.NaN, e = пк.Count > 3 ? пк[3].Модель : float.NaN;
            Color c = float.IsNaN(σ) ? p.ЦветВтор : σ <= 10f ? p.ЦветХорошо : σ <= 25f ? p.ЦветСредне : p.ЦветПлохо;
            p.Текст(_экран, rx, 372 + i * 20, rw, 20, 15, i == 0 ? p.ЦветТекста : p.ЦветВтор, перенос: false).text =
                $"<pos=0%>{Ч(пр.Kp, "0.0#")} / {Ч(пр.Ki, "0.0#")} / {Ч(пр.Kd, "0.0#")}<pos=32%>{(пр.Защита ? "да" : "нет")}" +
                $"<pos=42%>{Mathf.RoundToInt(пр.Предел * 100f)} %<pos=55%><color=#{VrPanel.Hex(c)}>{Ч(σ, "0.#")} %</color>" +
                $"<pos=71%>{(float.IsNaN(tу) ? "—" : Ч(tу, "0.0") + " с")}<pos=86%>{Ч(e * 100f, "0")} см";
        }
        _статус = p.Текст(_экран, rx, 518, rw, 24, 16, p.ЦветСредне, перенос: false);
        if (!_идётПрогон) _статус.text = _сообщениеПИД ?? "";

        float ny = 640 - 76;
        p.Кнопка(_экран, 32, ny, 200, 52, "<b>ВСЕ ОПЫТЫ</b>", p.ЦветАкцент, () => { if (!_идётПрогон) StartCoroutine(Серия()); }, кегль: 20);
        p.Кнопка(_экран, 242, ny, 200, 52, "<b>ЗАПОМНИТЬ</b>", p.ЦветХорошо, () =>
        {
            _ап.ЗапомнитьНастройку();
            _сообщениеПИД = $"Запомнено: Kp {Ч(pid.kp, "0.0#")}, Ki {Ч(pid.ki, "0.0#")}, Kd {Ч(pid.kd, "0.0#")} — во всех миссиях";
            ЭкранПИД();
        }, кегль: 20);
        p.Кнопка(_экран, 452, ny, 200, 52, "<b>ИСХОДНЫЕ</b>", p.ЦветВтор, () =>
        {
            pid.kp = _исхKp; pid.ki = _исхKi; pid.kd = _исхKd; pid.антиНасыщение = _исхЗащита; _ап.maxAuthority = _исхПредел;
            _сообщениеПИД = "Вернул значения, с которыми зашли на стенд";
            ЭкранПИД();
        }, кегль: 20);
        p.Кнопка(_экран, 1000 - 232, ny, 200, 52, "<b>В МЕНЮ</b>", p.ЦветВтор, ВМеню, кегль: 22);
    }

    IEnumerator ПрогонПИД()
    {
        _идётПрогон = true; _готово = false;
        if (_папкаПИД == null)
        {
            _папкаПИД = Path.Combine(Application.persistentDataPath, "Испытания", "ПИД_" + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss"));
            Directory.CreateDirectory(_папкаПИД);
        }
        var pid = _ап.depthPid;
        var пр = new Прогон { Kp = pid.kp, Ki = pid.ki, Kd = pid.kd, Защита = pid.антиНасыщение, Предел = _ап.maxAuthority };
        _номер = 0; _всего = 1;
        ЭкранПИД();
        var куда = new List<Опыт>();
        string файл = $"ступенька_{_прогоны.Count + 1:00}_Kp{pid.kp:0.##}_Ki{pid.ki:0.##}_Kd{pid.kd:0.##}".Replace(',', '.');
        yield return СтупенькаГлубины("Ступенька глубины +2 м", файл, куда, _папкаПИД);
        _команда = Vector4.zero;
        if (куда.Count > 0)
        {
            пр.О = куда[0];
            var прошлый = _прогоны.Count > 0 ? _прогоны[_прогоны.Count - 1].О : null;
            _прогоны.Add(пр);
            if (_графикПИД != null) Destroy(_графикПИД);
            // В приращении от глубины в момент команды: каждый прогон начинается с чуть
            // другой абсолютной глубины, а сравнивать нужно форму — 0 → 2 м.
            List<float> Δ(Опыт о, List<float> x) { if (о == null) return null; var r = new List<float>(x.Count); foreach (var v in x) r.Add(v - о.Начало); return r; }
            _графикПИД = НарисоватьЛинии(600, 250, false,
                (пр.О.t, Δ(пр.О, пр.О.эталон), ЦветЭталона, 3),
                (прошлый?.t, Δ(прошлый, прошлый?.модель), new Color(0.53f, 0.53f, 0.53f), 2),
                (пр.О.t, Δ(пр.О, пр.О.модель), ЦветМодели, 3));
            try { File.WriteAllBytes(Path.Combine(_папкаПИД, файл + ".png"), _графикПИД.EncodeToPNG()); } catch { }
            СохранитьПрогоны();
        }
        _сообщениеПИД = null;
        _идётПрогон = false; _готово = true;
        ЭкранПИД();
    }

    // Таблица всех прогонов настройки — для главы о системе управления («до / после»).
    void СохранитьПрогоны()
    {
        try
        {
            var sb = new StringBuilder("прогон;Kp;Ki;Kd;защита_от_насыщения;предел_тяги;перерегулирование_проц;время_нарастания_с;время_установления_с;ошибка_м\n");
            for (int i = 0; i < _прогоны.Count; i++)
            {
                var пр = _прогоны[i]; var пк = пр.О.Показатели;
                float П(int k) => k < пк.Count ? пк[k].Модель : float.NaN;
                sb.Append(i + 1).Append(';').Append(Ч(пр.Kp)).Append(';').Append(Ч(пр.Ki)).Append(';').Append(Ч(пр.Kd)).Append(';')
                  .Append(пр.Защита ? "да" : "нет").Append(';').Append(Ч(пр.Предел)).Append(';')
                  .Append(Ч(П(0), "0.##")).Append(';').Append(Ч(П(1), "0.##")).Append(';').Append(Ч(П(2), "0.##")).Append(';').Append(Ч(П(3), "0.###")).Append('\n');
            }
            File.WriteAllText(Path.Combine(_папкаПИД, "прогоны.csv"), sb.ToString(), new UTF8Encoding(true));
        }
        catch (Exception e) { Debug.LogWarning($"[Испытания] Не удалось записать прогоны: {e.Message}"); }
    }

    void ВМеню()
    {
        Идут = false;
        MainMenu.ПропуститьМенюОдинРаз = false;
        SceneManager.LoadScene(0);   // кабина → Main догрузится → главное меню
    }
}

// =============================================================================
// CurrentFieldVisualizer.cs
// -----------------------------------------------------------------------------
// Наглядная (отладочная/учебная) визуализация поля течения WaterCurrent.
// Включается/выключается клавишей (по умолчанию F9). Рисует:
//
//   1. ЛИНИИ ТОКА вокруг аппарата — из узлов сетки, привязанной к миру, поле
//      интегрируется методом Рунге—Кутты 2-го порядка: x(t+Δ) = x + Δ·v(x + ½Δ·v(x)).
//      Длина линии пропорциональна скорости воды (время интегрирования одно на
//      все линии), хвост прозрачный, голова яркая — видно направление. Цвет —
//      цвет СЛОЯ течения, в котором начинается линия. Проплывающий вихрь виден
//      как закрученные линии, сдвиг между слоями — как разворот линий по высоте.
//
// Клавиша F9 (F3 — доплеровский лаг, DopplerInstrumentsVisualizer).
//
//   2. ВЕРТИКАЛЬНЫЙ ПРОФИЛЬ рядом с аппаратом — стрелки среднего потока (без
//      вихрей) через каждые ШагПрофиля_м по высоте с подписями слоёв. Так
//      выглядит экран профилографа течений ADCP: поток по ячейкам глубины.
//
// Линии рисуются шейдером без тумана и освещения — видны в мутной воде.
// Компонент добавляется автоматически из RovController (Start).
// =============================================================================

using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
public class CurrentFieldVisualizer : MonoBehaviour
{
    [Tooltip("Клавиша показа/скрытия визуализации течения")]
    public Key клавиша = Key.F9;
    public bool Показывать = false;

    [Header("Линии тока")]
    [Tooltip("Число узлов сетки по каждой оси (N×N×N линий)")]
    [Range(2, 8)] public int УзловНаОсь = 5;
    [Tooltip("Шаг сетки, м")]
    public float ШагСетки_м = 3f;
    [Tooltip("Время интегрирования линии, с: длина линии = скорость × это время")]
    public float ВремяЛинии_с = 14f;
    [Range(4, 40)] public int ТочекНаЛинию = 16;
    public float ТолщинаЛинии = 0.035f;

    [Header("Профиль по глубине (как ADCP)")]
    public bool ПоказыватьПрофиль = true;
    [Tooltip("Смещение колонки профиля от аппарата (в осях аппарата), м")]
    public Vector3 СмещениеПрофиля = new Vector3(3f, 0f, 5f);
    [Tooltip("Шаг стрелок по высоте, м")]
    public float ШагПрофиля_м = 2f;
    [Tooltip("Сколько метров профиля выше и ниже аппарата")]
    public float ВысотаПрофиля_м = 30f;
    [Tooltip("Масштаб стрелки: метров длины на 1 м/с скорости")]
    public float МасштабСтрелки = 12f;

    [Tooltip("Частота пересчёта, Гц")]
    public float ЧастотаОбновления_Гц = 10f;

    private RovController _rov;
    private Transform _корень;
    private readonly List<LineRenderer> _линии = new List<LineRenderer>();
    private readonly List<LineRenderer> _стрелки = new List<LineRenderer>();
    private readonly List<TextMeshPro> _подписи = new List<TextMeshPro>();
    private LineRenderer _ось;
    private Material _мат;
    private float _след;
    private Camera _камера;
    private int _слой;

    void Awake() { _rov = GetComponent<RovController>(); }

    void OnDestroy() { if (_корень != null) Destroy(_корень.gameObject); }

    void Update()
    {
        if (клавиша != Key.None && Keyboard.current != null && Keyboard.current[KeyBindings.K(клавиша)].wasPressedThisFrame)
        {
            Показывать = !Показывать;
            var поле = _rov != null ? _rov.Течение : null;
            if (Показывать && поле != null)
            {
                var слой = поле.СлойВТочке(transform.position);
                Vector3 v = поле.Скорость(transform.position);
                Debug.Log($"[Течение] визуализация ВКЛ. Аппарат в слое «{(слой != null ? слой.Название : "—")}», " +
                          $"вода {v.magnitude:F2} м/с ({v.magnitude * 1.9438f:F2} уз)");
            }
        }

        if (_корень != null) _корень.gameObject.SetActive(Показывать);
        if (!Показывать || _rov == null || _rov.Течение == null) return;

        if (_корень == null) Построить();
        if (Time.time < _след) return;
        _след = Time.time + 1f / Mathf.Max(1f, ЧастотаОбновления_Гц);

        ОбновитьЛинии(_rov.Течение);
        ОбновитьПрофиль(_rov.Течение);
    }

    void LateUpdate()
    {
        // Подписи всегда лицом к камере аппарата (её видит оператор на мониторе).
        if (!Показывать || _подписи.Count == 0) return;
        if (_камера == null)
            foreach (var c in FindObjectsByType<Camera>(FindObjectsSortMode.None))
                if (c.targetTexture != null && c.enabled) { _камера = c; break; }
        if (_камера == null) _камера = Camera.main;
        if (_камера == null) return;
        foreach (var p in _подписи)
            if (p.gameObject.activeSelf)
                p.transform.rotation = Quaternion.LookRotation(p.transform.position - _камера.transform.position);
    }

    // ---------------------------------------------------------------- линии тока
    void ОбновитьЛинии(WaterCurrent поле)
    {
        int n = УзловНаОсь;
        int всего = n * n * n;
        while (_линии.Count < всего) _линии.Add(НоваяЛиния("Линия тока", ТолщинаЛинии));
        for (int i = всего; i < _линии.Count; i++) _линии[i].gameObject.SetActive(false);

        // Сетка привязана к МИРУ (узлы кратны шагу), а не к аппарату: при движении
        // аппарата линии не «едут» вместе с ним — видно поле, а не аппарат.
        Vector3 c = transform.position;
        Vector3 база = new Vector3(Mathf.Round(c.x / ШагСетки_м), Mathf.Round(c.y / ШагСетки_м), Mathf.Round(c.z / ШагСетки_м)) * ШагСетки_м;
        float пол = (n - 1) * 0.5f;
        float h = ВремяЛинии_с / (ТочекНаЛинию - 1);
        var точки = new Vector3[ТочекНаЛинию];

        int k = 0;
        for (int ix = 0; ix < n; ix++)
        for (int iy = 0; iy < n; iy++)
        for (int iz = 0; iz < n; iz++, k++)
        {
            Vector3 p = база + new Vector3(ix - пол, iy - пол, iz - пол) * ШагСетки_м;
            var lr = _линии[k];
            if (p.y > RovSystems.уровеньПоверхности_Y) { lr.gameObject.SetActive(false); continue; }
            lr.gameObject.SetActive(true);

            float vMax = 0f;
            точки[0] = p;
            for (int j = 1; j < ТочекНаЛинию; j++)
            {
                Vector3 v1 = поле.Скорость(p);
                Vector3 v2 = поле.Скорость(p + v1 * (0.5f * h));   // RK2 (средняя точка)
                p += v2 * h;
                точки[j] = p;
                vMax = Mathf.Max(vMax, v2.magnitude);
            }
            lr.positionCount = ТочекНаЛинию;
            lr.SetPositions(точки);

            var слой = поле.СлойВТочке(точки[0]);
            Color цвет = слой != null ? слой.Цвет : Color.cyan;
            // Ярче там, где течёт быстрее: 0.05 м/с — тускло, ≥0.4 м/с — полная яркость.
            float ярк = Mathf.Lerp(0.35f, 1f, Mathf.InverseLerp(0.05f, 0.4f, vMax));
            Color голова = цвет * ярк; голова.a = 0.9f;
            Color хвост = цвет; хвост.a = 0f;
            lr.startColor = хвост;
            lr.endColor = голова;
        }
    }

    // ----------------------------------------------------------- профиль (ADCP)
    void ОбновитьПрофиль(WaterCurrent поле)
    {
        if (_ось != null) _ось.gameObject.SetActive(ПоказыватьПрофиль);
        if (!ПоказыватьПрофиль) { foreach (var s in _стрелки) s.gameObject.SetActive(false); foreach (var t in _подписи) t.gameObject.SetActive(false); return; }

        // Колонка стоит сбоку-впереди аппарата, горизонтально (без наклона корпуса).
        Vector3 вперёд = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
        if (вперёд.sqrMagnitude < 1e-4f) вперёд = Vector3.forward;
        Vector3 вправо = Vector3.Cross(Vector3.up, вперёд);
        Vector3 низ0 = transform.position + вправо * СмещениеПрофиля.x + вперёд * СмещениеПрофиля.z;

        float yПоверх = RovSystems.уровеньПоверхности_Y;
        float yВерх = Mathf.Min(низ0.y + ВысотаПрофиля_м, yПоверх);
        float yНиз = низ0.y - ВысотаПрофиля_м;
        if (RovSystems.hasBottomEcho) yНиз = Mathf.Max(yНиз, transform.position.y - RovSystems.altitudeTrue_m + 0.3f);

        _ось.positionCount = 2;
        _ось.SetPosition(0, new Vector3(низ0.x, yНиз, низ0.z));
        _ось.SetPosition(1, new Vector3(низ0.x, yВерх, низ0.z));

        int шагов = Mathf.Max(1, Mathf.FloorToInt((yВерх - yНиз) / Mathf.Max(0.25f, ШагПрофиля_м)) + 1);
        while (_стрелки.Count < шагов) _стрелки.Add(НоваяЛиния("Стрелка профиля", ТолщинаЛинии * 1.6f));
        for (int i = шагов; i < _стрелки.Count; i++) _стрелки[i].gameObject.SetActive(false);

        WaterCurrent.СлойТечения прошлый = null;
        int подпись = 0;
        for (int i = 0; i < шагов; i++)
        {
            Vector3 p = new Vector3(низ0.x, yНиз + i * ШагПрофиля_м, низ0.z);
            Vector3 v = поле.СреднийПоток(p);
            var lr = _стрелки[i];
            lr.gameObject.SetActive(true);

            Vector3 конец = p + v * МасштабСтрелки;
            Vector3 dir = v.sqrMagnitude > 1e-6f ? v.normalized : вперёд;
            Vector3 бок = Vector3.Cross(Vector3.up, dir);
            float крыло = Mathf.Clamp(v.magnitude * МасштабСтрелки * 0.25f, 0.15f, 0.6f);
            // Полилиния: основание → остриё → крыло → остриё → крыло.
            lr.positionCount = 5;
            lr.SetPosition(0, p);
            lr.SetPosition(1, конец);
            lr.SetPosition(2, конец - dir * крыло + бок * крыло * 0.6f);
            lr.SetPosition(3, конец);
            lr.SetPosition(4, конец - dir * крыло - бок * крыло * 0.6f);

            var слой = поле.СлойВТочке(p);
            Color цвет = слой != null ? слой.Цвет : Color.white; цвет.a = 0.95f;
            lr.startColor = lr.endColor = цвет;

            // Подпись — у первой стрелки каждого нового слоя.
            if (слой != null && слой != прошлый)
            {
                var t = Подпись(подпись++);
                t.transform.position = p + Vector3.up * 0.6f - вправо * 0.4f;
                float курс = (Mathf.Atan2(v.x, v.z) * Mathf.Rad2Deg + 360f) % 360f;
                t.text = $"{слой.Название}\n{v.magnitude:F2} м/с  →{курс:F0}°";
                t.color = цвет;
                прошлый = слой;
            }
        }
        for (int i = подпись; i < _подписи.Count; i++) _подписи[i].gameObject.SetActive(false);
    }

    // ------------------------------------------------------------------ служебное
    void Построить()
    {
        _корень = new GameObject("Визуализация течения (F9)").transform;
        // Камера монитора рисует только свой слой (Underwater) — иначе линий на мониторе не видно.
        _слой = LayerMask.NameToLayer("Underwater");
        if (_слой < 0) _слой = 0;
        _корень.gameObject.layer = _слой;
        Shader sh = Shader.Find("Sprites/Default");   // вершинный цвет, без тумана и освещения
        _мат = new Material(sh) { name = "CurrentViz (auto)" };
        _ось = НоваяЛиния("Ось профиля", ТолщинаЛинии * 0.8f);
        _ось.startColor = _ось.endColor = new Color(1f, 1f, 1f, 0.35f);
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
        lr.alignment = LineAlignment.View;
        return lr;
    }

    TextMeshPro Подпись(int i)
    {
        while (_подписи.Count <= i)
        {
            var go = new GameObject("Подпись слоя");
            go.layer = _слой;
            go.transform.SetParent(_корень, false);
            var t = go.AddComponent<TextMeshPro>();
            t.fontSize = 3f;
            t.alignment = TextAlignmentOptions.Right;
            t.rectTransform.sizeDelta = new Vector2(8f, 2f);
            t.rectTransform.pivot = new Vector2(1f, 0.5f);
            _подписи.Add(t);
        }
        _подписи[i].gameObject.SetActive(true);
        return _подписи[i];
    }
}

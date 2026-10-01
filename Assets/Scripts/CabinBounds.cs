using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.SceneManagement;

// Не даёт оператору выйти из кабины: головой сквозь стены, под пол или над потолком
// (шагом по комнате, наклоном или перемещением в XR-эмуляторе).
//
// Границы определяются сами при загрузке кабины: из стартовой точки рига лучи
// вперёд/назад/вбок/вверх/вниз до коллайдеров. Берётся САМОЕ ДАЛЬНЕЕ попадание
// в пределах ДлинаЛуча — так пульт, кресло и рычаги на пути не считаются стеной.
// Если луч ничего не нашёл (у стены нет коллайдера) — запасной размер.
// Коробка повёрнута по рысканию рига на старте, т.е. по осям самой кабины.
//
// Внутри коробки голова ещё и не проходит сквозь предметы кабины (пульт, мониторы,
// колонки на стойках): движение головы за кадр проверяется сферой (SphereCast) от
// прошлого положения к новому; на пути коллайдер — голова останавливается у
// поверхности и скользит вдоль неё (как у контроллера персонажа), а риг сдвигается.
// Если голова уже оказалась внутри (старт, телепорт) — выйти можно, войти — нет.
// У предметов БЕЗ коллайдера (часть кабины — префабы без физики) берётся их габаритная
// коробка (меш в своих осях) и голова из неё выталкивается. Физических коллайдеров
// не добавляем нарочно: они перехватывали бы лучи контроллеров к экранам и кнопкам.
//
// В меню (MainMenu.Открыто) не работает: там свой «поводок» у точки обзора.
public class CabinBounds : MonoBehaviour
{
    const string ИмяКабины = "Cabin";
    const float ДлинаЛуча = 8f;
    const float ОтступОтСтен = 0.25f;     // голова не ближе к стене, м
    const float ОтступОтПотолка = 0.15f;
    const float МинВысотаГоловы = 0.35f;  // над полом: можно присесть, но не «уйти» в пол
    const float ЗапаснойРадиус = 1.5f, ЗапаснаяВысота = 2.5f;
    const float РадиусГоловы = 0.12f, Зазор = 0.01f;

    XROrigin _риг;
    Vector3 _центр; Quaternion _поворот;
    Vector3 _мин, _макс;   // в осях кабины относительно _центр (на уровне пола)
    bool _готово;
    Vector3? _прошлаяГолова;   // null — после меню/загрузки: первый кадр без проверки пути
    readonly System.Collections.Generic.List<(Transform t, Bounds b)> _коробки = new System.Collections.Generic.List<(Transform, Bounds)>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Создать()
    {
        if (FindFirstObjectByType<CabinBounds>() != null) return;
        var go = new GameObject("CabinBounds");
        DontDestroyOnLoad(go);
        var г = go.AddComponent<CabinBounds>();
        var кабина = SceneManager.GetSceneByName(ИмяКабины);
        if (кабина.IsValid() && кабина.isLoaded) г.НаЗагрузку(кабина, LoadSceneMode.Single);
    }

    void OnEnable() => SceneManager.sceneLoaded += НаЗагрузку;
    void OnDisable() => SceneManager.sceneLoaded -= НаЗагрузку;

    void НаЗагрузку(Scene сцена, LoadSceneMode режим)
    {
        if (сцена.name != ИмяКабины) return;
        _готово = false;
        _прошлаяГолова = null;
        _риг = null;
        foreach (var root in сцена.GetRootGameObjects())
        {
            _риг = root.GetComponentInChildren<XROrigin>();
            if (_риг != null) break;
        }
        if (_риг == null) { Debug.LogWarning("[Кабина] Не найден XR Origin — границы кабины не заданы."); return; }
        Измерить();
        СобратьКоробки(сцена);
    }

    // Меши кабины без коллайдера: их коробки (в своих осях). Пропускаем риг, UI,
    // огромное (оболочку комнаты — её держит коробка кабины) и совсем мелкое.
    void СобратьКоробки(Scene сцена)
    {
        _коробки.Clear();
        foreach (var root in сцена.GetRootGameObjects())
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(false))
            {
                var t = mf.transform;
                if (mf.sharedMesh == null || t.IsChildOf(_риг.transform)) continue;
                if (mf.GetComponent<MeshRenderer>() == null || mf.GetComponentInParent<Canvas>() != null) continue;
                if (mf.GetComponent<Collider>() != null) continue;   // с коллайдером — через SphereCast
                Vector3 размер = Vector3.Scale(mf.sharedMesh.bounds.size, t.lossyScale);
                размер = new Vector3(Mathf.Abs(размер.x), Mathf.Abs(размер.y), Mathf.Abs(размер.z));
                float макс = Mathf.Max(размер.x, Mathf.Max(размер.y, размер.z));
                if (макс > 3f || макс < 0.05f) continue;
                // Коробка вокруг места оператора — это оболочка комнаты (или её часть), а не предмет:
                // иначе голову выталкивало бы из кабины наружу.
                var bb = mf.sharedMesh.bounds;
                if (bb.Contains(t.InverseTransformPoint(_центр + Vector3.up * 1.0f)) ||
                    bb.Contains(t.InverseTransformPoint(_центр + Vector3.up * 1.6f))) continue;
                _коробки.Add((t, mf.sharedMesh.bounds));
            }
        Debug.Log($"[Кабина] Предметов без коллайдера, в которые нельзя войти головой: {_коробки.Count}.");
    }

    // Вытолкнуть сферу головы из коробок предметов без коллайдера.
    Vector3 ВытолкнутьИзКоробок(Vector3 голова)
    {
        for (int k = 0; k < _коробки.Count; k++)
        {
            var (t, b) = _коробки[k];
            if (t == null || !t.gameObject.activeInHierarchy) continue;
            Vector3 л = t.InverseTransformPoint(голова);
            Vector3 ближ = b.ClosestPoint(л);
            Vector3 ближМир = t.TransformPoint(ближ);
            Vector3 от = голова - ближМир;
            float дист = от.magnitude;
            if (л != ближ)
            {
                if (дист < РадиусГоловы && дист > 1e-5f) голова += от / дист * (РадиусГоловы - дист);
                continue;
            }
            // Центр головы внутри коробки — наружу через ближайшую грань (в метрах мира).
            Vector3 масштаб = t.lossyScale;
            float лучше = float.MaxValue; Vector3 выход = Vector3.zero;
            for (int ось = 0; ось < 3; ось++)
            {
                float м = Mathf.Abs(масштаб[ось]);
                float доМин = (л[ось] - b.min[ось]) * м, доМакс = (b.max[ось] - л[ось]) * м;
                Vector3 осьМир = t.TransformDirection(Vector3.right * (ось == 0 ? 1 : 0) + Vector3.up * (ось == 1 ? 1 : 0) + Vector3.forward * (ось == 2 ? 1 : 0)).normalized;
                if (доМин < лучше) { лучше = доМин; выход = -осьМир * (доМин + РадиусГоловы); }
                if (доМакс < лучше) { лучше = доМакс; выход = осьМир * (доМакс + РадиусГоловы); }
            }
            голова += выход;
        }
        return голова;
    }

    // Риг на старте стоит на полу кабины — от этой точки и меряем.
    void Измерить()
    {
        Physics.SyncTransforms();
        var t = _риг.transform;
        _центр = t.position;
        _поворот = Quaternion.Euler(0f, t.eulerAngles.y, 0f);
        Vector3 из = _центр + Vector3.up * 1.0f;

        float вперёд = Луч(из, _поворот * Vector3.forward, ЗапаснойРадиус);
        float назад  = Луч(из, _поворот * Vector3.back,    ЗапаснойРадиус);
        float вправо = Луч(из, _поворот * Vector3.right,   ЗапаснойРадиус);
        float влево  = Луч(из, _поворот * Vector3.left,    ЗапаснойРадиус);
        float вверх  = Луч(из, Vector3.up, ЗапаснаяВысота - 1f) + 1f;   // от пола
        float вниз   = Луч(из, Vector3.down, 1f, ближайший: true) - 1f;  // пол — БЛИЖАЙШЕЕ снизу (≈0 у точки рига)

        _мин = new Vector3(-влево + ОтступОтСтен, -вниз + МинВысотаГоловы, -назад + ОтступОтСтен);
        _макс = new Vector3(вправо - ОтступОтСтен, вверх - ОтступОтПотолка, вперёд - ОтступОтСтен);
        // Слишком тесно (лучи упёрлись во что-то рядом) — хотя бы полметра свободы.
        for (int i = 0; i < 3; i++)
            if (_макс[i] - _мин[i] < 0.5f) { float с = (_макс[i] + _мин[i]) * 0.5f; _мин[i] = с - 0.25f; _макс[i] = с + 0.25f; }
        _готово = true;
        Debug.Log($"[Кабина] Границы от точки рига: вперёд {вперёд:F2}, назад {назад:F2}, вбок {влево:F2}/{вправо:F2}, потолок {вверх:F2}, пол {вниз:F2} м.");
    }

    float Луч(Vector3 из, Vector3 куда, float запас, bool ближайший = false)
    {
        float дальше = -1f, ближе = float.MaxValue;
        foreach (var h in Physics.RaycastAll(из, куда, ДлинаЛуча, ~0, QueryTriggerInteraction.Ignore))
        {
            if (h.collider.transform.IsChildOf(_риг.transform)) continue;
            if (h.collider.gameObject.scene != _риг.gameObject.scene) continue;   // только кабина
            if (h.distance > дальше) дальше = h.distance;
            if (h.distance < ближе) ближе = h.distance;
        }
        if (дальше < 0f) return запас;
        return ближайший ? ближе : дальше;
    }

    // После локомоции и трекинга этого кадра.
    void LateUpdate()
    {
        if (!_готово || _риг == null || _риг.Camera == null || MainMenu.Открыто) { _прошлаяГолова = null; return; }
        Vector3 голова = _риг.Camera.transform.position;

        // 1) Предметы кабины: путь головы за кадр.
        if (_прошлаяГолова.HasValue)
        {
            Vector3 можно = ПутьБезПрохода(_прошлаяГолова.Value, голова);
            if (можно != голова) { _риг.transform.position += можно - голова; голова = можно; }
        }
        Vector3 снаружи = ВытолкнутьИзКоробок(голова);
        if (снаружи != голова) { _риг.transform.position += снаружи - голова; голова = снаружи; }

        // 2) Стены, пол, потолок: коробка кабины.
        Vector3 л = Quaternion.Inverse(_поворот) * (голова - _центр);
        Vector3 к = new Vector3(Mathf.Clamp(л.x, _мин.x, _макс.x), Mathf.Clamp(л.y, _мин.y, _макс.y), Mathf.Clamp(л.z, _мин.z, _макс.z));
        if (к != л)
        {
            Vector3 сдвиг = _поворот * (к - л);
            _риг.transform.position += сдвиг;
            голова += сдвиг;
        }
        _прошлаяГолова = голова;
    }

    // Куда голова может дойти из «из» в сторону «в», не входя в коллайдеры кабины.
    // До трёх отражений: упёрлись — остаток движения проецируется на плоскость
    // поверхности, чтобы голова скользила вдоль стола, а не залипала.
    Vector3 ПутьБезПрохода(Vector3 из, Vector3 в)
    {
        Vector3 d = в - из;
        for (int i = 0; i < 3; i++)
        {
            float L = d.magnitude;
            if (L < 1e-5f) return из;
            Vector3 dir = d / L;
            if (!БлижайшееКасание(из, dir, L + Зазор, out RaycastHit h)) return из + d;
            float пройдено = Mathf.Max(0f, h.distance - Зазор);
            из += dir * пройдено;
            d = Vector3.ProjectOnPlane(dir * (L - пройдено), h.normal);
        }
        return из;
    }

    bool БлижайшееКасание(Vector3 из, Vector3 dir, float L, out RaycastHit лучшее)
    {
        лучшее = default;
        float мин = float.MaxValue;
        foreach (var h in Physics.SphereCastAll(из, РадиусГоловы, dir, L, ~0, QueryTriggerInteraction.Ignore))
        {
            if (h.distance <= 0f) continue;   // уже внутри этого коллайдера — выйти можно
            if (h.collider.transform.IsChildOf(_риг.transform)) continue;
            if (h.collider.gameObject.scene != _риг.gameObject.scene) continue;
            if (h.distance < мин) { мин = h.distance; лучшее = h; }
        }
        return мин < float.MaxValue;
    }
}

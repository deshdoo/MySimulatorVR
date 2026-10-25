using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Inputs;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Casters;

// Контроллеры, руки и лучи — поверх окон, которые рисуются поверх кабины (меню, настройки
// в миссии, итоги: ZTest Always, sortingOrder 100). Такое окно не пишет глубину и идёт в
// прозрачной очереди после всего остального — поэтому закрывало и контроллеры перед ним,
// и луч, которым в него целятся. Пока такое окно открыто, детали контроллеров и рук
// рисуются после него: непрозрачные переходят в прозрачную очередь (Transparent+100),
// всем ставится порядок сортировки выше окна. Проверка глубины у них своя, так что кабина
// их по-прежнему закрывает, а окно — только когда они за ним (пластина ниже). Окно
// закрыли — всё возвращается как было.
//
// Материалы рук (SkinnedMeshRenderer) не трогаются: их раздаёт РукиПризраки, и они уже
// прозрачные — им хватает порядка сортировки.
//
// Чтобы окно вело себя как твёрдая панель, а не как картинка, наклеенная на глаза, за
// фоном окна стоит невидимая пластина (Custom/WindowDepth): она пишет глубину на плоскости
// окна. Контроллер или рука перед окном видны, ушли за окно — окно их закрывает, как
// настоящая панель (раньше они рисовались поверх окна, даже пройдя сквозь него, и было не
// понять, где рука). Порядок: окно (100) → пластина (150) → контроллеры и руки (200).
//
// Луч контроллера, пока такое окно открыто, видит только слои окон: окно стоит поверх
// кабины лишь на экране, а в мире между контроллером и окном бывают пульт и мониторы — луч
// упирался в них, и кнопки окна не нажимались. Окно закрыли — маска луча прежняя.
public class КонтроллерыНадОкнами : MonoBehaviour
{
    const int ПорядокНадОкном = 200, ОчередьНадОкном = 3100, ПорядокПластины = 150;

    static readonly List<GameObject> _окна = new List<GameObject>();
    public static void Окно(GameObject корень) { if (корень != null && !_окна.Contains(корень)) _окна.Add(корень); }

    class Запись { public int Порядок; public Material[] Маты; public int[] Очереди; }
    readonly Dictionary<Renderer, Запись> _записи = new Dictionary<Renderer, Запись>();
    readonly Dictionary<GameObject, Transform> _пластины = new Dictionary<GameObject, Transform>();
    readonly Dictionary<Object, LayerMask> _маскиЛучей = new Dictionary<Object, LayerMask>();
    Mesh _квадрат;
    Material _матПластины;
    int _маскаОкон;
    bool _поверх;
    float _следПоиск;
    static КонтроллерыНадОкнами _я;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Запуск()
    {
        if (_я != null) return;
        var go = new GameObject("КонтроллерыНадОкнами");
        DontDestroyOnLoad(go);
        _я = go.AddComponent<КонтроллерыНадОкнами>();
    }

    void LateUpdate()
    {
        _окна.RemoveAll(g => g == null);
        bool нужно = false;
        int маска = 0;
        foreach (var g in _окна)
        {
            if (!g.activeInHierarchy) continue;
            нужно = true;
            маска |= 1 << g.layer;
            Пластина(g);
        }
        if (нужно != _поверх)
        {
            _поверх = нужно;
            _маскаОкон = маска;
            if (нужно) Собрать();
            foreach (var пара in _записи) Применить(пара.Key, пара.Value, нужно);
            if (!нужно) ВернутьЛучи();
            _следПоиск = Time.unscaledTime + 0.5f;
        }
        else if (нужно && (Time.unscaledTime >= _следПоиск || маска != _маскаОкон))
        {
            _следПоиск = Time.unscaledTime + 0.5f;   // модели контроллеров появляются, когда их увидела гарнитура
            _маскаОкон = маска;
            Собрать();
        }
    }

    // Пластина глубины окна: размер и место — по RectTransform окна (его размер задают
    // после создания, поэтому сверяется каждый кадр).
    void Пластина(GameObject окно)
    {
        if (!(окно.transform is RectTransform rt)) return;
        if (!_пластины.TryGetValue(окно, out var п) || п == null)
        {
            if (_матПластины == null)
            {
                var sh = Shader.Find("Custom/WindowDepth");
                if (sh == null) return;
                _матПластины = new Material(sh) { name = "Глубина окна (auto)", renderQueue = 3000 };
            }
            if (_квадрат == null) _квадрат = Квадрат();
            var go = new GameObject("Глубина окна");
            go.AddComponent<MeshFilter>().sharedMesh = _квадрат;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = _матПластины;
            r.sortingOrder = ПорядокПластины;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            п = go.transform;
            п.SetParent(rt, false);
            _пластины[окно] = п;
        }
        Rect rect = rt.rect;
        п.gameObject.layer = окно.layer;
        п.localPosition = rect.center;
        п.localRotation = Quaternion.identity;
        п.localScale = new Vector3(rect.width, rect.height, 1f);
        var группа = окно.GetComponent<CanvasGroup>();   // окно итогов плавно появляется и тает
        п.GetComponent<MeshRenderer>().enabled = группа == null || группа.alpha > 0.3f;
    }

    static Mesh Квадрат()
    {
        var m = new Mesh { name = "Пластина окна" };
        m.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f) };
        m.triangles = new[] { 0, 2, 1, 0, 3, 2 };
        m.RecalculateBounds();
        return m;
    }

    // Лучи контроллеров — только в окна (см. шапку). Исходные маски запоминаются один раз.
    void ЛучиВОкна(Component корень)
    {
        foreach (var нф in корень.GetComponentsInChildren<NearFarInteractor>(true))
            if (нф.farInteractionCaster is CurveInteractionCaster к) МаскаЛуча(к, к.raycastMask, м => к.raycastMask = м);
        foreach (var луч in корень.GetComponentsInChildren<XRRayInteractor>(true))
            МаскаЛуча(луч, луч.raycastMask, м => луч.raycastMask = м);
    }

    void МаскаЛуча(Object кто, LayerMask была, System.Action<LayerMask> задать)
    {
        if (!_маскиЛучей.ContainsKey(кто)) _маскиЛучей[кто] = была;
        задать(_маскаОкон);
    }

    void ВернутьЛучи()
    {
        foreach (var пара in _маскиЛучей)
        {
            if (пара.Key == null) continue;
            if (пара.Key is CurveInteractionCaster к) к.raycastMask = пара.Value;
            else if (пара.Key is XRRayInteractor луч) луч.raycastMask = пара.Value;
        }
        _маскиЛучей.Clear();
    }

    // Детали контроллеров и рук рига (вместе с лучами — они дочерние у контроллеров).
    void Собрать()
    {
        var мёртвые = new List<Renderer>();
        foreach (var r in _записи.Keys) if (r == null) мёртвые.Add(r);
        foreach (var r in мёртвые) _записи.Remove(r);
        foreach (var м in FindObjectsByType<XRInputModalityManager>(FindObjectsSortMode.None))
            foreach (var корень in new[] { м.leftController, м.rightController, м.leftHand, м.rightHand })
            {
                if (корень == null) continue;
                if (_поверх && _маскаОкон != 0) ЛучиВОкна(корень.transform);
                foreach (var r in корень.GetComponentsInChildren<Renderer>(true))
                {
                    if (_записи.ContainsKey(r)) continue;
                    var з = new Запись { Порядок = r.sortingOrder };
                    _записи[r] = з;
                    if (_поверх) Применить(r, з, true);
                }
            }
    }

    static void Применить(Renderer r, Запись з, bool поверх)
    {
        if (r == null) return;
        r.sortingOrder = поверх ? ПорядокНадОкном : з.Порядок;
        if (r is SkinnedMeshRenderer) return;
        if (поверх && з.Маты == null)
        {
            bool естьНепрозрачные = false;
            foreach (var m in r.sharedMaterials) if (m != null && m.renderQueue < 3000) естьНепрозрачные = true;
            if (!естьНепрозрачные) return;
            з.Маты = r.materials;   // свои копии: общие материалы-ассеты не меняем
            з.Очереди = new int[з.Маты.Length];
            for (int i = 0; i < з.Маты.Length; i++) з.Очереди[i] = з.Маты[i] != null ? з.Маты[i].renderQueue : 0;
        }
        if (з.Маты == null) return;
        for (int i = 0; i < з.Маты.Length; i++)
        {
            if (з.Маты[i] == null) continue;
            з.Маты[i].renderQueue = поверх && з.Очереди[i] < 3000 ? ОчередьНадОкном : з.Очереди[i];
        }
    }
}

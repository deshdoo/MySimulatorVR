using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Inputs;

// Контроллеры, руки и лучи — поверх окон, которые рисуются поверх кабины (меню, настройки
// в миссии, итоги: ZTest Always, sortingOrder 100). Такое окно не пишет глубину и идёт в
// прозрачной очереди после всего остального — поэтому закрывало и контроллеры перед ним,
// и луч, которым в него целятся. Пока такое окно открыто, детали контроллеров и рук
// рисуются после него: непрозрачные переходят в прозрачную очередь (Transparent+100),
// всем ставится порядок сортировки выше окна. Проверка глубины у них своя, так что кабина
// их по-прежнему закрывает, а окно — нет. Окно закрыли — всё возвращается как было.
//
// Материалы рук (SkinnedMeshRenderer) не трогаются: их раздаёт РукиПризраки, и они уже
// прозрачные — им хватает порядка сортировки.
public class КонтроллерыНадОкнами : MonoBehaviour
{
    const int ПорядокНадОкном = 200, ОчередьНадОкном = 3100;

    static readonly List<GameObject> _окна = new List<GameObject>();
    public static void Окно(GameObject корень) { if (корень != null && !_окна.Contains(корень)) _окна.Add(корень); }

    class Запись { public int Порядок; public Material[] Маты; public int[] Очереди; }
    readonly Dictionary<Renderer, Запись> _записи = new Dictionary<Renderer, Запись>();
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
        foreach (var g in _окна) if (g.activeInHierarchy) { нужно = true; break; }
        if (нужно != _поверх)
        {
            _поверх = нужно;
            if (нужно) Собрать();
            foreach (var пара in _записи) Применить(пара.Key, пара.Value, нужно);
            _следПоиск = Time.unscaledTime + 0.5f;
        }
        else if (нужно && Time.unscaledTime >= _следПоиск)
        {
            _следПоиск = Time.unscaledTime + 0.5f;   // модели контроллеров появляются, когда их увидела гарнитура
            Собрать();
        }
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

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// Заморозка ОДНОЙ сцены (подводной Main), а не всего мира.
//
// Time.timeScale = 0 не годится: на нём встаёт и XR-риг — эмулятор двигает
// контроллеры/руки через deltaTime, XRI сглаживает позы по времени, — оператор не
// может нажать кнопки меню. Поэтому кабина с ригом живёт в обычном времени, а в
// замораживаемой сцене выключаются скрипты проекта (аппарат, течение, симулятор
// систем, таймер миссии…), тела становятся кинематическими, частицы и аниматоры
// встают на паузу. Камеры и свет не трогаем — картинка просто застывает.
//
// Используют окно итогов (F10) и главное меню (пока открыто меню, миссия не идёт).
// Если скрипты сцены выключены ДО их Start (заморозка сразу при загрузке сцены),
// Unity вызовет Start при первом включении — миссия стартует с нуля в момент
// «Начать миссию».
public class SceneFreeze
{
    readonly List<MonoBehaviour> _выключенные = new List<MonoBehaviour>();
    readonly List<(Rigidbody rb, bool кин, Vector3 v, Vector3 w)> _тела = new List<(Rigidbody, bool, Vector3, Vector3)>();
    readonly List<ParticleSystem> _частицы = new List<ParticleSystem>();
    readonly List<(Animator a, float скорость)> _аниматоры = new List<(Animator, float)>();

    public bool Активна { get; private set; }

    // исключить — объекты, которые должны жить (сам компонент меню/окна и его панель).
    public void Заморозить(Scene сцена, params Object[] исключить)
    {
        if (Активна || !сцена.IsValid() || !сцена.isLoaded) return;
        var свои = typeof(SceneFreeze).Assembly;   // только скрипты проекта, не URP/UI/TMP/XRI
        foreach (var root in сцена.GetRootGameObjects())
        {
            foreach (var mb in root.GetComponentsInChildren<MonoBehaviour>(false))
            {
                if (mb == null || !mb.enabled || mb.GetType().Assembly != свои) continue;
                if (Исключён(mb, исключить)) continue;
                mb.enabled = false;
                _выключенные.Add(mb);
            }
            foreach (var rb in root.GetComponentsInChildren<Rigidbody>(false))
            {
                bool кин = rb.isKinematic;
                _тела.Add((rb, кин, кин ? Vector3.zero : rb.linearVelocity, кин ? Vector3.zero : rb.angularVelocity));
                rb.isKinematic = true;
            }
            foreach (var ps in root.GetComponentsInChildren<ParticleSystem>(false))
                if (ps.isPlaying) { ps.Pause(true); _частицы.Add(ps); }
            foreach (var a in root.GetComponentsInChildren<Animator>(false))
            {
                _аниматоры.Add((a, a.speed));
                a.speed = 0f;
            }
        }
        // Регистратор телеметрии живёт в кабине — останавливаем и его, иначе на
        // графиках за время паузы нарисуется ровная «полка».
        var рег = TelemetryRecorder.Instance;
        if (рег != null && рег.enabled) { рег.enabled = false; _выключенные.Add(рег); }
        Активна = true;
    }

    static bool Исключён(MonoBehaviour mb, Object[] исключить)
    {
        foreach (var o in исключить)
        {
            if (o == null) continue;
            if (o == mb) return true;
            if (o is GameObject go && mb.transform.IsChildOf(go.transform)) return true;
            if (o is Component c && mb.transform.IsChildOf(c.transform) && c is Transform) return true;
        }
        return false;
    }

    public void Разморозить()
    {
        if (!Активна) return;
        foreach (var mb in _выключенные) if (mb != null) mb.enabled = true;
        foreach (var (rb, кин, v, w) in _тела)
        {
            if (rb == null) continue;
            rb.isKinematic = кин;
            if (!кин) { rb.linearVelocity = v; rb.angularVelocity = w; }
        }
        foreach (var ps in _частицы) if (ps != null) ps.Play(true);
        foreach (var (a, скорость) in _аниматоры) if (a != null) a.speed = скорость;
        _выключенные.Clear(); _тела.Clear(); _частицы.Clear(); _аниматоры.Clear();
        Активна = false;
    }
}

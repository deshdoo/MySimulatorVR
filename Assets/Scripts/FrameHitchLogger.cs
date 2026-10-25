using System.Text;
using Unity.Profiling;
using UnityEngine;

// Счётчик подвисаний (только редактор и development-сборка). Создаётся сам при
// запуске. Раз в 10 с пишет в Console две строки:
//   [Кадры]  средний FPS, худший кадр, сколько было рывков (кадр дольше 1.5 средних
//            и > 25 мс) и сколько раз срабатывала сборка мусора — частая причина
//            коротких «подвисаний». Рывки, совпавшие со сборкой мусора, помечены «GC»;
//   [Нагрузка] кто держит кадр: время основного потока и видеокарты (если Unity её
//            меряет), вызовы отрисовки, треугольники и какие камеры сейчас рисуют —
//            каждая камера рисует сцену заново. Основной поток ≈ кадру — упор в
//            процессор (скрипты, физика, подготовка отрисовки), видеокарта ≈ кадру —
//            упор в графику (разрешение, туман, тени, число камер). «игра» — сам кадр
//            симулятора (скрипты, физика, отрисовка), «редактор» — окна Unity: открытое
//            окно Scene рисует сцену ещё раз, и FPS в редакторе падает втрое.
public class FrameHitchLogger : MonoBehaviour
{
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    const float Период_с = 10f, ПорогРывка_мс = 25f;

    float _начало, _сумма, _худший;
    int _кадров, _рывков, _рывковСGC, _gcНачало, _gcКадра;
    double _основной, _гпу, _батчи, _setPass, _треуг, _игра, _редактор, _физика, _скрипты;
    int _замеровГПУ;
    readonly StringBuilder _sb = new StringBuilder(512);
    readonly FrameTiming[] _тайминг = new FrameTiming[1];
    ProfilerRecorder _рОсновной, _рБатчи, _рSetPass, _рТреуг, _рИгра, _рРедактор, _рФизика, _рСкрипты;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Создать()
    {
        if (FindFirstObjectByType<FrameHitchLogger>() != null) return;
        var go = new GameObject("FrameHitchLogger");
        DontDestroyOnLoad(go);
        go.AddComponent<FrameHitchLogger>();
    }

    void OnEnable()
    {
        _рОсновной = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread");
        _рБатчи = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count");
        _рSetPass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
        _рТреуг = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
        _рИгра = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "PlayerLoop");
        _рРедактор = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "EditorLoop");
        _рФизика = ProfilerRecorder.StartNew(ProfilerCategory.Physics, "Physics.Simulate");
        _рСкрипты = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, "BehaviourUpdate");
        Сброс();
    }

    void OnDisable()
    {
        _рОсновной.Dispose(); _рБатчи.Dispose(); _рSetPass.Dispose(); _рТреуг.Dispose();
        _рИгра.Dispose(); _рРедактор.Dispose(); _рФизика.Dispose(); _рСкрипты.Dispose();
    }

    void Сброс()
    {
        _начало = Time.unscaledTime; _сумма = _худший = 0f;
        _кадров = _рывков = _рывковСGC = _замеровГПУ = 0;
        _основной = _гпу = _батчи = _setPass = _треуг = _игра = _редактор = _физика = _скрипты = 0;
        _gcНачало = _gcКадра = System.GC.CollectionCount(0);
    }

    void Update()
    {
        float мс = Time.unscaledDeltaTime * 1000f;
        int gc = System.GC.CollectionCount(0);
        bool былGC = gc != _gcКадра; _gcКадра = gc;

        _кадров++; _сумма += мс; _худший = Mathf.Max(_худший, мс);
        float среднее = _сумма / _кадров;
        if (_кадров > 10 && мс > ПорогРывка_мс && мс > среднее * 1.5f) { _рывков++; if (былGC) _рывковСGC++; }

        // Значения прошлого кадра (счётчики профайлера отстают на кадр — для средних неважно).
        if (_рОсновной.Valid) _основной += _рОсновной.LastValue * 1e-6;   // нс → мс
        if (_рБатчи.Valid) _батчи += _рБатчи.LastValue;
        if (_рSetPass.Valid) _setPass += _рSetPass.LastValue;
        if (_рТреуг.Valid) _треуг += _рТреуг.LastValue;
        if (_рИгра.Valid) _игра += _рИгра.LastValue * 1e-6;
        if (_рРедактор.Valid) _редактор += _рРедактор.LastValue * 1e-6;
        if (_рФизика.Valid) _физика += _рФизика.LastValue * 1e-6;
        if (_рСкрипты.Valid) _скрипты += _рСкрипты.LastValue * 1e-6;
        FrameTimingManager.CaptureFrameTimings();
        if (FrameTimingManager.GetLatestTimings(1, _тайминг) > 0 && _тайминг[0].gpuFrameTime > 0.0 && _тайминг[0].gpuFrameTime < 1000.0)   // бывают мусорные значения
        { _гпу += _тайминг[0].gpuFrameTime; _замеровГПУ++; }

        if (Time.unscaledTime - _начало < Период_с) return;
        _sb.Clear();
        _sb.Append("[Кадры] ").Append((1000f / среднее).ToString("F0")).Append(" FPS (").Append(среднее.ToString("F1"))
           .Append(" мс), худший кадр ").Append(_худший.ToString("F0")).Append(" мс, рывков ").Append(_рывков)
           .Append(" (из них GC ").Append(_рывковСGC).Append("), сборок мусора ").Append(gc - _gcНачало)
           .Append(MainMenu.Открыто ? " · в меню" : "");
        Debug.Log(_sb.ToString());

        _sb.Clear();
        _sb.Append("[Нагрузка] основной поток ").Append((_основной / _кадров).ToString("F1")).Append(" мс (игра ")
           .Append(Мс(_рИгра, _игра)).Append(", из них физика ").Append(Мс(_рФизика, _физика))
           .Append(" и скрипты Update ").Append(Мс(_рСкрипты, _скрипты)).Append("; редактор ").Append(Мс(_рРедактор, _редактор))
           .Append("), видеокарта ")
           .Append(_замеровГПУ > 0 ? (_гпу / _замеровГПУ).ToString("F1") + " мс" : "не меряется")
           .Append(" · батчей ").Append((_батчи / _кадров).ToString("F0"))
           .Append(", SetPass ").Append((_setPass / _кадров).ToString("F0"))
           .Append(", треугольников ").Append((_треуг / _кадров / 1000.0).ToString("F0")).Append(" тыс.")
           .Append(" · клубов мути ").Append(RovSiltStirring.ЖивыхКлубов)
           .Append(" · камер рисует: ");
        int камер = 0;
        foreach (var c in Camera.allCameras)   // только включённые
        {
            _sb.Append(камер++ == 0 ? "" : ", ").Append(c.name);
            if (c.targetTexture != null) _sb.Append(" (").Append(c.targetTexture.width).Append('×').Append(c.targetTexture.height).Append(')');
        }
        if (камер == 0) _sb.Append("—");
        Debug.Log(_sb.ToString());
        Сброс();
    }

    string Мс(ProfilerRecorder р, double сумма) => р.Valid ? (сумма / _кадров).ToString("F1") + " мс" : "—";
#endif
}

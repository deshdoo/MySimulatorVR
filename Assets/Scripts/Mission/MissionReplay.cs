using System.Collections.Generic;
using System.IO;
using UnityEngine;

// Запись траектории миссии для 3D-повтора в меню.
//
// Вместо видео пишем СОСТОЯНИЕ аппарата 10 раз в секунду: время, положение,
// поворот, свет, клешня. Повтор в меню воспроизводит миссию заново в той же
// подводной сцене (над той же трубой) с перемоткой — это легче видео в сотни раз
// (~33 байта на отсчёт → ~160 КБ за 8 минут) и не зависит от разрешения экрана.
//
// Пишет, только пока компонент включён: во время паузы (окно итогов/меню
// замораживают сцену) запись сама встаёт. Ставится MissionBootstrap'ом на НПА.
[DisallowMultipleComponent]
public class MissionReplayRecorder : MonoBehaviour
{
    public const float Шаг_с = 0.1f;   // 10 Гц — плавно и компактно
    public static MissionReplayRecorder Instance { get; private set; }

    readonly List<MissionReplay.Кадр> _кадры = new List<MissionReplay.Кадр>();
    float _следующий;

    void Awake() { Instance = this; }
    void OnDestroy() { if (Instance == this) Instance = null; }

    void Update()
    {
        var m = MissionManager.Instance;
        if (m == null || m.Завершена || !RovSystems.telemetryValid) return;
        float t = m.ВремяМиссии_с;
        if (t < _следующий) return;
        _следующий = t + Шаг_с;
        _кадры.Add(new MissionReplay.Кадр
        {
            t = t, поз = transform.position, пов = transform.rotation,
            свет = RovSystems.lightsOn, клешня = RovSystems.grabberClosed
        });
    }

    public byte[] ВБайты() => MissionReplay.Упаковать(_кадры);

    // Продолжение миссии: траектория до сохранения идёт в начало записи.
    public void Предзагрузить(List<MissionReplay.Кадр> прошлые)
    {
        if (прошлые == null || прошлые.Count == 0) return;
        _кадры.InsertRange(0, прошлые);
        _следующий = прошлые[прошлые.Count - 1].t + Шаг_с;
    }
}

public static class MissionReplay
{
    public struct Кадр { public float t; public Vector3 поз; public Quaternion пов; public bool свет, клешня; }

    const int Версия = 1;

    public static byte[] Упаковать(List<Кадр> кадры)
    {
        if (кадры == null || кадры.Count == 0) return null;
        using var ms = new MemoryStream(кадры.Count * 33 + 8);
        using var w = new BinaryWriter(ms);
        w.Write(Версия);
        w.Write(кадры.Count);
        foreach (var к in кадры)
        {
            w.Write(к.t);
            w.Write(к.поз.x); w.Write(к.поз.y); w.Write(к.поз.z);
            w.Write(к.пов.x); w.Write(к.пов.y); w.Write(к.пов.z); w.Write(к.пов.w);
            w.Write((byte)((к.свет ? 1 : 0) | (к.клешня ? 2 : 0)));
        }
        w.Flush();
        return ms.ToArray();
    }

    public static List<Кадр> Распаковать(byte[] данные)
    {
        var кадры = new List<Кадр>();
        if (данные == null || данные.Length < 8) return кадры;
        using var r = new BinaryReader(new MemoryStream(данные));
        if (r.ReadInt32() != Версия) return кадры;
        int n = r.ReadInt32();
        for (int i = 0; i < n; i++)
        {
            var к = new Кадр
            {
                t = r.ReadSingle(),
                поз = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()),
                пов = new Quaternion(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle())
            };
            byte ф = r.ReadByte();
            к.свет = (ф & 1) != 0; к.клешня = (ф & 2) != 0;
            кадры.Add(к);
        }
        return кадры;
    }

    // Состояние в момент t — интерполяция между соседними кадрами (бинарный поиск).
    public static Кадр В(List<Кадр> кадры, float t)
    {
        if (кадры.Count == 0) return default;
        if (t <= кадры[0].t) return кадры[0];
        if (t >= кадры[кадры.Count - 1].t) return кадры[кадры.Count - 1];
        int lo = 0, hi = кадры.Count - 1;
        while (hi - lo > 1) { int mid = (lo + hi) / 2; if (кадры[mid].t <= t) lo = mid; else hi = mid; }
        var a = кадры[lo]; var b = кадры[hi];
        float k = Mathf.InverseLerp(a.t, b.t, t);
        return new Кадр
        {
            t = t, поз = Vector3.Lerp(a.поз, b.поз, k), пов = Quaternion.Slerp(a.пов, b.пов, k),
            свет = k < 0.5f ? a.свет : b.свет, клешня = k < 0.5f ? a.клешня : b.клешня
        };
    }
}

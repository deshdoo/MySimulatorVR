using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// «Продолжить» сохранённую незаконченную миссию.
//
// Сцена Main при входе в меню заморожена ДО Start своих скриптов, поэтому
// восстановление идёт в два шага:
//   1) ДоСтарта (сцена ещё заморожена): убрать случайные дефекты и поставить
//      сохранённые (те же места/ориентация/тип), поставить аппарат туда, где он
//      был, вернуть заряд, повреждения и свет. Кабель-пуповина строится в своём
//      Start — уже от нового положения аппарата;
//   2) ПослеСтарта (миссия разморожена, Start'ы отработали): время и выполненные
//      этапы (MissionManager), найденные дефекты (сканер «находит» их заново,
//      сбор метрик получает сохранённые время/глубину/снимок), накопленные
//      показатели и траектория для 3D-повтора.
// Итог продолжения сохраняется ВМЕСТО исходной записи (MissionArchive заменить).
public static class MissionResume
{
    public static MissionArchive.Запись Запись { get; private set; }
    public static bool ИдётВосстановление { get; private set; }   // RovAudio молчит про «пинги» найденных
    public static string ИдЗаменить => Запись != null ? Запись.id : null;

    static readonly List<(PipelineDefect d, MissionArchive.Дефект з)> _дефекты = new List<(PipelineDefect, MissionArchive.Дефект)>();

    public static void Сбросить() { Запись = null; _дефекты.Clear(); }

    public static void ДоСтарта(MissionArchive.Запись з)
    {
        Запись = з;
        _дефекты.Clear();

        var спавнер = Object.FindFirstObjectByType<PipelineDefectSpawner>();
        if (спавнер != null && з.дефекты.Count > 0)
        {
            спавнер.УдалитьВсеДефекты();
            foreach (var д in з.дефекты)
            {
                var pd = спавнер.Разместить((PipelineDefect.ТипДефекта)д.тип, д.позиция, д.поворот);
                if (pd != null) _дефекты.Add((pd, д));
            }
        }

        var rov = Object.FindFirstObjectByType<RovController>();
        if (rov != null && з.позицияНПА != Vector3.zero)
        {
            rov.transform.SetPositionAndRotation(з.позицияНПА, з.поворотНПА);
            var rb = rov.GetComponent<Rigidbody>();
            if (rb != null) { rb.position = з.позицияНПА; rb.rotation = з.поворотНПА; }
        }

        // Ёмкость батареи могли уменьшить в настройках после сохранения — заряд не выше ёмкости.
        var сим = Object.FindFirstObjectByType<RovSystemsSimulator>();
        if (з.зарядАч > 0f) RovSystems.batteryCharge_Ah = сим != null ? Mathf.Min(з.зарядАч, сим.ЁмкостьАКБ) : з.зарядАч;
        if (з.повреждения != null && з.повреждения.Length >= 5)
        {
            RovSystems.thrusterDamage = з.повреждения[0]; RovSystems.cameraDamage = з.повреждения[1];
            RovSystems.manipulatorDamage = з.повреждения[2]; RovSystems.communicationDamage = з.повреждения[3];
            RovSystems.lightsDamage = з.повреждения[4];
        }
        RovSystems.lightsOn = з.свет;
        RovSystems.grabberClosed = з.клешня;
        RovSystems.missionTime_s = з.время_с;
        if (з.темпДвижителей_С >= 0f) RovSystems.thrusterTemp_C = з.темпДвижителей_С;
        if (з.зарядРезерва_Ач >= 0f && сим != null) сим.ЗарядРезерва_Ач = з.зарядРезерва_Ач;
        Debug.Log($"[Продолжение] Восстановлено до старта: дефектов {_дефекты.Count}, аппарат в {з.позицияНПА}.");
    }

    public static IEnumerator ПослеСтарта()
    {
        var з = Запись;
        if (з == null) yield break;
        // Дать отработать Start'ам размороженных скриптов (менеджер, сканер, метрики).
        yield return null; yield return null;
        ИдётВосстановление = true;

        var m = MissionManager.Instance;
        if (m != null) m.Восстановить(з.время_с, з.ВременаЭтапов());

        var найденные = new Dictionary<PipelineDefect, MissionArchive.Дефект>();
        var сканер = RovScanner.Instance;
        foreach (var (d, д) in _дефекты)
        {
            if (d == null || !д.найден) continue;
            найденные[d] = д;
            if (сканер != null) сканер.ВосстановитьНайденный(d);
        }

        var s = MissionStats.Instance;
        if (s != null) s.Восстановить(з, найденные);

        // Вентиль: если его этап уже закрыт — штурвал закрыт, утечек нет.
        var этапы = з.ВременаЭтапов();
        foreach (var it in Object.FindObjectsByType<InterventionTarget>(FindObjectsSortMode.None))
            if (it.ИндексЦели >= 0 && it.ИндексЦели < этапы.Length && этапы[it.ИндексЦели] >= 0f)
                it.ВосстановитьВыполненным();

        // Автопилот — на той глубине, что держал при сохранении.
        if (з.автопилот && RovAutopilot.Instance != null) RovAutopilot.Instance.ВосстановитьУдержание(з.глубинаАвтопилота_м);

        var рек = MissionReplayRecorder.Instance;
        if (рек != null) рек.Предзагрузить(MissionReplay.Распаковать(MissionArchive.ЗагрузитьПовтор(з.id)));

        yield return null;
        ИдётВосстановление = false;
        Debug.Log($"[Продолжение] Миссия продолжена с {MissionStats.ФорматВремени(з.время_с)}: найдено {найденные.Count} из {_дефекты.Count}.");
    }
}

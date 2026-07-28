using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

// Менеджер миссии: последовательный чеклист целей + таймер.
//
// Простая и защитимая модель — конечный автомат целей:
//   - цели заданы списком в инспекторе (заголовок + необязательная подсказка);
//   - в каждый момент активна одна цель (последовательное прохождение);
//   - при завершении активной цели активируется следующая ожидающая;
//   - когда активных не осталось — миссия завершена.
//
// «Что считается выполненным» намеренно вынесено НАРУЖУ: сам менеджер только
// ведёт состояние и время, а факт выполнения ему сообщают извне — зоной
// (MissionZoneTrigger), логикой сценария или тест-клавишей. Так менеджер не
// зависит от конкретной геометрии сцены и переиспользуется без правок.
//
// Отображение — отдельный MissionHudDisplay (подписывается на событие Изменилось).
//
// Настройка в сцене: пустой GameObject + этот компонент, заполнить список Цели.
[DisallowMultipleComponent]
public class MissionManager : MonoBehaviour
{
    public static MissionManager Instance { get; private set; }

    [Tooltip("Название миссии — верхняя строка HUD")]
    public string НазваниеМиссии = "Миссия";

    [Tooltip("Список целей по порядку прохождения")]
    public List<MissionObjective> Цели = new List<MissionObjective>();

    [Header("Тест без триггеров (в редакторе)")]
    [Tooltip("Клавиша: завершить текущую активную цель — для проверки без зон")]
    public Key клавишаЗавершитьЦель = Key.M;

    // HUD подписывается сюда, чтобы перерисоваться при смене состояния целей.
    public event Action Изменилось;
    // Срабатывает один раз, когда закрыта последняя цель.
    public event Action МиссияЗавершена;

    public bool Завершена { get; private set; }
    public float ВремяМиссии_с { get; private set; }
    private float _старт;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Start()
    {
        _старт = Time.time;
        АктивироватьСледующую();   // первая цель становится активной
        Изменилось?.Invoke();
    }

    void Update()
    {
        if (!Завершена) ВремяМиссии_с = Time.time - _старт;

        if (клавишаЗавершитьЦель != Key.None && Keyboard.current != null &&
            Keyboard.current[клавишаЗавершитьЦель].wasPressedThisFrame)
            ЗавершитьТекущую();
    }

    // Завершить текущую активную цель (основной путь — последовательный чеклист).
    public void ЗавершитьТекущую()
    {
        int i = ИндексАктивной();
        if (i >= 0) ЗавершитьЦель(i);
    }

    // Завершить конкретную цель по индексу (на случай непоследовательных целей).
    public void ЗавершитьЦель(int индекс)
    {
        if (Завершена || индекс < 0 || индекс >= Цели.Count) return;
        if (Цели[индекс].Состояние == MissionObjectiveState.Completed) return;

        Цели[индекс].Состояние = MissionObjectiveState.Completed;
        АктивироватьСледующую();
        Изменилось?.Invoke();

        if (ИндексАктивной() < 0 && ВсеЗакрыты())
        {
            Завершена = true;
            МиссияЗавершена?.Invoke();
        }
    }

    // Пометить цель проваленной (для будущих сценариев с провалом задачи).
    public void ПровалитьЦель(int индекс)
    {
        if (индекс < 0 || индекс >= Цели.Count) return;
        Цели[индекс].Состояние = MissionObjectiveState.Failed;
        Изменилось?.Invoke();
    }

    // Активировать первую ожидающую цель, если сейчас нет активной.
    void АктивироватьСледующую()
    {
        if (ИндексАктивной() >= 0) return;
        for (int i = 0; i < Цели.Count; i++)
            if (Цели[i].Состояние == MissionObjectiveState.Pending)
            {
                Цели[i].Состояние = MissionObjectiveState.Active;
                return;
            }
    }

    int ИндексАктивной()
    {
        for (int i = 0; i < Цели.Count; i++)
            if (Цели[i].Состояние == MissionObjectiveState.Active) return i;
        return -1;
    }

    bool ВсеЗакрыты()
    {
        for (int i = 0; i < Цели.Count; i++)
            if (Цели[i].Состояние == MissionObjectiveState.Pending ||
                Цели[i].Состояние == MissionObjectiveState.Active) return false;
        return Цели.Count > 0;
    }
}

public enum MissionObjectiveState { Pending, Active, Completed, Failed }

[Serializable]
public class MissionObjective
{
    [Tooltip("Короткая формулировка цели — строка чеклиста")]
    public string Заголовок = "Новая цель";
    [Tooltip("Подсказка/детали (показывается только у активной цели)"), TextArea]
    public string Подсказка = "";
    // Состояние ведёт менеджер; в инспекторе трогать не нужно.
    [HideInInspector] public MissionObjectiveState Состояние = MissionObjectiveState.Pending;
}

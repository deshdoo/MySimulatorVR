using UnityEngine;

// Зона выполнения цели. Вешается на объект с триггер-коллайдером: когда в зону
// входит НПА, соответствующая цель миссии закрывается. Самый частый тип цели
// «дойти до точки» — без единой строки кода на сцену.
//
// Настройка: объект с Collider (галка Is Trigger включится сама через Reset) в
// нужной точке маршрута + этот компонент. Указать индекс цели (или −1 = закрыть
// текущую активную) и тег корпуса НПА.
[RequireComponent(typeof(Collider))]
[DisallowMultipleComponent]
public class MissionZoneTrigger : MonoBehaviour
{
    [Tooltip("Индекс цели в списке MissionManager (с нуля). −1 = завершить текущую активную.")]
    public int ИндексЦели = -1;

    [Tooltip("Тег объекта НПА (обычно у корпуса). Пусто — реагировать на любой коллайдер.")]
    public string ТегНПА = "";

    [Tooltip("Сработать один раз и выключить зону")]
    public bool Одноразовый = true;

    // Автоматически делаем коллайдер триггером при добавлении компонента,
    // чтобы не забыть галку Is Trigger в инспекторе.
    void Reset()
    {
        var c = GetComponent<Collider>();
        if (c != null) c.isTrigger = true;
    }

    void OnTriggerEnter(Collider other)
    {
        if (!string.IsNullOrEmpty(ТегНПА) && !other.CompareTag(ТегНПА)) return;

        var m = MissionManager.Instance;
        if (m == null) return;

        if (ИндексЦели < 0) m.ЗавершитьТекущую();
        else                m.ЗавершитьЦель(ИндексЦели);

        if (Одноразовый) gameObject.SetActive(false);
    }
}

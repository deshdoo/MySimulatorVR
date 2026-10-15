using UnityEngine;

// Манипулятор «Марлина-350»: рабочая рука — rov_gripper (длинная рука с клешнёй, как
// на фото аппарата; её логика — Grabber анимирует челюсти, Pickup берёт груз). Если рука
// на аппарате есть и включена, захват из модели (Узел_схват) прячется, чтобы
// не было двух клешней. Если руки нет — работает «схват» модели (рука, кисть, две челюсти
// на шарнире, по фото «Марлина»): его челюсти
// сжимаются по тому же флагу RovSystems.grabberClosed (клавиша/кнопка пульта).
// Ставится на корпус НПА (RovController.Start добавляет сам).
public class RovMarlinGripper : MonoBehaviour
{
    [Tooltip("Угол сжатия каждой челюсти компактного захвата, град.")]
    public float УголСжатия = 11f;
    [Tooltip("Скорость сжатия/разжатия (доля полного хода в секунду).")]
    public float Скорость = 6f;

    Transform[] _челюсти;
    Quaternion[] _исходные;
    Vector3[] _оси;
    float[] _знак = { 1f, -1f };
    float _t;

    void Start()
    {
        bool естьРука = false;
        foreach (var g in GetComponentsInChildren<RovSim.Rov.Grabber>(false)) { естьРука = true; break; }

        Transform узел = null, л = null, п = null;
        foreach (var t in GetComponentsInChildren<Transform>(true))
        {
            if (t.name == "Узел_схват" || (узел == null && t.name == "Узел_захват")) узел = t;
            else if (t.name == "Схват_ЧелюстьЛ" || (л == null && t.name == "ЗахватЧелюстьЛ")) л = t;
            else if (t.name == "Схват_ЧелюстьП" || (п == null && t.name == "ЗахватЧелюстьП")) п = t;
        }

        if (естьРука)
        {
            // Рабочая рука на месте — компактный захват модели не нужен.
            if (узел != null) узел.gameObject.SetActive(false);
            enabled = false;
            return;
        }
        if (л == null || п == null) { enabled = false; return; }
        _челюсти = new[] { л, п };
        _исходные = new[] { л.localRotation, п.localRotation };
        _оси = new[] { л.parent.InverseTransformDirection(transform.up), п.parent.InverseTransformDirection(transform.up) };
        _t = RovSystems.grabberClosed ? 1f : 0f;
    }

    void Update()
    {
        _t = Mathf.MoveTowards(_t, RovSystems.grabberClosed ? 1f : 0f, Скорость * Time.deltaTime);
        for (int i = 0; i < _челюсти.Length; i++)
            _челюсти[i].localRotation = Quaternion.AngleAxis(_знак[i] * УголСжатия * _t, _оси[i]) * _исходные[i];
    }
}

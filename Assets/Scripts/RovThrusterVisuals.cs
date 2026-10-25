using UnityEngine;

// Вращение винтов движителей модели «Марлин-350» по командам управления.
//
// Схема движителей как у прототипа: четыре горизонтальных по углам под 45°
// (векторная схема — ход, лаг и разворот одними и теми же винтами) и два
// вертикальных. Команда каждого винта получается распределением общих команд
// (DroneInput: вперёд, лаг, вертикаль, рысканье) по направлению его оси и плечу
// относительно центра — так же, как это делает блок управления реального ROV:
//   uᵢ = dᵢ·(лаг, вперёд) + рысканье·sign((pᵢ × dᵢ)ᵧ),   затем нормировка в −1…1.
// Обороты набираются с инерцией (постоянная времени винта ~0.15 с), поэтому
// при резкой команде винт раскручивается, а не дёргается мгновенно.
//
// Винты находятся по имени «…Винт» внутри модели; ось вращения — направление
// от мотора к винту (объекты «…Мотор»), вычисляется один раз при старте.
// Ставится на корпус НПА (RovController.Start добавляет сам, если есть модель).
public class RovThrusterVisuals : MonoBehaviour
{
    [Tooltip("Обороты винта при полной команде, об/мин (только для вида).")]
    public float ОборотыМакс = 900f;
    [Tooltip("Постоянная времени раскрутки винта, с.")]
    public float ПостояннаяВремени = 0.15f;

    class Винт
    {
        public Transform t;
        public Quaternion исходный;
        public Vector3 осьЛок;      // ось вращения в осях родителя винта
        public Vector3 тягаКорпус;  // направление оси в осях корпуса (горизонтальная проекция)
        public float плечоЗнак;     // знак момента рысканья от этого винта
        public bool вертикальный;
        public float шаг;           // +1 — винт правого шага, −1 — левого (вращаются навстречу)
        public float угол, обороты; // град, доля от максимума (−1…1)
    }
    Винт[] _винты = new Винт[0];

    // Команды винтов (−1…1) — для звука, энергетики и отладки.
    public float[] Команды { get; private set; } = new float[0];

    void Start()
    {
        var список = new System.Collections.Generic.List<Винт>();
        foreach (var t in GetComponentsInChildren<Transform>(true))
        {
            if (!t.name.EndsWith("Винт")) continue;
            string база = t.name.Substring(0, t.name.Length - "Винт".Length);
            Transform мотор = НайтиВоВнуках(t.root, база + "Мотор");
            if (мотор == null || t.parent == null) continue;

            Vector3 осьМир = (t.position - мотор.position).normalized;
            Vector3 осьКорп = transform.InverseTransformDirection(осьМир);
            bool верт = Mathf.Abs(осьКорп.y) > 0.7f;
            Vector3 гор = new Vector3(осьКорп.x, 0f, осьКорп.z).normalized;
            Vector3 p = transform.InverseTransformPoint(t.position);
            float момент = p.z * гор.x - p.x * гор.z;   // (p × d)·y

            список.Add(new Винт
            {
                t = t, исходный = t.localRotation,
                осьЛок = t.parent.InverseTransformDirection(осьМир).normalized,
                тягаКорпус = гор, плечоЗнак = Mathf.Sign(момент), вертикальный = верт,
                // Парные винты разного шага: левый/правый борт у горизонтальных, нос/корма у
                // вертикальных. При одинаковой тяге пара крутится навстречу — реактивные моменты
                // моторов гасят друг друга, и корпус не закручивает.
                шаг = верт ? (p.z >= 0f ? 1f : -1f) : (p.x < 0f ? 1f : -1f),
            });
        }
        _винты = список.ToArray();
        Команды = new float[_винты.Length];
        Готов = true;
    }

    // Винты найдены и разобраны (Start прошёл).
    public bool Готов { get; private set; }
    public bool ЕстьГоризонтальные { get { foreach (var в in _винты) if (!в.вертикальный) return true; return false; } }

    // Какую долю полной тяги дают горизонтальные винты при команде (вперёд, лаг, рысканье),
    // если винт на заднем ходу тянет «реверс» от переднего. Распределение — то же, что для
    // вращения (Update): каждый винт получает свою команду по направлению оси и плечу.
    // Доля = Σ|uᵢ|·kᵢ / Σ|uᵢ|, kᵢ = 1 на переднем ходу и реверс на заднем.
    public float ДоляГоризонтальных(float вперёд, float лаг, float рыск, float реверс)
    {
        float норм = 1f, сумма = 0f, вес = 0f;
        foreach (var в in _винты)
            if (!в.вертикальный) норм = Mathf.Max(норм, Mathf.Abs(КомандаГоризонтального(в, вперёд, лаг, рыск)));
        foreach (var в in _винты)
        {
            if (в.вертикальный) continue;
            float u = КомандаГоризонтального(в, вперёд, лаг, рыск) / норм;
            if (Mathf.Abs(u) < 1e-4f) continue;
            вес += Mathf.Abs(u);
            сумма += Mathf.Abs(u) * (u >= 0f ? 1f : реверс);
        }
        return вес > 0f ? сумма / вес : 1f;
    }

    static float КомандаГоризонтального(Винт в, float вперёд, float лаг, float рыск) =>
        Vector3.Dot(в.тягаКорпус, new Vector3(лаг, 0f, вперёд)) * 1.41421f + рыск * в.плечоЗнак;

    static Transform НайтиВоВнуках(Transform корень, string имя)
    {
        foreach (var t in корень.GetComponentsInChildren<Transform>(true))
            if (t.name == имя) return t;
        return null;
    }

    void Update()
    {
        float dt = Time.deltaTime;
        float k = 1f - Mathf.Exp(-dt / Mathf.Max(0.01f, ПостояннаяВремени));
        float норм = 1f;
        // Распределение команд по горизонтальным винтам; если сумма выходит за ±1 —
        // все горизонтальные масштабируются одинаково (так не искажается направление тяги).
        for (int i = 0; i < _винты.Length; i++)
        {
            var в = _винты[i];
            float u = в.вертикальный
                ? DroneInput.vertical
                : Vector3.Dot(в.тягаКорпус, new Vector3(DroneInput.lateral, 0f, DroneInput.forward)) * 1.41421f
                  + DroneInput.yaw * в.плечоЗнак;
            Команды[i] = u;
            if (!в.вертикальный) норм = Mathf.Max(норм, Mathf.Abs(u));
        }
        for (int i = 0; i < _винты.Length; i++)
        {
            var в = _винты[i];
            float цель = в.вертикальный ? Mathf.Clamp(Команды[i], -1f, 1f) : Команды[i] / норм;
            Команды[i] = цель;
            в.обороты += (цель - в.обороты) * k;
            в.угол = Mathf.Repeat(в.угол + в.шаг * в.обороты * ОборотыМакс * 6f * dt, 360f);   // об/мин → град/с
            в.t.localRotation = Quaternion.AngleAxis(в.угол, в.осьЛок) * в.исходный;
        }
    }
}

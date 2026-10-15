using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;

// Переназначение клавиш клавиатуры (эмулятор XR и ПК VR с клавиатурой).
//
// Клавиши в проекте заданы по-разному: часть — полями в коде (F10, F3…), часть —
// сериализована в сцене у каждого рычага/кнопки (у каждого рычага свои I/K/J/L…).
// Чтобы не править сцену, переназначение сделано СЛОЕМ ПОДМЕНЫ: скрипт спрашивает
// не «нажата ли I», а «нажата ли клавиша, которая сейчас стоит вместо I» —
// Keyboard.current[KeyBindings.K(Key.I)]. Таблица «исходная → назначенная»
// хранится в PlayerPrefs. Так все экземпляры рычагов и кнопок подхватывают
// переназначение сразу.
//
// Реестр Действия — что показывать в меню «Управление» (исходная клавиша +
// понятное описание + группа). Конфликт при назначении решается обменом: если
// новая клавиша уже занята другим действием, тому достаётся старая клавиша.
public static class KeyBindings
{
    public struct Действие
    {
        public Key Исходная;
        public string Название;
        public string Группа;
        public Действие(Key k, string группа, string название) { Исходная = k; Группа = группа; Название = название; }
    }

    public static readonly Действие[] Действия =
    {
        new Действие(Key.I,         "Пульт (эмулятор)", "Рычаги: вперёд / вверх"),
        new Действие(Key.K,         "Пульт (эмулятор)", "Рычаги: назад / вниз"),
        new Действие(Key.J,         "Пульт (эмулятор)", "Рычаги: влево"),
        new Действие(Key.L,         "Пульт (эмулятор)", "Рычаги: вправо"),
        new Действие(Key.U,         "Пульт (эмулятор)", "Рычаг поворота: вправо"),
        new Действие(Key.M,         "Пульт (эмулятор)", "Кнопка пульта (тест)"),
        new Действие(Key.F10,       "Окна",             "Окно итогов миссии"),
        new Действие(Key.PageDown,  "Окна",             "Итоги: следующая страница"),
        new Действие(Key.PageUp,    "Окна",             "Итоги: предыдущая страница"),
        new Действие(Key.G,         "Окна",             "Графики: следующая страница"),
        new Действие(Key.F2,        "Приборы",          "Анимация гидролокатора"),
        new Действие(Key.F3,        "Приборы",          "Анимация доплеровского лага"),
        new Действие(Key.F6,        "Приборы",          "Анимация: вид сбоку"),
        new Действие(Key.F7,        "Приборы",          "Анимация: пауза"),
        new Действие(Key.F8,        "Приборы",          "Анимация: скорость"),
        new Действие(Key.F9,        "Приборы",          "Линии тока течения"),
        new Действие(Key.B,         "Приборы",          "Гидролокатор: экран вкл/выкл"),
        new Действие(Key.H,         "Приборы",          "Гидролокатор: шкала дальности"),
        new Действие(Key.V,         "Приборы",          "Видео камер захвата и коннектора: вкл/выкл"),
        new Действие(Key.O,         "Физика",           "Течение вкл/выкл (и рычаг поворота)"),
        new Действие(Key.Digit0,    "Физика",           "Удержание от сноса"),
        new Действие(Key.Backspace, "Отладка",          "Пропустить текущую цель"),
        new Действие(Key.P,         "Отладка",          "Пропустить этап"),
        new Действие(Key.F11,       "Отладка",          "Тест: повреждение камеры"),
    };

    const string КлючPrefs = "binds_v1";
    static Dictionary<Key, Key> _замены;

    static Dictionary<Key, Key> Замены
    {
        get { if (_замены == null) Загрузить(); return _замены; }
    }

    // Главный вызов для скриптов: какая клавиша сейчас стоит вместо исходной.
    public static Key K(Key исходная)
    {
        if (исходная == Key.None) return Key.None;
        return Замены.TryGetValue(исходная, out var k) ? k : исходная;
    }

    public static bool Переназначена(Key исходная) => Замены.ContainsKey(исходная);

    // Назначить исходной клавише новую. Если новая уже занята другим действием —
    // обмен: тому действию достаётся прежняя клавиша этого (без «дыр» и дублей).
    public static void Назначить(Key исходная, Key новая)
    {
        if (исходная == Key.None || новая == Key.None) return;
        Key прежняя = K(исходная);
        if (прежняя == новая) return;
        foreach (var д in Действия)
            if (д.Исходная != исходная && K(д.Исходная) == новая)
                Установить(д.Исходная, прежняя);
        Установить(исходная, новая);
        Сохранить();
    }

    static void Установить(Key исходная, Key новая)
    {
        if (новая == исходная) Замены.Remove(исходная);
        else Замены[исходная] = новая;
    }

    public static void СброситьВсё()
    {
        Замены.Clear();
        Сохранить();
    }

    static void Загрузить()
    {
        _замены = new Dictionary<Key, Key>();
        string s = PlayerPrefs.GetString(КлючPrefs, "");
        foreach (var пара in s.Split(';'))
        {
            var ч = пара.Split('=');
            if (ч.Length == 2 && int.TryParse(ч[0], out int a) && int.TryParse(ч[1], out int b))
                _замены[(Key)a] = (Key)b;
        }
    }

    static void Сохранить()
    {
        var sb = new StringBuilder();
        foreach (var kv in Замены) sb.Append((int)kv.Key).Append('=').Append((int)kv.Value).Append(';');
        PlayerPrefs.SetString(КлючPrefs, sb.ToString());
        PlayerPrefs.Save();
    }

    // Человекочитаемое имя клавиши для меню.
    public static string Имя(Key k)
    {
        switch (k)
        {
            case Key.None: return "—";
            case Key.Space: return "Пробел";
            case Key.Enter: return "Enter";
            case Key.Backspace: return "Backspace";
            case Key.PageUp: return "PgUp";
            case Key.PageDown: return "PgDn";
            case Key.LeftArrow: return "Стрелка влево";
            case Key.RightArrow: return "Стрелка вправо";
            case Key.UpArrow: return "Стрелка вверх";
            case Key.DownArrow: return "Стрелка вниз";
        }
        string n = k.ToString();
        if (n.StartsWith("Digit")) return n.Substring(5);
        if (n.StartsWith("Numpad")) return "Num " + n.Substring(6);
        return n;
    }

    // Клавиша, нажатая в этом кадре (для режима «нажмите клавишу»). Escape — отмена.
    public static Key НажатаЛюбая()
    {
        var kb = Keyboard.current;
        if (kb == null) return Key.None;
        foreach (var kc in kb.allKeys)
            if (kc != null && kc.wasPressedThisFrame) return kc.keyCode;
        return Key.None;
    }
}

using UnityEngine;
using TMPro;

// HUD миссии: рисует весь чеклист целей и таймер в ОДНО TMP-поле рич-текстом.
//
// Одно текстовое поле на весь список — сознательный выбор ради минимума настройки
// в сцене: не нужно создавать префаб-строку и инстанцировать по объекту на цель.
// Значок и цвет каждой строки задаются состоянием цели:
//   ✔ выполнена (зелёный), ► активная (жёлтый), ○ ожидает (серый), ✘ провал (красный).
//
// Настройка: TMP-поле на канвасе (лучше моноширинный или обычный шрифт с рич-текстом),
// перетащить его в «Поле», перетащить объект с MissionManager в «Менеджер» (или
// оставить пустым — возьмёт MissionManager.Instance).
[DisallowMultipleComponent]
public class MissionHudDisplay : MonoBehaviour
{
    [Tooltip("Текстовое поле для всего чеклиста (одно на список целей)")]
    public TMP_Text Поле;

    [Tooltip("Менеджер миссии. Пусто — возьмётся MissionManager.Instance")]
    public MissionManager Менеджер;

    [Header("Цвета строк")]
    public Color ЦветАктивной   = new Color(1f, 0.9f, 0.3f);
    public Color ЦветВыполненной = new Color(0.4f, 0.85f, 0.45f);
    public Color ЦветОжидания   = new Color(0.6f, 0.6f, 0.6f);
    public Color ЦветПровала    = new Color(0.9f, 0.35f, 0.35f);

    private readonly System.Text.StringBuilder _sb = new System.Text.StringBuilder();

    void Update()
    {
        if (Поле == null) return;

        var m = Менеджер != null ? Менеджер : MissionManager.Instance;
        if (m == null) { Поле.text = ""; return; }

        Поле.text = Построить(m);
    }

    string Построить(MissionManager m)
    {
        _sb.Clear();

        int сек = Mathf.Max(0, Mathf.FloorToInt(m.ВремяМиссии_с));
        _sb.Append("<b>").Append(m.НазваниеМиссии).Append("</b>   ")
           .AppendFormat("{0:00}:{1:00}", сек / 60, сек % 60).Append('\n');

        if (m.Завершена)
            _sb.Append("<color=#").Append(Hex(ЦветВыполненной)).Append(">ВЫПОЛНЕНО</color>\n");

        _sb.Append('\n');

        foreach (var ц in m.Цели)
        {
            string значок;
            Color цвет;
            switch (ц.Состояние)
            {
                case MissionObjectiveState.Completed: значок = "✔"; цвет = ЦветВыполненной; break; // ✔
                case MissionObjectiveState.Active:    значок = "►"; цвет = ЦветАктивной;    break; // ►
                case MissionObjectiveState.Failed:    значок = "✘"; цвет = ЦветПровала;     break; // ✘
                default:                              значок = "○"; цвет = ЦветОжидания;    break; // ○
            }

            string hex = Hex(цвет);
            _sb.Append("<color=#").Append(hex).Append('>')
               .Append(значок).Append(' ').Append(ц.Заголовок).Append("</color>\n");

            // Подсказку показываем только у активной цели, чтобы не загромождать список.
            if (ц.Состояние == MissionObjectiveState.Active && !string.IsNullOrEmpty(ц.Подсказка))
                _sb.Append("<color=#").Append(hex).Append("><size=80%>   ")
                   .Append(ц.Подсказка).Append("</size></color>\n");
        }

        return _sb.ToString();
    }

    static string Hex(Color c) => ColorUtility.ToHtmlStringRGB(c);
}

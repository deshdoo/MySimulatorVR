using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

// Архив пройденных миссий («сохранения результатов»).
//
// Каждая миссия — своя папка persistentDataPath/Миссии/<ггггММдд_ЧЧммсс>/:
//   итоги.json   — сводка и все показатели (читает меню «Результаты»);
//   отчёт.txt    — тот же разбор текстом (для приложения к ВКР);
//   снимок_N.jpg — фотофиксация дефектов, ужатая до 480 px по ширине (JPEG ~30 КБ);
//   повтор.bin   — траектория для 3D-повтора (~150–300 КБ на 10 минут).
// Итого обычно < 0.5 МБ на миссию. Удаление — вся папка целиком, без хвостов.
//
// Память: снимки для просмотра грузятся по требованию (ЗагрузитьСнимок) — кто
// загрузил, тот и уничтожает текстуру (Destroy), иначе при листании архива
// текстуры копились бы в памяти.
public static class MissionArchive
{
    [Serializable] public class Критерий { public string название; public float балл; public float макс; public string пояснение; }
    [Serializable] public class Дефект
    {
        public string метка; public bool найден; public float время_с; public float глубина_м;
        public float kp_м; public Vector3 позиция; public string снимок; public string времяСнимка;
        public Quaternion поворот; public int тип;   // для продолжения: поставить тот же дефект туда же
    }
    [Serializable] public class Удар { public float время_с; public float энергия_Дж; public string объект; }
    [Serializable] public class Этап { public string название; public string состояние; public float длительность_с; }

    [Serializable]
    public class Запись
    {
        public int версия = 1;
        public string id;             // имя папки
        public string дата;           // «01.10.2026 14:05»
        public string миссия;
        public bool досрочно;
        public float балл, максБалл;
        public string отметка;
        public float время_с;
        public int найдено, всегоДефектов;
        public float покрытие, скоростьОсмотра_уз, дистанция_м, путь_м, максГлубина_м, минВысота_м,
                     скоНаклона_град, максКабель_м, расходАКБ_проц, времяВМути_с;
        public int ударов, повреждений;
        public List<Этап> этапы = new List<Этап>();
        public List<Критерий> критерии = new List<Критерий>();
        public List<string> замечания = new List<string>();
        public List<Дефект> дефекты = new List<Дефект>();
        public List<Удар> удары = new List<Удар>();
        public bool естьПовтор;

        // ---- Состояние для «Продолжить» (миссия не закончена) ----
        public Vector3 позицияНПА; public Quaternion поворотНПА;
        public float зарядАч; public bool свет;
        public int[] повреждения = new int[5];   // движители, камера, манипулятор, связь, свет
        public float времяЗавершенияЭтапа0 = -1f, времяЗавершенияЭтапа1 = -1f, времяЗавершенияЭтапа2 = -1f, времяЗавершенияЭтапа3 = -1f;
        public float максТемп_С, максНаклон_град, времяУчёта_с, времяСоСветом_с, времяНаАвтопилоте_с, времяУТрубы_с;
        public float сумКвНаклона, сумСкорости, сумДистанции, зарядНаСтарте_проц;
        public int повреждённостьНаСтарте;
        public string покрытиеСтрока;
        public List<float> профильГлубины = new List<float>();
        // Физика, с которой шла миссия (PhysicsSettings, порядок Все[]). Пусто — запись
        // старше настроек физики, продолжается со штатными значениями.
        public float[] физика;
        // Состояние бортовых систем на момент сохранения (−1 — запись старее, не восстанавливать).
        public float темпДвижителей_С = -1f, зарядРезерва_Ач = -1f;
        public bool клешня, автопилот;
        public float глубинаАвтопилота_м;

        // Можно продолжить: есть невыполненные этапы (в т.ч. «проваленные» досрочным выходом).
        // Только записи нового формата (версия ≥ 2): в старых нет положения аппарата
        // и ориентации дефектов — восстановить честно нельзя.
        public bool МожноПродолжить
        {
            get
            {
                if (версия < 2 || этапы == null || этапы.Count == 0) return false;
                foreach (var э in этапы) if (э.состояние != "Completed") return true;
                return false;
            }
        }

        public float[] ВременаЭтапов() => new[] { времяЗавершенияЭтапа0, времяЗавершенияЭтапа1, времяЗавершенияЭтапа2, времяЗавершенияЭтапа3 };
    }

    public static string Папка => Path.Combine(Application.persistentDataPath, "Миссии");

    // Сохранить завершённую миссию. Возвращает путь папки (или null при ошибке).
    // заменить — id записи, которую продолжали: после успешного сохранения она
    // удаляется (результат продолжения заменяет её, а не плодит дубль).
    public static string Сохранить(MissionManager m, MissionStats s, byte[] повтор, string текстОтчёта, string заменить = null)
    {
        if (s == null) return null;
        try
        {
            var сейчас = DateTime.Now;
            string id = сейчас.ToString("yyyyMMdd_HHmmss");
            string папка = Path.Combine(Папка, id);
            Directory.CreateDirectory(папка);

            float процент = s.ИтогПроцент(out float сумма, out float макс);
            var з = new Запись
            {
                версия = 2,
                id = id,
                дата = сейчас.ToString("dd.MM.yyyy HH:mm"),
                миссия = m != null ? m.НазваниеМиссии : "Миссия",
                досрочно = m != null && m.Досрочно,
                балл = сумма, максБалл = макс, отметка = MissionStats.Отметка(процент),
                время_с = m != null ? m.ВремяМиссии_с : s.ВремяУчёта_с,
                найдено = s.ЧислоНайденных(), всегоДефектов = s.Дефекты.Count,
                покрытие = s.ЕстьТруба ? s.ДоляПокрытия : -1f,
                скоростьОсмотра_уз = s.ВремяУТрубы_с > 0f ? s.СкоростьОсмотра_уз : -1f,
                дистанция_м = s.ВремяУТрубы_с > 0f ? s.СредняяДистанция_м : -1f,
                путь_м = s.Путь_м, максГлубина_м = s.МаксГлубина_м,
                минВысота_м = float.IsInfinity(s.МинВысотаНадДном_м) ? -1f : s.МинВысотаНадДном_м,
                скоНаклона_град = s.СКОНаклона_град, максКабель_м = s.МаксКабель_м,
                расходАКБ_проц = s.РасходЗаряда_проц, времяВМути_с = s.ВремяВМути_с,
                ударов = s.Удары.Count, повреждений = s.ПолученоПовреждений,
                естьПовтор = повтор != null && повтор.Length > 0,
                свет = RovSystems.lightsOn, зарядАч = RovSystems.batteryCharge_Ah,
                повреждения = new[] { RovSystems.thrusterDamage, RovSystems.cameraDamage, RovSystems.manipulatorDamage,
                                      RovSystems.communicationDamage, RovSystems.lightsDamage },
                максТемп_С = s.МаксТемпература_С, максНаклон_град = s.МаксНаклон_град, времяУчёта_с = s.ВремяУчёта_с,
                времяСоСветом_с = s.ВремяСоСветом_с, времяНаАвтопилоте_с = s.ВремяНаАвтопилоте_с, времяУТрубы_с = s.ВремяУТрубы_с,
                сумКвНаклона = s.СуммаКвНаклона, сумСкорости = s.СуммаСкоростиУТрубы, сумДистанции = s.СуммаДистанции,
                зарядНаСтарте_проц = s.ЗарядНаСтарте_проц, повреждённостьНаСтарте = s.ПовреждённостьНаСтарте,
                физика = PhysicsSettings.Действующие,
                темпДвижителей_С = RovSystems.thrusterTemp_C,
                клешня = RovSystems.grabberClosed,
                автопилот = RovSystems.depthHoldActive, глубинаАвтопилота_м = RovSystems.depthHoldTarget_m,
            };
            var сим = UnityEngine.Object.FindFirstObjectByType<RovSystemsSimulator>();
            if (сим != null) з.зарядРезерва_Ач = сим.ЗарядРезерва_Ач;
            if (s.НПА != null) { з.позицияНПА = s.НПА.position; з.поворотНПА = s.НПА.rotation; }
            if (s.Покрытие != null)
            {
                var sbп = new StringBuilder(s.Покрытие.Length);
                foreach (var b in s.Покрытие) sbп.Append(b ? '1' : '0');
                з.покрытиеСтрока = sbп.ToString();
            }
            з.профильГлубины.AddRange(s.ПрофильГлубины);
            if (m != null)
            {
                var tz = new float[4];
                for (int i = 0; i < 4; i++) tz[i] = i < m.Цели.Count && m.Цели[i].Состояние == MissionObjectiveState.Completed ? m.Цели[i].ВремяЗавершения_с : -1f;
                з.времяЗавершенияЭтапа0 = tz[0]; з.времяЗавершенияЭтапа1 = tz[1]; з.времяЗавершенияЭтапа2 = tz[2]; з.времяЗавершенияЭтапа3 = tz[3];
            }
            if (m != null)
                for (int i = 0; i < m.Цели.Count; i++)
                    з.этапы.Add(new Этап
                    {
                        название = m.Цели[i].Заголовок,
                        состояние = m.Цели[i].Состояние.ToString(),
                        длительность_с = i < s.ДлительностиЦелей.Count ? s.ДлительностиЦелей[i] : -1f
                    });
            foreach (var к in s.Оценить())
                з.критерии.Add(new Критерий { название = к.Название, балл = к.Балл, макс = к.Макс, пояснение = к.Пояснение });
            foreach (var зам in s.Замечания()) з.замечания.Add(зам.Текст);
            foreach (var у in s.Удары) з.удары.Add(new Удар { время_с = у.Время_с, энергия_Дж = у.Энергия_Дж, объект = у.Объект });

            // Дефекты — в порядке номеров (найденные по времени обнаружения, как в окне итогов).
            var порядок = new List<int>();
            for (int i = 0; i < s.Дефекты.Count; i++) порядок.Add(i);
            порядок.Sort((a, b) =>
            {
                var да = s.Дефекты[a]; var дб = s.Дефекты[b];
                if (да.Обнаружен != дб.Обнаружен) return да.Обнаружен ? -1 : 1;
                return да.Обнаружен ? да.Время_с.CompareTo(дб.Время_с) : a.CompareTo(b);
            });
            for (int k = 0; k < порядок.Count; k++)
            {
                var д = s.Дефекты[порядок[k]];
                var зд = new Дефект
                {
                    метка = MissionStats.КороткаяМетка(д.Метка), найден = д.Обнаружен, время_с = д.Время_с,
                    глубина_м = д.Глубина_м, kp_м = д.KP_м, позиция = д.Позиция,
                    поворот = д.Поворот, тип = (int)д.Тип
                };
                if (д.Снимок != null)
                {
                    зд.снимок = $"снимок_{k + 1}.jpg";
                    зд.времяСнимка = д.ВремяСнимка.ToString("HH:mm:ss");
                    File.WriteAllBytes(Path.Combine(папка, зд.снимок), УжатыйJpg(д.Снимок, 480));
                }
                з.дефекты.Add(зд);
            }

            File.WriteAllText(Path.Combine(папка, "итоги.json"), JsonUtility.ToJson(з, true), new UTF8Encoding(false));
            if (!string.IsNullOrEmpty(текстОтчёта))
                File.WriteAllText(Path.Combine(папка, "отчёт.txt"), текстОтчёта, new UTF8Encoding(true));
            if (з.естьПовтор) File.WriteAllBytes(Path.Combine(папка, "повтор.bin"), повтор);
            Debug.Log($"[Архив] Миссия сохранена: {папка}");
            if (!string.IsNullOrEmpty(заменить) && заменить != id) Удалить(заменить);
            return папка;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[Архив] Не удалось сохранить миссию: {e.Message}");
            return null;
        }
    }

    // Снимок → JPEG шириной ширина px. Уменьшаем через GPU (Blit), временные
    // объекты сразу освобождаем.
    static byte[] УжатыйJpg(Texture2D src, int ширина)
    {
        int w = Mathf.Min(ширина, src.width);
        int h = Mathf.Max(1, Mathf.RoundToInt(src.height * (w / (float)src.width)));
        var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32);
        var прежняя = RenderTexture.active;
        Graphics.Blit(src, rt);
        RenderTexture.active = rt;
        var мал = new Texture2D(w, h, TextureFormat.RGB24, false);
        мал.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        мал.Apply();
        RenderTexture.active = прежняя;
        RenderTexture.ReleaseTemporary(rt);
        byte[] jpg = мал.EncodeToJPG(78);
        UnityEngine.Object.Destroy(мал);
        return jpg;
    }

    // Список сохранённых миссий, новые сверху (читает только итоги.json).
    public static List<Запись> Список()
    {
        var список = new List<Запись>();
        if (!Directory.Exists(Папка)) return список;
        foreach (var d in Directory.GetDirectories(Папка))
        {
            string f = Path.Combine(d, "итоги.json");
            if (!File.Exists(f)) continue;
            try
            {
                var з = JsonUtility.FromJson<Запись>(File.ReadAllText(f));
                if (з != null) { з.id = Path.GetFileName(d); список.Add(з); }
            }
            catch (Exception e) { Debug.LogWarning($"[Архив] Пропущена повреждённая запись {d}: {e.Message}"); }
        }
        список.Sort((a, b) => string.CompareOrdinal(b.id, a.id));
        return список;
    }

    // Снимок для просмотра. ВЫЗЫВАЮЩИЙ обязан уничтожить текстуру (Object.Destroy).
    public static Texture2D ЗагрузитьСнимок(string id, string файл)
    {
        if (string.IsNullOrEmpty(файл)) return null;
        string p = Path.Combine(Папка, id, файл);
        if (!File.Exists(p)) return null;
        var t = new Texture2D(2, 2, TextureFormat.RGB24, false);
        if (!t.LoadImage(File.ReadAllBytes(p), true)) { UnityEngine.Object.Destroy(t); return null; }
        return t;
    }

    public static byte[] ЗагрузитьПовтор(string id)
    {
        string p = Path.Combine(Папка, id, "повтор.bin");
        return File.Exists(p) ? File.ReadAllBytes(p) : null;
    }

    public static void Удалить(string id)
    {
        try
        {
            string p = Path.Combine(Папка, id);
            if (Directory.Exists(p)) Directory.Delete(p, true);
        }
        catch (Exception e) { Debug.LogWarning($"[Архив] Не удалось удалить {id}: {e.Message}"); }
    }

    public static void Удалить(IEnumerable<string> ids)
    {
        foreach (var id in ids) Удалить(id);
    }

    public static void УдалитьВсё()
    {
        try { if (Directory.Exists(Папка)) Directory.Delete(Папка, true); }
        catch (Exception e) { Debug.LogWarning($"[Архив] Не удалось очистить архив: {e.Message}"); }
    }

    public static long РазмерБайт()
    {
        if (!Directory.Exists(Папка)) return 0;
        long n = 0;
        foreach (var f in Directory.GetFiles(Папка, "*", SearchOption.AllDirectories))
            try { n += new FileInfo(f).Length; } catch { }
        return n;
    }

    public static string ФорматРазмера(long байт) =>
        байт < 1024 * 1024
            ? (байт / 1024f).ToString("0", CultureInfo.InvariantCulture) + " КБ"
            : (байт / (1024f * 1024f)).ToString("0.0", MissionStats.RU) + " МБ";
}

"""Графики испытаний НПА для текста ВКР.

Читает папки, которые пишет симулятор (RovManeuverTests):
    %USERPROFILE%/AppData/LocalLow/DefaultCompany/My project (1)/Испытания/
  • серия «Все опыты» (<дата>/)   — по PNG на опыт «модель против эталона» + сводная сетка;
  • подбор автопилота (ПИД_<дата>/) — все ступеньки на одном графике «до → после».

Запуск:
    pip install matplotlib
    python Tools/plot_maneuvers.py                     # последние серии обоих видов
    python Tools/plot_maneuvers.py <папка> [<папка>]   # конкретные серии
    python Tools/plot_maneuvers.py ... --out <папка>   # куда сложить картинки (иначе — рядом с CSV)
Картинки — PNG 300 dpi, подписи по-русски.
"""
import csv
import os
import sys
from pathlib import Path

import matplotlib

matplotlib.use("Agg")
import matplotlib.pyplot as plt
from matplotlib.ticker import FuncFormatter

ПАПКА_ИСПЫТАНИЙ = Path(os.environ.get("USERPROFILE", "~")).expanduser() / \
    "AppData" / "LocalLow" / "DefaultCompany" / "My project (1)" / "Испытания"

plt.rcParams.update({"font.family": "DejaVu Sans", "font.size": 9, "axes.titlesize": 10})
# Десятичная запятая на осях — как принято в русском тексте.
ЗАПЯТАЯ = FuncFormatter(lambda x, _: f"{x:g}".replace(".", ",").replace("-", "−"))


def запятые(ax):
    ax.xaxis.set_major_formatter(ЗАПЯТАЯ)
    ax.yaxis.set_major_formatter(ЗАПЯТАЯ)
СИНИЙ, ОРАНЖЕВЫЙ = "#1F77B4", "#E07B24"


def число(s):
    s = s.strip()
    return float(s.replace(",", ".")) if s else None


def прочитать(путь):
    with open(путь, encoding="utf-8-sig", newline="") as f:
        строки = list(csv.reader(f, delimiter=";"))
    заголовок, данные = строки[0], строки[1:]
    столбцы = [[число(r[i]) if i < len(r) else None for r in данные] for i in range(len(заголовок))]
    return заголовок, столбцы


def имя_опыта(путь):
    return путь.stem.split("_", 1)[1].replace("_", " ") if "_" in путь.stem else путь.stem


НАЗВАНИЯ = {"01": "Разгон вперёд и выбег", "02": "Боковой ход и выбег", "03": "Всплытие на тяге и выбег",
            "04": "Погружение на тяге и выбег", "05": "Пассивное всплытие", "06": "Разворот и выбег",
            "07": "Ступенька глубины +2 м (автопилот)", "08": "Снос ровным течением"}


def на_оси(ax, путь, подробно=True):
    заголовок, с = прочитать(путь)
    t, команда, модель, эталон = с[0], с[1], с[2], с[3]
    имя_эталона = заголовок[3].split(",")[0]
    ax.plot(t, эталон, color=ОРАНЖЕВЫЙ, lw=2.6, label=имя_эталона.capitalize(), zorder=2)
    ax.plot(t, модель, color=СИНИЙ, lw=1.2, label="Модель (симулятор)", zorder=3)
    if подробно and len(с) > 4:
        ax.plot(t, с[4], color="#2CA02C", lw=0.8, ls="--", label="Показание датчика глубины", zorder=1)
    ax.set_xlabel("Время, с")
    ax.set_ylabel(заголовок[2].replace(" (модель)", "").capitalize())
    if "глубина" in заголовок[2]:
        ax.invert_yaxis()
    for i in range(1, len(команда)):
        if команда[i - 1] not in (None, 0) and команда[i] == 0:
            ax.axvline(t[i], color="0.55", lw=0.8, ls=":")
            ax.annotate("выключение тяги", (t[i], 0.97), xycoords=("data", "axes fraction"),
                        fontsize=7, color="0.4", ha="left", va="top", xytext=(3, 0), textcoords="offset points")
            break
    ax.grid(True, lw=0.4, alpha=0.5)
    запятые(ax)
    ax.set_title(НАЗВАНИЯ.get(путь.stem[:2], имя_опыта(путь)))


def серия_опытов(папка, выход):
    файлы = sorted(папка.glob("[0-9][0-9]_*.csv"))
    готово = []
    for путь in файлы:
        fig, ax = plt.subplots(figsize=(6.5, 3.4), dpi=300)
        на_оси(ax, путь)
        ax.legend(fontsize=7.5, loc="best")
        fig.tight_layout()
        p = выход / f"{путь.stem}.png"
        fig.savefig(p); plt.close(fig); готово.append(p)
    # Сводная сетка опытов 1–6 (проверка адекватности одной картинкой)
    шесть = [f for f in файлы if f.stem[:2] in ("01", "02", "03", "04", "05", "06")]
    if шесть:
        fig, оси = plt.subplots(3, 2, figsize=(7.0, 8.2), dpi=300)
        for ax, путь in zip(оси.flat, шесть):
            на_оси(ax, путь, подробно=False)
            ax.title.set_fontsize(9)
        оси.flat[0].legend(fontsize=7, loc="lower right")
        fig.tight_layout()
        p = выход / "00_сводка_опыты_1-6.png"
        fig.savefig(p); plt.close(fig); готово.append(p)
    return готово


def подбор_автопилота(папка, выход):
    прогоны = []
    таблица = папка / "прогоны.csv"
    строки = {}
    if таблица.exists():
        with open(таблица, encoding="utf-8-sig") as f:
            for r in list(csv.reader(f, delimiter=";"))[1:]:
                строки[int(r[0])] = r
    for путь in sorted(папка.glob("ступенька_*.csv")):
        n = int(путь.stem.split("_")[1])
        _, с = прочитать(путь)
        t, модель, эталон = с[0], с[2], с[3]
        старт = модель[0]                # фактическая глубина в момент команды
        r = строки.get(n)
        подпись = f"{n}) Kp {r[1]}, Ki {r[2]}, Kd {r[3]}, защита {r[4]} — σ = {r[6]} %" if r else путь.stem
        прогоны.append((n, t, [x - старт for x in модель], подпись))
    if not прогоны:
        return []
    цвета = ["#D62728", "#FF7F0E", "#BCBD22", "#9467BD", "#1F77B4", "#17BECF", "#2CA02C"]
    fig, ax = plt.subplots(figsize=(6.8, 3.8), dpi=300)
    ax.axhline(2.0, color=ОРАНЖЕВЫЙ, lw=2.2, label="Уставка (+2 м)", zorder=1)
    ax.axhspan(1.9, 2.1, color=ОРАНЖЕВЫЙ, alpha=0.08, lw=0)
    for k, (n, t, dh, подпись) in enumerate(прогоны):
        последний = k == len(прогоны) - 1
        ax.plot(t, dh, color=цвета[k % len(цвета)], lw=2.0 if последний else 1.1, label=подпись, zorder=3 if последний else 2)
    ax.set_xlabel("Время после команды, с")
    ax.set_ylabel("Погружение относительно исходной глубины, м")
    ax.set_title("Переходная характеристика контура глубины при подборе регулятора")
    ax.grid(True, lw=0.4, alpha=0.5)
    запятые(ax)
    ax.legend(fontsize=6.8, loc="lower right")
    fig.tight_layout()
    p = выход / "подбор_автопилота.png"
    fig.savefig(p); plt.close(fig)
    return [p]


def main():
    арг = [a for a in sys.argv[1:]]
    выход_общий = None
    if "--out" in арг:
        i = арг.index("--out"); выход_общий = Path(арг[i + 1]); del арг[i:i + 2]
        выход_общий.mkdir(parents=True, exist_ok=True)
    if арг:
        папки = [Path(a) for a in арг]
    else:
        серии = sorted(p for p in ПАПКА_ИСПЫТАНИЙ.glob("20*") if p.is_dir())
        пид = sorted(p for p in ПАПКА_ИСПЫТАНИЙ.glob("ПИД_*") if p.is_dir())
        папки = ([серии[-1]] if серии else []) + ([пид[-1]] if пид else [])
    if not папки:
        sys.exit(f"Серий испытаний не найдено в {ПАПКА_ИСПЫТАНИЙ}")
    for папка in папки:
        выход = выход_общий or папка
        готово = подбор_автопилота(папка, выход) if папка.name.startswith("ПИД_") else серия_опытов(папка, выход)
        print(f"Серия {папка.name}:")
        for p in готово:
            print("   ", p)


if __name__ == "__main__":
    main()

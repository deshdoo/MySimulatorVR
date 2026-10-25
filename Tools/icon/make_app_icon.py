"""Рисованный вариант значка: «Марлин-350» над трубопроводом (запасной, в сборке не стоит).

Рисует слои адаптивного значка Android (фон и передний план, 432×432 и крупнее)
и обычный квадратный значок. Цвета — материалы модели «Марлина» (ТНПА_Марлин350.fbx):
жёлтая плавучесть, белая полипропиленовая рама, белый гермоблок, красные винты.

    python Tools/icon/make_app_icon.py
"""
import math, os, random
from PIL import Image, ImageDraw, ImageFilter, ImageChops

S = 2048            # рисуем крупно, потом уменьшаем — сглаживание
OUT = os.path.join(os.path.dirname(__file__), 'drawn')   # в сборку не идёт: значок — AppIcon_Original.webp

def lerp(a, b, t): return tuple(int(a[i] + (b[i] - a[i]) * t) for i in range(len(a)))

def вертикальный_градиент(w, h, стопы):
    img = Image.new('RGBA', (w, h))
    px = img.load()
    for y in range(h):
        t = y / (h - 1)
        for i in range(len(стопы) - 1):
            t0, c0 = стопы[i]; t1, c1 = стопы[i + 1]
            if t0 <= t <= t1:
                c = lerp(c0, c1, (t - t0) / (t1 - t0)); break
        for x in range(w): px[x, y] = c
    return img

def слой(): return Image.new('RGBA', (S, S), (0, 0, 0, 0))

def размыть(img, r): return img.filter(ImageFilter.GaussianBlur(r))

def фон(ty=0.775):
    """ty — верх трубы, доля высоты (у адаптивного значка труба выше: края обрежет лаунчер)."""
    img = вертикальный_градиент(S, S, [(0, (38, 150, 190, 255)), (0.45, (8, 62, 98, 255)), (1, (2, 16, 34, 255))])
    # Лучи солнца сверху
    лучи = слой(); d = ImageDraw.Draw(лучи)
    for x0, ширина, a in [(0.18, 0.07, 40), (0.36, 0.05, 30), (0.58, 0.09, 36), (0.8, 0.05, 26)]:
        cx = x0 * S
        d.polygon([(cx - ширина * S / 2, 0), (cx + ширина * S / 2, 0),
                   (cx + ширина * S * 2.2 + 0.25 * S, S * 0.85), (cx - ширина * S * 0.4 + 0.25 * S, S * 0.85)],
                  fill=(190, 240, 255, a))
    img = Image.alpha_composite(img, размыть(лучи, 30))
    # Дно — пологий бугор ила
    дно = слой(); d = ImageDraw.Draw(дно)
    d.ellipse([-0.4 * S, (ty + 0.025) * S, 1.4 * S, 1.6 * S], fill=(24, 44, 52, 255))
    img = Image.alpha_composite(img, размыть(дно, 6))
    # Трубопровод: бетонное покрытие, уходит в перспективу
    труба = слой(); d = ImageDraw.Draw(труба)
    верх = lambda x: ty * S + (x / S) * 0.035 * S
    низ  = lambda x: (ty + 0.11) * S + (x / S) * 0.015 * S
    шаг = 8
    for x in range(-шаг, S + шаг, шаг):
        y0, y1 = верх(x), низ(x)
        for k in range(int(y0), int(y1)):
            t = min(1.0, max(0.0, (k - y0) / (y1 - y0)))
            свет = 0.35 + 0.65 * math.sin(math.pi * min(1, t * 1.15)) ** 0.8 * (1 - t * 0.6)
            c = lerp((40, 46, 48), (150, 156, 150), свет)
            d.line([(x, k), (x + шаг, k)], fill=c + (255,))
    # Стыки покрытия
    for x in range(int(0.06 * S), S, int(0.22 * S)):
        d.line([(x, верх(x)), (x + 0.01 * S, низ(x))], fill=(28, 32, 34, 255), width=int(0.008 * S))
    # Анод-браслет
    xa = 0.70 * S
    d.rectangle([xa, верх(xa) - 6, xa + 0.035 * S, низ(xa) + 4], fill=(130, 118, 96, 255))
    img = Image.alpha_composite(img, труба)
    # Тень трубы на дне
    тень = слой(); d = ImageDraw.Draw(тень)
    d.ellipse([-0.1 * S, (ty + 0.085) * S, 1.1 * S, (ty + 0.165) * S], fill=(0, 0, 0, 120))
    img = Image.alpha_composite(img, размыть(тень, 24))
    img = Image.alpha_composite(img, труба)
    # Виньетка
    виньетка = слой(); d = ImageDraw.Draw(виньетка)
    d.rectangle([0, 0, S, S], fill=(0, 6, 16, 150))
    d.ellipse([-0.15 * S, -0.1 * S, 1.15 * S, 1.1 * S], fill=(0, 0, 0, 0))
    img = Image.alpha_composite(img, размыть(виньетка, 140))
    return img

def скруглённый(d, box, r, **kw): d.rounded_rectangle(box, radius=r, **kw)

def аппарат(масштаб=1.0, cy=0.47, до_трубы=390):
    """Передний план: «Марлин» анфас чуть сверху, лучи светильников, кабель, пузырьки."""
    img = слой()
    u = S * 0.001 * масштаб          # единица: тысячная холста
    cx, cy = S / 2, S * cy
    def X(v): return cx + v * u
    def Y(v): return cy + v * u

    # Лучи светильников — вниз к трубе
    лучи = слой(); d = ImageDraw.Draw(лучи)
    for сторона in (-1, 1):
        x0, y0 = X(сторона * 175), Y(-40)
        d.polygon([(x0 - 18 * u, y0), (x0 + 18 * u, y0),
                   (X(сторона * 330 + 120), Y(до_трубы)), (X(сторона * 330 - 160), Y(до_трубы))], fill=(255, 244, 200, 80))
        # Пятно света на трубе
        d.ellipse([X(сторона * 300 - 190), Y(до_трубы - 60), X(сторона * 300 + 150), Y(до_трубы + 30)], fill=(255, 240, 190, 110))
    img = Image.alpha_composite(img, размыть(лучи, 26 * масштаб))

    # Кабель-трос сверху (оранжевый)
    d = ImageDraw.Draw(img)
    d.line([(X(0), Y(-520)), (X(10), Y(-330)), (X(0), Y(-205))], fill=(232, 120, 30, 255), width=int(22 * u), joint='curve')

    тень = слой(); dt = ImageDraw.Draw(тень)
    скруглённый(dt, [X(-270), Y(-150), X(270), Y(225)], int(40 * u), fill=(0, 0, 0, 90))
    img = Image.alpha_composite(img, размыть(тень, 40 * масштаб))
    d = ImageDraw.Draw(img)

    белый, белый_тень = (236, 240, 242, 255), (170, 182, 190, 255)
    # Боковины рамы
    for сторона in (-1, 1):
        x_внеш, x_внутр = X(сторона * 290), X(сторона * 238)
        скруглённый(d, [min(x_внеш, x_внутр), Y(-150), max(x_внеш, x_внутр), Y(190)], int(16 * u), fill=белый)
        d.rectangle([min(x_внеш, x_внутр) + (0 if сторона > 0 else 30 * u), Y(-140),
                     min(x_внеш, x_внутр) + (22 * u if сторона > 0 else 52 * u), Y(180)], fill=белый_тень)
    # Полозья
    скруглённый(d, [X(-300), Y(175), X(300), Y(205)], int(14 * u), fill=(205, 212, 218, 255))
    # Нижняя поперечина
    d.rectangle([X(-240), Y(120), X(240), Y(145)], fill=белый_тень)

    # Внутренность рамы — тёмная
    d.rectangle([X(-238), Y(-130), X(238), Y(120)], fill=(22, 30, 38, 255))
    # Гермоблок (белый цилиндр) за камерой
    скруглённый(d, [X(-150), Y(-110), X(150), Y(40)], int(70 * u), fill=(214, 220, 226, 255))
    скруглённый(d, [X(-150), Y(-110), X(150), Y(-70)], int(30 * u), fill=(246, 248, 250, 255))

    # Горизонтальные движители по нижним углам — красные винты в кольцах
    for сторона in (-1, 1):
        x, y = X(сторона * 170), Y(80)
        r = 62 * u
        d.ellipse([x - r, y - r, x + r, y + r], fill=(48, 54, 60, 255))
        d.ellipse([x - r * 0.78, y - r * 0.78, x + r * 0.78, y + r * 0.78], fill=(18, 20, 24, 255))
        for k in range(3):
            a = math.radians(k * 120 + (20 if сторона > 0 else 50))
            лоп = [(x, y), (x + math.cos(a - 0.45) * r * 0.72, y + math.sin(a - 0.45) * r * 0.72),
                   (x + math.cos(a + 0.25) * r * 0.72, y + math.sin(a + 0.25) * r * 0.72)]
            d.polygon(лоп, fill=(214, 38, 38, 255))
        d.ellipse([x - r * 0.18, y - r * 0.18, x + r * 0.18, y + r * 0.18], fill=(170, 176, 180, 255))

    # Купол камеры по центру
    r = 66 * u; x, y = X(0), Y(-20)
    d.ellipse([x - r * 1.18, y - r * 1.18, x + r * 1.18, y + r * 1.18], fill=(150, 160, 168, 255))
    d.ellipse([x - r, y - r, x + r, y + r], fill=(14, 40, 60, 255))
    d.ellipse([x - r * 0.55, y - r * 0.55, x + r * 0.55, y + r * 0.55], fill=(8, 18, 28, 255))
    d.ellipse([x - r * 0.2, y - r * 0.2, x + r * 0.2, y + r * 0.2], fill=(40, 120, 160, 255))
    блик = слой(); db = ImageDraw.Draw(блик)
    db.ellipse([x - r * 0.75, y - r * 0.85, x - r * 0.05, y - r * 0.3], fill=(255, 255, 255, 150))
    img = Image.alpha_composite(img, размыть(блик, 6 * масштаб))
    d = ImageDraw.Draw(img)

    # Схват снизу по центру
    d.rectangle([X(-14), Y(120), X(14), Y(165)], fill=(150, 156, 160, 255))
    d.polygon([(X(-14), Y(160)), (X(-44), Y(200)), (X(-30), Y(206)), (X(-4), Y(172))], fill=(120, 126, 130, 255))
    d.polygon([(X(14), Y(160)), (X(44), Y(200)), (X(30), Y(206)), (X(4), Y(172))], fill=(120, 126, 130, 255))

    # Блок плавучести сверху — жёлтый
    жёлтый, жёлтый_свет, жёлтый_тень = (247, 190, 22, 255), (255, 222, 96, 255), (196, 132, 8, 255)
    скруглённый(d, [X(-310), Y(-250), X(310), Y(-120)], int(40 * u), fill=жёлтый_тень)
    скруглённый(d, [X(-310), Y(-262), X(310), Y(-140)], int(40 * u), fill=жёлтый)
    скруглённый(d, [X(-290), Y(-255), X(290), Y(-222)], int(18 * u), fill=жёлтый_свет)
    # Скоба подвеса
    d.arc([X(-38), Y(-318), X(38), Y(-242)], 180, 360, fill=(180, 188, 194, 255), width=int(14 * u))

    # Светильники на углах блока
    for сторона in (-1, 1):
        x, y = X(сторона * 175), Y(-118)
        скруглённый(d, [x - 38 * u, y - 22 * u, x + 38 * u, y + 24 * u], int(12 * u), fill=(60, 66, 72, 255))
        d.ellipse([x - 24 * u, y - 14 * u, x + 24 * u, y + 18 * u], fill=(255, 250, 225, 255))
        сияние = слой(); dg = ImageDraw.Draw(сияние)
        dg.ellipse([x - 70 * u, y - 60 * u, x + 70 * u, y + 64 * u], fill=(255, 240, 190, 120))
        img = Image.alpha_composite(img, размыть(сияние, 24 * масштаб))
        d = ImageDraw.Draw(img)

    # Пузырьки
    rnd = random.Random(7)
    for _ in range(14):
        bx = X(rnd.uniform(-420, 420)); by = Y(rnd.uniform(-470, -250))
        br = rnd.uniform(6, 16) * u
        d.ellipse([bx - br, by - br, bx + br, by + br], outline=(220, 245, 255, 170), width=max(2, int(3 * u)))
    return img

def сохранить(img, имя, размер):
    os.makedirs(OUT, exist_ok=True)
    img.resize((размер, размер), Image.LANCZOS).save(os.path.join(OUT, имя))

if __name__ == '__main__':
    ф = фон()
    # Обычный значок: аппарат крупно
    полный = Image.alpha_composite(ф, аппарат(1.12, 0.46))
    сохранить(полный, 'AppIcon.png', 1024)
    # Адаптивный: передний план в безопасной зоне (центральные 61 %) — лаунчер обрежет края
    сохранить(фон(0.66), 'AppIcon_Background.png', 432 * 2)
    сохранить(аппарат(0.66, 0.46, 390), 'AppIcon_Foreground.png', 432 * 2)
    print('готово:', os.path.abspath(OUT))

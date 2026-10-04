#!/usr/bin/env python3
"""Rule/status icons (white glyph, dark outline, transparent) for the hero's rule display and HUD. No AI imagery."""
import math, os
from PIL import Image, ImageDraw, ImageFilter
ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), '..'))
OUT = os.path.join(ROOT, 'Escort/Assets/_Game/Resources/Icons')
S = 256

def canvas():
    return Image.new('RGBA', (S, S), (0, 0, 0, 0))

def finish(im, name):
    # dark outline via dilated alpha
    a = im.split()[3]
    halo = a.filter(ImageFilter.MaxFilter(15))
    base = Image.new('RGBA', (S, S), (18, 16, 22, 0))
    base.putalpha(halo.point(lambda v: 230 if v > 0 else 0))
    base.alpha_composite(im)
    base.save(os.path.join(OUT, name + '.png'))

W = (255, 255, 255, 255)

def sword(d, cx, cy, ang, length=180, w=18):
    ca, sa = math.cos(ang), math.sin(ang)
    def P(x, y): return (cx + x * ca - y * sa, cy + x * sa + y * ca)
    blade = [P(-w/2, -length/2), P(w/2, -length/2), P(w/2, length/2 - 30), P(0, length/2), P(-w/2, length/2 - 30)]
    d.polygon(blade, fill=W)
    guard = [P(-45, -length/2 - 4), P(45, -length/2 - 4), P(45, -length/2 + 14), P(-45, -length/2 + 14)]
    d.polygon(guard, fill=W)
    grip = [P(-8, -length/2 - 40), P(8, -length/2 - 40), P(8, -length/2), P(-8, -length/2)]
    d.polygon(grip, fill=W)

def icons():
    os.makedirs(OUT, exist_ok=True)
    # fight: crossed swords
    im = canvas(); d = ImageDraw.Draw(im)
    sword(d, 128, 128, math.radians(-40)); sword(d, 128, 128, math.radians(40)); finish(im, 'fight')
    # challenge / salute: single upright sword
    im = canvas(); d = ImageDraw.Draw(im); sword(d, 128, 140, math.radians(180)); finish(im, 'challenge')
    # wait: hourglass
    im = canvas(); d = ImageDraw.Draw(im)
    d.rectangle([60, 30, 196, 50], fill=W); d.rectangle([60, 206, 196, 226], fill=W)
    d.polygon([(76, 50), (180, 50), (136, 128), (180, 206), (76, 206), (120, 128)], fill=W)
    d.polygon([(96, 60), (160, 60), (128, 110)], fill=(0, 0, 0, 0)); finish(im, 'wait')
    # fall back: shield
    im = canvas(); d = ImageDraw.Draw(im)
    d.polygon([(128, 24), (216, 56), (206, 150), (128, 232), (50, 150), (40, 56)], fill=W); finish(im, 'fallback')
    # scold: exclamation
    im = canvas(); d = ImageDraw.Draw(im)
    d.rounded_rectangle([104, 28, 152, 164], 20, fill=W); d.ellipse([102, 184, 154, 236], fill=W); finish(im, 'scold')
    # threshold: doorway arch
    im = canvas(); d = ImageDraw.Draw(im)
    d.rectangle([56, 90, 84, 230], fill=W); d.rectangle([172, 90, 200, 230], fill=W)
    d.pieslice([56, 30, 200, 150], 180, 360, fill=W); d.pieslice([84, 58, 172, 122], 180, 360, fill=(0, 0, 0, 0))
    d.rectangle([84, 90, 172, 230], fill=(0, 0, 0, 0)); finish(im, 'threshold')
    # route: footsteps
    im = canvas(); d = ImageDraw.Draw(im)
    for (x, y) in [(80, 170), (150, 90)]:
        d.ellipse([x, y, x + 40, y + 62], fill=W); d.ellipse([x + 6, y - 30, x + 34, y - 6], fill=W)
    finish(im, 'route')
    # parry / riposte: shield + spark
    im = canvas(); d = ImageDraw.Draw(im)
    d.polygon([(128, 40), (196, 66), (188, 140), (128, 206), (68, 140), (60, 66)], fill=W)
    d.polygon([(128, 70), (140, 110), (180, 118), (140, 128), (128, 170), (116, 128), (76, 118), (116, 110)], fill=(0, 0, 0, 0)); finish(im, 'riposte')
    # recite / honor: laurel-ish star
    im = canvas(); d = ImageDraw.Draw(im)
    pts = []
    for i in range(10):
        r = 104 if i % 2 == 0 else 46
        a = math.radians(-90 + i * 36)
        pts.append((128 + r * math.cos(a), 132 + r * math.sin(a)))
    d.polygon(pts, fill=W); finish(im, 'honor')
    # eye (witness / insight)
    im = canvas(); d = ImageDraw.Draw(im)
    d.ellipse([28, 72, 228, 184], fill=W); d.ellipse([98, 98, 158, 158], fill=(0, 0, 0, 0)); d.ellipse([114, 114, 142, 142], fill=W); finish(im, 'eye')
    # ping marker
    im = canvas(); d = ImageDraw.Draw(im)
    d.polygon([(128, 236), (60, 110), (196, 110)], fill=W); d.ellipse([60, 40, 196, 176], fill=W); d.ellipse([100, 80, 156, 136], fill=(0, 0, 0, 0)); finish(im, 'ping')
    print('icons written to', OUT)



# ---------------------------------------------------------------------------------------------------------------------
# Tutorial / HUD icons (2026-10-03): skills, verbs, wounds, skill families, marks. Same style: white glyph, dark outline.
CLEAR = (0, 0, 0, 0)


def thick(d, pts, w, fill=W):
    d.line(pts, fill=fill, width=w, joint='curve')
    r = w / 2
    for (x, y) in (pts[0], pts[-1]):
        d.ellipse([x - r, y - r, x + r, y + r], fill=fill)


def star(d, cx, cy, ro, ri, n=5, rot=-90, fill=W):
    pts = []
    for i in range(n * 2):
        r = ro if i % 2 == 0 else ri
        a = math.radians(rot + i * 180 / n)
        pts.append((cx + r * math.cos(a), cy + r * math.sin(a)))
    d.polygon(pts, fill=fill)


def arrowhead(d, tip, direction, length=34, half=24, fill=W):
    dx, dy = direction
    L = math.hypot(dx, dy)
    dx, dy = dx / L, dy / L
    nx, ny = -dy, dx
    bx, by = tip[0] - dx * length, tip[1] - dy * length
    d.polygon([tip, (bx + nx * half, by + ny * half), (bx - nx * half, by - ny * half)], fill=fill)


def arc_arrow(d, box, start, end, width, head_at_start=False, fill=W):
    """PIL arc (degrees clockwise from 3 o'clock) with an arrowhead at one end, pointing along the arc."""
    d.arc(box, start=start, end=end, fill=fill, width=width)
    cx, cy = (box[0] + box[2]) / 2, (box[1] + box[3]) / 2
    rx, ry = (box[2] - box[0]) / 2 - width / 2, (box[3] - box[1]) / 2 - width / 2
    a = math.radians(start if head_at_start else end)
    p = (cx + rx * math.cos(a), cy + ry * math.sin(a))
    t = (rx * math.sin(a), -ry * math.cos(a)) if head_at_start else (-rx * math.sin(a), ry * math.cos(a))
    arrowhead(d, (p[0] + t[0] / math.hypot(*t) * 26, p[1] + t[1] / math.hypot(*t) * 26), t, length=44, half=width * 1.25)


def rotated(draw_fn, degrees):
    """Draw on a fresh canvas, then rotate it (for shapes PIL can't draw at an angle)."""
    im = canvas()
    draw_fn(ImageDraw.Draw(im))
    return im.rotate(degrees, resample=Image.BICUBIC, center=(S / 2, S / 2))


def bez(p0, p1, p2, t):
    return ((1 - t) ** 2 * p0[0] + 2 * (1 - t) * t * p1[0] + t * t * p2[0],
            (1 - t) ** 2 * p0[1] + 2 * (1 - t) * t * p1[1] + t * t * p2[1])


def tutorial_icons():
    os.makedirs(OUT, exist_ok=True)

    # --- skills ---------------------------------------------------------------------------------------------------
    # Pocket Sand: a cinched pouch, grit spraying up and out of it
    im = canvas(); d = ImageDraw.Draw(im)
    d.ellipse([34, 132, 146, 238], fill=W)
    d.polygon([(64, 140), (118, 140), (108, 112), (74, 112)], fill=W)
    d.ellipse([58, 90, 126, 120], fill=W)
    d.line([(66, 126), (116, 126)], fill=CLEAR, width=6)
    for (x, y, r) in [(128, 78, 13), (158, 58, 12), (152, 98, 10), (188, 40, 11), (190, 80, 10), (218, 26, 8),
                      (216, 62, 9), (226, 102, 7), (240, 44, 6), (180, 116, 7), (242, 80, 6)]:
        d.ellipse([x - r, y - r, x + r, y + r], fill=W)
    finish(im, 'skill_pocket_sand')

    # Loosen Bolt: a hex nut with a counter-clockwise ("lefty-loosey") arrow over it
    im = canvas(); d = ImageDraw.Draw(im)
    cx, cy = 128, 142
    d.polygon([(cx + 64 * math.cos(math.radians(a)), cy + 64 * math.sin(math.radians(a))) for a in range(0, 360, 60)], fill=W)
    d.ellipse([cx - 27, cy - 27, cx + 27, cy + 27], fill=CLEAR)
    arc_arrow(d, [cx - 112, cy - 116, cx + 112, cy + 108], 208, 338, 20, head_at_start=True)
    finish(im, 'skill_loosen_bolt')

    # Quiet Feet: a feather
    im = canvas(); d = ImageDraw.Draw(im)
    p0, p1, p2 = (48, 222), (72, 96), (218, 34)
    left, right, n = [], [], 48
    for i in range(n + 1):
        t = i / n
        x, y = bez(p0, p1, p2, t)
        x1, y1 = bez(p0, p1, p2, max(0, t - 0.01)); x2, y2 = bez(p0, p1, p2, min(1, t + 0.01))
        tx, ty = x2 - x1, y2 - y1; L = math.hypot(tx, ty); tx, ty = tx / L, ty / L
        nx, ny = -ty, tx
        u = (t - 0.16) / 0.84
        w = 0 if u <= 0 else 46 * math.sin(math.pi * min(1.0, u)) ** 0.75
        left.append((x + nx * w, y + ny * w)); right.append((x - nx * w * 0.82, y - ny * w * 0.82))
    d.polygon(left + right[::-1], fill=W)
    thick(d, [bez(p0, p1, p2, t / 20) for t in range(0, 5)], 12)
    d.line([bez(p0, p1, p2, 0.2 + 0.74 * i / 24) for i in range(25)], fill=CLEAR, width=6)
    for t in (0.44, 0.68):
        i = int(t * n)
        d.polygon([left[i], bez(p0, p1, p2, t + 0.06), left[i + 4]], fill=CLEAR)
    finish(im, 'skill_quiet_feet')

    # Crossbow: top-down, bow across the top, string drawn back to the nut, a bolt laid in the groove
    im = canvas(); d = ImageDraw.Draw(im)
    d.rounded_rectangle([114, 66, 142, 238], 10, fill=W)
    d.arc([22, 46, 234, 178], start=196, end=344, fill=W, width=20)
    d.line([(34, 98), (128, 146), (222, 98)], fill=W, width=7)
    d.rectangle([124, 30, 132, 72], fill=W)
    d.polygon([(128, 6), (148, 40), (108, 40)], fill=W)
    d.rectangle([142, 178, 160, 194], fill=W)
    finish(im, 'skill_crossbow')

    # Bandage: an adhesive strip at 45 degrees
    def bandage(d):
        d.rounded_rectangle([24, 92, 232, 164], 36, fill=W)
        d.rectangle([94, 98, 162, 158], outline=CLEAR, width=6)
        for (x, y) in [(52, 116), (52, 140), (70, 128), (204, 116), (204, 140), (186, 128)]:
            d.ellipse([x - 6, y - 6, x + 6, y + 6], fill=CLEAR)
    finish(rotated(bandage, 45), 'skill_bandage')

    # Cover Story: a speech bubble with a knowing "..."
    im = canvas(); d = ImageDraw.Draw(im)
    d.rounded_rectangle([24, 36, 232, 176], 48, fill=W)
    d.polygon([(66, 160), (120, 170), (46, 230)], fill=W)
    for x in (84, 128, 172):
        d.ellipse([x - 15, 92, x + 15, 122], fill=CLEAR)
    finish(im, 'skill_cover_story')

    # --- verbs ----------------------------------------------------------------------------------------------------
    def knife(d):
        d.polygon([(98, 104), (222, 104), (240, 112), (226, 128), (184, 140), (98, 142)], fill=W)
        d.rectangle([90, 98, 104, 148], fill=W)
        d.rounded_rectangle([18, 108, 94, 138], 12, fill=W)
        for x in (40, 66):
            d.ellipse([x - 6, 117, x + 6, 129], fill=CLEAR)
    finish(rotated(knife, 38), 'verb_knife')

    im = canvas(); d = ImageDraw.Draw(im)   # dodge: a roll arrow over the ground
    arc_arrow(d, [36, 44, 220, 228], 196, 340, 24)
    d.rounded_rectangle([24, 214, 232, 230], 8, fill=W)
    for y in (150, 182):
        d.rounded_rectangle([8, y, 52, y + 12], 6, fill=W)
    finish(im, 'verb_dodge')

    im = canvas(); d = ImageDraw.Draw(im)   # ping: the pin with signal arcs
    d.polygon([(128, 240), (82, 156), (174, 156)], fill=W)
    d.ellipse([80, 104, 176, 200], fill=W)
    d.ellipse([110, 134, 146, 170], fill=CLEAR)
    d.arc([60, 30, 196, 166], start=225, end=315, fill=W, width=16)
    d.arc([22, -4, 234, 208], start=235, end=305, fill=W, width=16)
    finish(im, 'verb_ping')

    im = canvas(); d = ImageDraw.Draw(im)   # crouch: lowering chevrons onto the ground
    for y in (34, 98):
        d.polygon([(44, y), (128, y + 58), (212, y), (212, y + 32), (128, y + 90), (44, y + 32)], fill=W)
    d.rounded_rectangle([36, 210, 220, 230], 9, fill=W)
    finish(im, 'verb_crouch')

    im = canvas(); d = ImageDraw.Draw(im)   # interact: an open hand
    d.rounded_rectangle([70, 112, 188, 230], 40, fill=W)
    for (x, top) in [(86, 46), (114, 30), (142, 34), (170, 56)]:
        d.rounded_rectangle([x - 13, top, x + 13, 140], 13, fill=W)
    thick(d, [(80, 182), (40, 128)], 28)
    finish(im, 'verb_interact')

    # --- wounds ---------------------------------------------------------------------------------------------------
    im = canvas(); d = ImageDraw.Draw(im)   # sprained ankle: a boot with a crack
    d.rounded_rectangle([82, 26, 152, 160], 10, fill=W)
    d.polygon([(82, 150), (152, 150), (214, 178), (224, 214), (78, 214)], fill=W)
    d.rounded_rectangle([72, 206, 230, 232], 10, fill=W)
    d.line([(80, 132), (108, 120), (98, 146), (130, 136), (120, 160)], fill=CLEAR, width=8)
    finish(im, 'wound_ankle')

    im = canvas(); d = ImageDraw.Draw(im)   # cracked ribs
    d.rounded_rectangle([118, 24, 138, 232], 8, fill=W)
    for y in (46, 92, 138):
        d.arc([40, y, 216, y + 96], start=180, end=270, fill=W, width=16)
        d.arc([40, y, 216, y + 96], start=270, end=360, fill=W, width=16)
    d.line([(52, 128), (70, 116), (64, 140), (84, 130)], fill=CLEAR, width=7)
    finish(im, 'wound_ribs')

    im = canvas(); d = ImageDraw.Draw(im)   # sword-arm strain: a broken sword (the tip snapped off and falling away)
    d.polygon([(110, 156), (110, 98), (122, 90), (132, 102), (146, 88), (146, 156)], fill=W)        # lower blade, jagged top
    d.polygon([(150, 70), (156, 40), (176, 10), (186, 46), (172, 78), (160, 66)], fill=W)           # the snapped tip
    d.rounded_rectangle([74, 154, 182, 174], 8, fill=W)                                           # guard
    d.rectangle([116, 172, 140, 218], fill=W)                                                     # grip
    d.ellipse([110, 210, 146, 246], fill=W)                                                       # pommel
    finish(im, 'wound_arm')

    im = canvas(); d = ImageDraw.Draw(im)   # fever: a thermometer
    d.rounded_rectangle([108, 20, 148, 196], 20, fill=W)
    d.ellipse([88, 166, 168, 246], fill=W)
    d.rounded_rectangle([120, 38, 136, 160], 8, fill=CLEAR)
    d.rounded_rectangle([120, 104, 136, 190], 8, fill=W)
    for y in (52, 76, 100):
        d.line([(160, y), (184, y)], fill=W, width=7)
    finish(im, 'wound_fever')

    im = canvas(); d = ImageDraw.Draw(im)   # concussion: a spiral and stars
    pts = []
    for i in range(0, 161):
        th = i / 160 * 3.6 * math.pi
        r = 8 + 13 * th / math.pi
        pts.append((128 + r * math.cos(th), 140 + r * math.sin(th)))
    d.line(pts, fill=W, width=14, joint='curve')
    star(d, 50, 44, 32, 13); star(d, 210, 52, 26, 11); star(d, 214, 206, 22, 9)
    finish(im, 'wound_concussion')

    # --- skill families --------------------------------------------------------------------------------------------
    def wrench(d):
        d.rounded_rectangle([70, 112, 236, 144], 16, fill=W)
        d.ellipse([12, 84, 100, 172], fill=W)
        d.rectangle([12, 114, 58, 142], fill=CLEAR)
        d.ellipse([208, 120, 224, 136], fill=CLEAR)
    finish(rotated(wrench, -45), 'fam_fixer')

    im = canvas(); d = ImageDraw.Draw(im)   # handler: a signal flag
    d.rounded_rectangle([52, 22, 72, 236], 8, fill=W)
    d.polygon([(72, 32), (124, 18), (176, 36), (222, 30), (214, 112), (168, 120), (118, 104), (72, 116)], fill=W)
    finish(im, 'fam_handler')

    im = canvas(); d = ImageDraw.Draw(im)   # provisioner: a flask
    d.ellipse([48, 92, 208, 246], fill=W)
    d.rectangle([102, 36, 154, 110], fill=W)
    d.rounded_rectangle([90, 22, 166, 46], 8, fill=W)
    d.line([(52, 150), (204, 150)], fill=CLEAR, width=7)                                          # liquid level
    d.ellipse([140, 178, 160, 198], fill=CLEAR); d.ellipse([104, 196, 118, 210], fill=CLEAR)      # bubbles
    finish(im, 'fam_provisioner')

    im = canvas(); d = ImageDraw.Draw(im)   # scholar: an open book
    d.polygon([(20, 68), (124, 88), (124, 216), (20, 196)], fill=W)
    d.polygon([(236, 68), (132, 88), (132, 216), (236, 196)], fill=W)
    for k in range(4):
        y = 110 + k * 22
        d.line([(38, y - 14), (108, y)], fill=CLEAR, width=5)
        d.line([(148, y), (218, y - 14)], fill=CLEAR, width=5)
    finish(im, 'fam_scholar')

    im = canvas(); d = ImageDraw.Draw(im)   # combat: a dagger
    d.polygon([(128, 14), (152, 60), (146, 150), (110, 150), (104, 60)], fill=W)
    d.rounded_rectangle([76, 148, 180, 168], 8, fill=W)
    d.rectangle([116, 166, 140, 218], fill=W)
    d.ellipse([110, 210, 146, 246], fill=W)
    finish(im, 'fam_combat')

    # --- marks ----------------------------------------------------------------------------------------------------
    im = canvas(); d = ImageDraw.Draw(im)   # blinded: stars over a closed eye
    star(d, 62, 92, 44, 18); star(d, 128, 56, 40, 16); star(d, 194, 92, 44, 18)
    d.arc([56, 136, 200, 226], start=15, end=165, fill=W, width=16)
    for (x0, y0, x1, y1) in [(84, 206, 70, 238), (128, 224, 128, 252), (172, 206, 186, 238)]:
        d.line([(x0, y0), (x1, y1)], fill=W, width=10)
    finish(im, 'blind')

    im = canvas(); d = ImageDraw.Draw(im)   # alert: "!" cut out of a burst
    star(d, 128, 128, 118, 90, n=12, rot=-90)
    d.rounded_rectangle([112, 48, 144, 148], 14, fill=CLEAR)
    d.ellipse([110, 164, 146, 200], fill=CLEAR)
    finish(im, 'alert')

    im = canvas(); d = ImageDraw.Draw(im)   # question
    d.arc([66, 22, 190, 146], start=180, end=420, fill=W, width=30)
    thick(d, [(152, 132), (128, 150), (128, 176)], 30)
    d.ellipse([110, 196, 146, 232], fill=W)
    finish(im, 'question')

    im = canvas(); d = ImageDraw.Draw(im)   # check
    thick(d, [(40, 134), (100, 194), (216, 62)], 40)
    finish(im, 'check')
    print('tutorial icons written to', OUT)


# ---------------------------------------------------------------------------------------------------------------------
# Campaign icons (2026-10-04): Callum's Recall rule and the chapter 2 skills. Same style.
def campaign_icons():
    os.makedirs(OUT, exist_ok=True)
    # recall: a figure lying flat, an arrow lifting it
    im = canvas(); d = ImageDraw.Draw(im)
    d.rounded_rectangle([40, 196, 196, 226], 15, fill=W); d.ellipse([196, 190, 236, 230], fill=W)
    thick(d, [(128, 176), (128, 70)], 26); arrowhead(d, (128, 30), (0, -1), 48, 40)
    finish(im, 'recall')
    # splint & stitch: two splints bound, a stitched line
    im = canvas(); d = ImageDraw.Draw(im)
    d.rounded_rectangle([70, 30, 106, 226], 14, fill=W); d.rounded_rectangle([150, 30, 186, 226], 14, fill=W)
    for y in (70, 128, 186):
        d.rectangle([60, y - 9, 196, y + 9], fill=W)
    for k in range(5):
        x = 84 + k * 22
        d.line([(x, 96), (x + 12, 108)], fill=CLEAR, width=6)
    finish(im, 'skill_splint_and_stitch')
    # pull back: a curved arrow hooking backwards
    im = canvas(); d = ImageDraw.Draw(im)
    arc_arrow(d, [40, 40, 216, 216], 200, 20, 28, head_at_start=True)
    d.ellipse([168, 150, 216, 198], fill=W)
    finish(im, 'skill_pull_back')
    # sling: two cords to a pouch with a stone, swung
    im = canvas(); d = ImageDraw.Draw(im)
    thick(d, [(60, 40), (118, 168)], 14); thick(d, [(196, 40), (138, 168)], 14)
    d.ellipse([92, 150, 164, 214], fill=W); d.ellipse([110, 166, 146, 200], fill=CLEAR); d.ellipse([116, 172, 140, 196], fill=W)
    d.ellipse([46, 26, 76, 56], fill=W); d.ellipse([180, 26, 210, 56], fill=W)
    finish(im, 'skill_sling')
    # read the room: an eye with three marks of intent above it
    im = canvas(); d = ImageDraw.Draw(im)
    d.ellipse([28, 108, 228, 212], fill=W); d.ellipse([98, 130, 158, 190], fill=CLEAR); d.ellipse([114, 146, 142, 174], fill=W)
    for x in (70, 128, 186):
        d.rounded_rectangle([x - 9, 22, x + 9, 72], 8, fill=W); d.ellipse([x - 10, 78, x + 10, 98], fill=W)
    finish(im, 'skill_read_the_room')
    # ration: a loaf with score marks
    im = canvas(); d = ImageDraw.Draw(im)
    d.rounded_rectangle([34, 96, 222, 190], 46, fill=W)
    d.ellipse([60, 70, 196, 150], fill=W)
    for x in (92, 128, 164):
        d.line([(x - 12, 92), (x + 12, 128)], fill=CLEAR, width=10)
    finish(im, 'ration')
    # pendant: a dull stone on a cord
    im = canvas(); d = ImageDraw.Draw(im)
    thick(d, [(60, 30), (128, 120), (196, 30)], 12)
    d.ellipse([84, 112, 172, 214], fill=W); d.ellipse([104, 134, 152, 190], fill=CLEAR); d.ellipse([116, 146, 140, 176], fill=W)
    finish(im, 'pendant')
    # snare: a noose loop pegged to the ground
    im = canvas(); d = ImageDraw.Draw(im)
    d.ellipse([56, 110, 200, 210], outline=W, width=20)
    thick(d, [(128, 112), (128, 40)], 14); d.rectangle([108, 22, 148, 44], fill=W)
    thick(d, [(40, 200), (40, 236)], 14); thick(d, [(216, 200), (216, 236)], 14)
    finish(im, 'snare')
    # judgment: an upright sword with rays (Callum's Finisher)
    im = canvas(); d = ImageDraw.Draw(im)
    sword(d, 128, 150, math.radians(180), 170, 20)
    for k in range(7):
        a = math.radians(-90 + (k - 3) * 26)
        x0, y0 = 128 + math.cos(a) * 92, 118 + math.sin(a) * 92
        x1, y1 = 128 + math.cos(a) * 122, 118 + math.sin(a) * 122
        thick(d, [(x0, y0), (x1, y1)], 12)
    finish(im, 'judgment')
    # look away: a closed eye (a lid's curve and lashes)
    im = canvas(); d = ImageDraw.Draw(im)
    d.arc([28, 40, 228, 190], 20, 160, fill=W, width=24)
    for k in range(5):
        a = math.radians(40 + k * 25)
        x0, y0 = 128 + math.cos(a) * 92, 115 + math.sin(a) * 68
        x1, y1 = 128 + math.cos(a) * 122, 115 + math.sin(a) * 96
        thick(d, [(x0, y0), (x1, y1)], 12)
    finish(im, 'look_away')
    # read runes: a rune stone with a glyph
    im = canvas(); d = ImageDraw.Draw(im)
    d.rounded_rectangle([58, 26, 198, 232], 40, fill=W)
    thick(d, [(128, 58), (128, 200)], 14, CLEAR); thick(d, [(128, 92), (168, 64)], 14, CLEAR); thick(d, [(128, 140), (88, 112)], 14, CLEAR)
    finish(im, 'skill_read_runes')
    # lockpick: a padlock with a pick in the keyhole
    im = canvas(); d = ImageDraw.Draw(im)
    d.arc([70, 24, 186, 140], 180, 360, fill=W, width=22)
    d.rectangle([70, 80, 92, 112], fill=W); d.rectangle([164, 80, 186, 112], fill=W)
    d.rounded_rectangle([46, 104, 210, 228], 22, fill=W)
    d.ellipse([112, 138, 144, 170], fill=CLEAR); d.rectangle([122, 160, 134, 196], fill=CLEAR)
    thick(d, [(130, 160), (232, 238)], 10)
    finish(im, 'skill_lockpick')
    # map sketch: a scroll with a dotted path and an X
    im = canvas(); d = ImageDraw.Draw(im)
    d.rounded_rectangle([30, 46, 226, 210], 18, fill=W)
    for k, (x, y) in enumerate([(62, 176), (88, 150), (112, 132), (138, 124), (162, 108)]):
        d.ellipse([x - 9, y - 9, x + 9, y + 9], fill=CLEAR)
    thick(d, [(176, 74), (204, 102)], 12, CLEAR); thick(d, [(204, 74), (176, 102)], 12, CLEAR)
    finish(im, 'skill_map_sketch')
    # buckler: a round shield with a boss
    im = canvas(); d = ImageDraw.Draw(im)
    d.ellipse([34, 34, 222, 222], fill=W); d.ellipse([58, 58, 198, 198], fill=CLEAR); d.ellipse([96, 96, 160, 160], fill=W)
    for a in range(0, 360, 45):
        r = math.radians(a)
        d.ellipse([128 + math.cos(r) * 82 - 7, 128 + math.sin(r) * 82 - 7, 128 + math.cos(r) * 82 + 7, 128 + math.sin(r) * 82 + 7], fill=CLEAR)
    finish(im, 'skill_buckler')
    print('campaign icons written to', OUT)


def finale_icons():
    """Chapter 4–5: the Bastion's tricks, the capstones, the Gallery's marks."""
    os.makedirs(OUT, exist_ok=True)
    # bait & switch: a cloak on a stick (a scarecrow in her shape)
    im = canvas(); d = ImageDraw.Draw(im)
    thick(d, [(128, 30), (128, 236)], 14); thick(d, [(50, 82), (206, 82)], 14)
    d.ellipse([104, 20, 152, 68], fill=W)
    d.polygon([(70, 88), (186, 88), (206, 196), (50, 196)], fill=W)
    d.polygon([(118, 110), (138, 110), (146, 176), (110, 176)], fill=CLEAR)
    finish(im, 'skill_bait_and_switch')
    # smoke bomb: a round bomb under a billow of three puffs
    im = canvas(); d = ImageDraw.Draw(im)
    d.ellipse([70, 132, 186, 236], fill=W); d.rectangle([116, 116, 140, 140], fill=W)
    for (x, y, r) in [(80, 76, 40), (130, 52, 46), (182, 80, 38)]:
        d.ellipse([x - r, y - r, x + r, y + r], fill=W)
    d.ellipse([96, 160, 132, 196], fill=CLEAR)
    finish(im, 'skill_smoke_bomb')
    # pep talk: a speech bubble with an exclamation
    im = canvas(); d = ImageDraw.Draw(im)
    d.rounded_rectangle([28, 34, 228, 172], 40, fill=W); d.polygon([(70, 160), (120, 160), (56, 228)], fill=W)
    d.rounded_rectangle([116, 56, 140, 122], 10, fill=CLEAR); d.ellipse([114, 130, 142, 158], fill=CLEAR)
    finish(im, 'skill_pep_talk')
    # shoulder check: a shoulder ploughing into a wall of motion lines
    im = canvas(); d = ImageDraw.Draw(im)
    d.pieslice([40, 60, 200, 220], 180, 360, fill=W); d.rectangle([40, 138, 200, 230], fill=W)
    for k, y in enumerate((70, 120, 170)):
        thick(d, [(206, y), (240, y + 6 * (k - 1))], 12)
    d.ellipse([90, 150, 130, 190], fill=CLEAR)
    finish(im, 'skill_shoulder_check')
    # domino effect: three tiles toppling in a row
    im = canvas(); d = ImageDraw.Draw(im)
    for k in range(3):
        im2 = rotated(lambda dd: dd.rounded_rectangle([108, 50, 148, 206], 10, fill=W), -18 * k)
        im.alpha_composite(im2, (int(-60 + k * 60), int(k * 10)))
    d = ImageDraw.Draw(im); d.rectangle([20, 222, 236, 236], fill=W)
    finish(im, 'skill_domino_effect')
    # crossfire: three bolts fanning in on one target
    im = canvas(); d = ImageDraw.Draw(im)
    d.ellipse([168, 98, 236, 166], outline=W, width=12); d.ellipse([192, 122, 212, 142], fill=W)
    for y0 in (40, 132, 224):
        tip = (170, 132 + (y0 - 132) * 0.18)
        thick(d, [(24, y0), tip], 12)
        dx, dy = tip[0] - 24, tip[1] - y0
        n = math.hypot(dx, dy)
        arrowhead(d, (tip[0] + dx / n * 16, tip[1] + dy / n * 16), (dx / n, dy / n), 34, 22)
    finish(im, 'skill_crossfire')
    # hold please: a raised open hand
    im = canvas(); d = ImageDraw.Draw(im)
    d.rounded_rectangle([64, 112, 192, 232], 36, fill=W)
    for k, (x, top) in enumerate([(78, 54), (108, 30), (138, 34), (168, 58)]):
        d.rounded_rectangle([x - 2, top, x + 22, 150], 12, fill=W)
    thick(d, [(70, 168), (30, 118)], 26)
    finish(im, 'skill_hold_please')
    # silent partner: two figures, one half-hidden behind the other
    im = canvas(); d = ImageDraw.Draw(im)
    d.ellipse([136, 34, 196, 94], fill=W); d.rounded_rectangle([120, 100, 212, 236], 40, fill=W)
    d.ellipse([54, 50, 120, 116], fill=W); d.rounded_rectangle([36, 124, 138, 240], 44, fill=W)
    d.ellipse([60, 56, 114, 110], fill=CLEAR)
    finish(im, 'skill_silent_partner')
    # duet: two swords crossed over a ring (he asks for a hand)
    im = canvas(); d = ImageDraw.Draw(im)
    d.ellipse([30, 30, 226, 226], outline=W, width=16)
    sword(d, 128, 128, math.radians(-35), 160, 16); sword(d, 128, 128, math.radians(35), 160, 16)
    finish(im, 'duet')
    # hostage: a figure with bound wrists behind a bow
    im = canvas(); d = ImageDraw.Draw(im)
    d.ellipse([96, 24, 160, 88], fill=W); d.rounded_rectangle([80, 94, 176, 236], 36, fill=W)
    d.rectangle([84, 150, 172, 168], fill=CLEAR); d.rectangle([84, 176, 172, 186], fill=CLEAR)
    d.arc([150, 40, 250, 220], 110, 250, fill=W, width=12)
    finish(im, 'hostage')
    # sluice: a spoked wheel over waves
    im = canvas(); d = ImageDraw.Draw(im)
    d.ellipse([56, 20, 200, 164], outline=W, width=18); d.ellipse([112, 76, 144, 108], fill=W)
    for a in range(0, 360, 60):
        r = math.radians(a)
        thick(d, [(128, 92), (128 + math.cos(r) * 66, 92 + math.sin(r) * 66)], 10)
    for y in (196, 228):
        d.arc([20, y - 24, 84, y + 8], 200, 340, fill=W, width=12); d.arc([84, y - 24, 148, y + 8], 200, 340, fill=W, width=12)
        d.arc([148, y - 24, 212, y + 8], 200, 340, fill=W, width=12)
    finish(im, 'sluice')
    # mirror: a hand mirror, a crack across it
    im = canvas(); d = ImageDraw.Draw(im)
    d.ellipse([56, 20, 200, 180], fill=W); d.ellipse([74, 38, 182, 162], fill=CLEAR)
    thick(d, [(110, 50), (134, 96), (118, 120), (150, 152)], 8)
    d.rounded_rectangle([114, 176, 142, 240], 12, fill=W)
    finish(im, 'mirror')
    # link: two interlocked rings
    im = canvas(); d = ImageDraw.Draw(im)
    d.ellipse([20, 64, 148, 192], outline=W, width=22); d.ellipse([108, 64, 236, 192], outline=W, width=22)
    finish(im, 'link')
    print('finale icons written to', OUT)


if __name__ == '__main__':
    import sys
    which = sys.argv[1] if len(sys.argv) > 1 else 'all'
    if which in ('all', 'rules'):
        icons()
    if which in ('all', 'campaign'):
        campaign_icons()
    if which in ('all', 'tutorial'):
        tutorial_icons()
    if which in ('all', 'finale'):
        finale_icons()

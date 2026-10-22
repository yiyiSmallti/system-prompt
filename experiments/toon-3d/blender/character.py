"""ChatGPT, spring/autumn uniform, as procedural parts.

Proportions follow assets/characters/turnarounds/chatgpt/1-uniform/: 166 cm to the top of the hair,
about 7.2 heads, horn tips at 1.725 m. Every part is one object so it can get its own outline width.
Coordinates: metres, feet on z=0, facing -Y, +X is her left (image right in the front view).
"""
import numpy as np

from geom import MeshBuilder, arclength, blend_normals, catmull_rom, interp, ring_xy, sweep

RNG = np.random.default_rng(7)

# ---------------------------------------------------------------- body tables
# head rings: z, half-width, front depth, back depth, centre y
HEAD = [
    (1.424, 0.003, 0.003, 0.003, -0.064),
    (1.432, 0.018, 0.010, 0.012, -0.060),
    (1.445, 0.036, 0.018, 0.030, -0.052),
    (1.465, 0.054, 0.030, 0.062, -0.040),
    (1.485, 0.066, 0.045, 0.082, -0.028),
    (1.505, 0.073, 0.060, 0.091, -0.017),
    (1.530, 0.077, 0.071, 0.095, -0.008),
    (1.560, 0.079, 0.077, 0.097, 0.000),
    (1.590, 0.077, 0.078, 0.095, 0.004),
    (1.620, 0.067, 0.070, 0.085, 0.007),
    (1.640, 0.050, 0.054, 0.066, 0.008),
    (1.652, 0.026, 0.028, 0.034, 0.008),
    (1.656, 0.004, 0.004, 0.004, 0.008),
]
# torso rings (shirt): z, half-width, front depth, back depth, centre y
TORSO = [
    (0.760, 0.080, 0.050, 0.050, 0.000),
    (0.800, 0.128, 0.072, 0.085, 0.000),
    (0.860, 0.150, 0.083, 0.100, 0.005),
    (0.950, 0.140, 0.078, 0.090, 0.004),
    (1.050, 0.112, 0.068, 0.070, 0.000),
    (1.130, 0.086, 0.060, 0.058, 0.000),
    (1.175, 0.090, 0.064, 0.060, 0.000),
    (1.215, 0.100, 0.076, 0.064, 0.000),
    (1.255, 0.108, 0.084, 0.069, 0.000),
    (1.295, 0.113, 0.076, 0.074, 0.005),
    (1.330, 0.122, 0.060, 0.074, 0.010),
    (1.350, 0.114, 0.052, 0.064, 0.012),
    (1.368, 0.082, 0.046, 0.052, 0.014),
    (1.384, 0.048, 0.040, 0.042, 0.015),
    (1.395, 0.036, 0.034, 0.036, 0.016),
]
# legs: z, centre x, centre y, half-width, front depth, back depth
LEG = [
    (0.030, 0.060, 0.000, 0.028, 0.034, 0.032),
    (0.070, 0.059, 0.000, 0.026, 0.030, 0.030),
    (0.105, 0.058, 0.002, 0.024, 0.026, 0.028),
    (0.160, 0.058, 0.004, 0.029, 0.028, 0.036),
    (0.240, 0.058, 0.004, 0.038, 0.031, 0.047),
    (0.320, 0.058, 0.004, 0.043, 0.034, 0.051),
    (0.400, 0.057, 0.002, 0.040, 0.034, 0.044),
    (0.470, 0.056, 0.000, 0.038, 0.037, 0.037),
    (0.510, 0.056, -0.002, 0.039, 0.040, 0.037),
    (0.560, 0.058, -0.002, 0.044, 0.044, 0.044),
    (0.640, 0.061, -0.002, 0.051, 0.051, 0.053),
    (0.720, 0.064, 0.000, 0.057, 0.057, 0.059),
    (0.800, 0.066, 0.004, 0.062, 0.061, 0.066),
    (0.870, 0.068, 0.008, 0.065, 0.064, 0.069),
]
STOCKING_TOP = 0.728
# skirt rings: z, half-width, depth, centre y
SKIRT = [
    (1.168, 0.096, 0.076, 0.003),
    (1.128, 0.094, 0.077, 0.004),
    (1.090, 0.110, 0.089, 0.006),
    (1.020, 0.141, 0.113, 0.008),
    (0.950, 0.168, 0.135, 0.010),
    (0.880, 0.192, 0.155, 0.012),
    (0.820, 0.210, 0.172, 0.014),
    (0.785, 0.218, 0.180, 0.015),
]
STRIPE = (0.806, 0.820)
# loafer rings along y (heel -> toe): y, half-width, top z
SHOE = [
    (0.060, 0.016, 0.050),
    (0.052, 0.028, 0.072),
    (0.030, 0.034, 0.080),
    (0.000, 0.037, 0.080),
    (-0.040, 0.040, 0.066),
    (-0.090, 0.042, 0.051),
    (-0.130, 0.040, 0.043),
    (-0.160, 0.032, 0.037),
    (-0.178, 0.020, 0.031),
    (-0.186, 0.008, 0.025),
]
FOOT_X = 0.062

HAIR_CAP = dict(c=np.array([0.0, 0.010, 1.552]), r=np.array([0.094, 0.104, 0.110]))


def smooth_table(table, n):
    """Resample a profile table with a Catmull-Rom curve so ring silhouettes have no kinks."""
    return catmull_rom(np.asarray(table, dtype=float), n)


def profile(s, table):
    """Radius (or any scalar) along a normalised arc length s from [(s, value), ...]."""
    return interp(s, table)[0]


# ---------------------------------------------------------------- body
def head(mats):
    mb = MeshBuilder()
    rows = smooth_table(HEAD, 44)
    rings = np.array([ring_xy(0, cy, z, w, df, db, 48, power=2.3) for z, w, df, db, cy in rows])
    # a small nose so the profile is not flat
    front = rings[..., 1] < rows[:, 4, None]
    rings[..., 1] -= 0.006 * front * np.exp(-((rings[..., 2] - 1.474) / 0.012) ** 2) * np.exp(-(rings[..., 0] / 0.008) ** 2)
    mb.add_rings(rings, mat=0)

    def face_uv(p):
        # front planar projection; textures/face.png covers x in [-0.1, 0.1], z in [1.42, 1.62]
        return np.stack([0.5 + p[:, 0] / 0.2, (p[:, 2] - 1.42) / 0.2], axis=1)
    return mb.build("Head", [mats["face"]], uv_fn=face_uv)


def torso_front_y(x, z):
    """Front surface of the shirt at (x, z), including the bust; used to place the bow and buttons."""
    w, df, db, cy = interp(z, TORSO)
    t = np.clip(x / w, -0.999, 0.999)
    y = cy - df * (1 - np.abs(t) ** 2.0) ** 0.5
    return y - bust_offset(x, z)


def bust_offset(x, z):
    return 0.030 * np.exp(-((z - 1.252) / 0.048) ** 2) * np.exp(-((np.abs(x) - 0.052) / 0.042) ** 2)


def torso(mats):
    mb = MeshBuilder()
    rows = smooth_table(TORSO, 56)
    rings = []
    for z, w, df, db, cy in rows:
        r = ring_xy(0, cy, z, w, df, db, 56, power=2.0)
        front = np.clip(-(r[:, 1] - cy) / max(df, 1e-4), 0, 1) ** 0.5
        r[:, 1] -= bust_offset(r[:, 0], z) * front
        rings.append(r)
    mb.add_rings(np.array(rings), mat=0)
    # neck
    path = catmull_rom([(0, 0.018, 1.36), (0, 0.016, 1.42), (0, 0.010, 1.47)], 10)
    mb.add_rings(sweep(path, 0.032, 0.033, 24), mat=1)
    # buttons down the placket
    for z in (1.330, 1.287, 1.243, 1.200):
        c = np.array([0, torso_front_y(0.0, z) - 0.001, z])
        add_ball(mb, c, 0.0045, mat=0)
    return mb.build("Torso", [mats["shirt"], mats["skin"]])


def add_ball(mb, c, r, mat=0, scale=(1, 1, 1), n=12):
    zs = np.cos(np.linspace(0, np.pi, n))
    rings = [ring_xy(c[0], c[1], c[2] + r * scale[2] * z, r * scale[0] * (1 - z * z) ** 0.5,
                     r * scale[1] * (1 - z * z) ** 0.5, r * scale[1] * (1 - z * z) ** 0.5, 16) for z in zs]
    mb.add_rings(np.array(rings), mat=mat)


def collar_and_bow(mats):
    mb = MeshBuilder()
    # stand collar around the neck
    lo = ring_xy(0, 0.016, 1.372, 0.040, 0.044, 0.040, 32)
    hi = ring_xy(0, 0.016, 1.402, 0.037, 0.040, 0.037, 32)
    inner_lo = ring_xy(0, 0.016, 1.372, 0.033, 0.035, 0.034, 32)
    inner_hi = ring_xy(0, 0.016, 1.402, 0.033, 0.035, 0.034, 32)
    mb.add_rings(np.array([inner_lo, lo, hi, inner_hi, inner_lo]), mat=0, cap0=False, cap1=False)
    # collar points
    for sgn in (-1, 1):
        path = catmull_rom([(sgn * 0.010, -0.040, 1.392), (sgn * 0.026, -0.050, 1.372), (sgn * 0.044, -0.056, 1.352)], 8)
        s = np.linspace(0, 1, 8)
        mb.add_rings(sweep(path, 0.003, 0.018 * (1 - s) + 0.002, 10, up=(0, -1, 0.3)), mat=0)
    # bow
    k = np.array([0, torso_front_y(0, 1.360) - 0.012, 1.362])
    add_ball(mb, k, 0.010, mat=1, scale=(1.0, 0.7, 0.9))
    for sgn in (-1, 1):
        loop = catmull_rom([k, k + (sgn * 0.022, -0.004, 0.016), k + (sgn * 0.050, 0.004, 0.010),
                            k + (sgn * 0.046, 0.006, -0.010), k + (sgn * 0.020, -0.002, -0.010), k], 24)
        mb.add_rings(sweep(loop, 0.004, 0.007, 10, up=(0, -1, 0)), mat=1)
        tail = []
        for t in np.linspace(0, 1, 10):
            z = k[2] - 0.010 - t * 0.090
            x = sgn * (0.006 + t * 0.020)
            tail.append((x, torso_front_y(x, z) - 0.006, z))
        s = np.linspace(0, 1, 10)
        mb.add_rings(sweep(np.array(tail), 0.002, 0.008 + 0.004 * s, 10, up=(0, -1, 0)), mat=1)
    return mb.build("CollarBow", [mats["shirt"], mats["bow"]])


ARM_CTRL = [(0.135, 0.004, 1.350), (0.150, 0.004, 1.335), (0.205, 0.012, 1.115), (0.248, 0.000, 0.935)]
SLEEVE_R = [(0.0, 0.046), (0.15, 0.047), (0.40, 0.046), (0.55, 0.048), (0.75, 0.056), (0.86, 0.054),
            (0.885, 0.041), (0.90, 0.037), (1.0, 0.036)]


def arm_path(sgn, n=48):
    ctrl = [(sgn * x, y, z) for x, y, z in ARM_CTRL]
    path = catmull_rom(ctrl, n)
    s = arclength(path)
    return path, s / s[-1]


def arms(mats):
    mb = MeshBuilder()
    for sgn in (-1, 1):
        path, s = arm_path(sgn)
        r = profile(s, SLEEVE_R)
        mb.add_rings(sweep(path, r, r * 0.95, 28), mat=0)
        add_ball(mb, np.array([sgn * 0.144, 0.004, 1.322]), 0.050, mat=0)
        if sgn > 0:
            # student council armband on her left upper arm
            sel = (s > 0.08) & (s < 0.30)
            rr = r[sel] + 0.004
            mb.add_rings(sweep(path[sel], rr, rr * 0.95, 28), mat=1)
    return mb.build("Sleeves", [mats["shirt"], mats["armband"]])


def hand_frame(sgn):
    path, _ = arm_path(sgn)
    wrist = path[-1]
    d = path[-1] - path[-4]
    d /= np.linalg.norm(d)
    pn = np.array([-sgn * 1.0, -0.45, 0.0])        # palm faces the thigh, slightly forward
    pn -= np.dot(pn, d) * d
    pn /= np.linalg.norm(pn)
    a = np.cross(d, pn)
    if a[1] > 0:                                    # thumb side towards the front
        a = -a
    return wrist, d, pn, a


def hands(mats):
    mb = MeshBuilder()
    for sgn in (-1, 1):
        w, d, pn, a = hand_frame(sgn)
        palm = catmull_rom([w - d * 0.01, w + d * 0.03, w + d * 0.072], 10)
        s = np.linspace(0, 1, 10)
        mb.add_rings(sweep(palm, 0.022 + 0.014 * np.sin(s * 2.4), 0.011, 16, up=a), mat=0)
        for k, (off, length, spread) in enumerate([(0.022, 0.058, 0.10), (0.008, 0.066, 0.03),
                                                   (-0.007, 0.062, -0.04), (-0.020, 0.050, -0.10)]):
            base = w + d * 0.068 + a * off
            dirn = d + a * spread
            dirn /= np.linalg.norm(dirn)
            pts = [base, base + dirn * length * 0.5 + pn * 0.004, base + dirn * length * 0.95 + pn * 0.012]
            path = catmull_rom(pts, 10)
            ss = np.linspace(0, 1, 10)
            rr = 0.0075 - 0.002 * ss
            rr[-1] *= 0.5
            mb.add_rings(sweep(path, rr, rr, 10, up=pn), mat=0)
        tb = w + d * 0.020 + a * 0.024 + pn * 0.006
        tdir = d * 0.65 + a * 0.55 + pn * 0.35
        tdir /= np.linalg.norm(tdir)
        path = catmull_rom([tb, tb + tdir * 0.028, tb + tdir * 0.050 + d * 0.006], 10)
        rr = 0.009 - 0.003 * np.linspace(0, 1, 10)
        rr[-1] *= 0.5
        mb.add_rings(sweep(path, rr, rr, 10, up=pn), mat=0)
        if sgn > 0:
            # wrist terminal: thin silver bangle with a lavender crystal
            ring = [w + d * 0.004 + 0.034 * (np.cos(t) * a + np.sin(t) * pn) for t in np.linspace(0, 2 * np.pi, 33)]
            mb.add_rings(sweep(np.array(ring), 0.0025, 0.0025, 8, up=d), mat=1, cap0=False, cap1=False)
            add_crystal(mb, w + d * 0.022 - pn * 0.030 + a * 0.012, 0.007, mat=2)
    return mb.build("Hands", [mats["skin"], mats["silver"], mats["crystal"]])


def add_crystal(mb, c, r, mat):
    """Elongated octahedron (the lavender rhombus crystals in the design)."""
    rings = [np.tile(c + (0, 0, 1.6 * r), (4, 1)),
             c + np.array([(r, 0, 0), (0, r, 0), (-r, 0, 0), (0, -r, 0)]),
             np.tile(c - (0, 0, 1.6 * r), (4, 1))]
    mb.add_rings(np.array(rings), mat=mat, cap0=False, cap1=False)


def legs(mats):
    mb = MeshBuilder()
    rows = smooth_table(LEG, 60)
    for sgn in (-1, 1):
        rings = np.array([ring_xy(sgn * xc, cy, z, w, df, db, 32) for z, xc, cy, w, df, db in rows])
        mb.add_rings(rings, mat_fn=lambda p: 1 if p[2] < STOCKING_TOP else 0)
        # thicker welt at the top of the stocking
        z0, z1 = STOCKING_TOP - 0.012, STOCKING_TOP + 0.002
        band = []
        for z, k in ((z0, 0.99), (z0, 1.045), (z1, 1.045), (z1, 0.99), (z0, 0.99)):
            xc, cy, w, df, db = interp(z, LEG)
            band.append(ring_xy(sgn * xc, cy, z, w * k, df * k, db * k, 32))
        mb.add_rings(np.array(band), mat=1, cap0=False, cap1=False)
    return mb.build("Legs", [mats["skin"], mats["stocking"]])


def shoes(mats):
    mb = MeshBuilder()
    rows = catmull_rom(np.asarray(SHOE, dtype=float), 28)
    t = np.linspace(0, 2 * np.pi, 32, endpoint=False)
    e = 2.0 / 3.0
    c, s = np.cos(t), np.sin(t)
    c, s = np.sign(c) * np.abs(c) ** e, np.sign(s) * np.abs(s) ** e
    for sgn in (-1, 1):
        rings = []
        for y, w, top in rows:
            rings.append(np.stack([sgn * FOOT_X + w * c, np.full_like(c, y), top / 2 + top / 2 * s], axis=1))
        mb.add_rings(np.array(rings), mat_fn=lambda p: 1 if p[2] < 0.012 else 0)
        # heel block
        heel = [ring_xy(sgn * FOOT_X, 0.030, z, 0.030, 0.030, 0.026, 16, power=4) for z in (0.0, 0.02)]
        mb.add_rings(np.array(heel), mat=1)
        # penny-loafer strap across the vamp
        arc = []
        for a in np.linspace(0.12 * np.pi, 0.88 * np.pi, 16):
            ca, sa = np.cos(a), np.sin(a)
            ca, sa = np.sign(ca) * abs(ca) ** e, np.sign(sa) * abs(sa) ** e
            arc.append((sgn * FOOT_X + 0.0435 * ca, -0.075, 0.0285 + 0.0285 * sa + 0.002))
        mb.add_rings(sweep(np.array(arc), 0.0025, 0.009, 8, up=(0, -1, 0)), mat=0)
    return mb.build("Shoes", [mats["shoe"], mats["sole"]])


def skirt(mats):
    mb = MeshBuilder()
    zs = np.unique(np.concatenate([np.linspace(0.785, 1.168, 34), STRIPE, [1.128]]))[::-1]
    n_pleats, per = 26, 6
    t = np.linspace(0, 2 * np.pi, n_pleats * per, endpoint=False)
    saw = (t * n_pleats / (2 * np.pi)) % 1.0
    rings = []
    for z in zs:
        w, d, cy = interp(z, SKIRT)
        amp = 0.075 * np.clip((1.128 - z) / (1.128 - 0.785), 0, 1) ** 0.8
        k = 1 + amp * (saw - 0.5)
        rings.append(np.stack([w * np.cos(t) * k, cy + d * np.sin(t) * k, np.full_like(t, z)], axis=1))
    mb.add_rings(np.array(rings), cap0=False, cap1=False,
                 mat_fn=lambda p: 1 if STRIPE[0] < p[2] < STRIPE[1] else 0)
    return mb.build("Skirt", [mats["skirt"], mats["stripe"]], smooth=False)


# ---------------------------------------------------------------- head details
def ears(mats):
    mb = MeshBuilder()
    for sgn in (-1, 1):
        path = catmull_rom([(sgn * 0.066, 0.016, 1.500), (sgn * 0.100, 0.028, 1.522), (sgn * 0.142, 0.048, 1.556)], 12)
        s = np.linspace(0, 1, 12)
        width = 0.017 * (1 - s ** 1.6) + 0.001
        mb.add_rings(sweep(path, 0.0045 * (1 - 0.6 * s), width, 12, up=(sgn * 0.3, -1, 0)), mat=0)
        add_crystal(mb, np.array([sgn * 0.090, -0.006, 1.462]), 0.0065, mat=1)
    return mb.build("Ears", [mats["skin"], mats["crystal"]])


def cap_point(theta, phi, off=0.0, back=True):
    """Point on the hair-cap ellipsoid; theta from the top, phi=0 is the back (+Y) or the front (-Y)."""
    c, r = HAIR_CAP["c"], HAIR_CAP["r"] + off
    yd = 1 if back else -1
    return c + np.array([r[0] * np.sin(theta) * np.sin(phi), yd * r[1] * np.sin(theta) * np.cos(phi), r[2] * np.cos(theta)])


def horns(mats):
    mb = MeshBuilder()
    for sgn in (-1, 1):
        ctrl = [(sgn * 0.046, 0.016, 1.600), (sgn * 0.070, 0.022, 1.645), (sgn * 0.098, 0.030, 1.676),
                (sgn * 0.108, 0.040, 1.701), (sgn * 0.098, 0.044, 1.721), (sgn * 0.084, 0.040, 1.731)]
        path = catmull_rom(ctrl, 40)
        s = np.linspace(0, 1, 40)
        r = profile(s, [(0, 0.024), (0.3, 0.020), (0.6, 0.014), (0.85, 0.008), (1.0, 0.0012)])
        ridge = np.where(s < 0.85, np.clip(np.sin(2 * np.pi * 6 * s), 0, 1) ** 3, 0)
        r = r * (1 + 0.10 * ridge)
        mb.add_rings(sweep(path, r, r, 20), mat=0)
    return mb.build("Horns", [mats["horn"]])


def rose(mats):
    """White rose knot above her left temple, with a short silver tassel."""
    mb = MeshBuilder()
    c = np.array([0.104, -0.030, 1.585])
    n = np.array([1.0, -0.7, 0.15])
    n /= np.linalg.norm(n)
    u = np.cross(n, (0, 0, 1))
    u /= np.linalg.norm(u)
    v = np.cross(n, u)
    t = np.linspace(0, 5 * np.pi, 80)
    rad = 0.003 + 0.016 * t / t[-1]
    spiral = c + np.outer(rad * np.cos(t), u) + np.outer(rad * np.sin(t), v) - np.outer(0.004 * t / t[-1], n)
    mb.add_rings(sweep(spiral, 0.0055, 0.0022, 10, up=n), mat=0)
    tassel = catmull_rom([c - v * 0.018, c - v * 0.032 + (0, 0, -0.01), c + (0.004, -0.004, -0.060)], 10)
    mb.add_rings(sweep(tassel, 0.0016, 0.0016, 6), mat=1)
    add_crystal(mb, c + (0.004, -0.004, -0.066), 0.005, mat=2)
    return mb.build("Rose", [mats["silver_white"], mats["silver"], mats["crystal"]])


# ---------------------------------------------------------------- hair
def hair_cap(mb):
    c, r = HAIR_CAP["c"], HAIR_CAP["r"]
    t = np.linspace(0, 2 * np.pi, 48, endpoint=False)
    rings = np.array([np.stack([c[0] + r[0] * np.sin(th) * np.cos(t), c[1] + r[1] * np.sin(th) * np.sin(t),
                                np.full_like(t, c[2] + r[2] * np.cos(th))], axis=1)
                      for th in np.linspace(0.02, 2.30, 34)])

    def keep(p):
        # open the face: drop the front of the cap below the hairline
        return None if (p[1] < -0.025 and p[2] < 1.585) else 0
    mb.add_rings(rings, mat_fn=keep, cap1=False)


def strand(mb, path, half_width, thickness, up, wave=None, mat=0):
    """One flattened, tapering lock of hair swept along a path."""
    path = np.asarray(path)
    if wave is not None:
        path = path + wave
    u = np.linspace(0, 1, len(path))
    taper = np.clip((1 - u) / 0.22, 0, 1) ** 0.7
    taper = np.maximum(taper, 0.02)
    root = 0.65 + 0.35 * np.clip(u / 0.08, 0, 1)
    mb.add_rings(sweep(path, thickness * taper * root, half_width * taper * root, 10, up=up), mat=mat)


def bangs(mb):
    for k, phi in enumerate(np.linspace(-1.30, 1.30, 11)):
        jitter = RNG.uniform(-0.006, 0.006)
        on_cap = [cap_point(th, phi, off=0.004 + 0.006 * th, back=False) for th in np.linspace(0.18, 1.02, 6)]
        h = on_cap[-1]
        z_tip = 1.522 - 0.045 * (abs(phi) / 1.3) ** 2 + jitter
        if k == 5:
            z_tip = 1.500          # the lock that falls between the eyes
        drop = [h + (0.0, 0.003, -0.025), (h[0] * 0.97, h[1] + 0.008, z_tip)]
        path = catmull_rom(on_cap + drop, 30)
        outward = path[len(path) // 2] - HAIR_CAP["c"]
        strand(mb, path, 0.016 + 0.003 * RNG.random(), 0.0055, up=outward)


def side_locks(mb):
    for sgn in (-1, 1):
        for k, (phi, z_tip, dx) in enumerate([(1.42, 1.18, 0.0), (1.70, 1.23, 0.018)]):
            on_cap = [cap_point(th, sgn * phi, off=0.006, back=False) for th in np.linspace(0.45, 1.35, 4)]
            ctrl = on_cap + [(sgn * (0.090 + dx), -0.008 + dx, 1.44), (sgn * (0.102 + dx), -0.034 + dx, 1.37),
                             (sgn * (0.112 + dx), -0.066 + dx, 1.30), (sgn * (0.116 + dx), -0.082 + dx, z_tip + 0.05),
                             (sgn * (0.108 + dx), -0.080 + dx, z_tip)]
            path = catmull_rom(ctrl, 40)
            zz = path[:, 2]
            amp = 0.010 * np.clip((1.36 - zz) / 0.15, 0, 1)
            wave = np.outer(amp * np.sin((1.40 - zz) / 0.065 * 2 * np.pi), (1, 0, 0)) * sgn
            strand(mb, path, 0.019, 0.007, up=(sgn, -0.4, 0), wave=wave)


# back hair fall: z, lateral spread, depth of the sheet centre, bulge to the back
FALL = [
    (1.40, 0.145, 0.015, 0.110),
    (1.30, 0.225, 0.035, 0.092),
    (1.15, 0.285, 0.048, 0.098),
    (1.00, 0.330, 0.056, 0.112),
    (0.85, 0.360, 0.060, 0.125),
    (0.70, 0.358, 0.060, 0.125),
    (0.55, 0.320, 0.056, 0.115),
    (0.40, 0.270, 0.050, 0.100),
    (0.22, 0.225, 0.046, 0.090),
]
# layers from the outside in: depth factor, spread factor, root angle on the cap, length offset
LAYERS = [(1.35, 1.00, 0.30, 0.00), (1.00, 0.93, 0.42, 0.05), (0.72, 0.84, 0.55, 0.12)]


def back_strand(mb, phi, layer):
    s, c = np.sin(phi), np.cos(phi)
    depth, spread_k, th0, shorter = LAYERS[layer]
    # locks leave the cap behind the ears, then fan out; lateral ones hang behind the arms
    phi_cap = np.clip(phi, -1.45, 1.45)
    ctrl = [cap_point(th, phi_cap, off=0.004 - 0.002 * layer + 0.010 * (th - th0)) for th in np.linspace(th0, 1.75, 5)]
    for z, spread, y0, back in FALL:
        ctrl.append((s * spread * spread_k, y0 + back * depth * (0.45 + 0.55 * max(c, -0.3)), z))
    path = catmull_rom(ctrl, 90)
    z_end = 0.70 - 0.44 * (abs(phi) / 2.0) ** 1.3 + RNG.uniform(-0.05, 0.05) + shorter
    below = np.nonzero(path[:, 2] < z_end)[0]
    if len(below):
        path = path[:below[0] + 1]
    zz = path[:, 2]
    L = arclength(path)
    amp = 0.005 + 0.036 * np.clip((1.30 - zz) / 0.75, 0, 1)
    lam = 0.19 + 0.05 * RNG.random()
    ph = RNG.uniform(0, 2 * np.pi)
    side = np.array([c, -s, 0.0])            # across the hair sheet
    out = np.array([s, c, 0.0])               # away from the body
    # helical offset: large round curls rather than a flat zigzag
    wave = np.outer(amp * np.sin(2 * np.pi * L / lam + ph), side) + np.outer(0.8 * amp * np.cos(2 * np.pi * L / lam + ph), out)
    widen = 1 + 0.5 * np.clip((1.35 - zz) / 0.8, 0, 1)
    strand(mb, path, (0.032 + 0.008 * RNG.random()) * widen, 0.020, up=out, wave=wave)


def ahoge(mb):
    path = catmull_rom([(0.0, 0.004, 1.650), (0.004, -0.002, 1.690), (0.012, -0.016, 1.712),
                        (0.023, -0.028, 1.704), (0.025, -0.024, 1.690)], 20)
    strand(mb, path, 0.006, 0.003, up=(0, 0, 1))


def hair(mats):
    mb = MeshBuilder()
    hair_cap(mb)
    bangs(mb)
    side_locks(mb)
    for layer, (count, span) in enumerate([(25, 2.0), (19, 1.85), (13, 1.6)]):
        for phi in np.linspace(-span, span, count):
            back_strand(mb, phi + RNG.uniform(-0.03, 0.03), layer)
    ahoge(mb)
    ob = mb.build("Hair", [mats["hair"]])
    blend_normals(ob, hair_proxy_normal, 0.8)
    ob.visible_shadow = False      # strands shadowing each other is what makes cel-shaded hair look noisy
    return ob


def hair_proxy_normal(p):
    """Normals of a capsule around the head and the hanging hair: a sphere above the eyes,
    a vertical cylinder below."""
    c = np.stack([np.zeros(len(p)), np.full(len(p), 0.02), np.minimum(p[:, 2], 1.53)], axis=1)
    return p - c


# ---------------------------------------------------------------- wings and tail
def wings(mats):
    mb = MeshBuilder()
    for sgn in (-1, 1):
        R = np.array([sgn * 0.045, 0.088, 1.270])
        E = np.array([sgn * 0.195, 0.185, 1.385])
        tips = [np.array([sgn * 0.258, 0.180, 1.130]), np.array([sgn * 0.215, 0.215, 1.055]),
                np.array([sgn * 0.140, 0.200, 1.085])]
        back = np.array([sgn * 0.060, 0.110, 1.150])
        boundary = []
        for a, b in zip(tips, tips[1:] + [back]):
            mid = (a + b) / 2
            ctrl = mid + 0.45 * (E - mid)
            for t in np.linspace(0, 1, 9)[:-1]:
                boundary.append((1 - t) ** 2 * a + 2 * t * (1 - t) * ctrl + t * t * b)
        boundary.append(back)
        boundary.append(R + (0, 0.004, 0))
        mb.add_fan(E, np.array(boundary), mat=0)
        # bones
        arm = catmull_rom([R, (sgn * 0.120, 0.150, 1.360), E], 14)
        mb.add_rings(sweep(arm, np.linspace(0.010, 0.007, 14), np.linspace(0.010, 0.007, 14), 10), mat=1)
        for tip in tips:
            mid = (E + tip) / 2 + (0, 0.008, 0) + (sgn * 0.010, 0, 0)
            bone = catmull_rom([E, mid, tip], 14)
            r = np.linspace(0.0065, 0.0018, 14)
            mb.add_rings(sweep(bone, r, r, 8), mat=1)
        claw = catmull_rom([E, E + (sgn * 0.010, 0.002, 0.020), E + (sgn * 0.004, -0.002, 0.034)], 8)
        r = np.linspace(0.006, 0.0008, 8)
        mb.add_rings(sweep(claw, r, r, 8), mat=1)
    return mb.build("Wings", [mats["wing"], mats["wing_bone"]])


TAIL_CTRL = [(0, 0.060, 0.860), (0, 0.112, 0.780), (0, 0.150, 0.640), (0, 0.172, 0.460), (0, 0.182, 0.290),
             (0, 0.192, 0.140), (0, 0.228, 0.050), (0, 0.300, 0.030), (0, 0.360, 0.058), (0, 0.386, 0.118),
             (0, 0.378, 0.170)]


def tail(mats):
    mb = MeshBuilder()
    path = catmull_rom(TAIL_CTRL, 90)
    L = arclength(path)
    s = L / L[-1]
    r = profile(s, [(0, 0.050), (0.15, 0.046), (0.40, 0.038), (0.60, 0.030), (0.75, 0.022), (0.88, 0.013), (1.0, 0.0015)])
    mb.add_rings(sweep(path, r, r, 28, up=(1, 0, 0)), mat=0, u_scale=3.0, v_coords=L / 0.05)
    tangents = np.gradient(path, axis=0)
    tangents /= np.linalg.norm(tangents, axis=1, keepdims=True)
    for sk in np.linspace(0.10, 0.90, 17):
        i = int(sk * (len(path) - 1))
        dorsal = np.cross((1, 0, 0), tangents[i])
        dorsal /= np.linalg.norm(dorsal)
        base = path[i] + dorsal * r[i] * 0.75
        dirn = dorsal + 0.7 * tangents[i]
        dirn /= np.linalg.norm(dirn)
        size = 0.036 * (1 - 0.6 * sk)
        spike = catmull_rom([base, base + dirn * size * 0.5, base + dirn * size + tangents[i] * size * 0.25], 8)
        w = np.linspace(0.012 * (1 - 0.5 * sk), 0.0006, 8)
        mb.add_rings(sweep(spike, 0.0028 * np.linspace(1, 0.3, 8), w, 8, up=(1, 0, 0)), mat=1)
    return mb.build("Tail", [mats["tail"], mats["horn"]])


def build_all(mats):
    """Returns {object: outline thickness in metres}."""
    return {
        head(mats): 0.0020,
        torso(mats): 0.0022,
        collar_and_bow(mats): 0.0012,
        arms(mats): 0.0022,
        hands(mats): 0.0012,
        legs(mats): 0.0022,
        shoes(mats): 0.0018,
        skirt(mats): 0.0024,
        ears(mats): 0.0014,
        horns(mats): 0.0018,
        rose(mats): 0.0010,
        hair(mats): 0.0026,
        wings(mats): 0.0018,
        tail(mats): 0.0022,
    }

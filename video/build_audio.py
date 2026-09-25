"""Synthesize an original music bed + SFX, timed to the voiceover timeline,
then mix with the VO (auto-ducking the music under speech).

Everything is generated from scratch with numpy — no samples, no licensing.
Key of D, 100 BPM, I–V–vi–IV with extended voicings (Dmaj9, A/C#, Bm9, Gmaj7).
"""
import json, os
import numpy as np
import soundfile as sf

HERE = os.path.dirname(os.path.abspath(__file__))
SR = 48000
rng = np.random.default_rng(7)

tl = json.load(open(os.path.join(HERE, "timeline.json")))
SEG = {s["id"]: s for s in tl["segments"]}
TOTAL = tl["total"]
N = int(TOTAL * SR)
t_all = np.arange(N) / SR

BPM = 100
BEAT = 60 / BPM
BAR = BEAT * 4


def midi(m):
    return 440.0 * 2 ** ((m - 69) / 12)


# D major: Dmaj9 | A/C# (add9) | Bm9 | Gmaj7(#11-ish, soft)
CHORDS = [
    {"root": 38, "notes": [62, 66, 69, 73, 76]},   # D  F# A  C# E
    {"root": 37, "notes": [61, 64, 69, 71, 76]},   # C# E  A  B  E   (A/C#)
    {"root": 35, "notes": [62, 66, 69, 71, 73]},   # D  F# A  B  C#  (Bm9-ish)
    {"root": 31, "notes": [62, 66, 67, 71, 74]},   # D  F# G  B  D   (Gmaj7)
]


def env_adsr(n, a, r, sr=SR):
    e = np.ones(n, dtype=np.float32)
    na, nr = int(a * sr), int(r * sr)
    if na:
        e[:na] = np.linspace(0, 1, na) ** 1.5
    if nr:
        e[-nr:] *= np.linspace(1, 0, nr) ** 1.2
    return e


def soft_saw(freq, n, detune_cents=0.0, harmonics=10, brightness=1.6):
    f = freq * 2 ** (detune_cents / 1200)
    t = np.arange(n) / SR
    ph = rng.uniform(0, 2 * np.pi)
    out = np.zeros(n, dtype=np.float32)
    for h in range(1, harmonics + 1):
        if f * h > 9000:
            break
        out += (np.sin(2 * np.pi * f * h * t + ph * h) / h ** brightness).astype(np.float32)
    return out


def one_pole_lp(x, cutoff):
    """cutoff: scalar or per-sample array (Hz)."""
    c = np.broadcast_to(np.asarray(cutoff, dtype=np.float64), x.shape)
    a = np.exp(-2 * np.pi * c / SR)
    y = np.empty_like(x)
    acc = 0.0
    for i in range(len(x)):
        acc = (1 - a[i]) * x[i] + a[i] * acc
        y[i] = acc
    return y


def place(buf, clip, t0, gain=1.0):
    s = int(t0 * SR)
    if s >= len(buf):
        return
    e = min(len(buf), s + len(clip))
    buf[s:e] += clip[: e - s] * gain


# ---------- section markers from the VO timeline ----------
T_INTRO_END = SEG["v2"]["start"] - 0.2
T_B = SEG["v3a"]["start"] - 0.1          # process section: groove comes in
T_C = SEG["v4"]["start"] - 0.1           # "No spreadsheets" — pull the drums back
T_D = SEG["v5a"]["start"] - 0.1          # scenarios — build
T_MILLION = SEG["v5d"]["start"] + 0.8 * (SEG["v5d"]["end"] - SEG["v5d"]["start"])
T_OUTRO = SEG["v6"]["start"] - 0.35      # drums stop, final chord rings

L = np.zeros(N, dtype=np.float32)
R = np.zeros(N, dtype=np.float32)


def add_st(clip_l, clip_r, t0, gain=1.0):
    place(L, clip_l, t0, gain)
    place(R, clip_r, t0, gain)


SL = np.zeros(N, dtype=np.float32)
SR_ = np.zeros(N, dtype=np.float32)


def add_sfx(clip_l, clip_r, t0, gain=1.0):
    """SFX get their own bus: ducked less than the music so they still
    punctuate (impact, chime), but never louder than the voice."""
    place(SL, clip_l, t0, gain)
    place(SR_, clip_r, t0, gain)


# ---------- pad ----------
pad_l = np.zeros(N, dtype=np.float32)
pad_r = np.zeros(N, dtype=np.float32)
bar_idx, t = 0, 0.0
while t < TOTAL:
    ch = CHORDS[bar_idx % 4]
    # hold the tonic through the whole outro so the piece resolves home
    if t >= T_OUTRO:
        ch = CHORDS[0]
        n = int((TOTAL - t) * SR)
        rel = min(2.0, TOTAL - t - 0.05)
    else:
        n = int((BAR + 0.9) * SR)   # overlap into the next chord
        rel = 0.9
    e = env_adsr(n, 0.7 if t > 0 else 3.2, rel)
    for m in ch["notes"]:
        f = midi(m - 12)
        pad_l[int(t * SR): int(t * SR) + n] += (soft_saw(f, n, -4) * e)[: max(0, N - int(t * SR))]
        pad_r[int(t * SR): int(t * SR) + n] += (soft_saw(f, n, +4) * e)[: max(0, N - int(t * SR))]
    if t >= T_OUTRO:
        break
    t += BAR
    bar_idx += 1
# warm it up: gentle lowpass that opens during the build
cut = np.where(t_all < T_D, 1400.0, 1400.0 + 2600.0 * np.clip((t_all - T_D) / (T_MILLION - T_D), 0, 1))
cut = np.where(t_all > T_OUTRO, 1800.0, cut)
pad_l = one_pole_lp(pad_l, cut).astype(np.float32)
pad_r = one_pole_lp(pad_r, cut).astype(np.float32)
pad_gain = np.clip(t_all / 3.0, 0, 1) * 0.11
L += pad_l * pad_gain
R += pad_r * pad_gain

# ---------- bass (from the groove onward) ----------
t = 0.0
bar_idx = 0
while t < TOTAL:
    ch = CHORDS[bar_idx % 4] if t < T_OUTRO else CHORDS[0]
    if t >= T_B - 0.01:
        n = int((BAR if t < T_OUTRO else TOTAL - t) * SR)
        tt = np.arange(n) / SR
        f = midi(ch["root"])
        b = (np.sin(2 * np.pi * f * tt) + 0.25 * np.sin(2 * np.pi * 2 * f * tt)).astype(np.float32)
        b *= env_adsr(n, 0.04, 0.25 if t < T_OUTRO else 1.8)
        g = 0.16 if t < T_D else 0.2
        if T_C <= t < T_D:
            g = 0.1
        add_st(b, b, t, g)
    if t >= T_OUTRO:
        break
    t += BAR
    bar_idx += 1

# ---------- arpeggio pluck (8ths) ----------
def pluck(freq, dur=0.45, bright=1.0):
    n = int(dur * SR)
    tt = np.arange(n) / SR
    x = np.sin(2 * np.pi * freq * tt) + 0.35 * bright * np.sin(2 * np.pi * 2 * freq * tt) + 0.12 * bright * np.sin(2 * np.pi * 3 * freq * tt)
    return (x * np.exp(-tt / 0.18) * env_adsr(n, 0.004, 0.05)).astype(np.float32)

step = BEAT / 2
t = T_INTRO_END
i = 0
pattern = [0, 2, 4, 1, 3, 2, 4, 3]
while t < T_OUTRO - 0.05:
    bar_no = int(t / BAR)
    ch = CHORDS[bar_no % 4]
    note = ch["notes"][pattern[i % 8] % len(ch["notes"])] + 12
    in_build = t >= T_D
    bright = 1.4 if in_build else 1.0
    g = 0.045 * (0.75 + 0.25 * (i % 2 == 0))
    if in_build:
        g *= 1.25
    if T_C <= t < T_D:
        g *= 0.8
    p = pluck(midi(note), bright=bright)
    pan = 0.3 * np.sin(i * 0.9)
    add_st(p * (1 - pan), p * (1 + pan), t, g)
    if in_build and i % 2 == 0:   # octave doubling for lift
        p2 = pluck(midi(note + 12), dur=0.3, bright=0.8)
        add_st(p2 * (1 + pan), p2 * (1 - pan), t, g * 0.35)
    t += step
    i += 1

# ---------- drums ----------
def kick():
    n = int(0.42 * SR)
    tt = np.arange(n) / SR
    f = 45 + 75 * np.exp(-tt / 0.035)
    ph = 2 * np.pi * np.cumsum(f) / SR
    x = np.sin(ph) * np.exp(-tt / 0.16)
    x[:60] += np.linspace(0.6, 0, 60) * rng.uniform(-1, 1, 60)  # click
    return x.astype(np.float32)

def hat():
    n = int(0.06 * SR)
    x = np.diff(rng.uniform(-1, 1, n + 1)).astype(np.float32)
    return x * np.exp(-np.arange(n) / SR / 0.012).astype(np.float32)

K, H = kick(), hat()
beat_t = 0.0
b = 0
while beat_t < T_OUTRO - 0.05:
    if beat_t >= T_B:
        in_c = T_C <= beat_t < T_D
        in_d = beat_t >= T_D
        if (b % 4 in (0, 2)) or in_d:
            if not in_c:
                add_st(K, K, beat_t, 0.32 if in_d else 0.26)
        # 8th-note hats, quieter on the off-beat
        for k, off in enumerate((0.0, BEAT / 2)):
            if not in_c or k == 1:
                g = (0.035 if k == 0 else 0.05) * (1.3 if in_d else 1.0)
                add_st(H * 0.8, H, beat_t + off, g)
    beat_t += BEAT
    b += 1

# ---------- SFX ----------
def whoosh(dur=0.9, up=True):
    n = int(dur * SR)
    tt = np.linspace(0, 1, n)
    noise = rng.uniform(-1, 1, n).astype(np.float32)
    cutoff = (300 + 5200 * (tt if up else 1 - tt) ** 2)
    x = one_pole_lp(noise, cutoff) - one_pole_lp(noise, cutoff * 0.25)
    shape = np.sin(np.pi * tt) ** 2
    return (x * shape).astype(np.float32)

def boom(dur=2.2, f0=58, f1=32):
    n = int(dur * SR)
    tt = np.arange(n) / SR
    f = f1 + (f0 - f1) * np.exp(-tt / 0.25)
    x = np.sin(2 * np.pi * np.cumsum(f) / SR) * np.exp(-tt / 0.7)
    return x.astype(np.float32)

def shimmer(dur=2.4, base=1760):
    n = int(dur * SR)
    tt = np.arange(n) / SR
    x = np.zeros(n, dtype=np.float32)
    for k, m in enumerate([0, 7, 12, 16, 19]):
        f = base * 2 ** (m / 12)
        x += (np.sin(2 * np.pi * f * tt + k) * np.exp(-tt / (0.9 - k * 0.1))).astype(np.float32)
    return x * env_adsr(n, 0.25, 0.8)

def chime():
    n = int(0.9 * SR)
    tt = np.arange(n) / SR
    x = np.zeros(n, dtype=np.float32)
    for f, d, a in ((1318.5, 0.0, 1.0), (1975.5, 0.09, 0.8)):
        s = int(d * SR)
        seg = tt[: n - s]
        x[s:] += (a * np.sin(2 * np.pi * f * seg) * np.exp(-seg / 0.28)).astype(np.float32)
    return x

def tick():
    n = int(0.03 * SR)
    tt = np.arange(n) / SR
    return (np.sin(2 * np.pi * 3200 * tt) * np.exp(-tt / 0.004)).astype(np.float32)

def riser(dur):
    n = int(dur * SR)
    tt = np.linspace(0, 1, n)
    noise = rng.uniform(-1, 1, n).astype(np.float32)
    x = one_pole_lp(noise, 200 + 7000 * tt ** 2.2) - one_pole_lp(noise, 120 + 900 * tt ** 2)
    tone_f = 220 * 2 ** (tt * 2.0)
    tone = np.sin(2 * np.pi * np.cumsum(tone_f) / SR) * 0.25
    return ((x + tone) * tt ** 2.4).astype(np.float32)

def impact():
    b = boom(2.8, 70, 30)
    n = int(1.6 * SR)
    noise = rng.uniform(-1, 1, n).astype(np.float32)
    crash = (noise - one_pole_lp(noise, 2500)) * np.exp(-np.arange(n) / SR / 0.45)
    out = np.zeros(len(b), dtype=np.float32)
    out += b
    out[:n] += crash.astype(np.float32) * 0.35
    return out

# logo arrival
bm = boom()
add_sfx(bm, bm, 0.35, 0.55)
sh = shimmer()
add_sfx(sh * 0.9, sh, 0.25, 0.05)

# scene transitions
for tt_ in (T_INTRO_END - 0.45, SEG["v3a"]["start"] - 0.55, SEG["v4"]["start"] - 0.55,
            SEG["v5a"]["start"] - 0.55, SEG["v6"]["start"] - 0.7):
    w = whoosh()
    add_sfx(w, w * 0.85, tt_, 0.09)

# approval chime right on "decision in seconds"
ch_ = chime()
add_sfx(ch_ * 0.9, ch_, SEG["v3e"]["end"] - 0.15, 0.07)

# count-up ticks as each scenario figure rolls up
for sid in ("v5b", "v5c", "v5d"):
    start = SEG[sid]["start"] + 0.25
    for k in range(16):
        tk = tick()
        add_sfx(tk, tk, start + k * 0.055, 0.035 * (1 - k / 20))

# riser into the "one point two million" impact
rise_len = max(1.2, T_MILLION - SEG["v5c"]["end"])
rs = riser(rise_len)
add_sfx(rs * 0.9, rs, T_MILLION - rise_len, 0.11)
im = impact()
add_sfx(im, im, T_MILLION, 0.42)

# end card shimmer
sh2 = shimmer(3.0, 1318.5)
add_sfx(sh2, sh2 * 0.9, SEG["v6"]["start"] - 0.2, 0.045)

# ---------- mix with VO + ducking ----------
vo, sr_vo = sf.read(os.path.join(HERE, "vo.wav"), dtype="float32")
assert sr_vo == SR
vo = np.pad(vo, (0, max(0, N - len(vo))))[:N]

# smoothed VO envelope -> music gain reduction (attack ~25ms, release ~300ms)
rect = np.abs(vo)
env = np.empty_like(rect)
acc = 0.0
a_att, a_rel = np.exp(-1 / (0.025 * SR)), np.exp(-1 / (0.30 * SR))
for i in range(N):
    x = rect[i]
    a = a_att if x > acc else a_rel
    acc = a * acc + (1 - a) * x
    env[i] = acc
env = env / (env.max() or 1.0)
duck = 1.0 - 0.6 * np.clip(env * 4.0, 0, 1)   # up to ~ -8 dB under speech

MUSIC_GAIN = 0.3   # music bus sits well under the voice (~12-15 dB below it)
SFX_GAIN = 0.5
sfx_duck = 1.0 - 0.3 * np.clip(env * 4.0, 0, 1)
music_l = L * duck * MUSIC_GAIN + SL * sfx_duck * SFX_GAIN
music_r = R * duck * MUSIC_GAIN + SR_ * sfx_duck * SFX_GAIN
# fade out the very end
fade = np.clip((TOTAL - t_all) / 1.2, 0, 1)
music_l *= fade
music_r *= fade

mix_l = vo * 1.0 + music_l
mix_r = vo * 1.0 + music_r
peak = max(np.max(np.abs(mix_l)), np.max(np.abs(mix_r)))
gain = 0.93 / peak
mix = np.stack([mix_l, mix_r], axis=1) * gain
mix = np.tanh(mix * 1.05) / np.tanh(1.05)  # gentle safety saturation
sf.write(os.path.join(HERE, "soundtrack.wav"), mix.astype(np.float32), SR)
sf.write(os.path.join(HERE, "music_only.wav"), (np.stack([music_l, music_r], axis=1) * gain).astype(np.float32), SR)


def rms_db(x):
    return 20 * np.log10(np.sqrt(np.mean(x ** 2)) + 1e-9)

speech = env > 0.08
print("duration", round(TOTAL, 2), "s")
print("peak after gain", round(float(np.max(np.abs(mix))), 3))
print("VO rms (during speech) dB", round(rms_db(vo[speech] * gain), 1))
print("music rms (during speech) dB", round(rms_db(music_l[speech] * gain), 1))
print("music rms (no speech) dB", round(rms_db(music_l[~speech] * gain), 1))
print("T_MILLION impact at", round(T_MILLION, 2), "s")

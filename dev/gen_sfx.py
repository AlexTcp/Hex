"""Generate the Hex board SFX set as small mono 16-bit WAVs.

Sound design goal: a quiet tournament room — felt thuds, muted ticks,
low wooden creaks, soft chimes. Nothing arcade-y.
"""
import math
import os
import random
import struct
import wave

SR = 22050
OUT = r"E:\Hex\audio"

random.seed(7)


def write_wav(name, samples):
    os.makedirs(OUT, exist_ok=True)
    path = os.path.join(OUT, name + ".wav")
    peak = max(1e-9, max(abs(s) for s in samples))
    if peak > 0.98:
        samples = [s * 0.98 / peak for s in samples]
    with wave.open(path, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(b"".join(struct.pack("<h", int(max(-1, min(1, s)) * 32767)) for s in samples))
    print(f"{name}.wav  {len(samples)/SR*1000:.0f} ms")


def silence(ms):
    return [0.0] * int(SR * ms / 1000)


def env_exp(n, k):
    return [math.exp(-k * i / n) for i in range(n)]


def sine_note(freq, ms, amp=0.5, decay=5.0, harmonics=((1, 1.0),), attack_ms=3):
    n = int(SR * ms / 1000)
    att = max(1, int(SR * attack_ms / 1000))
    out = []
    for i in range(n):
        t = i / SR
        v = sum(a * math.sin(2 * math.pi * freq * h * t) for h, a in harmonics)
        e = math.exp(-decay * i / n) * (min(1.0, i / att))
        out.append(amp * v * e)
    return out


def mix(*tracks):
    n = max(len(t) for t in tracks)
    out = [0.0] * n
    for t in tracks:
        for i, s in enumerate(t):
            out[i] += s
    return out


def delayed(track, ms):
    return silence(ms) + track


# --- select: tiny felt tick -------------------------------------------------
tick = sine_note(1500, 30, amp=0.30, decay=9, harmonics=((1, 1.0), (2.7, 0.25)), attack_ms=1)
write_wav("select", tick)

# --- move: soft felt thud (downward sweep) ----------------------------------
n = int(SR * 0.11)
thud = []
for i in range(n):
    t = i / SR
    f = 170 * math.exp(-6.0 * i / n) + 70
    ph = 2 * math.pi * f * t
    e = math.exp(-7.0 * i / n) * min(1.0, i / (0.002 * SR))
    thud.append(0.62 * math.sin(ph) * e)
noise = [0.08 * (random.random() * 2 - 1) * math.exp(-25.0 * i / n) for i in range(n)]
write_wav("move", mix(thud, noise))

# --- capture: firmer thud + short brass ring --------------------------------
n = int(SR * 0.08)
hit = []
for i in range(n):
    t = i / SR
    f = 150 * math.exp(-5.0 * i / n) + 60
    e = math.exp(-6.0 * i / n) * min(1.0, i / (0.0015 * SR))
    hit.append(0.7 * math.sin(2 * math.pi * f * t) * e)
ring = mix(
    sine_note(1180, 240, amp=0.16, decay=7, harmonics=((1, 1.0),)),
    sine_note(1770, 200, amp=0.10, decay=8, harmonics=((1, 1.0),)),
)
write_wav("capture", mix(hit, delayed(ring, 18)))

# --- coin: two quick tings ---------------------------------------------------
coin = mix(
    sine_note(2093, 70, amp=0.22, decay=6, harmonics=((1, 1.0), (2, 0.15))),
    delayed(sine_note(2637, 110, amp=0.22, decay=6, harmonics=((1, 1.0), (2, 0.12))), 55),
)
write_wav("coin", coin)

# --- crack: dry wooden crackle ----------------------------------------------
n = int(SR * 0.30)
crack = []
burst_at = sorted(random.randint(0, n - 400) for _ in range(9))
for i in range(n):
    v = 0.05 * (random.random() * 2 - 1) * math.exp(-6.0 * i / n)
    crack.append(v)
for b in burst_at:
    ln = random.randint(120, 320)
    amp = random.uniform(0.35, 0.6)
    for j in range(ln):
        if b + j < n:
            crack[b + j] += amp * (random.random() * 2 - 1) * math.exp(-10.0 * j / ln)
# gentle lowpass (moving average) to take the fizz off
crack = [sum(crack[max(0, i - 2):i + 1]) / 3 for i in range(len(crack))]
write_wav("crack", crack)

# --- collapse: low rumble + debris -------------------------------------------
n = int(SR * 0.5)
rumble = []
for i in range(n):
    t = i / SR
    e = math.exp(-4.0 * i / n) * min(1.0, i / (0.01 * SR))
    v = 0.55 * math.sin(2 * math.pi * 52 * t) + 0.3 * math.sin(2 * math.pi * 38 * t + 1.0)
    rumble.append(v * e)
deb = [0.5 * (random.random() * 2 - 1) for i in range(n)]
# heavy lowpass on the noise
for _ in range(3):
    deb = [sum(deb[max(0, i - 7):i + 1]) / 8 for i in range(n)]
deb = [deb[i] * math.exp(-5.0 * i / n) for i in range(n)]
write_wav("collapse", mix(rumble, deb))

# --- win: soft ascending chime -----------------------------------------------
w1 = sine_note(523.25, 320, amp=0.30, decay=4, harmonics=((1, 1.0), (2, 0.2)))
w2 = sine_note(659.25, 320, amp=0.30, decay=4, harmonics=((1, 1.0), (2, 0.2)))
w3 = sine_note(783.99, 460, amp=0.32, decay=3.5, harmonics=((1, 1.0), (2, 0.2)))
write_wav("win", mix(w1, delayed(w2, 110), delayed(w3, 220)))

# --- lose: two-note minor sting ----------------------------------------------
l1 = sine_note(220.0, 320, amp=0.4, decay=3.5, harmonics=((1, 1.0), (2, 0.3), (3, 0.1)))
l2 = sine_note(155.56, 520, amp=0.42, decay=3.0, harmonics=((1, 1.0), (2, 0.3), (3, 0.1)))
write_wav("lose", mix(l1, delayed(l2, 240)))

print("done ->", OUT)

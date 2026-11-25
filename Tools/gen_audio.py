"""Synthesizes Run Across's placeholder audio (stdlib only, no numpy needed).

Run from the repo root:  python3 Tools/gen_audio.py
Writes 16-bit mono WAVs into RunAcross/Assets/_Project/Audio/. Replace any file with a real asset
of the same name and the game picks it up unchanged.
"""
import math
import os
import random
import struct
import wave

RATE = 44100
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "RunAcross", "Assets", "_Project", "Audio")
os.makedirs(OUT, exist_ok=True)
rng = random.Random(7)
TAU = 2 * math.pi


def write(name, samples, peak=0.85):
    m = max(1e-9, max(abs(s) for s in samples))
    k = peak / m
    with wave.open(os.path.join(OUT, name), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(b"".join(struct.pack("<h", int(max(-1, min(1, s * k)) * 32767)) for s in samples))
    print("wrote", name, round(len(samples) / RATE, 2), "s")


def env_adsr(i, n, a=0.005, r=0.1):
    t = i / RATE
    dur = n / RATE
    if t < a:
        return t / a
    if t > dur - r:
        return max(0.0, (dur - t) / r)
    return 1.0


def saw(phase):
    return 2.0 * (phase - math.floor(phase + 0.5))


def note(midi):
    return 440.0 * 2 ** ((midi - 69) / 12)


class LowPass:
    def __init__(self):
        self.y = 0.0

    def __call__(self, x, cutoff):
        a = 1 - math.exp(-TAU * cutoff / RATE)
        self.y += a * (x - self.y)
        return self.y


# ---------------------------------------------------------------- sfx

def shoot():
    n = int(0.22 * RATE)
    out, ph = [], 0.0
    for i in range(n):
        t = i / RATE
        f = 1900 * math.exp(-t * 16) + 220
        ph += f / RATE
        s = 0.6 * math.sin(TAU * ph) + 0.25 * saw(ph * 0.5) + 0.12 * (rng.random() * 2 - 1) * math.exp(-t * 60)
        out.append(s * math.exp(-t * 14))
    return out


def target_hit():
    n = int(0.7 * RATE)
    out = []
    for i in range(n):
        t = i / RATE
        s = 0.0
        for k, (m, d) in enumerate([(88, 0.0), (95, 0.06)]):
            if t >= d:
                tt = t - d
                f = note(m)
                s += math.sin(TAU * f * tt) * math.exp(-tt * 7) + 0.3 * math.sin(TAU * f * 2.01 * tt) * math.exp(-tt * 12)
        s += 0.3 * (rng.random() * 2 - 1) * math.exp(-t * 90)
        out.append(s)
    return out


def gate_open():
    n = int(1.6 * RATE)
    out, lp, ph1, ph2 = [], LowPass(), 0.0, 0.0
    for i in range(n):
        t = i / RATE
        f = 55 + 80 * (t / 1.6)
        ph1 += f / RATE
        ph2 += f * 1.5 / RATE
        noise = rng.random() * 2 - 1
        sweep = lp(noise, 300 + 3500 * math.sin(math.pi * t / 1.6))
        s = 0.55 * saw(ph1) + 0.3 * saw(ph2) + 1.4 * sweep
        out.append(s * env_adsr(i, n, 0.08, 0.6))
    lp2 = LowPass()
    return [lp2(s, 2400) for s in out]


def ui_click():
    n = int(0.09 * RATE)
    return [math.sin(TAU * (1320 if i < n / 2 else 1760) * i / RATE) * math.exp(-i / RATE * 40) for i in range(n)]


def ui_hover():
    n = int(0.05 * RATE)
    return [0.5 * math.sin(TAU * 980 * i / RATE) * math.exp(-i / RATE * 70) for i in range(n)]


def jump():
    n = int(0.18 * RATE)
    out, ph = [], 0.0
    for i in range(n):
        t = i / RATE
        ph += (260 + 900 * t / 0.18) / RATE
        out.append((0.5 * math.sin(TAU * ph) + 0.15 * (rng.random() * 2 - 1)) * math.exp(-t * 18))
    return out


def land():
    n = int(0.16 * RATE)
    lp = LowPass()
    return [lp(rng.random() * 2 - 1, 900) * math.exp(-i / RATE * 30) + 0.4 * math.sin(TAU * 70 * i / RATE) * math.exp(-i / RATE * 35)
            for i in range(n)]


def arpeggio(notes, step, tail, decay):
    n = int((len(notes) * step + tail) * RATE)
    out = [0.0] * n
    for k, m in enumerate(notes):
        start = int(k * step * RATE)
        f = note(m)
        for i in range(start, n):
            tt = (i - start) / RATE
            out[i] += (math.sin(TAU * f * tt) + 0.35 * saw(f * tt)) * math.exp(-tt * decay)
    return out


def respawn():
    n = int(0.6 * RATE)
    out, ph = [], 0.0
    for i in range(n):
        t = i / RATE
        ph += (900 * math.exp(-t * 5) + 90) / RATE
        out.append((0.6 * saw(ph) + 0.3 * math.sin(TAU * ph * 0.5)) * env_adsr(i, n, 0.01, 0.3))
    lp = LowPass()
    return [lp(s, 1800) for s in out]


# ---------------------------------------------------------------- music: slow neon synthwave loop

def music():
    bpm = 92
    beat = 60 / bpm
    bars = 8
    n = int(bars * 4 * beat * RATE)
    out = [0.0] * n
    # Am - F - C - G, two bars each
    chords = [[57, 60, 64], [53, 57, 60], [48, 52, 55], [55, 59, 62]]
    pad_lp, bass_lp, arp_lp = LowPass(), LowPass(), LowPass()
    phases = [0.0] * 6
    bass_ph = 0.0
    arp_ph = 0.0
    for i in range(n):
        t = i / RATE
        b = t / beat
        bar = int(b // 4)
        chord = chords[(bar // 2) % 4]
        # pad: detuned saws, slow filter swell
        pad = 0.0
        for v, m in enumerate(chord):
            for d, det in enumerate((-0.07, 0.07)):
                idx = v * 2 + d
                phases[idx] += note(m + det) / RATE
                pad += saw(phases[idx])
        swell = 700 + 500 * math.sin(TAU * t / (bars * 4 * beat))
        pad = pad_lp(pad * 0.12, swell)
        # bass: root on eighths with pluck envelope
        eighth = (b * 2) % 1.0
        bass_ph += note(chord[0] - 12) / RATE
        bass = bass_lp(saw(bass_ph), 260 + 900 * math.exp(-eighth * 6)) * (0.35 + 0.65 * math.exp(-eighth * 3)) * 0.5
        # arp: chord tones up an octave on sixteenths
        six = int(b * 4)
        m = chord[six % 3] + 12 + (12 if (six // 3) % 2 else 0)
        sx = (b * 4) % 1.0
        arp_ph += note(m) / RATE
        arp = arp_lp(saw(arp_ph) * 0.5 + 0.5 * math.sin(TAU * arp_ph), 2200) * math.exp(-sx * 5) * 0.16
        # soft kick on every beat + hat on offbeats
        bt = (b % 1.0) * beat
        kick = math.sin(TAU * (50 + 90 * math.exp(-bt * 30)) * bt) * math.exp(-bt * 9) * 0.55
        ht = ((b + 0.5) % 1.0) * beat
        hat = (rng.random() * 2 - 1) * math.exp(-ht * 70) * 0.05
        out[i] = pad + bass + arp + kick + hat
    # tiny crossfade so the loop point is seamless
    fade = int(0.02 * RATE)
    for i in range(fade):
        g = i / fade
        out[i] *= g
        out[-1 - i] *= g
    return out


write("sfx_shoot.wav", shoot())
write("sfx_target_hit.wav", target_hit())
write("sfx_gate_open.wav", gate_open())
write("sfx_ui_click.wav", ui_click(), 0.6)
write("sfx_ui_hover.wav", ui_hover(), 0.4)
write("sfx_jump.wav", jump(), 0.5)
write("sfx_land.wav", land(), 0.5)
write("sfx_checkpoint.wav", arpeggio([72, 76, 79], 0.07, 0.5, 6))
write("sfx_complete.wav", arpeggio([69, 72, 76, 81, 84, 88], 0.11, 1.4, 3))
write("sfx_respawn.wav", respawn())
write("music_neon_loop.wav", music(), 0.7)

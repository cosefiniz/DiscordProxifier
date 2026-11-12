# Synthesizes the app's notification sounds (no samples, nothing copyrighted):
#   note.wav  - two soft bell tones going up (C6 -> G6): something happened
#   alert.wav - two softer, lower tones going down (G5 -> D5): something needs you
# Usage: python tools/make-sounds.py DiscordProxifier/Assets/Sounds
import math, os, struct, sys, wave

RATE = 44100


def bell(freq, start, length, amp, out):
    # A bell-like tone: fundamental + octave + a slightly inharmonic partial,
    # 4 ms attack, exponential decay; higher partials die faster.
    partials = [(1.0, 1.0, 0.20), (2.0, 0.28, 0.11), (2.76, 0.07, 0.06)]
    n0 = int(start * RATE)
    for i in range(int(length * RATE)):
        t = i / RATE
        env = min(1.0, t / 0.004)
        v = 0.0
        for mul, a, tau in partials:
            v += a * math.exp(-t / tau) * math.sin(2 * math.pi * freq * mul * t)
        k = n0 + i
        if k < len(out):
            out[k] += amp * env * v


def write(path, tones, total, peak_db):
    out = [0.0] * int(total * RATE)
    for f, s, l, a in tones:
        bell(f, s, l, a, out)
    peak = max(abs(x) for x in out) or 1.0
    gain = (10 ** (peak_db / 20.0)) / peak
    # gentle fade at the very end, no click
    fade = int(0.03 * RATE)
    for i in range(fade):
        out[-1 - i] *= i / fade
    with wave.open(path, 'wb') as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(b''.join(struct.pack('<h', int(max(-1, min(1, x * gain)) * 32767)) for x in out))


def main():
    d = sys.argv[1]
    os.makedirs(d, exist_ok=True)
    write(os.path.join(d, 'note.wav'), [(1046.5, 0.0, 0.5, 1.0), (1568.0, 0.085, 0.55, 0.85)], 0.68, -11)
    write(os.path.join(d, 'alert.wav'), [(784.0, 0.0, 0.6, 1.0), (587.3, 0.12, 0.65, 0.95)], 0.80, -12)
    print('ok')


if __name__ == '__main__':
    main()

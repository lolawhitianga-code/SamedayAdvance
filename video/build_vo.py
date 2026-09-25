"""Generate the voiceover as continuous, naturally-flowing passages.

v1 synthesized 15 separate fragments and glued them together with fixed
gaps. Every fragment restarted its intonation from scratch, mid-sentence
splits ended on a falling "full stop" tone, and the inserted gaps didn't
match natural reading rhythm — which is what made it sound choppy.

v2 synthesizes each scene's narration as ONE passage (each fits in a single
model batch, so prosody runs continuously across clauses). The visuals
still need per-beat sync points and the model doesn't report timings, so
beats are located by aligning the pauses the model actually produced to the
passage's punctuation (see align()). Over-long pauses are then tightened to
natural narration lengths, the way an audio editor would.

Output: vo.wav (48 kHz mono) + timeline.json. Segment ids are unchanged from
v1, so the animation and music builders consume it as before.

VOICE: VO_ENGINE=kokoro (offline, default) uses a British English female
voice — the model has no Australian voice. VO_ENGINE=edge uses Microsoft's
Australian female neural voice (en-AU-NatashaNeural); needs
speech.platform.bing.com allowed by the environment's network policy.
"""
import asyncio, io, json, os, ssl, subprocess
import numpy as np
import soundfile as sf

HERE = os.path.dirname(os.path.abspath(__file__))
SR = 48000
ENGINE = os.environ.get("VO_ENGINE", "kokoro")
SPEED = float(os.environ.get("VO_SPEED", "1.0"))
VOICE = os.environ.get("VO_VOICE", "bf_emma")
LEAD_IN = 0.9   # logo animates in before the first word

# Each passage is read in one go. Segments inside a passage are sync points
# for the visuals, not separate recordings. gap = pause after the passage
# (a natural breath between scenes, where the scene transition happens).
PASSAGES = [
    {"id": "P1", "gap": 0.55, "segs": [
        ("v1", "Same Day Advance, because your next payday shouldn't decide your today."),
    ], "marks": {"tag": ("v1", "Same Day Advance,")}},
    {"id": "P2", "gap": 0.5, "segs": [
        ("v2", "We're a mobile-first wage advance platform, giving everyday Australians fair, fast access to money they've already earned, before payday, and without the fees of a payday loan."),
    ], "marks": {"b1": ("v2", "We're a"),
                 "b2": ("v2", "We're a mobile-first wage advance platform, giving"),
                 "b3": ("v2", "We're a mobile-first wage advance platform, giving everyday Australians fair, fast access to money they've already earned, before payday, and")}},
    {"id": "P3", "gap": 0.5, "segs": [
        ("v3a", "Here's how it works."),
        ("v3b", "Someone applies from their phone,"),
        ("v3c", "and we securely read their real bank transaction data."),
        ("v3d", "Our own risk-scoring engine, built in-house, checks income stability, account conduct, existing debt and spare cash flow,"),
        ("v3e", "and returns a decision in seconds."),
        ("v3f", "Approved funds land the same day."),
    ], "marks": {"f1": ("v3d", "Our own risk-scoring engine, built in-house, checks"),
                 "f2": ("v3d", "Our own risk-scoring engine, built in-house, checks income stability,"),
                 "f3": ("v3d", "Our own risk-scoring engine, built in-house, checks income stability, account conduct,"),
                 "f4": ("v3d", "Our own risk-scoring engine, built in-house, checks income stability, account conduct, existing debt and")}},
    {"id": "P4", "gap": 0.55, "segs": [
        ("v4", "No spreadsheets, no guesswork. Just data-driven lending, done responsibly."),
    ], "marks": {"k2": ("v4", "No spreadsheets,"),
                 "k3": ("v4", "No spreadsheets, no guesswork."),
                 "k4": ("v4", "No spreadsheets, no guesswork. Just data-driven lending,")}},
    {"id": "P5", "gap": 0.6, "segs": [
        ("v5a", "We've modelled three growth scenarios over three years, on the same twenty-one-thousand-dollar starting pool."),
        ("v5b", "Conservative: two hundred and sixty-four thousand dollars profit."),
        ("v5c", "Our base case: six hundred and ninety-nine thousand."),
        ("v5d", "And our high-growth case: nearly one point two million dollars;"),
        ("v5e", "currently capped by capital, not by demand."),
    ], "marks": {"million": ("v5d", "And our high-growth case: nearly one point two")}},
    {"id": "P6", "gap": 0.0, "segs": [
        ("v6", "Same Day Advance. Smarter lending, real returns. Let's talk."),
    ], "marks": {"e1": ("v6", "Same Day Advance."),
                 "e2": ("v6", "Same Day Advance. Smarter lending,"),
                 "e3": ("v6", "Same Day Advance. Smarter lending, real returns.")}},
]
TAIL = 2.2   # end card + music resolve after the last word


# ---------------------------------------------------------------- helpers
def resample(x, sr_in, sr_out):
    if sr_in == sr_out:
        return x.astype(np.float32)
    n_out = int(round(len(x) * sr_out / sr_in))
    return np.interp(np.linspace(0, 1, n_out, endpoint=False),
                     np.linspace(0, 1, len(x), endpoint=False), x).astype(np.float32)


def trim_edges(x, thresh=0.008, pad=0.05, fade=0.01):
    idx = np.where(np.abs(x) > thresh)[0]
    if not len(idx):
        return x
    p = int(pad * SR)
    y = x[max(0, idx[0] - p): min(len(x), idx[-1] + p)].copy()
    nf = int(fade * SR)
    y[:nf] *= np.linspace(0, 1, nf)
    y[-nf:] *= np.linspace(1, 0, nf)
    return y


def frame_rms_db(x, hop=0.01, win=0.025):
    h, w = int(hop * SR), int(win * SR)
    n = max(1, (len(x) - w) // h + 1)
    r = np.array([np.sqrt(np.mean(x[i * h: i * h + w] ** 2) + 1e-12) for i in range(n)])
    return 20 * np.log10(r + 1e-12), hop


def find_pauses(x, min_len=0.07, rel_db=-34.0):
    """Silence runs (relative to the passage's loud speech) of at least min_len."""
    db, hop = frame_rms_db(x)
    ref = np.percentile(db, 90)
    silent = db < ref + rel_db
    pauses, i = [], 0
    while i < len(silent):
        if silent[i]:
            j = i
            while j < len(silent) and silent[j]:
                j += 1
            s, e = i * hop, j * hop
            if e - s >= min_len and s > 0.05 and e < len(x) / SR - 0.05:
                pauses.append((round(s, 3), round(e, 3)))
            i = j
        else:
            i += 1
    return pauses


# ---------------------------------------------------------------- engines
class KokoroEngine:
    def __init__(self):
        from kokoro_onnx import Kokoro
        self.k = Kokoro(os.path.join(HERE, "kokoro-v1.0.int8.onnx"), os.path.join(HERE, "voices-v1.0.bin"))
        self.base = self.k.get_voice_style(VOICE)

    def style_for_passage(self, text):
        """The model picks its style vector by token count. Pin the full
        passage's style so prefix reads (used to locate beats) sound and
        pace the same as the real read."""
        ph = " ".join(self.k.tokenizer.phonemize(text, "en-gb").split())
        n = len(self.k.tokenizer.tokenize(ph))
        row = self.base[min(n, len(self.base)) - 1]
        return np.repeat(row[None], len(self.base), axis=0)

    def say(self, text, style):
        samples, sr = self.k.create(text, voice=style, speed=SPEED, lang="en-gb")
        return trim_edges(resample(np.asarray(samples, dtype=np.float32), sr, SR))


class EdgeEngine:
    def __init__(self):
        import edge_tts.communicate as comm
        comm._SSL_CTX = ssl.create_default_context(cafile="/root/.ccr/ca-bundle.crt")
        import imageio_ffmpeg
        self.ff = imageio_ffmpeg.get_ffmpeg_exe()

    def style_for_passage(self, text):
        return None

    def say(self, text, style):
        import edge_tts
        rate = "%+d%%" % round((SPEED - 1) * 100)

        async def go():
            buf = io.BytesIO()
            async for ch in edge_tts.Communicate(text, "en-AU-NatashaNeural", rate=rate).stream():
                if ch["type"] == "audio":
                    buf.write(ch["data"])
            return buf.getvalue()

        pcm = subprocess.run([self.ff, "-loglevel", "error", "-i", "pipe:0", "-f", "f32le", "-ac", "1",
                              "-ar", str(SR), "pipe:1"], input=asyncio.run(go()), capture_output=True, check=True).stdout
        return trim_edges(np.frombuffer(pcm, dtype=np.float32).copy())


# ---------------------------------------------------------------- alignment
# The model doesn't report timings, and reading a prefix of a passage runs at
# a different pace than the same words inside the full read (the model paces
# the whole sequence), so beats are located from the read's own structure:
# every punctuation mark is a *potential* pause; the pauses the model actually
# produced are matched to them in order (sequence alignment — the model flows
# straight through some commas, and occasionally breaks where there's no
# mark); beats with no pause of their own are placed between matched anchors
# in proportion to the phonemes spoken in between.
_TOK = None


def phonemes_in(word):
    global _TOK
    if _TOK is None:
        from kokoro_onnx import Kokoro
        _TOK = Kokoro(os.path.join(HERE, "kokoro-v1.0.int8.onnx"), os.path.join(HERE, "voices-v1.0.bin")).tokenizer
    ph = " ".join(_TOK.phonemize(word.strip(",.:;!?"), "en-gb").split())
    return max(1, len(_TOK.tokenize(ph)))


def word_units(text):
    """[(word, phoneme_weight, trailing_punct)] for a passage."""
    out = []
    for w in text.split():
        p = w[-1] if w[-1] in ",.:;!?" else ""
        out.append((w, phonemes_in(w), p))
    return out


SKIP_BREAK = {",": 0.3, ";": 0.5, ":": 0.6, ".": 1.4, "!": 1.4, "?": 1.4}
CAP = {",": 0.30, ";": 0.36, ":": 0.40, ".": 0.48, "!": 0.48, "?": 0.48}


def align(units, pauses, dur, iters=4):
    """Match detected pauses to punctuation breaks. Returns (offset->time fn,
    matches {break_index: pause})."""
    offs, acc = [], 0
    for _, w, _ in units:
        acc += w
        offs.append(acc)
    total = acc
    breaks = [(i, offs[i], p) for i, (_, _, p) in enumerate(units) if p and i < len(units) - 1]
    onset, finish = 0.05, dur - 0.05

    anchors = [(0, onset, onset), (total, finish, finish)]

    def f(off):
        for (o1, _, e1), (o2, s2, _) in zip(anchors, anchors[1:]):
            if o1 <= off <= o2:
                return e1 + (s2 - e1) * ((off - o1) / (o2 - o1) if o2 > o1 else 0)
        return finish

    matches = {}
    for _ in range(iters):
        n, m = len(breaks), len(pauses)
        INF = 1e9
        dp = [[INF] * (m + 1) for _ in range(n + 1)]
        bt = [[None] * (m + 1) for _ in range(n + 1)]
        dp[0][0] = 0
        for i in range(n + 1):
            for j in range(m + 1):
                if dp[i][j] >= INF:
                    continue
                if i < n:  # break with no pause (read straight through)
                    c = dp[i][j] + SKIP_BREAK[breaks[i][2]]
                    if c < dp[i + 1][j]:
                        dp[i + 1][j], bt[i + 1][j] = c, ("b", i, j)
                if j < m:  # pause with no punctuation (breath / stop closure)
                    ln = pauses[j][1] - pauses[j][0]
                    c = dp[i][j] + (0.4 if ln < 0.15 else 1.6)
                    if c < dp[i][j + 1]:
                        dp[i][j + 1], bt[i][j + 1] = c, ("p", i, j)
                if i < n and j < m:
                    ps, pe = pauses[j]
                    ln = pe - ps
                    c = abs(f(breaks[i][1]) - (ps + pe) / 2) / 0.4
                    if breaks[i][2] in ".!?" and ln < 0.2:
                        c += 0.8
                    c = dp[i][j] + c
                    if c < dp[i + 1][j + 1]:
                        dp[i + 1][j + 1], bt[i + 1][j + 1] = c, ("m", i, j)
        i, j, matches = n, m, {}
        while (i, j) != (0, 0):
            kind, pi, pj = bt[i][j]
            if kind == "m":
                matches[pi] = pauses[pj]
            i, j = pi, pj
        anchors = [(0, onset, onset)] + sorted(
            (breaks[bi][1], p[0], p[1]) for bi, p in matches.items()) + [(total, finish, finish)]
    return f, matches, breaks, offs


def tighten(audio, pauses_by_type):
    """Shorten over-long pauses to natural narration lengths by removing the
    middle of the silence (keeps each edge's breath/decay intact)."""
    cuts = []
    for (s, e), typ in pauses_by_type:
        excess = (e - s) - CAP[typ]
        if excess > 0.02:
            mid = (s + e) / 2
            cuts.append((mid - excess / 2, mid + excess / 2))
    if not cuts:
        return audio, 0.0
    parts, last, removed = [], 0, 0.0
    xf = int(0.012 * SR)
    for a, b in sorted(cuts):
        ia, ib = int(a * SR), int(b * SR)
        parts.append(audio[last:ia])
        last = ib
        removed += b - a
    parts.append(audio[last:])
    out = parts[0]
    for p in parts[1:]:  # short crossfade across each (silent) cut
        if len(out) > xf and len(p) > xf:
            fade = np.linspace(0, 1, xf, dtype=np.float32)
            out = np.concatenate([out[:-xf], out[-xf:] * (1 - fade) + p[:xf] * fade, p[xf:]])
        else:
            out = np.concatenate([out, p])
    return out, removed


# ---------------------------------------------------------------- build
def main():
    eng = EdgeEngine() if ENGINE == "edge" else KokoroEngine()
    segments, marks, report, clips = [], {}, [], []
    cursor = LEAD_IN

    for P in PASSAGES:
        full_text = " ".join(t for _, t in P["segs"])
        audio = eng.say(full_text, eng.style_for_passage(full_text))
        units = word_units(full_text)

        # pass 1: align, then tighten the pauses that run long for their mark
        pauses = find_pauses(audio, min_len=0.09)
        _, matches, breaks, _ = align(units, pauses, len(audio) / SR)
        before_ms = [int((e - s) * 1000) for s, e in pauses]
        audio, removed = tighten(audio, [(p, breaks[bi][2]) for bi, p in matches.items()])

        # pass 2: re-align on the tightened read (these are the final times)
        dur = len(audio) / SR
        pauses = find_pauses(audio, min_len=0.09)
        f, matches, breaks, offs = align(units, pauses, dur)
        by_word = {breaks[bi][0]: p for bi, p in matches.items()}

        # segment boundaries = the break after each segment's last word
        wi, beats = 0, []
        for k, (sid, text) in enumerate(P["segs"]):
            last = wi + len(text.split()) - 1
            start = 0.05 if k == 0 else seg_next_start
            if k == len(P["segs"]) - 1:
                end = dur - 0.05
            elif last in by_word:
                end, seg_next_start = by_word[last]
            else:
                end = f(offs[last]); seg_next_start = end + 0.02
            segments.append({"id": sid, "text": text, "start": round(cursor + start, 3), "end": round(cursor + end, 3)})
            if k < len(P["segs"]) - 1:
                beats.append((P["segs"][k + 1][0], "pause" if last in by_word else "interpolated", round(end, 2)))
            wi = last + 1

        for mname, (sid, prefix) in P.get("marks", {}).items():
            words_before = 0
            for s_id, t in P["segs"]:
                if s_id == sid:
                    break
                words_before += len(t.split())
            prev = words_before + len(prefix.split()) - 1   # last word of the prefix
            t_word = by_word[prev][1] if prev in by_word else f(offs[prev])
            marks[mname] = round(cursor + t_word, 3)

        report.append({"id": P["id"], "start": round(cursor, 2), "dur": round(dur, 2),
                       "pauses_before_ms": before_ms, "trimmed_s": round(removed, 2),
                       "pauses_ms": [int((e - s) * 1000) for s, e in pauses],
                       "matched": [(units[breaks[bi][0]][0], int((p[1] - p[0]) * 1000)) for bi, p in sorted(matches.items())],
                       "flowed_through": [units[b[0]][0] for k2, b in enumerate(breaks) if k2 not in matches],
                       "beats": beats})
        clips.append((cursor, audio))
        cursor += dur + P["gap"]

    total = cursor + TAIL
    vo = np.zeros(int(total * SR), dtype=np.float32)
    for t0, a in clips:
        s = int(t0 * SR)
        vo[s:s + len(a)] += a
    vo = vo / (np.max(np.abs(vo)) or 1.0) * 0.89

    sf.write(os.path.join(HERE, "vo.wav"), vo, SR)
    json.dump({"total": round(total, 3), "engine": ENGINE, "voice": VOICE if ENGINE == "kokoro" else "en-AU-NatashaNeural",
               "speed": SPEED, "segments": segments, "marks": marks, "passages": report},
              open(os.path.join(HERE, "timeline.json"), "w"), indent=1)

    for r in report:
        print(f'{r["id"]}  start {r["start"]:6.2f}  dur {r["dur"]:5.2f}s  trimmed {r["trimmed_s"]:.2f}s')
        print(f'      pauses before {r["pauses_before_ms"]}  ->  after {r["pauses_ms"]}')
        print(f'      matched to punctuation: {r["matched"]}')
        print(f'      read straight through: {r["flowed_through"]}')
        for sid, how, t in r["beats"]:
            print(f'      beat {sid:4s} at {t:6.2f}s ({how})')
    print("marks", marks)
    print("TOTAL", round(total, 2), "s | spoken ends at", round(cursor, 2), "s | engine", ENGINE)


if __name__ == "__main__":
    main()

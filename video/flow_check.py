"""Objective flow check (I can't listen): compare v1 (fragments) vs v2
(continuous passages) at the points where v1 split mid-sentence.

- Pitch: a sentence-final fall at a mid-sentence split is what makes a read
  sound choppy. Report the last ~400ms pitch relative to the passage median,
  in semitones (strongly negative = "full stop" fall; ~0 or rising = the
  sentence audibly carries on).
- Silence: total silence inside spoken passages, and the gap at each split.
"""
import json
import numpy as np
import soundfile as sf

SR = 48000


def f0_track(x, t0, t1, hop=0.01, win=0.04, fmin=140, fmax=420):
    out = []
    w, h = int(win * SR), int(hop * SR)
    lo, hi = int(SR / fmax), int(SR / fmin)
    for s in range(int(t0 * SR), int(t1 * SR) - w, h):
        fr = x[s:s + w] - np.mean(x[s:s + w])
        if np.sqrt(np.mean(fr ** 2)) < 0.02:
            out.append(np.nan); continue
        ac = np.correlate(fr, fr, "full")[w - 1:]
        ac /= ac[0] + 1e-12
        lag = lo + int(np.argmax(ac[lo:hi]))
        out.append(SR / lag if ac[lag] > 0.45 else np.nan)
    return np.array(out)


def semis(a, ref):
    return 12 * np.log2(a / ref)


def silence_total(x, t0, t1, thresh_db=-40, min_len=0.09):
    seg = x[int(t0 * SR):int(t1 * SR)]
    h = int(0.01 * SR)
    db = np.array([20 * np.log10(np.sqrt(np.mean(seg[i:i + h] ** 2)) + 1e-9) for i in range(0, len(seg) - h, h)])
    ref = np.percentile(db, 90)
    sil = db < ref + thresh_db + 6
    total, run = 0.0, 0
    for v in list(sil) + [False]:
        if v:
            run += 1
        else:
            if run * 0.01 >= min_len:
                total += run * 0.01
            run = 0
    return total


import os
RUNS = [r for r in (("v1 fragments", "vo_v1.wav", "timeline_v1.json"), ("current", "vo.wav", "timeline.json")) if os.path.exists(r[1])]
for label, wav, tl in RUNS:
    x, _ = sf.read(wav, dtype="float32")
    T = {s["id"]: s for s in json.load(open(tl))["segments"]}
    print(f"== {label}")
    for sid, nxt, passage in (("v3b", "v3c", ("v3a", "v3f")), ("v3d", "v3e", ("v3a", "v3f")), ("v5d", "v5e", ("v5a", "v5e"))):
        ps, pe = T[passage[0]]["start"], T[passage[1]]["end"]
        whole = f0_track(x, ps, pe)
        ref = np.nanmedian(whole)
        tail = f0_track(x, T[sid]["end"] - 0.4, T[sid]["end"])
        tail_st = semis(np.nanmedian(tail[-15:]), ref) if np.any(~np.isnan(tail)) else float("nan")
        gap = T[nxt]["start"] - T[sid]["end"]
        print(f"   end of {sid} ('{T[sid]['text'].split()[-1]}'): pitch vs passage median {tail_st:+.1f} st | gap to next {gap*1000:.0f} ms")
    for passage in (("v3a", "v3f"), ("v5a", "v5e")):
        ps, pe = T[passage[0]]["start"], T[passage[1]]["end"]
        st = silence_total(x, ps, pe)
        print(f"   silence inside {passage[0][:2]} passage: {st:.2f}s of {pe-ps:.2f}s ({100*st/(pe-ps):.0f}%)")

"""Generate the voiceover as timed segments and lay them out on a timeline.

Each script line is synthesized separately so the visuals can sync to the
exact moment each beat is spoken (process steps, each scenario number).
Output: vo.wav (48 kHz mono) + timeline.json (start/end seconds per segment).

VOICE: set via env VO_ENGINE. "kokoro" (offline, default) uses a British
English female voice because the model has no Australian voice. "edge" uses
Microsoft's genuine Australian female neural voice (en-AU-NatashaNeural),
which needs speech.platform.bing.com allowed in the environment's network
policy.
"""
import asyncio, io, json, os, ssl, subprocess, sys
import numpy as np
import soundfile as sf

HERE = os.path.dirname(os.path.abspath(__file__))
SR = 48000
ENGINE = os.environ.get("VO_ENGINE", "kokoro")
SPEED = float(os.environ.get("VO_SPEED", "1.0"))

# (id, text, pause after in seconds)
SEGMENTS = [
    ("v1",  "Same Day Advance. Because your next payday shouldn't decide your today.", 0.7),
    ("v2",  "We're a mobile-first wage advance platform, giving everyday Australians fair, fast access to money they've already earned, before payday, without the fees of a payday loan.", 0.6),
    ("v3a", "Here's how it works.", 0.35),
    ("v3b", "Someone applies from their phone.", 0.3),
    ("v3c", "We securely read their real bank transaction data.", 0.3),
    ("v3d", "Our own risk-scoring engine, built in-house, checks income stability, account conduct, existing debt, and spare cash flow,", 0.15),
    ("v3e", "and returns a decision in seconds.", 0.3),
    ("v3f", "Approved funds land the same day.", 0.6),
    ("v4",  "No spreadsheets. No guesswork. Just data-driven lending, done responsibly.", 0.6),
    ("v5a", "We've modelled three growth scenarios over three years, on the same twenty-one-thousand-dollar starting pool.", 0.45),
    ("v5b", "Conservative: two hundred and sixty-four thousand dollars profit.", 0.35),
    ("v5c", "Our base case: six hundred and ninety-nine thousand.", 0.35),
    ("v5d", "And our high-growth case: nearly one point two million dollars,", 0.15),
    ("v5e", "currently capped by capital, not by demand.", 0.8),
    ("v6",  "Same Day Advance. Smarter lending. Real returns. Let's talk.", 0.0),
]
LEAD_IN = 0.9  # silence before the first line, while the logo animates in


def resample(x, sr_in, sr_out):
    if sr_in == sr_out:
        return x
    n_out = int(round(len(x) * sr_out / sr_in))
    t_in = np.linspace(0, 1, len(x), endpoint=False)
    t_out = np.linspace(0, 1, n_out, endpoint=False)
    return np.interp(t_out, t_in, x).astype(np.float32)


def trim(x, thresh=0.01, pad=0.03):
    idx = np.where(np.abs(x) > thresh)[0]
    if not len(idx):
        return x
    p = int(pad * SR)
    return x[max(0, idx[0] - p): min(len(x), idx[-1] + p)]


def synth_kokoro(texts):
    from kokoro_onnx import Kokoro
    k = Kokoro(os.path.join(HERE, "kokoro-v1.0.int8.onnx"), os.path.join(HERE, "voices-v1.0.bin"))
    out = []
    for t in texts:
        samples, sr = k.create(t, voice="bf_emma", speed=SPEED, lang="en-gb")
        out.append(resample(np.asarray(samples, dtype=np.float32), sr, SR))
    return out


def synth_edge(texts):
    import edge_tts
    import edge_tts.communicate as comm
    ctx = ssl.create_default_context(cafile="/root/.ccr/ca-bundle.crt")
    comm._SSL_CTX = ctx
    import imageio_ffmpeg
    ff = imageio_ffmpeg.get_ffmpeg_exe()
    rate = "+%d%%" % round((SPEED - 1) * 100) if SPEED >= 1 else "-%d%%" % round((1 - SPEED) * 100)

    async def one(t):
        c = edge_tts.Communicate(t, "en-AU-NatashaNeural", rate=rate)
        buf = io.BytesIO()
        async for ch in c.stream():
            if ch["type"] == "audio":
                buf.write(ch["data"])
        return buf.getvalue()

    out = []
    for t in texts:
        mp3 = asyncio.run(one(t))
        pcm = subprocess.run([ff, "-loglevel", "error", "-i", "pipe:0", "-f", "f32le", "-ac", "1", "-ar", str(SR), "pipe:1"],
                             input=mp3, capture_output=True, check=True).stdout
        out.append(np.frombuffer(pcm, dtype=np.float32).copy())
    return out


def main():
    texts = [s[1] for s in SEGMENTS]
    clips = synth_edge(texts) if ENGINE == "edge" else synth_kokoro(texts)
    clips = [trim(c) for c in clips]

    timeline, cursor = [], LEAD_IN
    for (sid, text, pause), clip in zip(SEGMENTS, clips):
        dur = len(clip) / SR
        timeline.append({"id": sid, "text": text, "start": round(cursor, 3), "end": round(cursor + dur, 3)})
        cursor += dur + pause
    total = cursor + 2.2  # tail for the end card + music resolve

    vo = np.zeros(int(total * SR), dtype=np.float32)
    for seg, clip in zip(timeline, clips):
        s = int(seg["start"] * SR)
        vo[s:s + len(clip)] += clip
    peak = np.max(np.abs(vo)) or 1.0
    vo = vo / peak * 0.89  # ~ -1 dBFS peak

    sf.write(os.path.join(HERE, "vo.wav"), vo, SR)
    json.dump({"total": round(total, 3), "engine": ENGINE, "segments": timeline},
              open(os.path.join(HERE, "timeline.json"), "w"), indent=1)
    for seg in timeline:
        print(f'{seg["id"]:4s} {seg["start"]:6.2f} -> {seg["end"]:6.2f}  {seg["text"][:60]}')
    print("TOTAL", round(total, 2), "s  engine:", ENGINE)


if __name__ == "__main__":
    main()

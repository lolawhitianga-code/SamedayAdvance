"""Mux the rendered video with the soundtrack, loudness-normalized to -16 LUFS
(two-pass ffmpeg loudnorm — the usual target for web/online video)."""
import json, os, re, subprocess, sys
import imageio_ffmpeg

HERE = os.path.dirname(os.path.abspath(__file__))
FF = imageio_ffmpeg.get_ffmpeg_exe()
out = sys.argv[1] if len(sys.argv) > 1 else os.path.join(HERE, "same-day-advance-investor-film.mp4")
TARGET = "I=-16:TP=-1.5:LRA=11"

# pass 1: measure
p = subprocess.run([FF, "-hide_banner", "-i", os.path.join(HERE, "soundtrack.wav"),
                    "-af", f"loudnorm={TARGET}:print_format=json", "-f", "null", "-"],
                   capture_output=True, text=True)
m = json.loads(re.search(r"\{[^{}]*\"input_i\"[^{}]*\}", p.stderr, re.S).group(0))
print("measured:", {k: m[k] for k in ("input_i", "input_tp", "input_lra")})

# pass 2: apply (linear where possible) + mux
af = (f"loudnorm={TARGET}:measured_I={m['input_i']}:measured_TP={m['input_tp']}:"
      f"measured_LRA={m['input_lra']}:measured_thresh={m['input_thresh']}:offset={m['target_offset']}:linear=true")
subprocess.run([FF, "-y", "-loglevel", "error",
                "-i", os.path.join(HERE, "video_only.mp4"), "-i", os.path.join(HERE, "soundtrack.wav"),
                "-map", "0:v", "-map", "1:a", "-c:v", "copy",
                "-af", af, "-ar", "48000", "-c:a", "aac", "-b:a", "192k",
                "-shortest", "-movflags", "+faststart", out], check=True)

# verify
v = subprocess.run([FF, "-hide_banner", "-i", out, "-af", "ebur128=peak=true", "-f", "null", "-"],
                   capture_output=True, text=True).stderr
integ = re.findall(r"I:\s+(-?[\d.]+) LUFS", v)
peak = re.findall(r"Peak:\s+(-?[\d.]+) dBFS", v)
print("output:", out)
print("integrated loudness:", integ[-1] if integ else "?", "LUFS | true peak:", peak[-1] if peak else "?", "dBFS")
print("size MB:", round(os.path.getsize(out) / 1e6, 1))

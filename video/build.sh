#!/usr/bin/env bash
# Rebuild the investor film end to end: voiceover -> music/SFX mix -> frames -> MP4.
#
#   ./build.sh                 # offline British-English female voice (bf_emma)
#   VO_ENGINE=edge ./build.sh  # genuine Australian female voice (en-AU-NatashaNeural)
#                              # — needs speech.platform.bing.com allowed by the
#                              #   environment's network policy
#
# Scene timing is derived from the voiceover timeline, so swapping the voice
# automatically re-syncs every visual beat.
set -euo pipefail
cd "$(dirname "$0")"

pip install -q edge-tts numpy imageio-ffmpeg kokoro-onnx soundfile

fetch() { [ -s "$2" ] || curl -sSL --fail -o "$2" "$1"; }
fetch https://github.com/thewh1teagle/kokoro-onnx/releases/download/model-files-v1.0/kokoro-v1.0.int8.onnx kokoro-v1.0.int8.onnx
fetch https://github.com/thewh1teagle/kokoro-onnx/releases/download/model-files-v1.0/voices-v1.0.bin voices-v1.0.bin
if [ ! -s InterVariable.ttf ]; then
  fetch https://github.com/rsms/inter/releases/download/v4.1/Inter-4.1.zip inter.zip
  python3 -c "import zipfile;open('InterVariable.ttf','wb').write(zipfile.ZipFile('inter.zip').read('InterVariable.ttf'))"
fi
if [ ! -s JetBrainsMono.ttf ]; then
  fetch https://github.com/JetBrains/JetBrainsMono/releases/download/v2.304/JetBrainsMono-2.304.zip jbmono.zip
  python3 -c "import zipfile;open('JetBrainsMono.ttf','wb').write(zipfile.ZipFile('jbmono.zip').read('fonts/variable/JetBrainsMono[wght].ttf'))"
fi

VO_SPEED="${VO_SPEED:-1.08}" python3 build_vo.py
python3 -c "import json;open('timeline.js','w').write('window.TL = '+json.dumps(json.load(open('timeline.json')))+';')"
python3 build_audio.py
node render.js
python3 mux.py "${1:-same-day-advance-investor-film.mp4}"

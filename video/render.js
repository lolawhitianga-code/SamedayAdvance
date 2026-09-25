// Render scene.html frame-by-frame (deterministic: every frame computed from t),
// pipe JPEG frames straight into ffmpeg, then mux with the soundtrack.
const { chromium } = require('/opt/node22/lib/node_modules/playwright');
const { spawn, execFileSync } = require('child_process');
const path = require('path');
const fs = require('fs');

const FFMPEG = execFileSync('python3', ['-c', 'import imageio_ffmpeg;print(imageio_ffmpeg.get_ffmpeg_exe())']).toString().trim();
const DIR = __dirname;
const FPS = 30;
const OUT_VIDEO = path.join(DIR, 'video_only.mp4');

(async () => {
  const browser = await chromium.launch({ executablePath: '/opt/pw-browsers/chromium' });
  const page = await browser.newPage({ viewport: { width: 1920, height: 1080 } });
  const errors = [];
  page.on('pageerror', (e) => errors.push(e.message));
  await page.goto('file://' + path.join(DIR, 'scene.html'));
  await page.evaluate(() => document.fonts.ready);
  const total = await page.evaluate(() => window.TOTAL);
  const nFrames = Math.ceil(total * FPS);

  const ff = spawn(FFMPEG, [
    '-y', '-loglevel', 'error',
    '-f', 'image2pipe', '-framerate', String(FPS), '-c:v', 'mjpeg', '-i', '-',
    '-c:v', 'libx264', '-preset', 'slow', '-crf', '17', '-pix_fmt', 'yuv420p',
    '-movflags', '+faststart', OUT_VIDEO,
  ], { stdio: ['pipe', 'inherit', 'inherit'] });

  const t0 = Date.now();
  for (let f = 0; f < nFrames; f++) {
    const t = f / FPS;
    await page.evaluate((t) => window.renderAt(t), t);
    const buf = await page.screenshot({ type: 'jpeg', quality: 94 });
    if (!ff.stdin.write(buf)) await new Promise((r) => ff.stdin.once('drain', r));
    if (f % 150 === 0) console.log(`frame ${f}/${nFrames}  t=${t.toFixed(1)}s  elapsed ${((Date.now() - t0) / 1000).toFixed(0)}s`);
  }
  ff.stdin.end();
  await new Promise((r) => ff.on('close', r));
  await browser.close();
  console.log('frames:', nFrames, 'render time:', ((Date.now() - t0) / 1000).toFixed(0) + 's', 'page errors:', errors.length ? errors : 'none');
})();

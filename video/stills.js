const { chromium } = require('/opt/node22/lib/node_modules/playwright');
const path = require('path');
(async () => {
  const times = process.argv.slice(2).map(Number);
  const browser = await chromium.launch({ executablePath: '/opt/pw-browsers/chromium' });
  const page = await browser.newPage({ viewport: { width: 1920, height: 1080 } });
  const errors = [];
  page.on('pageerror', (e) => errors.push(e.message));
  page.on('console', (m) => { if (m.type() === 'error') errors.push(m.text()); });
  await page.goto('file://' + path.resolve(__dirname, 'scene.html'));
  await page.evaluate(() => document.fonts.ready);
  for (const t of times) {
    await page.evaluate((t) => window.renderAt(t), t);
    await page.screenshot({ path: path.resolve(__dirname, `still_${t.toFixed(1)}.jpg`), type: 'jpeg', quality: 85 });
  }
  console.log('fonts:', await page.evaluate(() => [...document.fonts].map(f => f.family + ':' + f.status).join(', ')));
  console.log('errors:', errors.length ? errors : 'none');
  await browser.close();
})();

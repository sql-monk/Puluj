// Exports docs/diagrams/*.drawio to PNG through the draw.io embed page in headless Chromium (no desktop app needed).
// Usage: node docs/diagrams/export.mjs   (requires `playwright` with chromium; see docs/diagrams/README.md)
import { chromium } from 'playwright';
import { readdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const dir = dirname(fileURLToPath(import.meta.url));
const files = readdirSync(dir).filter((f) => f.endsWith('.drawio'));
const browser = await chromium.launch();
const page = await browser.newPage({ viewport: { width: 1600, height: 1000 } });
await page.goto('https://embed.diagrams.net/?embed=1&proto=json&spin=1&ui=min', { waitUntil: 'networkidle' });

for (const file of files) {
  const xml = readFileSync(join(dir, file), 'utf8');
  const png = await page.evaluate(
    (xml) =>
      new Promise((resolve, reject) => {
        const timer = setTimeout(() => reject(new Error('export timeout')), 60000);
        const onMessage = (e) => {
          let msg;
          try { msg = JSON.parse(e.data); } catch { return; }
          if (msg.event === 'load') {
            window.postMessage(JSON.stringify({ action: 'export', format: 'png', scale: 2, border: 20, transparent: false }), '*');
          } else if (msg.event === 'export') {
            clearTimeout(timer);
            window.removeEventListener('message', onMessage);
            resolve(msg.data);
          }
        };
        window.addEventListener('message', onMessage);
        window.postMessage(JSON.stringify({ action: 'load', xml, autosave: 0 }), '*');
      }),
    xml,
  );
  const out = join(dir, file.replace(/\.drawio$/, '.png'));
  writeFileSync(out, Buffer.from(png.split(',')[1], 'base64'));
  console.log('exported', out);
}
await browser.close();

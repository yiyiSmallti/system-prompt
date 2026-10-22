// Serve experiments/toon-3d over localhost, open threejs/index.html in headless Chromium (SwiftShader
// WebGL, no GPU needed) and save one PNG per turnaround view.
//
// usage: node render.mjs [--out ../out] [--glb ../out/chatgpt_uniform.glb] [--chrome /path/to/chrome]
//        node render.mjs --mode reference --glb <VRM1_Constraint_Twist_Sample.vrm>   (VRoid control model)
import { createServer } from 'node:http';
import { mkdir, readFile, writeFile } from 'node:fs/promises';
import { existsSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { chromium } from 'playwright-core';

const HERE = path.dirname(fileURLToPath(import.meta.url));
const ROOT = path.resolve(HERE, '..');
const arg = (name, fallback) => {
  const i = process.argv.indexOf(`--${name}`);
  return i > 0 ? process.argv[i + 1] : fallback;
};
const OUT = path.resolve(arg('out', path.join(ROOT, 'out')));
const MODE = arg('mode', 'chatgpt');
const GLB = path.resolve(arg('glb', path.join(OUT, 'chatgpt_uniform.glb')));
const CHROME = arg('chrome', process.env.CHROME_PATH || '/opt/pw-browsers/chromium-1194/chrome-linux/chrome');
const TYPES = { '.html': 'text/html', '.js': 'text/javascript', '.mjs': 'text/javascript', '.glb': 'model/gltf-binary', '.png': 'image/png' };

// static file server rooted at experiments/toon-3d; the glb may live elsewhere, so it gets its own route
const server = createServer(async (req, res) => {
  const url = decodeURIComponent(new URL(req.url, 'http://x').pathname);
  const file = url === '/model.glb' ? GLB : path.join(ROOT, url);
  if (!file.startsWith(ROOT) && file !== GLB) return res.writeHead(403).end();
  try {
    const body = await readFile(file);
    res.writeHead(200, { 'Content-Type': TYPES[path.extname(file)] || 'application/octet-stream' }).end(body);
  } catch {
    res.writeHead(404).end();
  }
});
await new Promise((r) => server.listen(0, '127.0.0.1', r));
const port = server.address().port;

if (!existsSync(CHROME)) throw new Error(`Chromium not found at ${CHROME}; pass --chrome or set CHROME_PATH`);
const browser = await chromium.launch({
  executablePath: CHROME,
  args: ['--use-angle=swiftshader', '--enable-unsafe-swiftshader', '--ignore-gpu-blocklist'],
});
const page = await browser.newPage({ viewport: { width: 1000, height: 1000 } });
page.on('console', (m) => console.log('[page]', m.text()));
page.on('pageerror', (e) => console.error('[page error]', e.message));
await page.goto(`http://127.0.0.1:${port}/threejs/index.html?glb=/model.glb&mode=${MODE}`);
const ok = await page.evaluate(() => window.sceneReady);
if (ok !== true) throw new Error(`scene failed to load: ${ok}`);

const prefix = MODE === 'chatgpt' ? '' : `${MODE}_`;
const dir = path.join(OUT, 'renders', `${prefix}threejs`);
await mkdir(dir, { recursive: true });
const times = {};
for (const view of await page.evaluate(() => window.views())) {
  const t = Date.now();
  const url = await page.evaluate((v) => window.renderView(v), view);
  times[view] = (Date.now() - t) / 1000;
  await writeFile(path.join(dir, `${view}.png`), Buffer.from(url.split(',')[1], 'base64'));
  console.log(`rendered ${view} in ${times[view]} s`);
}
const info = { ...(await page.evaluate(() => window.glInfo())), chromium: browser.version(), render_seconds: times };
await writeFile(path.join(OUT, `${prefix}threejs_stats.json`), JSON.stringify(info, null, 1));
console.log(info);
await browser.close();
server.close();

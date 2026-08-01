const fs = require('node:fs');
const path = require('node:path');

const root = path.resolve(__dirname, '..');
const manifest = JSON.parse(fs.readFileSync(path.join(root, 'manifest.json'), 'utf8'));
const outDir = path.join(root, 'dist', `${manifest.Name.replace(/[^a-z0-9_-]+/gi, '-').toLowerCase()}.sdPlugin`);
const include = [
  'manifest.json',
  'property-inspector.html',
  'property-inspector.js',
  'property-inspector.css',
  'icons'
];

function copyRecursive(source, target) {
  const stat = fs.statSync(source);
  if (stat.isDirectory()) {
    fs.mkdirSync(target, { recursive: true });
    for (const entry of fs.readdirSync(source)) {
      copyRecursive(path.join(source, entry), path.join(target, entry));
    }
    return;
  }
  fs.mkdirSync(path.dirname(target), { recursive: true });
  fs.copyFileSync(source, target);
}

fs.rmSync(outDir, { recursive: true, force: true });
for (const item of include) {
  copyRecursive(path.join(root, item), path.join(outDir, item));
}
copyRecursive(path.join(root, 'dist', 'plugin'), path.join(outDir, 'plugin'));

// The property inspector runs in the Stream Dock host's own embedded webview, which can
// cache property-inspector.js/.css by URL across plugin updates. Append a version query
// string so every release forces a fresh fetch instead of silently reusing stale JS/CSS.
const htmlPath = path.join(outDir, 'property-inspector.html');
const html = fs.readFileSync(htmlPath, 'utf8')
  .replace('property-inspector.css"', `property-inspector.css?v=${manifest.Version}"`)
  .replace('property-inspector.js"', `property-inspector.js?v=${manifest.Version}"`);
fs.writeFileSync(htmlPath, html);

console.log(outDir);

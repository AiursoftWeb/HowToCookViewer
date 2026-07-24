import fs from 'fs';
import path from 'path';

const pkg = JSON.parse(fs.readFileSync('package.json', 'utf8'));
const keep = pkg.keep;

if (!keep || typeof keep !== 'object' || Object.keys(keep).length === 0) {
  console.error('ERROR: package.json is missing a non-empty "keep" section.');
  console.error('       Add "keep": { "pkg-name": "subdir" } to declare which files to keep from node_modules.');
  process.exit(1);
}

// Stage kept files into a temp directory
const staging = '.node_modules-keep';
fs.rmSync(staging, { recursive: true, force: true });

let count = 0;
for (const [pkgName, subdir] of Object.entries(keep)) {
  const src = path.resolve('node_modules', pkgName, subdir);
  const dest = path.join(staging, pkgName, subdir);

  if (!fs.existsSync(src)) {
    console.error(`ERROR: Source not found: ${src}`);
    console.error('       Did you run "npm install" first?');
    fs.rmSync(staging, { recursive: true, force: true });
    process.exit(1);
  }

  fs.mkdirSync(path.dirname(dest), { recursive: true });
  fs.cpSync(src, dest, { recursive: true });

  const files = fs.statSync(src).isDirectory()
    ? fs.readdirSync(src, { recursive: true }).filter(f => fs.statSync(path.join(src, f)).isFile()).length
    : 1;

  console.log(`  keep: ${pkgName}/${subdir}  (${files} files)`);
  count += files;
}

// Replace node_modules with the slim version
fs.rmSync('node_modules', { recursive: true, force: true });
fs.renameSync(staging, 'node_modules');

console.log(`Build complete — node_modules slimmed to ${count} file(s).`);

import fs from 'node:fs';
const asar = process.argv[2];
const want = process.argv[3] || '';
const extractTo = process.argv[4] || '';
const prefix = process.argv[5] || '';
const fd = fs.openSync(asar, 'r');
const head = Buffer.alloc(16);
fs.readSync(fd, head, 0, 16, 0);
const headerSize = head.readUInt32LE(4);
const pickleSize = head.readUInt32LE(8);
const jsonSize = head.readUInt32LE(12);
const buf = Buffer.alloc(pickleSize);
fs.readSync(fd, buf, 0, pickleSize, 16);
const header = JSON.parse(buf.toString('utf8', 0, jsonSize));
const base = 8 + headerSize;
function* walk(node, prefixPath) {
  for (const [name, entry] of Object.entries(node.files || {})) {
    const p = prefixPath + '/' + name;
    if (entry.files) yield* walk(entry, p);
    else yield { path: p, entry };
  }
}
let n = 0, bytes = 0;
for (const f of walk(header, '')) {
  if (extractTo && prefix ? !f.path.startsWith(prefix) : (want && !f.path.includes(want))) continue;
  if (!extractTo) { console.log(f.path); n++; continue; }
  const start = base + Number(f.entry.offset);
  const b = Buffer.alloc(f.entry.size);
  fs.readSync(fd, b, 0, f.entry.size, start);
  const out = extractTo + f.path;
  fs.mkdirSync(out.slice(0, out.lastIndexOf('/')), { recursive: true });
  fs.writeFileSync(out, b);
  n++; bytes += f.entry.size;
}
console.log('--- ' + n + ' file(s), ' + bytes + ' bytes');
fs.closeSync(fd);

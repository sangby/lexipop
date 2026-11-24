import fs from 'node:fs';

const url = process.argv[2];
const out = process.argv[3];
const retries = Number(process.argv[4] ?? 6);

if (!url || !out) {
  console.error('usage: node download-retry.mjs <url> <out> [retries]');
  process.exit(2);
}

for (let attempt = 1; attempt <= retries; attempt++) {
  try {
    console.log(`attempt ${attempt}/${retries} ...`);
    const res = await fetch(url, { redirect: 'follow' });
    if (!res.ok) throw new Error('HTTP ' + res.status + ' ' + res.statusText);

    const buf = Buffer.from(await res.arrayBuffer());
    if (buf.length < 1024) throw new Error('too small: ' + buf.length);

    fs.writeFileSync(out, buf);
    console.log('saved', out, buf.length, 'bytes');
    process.exit(0);
  } catch (e) {
    console.error('failed:', e.message);
    if (attempt < retries) {
      await new Promise((r) => setTimeout(r, 4000));
    }
  }
}

console.error('all attempts failed');
process.exit(1);

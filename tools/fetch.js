const fs = require('fs');

const url = process.argv[2];
const out = process.argv[3];

if (!url || !out) {
  console.error('usage: node fetch.js <url> <outfile>');
  process.exit(2);
}

(async () => {
  const res = await fetch(url, { redirect: 'follow' });
  if (!res.ok) {
    console.error('HTTP', res.status, res.statusText);
    process.exit(1);
  }
  const buf = Buffer.from(await res.arrayBuffer());
  fs.writeFileSync(out, buf);
  console.log('saved', out, buf.length, 'bytes');
})().catch((e) => {
  console.error('ERR', e.message);
  process.exit(1);
});

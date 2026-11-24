import fs from 'node:fs';

const url = process.argv[2];
const out = process.argv[3];
const chunkSize = Number(process.argv[4] ?? 2 * 1024 * 1024);
const attemptsPerChunk = 30;

if (!url || !out) {
  console.error('usage: node download-chunked.mjs <url> <out> [chunkSize]');
  process.exit(2);
}

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

// 1) 获取总长度
let total = 0;
for (let i = 0; i < 10 && total === 0; i++) {
  try {
    const head = await fetch(url, { method: 'HEAD', redirect: 'follow' });
    total = Number(head.headers.get('content-length') ?? 0);
    console.log('size =', total);
  } catch (e) {
    console.error('head failed:', e.message);
    await sleep(2000);
  }
}

if (total <= 0) {
  console.error('cannot determine size');
  process.exit(1);
}

// 2) 分块下载
const fd = fs.openSync(out, 'w');
let offset = 0;
let chunks = 0;

try {
  while (offset < total) {
    const end = Math.min(offset + chunkSize - 1, total - 1);
    let ok = false;

    for (let attempt = 1; attempt <= attemptsPerChunk && !ok; attempt++) {
      try {
        const res = await fetch(url, {
          headers: { range: `bytes=${offset}-${end}` },
          redirect: 'follow',
        });
        if (!res.ok) throw new Error('HTTP ' + res.status);
        const buf = Buffer.from(await res.arrayBuffer());
        if (buf.length !== end - offset + 1) throw new Error('short read ' + buf.length);
        fs.writeSync(fd, buf, 0, buf.length, offset);
        ok = true;
      } catch (e) {
        if (attempt === attemptsPerChunk) throw e;
        await sleep(1500);
      }
    }

    offset = end + 1;
    chunks++;
    if (chunks % 5 === 0 || offset === total) {
      const pct = ((offset / total) * 100).toFixed(1);
      console.log(`progress ${pct}% (${offset}/${total})`);
    }
  }

  fs.closeSync(fd);
  console.log('done', out, total, 'bytes');
} catch (e) {
  fs.closeSync(fd);
  console.error('chunk failed at offset', offset, e.message);
  process.exit(1);
}

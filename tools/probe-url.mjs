const url = process.argv[2];
if (!url) {
  console.error('usage: node probe-url.mjs <url>');
  process.exit(2);
}

try {
  const res = await fetch(url, { method: 'HEAD', redirect: 'follow' });
  console.log(url, '->', res.status, res.statusText, 'len=' + (res.headers.get('content-length') ?? '?'));
} catch (e) {
  console.log(url, '-> ERR', e.message);
}

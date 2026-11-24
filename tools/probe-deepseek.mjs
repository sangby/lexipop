const targets = [
  'https://api.deepseek.com/v1/tools/search',
  'https://api.deepseek.com/v1/tools/fetch',
  'https://api-docs.deepseek.com/quick_start/pricing',
];

for (const url of targets) {
  try {
    const res = await fetch(url, {
      method: 'POST',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify({}),
    });
    console.log(url, '->', res.status, res.statusText);
  } catch (e) {
    console.log(url, '-> ERR', e.message);
  }
}

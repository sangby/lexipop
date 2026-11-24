const UA = 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Safari/537.36';

const urls = [
  'https://raw.gitcode.com/gh_mirrors/ec/ECDICT/master/stardict.csv',
  'https://gitcode.com/gh_mirrors/ec/ECDICT/raw/master/stardict.csv?raw=true',
  'https://gitee.com/mqycn/ECDICT/raw/master/stardict.csv',
  'https://gitee.com/erms/ECDICT/raw/master/stardict.csv',
];

for (const url of urls) {
  try {
    const res = await fetch(url, {
      headers: { 'user-agent': UA, 'range': 'bytes=0-255' },
      redirect: 'follow',
    });
    const text = await res.text();
    const head = text.slice(0, 80).replace(/\s+/g, ' ');
    console.log(url);
    console.log('  ->', res.status, 'len=' + text.length, 'head=' + JSON.stringify(head));
  } catch (e) {
    console.log(url, '-> ERR', e.message);
  }
}

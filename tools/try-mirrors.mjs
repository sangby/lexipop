const UA = 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Safari/537.36';
const RAW = 'https://raw.githubusercontent.com/skywind3000/ECDICT/master/stardict.csv';

const mirrors = [
  'https://ghproxy.net/',
  'https://gh-proxy.com/',
  'https://github.moeyy.xyz/',
  'https://mirror.ghproxy.com/',
  'https://ghfast.top/',
  'https://gh.llkk.cc/',
];

for (const base of mirrors) {
  const url = base + RAW;
  try {
    const res = await fetch(url, {
      headers: { 'user-agent': UA, 'range': 'bytes=0-255' },
      redirect: 'follow',
    });
    const text = await res.text();
    const head = text.slice(0, 80).replace(/\s+/g, ' ');
    console.log(base, '->', res.status, 'len=' + text.length, 'head=' + JSON.stringify(head));
  } catch (e) {
    console.log(base, '-> ERR', e.message);
  }
}

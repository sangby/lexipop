import fs from 'node:fs';
import path from 'node:path';

const settingsPath = path.join(process.env.LOCALAPPDATA, 'LexiPop', 'settings.json');
const raw = fs.readFileSync(settingsPath, 'utf8').replace(/^\uFEFF/, '');
const settings = JSON.parse(raw);
const key = process.env.LEXIPOP_API_KEY ||
  (settings.ProviderId === 'deepseek' ? process.env.LEXIPOP_DEEPSEEK_KEY : '') ||
  settings.ApiKey || settings.DeepSeekApiKey;

if (!key) {
  console.error('no api key in settings.json');
  process.exit(1);
}

const res = await fetch(settings.Endpoint || settings.DeepSeekEndpoint, {
  method: 'POST',
  headers: {
    'content-type': 'application/json',
    authorization: 'Bearer ' + key,
  },
  body: JSON.stringify({
    model: settings.Model || settings.DeepSeekModel,
    messages: [
      { role: 'system', content: '你是翻译引擎。只输出译文，不要解释。' },
      { role: 'user', content: 'The quick brown fox jumps over the lazy dog.' },
    ],
    stream: false,
  }),
});

console.log('status', res.status);
const data = await res.json();

if (!res.ok) {
  console.log(JSON.stringify(data).slice(0, 500));
  process.exit(0);
}

const message = data.choices?.[0]?.message;
console.log('keys:', Object.keys(message ?? {}));
console.log('content:', message?.content);
console.log('reasoning_content:', (message?.reasoning_content ?? '').slice(0, 200));
console.log('usage:', JSON.stringify(data.usage));

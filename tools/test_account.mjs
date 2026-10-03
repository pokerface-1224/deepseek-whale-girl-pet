import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';

const exe = process.argv[2];
assert.ok(exe, 'Run tools/test_account.ps1 to compile the native fixture');
const names = ['鲸鱼娘测试', '张三"小鲸"\\目录', '海豚🐳 café', '已登录用户', '中文\\u0061'];
const child = spawn(exe, [], { windowsHide: true, stdio: ['pipe', 'pipe', 'pipe'] });
let stdout = '', stderr = '';
child.stdout.setEncoding('utf8');
child.stderr.setEncoding('utf8');
child.stdout.on('data', s => { stdout += s; });
child.stderr.on('data', s => { stderr += s; });
const completion = new Promise((resolve, reject) => {
  child.on('error', reject);
  child.on('close', code => code === 0 ? resolve() : reject(new Error(stderr || `Native fixture exited ${code}`)));
});
const timer = setTimeout(() => child.kill(), 30000);
try {
  for (const [index, name] of names.entries()) {
    let json = JSON.stringify({ authenticated: true, user: { name }, balance: { normal: '10.00', bonus: 2.5, total: '12.50', currency: 'CNY' } }, null, index === 1 ? 1 : undefined);
    // Protocol frames are single lines; still exercise legal JSON whitespace.
    json = json.replace(/\n/g, ' ');
    if (index === 2) json = json.replace(/[\u0080-\uffff]/g, c => '\\u' + c.charCodeAt(0).toString(16).padStart(4, '0'));
    const bytes = Buffer.from(`account:info:${json}\n`, 'utf8');
    // UTF-8 characters may be split across pipe writes.
    for (let i = 0; i < bytes.length; i += 2) child.stdin.write(bytes.subarray(i, i + 2));
  }
  child.stdin.end();
  await completion;
  assert.match(stdout, /PASS:/);
  console.log(stdout.trim());
} finally { clearTimeout(timer); }

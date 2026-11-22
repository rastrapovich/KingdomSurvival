// Матрица покрытия боевых состояний и тегов (этап 0 брифа завершения 22.11.2026).
// Источник исполнимости — код каталогов (не тексты JSON):
//   Assets/_Project/BattleSandbox/Runtime/SandboxStatusCatalog.cs  — Pending(...) = не исполняется;
//   Assets/_Project/BattleSandbox/Runtime/SandboxPassiveCatalog.cs — T("id", ...) = исполняемое правило;
//   SandboxPassiveCatalog.cs: Deferred("id", "поле", "причина") = в каталоге, но ждёт названной capability.
// Тесты — упоминание ID в файлах Tests/EditMode. Запуск: node build_coverage.js (из корня проекта).
const fs = require('fs');
const path = require('path');
const root = process.cwd();
const rd = p => fs.readFileSync(path.join(root, p), 'utf8');
const data = 'ProjectDocs/CombatStatusPack/Data/';
const statuses = JSON.parse(rd(data + 'combat_statuses.json')).rows;
const bank = JSON.parse(rd(data + 'passive_tags_effective.json')).records;
const sel = JSON.parse(rd(data + 'selection_manifest.json'));
const statusSrc = rd('Assets/_Project/BattleSandbox/Runtime/SandboxStatusCatalog.cs');
const passiveSrc = fs.readdirSync(path.join(root, 'Assets/_Project/BattleSandbox/Runtime'))
  .filter(f => f.startsWith('SandboxPassiveCatalog') && f.endsWith('.cs'))
  .map(f => rd('Assets/_Project/BattleSandbox/Runtime/' + f)).join('\n');
const testDirs = ['Assets/_Project/BattleSandbox/Tests/EditMode/', 'Assets/_Project/UnitDatabase/Tests/EditMode/'];
// Тест упоминает ID напрямую или через константу-префикс (Q + "005").
const tests = testDirs.flatMap(dir => fs.readdirSync(path.join(root, dir)).filter(f => f.endsWith('.cs')).map(f => {
  let src = rd(dir + f);
  const pre = {};
  for (const m of src.matchAll(/const string (\w+) = "([^"]+)";/g)) pre[m[1]] = m[2];
  for (const m of src.matchAll(/\b(\w+) \+ "([^"]+)"/g)) if (pre[m[1]]) src += '\n"' + pre[m[1]] + m[2] + '"';
  return [f, src];
}));
const dbAsset = rd('Assets/_Project/UnitDatabase/Resources/UnitDatabase/KingdomSurvivalUnits.asset');

const prefixes = {};
for (const m of passiveSrc.matchAll(/const string (\w+) = "([^"]+)";/g)) prefixes[m[1]] = m[2];
const executable = new Set([...passiveSrc.matchAll(/\bT\("([^"]+)"/g)].map(m => m[1])
  .concat([...passiveSrc.matchAll(/\bT\((\w+) \+ "([^"]+)"/g)].map(m => (prefixes[m[1]] || '') + m[2])));
// Карта обработчиков: виды правил каждого тега (из строк T(...).R(K.Вид ...)).
const handlers = {};
for (const line of passiveSrc.split('\n')) {
  const m = line.match(/\bT\((?:"([^"]+)"|(\w+) \+ "([^"]+)")/);
  if (!m) continue;
  const id = m[1] || ((prefixes[m[2]] || '') + m[3]);
  const kinds = [...line.matchAll(/\.R\(K\.(\w+)/g)].map(k => k[1])
    .concat([...line.matchAll(/\.React\(E\.(\w+)/g)].map(k => 'StatusReaction:' + k[1]))
    .concat([...line.matchAll(/\.Heal\(K\.(\w+)/g)].map(k => k[1]))
    .concat(line.includes('.Alias(') ? ['Alias'] : []);
  handlers[id] = (handlers[id] || []).concat(kinds);
}
const deferred = {};
for (const m of passiveSrc.matchAll(/\bDeferred\("([^"]+)",\s*"([^"]*)",\s*"([^"]*)"/g)) deferred[m[1]] = { field: m[2], reason: m[3] };
const pendingStatus = {};
for (const m of statusSrc.matchAll(/Def\("(status\.\d+)"[\s\S]*?(?=Def\("status|\/\/ ----------|$)/g)) {
  const pm = m[0].match(/\.Pending\("([^"]+)"\)/);
  if (pm) pendingStatus[m[1]] = pm[1];
}
const testedBy = id => tests.filter(([, src]) => src.includes('"' + id + '"')).map(([f]) => f.replace('.cs', '')).join(' ');
const assignedTo = id => {
  const units = [];
  let unit = null;
  for (const line of dbAsset.split('\n')) {
    const u = line.match(/^  - id: (\S+)/);
    if (u) unit = u[1];
    if (line.trim() === '- ' + id && unit) units.push(unit);
  }
  return units.filter(u => u !== id).join(' ');
};

const rows = [];
for (const s of statuses) {
  const pending = pendingStatus[s.id];
  rows.push({ kind: 'status', id: s.id, name: s.name, star: s.favorite ? '★' : '', executable: pending ? 'нет' : 'да',
    block: pending || '', tests: testedBy(s.id), db: 'каталог состояний' });
}
const fav = new Set(sel.selectedActiveCombatIds);
for (const id of sel.activeCombatIds) {
  const r = bank.find(x => x.id === id);
  const d = deferred[id];
  rows.push({ kind: 'tag', id, name: r ? r.name : '?', star: fav.has(id) ? '★' : '',
    executable: executable.has(id) ? 'да' : d ? 'ждёт capability' : 'нет',
    block: d ? d.field + ': ' + d.reason : executable.has(id) ? '' : 'нет исполнителя',
    tests: testedBy(id), db: assignedTo(id), handler: (handlers[id] || []).join(' ') });
}
// Статусы брифа (этап 17): Imported / Executable / Configurable / ReachableFromGame /
// VerifiedEditMode / VerifiedPlayMode / VerifiedManual. Configurable — тег назначается
// из каталога Базы существ; Reachable — адаптер переносит tagIds бойца в бой.
// PlayMode и ручная проверка по отдельному тегу не автоматизированы — «нет».
for (const r of rows) {
  r.configurable = r.executable === 'да' ? 'да' : 'нет';
  r.reachable = r.executable === 'да' ? 'да' : 'нет';
  // «механизм» — свой тест на правило; «дымовой» — только общий прогон боя
  // каждого тега каталога (PassiveTagSmokeTests), без проверки самого эффекта.
  r.editmode = r.tests ? 'механизм' : r.executable === 'да' ? 'дымовой' : 'нет';
}
const csv = ['kind;id;name;star;executable;block;tests;assigned;handler;imported;configurable;reachable;verifiedEditMode;verifiedPlayMode;verifiedManual']
  .concat(rows.map(r => [r.kind, r.id, r.name, r.star, r.executable, r.block, r.tests, r.db, r.handler || '', 'да', r.configurable, r.reachable, r.editmode, 'нет', 'нет']
    .map(v => String(v).replace(/;/g, ',')).join(';')))
  .join('\n');
// Повтор записи: Windows иногда кратко держит только что записанный файл.
for (let attempt = 0; ; attempt++) {
  try { fs.writeFileSync(path.join(root, data + 'coverage_matrix.csv'), csv); break; }
  catch (e) { if (attempt >= 20) throw e; const until = Date.now() + 150; while (Date.now() < until) { } }
}
const sum = k => rows.filter(r => r.kind === k);
const st = sum('status'), tg = sum('tag');
console.log(JSON.stringify({
  statuses: st.length, statusesExecutable: st.filter(r => r.executable === 'да').length,
  tags: tg.length, tagsExecutable: tg.filter(r => r.executable === 'да').length,
  tagsDeferred: tg.filter(r => r.executable === 'ждёт capability').length,
  tagsMissing: tg.filter(r => r.executable === 'нет').length,
  tagsTested: tg.filter(r => r.tests).length,
  statusesTested: st.filter(r => r.tests).length,
}));
if (process.argv.includes('--untested')) console.log(tg.filter(r => !r.tests).map(r => r.id + ' | ' + r.name + ' | ' + (r.handler || '')).join('\n'));
if (process.argv.includes('--missing')) console.log(tg.filter(r => r.executable !== 'да').map(r => r.id + ' | ' + r.name + (r.block ? ' | ' + r.block : '')).join('\n'));

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
const testDir = 'Assets/_Project/BattleSandbox/Tests/EditMode/';
const tests = fs.readdirSync(path.join(root, testDir)).filter(f => f.endsWith('.cs')).map(f => [f, rd(testDir + f)]);
const dbAsset = rd('Assets/_Project/UnitDatabase/Resources/UnitDatabase/KingdomSurvivalUnits.asset');

const prefixes = {};
for (const m of passiveSrc.matchAll(/const string (\w+) = "([^"]+)";/g)) prefixes[m[1]] = m[2];
const executable = new Set([...passiveSrc.matchAll(/\bT\("([^"]+)"/g)].map(m => m[1])
  .concat([...passiveSrc.matchAll(/\bT\((\w+) \+ "([^"]+)"/g)].map(m => (prefixes[m[1]] || '') + m[2])));
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
    tests: testedBy(id), db: assignedTo(id) });
}
const csv = ['kind;id;name;star;executable;block;tests;assigned']
  .concat(rows.map(r => [r.kind, r.id, r.name, r.star, r.executable, r.block, r.tests, r.db].map(v => String(v).replace(/;/g, ',')).join(';')))
  .join('\n');
fs.writeFileSync(path.join(root, data + 'coverage_matrix.csv'), csv);
const sum = k => rows.filter(r => r.kind === k);
const st = sum('status'), tg = sum('tag');
console.log(JSON.stringify({
  statuses: st.length, statusesExecutable: st.filter(r => r.executable === 'да').length,
  tags: tg.length, tagsExecutable: tg.filter(r => r.executable === 'да').length,
  tagsDeferred: tg.filter(r => r.executable === 'ждёт capability').length,
  tagsMissing: tg.filter(r => r.executable === 'нет').length,
  tagsTested: tg.filter(r => r.tests).length,
}));
if (process.argv.includes('--missing')) console.log(tg.filter(r => r.executable !== 'да').map(r => r.id + ' | ' + r.name + (r.block ? ' | ' + r.block : '')).join('\n'));

"""Validate this handoff's contents. Does not simulate Unity combat."""
import csv, hashlib, json, re, struct, sys
from pathlib import Path
from xml.etree import ElementTree

root=Path(__file__).resolve().parents[1]
def read(name):return json.loads((root/name).read_text(encoding='utf-8'))
def check(value,message):
 if not value:raise AssertionError(message)
def payload(name,id):
 text=(root/name).read_text(encoding='utf-8')
 match=re.search(r'<script\b[^>]*id="'+id+r'"[^>]*>(.*?)</script>',text,re.S)
 check(match,'Missing embedded '+id)
 return json.loads(match[1])
def run():
 statuses=read('Data/combat_statuses.json')['rows'];tags=read('Data/passive_tags_effective.json')['records'];icons=read('Data/status_icon_manifest.json')['items']
 check(len(statuses)==89,'Expected 89 statuses');check(len(tags)==457,'Expected all 457 tags')
 check(len({r['id']for r in statuses})==89,'Duplicate status ID');check(len({r['id']for r in tags})==457,'Duplicate tag ID')
 sids={r['id']for r in statuses};tmap={r['id']:r for r in tags}
 original=payload('Sources/Creature_Tag_Workshop_source.html','catalog-data');workspace=payload('Sources/Creature_Tag_Workshop_source.html','workspace-state')
 for row in original['records']:
  expected={**row,**workspace['changes'].get(row['id'],{})};actual=tmap[row['id']]
  for key in ['name','effect','notes','favorite','deleted','archived']:
   check(actual.get(key)==expected.get(key),f'Changed user field {row["id"]}.{key}')
 check(payload('Sources/Combat_Status_Workshop_source.html','catalog-data')['rows']==statuses,'Status source changed')
 check(sum(bool(r.get('favorite'))for r in tags)==176,'Favorite flags changed')
 check(sum(bool(r.get('deleted'))for r in tags)==197,'Trash changed')
 selection=read('Data/selection_manifest.json')
 check(len(selection['selectedActiveCombatIds'])==168,'Selected combat count')
 check(len(selection['activeCombatIds'])==254,'Active combat count')
 check(not set(selection['trashIds'])&set(selection['activeCombatIds']),'Trash enabled')
 check(len(icons)==89 and {r['statusId']for r in icons}==sids,'Icon mapping incomplete')
 digests=[]
 for item in icons:
  ElementTree.parse(root/item['svg'])
  for size in [256,128]:
   data=(root/item['png'+str(size)]).read_bytes()
   check(data[:8]==b'\x89PNG\r\n\x1a\n','Not PNG')
   check(struct.unpack('>II',data[16:24])==(size,size),'Wrong PNG dimensions')
   check(data[25]in [4,6],'PNG missing alpha channel')
  digests.append(hashlib.sha256((root/item['png256']).read_bytes()).hexdigest())
 check(len(set(digests))==89,'Duplicate icon bitmaps')
 for link in read('Data/state_tag_links.json')['links']:
  check(link['tagId']in tmap and link['statusId']in sids,'Broken tag/status link')
 for row in read('Data/rule_variants_and_conflicts.json')['entries']:
  check(row['tagId']in tmap or row['tagId']=='status.130/formula','Unknown resolution ID')
  for id in row['normalizedStatusIds']:check(id in sids,'Unknown normalized status')
 units={r['id']for r in read('Data/unit_baseline_snapshot.json')['units']}
 for row in read('Data/creature_assignments.json')['assignments']:
  check(row['unitId']in units,'Unknown pilot unit')
  for id in row['addPassiveIds']:
   check(id in selection['selectedActiveCombatIds'],'Pilot tag not selected active combat')
  for effect in row['abilityStatusPayloads']:
   if effect.get('statusId'):check(effect['statusId']in sids,'Unknown pilot state')
 brief=(root/'CLAUDE_MASTER_BRIEF_RU.md').read_text(encoding='utf-8')
 for row in statuses+tags:check('### `'+row['id']+'` — '+row['name']in brief,'Brief missing '+row['id'])
 with(root/'Data/implementation_coverage.csv').open(encoding='utf-8-sig')as f:check(len(list(csv.DictReader(f)))==546,'Coverage row missing')
 with(root/'Data/status_acceptance_matrix.csv').open(encoding='utf-8-sig')as f:check(len(list(csv.DictReader(f)))==89,'Status acceptance row missing')
 manifest=read('checksums.json')
 for item in manifest['files']:
  p=root/item['path'];data=p.read_bytes();check(len(data)==item['sizeBytes'],'Size '+item['path']);check(hashlib.sha256(data).hexdigest()==item['sha256'],'Checksum '+item['path'])
 return {'packageValidation':'PASS','statuses':89,'tags':457,'favoritesPreserved':176,'selectedActiveCombat':168,'trashPreserved':197,'svgFiles':89,'pngFiles':178,'uniqueIcons':89,'checkedFiles':len(manifest['files']),'sourceFieldsPreserved':True,'unityImplementationTested':False,'note':'Проверка комплектности/сохранности, не тест реализации боевых правил.'}
if __name__=='__main__':
 try:print(json.dumps(run(),ensure_ascii=False,indent=2))
 except Exception as e:print('FAIL: '+str(e),file=sys.stderr);raise

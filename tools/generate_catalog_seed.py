"""Extract 27 concept-confirmed plugins from source GDD v2 tables into machine-readable seed.

The IDs, display names and balance are DRAFT. Importer must require separate content approval
before saving these draft IDs into a production player save file.
"""
from pathlib import Path
import json, re

root = Path(__file__).resolve().parents[1]
src = root / 'docs' / '03_PLUGINS_AND_SKILLS.md'
text = src.read_text(encoding='utf-8')
rows = []
for line in text.splitlines():
    match = re.match(r'^\|\s*([GRL])(\d\d)\s*\|\s*([^|]+)\|\s*([^|]+)\|\s*([^|]+)\|', line)
    if not match:
        continue
    rarity = {'G': 'common', 'R': 'rare', 'L': 'legendary'}[match.group(1)]
    rows.append({
        'draftId': f'{match.group(1)}{match.group(2)}',
        'rarity': rarity,
        'draftDisplayNameKo': match.group(3).strip(),
        'originalEffectDirection': match.group(4).strip(),
        'unresolvedDesign': match.group(5).strip(),
        'designStatus': 'concept-confirmed | balance-and-final-ids-draft',
        'learnableAfterExtraction': rarity in ('common', 'rare'),
    })
assert len(rows) == 27, f'expected 27, found {len(rows)}'
assert {v:sum(r['rarity']==v for r in rows) for v in ('common','rare','legendary')} == {'common':17,'rare':7,'legendary':3}
result={
    'catalogVersion':'planning-v2',
    'generatedFrom':'docs/03_PLUGINS_AND_SKILLS.md',
    'warning':'Draft IDs and Korean display names are NOT approved immutable save IDs. No mythic content is approved.',
    'pluginCountExistingConcepts':27,
    'plugins':rows
}
out=root/'docs'/'data'/'plugin_catalog_seed.json'
out.write_text(json.dumps(result,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
print(f'Wrote {out} ({len(rows)} entries)')

from pathlib import Path

p = Path('ProjectDocs/DEVELOPMENT_STATUS.md')
text = p.read_text(encoding='utf-8')
old = '''### Реализация и проверка

- изменяются только `ProjectDocs/LORE.md`, `ProjectDocs/BESTIARY.md` и `ProjectDocs/DEVELOPMENT_STATUS.md`;
- изменения документальные, поэтому Unity compilation и Unity Test Runner не запускаются;
- проверяется, что новые утверждённые принципы не противоречат разделам 54–58 `LORE.md` и 24–27 `BESTIARY.md`;
- источник реально использован: Н. Я. Никифоровский — **«Нечистики»** (1907);
- после публикации требуется только Pull актуального `main`; техническая миграция в Unity не нужна.'''
new = '''### Реализация и проверка

- `ProjectDocs/LORE.md`, `ProjectDocs/BESTIARY.md` и `ProjectDocs/DEVELOPMENT_STATUS.md` интегрированы поверх решений Гнатюка commit `10521bc` (`docs: integrate Nikiforovsky folklore decisions`);
- итоговая интеграция затрагивает только эти три документа; код, сцены, UXML/USS и игровые данные не менялись;
- изменения документальные, поэтому Unity compilation и Unity Test Runner не запускались;
- проверено, что утверждённые принципы Никифоровского не противоречат разделам 54–58 `LORE.md` и 24–27 `BESTIARY.md`; новые разделы получили номера 59 и 28 соответственно;
- источник реально использован: Н. Я. Никифоровский — **«Нечистики»** (1907);
- после Pull актуального `main` техническая миграция в Unity не требуется.'''
if old not in text:
    raise RuntimeError('status block not found')
p.write_text(text.replace(old, new, 1), encoding='utf-8')
print('status finalized')
# trigger valid workflow

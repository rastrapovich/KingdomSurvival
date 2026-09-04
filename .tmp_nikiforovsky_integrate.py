from pathlib import Path
import subprocess

OLD_COMMIT = "c96afa45591a8bed54097672a3a159c20dcbc86e"


def git_show(path: str) -> str:
    return subprocess.check_output(["git", "show", f"{OLD_COMMIT}:{path}"], text=True, encoding="utf-8")


def extract_from(text: str, marker: str) -> str:
    idx = text.find(marker)
    if idx < 0:
        raise RuntimeError(f"marker not found: {marker}")
    return text[idx:].strip() + "\n"


lore_path = Path("ProjectDocs/LORE.md")
bestiary_path = Path("ProjectDocs/BESTIARY.md")
status_path = Path("ProjectDocs/DEVELOPMENT_STATUS.md")

# LORE: preserve current Gnatiuk section 58 and append Nikiforovsky as 59.
lore = lore_path.read_text(encoding="utf-8")
new_lore_marker = "# 59. Решения по Н. Я. Никифоровскому — «Нечистики»"
if new_lore_marker not in lore:
    old_lore = git_show("ProjectDocs/LORE.md")
    section = extract_from(old_lore, "# 58. Решения по Н. Я. Никифоровскому — «Нечистики»")
    section = section.replace("# 58. Решения по Н. Я. Никифоровскому", "# 59. Решения по Н. Я. Никифоровскому", 1)
    section = section.replace("## 58.", "## 59.")
    section = section.replace("раздела 58", "раздела 59").replace("раздел 58", "раздел 59")
    lore = lore.rstrip() + "\n\n---\n\n" + section

    journal_row = "| 05.09.2026 | По Н. Я. Никифоровскому «Нечистики» утверждены принципы самостоятельных отношений хозяйственных пространств, преобразования мифологической территории человеком, памяти долга, сложного права на клад, исторического происхождения богатства, сверхъестественного изменения ориентиров, ненаследуемости колдовской вины и изменения среды как допустимого способа решения; конкретные сущности вынесены в BESTIARY как рабочий FOLKLORE-материал. | УТВЕРЖДЕНО / РАБОЧЕЕ |"
    if journal_row not in lore:
        anchor = "| 05.09.2026 | По В. Гнатюку утверждены карпатско-галицкие принципы пространственного «блуда», хозяйственной взаимности, нечистого богатства, локальных названий мёртвых и социального статуса знающих людей; блуд, хмарник, инклюз, хованец, покутник и кладовые признаки сохранены как рабочий материал там, где их объективная природа не установлена. | УТВЕРЖДЕНО / РАБОЧЕЕ |"
        if anchor not in lore:
            raise RuntimeError("Gnatiuk journal anchor not found")
        lore = lore.replace(anchor, anchor + "\n" + journal_row, 1)
    lore_path.write_text(lore.rstrip() + "\n", encoding="utf-8")

# BESTIARY: preserve current Gnatiuk section 27 and append Nikiforovsky as 28.
bestiary = bestiary_path.read_text(encoding="utf-8")
new_bestiary_marker = "# 28. Никифоровский — правила и рабочие сущности"
if new_bestiary_marker not in bestiary:
    old_bestiary = git_show("ProjectDocs/BESTIARY.md")
    section = extract_from(old_bestiary, "# 27. Никифоровский — правила и рабочие сущности")
    section = section.replace("# 27. Никифоровский", "# 28. Никифоровский", 1)
    section = section.replace("## 27.", "## 28.")
    section = section.replace("Раздел 27", "Раздел 28").replace("раздел 27", "раздел 28")
    bestiary = bestiary.rstrip() + "\n\n---\n\n" + section
    bestiary_path.write_text(bestiary.rstrip() + "\n", encoding="utf-8")

# DEVELOPMENT_STATUS: Gnatiuk is section 16, so Nikiforovsky becomes 17.
status = status_path.read_text(encoding="utf-8")
new_status_marker = "## 17. Н. Я. Никифоровский — утверждённые решения по «Нечистикам»"
if new_status_marker not in status:
    old_status = git_show("ProjectDocs/DEVELOPMENT_STATUS.md")
    section = extract_from(old_status, "## 16. Н. Я. Никифоровский — утверждённые решения по «Нечистикам»")
    section = section.replace("## 16. Н. Я. Никифоровский", "## 17. Н. Я. Никифоровский", 1)
    section = section.replace("разделам 54–57 `LORE.md` и 24–26 `BESTIARY.md`", "разделам 54–58 `LORE.md` и 24–27 `BESTIARY.md`")
    status = status.rstrip() + "\n\n" + section
    status_path.write_text(status.rstrip() + "\n", encoding="utf-8")

# Guardrails: only intended documents may differ after temporary files are removed by the workflow.
for path, marker in [
    (lore_path, new_lore_marker),
    (bestiary_path, new_bestiary_marker),
    (status_path, new_status_marker),
]:
    text = path.read_text(encoding="utf-8")
    if marker not in text:
        raise RuntimeError(f"integration marker missing in {path}")

print("Nikiforovsky integration prepared successfully")

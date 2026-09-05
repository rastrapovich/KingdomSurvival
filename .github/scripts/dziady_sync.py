from pathlib import Path
import re
import subprocess

SOURCE_WORKFLOW = ".github/workflows/dziady-doc-sync.yml"
TARGETS = {
    "ProjectDocs/LORE.md": "DZII-L1",
    "ProjectDocs/BESTIARY.md": "DZII-B1",
    "ProjectDocs/NARRATIVE.md": "DZII-N1",
    "ProjectDocs/DEVELOPMENT_STATUS.md": "Документальная интеграция Adam Mickiewicz — `Dziady`, часть II",
}

source = subprocess.check_output(
    ["git", "show", f"HEAD^:{SOURCE_WORKFLOW}"],
    text=True,
    encoding="utf-8",
)

pattern = re.compile(
    r'^\s*"(?P<path>ProjectDocs/(?:LORE|BESTIARY|NARRATIVE|DEVELOPMENT_STATUS)\.md)": r\'\'\'(?P<body>.*?)^\s*\'\'\',\s*$',
    re.MULTILINE | re.DOTALL,
)
blocks = {match.group("path"): match.group("body") for match in pattern.finditer(source)}

missing = [path for path in TARGETS if path not in blocks]
if missing:
    raise SystemExit(f"Could not recover Dziady II blocks from parent workflow: {missing}")

already_present = []
for path_str, marker in TARGETS.items():
    text = Path(path_str).read_text(encoding="utf-8")
    if marker in text:
        already_present.append(path_str)

if already_present:
    raise SystemExit(f"Dziady II markers already present before sync: {already_present}")

for path_str in TARGETS:
    path = Path(path_str)
    text = path.read_text(encoding="utf-8")
    addition = blocks[path_str].strip("\n")
    path.write_text(text.rstrip() + "\n\n" + addition + "\n", encoding="utf-8")

for path_str, marker in TARGETS.items():
    text = Path(path_str).read_text(encoding="utf-8")
    count = text.count(marker)
    if count != 1:
        raise SystemExit(f"Expected exactly one {marker!r} in {path_str}, found {count}")

Path(SOURCE_WORKFLOW).unlink()
Path(__file__).unlink()

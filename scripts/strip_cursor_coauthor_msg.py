"""Используется как git filter-branch --msg-filter для удаления строки Co-authored-by Cursor."""
import sys

text = sys.stdin.read()
lines = text.splitlines(True)
out = "".join(
    ln
    for ln in lines
    if not ln.strip().startswith("Co-authored-by: Cursor")
)
sys.stdout.write(out)

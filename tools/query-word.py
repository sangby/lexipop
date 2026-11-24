import sqlite3
import sys

word = sys.argv[1] if len(sys.argv) > 1 else "hello"
conn = sqlite3.connect("data/stardict.db")
row = conn.execute(
    "SELECT word, phonetic, translation, pos FROM stardict WHERE word = ?", (word,)
).fetchone()
print(repr(row))

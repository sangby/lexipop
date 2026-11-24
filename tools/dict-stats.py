import sqlite3

c = sqlite3.connect("data/stardict.db")

print("total:", c.execute("SELECT COUNT(*) FROM stardict").fetchone()[0])
print("with_freq:", c.execute(
    "SELECT COUNT(*) FROM stardict WHERE collins>0 OR bnc>0 OR frq>0"
).fetchone()[0])
print("with_trans:", c.execute(
    "SELECT COUNT(*) FROM stardict WHERE translation IS NOT NULL AND translation != ''"
).fetchone()[0])
print("with_freq_and_trans:", c.execute(
    "SELECT COUNT(*) FROM stardict WHERE (collins>0 OR bnc>0 OR frq>0) "
    "AND translation IS NOT NULL AND translation != ''"
).fetchone()[0])

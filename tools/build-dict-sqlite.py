"""从 ECDICT 的 SQLite 数据库（ecdict-sqlite-28.zip 解压出的 ecdict.db）
生成 LexiPop 自定义二进制词库 dict.bin。

格式与 tools/build-dict.py 保持一致。
"""

import sqlite3
import struct
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
SRC = ROOT / "data" / "stardict.db"
DST = ROOT / "data" / "dict.bin"

POS_MAP = {
    "n": 1, "v": 2, "adj": 3, "adv": 4, "prep": 5, "conj": 6,
    "pron": 7, "num": 8, "art": 9, "int": 10, "aux": 11, "abbr": 12,
}


def to_int(value) -> int:
    try:
        return int(value or 0)
    except (TypeError, ValueError):
        return 0


def main() -> int:
    if not SRC.exists():
        print(f"找不到 {SRC}", file=sys.stderr)
        return 1

    conn = sqlite3.connect(str(SRC))
    cur = conn.cursor()

    cur.execute("SELECT name FROM sqlite_master WHERE type='table'")
    tables = [row[0] for row in cur.fetchall()]
    print("tables:", tables)

    table = "stardict" if "stardict" in tables else tables[0]
    cur.execute(
        f"SELECT word, phonetic, translation, pos, collins, bnc, frq FROM {table}"
    )

    unique = {}
    total = 0
    skipped = 0

    for word, phonetic, translation, pos, collins, bnc, frq in cur:
        total += 1

        word = (word or "").strip()
        translation = (translation or "").strip()
        phonetic = (phonetic or "").strip()

        if not word or not translation:
            skipped += 1
            continue

        collins_i = to_int(collins)
        bnc_i = to_int(bnc)
        frq_i = to_int(frq)

        if collins_i == 0 and bnc_i == 0 and frq_i == 0:
            skipped += 1
            continue

        if len(word) > 64 or len(translation) > 4000:
            skipped += 1
            continue

        pos_key = (pos or "").strip().split(":")[0].lower()
        unique.setdefault(word.lower(), (
            word,
            phonetic,
            translation,
            POS_MAP.get(pos_key, 0),
            min(collins_i, 255),
            bnc_i,
            frq_i,
        ))

    conn.close()

    ordered = sorted(unique.values(), key=lambda e: e[0].lower())

    with DST.open("wb") as f:
        f.write(b"LXPD")
        f.write(struct.pack("<H", 1))
        f.write(struct.pack("<I", len(ordered)))

        for word, phonetic, translation, pos, collins, bnc, frq in ordered:
            word_b = word.encode("utf-8")
            phon_b = phonetic.encode("utf-8")
            trans_b = translation.encode("utf-8")

            f.write(struct.pack("<H", len(word_b)))
            f.write(word_b)
            f.write(struct.pack("<H", len(phon_b)))
            f.write(phon_b)
            f.write(struct.pack("<I", len(trans_b)))
            f.write(trans_b)
            f.write(struct.pack("<B", pos))
            f.write(struct.pack("<B", collins))
            f.write(struct.pack("<I", bnc))
            f.write(struct.pack("<I", frq))

    size_mb = DST.stat().st_size / 1024 / 1024
    print(f"总行数: {total}")
    print(f"跳过:   {skipped}")
    print(f"写入:   {len(ordered)}")
    print(f"输出:   {DST} ({size_mb:.1f} MB)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

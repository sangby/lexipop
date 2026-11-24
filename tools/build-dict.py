"""把 ECDICT 的 stardict.csv 转成 LexiPop 自定义二进制词库。

格式（小端）：
  magic   "LXPD"
  version uint16
  count   uint32
  重复 count 次：
    wordLen  uint16, word  utf8
    phonLen  uint16, phon  utf8
    transLen uint32, trans utf8
    pos      uint8   (0=未知)
    collins  uint8
    bnc      uint32
    frq      uint32

筛选策略：只保留有中文释义、且有词频/星级标注的常用词，
避免把 340 万生僻词全塞进内存。
"""

import csv
import struct
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
SRC = ROOT / "data" / "stardict.csv"
DST = ROOT / "data" / "dict.bin"

POS_MAP = {
    "n": 1, "v": 2, "adj": 3, "adv": 4, "prep": 5, "conj": 6,
    "pron": 7, "num": 8, "art": 9, "int": 10, "aux": 11, "abbr": 12,
}


def parse_int(value: str) -> int:
    try:
        return int(value or 0)
    except ValueError:
        return 0


def main() -> int:
    if not SRC.exists():
        print(f"找不到 {SRC}", file=sys.stderr)
        return 1

    entries = []
    total = 0
    skipped = 0

    with SRC.open("r", encoding="utf-8", newline="") as f:
        reader = csv.DictReader(f)
        for row in reader:
            total += 1

            word = (row.get("word") or "").strip()
            translation = (row.get("translation") or "").strip()
            phonetic = (row.get("phonetic") or "").strip()

            if not word or not translation:
                skipped += 1
                continue

            collins = parse_int(row.get("collins"))
            bnc = parse_int(row.get("bnc"))
            frq = parse_int(row.get("frq"))

            # 只保留有词频/星级标注的常用词
            if collins == 0 and bnc == 0 and frq == 0:
                skipped += 1
                continue

            if len(word) > 64 or len(translation) > 4000:
                skipped += 1
                continue

            pos = (row.get("pos") or "").strip().split(":")[0].lower()
            entries.append((
                word,
                phonetic,
                translation,
                POS_MAP.get(pos, 0),
                min(collins, 255),
                bnc,
                frq,
            ))

    # 去重（保留第一次出现），按小写排序
    unique = {}
    for entry in entries:
        unique.setdefault(entry[0].lower(), entry)
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

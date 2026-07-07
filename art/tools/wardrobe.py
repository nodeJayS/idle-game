# Wardrobe browser over the extracted MS2 item tables (ROADMAP 10.11a).
# Joins three sources into one searchable index so hero relooks can SHOP:
#   1. Extracted\Item\**\*.nif          — the wearable meshes that actually exist
#   2. Extracted\Xml\string\en\itemname.xml — item id -> English display name
#   3. Extracted\Xml\item\{p}\{pp}\{id}.xml — equip slot + gender limit
# The joined index is cached OUTSIDE the repo (next to the extracts) per the
# raw-extracts-stay-outside rule; only this script is committed.
#
#   python art/tools/wardrobe.py wizard hat            # keyword search (AND)
#   python art/tools/wardrobe.py --slot CP --gender f  # all female hats
#   python art/tools/wardrobe.py bandana --json        # machine-readable
#   python art/tools/wardrobe.py --rebuild             # force reindex
#
# Output rows end with the manifest-ready path ("Item/1/13/....nif") so a hit
# can be pasted straight into art/heroes/<defId>.json.

import argparse
import json
import os
import re
import sys
import xml.etree.ElementTree as ET

if hasattr(sys.stdout, "reconfigure"):  # item names carry accents; Windows console defaults to cp1252
    sys.stdout.reconfigure(encoding="utf-8")

EXTRACT_ROOT = r"C:\Games\MapleStory2\Extracted"
ITEM_NIF_ROOT = os.path.join(EXTRACT_ROOT, "Item")
XML_ROOT = os.path.join(EXTRACT_ROOT, "Xml")
CACHE = os.path.join(EXTRACT_ROOT, "wardrobe_index.json")

# Slot codes as they appear in item xml <slot name="..."> / nif shortnames.
SLOT_WORDS = {
    "CP": "hat", "HR": "hair", "FA": "face", "FD": "face-decal",
    "CL": "top", "PA": "pants", "GL": "gloves", "SH": "shoes",
    "MT": "mantle", "ER": "earring", "EA": "ear", "BE": "belt",
    "RH": "weapon-R", "LH": "weapon-L", "OH": "two-hand", "FH": "off-hand",
}

NIF_RE = re.compile(r"^(\d{8})(?:_(f|m))?_(.+)$")

# Some ids have meshes but no item xml (tables predate them) — infer the slot
# from the shortname's leading code, e.g. "cpsnowbell" -> CP. "op" = one-piece
# suits, which occupy the CL slot.
SHORT_PREFIXES = {p.lower(): p for p in SLOT_WORDS}
SHORT_PREFIXES["op"] = "CL"


def slot_from_short(item_id, short):
    if int(item_id) >= 13000000:  # weapons don't carry the code prefix; avoid "shadowsword"->SH
        return ""
    code = short[:2].lower()
    return SHORT_PREFIXES.get(code, "")


def find_itemname_xml():
    """The English item-name string table (other locales sit alongside it)."""
    path = os.path.join(XML_ROOT, "string", "en", "itemname.xml")
    return path if os.path.exists(path) else None


def load_names():
    path = find_itemname_xml()
    if not path:
        sys.exit("itemname.xml not found under %s — extract Xml.m2d first (see README)." % XML_ROOT)
    names = {}
    for _ev, el in ET.iterparse(path):
        if el.tag == "key":
            iid, name = el.get("id"), el.get("name")
            if iid and name:
                names[iid.zfill(8)] = (name, el.get("class") or "")
            el.clear()
    return names


def item_xml_meta(item_id):
    """slot code + gender limit straight from the item's own xml (authoritative)."""
    # layout: item/{id//10000000}/{(id//100000)%100:02d}/{id}.xml
    n = int(item_id)
    path = os.path.join(XML_ROOT, "item", str(n // 10000000), "%02d" % ((n // 100000) % 100), "%s.xml" % item_id)
    if not os.path.exists(path):
        return None, None
    try:
        root = ET.parse(path).getroot()
    except ET.ParseError:
        return None, None
    env = root.find("environment")
    if env is None:
        return None, None
    slot = None
    slots = env.find("slots")
    if slots is not None:
        for s in slots.findall("slot"):
            if s.get("name"):
                slot = s.get("name")
                break
    limit = env.find("limit")
    gender = limit.get("genderLimit") if limit is not None else None
    return slot, gender


def build_index():
    print("indexing %s ..." % ITEM_NIF_ROOT, file=sys.stderr)
    names = load_names()
    rows = []
    for base, _dirs, files in os.walk(ITEM_NIF_ROOT):
        for f in files:
            if not f.lower().endswith(".nif"):
                continue
            m = NIF_RE.match(os.path.splitext(f)[0])
            if not m:
                continue
            iid, gender, short = m.group(1), m.group(2) or "", m.group(3)
            rel = os.path.relpath(os.path.join(base, f), EXTRACT_ROOT).replace("\\", "/")
            slot, glimit = item_xml_meta(iid)
            name, cls = names.get(iid, ("", ""))
            rows.append({
                "id": iid,
                "name": name,
                "class": cls,
                "short": short,
                "slot": slot or slot_from_short(iid, short),
                # mesh filename token (_f_/_m_) wins; else item xml genderLimit (0 male, 1 female, 2 both)
                "gender": gender or ({"0": "m", "1": "f"}.get(glimit or "", "")),
                "nif": rel,
            })
    with open(CACHE, "w", encoding="utf-8") as fh:
        json.dump(rows, fh, ensure_ascii=False, indent=0)
    print("indexed %d items -> %s" % (len(rows), CACHE), file=sys.stderr)
    return rows


def load_index(rebuild=False):
    if not rebuild and os.path.exists(CACHE):
        with open(CACHE, encoding="utf-8") as fh:
            return json.load(fh)
    return build_index()


def main():
    ap = argparse.ArgumentParser(description="Browse the extracted MS2 wardrobe.")
    ap.add_argument("keywords", nargs="*", help="AND-matched against name/shortname/id")
    ap.add_argument("--slot", help="slot code (CP hat, CL top, PA pants, GL gloves, SH shoes, ...)")
    ap.add_argument("--gender", choices=["f", "m"], help="only this gender (unisex always included)")
    ap.add_argument("--json", action="store_true", help="print matches as JSON")
    ap.add_argument("--rebuild", action="store_true", help="reindex from the extracts")
    ap.add_argument("--limit", type=int, default=60, help="max rows to print (default 60, 0 = all)")
    args = ap.parse_args()
    if args.limit <= 0:
        args.limit = None

    rows = load_index(args.rebuild)
    kws = [k.lower() for k in args.keywords]
    hits = []
    for r in rows:
        hay = ("%s %s %s %s" % (r["name"], r["class"], r["short"], r["id"])).lower()
        if kws and not all(k in hay for k in kws):
            continue
        if args.slot and r["slot"].upper() != args.slot.upper():
            continue
        if args.gender and r["gender"] and r["gender"] != args.gender:
            continue
        hits.append(r)

    if args.json:
        print(json.dumps(hits[: args.limit], ensure_ascii=False, indent=2))
        return
    for r in hits[: args.limit]:
        slot = r["slot"] or "??"
        word = SLOT_WORDS.get(slot, "")
        print("%s  %-2s %-9s %-1s  %-38s %s" % (r["id"], slot, word, r["gender"] or "-", (r["name"] or r["short"])[:38], r["nif"]))
    shown = len(hits[: args.limit])
    print("-- %d match(es)%s" % (len(hits), (", showing %d" % shown) if shown < len(hits) else ""), file=sys.stderr)


if __name__ == "__main__":
    main()

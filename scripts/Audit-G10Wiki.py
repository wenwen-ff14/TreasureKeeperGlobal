"""Compare pinned Wiki reward sections to each corresponding embedded catalog.

Run from the repository root after Fetch-CatalogSources.ps1. No network or
catalog writes: the output is docs/g10-wiki-check.json. Website current revision
verification is a separate, dated observation in G10_AUDIT.md.
"""
import hashlib
import html
import json
import re
from pathlib import Path

root = Path(__file__).resolve().parent.parent


def read_json(path):
    return json.loads((root / path).read_text(encoding="utf-8-sig"))


catalog = read_json("TreasureKeeper/Data/catalog.json")
sources = read_json("docs/catalog-sources.json")
names = read_json(".cache/teamcraft/items.json")
by_name = {}
for item_id, translations in names.items():
    if translations.get("en"):
        by_name.setdefault(translations["en"].casefold(), []).append(int(item_id))

pages = {
    17836: "Timeworn_Gazelleskin_Map",
    55002: "The_Lost_Canals_of_Uznair",
    55004: "The_Shifting_Altars_of_Uznair",
    55003: "The_Hidden_Canals_of_Uznair",
}
ignored = {"Final Fantasy XIV Wiki:Style manual", "Treasure Hunt"}
scopes = []
failures = []
for vault_id, page in pages.items():
    provenance = next(s for s in sources["Wiki"] if s["Page"] == page)
    raw = (root / f".cache/wiki/{page}.html").read_bytes()
    digest = hashlib.sha256(raw).hexdigest().upper()
    if digest != provenance["SHA256"]:
        raise ValueError(f"Source hash mismatch: {page}; refresh source metadata first")
    document = raw.decode("utf-8-sig")
    if f'oldid={provenance["Revision"]}' not in document:
        raise ValueError(f"Revision mismatch: {page}")
    heading = r'<h3[^>]*id="Loot"' if vault_id == 17836 else r'<h2[^>]*id="(?:Possible|Potential)_Rewards"'
    start = re.search(heading, document)
    if start is None:
        raise ValueError(f"Missing reward section: {page}")
    rest = document[start.end():]
    end = re.search(r"<h2[ >]", rest)
    if end is None:
        raise ValueError(f"Missing reward section boundary: {page}")
    section = rest[:end.start()]
    vault = next(v for v in catalog["Vaults"] if v["Id"] == vault_id)
    existing = {i["Id"]: i for i in vault["Items"]}
    rows = []
    seen = set()
    for match in re.finditer(r'<a[^>]+title="([^"]+)"', section):
        title = html.unescape(match.group(1))
        if title in ignored:
            continue
        ids = by_name.get(title.casefold(), [])
        if len(ids) != 1:
            raise ValueError(f"Unresolved/ambiguous reward: {page}: {title}: {ids}")
        item_id = ids[0]
        if item_id in seen:
            continue
        seen.add(item_id)
        headings = re.findall(r'<h[34][^>]*id="([^"]+)"', section[:match.start()])
        group = headings[-1] if headings else "Rewards"
        item = existing.get(item_id)
        has_source = item is not None and "Consolegameswiki" in item["Sources"]
        row = {"Id": item_id, "EnglishName": title, "Name": item["Name"] if item else None,
               "Group": group, "Included": item is not None, "HasSource": has_source}
        rows.append(row)
        if not has_source:
            failures.append(f"{vault_id}/{item_id}: {title}")
    if not rows:
        raise ValueError(f"Empty reward list: {page}")
    missing = [r["Id"] for r in rows if not r["Included"]]
    scope = {"VaultId": vault_id, "Name": vault["Name"], "Page": page,
             "Revision": provenance["Revision"], "RevisionUrl": provenance["RevisionUrl"],
             "SHA256": digest, "CatalogCount": len(existing), "WikiCount": len(rows),
             "IncludedCount": len(rows) - len(missing), "MissingIds": missing, "Items": rows}
    scopes.append(scope)
    groups = {g: sum(r["Group"] == g for r in rows) for g in dict.fromkeys(r["Group"] for r in rows)}
    print(f'{page}: {scope["IncludedCount"]}/{len(rows)} included; catalog={len(existing)}; groups={groups}; missing={missing}')

result = {"Note": "Pinned source coverage only; not a complete server loot table. Each page is compared to its own vault.",
          "TeamcraftCommit": sources["TeamcraftCommit"], "Scopes": scopes}
(root / "docs/g10-wiki-check.json").write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
if failures:
    raise ValueError("Missing items or source tags: " + "; ".join(failures))

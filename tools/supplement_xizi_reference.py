#!/usr/bin/env python3
"""Compare a complete XIZI HTML project and import its prices with an explicit source priority."""
from __future__ import annotations

import argparse
import hashlib
import json
import re
from copy import deepcopy
from datetime import datetime, timezone
from pathlib import Path

from sync_xizi_excel import PRICE_KEYS, apply_supplement


FILES = {
    "xizi-base-prices.js": "XIZIPRICEDATA",
    "xizi-extra-rise.js": "EXTRARISEDATA",
    "xizi-doors-prices.js": "DOORSPRICEDATA_RAW",
    "xizi-cabin-finish.js": "CABINFINISHDATA_RAW",
    "xizi-panels.js": "PANELSDATA_RAW",
    "xizi-options.js": "OPTIONSDATA_RAW",
}

# Effective assignments in calculateOptionPrice, not the unused OPTIONS_CATALOG.
# The exact expression is checked against the supplied HTML before it is used.
FORMULA_RULES = {
    "CWT_SIDE": (4500, "P+(R+K+S)*4", "4500 + (R + K + S) * 4"),
    "CWTSAFETY": (4500, "P+(R+K+S)*4", "4500 + (R + K + S) * 4"),
    "COP2": (1508, "P+(N-10)*45", "1508 + (N - 10) * 45"),
    "CCTV": (14, "P*(R+K+16)", "14 * (R + K + 16)"),
    "TC": (21, "P*(R+K+16)", "21 * (R + K + 16)"),
    "EARTHQUAKE_EMERGENCY_RETURN": (3500, "P+300*((R+K+S)/1.5-(R+K+S)/2.5)", "3500 + 300 * ((R + K + S)/1.5-(R + K + S)/2.5)"),
    "ACCOLDSMALL": (3654, "P+11*(R+K+12)", "3654 + 11 * (R + K + 12)"),
    "ACCOLDLARGE": (4583, "P+11*(R+K+12)", "4583 + 11 * (R + K + 12)"),
    "ACHEATSMALL": (3983, "P+11*(R+K+12)", "3983 + 11 * (R + K + 12)"),
    "ACHEATLARGE": (4925, "P+11*(R+K+12)", "4925 + 11 * (R + K + 12)"),
    "HAD": (600, "P+101*D", "600 + 101 * D"),
    "RUS_PIT_INSPECTION_BOX": (1050, "P+24*(R+K+16)", "1050 + 24 * (R + K + 16)"),
    "RUS_HOISTWAY_LIGHTING_BY": (15, "P*((R+K+S)/4-(R+K+S)/7)", "(buildingHeight / 4 - buildingHeight / 7) * 15"),
}


def read_project(html_path, reference_directory):
    html = html_path.read_text()
    paired_html = reference_directory / html_path.name
    if paired_html.read_bytes() != html_path.read_bytes():
        raise ValueError("The reference directory HTML does not match the supplied HTML")
    hashes = {html_path.name: hashlib.sha256(html_path.read_bytes()).hexdigest()}
    data = {}
    for filename, variable in FILES.items():
        if not re.search(r'<script\s+src=[\"\']' + re.escape(filename) + r'[\"\']', html):
            raise ValueError(f"HTML does not reference {filename}")
        path = reference_directory / filename
        text = path.read_text()
        match = re.search(r"window\." + variable + r"\s*=\s*", text)
        if not match:
            raise ValueError(f"Missing data assignment: {variable}")
        # Parse JSON literals only. Never run the supplied page or JS files.
        data[variable], _ = json.JSONDecoder().raw_decode(text[match.end():])
        hashes[filename] = hashlib.sha256(path.read_bytes()).hexdigest()
    return html, data, {"kind": "reference-project", "html": html_path.name, "sha256": hashes}


def normalize_reference(html, data):
    result = {name: [] for name in PRICE_KEYS}

    def source(file, key, **extra):
        return {"kind": "reference-project", "file": file, "key": key, **extra}

    for series, speeds in data["XIZIPRICEDATA"].items():
        extra_series = "UN-Victor MRL" if series == "UN-Victor MRL(T)" else series
        for speed, stops_map in speeds.items():
            for stops, capacities in stops_map.items():
                for capacity, price in capacities.items():
                    result["basePrices"].append({
                        "series": series, "speed": float(speed), "stops": int(stops), "capacity": int(capacity),
                        "price": price,
                        "extraRisePerMeter": data["EXTRARISEDATA"].get(extra_series, {}).get(speed, {}).get(capacity),
                        "source": source("xizi-base-prices.js", f"{series}/{speed}/{stops}/{capacity}",
                                         extraRiseFile="xizi-extra-rise.js", extraRiseSeries=extra_series),
                    })

    capacities = sorted({1250 if e["capacity"] == 1275 else e["capacity"] for e in result["basePrices"]} | {550})
    for manufacturer, parts in data["DOORSPRICEDATA_RAW"].items():
        for part, types in parts.items():
            for door_type, prices in types.items():
                for raw_key, value in prices.items():
                    segments = raw_key.split("|")
                    if part == "car" and len(segments) != 4:
                        continue
                    if part == "through":
                        width, fire, finish = segments
                        caps = capacities
                    else:
                        cap, width, fire, finish = segments[:4]
                        caps = [450, 550, 630] if cap == "450~630" else [int(cap)]
                    price = value
                    if part == "car":
                        base = prices.get(f"{width}|{fire}|{finish}", 0)
                        price = -1 if -1 in (value, base) else base + value
                    floor = "-" if part != "landing" else "First" if segments[4] == "1st floor" else "Other"
                    for capacity in caps:
                        entry = {
                            "manufacturer": manufacturer, "part": {"car": "Car door", "through": "2nd door", "landing": "Shaft door"}[part],
                            "doorType": "2S" if door_type == "SO" else door_type, "fireRating": fire,
                            "finish": "AISI443" if "stainless" in finish.lower() else "Painted steel",
                            "capacity": capacity, "floor": floor, "width": int(width), "price": price,
                            "source": source("xizi-doors-prices.js", f"{manufacturer}/{part}/{door_type}/{raw_key}"),
                        }
                        result["doors"].append(entry)
                # The HTML falls back to other floors when the main floor price is absent.
                if part == "landing":
                    for entry in list(result["doors"]):
                        if entry["manufacturer"] == manufacturer and entry["part"] == "Shaft door" and entry["doorType"] == ("2S" if door_type == "SO" else door_type) and entry["floor"] == "Other":
                            candidate = {**entry, "floor": "First"}
                            fields = PRICE_KEYS["doors"]
                            if not any(all(e.get(k) == candidate.get(k) for k in fields) for e in result["doors"]):
                                candidate["source"] = {**entry["source"], "mainFloorFallback": True}
                                result["doors"].append(candidate)

    groups = {
        "designs": "Car design", "wall_materials": "Car walls", "ceilings": "Ceiling",
        "floors": "Floor", "mirrors": "Mirror", "handrails": "Handrail",
        "cop": "COP", "cop_buttons": "Button", "lop": "LOP", "lip": "LIP",
    }
    for variable, filename in (("CABINFINISHDATA_RAW", "xizi-cabin-finish.js"), ("PANELSDATA_RAW", "xizi-panels.js")):
        for group, items in data[variable].items():
            for code, item in items.items():
                entry = {"category": groups[group], "code": code,
                         "price": item.get("base_price", item.get("price", item.get("price_per_door"))),
                         "overprice": item.get("price_per_100mm", item.get("price_per_floor_above_4")),
                         "height": item.get("standard_height"),
                         "source": source(filename, f"{group}/{code}")}
                if group == "wall_materials":
                    entry.update(overprice=352, height=2400)
                if group == "handrails":
                    entry.update(price=item["price_small_cabin"], overprice=item["price_large_cabin"] - item["price_small_cabin"])
                for target, original in (("multiplier10501600", "multiplier_1050_1600"), ("multiplier1600Plus", "multiplier_1600_plus")):
                    if original in item:
                        entry[target] = item[original]
                result["decorations"].append(entry)

    function = html.split("function calculateOptionPrice(optionKey)", 1)[1].split("function updateFormulaOptionPrices", 1)[0]
    compact_function = re.sub(r"\s+", "", function)
    for code, item in data["OPTIONSDATA_RAW"].items():
        entry = {"code": code, "category": item["category"], "description": item.get("name"),
                 "price": item.get("price"), "type": item["type"], "formula": item.get("formula"),
                 "showInKp": item.get("showInKP"), "source": source("xizi-options.js", code)}
        if item["type"] == "formula":
            price, formula, expression = FORMULA_RULES[code]
            if "price=" + re.sub(r"\s+", "", expression) + ";" not in compact_function:
                raise ValueError(f"Effective HTML formula changed for {code}; review it before importing")
            entry.update(price=price, formula=formula)
            entry["source"].update(runtimeFunction="calculateOptionPrice", declaredFormula=item["formula"], effectiveExpression=expression)
        result["options"].append(entry)
    return result


def compare_and_supplement(catalog, reference, source, prefer_reference=False):
    xizi = catalog["xizi"]
    supplement = {"source": source, **{name: [] for name in PRICE_KEYS}}
    supplement["priority"] = "reference-project" if prefer_reference else "excel"
    report = {"source": source, "priority": supplement["priority"], "groups": {}}
    for group, fields in PRICE_KEYS.items():
        current = xizi[group] + (xizi["localRequirements"] if group == "options" else [])
        index = {tuple(e.get(k) for k in fields): e for e in current}
        summary = {"referenceEntries": len(reference[group]), "matchedPrices": 0, "additions": [], "differences": [], "onlyInOurCatalog": []}
        seen = set()
        for entry in reference[group]:
            key = tuple(entry.get(k) for k in fields)
            existing = index.get(key)
            # Compare the generic Excel door with the reference fire-specific price.
            if existing is None and group == "doors" and entry["part"] != "Shaft door":
                lookup = {**entry, "fireRating": "None"}
                existing = index.get(tuple(lookup.get(k) for k in fields))
            if existing is not None:
                seen.add(tuple(existing.get(k) for k in fields))
            label = dict(zip(fields, key))
            if existing is None or existing.get("price") is None or (not prefer_reference and existing.get("source", {}).get("kind") == "reference-project"):
                if entry.get("price") is not None:
                    supplement[group].append(entry)
                    summary["additions"].append({**label, "price": entry["price"], "source": entry["source"]})
                continue
            if existing["price"] == entry["price"]:
                summary["matchedPrices"] += 1
            compared = ("price", "extraRisePerMeter") if group == "basePrices" else ("price", "overprice", "height") if group == "decorations" else ("price", "formula") if group == "options" else ("price",)
            for field in compared:
                left, right = existing.get(field), entry.get(field)
                if field != "price" and (left is None or right is None) and (left or right) in (None, 0):
                    continue
                if left != right:
                    summary["differences"].append({**label, "field": field, "ours": left, "reference": right, "source": entry["source"]})
        summary["onlyInOurCatalog"] = [dict(zip(fields, key)) for key in index if key not in seen]
        if prefer_reference:
            supplement[group] = deepcopy(reference[group])
        summary["importedEntries"] = len(supplement[group])
        report["groups"][group] = summary
    return supplement, report


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("html", type=Path)
    parser.add_argument("--reference-directory", type=Path)
    parser.add_argument("--catalog", type=Path, default=Path("src/TFlexDrawingService.Api/Data/pricing-catalog.json"))
    parser.add_argument("--report", type=Path, default=Path("docs/xizi-reference-comparison.json"))
    parser.add_argument("--prefer-reference", action="store_true", help="Replace matching prices using the HTML project and persist this priority")
    args = parser.parse_args()
    html, data, source = read_project(args.html, args.reference_directory or args.html.parent)
    reference = normalize_reference(html, data)
    catalog = json.loads(args.catalog.read_text())
    supplement, report = compare_and_supplement(catalog, reference, source, args.prefer_reference)
    result = apply_supplement(deepcopy(catalog), supplement)
    result["generatedAt"] = datetime.now(timezone.utc).isoformat()
    for path, content in ((args.catalog.with_name("xizi-price-supplement.json"), supplement), (args.report, report), (args.catalog, result)):
        path.write_text(json.dumps(content, ensure_ascii=False, indent=2) + "\n")
    print(json.dumps({group: {"reference": item["referenceEntries"], "matched": item["matchedPrices"], "added": len(item["additions"]), "differences": len(item["differences"])} for group, item in report["groups"].items()}))


if __name__ == "__main__":
    main()

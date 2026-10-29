"""Extracts static game data (item names, prices, limits, localisation) from the
CRACK install into GameData.json, which the editor embeds as a resource.

Usage: python extract_gamedata.py <CRACK_Data dir> <output json>
Requires: UnityPy, TypeTreeGeneratorAPI
"""
import csv, io, json, os, struct, sys
import UnityPy
from UnityPy.helpers.TypeTreeGenerator import TypeTreeGenerator

G = sys.argv[1]
OUT = sys.argv[2]
FILES = ["globalgamemanagers.assets", "resources.assets", "sharedassets1.assets",
         "level1", "level0", "sharedassets0.assets"]
env = UnityPy.load(*[os.path.join(G, f) for f in FILES])

# Resolve script class names before attaching the generator (raw header read).
behaviours = []
for o in env.objects:
    if o.type.name != "MonoBehaviour":
        continue
    try:
        mb = o.read(check_read=False)
        behaviours.append((o, mb.m_Script.deref().read().m_ClassName, mb))
    except Exception:
        pass

gen = TypeTreeGenerator("6000.3.9f1")
gen.load_local_dll_folder(os.path.join(G, "Managed"))
env.typetree_generator = gen


def go_key(o, mb):
    ref = mb.m_GameObject
    return (o.assets_file.name, ref.m_PathID)


def go_name(mb):
    try:
        return mb.m_GameObject.deref().read().m_Name
    except Exception:
        return None


# --- localisation ---------------------------------------------------------
loc = {}
languages = []
for o in env.objects:
    if o.type.name == "TextAsset":
        d = o.read()
        if d.m_Name == "lang":
            text = d.m_Script if isinstance(d.m_Script, str) else d.m_Script.decode("utf-8", "replace")
            rows = list(csv.reader(io.StringIO(text)))
            header = rows[0]
            languages = [h.strip() for h in header[1:] if h.strip()]
            de = next((i for i, h in enumerate(header) if h.strip().lower() in ("german", "de", "deutsch")), 4)
            en = next((i for i, h in enumerate(header) if h.strip().lower() in ("english", "en")), 1)
            for r in rows[1:]:
                if len(r) > max(de, en) and r[0]:
                    loc[r[0]] = {"de": r[de], "en": r[en]}
            print("lang header:", header[:6], "de col", de)

# --- components -----------------------------------------------------------
asset_ids = {}          # gameobject key -> assetId
items = {}              # gameobject key -> item info
data = {"scanner": None, "botMaxLevel": 5, "cartTierPrices": [], "garage": {},
        "startingBalance": 2000, "maxAgitation": 100.0, "catalogs": [], "shop": []}

raw_shop = []
for o, cls, mb in behaviours:
    if cls == "ShopItemData":
        raw_shop.append(o)
        continue
    try:
        tt = o.read_typetree()
    except Exception:
        continue
    key = go_key(o, mb)
    if cls == "NetworkIdentity" and tt.get("_assetId"):
        asset_ids[key] = tt["_assetId"]
    if "itemNameKey" in tt:
        items[key] = {"prefab": go_name(mb), "nameKey": tt["itemNameKey"],
                      "maxDurability": tt.get("maxDurability", 0), "scrapValue": tt.get("scrapValue", 0),
                      "class": cls}
    if cls == "ScannerUpgradeManager":
        data["scanner"] = {k: tt[k] for k in ("depth", "spread", "duration", "cooldown", "sensitivity")}
    elif cls == "BotLevel":
        data["botMaxLevel"] = max(data["botMaxLevel"], tt.get("maxLevel", 0))
    elif cls == "CartFleetManager":
        data["cartTierPrices"] = tt["tierPrices"]
    elif cls == "GarageTerminal" and tt.get("_terminalId"):
        data["garage"][tt["_terminalId"]] = tt["_cost"]
    elif cls == "FinanceManager":
        data["startingBalance"] = tt["_startingBalance"]
    elif cls == "AgitationManager":
        data["maxAgitation"] = tt["maxAgitation"]
    elif cls == "ShopCatalog":
        data["catalogs"].append({"id": tt["m_Name"], "nameKey": tt["catalogName"],
                                 "fromStart": bool(tt["unlockedFromStart"]),
                                 "items": [(o.assets_file.name, p["m_PathID"]) for p in tt["items"]]})


# ShopItemData: generated typetree breaks on string[] features, parse by hand.
class R:
    def __init__(self, b): self.b, self.p = b, 0
    def i32(self): v = struct.unpack_from("<i", self.b, self.p)[0]; self.p += 4; return v
    def i64(self): v = struct.unpack_from("<q", self.b, self.p)[0]; self.p += 8; return v
    def u8(self): v = self.b[self.p]; self.p += 1; return v
    def align(self): self.p = (self.p + 3) & ~3
    def s(self):
        n = self.i32(); v = self.b[self.p:self.p + n].decode("utf-8", "replace"); self.p += n; self.align(); return v
    def pptr(self): return (self.i32(), self.i64())


shop_by_path = {}
for o in raw_shop:
    r = R(o.get_raw_data())
    r.pptr(); r.u8(); r.align(); r.pptr()
    name = r.s(); item_name = r.s(); r.s(); r.pptr()            # m_Name, itemName, tagline, icon
    price = r.i32(); shipping = r.i32(); spawn = r.pptr()
    for _ in range(r.i32()):                                    # features
        r.s()
    r.s(); max_p = r.i32(); reset = r.u8()                      # footnote, maxPurchases, resetDaily
    # Same-file prefab reference; cross-file ones are matched by name key below.
    aid = asset_ids.get((o.assets_file.name, spawn[1])) if spawn[0] == 0 else None
    entry = {"id": name, "nameKey": item_name, "price": price, "shippingCents": shipping,
             "maxPurchases": max_p, "resetDaily": bool(reset), "assetId": aid}
    shop_by_path[(o.assets_file.name, o.path_id)] = entry
    data["shop"].append(entry)

for c in data["catalogs"]:
    c["items"] = [shop_by_path[k]["id"] for k in c["items"] if k in shop_by_path]

# --- assetId -> prefab/item ----------------------------------------------
prefabs = {}
for key, aid in asset_ids.items():
    it = items.get(key, {})
    prefabs[str(aid)] = {"prefab": it.get("prefab"), "nameKey": it.get("nameKey"),
                         "maxDurability": it.get("maxDurability", 0), "scrapValue": it.get("scrapValue", 0)}
for o, cls, mb in behaviours:
    if cls == "NetworkIdentity":
        k = go_key(o, mb)
        if k in asset_ids and not prefabs[str(asset_ids[k])]["prefab"]:
            prefabs[str(asset_ids[k])]["prefab"] = go_name(mb)
data["prefabs"] = prefabs

# Link shop items to assetIds by matching item name key.
by_key = {v["nameKey"]: int(k) for k, v in prefabs.items() if v.get("nameKey")}
for s in data["shop"]:
    if not s["assetId"]:
        s["assetId"] = by_key.get(s["nameKey"])

used = set()
for v in prefabs.values():
    if v.get("nameKey"): used.add(v["nameKey"])
for s in data["shop"]: used.add(s["nameKey"])
for c in data["catalogs"]: used.add(c["nameKey"])
data["languages"] = languages
data["loc"] = {k: loc[k] for k in used if k in loc}
# Only the short names the editor shows (work order steps, robot names); no long game texts.
data["locAll"] = {k: v for k, v in loc.items() if k.startswith(("WorkOrder_Step_", "Bot_Collector_Name", "Bot_Drone_Name"))}

with open(OUT, "w", encoding="utf-8") as f:
    json.dump(data, f, ensure_ascii=False, indent=1)
print("prefabs", len(prefabs), "items", len(items), "shop", len(data["shop"]), "loc", len(data["loc"]), len(data["locAll"]))

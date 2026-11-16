"""Software modules and their gameplay effects (legacy API names retained)."""

from __future__ import annotations



# ----------------------------------------------------------------------------
# 소프트웨어
# ----------------------------------------------------------------------------
# (id, 이름, 설명, 등급, 최대 중첩)
UPGRADES = [
    ("rapid", "과열 방출", "연사 속도 +18%", 0, 6),
    ("power", "과충전 탄두", "탄환 피해 +4", 0, 8),
    ("pierce", "관통 코어", "탄환이 적 1체를 더 관통", 0, 3),
    ("split", "분열 사격", "탄환이 갈라져 2발 추가", 0, 3),
    ("homing", "유도 코일", "탄환이 적을 추적", 1, 3),
    ("boom", "폭발 탄두", "명중 시 주변에 폭발 피해", 1, 3),
    ("crit", "치명타 회로", "치명타 확률 +8%", 0, 5),
    ("vamp", "흡혈 장치", "적 처치 시 체력 +0.7", 1, 3),
    ("thorn", "가시 갑옷", "접촉한 적에게 피해 반사", 0, 3),
    ("dash2", "대시 충전", "대시 횟수 +1", 1, 3),
    ("dashb", "대시 폭발", "대시 중 주변 적에게 피해", 1, 3),
    ("slow", "시간 왜곡", "처치 시 시간이 느려짐", 2, 3),
    ("magnet", "자력 수집", "파편 수집 범위 +90", 0, 3),
    ("drone", "위성 드론", "궤도 드론이 자동 사격", 2, 3),
    ("chain", "연쇄 방전", "명중 시 근처 적에게 방전", 2, 2),
    ("rear", "후방 포탑", "후방으로 추가 사격", 1, 1),
    ("hull", "외골격", "최대 체력 +20 (즉시 회복)", 0, 4),
    ("ricochet", "도탄 장치", "탄환이 벽에서 1회 튕김", 1, 2),
    ("armor", "장갑 소프트웨어", "방어력 +5", 0, 5),
    ("breach", "관통 연산", "방어구 관통력 +4", 0, 5),
    ("mana", "마나 확장팩", "최대 마나 +25, 마나 25 회복", 0, 4),
    ("regen", "마나 순환기", "초당 마나 회복 +2", 0, 5),
    ("burn", "점화 프로토콜", "탄환 적중 시 18% 화상, 지속 피해 중첩", 1, 3),
    ("toxin", "부식 프로토콜", "탄환 적중 시 16% 중독, 지속 피해 중첩", 1, 3),
    ("leech", "에너지 환원", "탄환 적중마다 체력 +0.45", 1, 3),
    ("focus", "충격파 증폭", "Q 스킬 피해 +25%", 1, 4),
    ("cool", "스킬 가속기", "스킬 재사용 대기시간 9% 감소", 1, 4),
    ("eco", "마나 최적화", "스킬 마나 소모 10% 감소", 0, 4),
    ("sustain", "자가 복구", "초당 체력 회복 +0.5", 1, 4),
    ("duration", "지속 증폭", "화상/중독/보호막 지속시간 +20%", 1, 3),
]
UP_BY_ID = {u[0]: u for u in UPGRADES}
UP_WEIGHT = {0: 1.0, 1: 0.42, 2: 0.13}


def apply_upgrade(g, uid):
    st = g.p.st
    g.levels[uid] = g.levels.get(uid, 0) + 1
    if uid == "rapid":
        st["rate"] *= 1.18
    elif uid == "power":
        st["dmg"] += 4.0
    elif uid == "pierce":
        st["pierce"] += 1
    elif uid == "split":
        st["split"] += 1
    elif uid == "homing":
        st["homing"] += 2.6
    elif uid == "boom":
        st["explode"] += 20.0
    elif uid == "crit":
        st["crit"] += 0.08
    elif uid == "vamp":
        st["lifesteal"] += 0.7
    elif uid == "thorn":
        st["thorns"] += 14.0
    elif uid == "dash2":
        st["dash_max"] += 1
        g.p.charges += 1
    elif uid == "dashb":
        st["dash_blast"] += 30.0
    elif uid == "slow":
        st["slow"] += 0.075
    elif uid == "magnet":
        st["magnet"] += 90.0
    elif uid == "drone":
        st["drone"] += 1
    elif uid == "chain":
        st["chain"] += 1
    elif uid == "rear":
        st["rear"] += 1
    elif uid == "hull":
        g.p.maxhp += 20.0
        g.p.hp = min(g.p.maxhp, g.p.hp + 20.0)
    elif uid == "ricochet":
        st["ricochet"] += 1
    elif uid == "armor":
        st["armor"] += 5.0
    elif uid == "breach":
        st["armor_pen"] += 4.0
    elif uid == "mana":
        g.p.maxmana += 25.0
        g.p.mana = min(g.p.maxmana, g.p.mana + 25.0)
    elif uid == "regen":
        st["mana_regen"] += 2.0
    elif uid == "burn":
        st["burn_chance"] = min(0.75, st["burn_chance"] + 0.18)
        st["burn_dps"] += 2.5
    elif uid == "toxin":
        st["toxin_chance"] = min(0.75, st["toxin_chance"] + 0.16)
        st["toxin_dps"] += 2.0
    elif uid == "leech":
        st["life_on_hit"] += 0.45
    elif uid == "focus":
        st["skill_power"] *= 1.25
    elif uid == "cool":
        st["skill_cooldown"] *= 0.91
    elif uid == "eco":
        st["skill_efficiency"] *= 0.9
    elif uid == "sustain":
        st["hp_regen"] += 0.5
    elif uid == "duration":
        st["effect_duration"] += 0.2

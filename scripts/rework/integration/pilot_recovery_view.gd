extends RefCounted
## A-owned, read-only incident report for the opt-in rework CPU pilot.
## A verified receipt is evidence, NOT permission to resume or retry.
## This helper never calls Gateway.transact_command or changes C/B runtime.
const Gateway = preload("res://scripts/rework/persistence/progression_gateway.gd")
const Rare = preload("res://scripts/rework/progression/rare_bonus.gd")
const Catalog = preload("res://scripts/rework/plugins/plugin_catalog.gd")

static func inspect(slot_path: String, pending_command: Dictionary = {}) -> Dictionary:
    var report: Dictionary = {
        "readable": false, "state": "unverified", "revision": -1,
        "receipt_status": "not_checked", "rare_bonus_verified": false,
        "safe_to_resume": false, "can_retry": false,
        "text": "저장 상태를 확인할 수 없어. 자동 복구·초기화하지 않았어."
    }
    if slot_path.is_empty():
        report["text"] = "저장 슬롯 경로가 없어. 사용자 데이터를 변경하지 않았어."
        return report
    # E S01 metadata-only report. All statuses are advisory, never a grant,
    # auto-restore or proof that an interrupted active mission can resume.
    var diagnostic: Dictionary = Gateway.diagnose_slot(slot_path)
    var inspected: Dictionary = Gateway.inspect_slot(slot_path)
    if not bool(inspected.get("ok", false)):
        report["text"] = "저장 파일 검증 실패: " + str(inspected.get("reason", "unknown")) \
            + "\nE 진단: " + str(diagnostic.get("reason", "unknown")) \
            + "\n오프라인 점검: " + str(diagnostic.get("offline_recovery_hint", "inspect_manually")) \
            + "\n원본을 변경하지 않았어. 수동 점검이 필요해."
        return report

    report["readable"] = true
    report["state"] = str(inspected.get("state", "unknown"))
    report["revision"] = int(inspected.get("revision", -1))
    var lines: Array[String] = [
        "읽기 전용 저장 진단 · r%d" % int(report["revision"]),
        "상태: " + str(report["state"])
    ]
    var mission: Variant = inspected.get("mission_snapshot")
    if mission is Dictionary:
        lines.append("의뢰: " + str(mission.get("run_id", "unknown")))
        lines.append("정화한 구역: %d / 5 · 선택 대기 보상: %d" % [
            mission.get("hardware_cleaned_ids", []).size(),
            int(mission.get("pending_plugin_offers", 0))
        ])
        var reward: Dictionary = inspected.get("reward_snapshot", {})
        lines.append("카드 체크포인트: " + ("있음" if bool(inspected.get("checkpoint_present", false)) else "없음"))
        lines.append("재추첨 잔여: %d" % int(reward.get("rerolls_left", 0)))
    lines.append("안전 판정: " + str(inspected.get("reason", "unknown")))
    var files: Dictionary = diagnostic.get("files", {})
    lines.append("파일 상태: 원본 " + str(files.get("primary", {}).get("status", "unverified")) \
        + " · 백업 " + str(files.get("backup", {}).get("status", "unverified")) \
        + " · 복구본 " + str(files.get("recovery", {}).get("status", "unverified")))
    lines.append("쓰기 잠금: " + str(diagnostic.get("lock_owner_status", "unverified")) \
        + " · 오프라인 안내: " + str(diagnostic.get("offline_recovery_hint", "inspect_manually")))
    lines.append("자동 재개·포기·재지급·슬롯 초기화는 허용하지 않아.")

    if not pending_command.is_empty():
        var receipt_id: String = str(pending_command.get("receipt_id", ""))
        var run_id: String = str(pending_command.get("run_id", ""))
        var vaccine_id: String = str(pending_command.get("vaccine_id", ""))
        var generation: int = int(pending_command.get("generation", 0))
        if receipt_id.is_empty() or run_id.is_empty() or vaccine_id.is_empty() or generation < 1:
            report["receipt_status"] = "missing_identity"
            lines.append("보상 영수증: 식별 정보 부족. 재지급 금지.")
        else:
            # A durable receipt may belong to an older or already closed run.
            # Never misattribute it to a different live mission.
            if not mission is Dictionary or str(mission.get("run_id", "")) != run_id:
                report["receipt_status"] = "unverified"
                lines.append("보상 영수증: 현재 의뢰 ID와 일치하지 않아 확인 보류.")
                report["text"] = "\n".join(lines)
                return report
            var reconciled: Dictionary = Gateway.reconcile_receipt(slot_path,
                vaccine_id, generation, run_id, receipt_id,
                str(pending_command.get("offer_id", "")))
            if bool(reconciled.get("receipt_committed", false)):
                report["receipt_status"] = "committed"
                lines.append("보상 영수증: E 저장 확인됨. 다시 지급하지 않아.")
                if str(pending_command.get("action", "")) == "plugin_picked" \
                        and str(pending_command.get("plugin_id", "")) == Rare.SOURCE_ID:
                    var saved: Dictionary = inspected.get("snapshot", {})
                    var vaccine: Variant = saved.get("vaccine")
                    var active: Variant = vaccine.get("mission") if vaccine is Dictionary else null
                    var bonus: Dictionary = {}
                    if active is Dictionary:
                        bonus = active.get("environment_rolls", {}).get(
                            Rare.KEY, {}).get(receipt_id, {})
                    var bonus_id: String = str(bonus.get("plugin_id", ""))
                    var bonus_receipt: String = str(bonus.get("bonus_receipt", ""))
                    var earned: Dictionary = active.get("obtained_plugins", {}) if active is Dictionary else {}
                    var canonical: Dictionary = Catalog.find_by_id(bonus_id, Catalog.entries())
                    var receipts: Dictionary = vaccine.get("receipts", {}) if vaccine is Dictionary else {}
                    if not bonus.is_empty() and Rare.valid(active) \
                            and not canonical.is_empty() and str(canonical.get("rarity", "")) == "rare" \
                            and int(bonus.get("level", 0)) >= 1 \
                            and int(bonus.get("level", 0)) <= int(canonical.get("max_level", 0)) \
                            and bonus_receipt == receipt_id + ":rare:0" \
                            and str(bonus.get("rarity", "")) == "rare" \
                            and str(bonus.get("rng_policy", "")) == Rare.RNG_POLICY \
                            and receipts.has(receipt_id) and receipts.has(bonus_receipt) \
                            and earned.has(Rare.SOURCE_ID) and earned.has(bonus_id) \
                            and int(earned[Rare.SOURCE_ID].get("level", 0)) >= int(pending_command.get("level", 1)) \
                            and int(earned[bonus_id].get("level", 0)) >= int(bonus.get("level", -1)):
                        report["rare_bonus_verified"] = true
                        lines.append("레어 보너스: E 원본·파생 영수증 모두 검증됨 (" + bonus_id + ").")
                    else:
                        lines.append("레어 보너스: 원본과 파생 영수증을 함께 검증하지 못했어. 수동 점검 필요.")
            elif bool(reconciled.get("receipt_definitely_absent", false)):
                report["receipt_status"] = "absent"
                lines.append("보상 영수증: 현재 유효한 의뢰에서 미기록 확인. 자동 재시도하지 않아.")
            else:
                report["receipt_status"] = "unverified"
                lines.append("보상 영수증: 확인 불가 (" + str(reconciled.get("reason", "unknown")) + "). 재지급 금지.")
    report["text"] = "\n".join(lines)
    return report

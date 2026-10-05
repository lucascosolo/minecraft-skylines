"""Generate protocol/vectors/*.json from the reference codec.

    python3 protocol/reference/gen_vectors.py

Each implementation's tests decode every `hex` and compare with `fields`, and encode `fields`
and compare with `hex`. The coordinate vectors are checked here against direction vectors
(Unity's forward vector vs Minecraft's view vector), so they do not just restate the formula.
"""
from __future__ import annotations

import json
import math
import uuid
from pathlib import Path

import skbridge as sb

OUT = Path(__file__).resolve().parent.parent / "vectors"

SAVE_ID = uuid.UUID("0f1e2d3c-4b5a-6978-8796-a5b4c3d2e1f0")


def frames() -> list[dict]:
    vs = []

    def add(name, type_, fields, payload):
        vs.append({"name": name, "type": type_, "fields": fields, "hex": sb.frame(type_, payload).hex()})

    h = sb.Hello("minecraft-skylines", 1, 0, "Minecraft 26.3 (Fabric)", "0.1.0", 0x0123456789ABCDEF)
    add("hello", sb.HELLO, {"magic": h.magic, "bridgeVersion": 1, "appProtocol": h.app_protocol,
        "appMajor": 1, "appMinor": 0, "peerName": h.peer_name, "peerVersion": h.peer_version,
        "sessionNonce": str(h.session_nonce)}, h.encode())

    h2 = sb.Hello("minecraft-skylines", 1, 7, "Zé ✓ 名前", "", 0xFFFFFFFFFFFFFFFF)
    add("hello_unicode_maxnonce", sb.HELLO, {"magic": h2.magic, "bridgeVersion": 1,
        "appProtocol": h2.app_protocol, "appMajor": 1, "appMinor": 7, "peerName": h2.peer_name,
        "peerVersion": "", "sessionNonce": str(h2.session_nonce)}, h2.encode())

    w = sb.Welcome(True, 0, "", "minecraft-skylines", 1, 0, "Cities: Skylines 1.21.1-f5", "0.1.0",
                   1000, 5000, 42)
    add("welcome_accepted", sb.WELCOME, {"accepted": True, "rejectCode": 0, "rejectReason": "",
        "bridgeVersion": 1, "appProtocol": w.app_protocol, "appMajor": 1, "appMinor": 0,
        "peerName": w.peer_name, "peerVersion": "0.1.0", "heartbeatIntervalMs": 1000,
        "peerTimeoutMs": 5000, "sessionId": "42"}, w.encode())

    r = sb.Welcome(False, sb.REJECT_APP_MAJOR, "app major 2 != 1", "minecraft-skylines", 1, 3,
                   "Cities: Skylines", "0.2.0", 1000, 5000, 0)
    add("welcome_rejected", sb.WELCOME, {"accepted": False, "rejectCode": 3,
        "rejectReason": r.reject_reason, "bridgeVersion": 1, "appProtocol": r.app_protocol,
        "appMajor": 1, "appMinor": 3, "peerName": r.peer_name, "peerVersion": "0.2.0",
        "heartbeatIntervalMs": 1000, "peerTimeoutMs": 5000, "sessionId": "0"}, r.encode())

    hb = sb.Heartbeat(7, 123456789012)
    add("heartbeat", sb.HEARTBEAT, {"seq": 7, "senderUptimeMs": "123456789012"}, hb.encode())

    g = sb.Goodbye(sb.BYE_SHUTTING_DOWN, "game exiting")
    add("goodbye", sb.GOODBYE, {"code": 1, "reason": "game exiting"}, g.encode())

    hs = sb.HostStatus(0b0001, "New Tokyo", SAVE_ID, "1.21.1-f5")
    add("host_status", sb.HOST_STATUS, {"flags": 1, "cityName": "New Tokyo",
        "saveId": str(SAVE_ID), "gameVersion": "1.21.1-f5"}, hs.encode())

    gs = sb.GuestStatus(0b0011, "skylines-test", uuid.UUID(int=0))
    add("guest_status_unpaired", sb.GUEST_STATUS, {"flags": 3, "worldName": "skylines-test",
        "pairedSaveId": str(uuid.UUID(int=0))}, gs.encode())

    # ---- 1.1 (milestone 2). f32 values are chosen to be exactly representable.
    e = sb.EnterPlayerMode(3, 120.5, 64.0, -2048.25, -90.0, 12.5, 7)
    add("enter_player_mode", sb.ENTER_PLAYER_MODE, {"teleportSeq": 3, "x": 120.5, "y": 64.0,
        "z": -2048.25, "yaw": -90.0, "pitch": 12.5, "collisionEpoch": 7}, e.encode())

    add("exit_player_mode", sb.EXIT_PLAYER_MODE, {"reason": "player pressed Esc"},
        sb.ExitPlayerMode("player pressed Esc").encode())

    inp = sb.Input(179.5, -45.25, [sb.InputEvent(sb.IN_KEY, 1, 87), sb.InputEvent(sb.IN_BUTTON, 0, 1),
                                   sb.InputEvent(sb.IN_SCROLL, 0, -240), sb.InputEvent(sb.IN_TEXT, 0, 0x00E9),
                                   sb.InputEvent(sb.IN_RELEASE_ALL, 0, 0)])
    add("input", sb.INPUT, {"yaw": 179.5, "pitch": -45.25, "events": [
        {"kind": 1, "action": 1, "code": 87}, {"kind": 2, "action": 0, "code": 1},
        {"kind": 3, "action": 0, "code": -240}, {"kind": 4, "action": 0, "code": 233},
        {"kind": 5, "action": 0, "code": 0}]}, inp.encode())

    add("input_empty", sb.INPUT, {"yaw": 0.0, "pitch": 0.0, "events": []}, sb.Input(0.0, 0.0, []).encode())

    tris = [([0.0, 40.0, 0.0, 0.0, 40.0, 2.0, 2.0, 40.5, 0.0], 0x0001),
            ([-16.0, 61.25, -32.0, -14.0, 61.25, -32.0, -16.0, 61.25, -30.0], 0x0006)]
    add("collision_region", sb.COLLISION_REGION, {"epoch": 7, "regionX": -1, "regionZ": -2,
        "tris": [{"v": t[0], "flags": t[1]} for t in tris]}, sb.CollisionRegion(7, -1, -2, tris).encode())

    add("collision_region_empty", sb.COLLISION_REGION, {"epoch": 7, "regionX": 539, "regionZ": -540,
        "tris": []}, sb.CollisionRegion(7, 539, -540, []).encode())

    add("collision_reset", sb.COLLISION_RESET, {"epoch": 8}, sb.CollisionReset(8).encode())

    ps = {"flags": 0b10000011, "teleportAck": 3, "x": 120.5, "y": 64.0, "z": -2048.25,
          "eyeX": 120.5, "eyeY": 65.62, "eyeZ": -2048.25, "yaw": -90.0, "pitch": 12.5, "fovDeg": 70.0,
          "tickSeq": 123456, "prevX": 120.25, "prevY": 64.0, "prevZ": -2048.0,
          "curX": 120.75, "curY": 64.0, "curZ": -2048.5, "prevEyeHeight": 1.5, "curEyeHeight": 1.625,
          "partialTick": 0.5, "tickMs": 50.0}
    add("player_state", sb.PLAYER_STATE, ps, sb.PlayerState(ps).encode())

    # Forward compatibility: trailing bytes after the last field must be accepted and ignored.
    add("heartbeat_trailing_bytes", sb.HEARTBEAT, {"seq": 1, "senderUptimeMs": "0"},
        sb.Heartbeat(1, 0).encode() + b"\xAA\xBB")
    return vs


def invalid_frames() -> list[dict]:
    """Byte strings every decoder must reject (with a protocol error, not a crash)."""
    def bad(name, data, why):
        return {"name": name, "hex": data.hex(), "why": why}
    return [
        bad("nonzero_flags", sb.frame(sb.HEARTBEAT, sb.Heartbeat(1, 0).encode(), flags=1), "flags must be 0"),
        bad("oversize", sb.HEADER.pack(sb.MAX_PAYLOAD + 1, sb.HEARTBEAT, 0), "payloadLength > 16 MiB"),
        bad("truncated_heartbeat", sb.frame(sb.HEARTBEAT, b"\x01\x00\x00"), "payload ends before last field"),
        bad("bool_out_of_range", sb.frame(sb.WELCOME, b"\x02" + sb.Welcome(True, 0, "", "x", 1, 0, "", "", 1, 1, 1).encode()[1:]), "bool must be 0 or 1"),
        bad("string_overruns", sb.frame(sb.GOODBYE, b"\x00\x00\xFF\x00abc"), "string length past payload end"),
        bad("invalid_utf8", sb.frame(sb.GOODBYE, b"\x00\x00\x02\x00\xC3\x28"), "string not UTF-8"),
        bad("unknown_bridge_type", sb.frame(0x0042, b""), "types below 0x0100 are reserved"),
    ]


def wrap180(a: float) -> float:
    a = math.fmod(a, 360.0)
    if a >= 180.0:
        a -= 360.0
    elif a < -180.0:
        a += 360.0
    return a


def unity_forward(euler_x: float, euler_y: float) -> tuple[float, float, float]:
    # Quaternion.Euler(x, y, 0) * Vector3.forward
    p, y = math.radians(euler_x), math.radians(euler_y)
    return (math.sin(y) * math.cos(p), -math.sin(p), math.cos(y) * math.cos(p))


def mc_view(yaw: float, pitch: float) -> tuple[float, float, float]:
    # Entity.calculateViewVector(xRot = pitch, yRot = yaw)
    f = math.cos(-yaw * math.pi / 180 - math.pi)
    f1 = math.sin(-yaw * math.pi / 180 - math.pi)
    f2 = -math.cos(-pitch * math.pi / 180)
    f3 = math.sin(-pitch * math.pi / 180)
    return (f1 * f2, f3, f * f2)


def coords() -> list[dict]:
    cases = [
        (0, 0, 0, 0, 0), (100, 40, 200, 0, 90), (-8640, 1024, 8640, 30, 270),
        (8639.5, 0.25, -8639.5, 359, 359.5), (12.5, 60, -3.75, 315, 45),
        (-1, 2, -3, 89.9, 180), (0.001, 999.999, -0.001, 270.1, 0.5),
    ]
    out = []
    for x, y, z, ex, ey in cases:
        yaw, pitch = wrap180(ey + 180.0), wrap180(ex)
        fu = unity_forward(ex, ey)
        fm = mc_view(yaw, pitch)
        mapped = (fu[0], fu[1], -fu[2])
        assert all(abs(a - b) < 1e-9 for a, b in zip(mapped, fm)), (x, y, z, ex, ey, mapped, fm)
        out.append({"cs": {"x": x, "y": y, "z": z, "eulerX": ex, "eulerY": ey},
                    "mc": {"x": x, "y": y, "z": -z if z != 0 else 0.0, "yaw": yaw, "pitch": pitch}})
    return out


def main() -> None:
    OUT.mkdir(parents=True, exist_ok=True)
    (OUT / "frames.json").write_text(json.dumps({"valid": frames(), "invalid": invalid_frames()},
                                                indent=2, ensure_ascii=False) + "\n")
    (OUT / "coords.json").write_text(json.dumps({"yOffset": 0, "tolerance": 1e-9, "cases": coords()},
                                                indent=2) + "\n")
    print(f"wrote {OUT/'frames.json'} and {OUT/'coords.json'}")


if __name__ == "__main__":
    main()

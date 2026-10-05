"""Cross-language interop: a real host IUT (C#) against a real guest IUT (Java), no Python peer.

    python3 tools/interop.py --host "<host command>" --guest "<guest command>" [--junit out.xml]

Both commands follow the IUT contract in protocol/reference/conformance.py. Checks: handshake,
heartbeats keep the link alive past several timeout periods, guest GOODBYE is seen by the host,
the guest reconnects, host GOODBYE is seen by the guest.
"""
from __future__ import annotations

import argparse
import shlex
import socket
import sys
import time
import traceback
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "protocol" / "reference"))
from conformance import Iut, write_junit  # noqa: E402

APP, HB_MS, TIMEOUT_MS = "skbr-conformance", 200, 1000


def free_port() -> int:
    with socket.socket() as s:
        s.bind(("127.0.0.1", 0))
        return s.getsockname()[1]


def count(iut: Iut, prefix: str) -> int:
    return len(iut.events(prefix))


def scenario(host_cmd: list[str], guest_cmd: list[str]) -> None:
    port = free_port()
    host = Iut(host_cmd, ["host", "--port", str(port), "--heartbeat-ms", str(HB_MS),
                          "--timeout-ms", str(TIMEOUT_MS), "--app", APP])
    guest = None
    try:
        guest_args = ["guest", "--port", str(port), "--app", APP, "--major", "1", "--retry-ms", "200"]
        guest = Iut(guest_cmd, guest_args)
        host.wait_event("state connected", 5)
        guest.wait_event("state connected", 5)

        time.sleep(3 * TIMEOUT_MS / 1000)  # three timeout periods on heartbeats alone
        assert count(host, "disconnected") == 0, "host dropped a heartbeating guest:\n" + "\n".join(host.lines[-15:])
        assert count(guest, "disconnected") == 0, "guest dropped a heartbeating host:\n" + "\n".join(guest.lines[-15:])

        guest.stop()
        host.wait_event("disconnected cause=peer_goodbye", 3)

        guest = Iut(guest_cmd, guest_args)
        deadline = time.monotonic() + 5
        while count(host, "state connected") < 2:
            assert time.monotonic() < deadline, "host did not accept the reconnecting guest"
            time.sleep(0.05)
        guest.wait_event("state connected", 5)

        host.stop()
        guest.wait_event("disconnected cause=peer_goodbye", 3)
    finally:
        for p in (guest, host):
            if p and p.proc.poll() is None:
                p.stop()


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--host", required=True)
    ap.add_argument("--guest", required=True)
    ap.add_argument("--junit")
    a = ap.parse_args()
    t0 = time.monotonic()
    err = None
    try:
        scenario(shlex.split(a.host), shlex.split(a.guest))
    except Exception:
        err = traceback.format_exc()
    dt = time.monotonic() - t0
    print(f"{'PASS' if err is None else 'FAIL'} interop/cs-host_java-guest ({dt:.2f}s)")
    if err:
        print(err)
    if a.junit:
        write_junit(a.junit, "interop", [("cs-host_java-guest", dt, err)])
    return 1 if err else 0


if __name__ == "__main__":
    sys.exit(main())

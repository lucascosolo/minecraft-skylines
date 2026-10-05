# SKBR bridge protocol, version 1

The bridge is the game-agnostic transport between a **host** (a Cities: Skylines mod) and a
**guest** (another game's mod, here Minecraft). It knows nothing about Minecraft: it carries a
versioned handshake, liveness, a clean goodbye, and opaque application frames. A future
"Cities: Skylines + X" project reuses this layer unchanged and defines its own app protocol on
top (see `minecraft-skylines-v1.md` for this project's).

Implementations, all of which must pass `protocol/vectors/` and `protocol/reference/conformance.py`:

| Language | Path | Notes |
|---|---|---|
| Python | `protocol/reference/skbridge.py` | Reference codec and scriptable fake peer. The spec wins over it. |
| C# (.NET 3.5 + .NET 10) | `cs1/src/Skylines.Bridge/` | Host side, loaded by the CS1 mod |
| Java 21+ | `minecraft/bridge/src/main/java/dev/mcskylines/bridge/` | Guest side, no Minecraft imports |

## Transport

- TCP over IPv4 loopback only. The host listens on `127.0.0.1:47615` by default (configurable);
  it never binds a non-loopback address.
- The guest connects, retrying every 1 s for the first 10 attempts, then every 5 s.
- `TCP_NODELAY` is set on both ends.
- One active guest per host. A second connection that completes a valid HELLO gets
  `WELCOME(accepted=0, rejectCode=BUSY)` and is closed.

## Framing

Every frame is an 8-byte header followed by the payload. All integers are little-endian.

| Offset | Type | Field |
|---|---|---|
| 0 | u32 | `payloadLength` (bytes after the header) |
| 4 | u16 | `type` |
| 6 | u16 | `flags`, must be 0 in v1 (non-zero is a protocol error) |
| 8 | bytes | payload |

`payloadLength` above `16777216` (16 MiB) is a protocol error; the receiver closes without
reading the payload.

### Primitive encodings

| Name | Encoding |
|---|---|
| `u8 u16 u32 u64 i32 i64` | little-endian two's complement |
| `f32 f64` | little-endian IEEE 754 |
| `bool` | `u8`, 0 or 1; anything else is a protocol error |
| `string` | `u16` byte length, then that many bytes of UTF-8 (no terminator) |
| `uuid` | 16 raw bytes in RFC 4122 order (the textual order). Note: .NET `Guid.ToByteArray()` is *not* this order. |

A payload with bytes left over after its last field is valid (fields may be appended within a
major version); a payload that ends before its last field is a protocol error.

## Message types

`0x0000`-`0x00FF` belong to the bridge. `0x0100`-`0xFFFF` are application frames: the bridge
delivers them unchanged and never interprets them. An unknown bridge type is a protocol error;
an unknown application type is delivered to the application, which must ignore it (so an app
minor version can add messages).

### `0x0001 HELLO` (guest → host, first frame on the connection)

| Type | Field | Value |
|---|---|---|
| u32 | `magic` | `0x52424B53` (bytes `53 4B 42 52`, "SKBR") |
| u16 | `bridgeVersion` | `1` |
| string | `appProtocol` | e.g. `minecraft-skylines` |
| u16 | `appMajor` | |
| u16 | `appMinor` | |
| string | `peerName` | human-readable, e.g. `Minecraft 26.3 (Fabric)` |
| string | `peerVersion` | the guest mod's version |
| u64 | `sessionNonce` | random per connection attempt |

### `0x0002 WELCOME` (host → guest, reply to HELLO)

| Type | Field | Notes |
|---|---|---|
| bool | `accepted` | |
| u16 | `rejectCode` | 0 when accepted, see table |
| string | `rejectReason` | empty when accepted; human-readable, shown to the user |
| u16 | `bridgeVersion` | the host's |
| string | `appProtocol` | the host's |
| u16 | `appMajor` | the host's |
| u16 | `appMinor` | the host's; both sides use `min(guest, host)` |
| string | `peerName` | e.g. `Cities: Skylines 1.21.1-f5` |
| string | `peerVersion` | the host mod's version |
| u32 | `heartbeatIntervalMs` | default 1000 |
| u32 | `peerTimeoutMs` | default 5000 |
| u64 | `sessionId` | host-assigned, non-zero when accepted |

| `rejectCode` | Name | When |
|---|---|---|
| 1 | `BRIDGE_VERSION` | `bridgeVersion` differs |
| 2 | `APP_PROTOCOL` | `appProtocol` differs |
| 3 | `APP_MAJOR` | `appMajor` differs |
| 4 | `BUSY` | another guest is connected |
| 5 | `NOT_READY` | the host refuses guests right now (e.g. disabled by the user) |

After a rejection the host closes the connection. A guest that was rejected for 1, 2 or 3 stops
retrying and reports the reason; for 4 and 5 it keeps retrying on the normal schedule.

### `0x0003 HEARTBEAT` (both directions, after the handshake)

| Type | Field |
|---|---|
| u32 | `seq` (per-sender counter, starts at 1) |
| u64 | `senderUptimeMs` (monotonic milliseconds since the sender's bridge started) |

### `0x0004 GOODBYE` (both directions)

| Type | Field |
|---|---|
| u16 | `code` |
| string | `reason` |

| `code` | Name | Meaning |
|---|---|---|
| 0 | `NORMAL` | the user or the app ended the session |
| 1 | `SHUTTING_DOWN` | the game is exiting or the mod is being disabled |
| 2 | `PROTOCOL_ERROR` | the sender received something invalid |
| 3 | `TIMEOUT` | the sender stopped hearing from the peer |
| 4 | `BACKPRESSURE` | the sender's outbound queue overflowed |

After sending GOODBYE the sender stops sending, shuts down its write side and closes within 1 s.
A receiver of GOODBYE closes immediately and reports the code and reason to its application. A
GOODBYE before the handshake completes is valid in both directions.

## Rules

1. The guest sends HELLO immediately after connecting. The host replies within 5 s or the guest
   closes. A host that has not received HELLO within 5 s closes.
2. If `magic` is wrong, the host closes **without** replying: the peer is not a bridge.
3. Before the handshake completes, any frame except HELLO, WELCOME and GOODBYE is a protocol
   error. Application frames are only valid after an accepted WELCOME.
4. Each side sends HEARTBEAT every `heartbeatIntervalMs` after the handshake. Receiving **any**
   frame resets the peer's liveness timer. No frame for `peerTimeoutMs` means the peer is dead:
   send `GOODBYE(TIMEOUT)` best-effort and close.
5. On a protocol error, send `GOODBYE(PROTOCOL_ERROR)` best-effort and close.
6. Socket I/O and heartbeats run on background threads. A game thread never blocks on the
   bridge: it enqueues outbound frames and drains a queue of inbound events once per frame.
   Liveness therefore measures the peer *process*, not its main thread (a CS1 save load stalls
   the main thread for many seconds; that must not drop the link).
7. The outbound queue is bounded (default 64 MiB of payload). Overflow sends
   `GOODBYE(BACKPRESSURE)` and closes. Coalescing per-frame state is the application's job.

## Events an implementation reports to its application

`StateChanged(state, detail)` with `state` in `disconnected`, `listening` (host),
`connecting` (guest), `handshaking`, `connected`, `rejected`, `closing`; `Message(type, payload)`;
`Disconnected(cause, code, reason)` where `cause` is one of `peer_goodbye`, `local_goodbye`,
`timeout`, `protocol_error`, `connection_lost`, `rejected`, `backpressure`.

A GOODBYE received during the handshake is reported as `peer_goodbye`; a guest that gets no
WELCOME within 5 s reports `timeout`. A host that rejects or
drops a *second* connection (BUSY, bad magic, bad HELLO) does not report `Disconnected`: its
active session is unaffected; it only logs the rejection.

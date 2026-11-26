# Protocol

The watch is a BLE *central*; the Windows app is a BLE *peripheral* (GATT server).

| Item | Value |
|------|-------|
| Service UUID | `7d9b0001-5a4c-4f6e-9a3b-2c8f1e0d4b11` |
| Command characteristic | `7d9b0002-5a4c-4f6e-9a3b-2c8f1e0d4b11` (write / write without response) |

## Commands

| Byte | Meaning | Key sent by the PC |
|------|---------|--------------------|
| `0x00` | Hello: sent on connect and every 10 s as a heartbeat | none |
| `0x01` | Next slide | Right arrow (or Page Down) |
| `0x02` | Previous slide | Left arrow (or Page Up) |
| `0x03` | Start slideshow | F5 |
| `0x04` | End slideshow | Esc |
| `0x05` | Black screen toggle | B |

## Packet format (13 bytes)

Every write is signed with the PIN:

```
byte  0      command
bytes 1-4    counter, unsigned 32-bit, big-endian
bytes 5-12   tag = first 8 bytes of HMAC-SHA256(key, bytes 0-4)
key          = SHA-256( UTF-8("PPTRemote-v1:" + PIN) )
```

Rules the PC applies:

1. Length must be exactly 13 bytes.
2. The tag must match (compared in constant time).
3. The counter must be **greater** than the last accepted counter (stops replays). The PC saves it to disk.
4. After 5 bad packets within 60 seconds, all writes are refused for 60 seconds.

A refused write is answered with ATT error `Insufficient Authorization` (when the write expects a response). Refused packets never change the "watch connected" state.

The watch keeps its counter in storage and seeds it from its clock (`max(stored + 1, seconds since 1970)`), so reinstalling the app can't wind it back.

PINs are 6 to 12 digits. A short PIN stops casual and drive-by devices; it does **not** stop someone who records a genuine packet and tries every PIN offline, because the Bluetooth link itself is not encrypted. Use 10 or 12 digits if that matters to you.

## Test vector

PIN `123456`, command `0x01`, counter `1767225600` gives this packet (hex):

```
016955B900F4450A08DC1A3B76
```

The Windows app checks this at startup ("Security self-test passed" in the activity log).

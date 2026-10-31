# Serial protocol v1

USB serial, **115200 baud, 8N1**. One JSON object per line, terminated by `\n` (a trailing `\r` is tolerated). Lines longer than 512 characters are discarded. ASCII only.

The parser is `src/sensor/protocol.ts`. Every field is type- and range-checked, and a line that fails is counted as *malformed* and ignored (see the diagnostics panel).

## Device → browser

### `imu`: fused attitude, 50 Hz

```json
{"t":"imu","s":1234,"ms":56789,"p":-3.25,"r":10.50,"y":-12.00,"pr":1.5,"rr":-2.0,"yr":0.3,"a":1.002}
```

| Key | Meaning | Unit / range |
| --- | --- | --- |
| `s` | Sequence number, +1 per sample, wraps at 2³² | integer |
| `ms` | Device uptime | ms |
| `p` | Pitch, **+ nose up** | deg, −90…90 |
| `r` | Roll, **+ right wing down** | deg, −180…180 |
| `y` | Relative yaw, **+ nose right**; zero at calibration or `ZEROYAW`; **drifts** | deg, −180…180 |
| `pr`, `rr`, `yr` | Pitch, roll and yaw (Euler) rates | deg/s, ±2000 |
| `a` | Accelerometer magnitude (≈1 at rest; used to judge vibration) | g, 0…16 |

Keys are short because 50 Hz × ~100 bytes uses about 45% of a 115200-baud link.

Gaps in `s` are counted as dropped samples. A large jump or a backwards step means the device restarted, which isn't counted as loss.

### `status`: device state changes

```json
{"t":"status","state":"ready","msg":"gyro bias calibrated"}
```

`state` is one of:

| State | Meaning | Browser behavior |
| --- | --- | --- |
| `calibrating` | Measuring gyro bias; no `imu` lines are sent | Controls held at zero |
| `ready` | Streaming valid attitude | Controls usable |
| `motion_during_cal` | Moved during calibration, retrying (keeps retrying while the controller is really moving) | Controls held at zero |
| `cal_failed` | No fully still sample after 5 tries; used the steadiest one (hand tremor only, gyro under 3 °/s). Accel scale unchanged | Data used, warning shown |
| `sensor_error` | MPU-6050 not responding | Controls held at zero, error shown |

### `hello`: sent at boot and in reply to `HELLO`

```json
{"t":"hello","fw":"mpu-flight/1.0.0","proto":1,"rate":50}
```

A `hello` arriving mid-session means the board rebooted, and it's counted as a restart.

### `ack`: command acknowledgment

```json
{"t":"ack","cmd":"CAL"}
{"t":"ack","cmd":"?","err":"unknown command"}
```

## Browser → device

Plain text commands, one per line:

| Command | Effect |
| --- | --- |
| `CAL` | Re-run gyro bias calibration (hold still for about 2 s) |
| `ZEROYAW` | Reset relative yaw to 0 |
| `HELLO` | Re-send the `hello` line |

## Link health (browser side)

| Condition | State | Controls |
| --- | --- | --- |
| Port open, no valid `imu` yet | `awaiting` | 0 |
| `imu` within the last 250 ms | `live` | ramp up to full authority over 1 s |
| No `imu` for more than 250 ms | `stale` | fade to 0 over 0.35 s |
| No `imu` for more than 2 s | `lost` | 0 |
| No bytes at all for 4 s after opening | `awaiting` plus a hint about port, baud rate and firmware | 0 |
| Port closed or USB unplugged | `disconnected` | 0; auto-reopens if the same port is plugged back in |

## Adding another transport

`src/sensor/transport/types.ts` defines `SensorTransport` (`open`, `close`, `send`, plus `onLine` and `onClose` callbacks). A WebSocket transport for an ESP32 on Wi-Fi only has to deliver the same lines. Parsing, calibration and mapping stay unchanged.

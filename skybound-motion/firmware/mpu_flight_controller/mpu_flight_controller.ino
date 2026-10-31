// MPU-6050 motion flight controller for ESP32.
//
// Reads the MPU-6050 over I2C at 200 Hz, removes gyro bias, fuses gyro and
// accelerometer with a complementary filter, and streams newline-delimited
// JSON (NDJSON) over USB serial at 50 Hz. Protocol: docs/PROTOCOL.md.
//
// No third-party libraries: only Wire (bundled with the ESP32 Arduino core).
//
// Axis convention (aircraft / aerospace body frame, angles in degrees):
//   pitch  + = nose up    (front edge of the board raised)
//   roll   + = right wing down (right edge of the board lowered)
//   yaw    + = nose right (relative, integrated gyro; drifts, no magnetometer)
//
// Default mounting: board flat, components up, the MPU-6050's printed +X arrow
// pointing forward (toward the screen). Change MOUNT_* below if yours differs.

#include <Wire.h>
#include <math.h>

// ---------- Configuration ----------
static const char *FW_VERSION = "mpu-flight/1.1.2";
static const int PROTO_VERSION = 1;

static const int PIN_SDA = 21;
static const int PIN_SCL = 22;
static const uint32_t I2C_HZ = 400000;
static const uint32_t SERIAL_BAUD = 115200;

static const uint8_t MPU_ADDR = 0x68;        // 0x69 if AD0 is pulled high
static const uint32_t SAMPLE_US = 5000;      // 200 Hz fusion loop
static const uint8_t SAMPLES_PER_SEND = 4;   // 200 / 4 = 50 Hz output

static const float FILTER_TAU_S = 0.5f;      // complementary filter time constant
static const float ACCEL_TRUST_BAND_G = 0.15f; // ignore accel when |a| is outside 1 g +/- this

static const uint16_t CAL_SAMPLES = 400;     // 2 s at 200 Hz
static const float CAL_MAX_GYRO_STD_DPS = 0.8f;
static const float CAL_MAX_ACCEL_STD_G = 0.02f;
static const uint8_t CAL_MAX_ATTEMPTS = 5;         // strict attempts before considering the fallback
static const float CAL_FALLBACK_GYRO_STD_DPS = 3.0f; // hand tremor yes, actual movement no
// Clone MPU-6050s often read gravity 10-20% high or low. The rest magnitude
// measured during calibration becomes "1 g" if it falls inside this band.
static const float ACCEL_SCALE_MIN = 0.85f, ACCEL_SCALE_MAX = 1.25f;

// Mounting: which chip axis points forward / left / up, with sign.
// Indices: 0 = chip X, 1 = chip Y, 2 = chip Z.
// Default: chip X forward, chip Y left, chip Z up.
static const int MOUNT_FWD_AXIS = 0, MOUNT_FWD_SIGN = 1;
static const int MOUNT_LEFT_AXIS = 1, MOUNT_LEFT_SIGN = 1;
static const int MOUNT_UP_AXIS = 2, MOUNT_UP_SIGN = 1;

// ---------- MPU-6050 registers ----------
static const uint8_t REG_SMPLRT_DIV = 0x19;
static const uint8_t REG_CONFIG = 0x1A;
static const uint8_t REG_GYRO_CONFIG = 0x1B;
static const uint8_t REG_ACCEL_CONFIG = 0x1C;
static const uint8_t REG_ACCEL_XOUT_H = 0x3B;
static const uint8_t REG_PWR_MGMT_1 = 0x6B;
static const uint8_t REG_WHO_AM_I = 0x75;

static const float ACCEL_LSB_PER_G = 8192.0f;  // +/- 4 g
static const float GYRO_LSB_PER_DPS = 65.5f;   // +/- 500 deg/s

static const float RAD2DEG = 57.2957795f;
static const float DEG2RAD = 0.0174532925f;

// ---------- State ----------
enum class State { SensorError, Calibrating, Ready };
static State state = State::SensorError;

struct Vec3 { float x, y, z; };
// Declared up here because the Arduino builder hoists function prototypes above later types.
struct CalAttempt {
  Vec3 bias;
  Vec3 accMean;   // chip frame, raw g
  float accMag;   // mean raw |a|
  float gyroStd;  // worst axis, deg/s
  float accStd;   // of |a|, g
};
static Vec3 gyroBias = {0, 0, 0};       // chip frame, deg/s
static float accelScale = 1.0f;         // raw |a| at rest, in nominal g
static float pitchDeg = 0, rollDeg = 0, yawDeg = 0;
static uint32_t seq = 0;
static uint32_t lastSampleUs = 0;
static uint8_t sampleCounter = 0;
static uint8_t consecutiveI2cErrors = 0;
static uint32_t lastProbeMs = 0;
static char cmdBuf[32];
static uint8_t cmdLen = 0;

// Latest fused rates (aircraft frame, deg/s) and accel magnitude for telemetry.
static float rollRate = 0, pitchRate = 0, yawRate = 0, accelMagG = 1;

// ---------- I2C helpers ----------
static bool writeReg(uint8_t reg, uint8_t val) {
  Wire.beginTransmission(MPU_ADDR);
  Wire.write(reg);
  Wire.write(val);
  return Wire.endTransmission() == 0;
}

static bool readRegs(uint8_t reg, uint8_t *buf, uint8_t len) {
  Wire.beginTransmission(MPU_ADDR);
  Wire.write(reg);
  if (Wire.endTransmission(false) != 0) return false;
  if (Wire.requestFrom(MPU_ADDR, len) != len) return false;
  for (uint8_t i = 0; i < len; i++) buf[i] = Wire.read();
  return true;
}

// Raw sample in chip frame: accel in g, gyro in deg/s (bias not removed).
static bool readRaw(Vec3 &accel, Vec3 &gyro) {
  uint8_t b[14];
  if (!readRegs(REG_ACCEL_XOUT_H, b, 14)) return false;
  auto s16 = [&](int i) { return (int16_t)((b[i] << 8) | b[i + 1]); };
  accel = {s16(0) / ACCEL_LSB_PER_G, s16(2) / ACCEL_LSB_PER_G, s16(4) / ACCEL_LSB_PER_G};
  gyro = {s16(8) / GYRO_LSB_PER_DPS, s16(10) / GYRO_LSB_PER_DPS, s16(12) / GYRO_LSB_PER_DPS};
  return true;
}

static float axisOf(const Vec3 &v, int axis) { return axis == 0 ? v.x : (axis == 1 ? v.y : v.z); }

// Chip frame -> controller frame (x forward, y left, z up).
static Vec3 toControllerFrame(const Vec3 &v) {
  return {MOUNT_FWD_SIGN * axisOf(v, MOUNT_FWD_AXIS),
          MOUNT_LEFT_SIGN * axisOf(v, MOUNT_LEFT_AXIS),
          MOUNT_UP_SIGN * axisOf(v, MOUNT_UP_AXIS)};
}

// ---------- Serial output ----------
static void sendStatus(const char *st, const char *msg) {
  Serial.printf("{\"t\":\"status\",\"state\":\"%s\",\"msg\":\"%s\"}\n", st, msg);
}

static void sendHello() {
  Serial.printf("{\"t\":\"hello\",\"fw\":\"%s\",\"proto\":%d,\"rate\":%u}\n", FW_VERSION,
                PROTO_VERSION, (unsigned)(1000000UL / (SAMPLE_US * SAMPLES_PER_SEND)));
}

static void sendImu() {
  Serial.printf(
      "{\"t\":\"imu\",\"s\":%lu,\"ms\":%lu,\"p\":%.2f,\"r\":%.2f,\"y\":%.2f,"
      "\"pr\":%.1f,\"rr\":%.1f,\"yr\":%.1f,\"a\":%.3f}\n",
      (unsigned long)seq++, (unsigned long)millis(), pitchDeg, rollDeg, yawDeg, pitchRate,
      rollRate, yawRate, accelMagG);
}

// ---------- Sensor setup and calibration ----------
static bool initSensor() {
  uint8_t who = 0;
  if (!readRegs(REG_WHO_AM_I, &who, 1)) return false;
  // Genuine MPU-6050 answers 0x68; common clones/derivatives answer 0x70, 0x72, 0x98.
  if (who != 0x68 && who != 0x70 && who != 0x72 && who != 0x98) return false;
  bool ok = writeReg(REG_PWR_MGMT_1, 0x80);  // device reset
  delay(100);
  ok = ok && writeReg(REG_PWR_MGMT_1, 0x01);   // wake, PLL with X gyro reference
  delay(10);
  ok = ok && writeReg(REG_CONFIG, 0x03);       // DLPF ~44 Hz accel / 42 Hz gyro
  ok = ok && writeReg(REG_SMPLRT_DIV, 0x04);   // 1 kHz / (1 + 4) = 200 Hz
  ok = ok && writeReg(REG_GYRO_CONFIG, 0x08);  // +/- 500 deg/s
  ok = ok && writeReg(REG_ACCEL_CONFIG, 0x08); // +/- 4 g
  return ok;
}

// Attitude from gravity alone (controller frame).
static void accelAttitude(const Vec3 &a, float &pitch, float &roll) {
  pitch = atan2f(a.x, sqrtf(a.y * a.y + a.z * a.z)) * RAD2DEG;
  roll = atan2f(a.y, a.z) * RAD2DEG;
}

// Adopt a calibration: gyro bias, optionally accel scale, and a filter seeded from gravity.
// Accel scale is only trusted from a still sample: during motion |a| isn't gravity.
static void applyCalibration(const CalAttempt &c, bool adoptScale) {
  gyroBias = c.bias;
  if (adoptScale && c.accMag >= ACCEL_SCALE_MIN && c.accMag <= ACCEL_SCALE_MAX) accelScale = c.accMag;
  Vec3 a = toControllerFrame(c.accMean);
  accelAttitude(a, pitchDeg, rollDeg);  // atan2 ratios: scale-independent
  yawDeg = 0;
}

static void sendCalStatus(const char *st, const char *what, const CalAttempt &c) {
  char msg[112];
  snprintf(msg, sizeof msg, "%s; gyro sd %.2f dps, accel sd %.3f g, accel scale %.3f", what, c.gyroStd,
           c.accStd, accelScale);
  sendStatus(st, msg);
}

// Average the gyro while the board is held still. Rejects the result if the
// board moved, so a bumped controller never bakes a bias into the filter.
// No attitude is streamed until this returns.
static bool calibrate() {
  state = State::Calibrating;
  sendStatus("calibrating", "hold the controller still");
  CalAttempt best = {{0, 0, 0}, {0, 0, 1}, 1, 1e9f, 1e9f};
  for (uint32_t attempt = 1;; attempt++) {
    double gs[3] = {0, 0, 0}, gss[3] = {0, 0, 0}, as = 0, ass = 0;
    Vec3 accSum = {0, 0, 0};
    uint16_t n = 0;
    uint32_t t = micros();
    while (n < CAL_SAMPLES) {
      if ((uint32_t)(micros() - t) < SAMPLE_US) continue;
      t += SAMPLE_US;
      Vec3 a, g;
      if (!readRaw(a, g)) {
        sendStatus("sensor_error", "i2c read failed during calibration");
        state = State::SensorError;
        return false;
      }
      float gv[3] = {g.x, g.y, g.z};
      for (int i = 0; i < 3; i++) { gs[i] += gv[i]; gss[i] += gv[i] * gv[i]; }
      float am = sqrtf(a.x * a.x + a.y * a.y + a.z * a.z);
      as += am; ass += am * am;
      accSum.x += a.x; accSum.y += a.y; accSum.z += a.z;
      n++;
    }
    float maxGyroStd = 0;
    for (int i = 0; i < 3; i++) {
      double mean = gs[i] / n;
      maxGyroStd = fmaxf(maxGyroStd, sqrt(fmax(0.0, gss[i] / n - mean * mean)));
    }
    double am = as / n;
    float accelStd = sqrt(fmax(0.0, ass / n - am * am));
    CalAttempt c = {{(float)(gs[0] / n), (float)(gs[1] / n), (float)(gs[2] / n)},
                    {accSum.x / n, accSum.y / n, accSum.z / n},
                    (float)am, maxGyroStd, accelStd};
    if (maxGyroStd <= CAL_MAX_GYRO_STD_DPS && accelStd <= CAL_MAX_ACCEL_STD_G) {
      applyCalibration(c, true);
      state = State::Ready;
      sendCalStatus("ready", "calibrated", c);
      return true;
    }
    if (c.gyroStd < best.gyroStd) best = c;
    // After several tries, accept a hand-held sample (tremor only), but not real motion:
    // a bias taken while turning is worse than none, so keep waiting instead.
    if (attempt >= CAL_MAX_ATTEMPTS && best.gyroStd <= CAL_FALLBACK_GYRO_STD_DPS) {
      applyCalibration(best, false);
      state = State::Ready;
      sendCalStatus("cal_failed", "no fully still sample, using steadiest", best);
      return false;
    }
    sendCalStatus("motion_during_cal", "movement, retrying", c);
  }
}

// ---------- Fusion ----------
static float wrap180(float d) {
  while (d > 180) d -= 360;
  while (d < -180) d += 360;
  return d;
}

static void fuse(const Vec3 &accelChip, const Vec3 &gyroChip, float dt) {
  Vec3 g0 = {gyroChip.x - gyroBias.x, gyroChip.y - gyroBias.y, gyroChip.z - gyroBias.z};
  Vec3 a = toControllerFrame({accelChip.x / accelScale, accelChip.y / accelScale, accelChip.z / accelScale});
  Vec3 g = toControllerFrame(g0);

  // Body rates in the aerospace frame (x fwd, y right, z down).
  // Controller frame is x fwd, y left, z up, so y and z flip sign.
  float p = g.x, q = -g.y, r = -g.z;

  // Euler angle kinematics, so combined pitch+roll tilts integrate correctly.
  float phi = rollDeg * DEG2RAD, theta = pitchDeg * DEG2RAD;
  float cphi = cosf(phi), sphi = sinf(phi);
  float ctheta = cosf(theta);
  if (fabsf(ctheta) < 0.05f) ctheta = ctheta < 0 ? -0.05f : 0.05f;  // gimbal-lock guard
  float ttheta = sinf(theta) / ctheta;
  float rollDot = p + (q * sphi + r * cphi) * ttheta;
  float pitchDot = q * cphi - r * sphi;
  float yawDot = (q * sphi + r * cphi) / ctheta;

  float gyroPitch = pitchDeg + pitchDot * dt;
  float gyroRoll = rollDeg + rollDot * dt;
  // Tipping past vertical: Euler pitch is limited to +/-90, so the same
  // orientation is pitch mirrored about 90 with roll and yaw turned 180.
  if (gyroPitch > 90.0f || gyroPitch < -90.0f) {
    gyroPitch = (gyroPitch > 0 ? 180.0f : -180.0f) - gyroPitch;
    gyroRoll += 180.0f;
    yawDeg += 180.0f;
  }

  accelMagG = sqrtf(a.x * a.x + a.y * a.y + a.z * a.z);
  // Linear acceleration and vibration make gravity unreliable: fade accel out.
  float err = fabsf(accelMagG - 1.0f);
  float trust = err >= ACCEL_TRUST_BAND_G ? 0.0f : 1.0f - err / ACCEL_TRUST_BAND_G;
  float k = (dt / (FILTER_TAU_S + dt)) * trust;

  float accPitch, accRoll;
  accelAttitude(a, accPitch, accRoll);
  pitchDeg = constrain(gyroPitch + k * (accPitch - gyroPitch), -90.0f, 90.0f);
  rollDeg = wrap180(gyroRoll + k * wrap180(accRoll - gyroRoll));
  yawDeg = wrap180(yawDeg + yawDot * dt);

  pitchRate = pitchDot;
  rollRate = rollDot;
  yawRate = yawDot;
}

// ---------- Host commands ----------
static void handleCommand(const char *cmd) {
  if (strcmp(cmd, "CAL") == 0) {
    Serial.println("{\"t\":\"ack\",\"cmd\":\"CAL\"}");
    calibrate();
  } else if (strcmp(cmd, "ZEROYAW") == 0) {
    yawDeg = 0;
    Serial.println("{\"t\":\"ack\",\"cmd\":\"ZEROYAW\"}");
  } else if (strcmp(cmd, "HELLO") == 0) {
    sendHello();
  } else {
    Serial.println("{\"t\":\"ack\",\"cmd\":\"?\",\"err\":\"unknown command\"}");
  }
}

static void pollCommands() {
  while (Serial.available()) {
    char c = (char)Serial.read();
    if (c == '\r') continue;
    if (c == '\n') {
      cmdBuf[cmdLen] = 0;
      if (cmdLen) handleCommand(cmdBuf);
      cmdLen = 0;
    } else if (cmdLen < sizeof(cmdBuf) - 1) {
      cmdBuf[cmdLen++] = c;
    } else {
      cmdLen = 0;  // overlong garbage: drop it
    }
  }
}

// ---------- Arduino entry points ----------
void setup() {
  Serial.begin(SERIAL_BAUD);
  delay(200);
  Wire.begin(PIN_SDA, PIN_SCL, I2C_HZ);
  sendHello();
  if (initSensor()) {
    calibrate();
  } else {
    sendStatus("sensor_error", "MPU-6050 not found: check wiring and address");
  }
  lastSampleUs = micros();
}

void loop() {
  pollCommands();

  if (state == State::SensorError) {
    // Retry once a second; never stream made-up attitude data.
    if (millis() - lastProbeMs > 1000) {
      lastProbeMs = millis();
      if (initSensor()) {
        consecutiveI2cErrors = 0;
        calibrate();
        lastSampleUs = micros();
      } else {
        sendStatus("sensor_error", "MPU-6050 not found: check wiring and address");
      }
    }
    return;
  }

  uint32_t now = micros();
  if ((uint32_t)(now - lastSampleUs) < SAMPLE_US) return;
  float dt = (now - lastSampleUs) * 1e-6f;
  lastSampleUs = now;
  if (dt > 0.05f) dt = 0.05f;  // after a stall, don't integrate a huge step

  Vec3 a, g;
  if (!readRaw(a, g)) {
    if (++consecutiveI2cErrors >= 10) {
      state = State::SensorError;
      sendStatus("sensor_error", "lost contact with MPU-6050");
    }
    return;
  }
  consecutiveI2cErrors = 0;
  fuse(a, g, dt);

  if (++sampleCounter >= SAMPLES_PER_SEND) {
    sampleCounter = 0;
    sendImu();
  }
}

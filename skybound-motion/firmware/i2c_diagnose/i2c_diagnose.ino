// I2C wiring diagnostic for the MPU-6050 controller.
// Scans several safe ESP32 pin pairs (including SDA/SCL swapped), lists every
// responding address, and reads WHO_AM_I (0x75) from 0x68/0x69.
// Flash it, read the serial output at 115200, then re-flash mpu_flight_controller.

#include <Wire.h>

struct PinPair { int sda, scl; const char *note; };
static const PinPair PAIRS[] = {
  {21, 22, "default"},
  {22, 21, "SDA/SCL swapped"},
  {33, 32, ""}, {32, 33, ""},
  {26, 25, ""}, {25, 26, ""},
  {19, 18, ""}, {18, 19, ""},
  {17, 16, ""}, {16, 17, ""},
};

static void scan(const PinPair &p) {
  // Idle I2C lines are pulled high. A low line means a short, a missing pull-up, or no power to the breakout.
  pinMode(p.sda, INPUT);
  pinMode(p.scl, INPUT);
  delay(2);
  int sdaIdle = digitalRead(p.sda), sclIdle = digitalRead(p.scl);

  Wire.begin(p.sda, p.scl, 100000);
  Serial.printf("{\"t\":\"diag\",\"sda\":%d,\"scl\":%d,\"note\":\"%s\",\"sdaIdle\":%d,\"sclIdle\":%d,\"found\":[", p.sda, p.scl, p.note, sdaIdle, sclIdle);
  bool first = true;
  for (uint8_t a = 1; a < 127; a++) {
    Wire.beginTransmission(a);
    if (Wire.endTransmission() == 0) {
      Serial.printf("%s\"0x%02X\"", first ? "" : ",", a);
      first = false;
    }
  }
  Serial.print("]");
  for (uint8_t addr : {0x68, 0x69}) {
    Wire.beginTransmission(addr);
    Wire.write(0x75);
    if (Wire.endTransmission(false) == 0 && Wire.requestFrom(addr, (uint8_t)1) == 1)
      Serial.printf(",\"whoami_%02X\":\"0x%02X\"", addr, Wire.read());
  }
  Serial.println("}");
  Wire.end();
}

void setup() {
  Serial.begin(115200);
  delay(300);
}

void loop() {
  for (const auto &p : PAIRS) scan(p);
  Serial.println("{\"t\":\"diag_done\"}");
  delay(2000);
}

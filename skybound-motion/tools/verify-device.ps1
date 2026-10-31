<#
.SYNOPSIS
  Checks an ESP32 running mpu_flight_controller against protocol v1, without the browser.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools\verify-device.ps1            # auto-detect port, 15 s
  powershell -ExecutionPolicy Bypass -File tools\verify-device.ps1 -Port COM5 -Seconds 30
  powershell -ExecutionPolicy Bypass -File tools\verify-device.ps1 -File capture.txt   # offline check of saved lines

  Keep the controller still for the first ~3 s (gyro calibration), then tilt it
  nose-up, nose-down, right-down and left-down so the pitch/roll ranges show up.
#>
param(
  [string]$Port,
  [int]$Seconds = 15,
  [int]$Baud = 115200,
  [string]$File,
  [string]$SaveTo,
  # Send CAL right after opening, for boards that don't reset when the port opens.
  [switch]$Recalibrate
)

$ErrorActionPreference = 'Stop'

function Find-EspPort {
  # USB-UART bridges used on ESP32 boards: Silicon Labs CP210x, WCH CH34x/CH9102, Espressif native USB, FTDI.
  $devs = Get-PnpDevice -PresentOnly -Class Ports -ErrorAction SilentlyContinue |
    Where-Object { $_.InstanceId -match 'VID_10C4|VID_1A86|VID_303A|VID_0403' -and $_.FriendlyName -match '\((COM\d+)\)' }
  $ports = @($devs | ForEach-Object { if ($_.FriendlyName -match '\((COM\d+)\)') { $Matches[1] } })
  if ($ports.Count -eq 0) { throw 'No USB serial bridge found. Plug in the ESP32 with a data cable (or install the CP210x / CH340 driver) and pass -Port COMx if needed.' }
  if ($ports.Count -gt 1) { Write-Host "Several candidate ports: $($ports -join ', '). Using $($ports[0]); pass -Port to choose." }
  return $ports[0]
}

function Read-Lines {
  if ($File) { return Get-Content -LiteralPath $File }
  if (-not $Port) { $script:Port = Find-EspPort }
  $sp = New-Object System.IO.Ports.SerialPort $Port, $Baud, 'None', 8, 'One'
  $sp.NewLine = "`n"
  $sp.ReadTimeout = 500
  # Same as the browser: asserting DTR/RTS on open usually resets the board, so we also see boot + calibration.
  $sp.DtrEnable = $true
  $sp.RtsEnable = $true
  try { $sp.Open() } catch { throw "Could not open $Port ($($_.Exception.Message)). Close Arduino Serial Monitor / the browser tab using it." }
  if ($Recalibrate) {
    Start-Sleep -Milliseconds 1500   # if the board did reset on open, let it finish booting first
    $sp.Write("CAL`n")
    Write-Host 'Sent CAL: keep the controller resting on the desk until it says Calibrated.'
  }
  Write-Host "Reading $Port at $Baud baud for $Seconds s. Hold still ~3 s, then tilt around..."
  $lines = New-Object System.Collections.Generic.List[string]
  $deadline = (Get-Date).AddSeconds($Seconds)
  try {
    while ((Get-Date) -lt $deadline) {
      try {
        $line = $sp.ReadLine().TrimEnd("`r")
        $lines.Add($line)
        # Echo device state live so the person holding the controller knows when to move.
        if ($line -match '"t":"status","state":"([a-z_]+)","msg":"([^"]*)"') {
          $elapsed = [int](($Seconds - ($deadline - (Get-Date)).TotalSeconds))
          $color = switch ($Matches[1]) { 'ready' { 'Green' } 'cal_failed' { 'Yellow' } 'sensor_error' { 'Red' } default { 'Gray' } }
          Write-Host ("  [{0,2}s] {1}: {2}" -f $elapsed, $Matches[1], $Matches[2]) -ForegroundColor $color
          if ($Matches[1] -eq 'ready' -or $Matches[1] -eq 'cal_failed') { Write-Host '  >>> Calibrated. Pick it up and start moving now.' -ForegroundColor Green }
        }
      } catch [System.TimeoutException] { }
    }
  } finally { $sp.Close() }
  if ($SaveTo) { $lines | Set-Content -LiteralPath $SaveTo -Encoding ascii }
  return $lines
}

$lines = @(Read-Lines)
$imu = New-Object System.Collections.Generic.List[object]
$events = New-Object System.Collections.Generic.List[string]
$malformed = 0; $noise = 0; $gaps = 0; $lastSeq = $null; $firstMalformed = $null
$outOfRange = 0; $firstOutOfRange = $null

foreach ($l in $lines) {
  $t = $l.Trim()
  if (-not $t) { continue }
  if ($t[0] -ne '{') { $noise++; continue }   # boot ROM chatter before setup() runs is normal
  try { $o = $t | ConvertFrom-Json } catch { $malformed++; if (-not $firstMalformed) { $firstMalformed = $t }; continue }
  switch ($o.t) {
    'imu' {
      $req = 's', 'ms', 'p', 'r', 'y', 'pr', 'rr', 'yr', 'a'
      $missing = $req | Where-Object { $null -eq $o.$_ }
      if ($missing) { $malformed++; if (-not $firstMalformed) { $firstMalformed = $t }; continue }
      # Same limits as src/sensor/protocol.ts: the browser drops anything outside them.
      if ([math]::Abs([double]$o.p) -gt 90.5 -or [math]::Abs([double]$o.r) -gt 180.5 -or [math]::Abs([double]$o.y) -gt 180.5) {
        $outOfRange++; if (-not $firstOutOfRange) { $firstOutOfRange = $t }
      }
      if ($null -ne $lastSeq) { $d = [int64]$o.s - $lastSeq; if ($d -gt 1 -and $d -lt 10000) { $gaps += $d - 1 } }
      $lastSeq = [int64]$o.s
      $imu.Add($o)
    }
    'status' { $events.Add("status: $($o.state) - $($o.msg)") }
    'hello'  { $events.Add("hello: fw=$($o.fw) proto=$($o.proto) rate=$($o.rate)") }
    'ack'    { $events.Add("ack: $($o.cmd) $($o.err)") }
    default  { $malformed++; if (-not $firstMalformed) { $firstMalformed = $t } }
  }
}

$fail = New-Object System.Collections.Generic.List[string]
$warn = New-Object System.Collections.Generic.List[string]

Write-Host ''
Write-Host '--- Device events ---'
if ($events.Count) { $events | ForEach-Object { Write-Host "  $_" } } else { Write-Host '  (none)' }
if (-not ($events -match '^hello')) { $warn.Add('No hello line seen (board may not have reset on open; that is fine if imu data flows).') }
if ($events -match 'sensor_error') { $fail.Add('Firmware reports sensor_error: check MPU-6050 wiring (VCC->3V3, GND, SDA->21, SCL->22) and address 0x68.') }
if ($events -match 'motion_during_cal|cal_failed') { $warn.Add('Calibration saw motion. Keep the controller still on a desk right after connecting.') }

Write-Host ''
Write-Host '--- Stream ---'
Write-Host ("  lines: {0}  imu: {1}  malformed: {2}  non-JSON boot lines: {3}  seq gaps: {4}" -f $lines.Count, $imu.Count, $malformed, $noise, $gaps)
if ($firstMalformed) { Write-Host "  first malformed: $firstMalformed" }
Write-Host "  out of protocol range (browser would reject): $outOfRange"
if ($firstOutOfRange) { Write-Host "  first out of range: $firstOutOfRange" }
if ($outOfRange -gt 0) { $fail.Add("$outOfRange imu lines outside protocol range (pitch +/-90, roll/yaw +/-180).") }

if ($lines.Count -eq 0) { $fail.Add('No bytes at all. Wrong port, firmware not flashed, or charge-only cable.') }
elseif ($imu.Count -eq 0) { $fail.Add('No imu lines. If you see garbage, the baud rate is wrong; if only status lines, read them above.') }
else {
  $ms = $imu | ForEach-Object { [double]$_.ms }
  $span = ($ms | Measure-Object -Maximum -Minimum)
  $durS = ($span.Maximum - $span.Minimum) / 1000
  $rate = if ($durS -gt 0) { ($imu.Count - 1) / $durS } else { 0 }
  $stat = {
    param($k)
    $v = $imu | ForEach-Object { [double]$_.$k }
    $m = $v | Measure-Object -Average -Minimum -Maximum
    '{0,-6} min {1,8:N2}  max {2,8:N2}  mean {3,8:N2}' -f $k, $m.Minimum, $m.Maximum, $m.Average
  }
  Write-Host ("  device-clock rate: {0:N1} Hz over {1:N1} s (expected 50)" -f $rate, $durS)
  foreach ($k in 'p', 'r', 'y', 'a') { Write-Host ('  ' + (& $stat $k)) }

  # Stillness of the first second of data = noise floor after calibration.
  $first = @($imu | Select-Object -First 50)
  if ($first.Count -ge 20) {
    $pv = $first | ForEach-Object { [double]$_.p }; $pm = ($pv | Measure-Object -Average).Average
    $sd = [math]::Sqrt((($pv | ForEach-Object { ($_ - $pm) * ($_ - $pm) }) | Measure-Object -Average).Average)
    Write-Host ("  pitch noise over first {0} samples: +/-{1:N2} deg (still controller should be < ~0.3)" -f $first.Count, $sd)
  }
  $yawFirst = [double]$imu[0].y; $yawLast = [double]$imu[$imu.Count - 1].y
  Write-Host ("  yaw change start->end: {0:N2} deg (drift if the controller was not twisted)" -f ($yawLast - $yawFirst))

  if ($rate -lt 45 -or $rate -gt 55) { $warn.Add(("Rate {0:N1} Hz is off the expected 50 Hz." -f $rate)) }
  if ($gaps -gt 0) { $warn.Add("$gaps samples missing by sequence number (USB/serial buffer drops).") }
  if ($malformed -gt 0) { $warn.Add("$malformed malformed lines.") }
  $a = ($imu | ForEach-Object { [double]$_.a } | Measure-Object -Average).Average
  if ($a -lt 0.9 -or $a -gt 1.1) { $warn.Add(("Mean accel magnitude {0:N2} g; expected ~1.0. Check the accel range setting / sensor." -f $a)) }
}

Write-Host ''
if ($fail.Count) { Write-Host 'RESULT: FAIL' -ForegroundColor Red; $fail | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red } }
elseif ($warn.Count) { Write-Host 'RESULT: PASS WITH WARNINGS' -ForegroundColor Yellow }
else { Write-Host 'RESULT: PASS' -ForegroundColor Green }
$warn | ForEach-Object { Write-Host "  - $_" -ForegroundColor Yellow }
Write-Host ''
Write-Host 'This checks the data stream only. Axis signs still need the hands-on check in the browser:'
Write-Host '  front edge up => pitch +,  right edge down => roll +.'
if ($fail.Count) { exit 1 }

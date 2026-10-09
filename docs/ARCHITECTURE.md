# Technical Architecture & Pipeline Specification — RutaCam GPS

This document provides an engineering breakdown of the **RutaCam GPS** core systems, video pipeline, OpenGL ES stabilization shaders, GNSS telemetry fusion, and crash resilience mechanisms.

---

## 1. High-Level System Architecture

RutaCam GPS is structured around decoupled components communicating through dependency injection (`Microsoft.Extensions.DependencyInjection` in `MauiProgram.cs`):

```mermaid
graph TD
    subgraph UI & Application Layer
        MP[MainPage - Dashcam HUD & Controls]
        HP[HistoryPage - Route & Clip Browser]
        OSP[OverlaySettingsPage - Telemetry & Camera Config]
        RDP[RouteDetailPage - Segment Inspector & Export]
    end

    subgraph Core Domain Logic (RutaCam.Core)
        LC[LicenseCodec & Validator]
        GSC[GpsSpeedCalculator & Haversine Math]
        GFC[GpsFilterConfig Profiles]
        B32[Base32Crockford Codec]
    end

    subgraph Services Layer
        ACT[ActivationService]
        RS[RouteStorageService - routes.json]
        JRN[RecordingSessionJournalService - Crash Recovery]
        EXP[RouteExportService - GPX / CSV]
        VID[VideoLibraryService - MediaStore Movies]
    end

    subgraph Native Android Camera & Video Pipeline
        CX[CameraXOverlayRecorderService]
        GPU[RutaCamGpuSurfaceProcessor - OpenGL ES 2.0 / EGL14]
        MT[RutaCamGpuMotionTracker - IMU Sensors]
        GNSS[GnssSpeedService - NMEA & LocationManager]
        AUD[BluetoothProcessedAudioRecorder - AAC MediaCodec]
        MUX[MediaTrackMuxer - MP4 Remuxer]
    end

    MP --> ACT
    MP --> CX
    MP --> GNSS
    MP --> RS
    MP --> JRN
    ACT --> LC
    CX --> GPU
    CX --> MT
    CX --> AUD
    CX --> MUX
    GNSS --> GSC
```

---

## 2. Hardware-Accelerated Video Pipeline

RutaCam GPS uses **AndroidX CameraX 1.4+** as its primary recording engine.

### 2.1 UseCaseGroup Architecture
When starting a capture session, CameraX binds three concurrent use cases to the Android lifecycle:
1. `Preview`: Live viewfinder on the device screen (via `CameraView`).
2. `VideoCapture<Recorder>`: High-bitrate H.264/HEVC encoder writing to the MP4 container.
3. `CameraEffect`: Intercepts video buffers between the camera sensor and the recording encoder.

### 2.2 GPU Stabilization & HUD Composition (`RutaCamGpuSurfaceProcessor`)
The GPU processor initializes an EGL14 display, context, and off-screen pbuffer surface:
- **Input Texture**: The camera hardware feeds frames as an external OES texture (`GL_TEXTURE_EXTERNAL_OES`).
- **Motion Compensation**: `RutaCamGpuMotionTracker` samples the device's gyroscope and linear accelerometer at 100+ Hz. It computes an orientation rotation quaternion and maps it to a 4×4 transformation matrix.
- **Dynamic Cropping**: The vertex shader applies a slight digital crop (5–12%) and translates vertices to counteract high-frequency vehicle vibrations.
- **HUD Composition**: The telemetry HUD (speed, mini-map, coordinates, clock) is rasterized into an alpha-blended texture (`GL_TEXTURE_2D`) and composited over the stabilized video frame in a single GPU pass.

---

## 3. GNSS Telemetry & Speed Filtering

Traditional mobile GPS updates at 1 Hz with significant latency. RutaCam GPS implements a dual-path pipeline:

### 3.1 NMEA Parsing Engine (`GnssSpeedService`)
The service attaches directly to `LocationManager.AddNmeaListener` and inspects raw satellite sentences in real time:
- `$xxVTG`: Ground speed in kilometers per hour.
- `$xxRMC`: Navigation speed in knots (multiplied by `1.852` km/h).

When fresh NMEA speed messages arrive (< 700 ms old), they take priority over Android's fused location estimates, eliminating smoothing lag when accelerating or braking.

### 3.2 Physical Constraint Filtering (`GpsSpeedCalculator`)
To filter out multipath reflections and satellite hop artifacts:
- **Geodesic Calculation**: Calculates displacement between GPS coordinates using the Haversine spherical formula.
- **Acceleration Clamping**: Any speed change exceeding physical vehicle limits (`MaxAccelerationKmhPerSecond` or `MaxDecelerationKmhPerSecond`) is clamped to realistic boundaries.

---

## 4. Dashcam Loop Chunking & Crash Journal

### 4.1 Segment Rotation
Continuous recording is divided into fixed-duration chunks (e.g., 5 or 10 minutes):
1. `RotateCameraXSegmentAsync` signals the CameraX recorder to close the active output target.
2. A new timestamped file is immediately opened to prevent frame drops between segments.
3. Completed segments are transferred to `MediaStore.Video.Media` under `Movies/RutaCam GPS`.

### 4.2 Crash Resilience Journal (`RecordingSessionJournalService`)
Dashcam footage is most critical during accidents or abrupt power disconnects:
- Every active recording maintains a lightweight checkpoint file `active_recording.json`.
- At application startup, the journal service checks for unfinished sessions.
- Orphaned MP4 segments are scanned, finalized, indexed into `routes.json`, and published with the flag:
  `"RECUPERADA TRAS CIERRE INESPERADO"` (Recovered after unexpected shutdown).

---

## 5. Security Architecture

1. **Cryptographic Validation**: Uses .NET's built-in `System.Security.Cryptography.ECDsa` on the NIST P-256 curve (`secp256r1`).
2. **Signature Concatenation**: Formatted according to IEEE P1363 (64 bytes: 32 bytes $r$ + 32 bytes $s$).
3. **No Dynamic Code Generation**: Uses pure AOT-compatible serialization without runtime reflection.

# InertialLink XR for Unity

Unity 2022.3 LTS+ package for authenticated, low-latency vehicle IMU input. It exposes filtered vehicle motion to
existing Unity/OpenXR applications and includes optional bounded visual drivers. It does **not** move an XR camera.

## Install

In Package Manager, add the package from this repository's Git URL with the package path:

```text
https://github.com/YamaTro/inertial-link-xr.git?path=/unity/io.github.yamatro.inertiallink.xr#v0.1.0
```

Pin a release tag as above for reproducible installs. Depending on the moving `main` branch is not recommended.

## Minimal integration

Add `UdpMotionSource` and `VehicleMotionHub` to one GameObject. Provide the phone's 32-hex-character pairing code
at runtime from your own pairing UI; the key is intentionally not serialized into a scene or prefab.

```csharp
using UnityEngine;
using YamaTro.InertialLink;

public sealed class PairInertialLink : MonoBehaviour
{
    [SerializeField] private UdpMotionSource receiver;
    [SerializeField] private VehicleMotionHub hub;

    public bool Pair(string codeFromPhone)
    {
        if (!receiver.ConfigurePairingKey(codeFromPhone)) return false;
        return hub.SetSource(receiver);
    }
}
```

Call `receiver.ClearPairingKey()` when leaving the pairing screen or experience; it stops the socket, clears
queued frames, and zeroizes the in-memory key.

Read `hub.Current`, subscribe to `hub.MotionUpdated`, add `PeripheralCueField`, or configure the bounded driver.
`VehicleMotionState.StatusBits` exposes field validity; optional invalid fields are published as zero or identity:

```csharp
driver.Configure(hub, visualContentRoot);
```

`visualContentRoot` must be a separate content hierarchy. The driver refuses roots that contain a Camera/XR Origin
or sit underneath one. Never pass the XR Origin, Camera Offset, or XR camera.

## Sources

- `UdpMotionSource`: authenticated Android sender, replay protection, freshness checks, and clock synchronization.
- `ReplayMotionSource`: bounded local CSV replay (`seconds,linX,linY,linZ,gyroX,gyroY,gyroZ`).
- `SyntheticMotionSource`: deterministic setup and cue-development testing without a moving vehicle.

Network authentication is required and the default UDP port is `28461`. No telemetry, cloud service, location,
camera, or microphone is used. See the repository protocol and security documentation before exposing a receiver
outside a trusted local network.

Live UDP input remains diagnostic-only at zero output while clock synchronization is pending. With the default
two-second sync cadence, initial activation normally takes a little over two seconds: two successful clock
exchanges followed by three consecutive valid IMU packets. The overlay shows `WarmingUp` during this period.

This is an experimental developer tool, not a medical device. Stop use immediately if discomfort occurs.

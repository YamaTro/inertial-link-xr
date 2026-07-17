# Unity/OpenXR integration

The Unity package is engine-facing infrastructure. It receives authenticated
vehicle motion and publishes a safe, filtered `VehicleMotionState`; your app
chooses an isolated visual root or cue that consumes it.

## Requirements and install

- Unity 2022.3 LTS or newer
- An OpenXR application, or a non-XR Unity scene for synthetic testing
- A platform that permits the application to receive local UDP on port `28461`

In Unity Package Manager choose **+ → Add package from disk**, then select:

```text
unity/io.github.yamatro.inertiallink.xr/package.json
```

For a Git dependency after the repository is public and tagged, pin an exact
release tag and package subdirectory rather than `main`:

```text
https://github.com/YamaTro/inertial-link-xr.git?path=/unity/io.github.yamatro.inertiallink.xr#v0.1.0
```

Do not use an unreviewed moving branch in a released XR application.

## Start with the built-in synthetic source

1. Create an empty GameObject named `Vehicle Motion`.
2. Add `SyntheticMotionSource` and `VehicleMotionHub` to that object. The hub
   discovers the same-object source automatically.
3. Create a separate GameObject named `Vehicle Visual Content` containing only
   disposable test geometry. It must not contain and must not be inside a
   Camera or XR Origin.
4. Add `EnvironmentMotionDriver` to another object and assign the hub and visual
   content root, or configure it in code.
5. Enter Play Mode without wearing the headset. Disable the source and confirm
   the root returns to neutral.

```csharp
using YamaTro.InertialLink;

public static bool ConnectSynthetic(
    VehicleMotionHub hub,
    SyntheticMotionSource source,
    EnvironmentMotionDriver driver,
    UnityEngine.Transform visualContentRoot)
{
    if (!hub.SetSource(source)) return false;
    return driver.Configure(hub, visualContentRoot);
}
```

The optional **Safe Motion Cue Demo** package sample uses generated content and
never moves the XR camera.

## Receive Android data

Add `UdpMotionSource` and `VehicleMotionHub` to the same GameObject. The pairing
key is intentionally not serialized into a scene, prefab, or asset. Obtain the
32-hex-character code from the current Android sender session and configure it
at runtime through an application UI that does not log it:

```csharp
using YamaTro.InertialLink;

public static bool ConnectPhone(
    UdpMotionSource udp,
    VehicleMotionHub hub,
    EnvironmentMotionDriver driver,
    UnityEngine.Transform visualContentRoot,
    string oneSessionPairingCode)
{
    if (!udp.ConfigurePairingKey(oneSessionPairingCode)) return false;
    if (!hub.SetSource(udp)) return false;
    return driver.Configure(hub, visualContentRoot);
}
```

Configure the Android sender with the XR device's private IP and UDP port
`28461`. Avoid public/hotel/vehicle Wi-Fi; use a private link. The receiver pins
the authenticated sender endpoint after a valid IMU sample, then initiates
clock synchronization. Output remains in `WarmingUp` until synchronized and
three consecutive valid samples arrive.

## Consume state without the reference driver

Custom cues should subscribe to `MotionUpdated` or read `Current` on the Unity
main thread. Always multiply output by `SafetyWeight` and return to neutral when
the state is not active/degraded as appropriate.

```csharp
private void OnEnable() => hub.MotionUpdated += OnMotion;
private void OnDisable() => hub.MotionUpdated -= OnMotion;

private void OnMotion(VehicleMotionState state)
{
    var safeLinear = state.LinearAcceleration * state.SafetyWeight;
    // Apply only to this component's isolated cue—not Camera or XR Origin.
    cue.SetVehicleAcceleration(safeLinear);
}
```

`VehicleMotionState` exposes raw and linear acceleration, angular velocity,
gravity, relative vehicle rotation, safety state/weight, sync state, session,
sequence, and calibration generation. Do not integrate acceleration into an
unbounded position. Keep application limits tighter than wire validation bounds.

## Coordinate conversion

Wire values use right-handed +X right, +Y up, -Z forward. Unity conversion is
centralized in `CoordinateMapping`; consumers receive Unity-space vectors and
quaternions. Do not add a second axis negation. The mount transform belongs on
the sender side and must be verified using [calibration](calibration.md).

## Application lifecycle

- Clear the in-memory key when leaving the pairing screen/experience.
- Stop the receiver while paused or backgrounded.
- Treat new session/calibration IDs as discontinuities; the hub resets filters.
- Show non-motion status for warming, degraded, faded, and rejected input.
- Make disable accessible without relying on moving content.
- Test network loss, Android backgrounding, headset tracking loss, and scene
  unload before a stationary-vehicle trial.

Android/standalone headset builds may require a platform-specific local-network
or Internet socket permission. Add only what the target platform requires; do
not add location permission for Wi-Fi discovery because this project does no
discovery.

For an Android host app targeting Android 17 / SDK 37 or higher, direct LAN UDP
requires the runtime `ACCESS_LOCAL_NETWORK` permission. Treat denial or later
revocation exactly like network loss: stop the socket path and let output fade
neutral. Targets at SDK 36 or lower retain implicit LAN access through
`INTERNET` and must not request the new permission early. See Android's
[local network permission](https://developer.android.com/privacy-and-security/local-network-permission)
documentation.

See [Architecture](architecture.md), [Protocol](../protocol/SPEC.md),
[Limitations](limitations.md), and [Safety](safety.md).

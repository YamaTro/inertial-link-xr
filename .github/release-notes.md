# InertialLink XR Research Preview

**Passenger use only. This is experimental research software, not a medical
device, and it has not been shown to prevent, treat, or reduce motion sickness.**
Stop immediately if discomfort occurs. Never use or configure a headset while
driving, cycling, walking, or controlling machinery.

This release is an application integration kit. It moves only an explicitly
selected Unity content root; it is not a Quest/OpenXR system overlay and cannot
alter unrelated applications. It does not replace headset tracking.

Automated release verification covers the protocol tools and mutation tests,
synthetic recording schema, documentation links, Unity-independent C# core,
Android unit/lint checks, and unsigned Android library builds. The Unity package
is archived from the exact tag. For v0.2.0, Unity Editor Edit/Play Mode checks
and a stationary Xiaomi XIG04-to-Unity transport run were completed. An OpenXR
headset, moving vehicle, and human-subject efficacy/safety testing have **not**
been completed. See the tagged
[validation record](https://github.com/YamaTro/inertial-link-xr/blob/v0.2.0/docs/VALIDATION.md).

Assets include the Unity Package Manager tarball, Kotlin protocol JAR, Android
motion-source AAR, normative ILXR/1.0 specification, license/notices, and
`SHA256SUMS`. No unsigned release APK, debug APK, signing key, pairing key,
packet capture, real-trip recording, or human-subject data is distributed.

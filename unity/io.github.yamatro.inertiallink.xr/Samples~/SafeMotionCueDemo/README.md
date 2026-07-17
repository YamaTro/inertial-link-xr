# Safe Motion Cue Demo

Create an empty GameObject, attach `SafeMotionCueBootstrap`, assign the XR camera, and enter Play Mode.
The sample uses `SyntheticMotionSource`; no phone or network is needed. It creates a separate visual root and
demonstrates the bounded environment driver. Replace the synthetic source with `UdpMotionSource` only after
the unmounted synthetic axis, polarity, disable, and dropout checks pass.

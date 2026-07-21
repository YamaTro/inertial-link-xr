# Research basis and claim boundary

Checked: 2026-07-22

InertialLink XR is informed by peer-reviewed passenger-VR research, but this repository has not performed a
human-subject efficacy study. The correct claim is:

> Prior on-road and on-vehicle studies report lower motion sickness when peripheral or background visual motion
> is synchronized with vehicle motion. InertialLink XR implements an open, safety-bounded way to build and tune
> that class of cue in Unity/OpenXR. Its own sickness-reduction efficacy is not yet established.

## Relevant evidence

| Study | What was evaluated | Reported result | Design implication here |
| --- | --- | --- | --- |
| Noh, Park, and Kim, *IEEE Access* 12 (2024), [doi:10.1109/ACCESS.2024.3408834](https://doi.org/10.1109/ACCESS.2024.3408834) | Vehicle motion mixed into the VR background as synchronized linear/circular optical flow; car and ship experiments | Significant sickness reduction without degrading immersive experience | Provide a general vehicle-motion input and background/peripheral cue layer, rather than taking over the camera. |
| Kim et al., *Proceedings of the Human Factors and Ergonomics Society Annual Meeting* 66(1) (2022), [doi:10.1177/1071181322661086](https://doi.org/10.1177/1071181322661086) | On-road video viewing with and without peripheral cues that opposed vehicle rotation | Mean motion-sickness levels were lower with the cues | Keep primary video content fixed and put subtle synchronized cues in the margins. |
| McGill, Ng, and Brewster, CHI 2017, [doi:10.1145/3025453.3026046](https://doi.org/10.1145/3025453.3026046) | Multiple visual presentations of real car motion in an on-road VR study | No single presentation best balanced sickness and immersion; susceptibility and preferences differed | Expose cue strength, mapping, and polarity for per-user/per-content tuning. |
| Qiu et al., *Applied Ergonomics* 135 (2026), [doi:10.1016/j.apergo.2026.104778](https://doi.org/10.1016/j.apergo.2026.104778) | On-road acceleration-based versus matched-motion visual cues during non-driving tasks | Acceleration cues reduced sickness comparably while causing less distraction | Support sparse, acceleration-driven cues suitable for tasks such as watching a movie. |

## What this repository now demonstrates

- A fixed 9:16 Unity video panel with 72 generated cue particles confined to the left and right margins.
- Synthetic vehicle input driving the cue field while the Camera/XR Origin and central video remain unchanged.
- A curved `DirectionalMotionDome` with 2,019 generated cues behind the protected video region.
- A stationary Xiaomi XIG04-to-Unity run with 216 authenticated packets accepted, 0 rejected or dropped,
  a 5.448 ms best synchronized round trip, video playback through frame 112, and an unchanged Camera pose.
- A `MotionAlignmentMonitor` that compares measured physical acceleration with an application's virtual
  acceleration, reports the mismatch, and offers a bounded correction suggestion.
- Local Play Mode evidence showing a 720x1280 video playing, all 72 cues outside the protected central region,
  and an unchanged camera pose. See [Validation](VALIDATION.md).

The correction is diagnostic only. It never drives a vehicle, actuator, Camera, XR Origin, or application
transform automatically.

## What remains unproven

- Whether this implementation reduces sickness for any person or population.
- The best cue geometry, polarity, gain, latency compensation, or threshold for a particular vehicle/content.
- Android-to-OpenXR-headset latency and behavior in a moving vehicle.
- Accessibility, comfort, and adverse-effect rates.

Those questions require stationary hardware checks followed by ethically governed passenger-only research with
informed consent, stop criteria, individual tuning, and appropriate review.

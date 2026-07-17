# Safety guide

InertialLink XR is experimental research software. It is **not a medical
device**, has not been demonstrated to prevent or treat motion sickness, and
must not be represented as doing so. Visual motion cues can be ineffective or
can make discomfort worse, particularly when direction, gain, or timing is
wrong.

## Non-negotiable boundaries

- **Passenger only.** Never use or configure it while driving, cycling,
  walking, standing in moving transit, controlling machinery, or supervising a
  safety-critical task.
- Remain seated and appropriately restrained. Keep the physical area clear and
  follow the headset, phone, vehicle, and transit operator's safety guidance.
- Stop immediately at the first discomfort, nausea, dizziness, headache,
  eyestrain, disorientation, sweating, balance change, anxiety, or tracking
  instability. Remove the headset only when it is physically safe to do so.
- Do not encourage another person to “push through” symptoms. Do not use
  incentives or social pressure to recruit testers.
- A person with relevant health concerns should seek advice from an appropriate
  professional; this repository cannot give individual medical clearance.

## Development progression

Do not start on a public road or public bus. Advance only after the prior gate
passes and results are documented:

1. **Offline:** protocol unit tests, malformed input, recorded synthetic signals.
2. **Desk/loopback:** Unity scene with synthetic sender; verify every axis and
   immediate disable without wearing a headset.
3. **Stationary headset:** seated, stationary-room test; verify Camera/XR Origin
   never moves and tracking loss fades safely.
4. **Stationary vehicle:** engine off, then controlled vibration only if lawful
   and supervised; confirm phone mount cannot become a projectile.
5. **Closed/control environment:** passenger, spotter, brief exposure, predefined
   stop criteria and recovery period.
6. **Human-subject research:** only with informed consent, privacy safeguards,
   appropriate ethics/IRB review, preregistered outcomes, and adverse-event plan.

Public transport introduces other passengers, operator rules, uncontrolled
motion, emergency evacuation, privacy, and limited stopping options. It is not
an appropriate early test environment.

## Integration checklist

- [ ] Authentication is required and the key is fresh for this session.
- [ ] The phone is rigidly mounted, cannot strike anyone, and cannot obstruct
  controls, airbags, exits, visibility, or safety equipment.
- [ ] The mount profile and axis signs passed synthetic and hand-held bench tests.
- [ ] `contentRoot` is isolated from Camera, XR Origin, UI required for exit, and
  unrelated scene objects.
- [ ] Gains begin at zero and increase only under a documented test plan.
- [ ] An immediate, reachable disable action returns output to neutral.
- [ ] Sync is healthy; the receiver is `Active`, with acceptable age/loss/jitter.
- [ ] Unplug, app background, Wi-Fi loss, invalid HMAC, and tracking-loss tests
  all fade to neutral within the documented bound.
- [ ] No experiment records GPS, camera, microphone, identities, health details,
  or real-trip data without separate, explicit, ethics-reviewed consent.

## Safe failure behavior

The receiver waits for synchronization and three valid packets before becoming
active. A sample older than 250 ms or more than 100 ms in the future is invalid.
After 250 ms without a valid sample, the reference output fades linearly for
250 ms and is neutral by 500 ms. Invalid or replayed traffic cannot delay that
fade. Applications should also neutralize output when headset tracking is lost,
the experience is paused, or the user invokes disable.

These numbers are engineering guardrails, not thresholds proven safe for every
person. Changing them requires tests and an explicit safety review.

## Claims and publication

Describe the software as transporting and applying vehicle-motion data. Report
measured latency, loss, cue parameters, participant selection, exposure, and all
outcomes—including worsening and withdrawals. Do not turn an exploratory result
into “prevents motion sickness,” and do not use stars/downloads as evidence of
effectiveness.

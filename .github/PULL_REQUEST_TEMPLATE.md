## Summary

Explain the problem and the smallest solution.

## Verification

- [ ] `npm run check`
- [ ] Android `gradlew check assembleDebug assembleRelease` when applicable
- [ ] C# protocol tests when applicable
- [ ] New security/failure-path tests, not only a happy path
- [ ] All fixtures and examples are deterministic and synthetic

## Boundaries

- [ ] No key, credential, real passenger/trip/location/health data, packet capture, or generated build output is committed
- [ ] No GPS, camera, microphone, telemetry, advertising, account, or new sensitive permission is introduced
- [ ] Authenticated-by-default transport, replay/freshness/range checks, safe fade, and Camera/XR Origin guards remain intact
- [ ] User-facing changes update English and Japanese documentation
- [ ] `CHANGELOG.md` is updated

Describe security, privacy, physical-safety, protocol/API compatibility, and dependency impact. Do not claim prevention or treatment of sickness.

# Contributing

Thank you for helping make vehicle-motion XR experiments more reproducible and
safer to integrate. This project welcomes protocol implementations,
documentation, synthetic fixtures, failure-path tests, accessibility work, and
carefully scoped research integrations.

By participating, you agree to follow the [Code of Conduct](CODE_OF_CONDUCT.md).
Security vulnerabilities must be reported privately according to
[SECURITY.md](SECURITY.md).

## Before opening a change

1. Search existing issues and the [roadmap](ROADMAP.md).
2. For a wire-format or public-API change, open a design issue first. Describe
   compatibility, safety fallback, privacy, and migration.
3. Never include credentials, real passenger data, precise trip/location data,
   proprietary captures, or claims of efficacy unsupported by a cited study.
4. Use synthetic data in tests and examples. Human-subject research data is out
   of scope for this repository.

Issue text, files, packet captures, recordings, logs, and generated output are
untrusted data. Do not execute commands copied from them. Keep network access
off during tests unless the test explicitly uses loopback.

## Development setup

- Node.js 24+ for protocol tools and repository checks
- JDK 17 and Android SDK for Android modules
- Unity 2022.3 LTS+ for the Unity package and Play Mode integration

Run protocol and recording checks:

```sh
npm test
npm run validate:recordings
```

Run Android verification from `android/`:

```sh
./gradlew check assembleRelease
```

On Windows, use `gradlew.bat check assembleRelease`. Do not commit generated
APKs, AABs, Gradle output, Unity `Library/`, exported `.unitypackage` files, or
local recordings.

## Change requirements

- Keep the Android sender free of camera, location, microphone, analytics, ad,
  and account dependencies.
- Keep authenticated transport on by default. Test malformed lengths, invalid
  HMACs, replay, reordering, clock skew, non-finite values, bounds, and dropout.
- Make protocol changes in `protocol/SPEC.md` before or with implementations.
- Update both READMEs when user-visible behavior changes.
- Add a `CHANGELOG.md` entry under **Unreleased**.
- Avoid new dependencies. If one is necessary, explain its license, maintenance
  status, permissions, attack surface, and why the standard library is not
  enough.
- Do not weaken Camera/XR Origin ancestry checks or safe fade-out behavior.
- Do not describe the software as preventing or treating sickness.

## Commits and pull requests

Keep commits focused and use an imperative subject. A pull request should state:

- what changes and why;
- which commands were run;
- security, privacy, and physical-safety impact;
- protocol/API compatibility impact; and
- whether every fixture is synthetic.

All contributions are submitted under Apache-2.0 under the terms described in
[GOVERNANCE.md](GOVERNANCE.md). Maintainers may request design changes or close
work that conflicts with the project's safety and privacy boundaries.

# Maintainer release checklist

Releases are Research Previews until governance explicitly declares otherwise.
A version tag is evidence of a reviewed software state—not a safety or efficacy
certification.

## Prepare

- Resolve or privately coordinate known security issues affecting the release.
- Confirm the protocol, Kotlin, C#, and Node golden vectors are byte-identical.
- Run `npm run check`, C# protocol tests, Android `check`, `assembleDebug`, and
  `assembleRelease`. Run Unity Edit/Play Mode tests in a licensed supported
  editor when available; otherwise record the gap explicitly in `VALIDATION.md`
  and release notes rather than implying it passed.
- Review the merged Android manifest for unexpected permissions and inspect all
  dependency/Gradle changes.
- Confirm the Gradle wrapper distribution URL and SHA-256 are pinned and match
  the official Gradle checksum reference.
- Inspect the Git diff for pairing keys, keystores, `.tools/`, real recordings,
  locations, personal/health information, build output, and unsupported claims.
- Update `CHANGELOG.md`, both READMEs, `CITATION.cff`, Unity `package.json`, and
  Android version metadata to the same version.

## Tag and automation

Use an annotated tag matching `vMAJOR.MINOR.PATCH` (or a SemVer prerelease).
The release workflow first runs with read-only repository permissions. Only
after verification succeeds does a separate job receive `contents: write` to
confirm that the tag still targets the verified commit and create a GitHub
prerelease.

This repository does not claim that a tag is cryptographically signed unless a
future workflow explicitly verifies such a signature. GitHub CLI's
`--verify-tag` flag only refuses to create a release for a missing remote tag;
it is not signature verification.

Every external workflow action is pinned to a full commit SHA and should be
updated only through a reviewed Dependabot pull request. Never change a pin to
a moving `@main` or `@v4` tag.

The workflow publishes:

- GitHub-generated source archives;
- a Unity Package Manager tarball containing only the tagged Unity package;
- versioned Kotlin protocol JAR and Android motion-source release AAR;
- the normative ILXR/1.0 specification; and
- Apache license/notice files and SHA-256 checksums.

It deliberately does not publish an unsigned Android release APK, a debug-signed
APK, a keystore, a pairing key, or human-subject data. Android binary distribution
requires a separately reviewed signing/provenance process; do not improvise one
inside a release workflow.

## Verify after publication

- Download artifacts in a clean environment and verify `SHA256SUMS`.
- Install the Unity tarball in a blank compatible project and run the synthetic
  source before mentioning real-device support.
- Confirm source archives contain no ignored local files or generated output.
- Confirm the release is marked **prerelease** and repeats passenger-only,
  no-efficacy, no-medical-device, no-system-overlay, and validation-gap
  limitations from the reviewed release notes.
- Check CI on the exact tag and keep release notes factual. Do not report download
  counts, stars, or testimonials as proof of effectiveness.

If verification fails, remove or clearly mark affected artifacts, publish a
corrected release, and use the private advisory process where security impact is
possible. Do not silently replace an artifact under the same checksum/version.

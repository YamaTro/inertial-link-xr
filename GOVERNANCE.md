# Governance

InertialLink XR uses a maintainer-led, consensus-seeking model while the
project is a Research Preview.

## Roles

**Contributors** submit issues, documentation, code, tests, and reviews.
Contributions do not confer special access or authority.

**Maintainers** triage reports, review changes, manage releases, protect the
security boundary, and decide when consensus cannot be reached. Maintainers
must disclose material conflicts of interest.

The current maintainers are the GitHub users with write access to the
repository. That verifiable repository setting is authoritative; this file
does not fabricate or promise a larger team.

## Decisions

Routine changes use pull-request review. Changes to the wire protocol, safety
fallback, data collection, permissions, licensing, or governance require a
public design discussion and explicit maintainer approval. Security fixes may
be developed privately and documented after coordinated disclosure.

Decision priorities are:

1. physical safety and honest limitations;
2. security, privacy, and local-first operation;
3. protocol interoperability and backward compatibility;
4. maintainability and testability; and
5. new capability.

Silence is not approval. Popularity, star counts, generated testimonials, or
fabricated adoption metrics are never evidence for a technical decision.

## Contributions and licensing

Contributions intentionally submitted for inclusion are licensed under
Apache-2.0 as described in Section 5 of the license. No Contributor License
Agreement is currently required. Contributors must have the right to submit
their work and identify third-party material.

## Releases

Maintainers create reviewed annotated tags and GitHub releases after required
checks pass. A cryptographic signature is claimed only when it is explicitly
verified and documented for that release.
Preview releases use semantic versions below `1.0.0` and may change APIs. A
stable `1.0.0` requires documented compatibility policy, independent protocol
interoperability, threat-model review, and evidence that failure paths work on
supported devices. It does not imply medical efficacy.

## Changing governance

Governance changes use the same public review as protocol changes. A future
multi-maintainer project may add nomination, removal, and quorum rules through
a reviewed amendment.

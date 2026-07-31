# Compatibility

## Euro Truck Simulator 2

- Publicly supported target of `0.3.11.47`.
- Main community validation: modern save format `97`.
- Existing compatibility handling declares save formats `61–97`, but the strongest evidence is for the tested modern fixture.
- Game updates can change save fields or reference graphs. Check issues before editing after a major update.

## American Truck Simulator

ATS is experimental and unvalidated in this release because the maintainer did not have an ATS test environment. This is not a blocker for the ETS2 release.

ATS reports are welcome when users:

- work on a copied save;
- provide the ATS version and save format;
- make one isolated change;
- attach all three logs;
- confirm reload and in-game behavior;
- provide an anonymized before/after diff when possible.

No claim of ATS support should be made until those tests pass across the critical domains.

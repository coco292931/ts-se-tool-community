# Final regression protocol

1. Build with zero errors and only the three historical warnings.
2. Open version `0.3.11.47`.
3. Load a copied ETS2 manual save.
4. Confirm the H21 initialization marker.
5. Open the final resource center.
6. Confirm `errorlog.log` stays empty during normal loading.
7. Confirm compatibility diagnostics are written to `compatibility.log`.
8. Edit one AI-driver skill, save once and reload.
9. Apply one verified accessory alternative, save once and reload.
10. Diff the timestamped backup and final `game.sii`.
11. Open the save in ETS2 and inspect the changed domains and regressions.
12. Confirm the About screen, repository link and support information.
13. Confirm unsafe operations remain disabled.
14. Verify the release ZIP with `SHA256SUMS.txt`.

ATS is not part of the H21 acceptance gate.

# Bad From Stock acceptance checks

Client-only change. No API deployment or database migration required.

- Add a manual equipment card and a Swap card. Bad From Stock starts unchecked.
- Check it: Left Serial Number and its space disappear; the remaining serial
  field is labeled Bad From Stock Serial Number. Enter the defective unit's serial.
- Preview: expect `Bad From Stock Radio SN: ABC123` for Radio / ABC123,
  without Found or Left lines for that card.
- Enter a Left serial before checking: it must not appear in the write-up.
  Uncheck: the field and its previous value return, with normal Found/Left output.
- Switch site tabs and return; verify selection and serial fields are preserved.
- Test multiple cards with mixed checked/unchecked states and remove a checked card.
- Equipment type remains required; serial numbers retain the existing optional rule.
  A checked card without a serial reports `(not recorded)` rather than inventing one.
- Confirm submission: check Dispatch and Site History narratives for the same line.
- Check light/dark themes and laptop sizing.

Source and diff checked. Windows build and live UI tests still required; the
editing environment has no .NET SDK.

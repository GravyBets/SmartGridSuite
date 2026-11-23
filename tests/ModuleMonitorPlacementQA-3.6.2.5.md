# SmartGridSuite 3.6.2.5 — Multi-monitor module placement QA

This client-only change positions modules on the **monitor containing the launcher** and returns the launcher to the **monitor used by the module when it closes**. It must not change API/database behavior, roles, launcher lifecycle, themes, or interface scaling.

## Required Windows tests

- [ ] Launch SmartGridSuite, move the launcher to a **secondary display**, then open Dispatcher. The Dispatcher must maximize on that secondary display.
- [ ] Close Dispatcher. The launcher must appear on that same secondary display, not on the primary display.
- [ ] Repeat with **Field Technician**, **Administration**, and **Lineman**.
- [ ] Move a maximized module to a different monitor using **Win + Shift + Left/Right Arrow**, then close it. The launcher must follow the module's final monitor.
- [ ] Open another module from the relocated launcher; it must maximize on the monitor where the launcher now sits.
- [ ] Start on the **primary** monitor; existing behavior must still work.
- [ ] Test mixed scaling, e.g. primary at 100% and secondary at 125%/150%, with secondary display **to the left** (negative virtual-screen coordinates), if available.
- [ ] Disconnect a secondary display after moving the launcher/module there. Closing or reopening must restore the window to an available display rather than losing it off-screen.
- [ ] Cancel a close prompt due to unsaved write-up or dispatcher data; launcher must remain hidden and module must remain on its monitor.
- [ ] Close the launcher; confirm the app exits and does not remain stuck in Task Manager.
- [ ] Validate the normal ClickOnce WPF client build (Windows .NET 8).

## Release status

Source implementation only. No Windows build or multi-monitor runtime test was performed by ChatGPT.

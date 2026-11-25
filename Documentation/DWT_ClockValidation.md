# DWT runtime clock verification

2026-11-24, Unity 6000.6.4f1, OceanOnly scene.

- Removed shader `_Time.y` dependency and the manual-time material toggle.
- Connected `DwtOceanClock` on `OceanSurface_DWT` and saved the scene.
- Runtime clock uses scaled deltaTime and a renderer MaterialPropertyBlock. It does not mutate the material asset or execute in Edit Mode.
- Edit Mode: elapsed/shader time = 0; captures separated in wall time were identical (SHA256 `D902EB7FC22F6935587D07196A85982BF5759B00BFC2D65100CEF8AE5E6A01AC`).
- Play Mode: elapsed advanced from 19.508446 to 46.238600 seconds, matching the float shader property.
- timeScale=0: frames advanced from 5774 to 7767 while elapsed remained exactly 46.238599832402542; paused captures identical (SHA256 `F2A5620310CF5C600DD75DE2D90C1A2636FA73CB738458A42F8F9E52C19013D7`).
- Restoring timeScale=1 resumed progression to 60.260649 seconds.
- Editor Pause: both reads reported frame 9419 and elapsed 60.260649038944393.
- After exiting Play Mode: elapsed, renderer shader time and material time all 0; scene saved; shader errors false; Console errors/warnings 0/0.
- Restored timeScale, background-run override and auto-tick; left Editor stopped.

Captures: `Clock_Edit_A.png`, `Clock_Edit_B.png`, `Clock_Play_A.png`, `Clock_Paused_A.png`, `Clock_Paused_B.png`, `Clock_Edit_AfterPlay.png`.

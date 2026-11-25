# Dynamic Wave Trains — first Unity implementation

Updated: 2026-11-24

## Run

Open `Assets/Scenes/OceanOnly.unity` and enter Play Mode. `OceanSurface_DWT` uses `Assets/Ocean/DWT/DynamicWaveTrains.mat`. `DwtOceanClock` advances a local clock using scaled `Time.deltaTime` only in Play Mode and supplies `_OceanTime` through a renderer property block. The shader does not read Unity's global `_Time`. Edit Mode stays at time zero; exiting Play Mode resets the property block to zero. Play Mode pause and timeScale=0 stop the waves. Shared material assets are not changed by the runtime clock. The scene does not contain a ship or navigation implementation yet.

The material exposes five vectors: X = wavelength in metres, Y = orientation in degrees, Z = profile strength in [0,1], W unused. `Height scale`, `Time scale`, `Base perceptual roughness` and water color are editable. `Logical ocean offset XZ` is the evaluation origin for future relative navigation, not a simulated moving field. `_OceanTime` is hidden and runtime-owned; the old manual-time toggle was removed. `View` can show height or normals for inspection.

## Algorithm and source

Reference: Fournier, Laurain, Kraemer, Gilet and Sauvage, *Dynamic Wave Trains: A Procedural Approach to Spatially Varying Ocean Synthesis*, Computer Graphics Forum, 2026. https://doi.org/10.1111/cgf.70495

Public reference: https://git.unistra.fr/igg_astex/fournier-et-al-dynamic-wave-trains

Pinned commit: `98d0644887451fc7b636b017e7d2066867fd3beb`.

Local original: `References/DynamicWaveTrains` (Godot demo, outside Unity asset import). `References/DWTSource` contains the inspected shader snapshot. No code from the former 2D project's water implementation was used.

Implemented in `DwtSynthesis.hlsl`:

- Signed Gaussian complex exemplar, 512x512 EXR, ten wave cycles in the source texture.
- Triangular stochastic tiling with hashed offsets and L2-normalized blending weights.
- Per-train wavelength/orientation/profile control, deep-water phase speed sqrt(g*lambda/(2*pi)).
- Radial tanh compression and source trochoidal profile lookup, followed by summation of five trains.
- Mesh-footprint MIP filtering and pixel-footprint filtering, with screen derivatives transformed back into ocean-coordinate height slopes.
- Deep-water homogeneous controls only. Identical tile transforms make the reference's vertex-slot permutation unnecessary in this first implementation.

Unity adaptation:

- Y-up, XZ ocean plane. The mesh samples the filtered profile height field directly. This first version does **not** port the optional Appendix C tangential-displacement representation.
- Explicit isotropic exemplar MIP selection shared by vertex/pixel/depth passes. Pixel profile lookups retain implicit MIP filtering; vertex/compute profile lookups use mip 0.
- Slope variance drives an isotropic roughness approximation, as in the public demo. This is not the paper's full anisotropic covariance BRDF.
- Custom URP Fresnel/GGX directional-light and HDR environment reflection shading; opaque deep-water surface with a matching displaced DepthOnly pass.
- Grid: 128m square, 0.5m cells, 66,049 vertices / 131,072 triangles, conservative vertical bounds. Adaptive mesh LOD is not implemented.
- ACES and bloom remain enabled. DOF is configured but temporarily inactive to inspect the wave detail without blur.

## Data provenance

`exemplar.exr` SHA256: `2905D7EE57364C5D0510AC85FD5EF1776F44FA19D3F577F907B91D66061AAB87`

`profile.exr` SHA256: `CE1A37944149994C5B9473F7CEC4F9A21F5D432E6B035E64BE099B2C3AE89E95`

Both are imported linear, uncompressed RGBAHalf with Box-filtered MIPs; repeat for exemplar, clamp for profile. They retain negative data. Readable texture data is retained for editor validation.

`DaySky.exr` is the reference demo's HDRI.exr. Its credits identify ambientCG Day Sky HDRI 063 B under CC0: https://ambientcg.com/view?id=DaySkyHDRI063B. Imported as a specular-prefiltered cubemap, used by the sky and environment reflection.

No LICENSE/COPYING file was found in the pinned repository. The sky's CC0 credit does not establish a license for the shader code or the two generated wave-data textures. Redistribution/commercial licensing for those sources remains unverified; no release or publication has been performed.

## Validation

`DwtValidation.Run()` evaluates 1,024 points on the GPU. Results are recorded in `Documentation/DWT_Validation.txt`:

- signed texture ranges survive import;
- all sampled heights finite;
- identical position/time repeats exactly;
- advancing time changes the wave field;
- large footprints suppress unresolved wave energy;
- zero profile gives a flat surface (the source LUT has a small constant discretization bias).

Play Mode was observed advancing from frame 2,286 to 2,300 in a server-side wait, and later frame 13,479 at 116.12 seconds. Camera captures at different live times: `DWT_Play_A.png`, `DWT_Play_B.png`. `DWT_Final.png` records the filtered final look. These are desktop rendering/behavior checks, not mobile performance measurements or visual acceptance by the user.

Final saved-scene rerun: frames 3,228 -> 3,242; ShaderUtil reports no errors/messages; zero missing scripts; Console errors/warnings = 0/0. Device: NVIDIA GeForce MX150, Direct3D 11. Test Play Mode was stopped and the temporary background-run/auto-tick overrides were restored.

## Whitecaps

The first DWT train supplies the phase and radial compression loss for whitecaps. Section 9's separation is reflected in two masks: foam fades behind the crest, and spray has a narrow band in front of it whose width grows with the energy loss. The Foam and Spray material views expose the masks.

Foam samples the static ambientCG Foam 001 texture (CC0) with three overlapping stochastic tiles. Spray samples a precomputed 256×256×32 periodic Worley volume with variable seed counts and radii driven by a periodic density field. Its spatial axes wrap and its time axis loops. Three overlapping tiles use separate spatial and time offsets. Wave orientation and phase speed transport the spray without scaling bubble size by wavelength. Both effects use the ocean clock.

Unity's imported reference exemplar presented identical R and G values, collapsing the phase needed by the spray mask. The material now uses the seeded `ComplexPhaseExemplar.asset`, which generates signed sine and cosine components using the public demo's Gaussian-windowed kernel method. `DwtValidation` checks that the channels are distinct and signed. The changed exemplar also changes individual wave shapes.

The review scene uses an ellipsoid with a capsule contact proxy and a falling box with a box contact proxy. A `WaterInteractor` explicitly registers each proxy. Sphere, box and capsule proxies yield one, eight and seven samples respectively. On this scene's single water surface, the sampler batches up to 128 points every 0.08 game seconds and reads the mesh-filtered DWT height asynchronously on the GPU. Successive results detect entry and estimate contact-point motion. The common shader combines up to 48 short-lived directional or radial wave packets into displacement, normals and foam. Each source retains its birth position in logical ocean coordinates. The water height query samples the DWT base surface. Coupled buoyancy, wave reflection and interaction-driven object motion are outside the current model.

In an isolated Play Mode check, moving the registered box through the surface added eight impact sources. Repeating with its interactor disabled added zero. The existing sources persisted.

In OceanOnly, W accelerates, S brakes and reverses, and A/D steer. Releasing the throttle slows the hull to a stop. The main camera follows behind the hull. The review controller smooths hull height and attitude from five DWT height samples and moves the water mesh window as the hull travels. The box remains available as a Contact setup for manual tests.

Select the WaterInteractor mode explicitly. Contact uses the assigned primitive collider for entry and depth changes. Wake uses the optional Wake Anchor (otherwise the interactor transform), authored Wake Width, Wake Rear Offset and Response. Horizontal motion sets the direction and triggers emission. The wake anchor still queries the base height for the review hull bobbing. Collider intersections do not determine wake width or activation.

`DwtWakeKernel` bakes a radial inverse transform of a Gaussian pressure impulse with the linear deep-water dispersion relation `omega² = g*k`. The signed response depends on distance and elapsed time. The lookup and runtime sum use a finite spatial window, wavelength band and five-second history. The demo uses horizontal ocean-space velocity as relative velocity because it has no current field. The pressure proxy approximates the collider waterline through width and offset and uses one isotropic Gaussian. It does not solve the full pressure distribution on a hull. Entry and heave packets retain the separate contact model.

At 3.8 m/s in a five-second straight pass, the paused shader rendered 50 pressure samples. With the contact packets and foam excluded, a 640×360 comparison against wake strength zero found 6,776 crest pixels and 9,778 trough pixels above the absolute 6/255 threshold. See `DWT_DispersiveWakeHeight.png` for the isolated height visualization. No quantitative Kelvin angle or long-range wake validation has been performed.

## Desktop performance review

In the Unity Editor on an MX150 with Direct3D 11, a fixed 1280×720 camera request with 24 wake samples was measured after five warm-up renders. Each batch used 24 renders with a blocking one-pixel readback. The measurement includes CPU work and GPU synchronization. The baseline median was 10.86 ms, with optimized medians of 9.40 ms and 8.34 ms in two runs. These timings do not establish player or device frame rates.

The shader skips foam and cellular spray sampling where their masks have no coverage. Vessel and ocean foam share one texture evaluation. The shared exemplar mean and isotropic footprint length are reused across wave trains.

The timing above predates the periodic spray volume and regenerated phase texture. After both changes, the 1,024-point GPU wave probe passed. At 480×270 with the hull hidden, the raw spray mask and composited spray exceeded 10/255 in every sample from 0 to 8.25 seconds at 0.75-second intervals. The composited spray debug view had 4,699–14,891 pixels above that threshold across the 12 samples.

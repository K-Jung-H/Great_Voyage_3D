# Great_Voyage_3D — Ocean scene

Updated: 2026-11-23

## Project

- Root: `D:/Great_Voyage/Great_Voyage_3D`
- Product name: `Great_Voyage_3D`
- Unity 6000.6.4f1; URP 17.6.0; Pipeline 1.0.1.
- The former `My project` directory was moved into its parent. Unity Hub now registers the new root.
- Existing 2D project assets and water implementations were not imported.

## Open and run

Open `Assets/Scenes/OceanOnly.unity`. It is the enabled build scene and Editor Play Mode start scene. The original template SampleScene is preserved but excluded from the build list.

- `Main Camera`: perspective, 38 degree vertical FOV, 48 degree downward angle, 35 degree yaw. Looks at the origin from 42 units away.
- `VesselAnchor`: empty origin for a future centered vessel; no vessel model or navigation logic yet.
- `NearbyObjects`: empty parent for future relative-position objects.
- `OceanSurface_DWT`: 128m grid, 0.5m cells, GPU DWT height displacement and filtered normals. No collider. See [DWT implementation](DWT.md).
- `Environment`: directional sunlight, HDR sky/environment reflection, global post-processing volume.
- `OceanVolume`: ACES, mild bloom, Bokeh DOF configured at the origin but inactive for wave review. This is conventional depth of field, not a custom tilted focal-plane effect.

## Deliberately not implemented yet

The active scene uses DWT waves with surface foam and spray. Navigation, local vessel effects and device quality paths remain pending.

The project direction is DWT with a separate low-poly wave quality path based on device capability. S22 is not the overriding quality constraint. Do not reuse the old 2D water architecture.

## Validation

- Scene generated and saved through the running Unity Editor API.
- Perspective camera, active URP/HDR, grid counts and zero missing scripts verified.
- DWT GPU invariants and live Play Mode animation were checked; see `DWT.md` and `DWT_Validation.txt`.
- Actual DWT camera rendering inspected: `Documentation/DWT_Final.png`.
- No mobile performance or user visual acceptance is claimed.

`Assets/Editor/OceanSceneSetup.cs` contains the initial creation recipe. It refuses to overwrite an existing OceanOnly scene.

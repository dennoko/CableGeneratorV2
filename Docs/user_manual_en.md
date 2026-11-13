# CableGenerator V2 User Manual

CableGenerator V2 is a Unity editor extension designed to generate cables and wiring meshes intuitively and with minimal effort.  
It operates completely standalone without any dependency on Unity's official Splines package, and allows you to wire cables onto background meshes immediately without requiring pre-configured colliders.

---

## Table of Contents

1. [Quick Start](#1-quick-start)
   - [1.1 Opening the Tool Window](#11-opening-the-tool-window)
   - [1.2 Creating a New Cable](#12-creating-a-new-cable)
   - [1.3 Quick Wiring (Wire between 2 points / Wire along stroke)](#13-quick-wiring-wire-between-2-points--wire-along-stroke)
   - [1.4 Adjusting Sag](#14-adjusting-sag)
   - [1.5 Choosing a Profile](#15-choosing-a-profile)
   - [1.6 Exporting (Mesh Asset / FBX / Prefab)](#16-exporting-mesh-asset--fbx--prefab)
2. [Feature Overview](#2-feature-overview)
   - [2.1 UI Layout](#21-ui-layout)
   - [2.2 Feature Summary Table](#22-feature-summary-table)
3. [Feature Details](#3-feature-details)
   - [3.1 Wiring (Wire Tab)](#31-wiring-wire-tab)
   - [3.2 Point Adjustment (Points Tab)](#32-point-adjustment-points-tab)
   - [3.3 Cable Profile (Cable Type Tab)](#33-cable-profile-cable-type-tab)
   - [3.4 Output, LOD & Bones (Output Tab)](#34-output-lod--bones-output-tab)
   - [3.5 Scene View Operations (S1 Toolbar & Direct Manipulation)](#35-scene-view-operations-s1-toolbar--direct-manipulation)
4. [Inquiries & Support](#4-inquiries--support)

---

## 1. Quick Start

### 1.1 Opening the Tool Window

You can open the dedicated tool window using any of the following methods:

- Menu bar: **`Tools` > `dennokoworks` > `Cable Generator`**
- Hierarchy right-click: **`Cable Generator V2` > `Open in Window`**
- In the `CableGenerator` component Inspector: click the **"Open Cable Generator"** button

### 1.2 Creating a New Cable

1. In the tool window, click the **"+ New"** button at the top.  
   (Or right-click in the Hierarchy > **`Cable Generator V2` > `Create Cable`**)
2. A new cable object will be created in the scene and automatically selected as the active target.

### 1.3 Quick Wiring (Wire between 2 points / Wire along stroke)

Even if objects in the scene do not have colliders, CableGenerator raycasts directly against mesh surfaces with zero setup.

#### A. Wire between 2 points (Specify end points for straight/curved wiring)
1. In the **"Wire"** tab of the window, click the **"Wire between 2 points"** button (or click the corresponding icon in the Scene view toolbar).
2. Click on any mesh surface in the Scene view to set the start point.
3. Click on another mesh surface to set the end point.
4. The cable connecting the two points is created immediately. Press `Esc` or click "End" in the banner to exit the mode.

#### B. Wire along stroke (Freehand drawing across surfaces)
1. In the **"Wire"** tab, click the **"Wire along stroke"** button.
2. Drag across mesh surfaces in the Scene view.
3. When you release the mouse button, control points are automatically generated along the drawn trajectory, producing a cable slightly offset from the surface.

### 1.4 Adjusting Sag

1. When a cable is selected, a **`▼` (sag handle)** appears at the center of each segment in the Scene view.
2. Simply drag this `▼` handle downward to intuitively add or adjust sag.
3. In the **"Points"** tab, using the "All" sag slider will apply the same sag amount across all segments at once.

### 1.5 Choosing a Profile

1. Open the **"Cable Type"** tab in the tool window.
2. Click on any of the 7 built-in profile thumbnails (Heavy, Flat, Parallel, Bundled, Cluster, Spiral tube, Twisted).
3. The cable mesh in the scene will immediately update to the selected cross-section.

### 1.6 Exporting (Mesh Asset / FBX / Prefab)

1. Click the primary **"Export"** button at the bottom right of the window (or use the "Output" tab).
2. **Mesh (.asset)**: Saves the generated mesh into the project as an asset and instantiates a baked GameObject in the scene.
3. **FBX**: Exports the cable as a standalone `.fbx` model file (with support for skinning bones and weights).
4. **Prefab (LOD)**: Saves a `.prefab` with LOD0–LOD2 meshes and a configured `LODGroup`.
5. During export, the original cable object is automatically disabled and tagged with `EditorOnly`, allowing you to safely re-edit the cable at any time.

---

## 2. Feature Overview

### 2.1 UI Layout

You work with CableGenerator V2 mainly in two places: the **tool window** and the **Scene view**. The Inspector only shows a summary and is not used for editing.

**Tool window (Cable Generator)**
This is where you change settings and export. From top to bottom it contains:

- **Top bar**: Version check, language toggle (JA / EN) and the settings menu.
- **Target card**: Choose the cable to edit, pin it, or create a new one.
- **Tabs**: Wire, Points, Cable Type and Output. They are ordered by workflow, but you can use them in any order.
- **Session banner**: Shown only while wiring, telling you the next step and how to finish.
- **Tab content**: The settings of the selected tab. Only this area scrolls.
- **Output dock and footer**: The output folder, a summary of the export format, cable info (point count, total length, polygon count) and the **Export** button.

**Scene view**
This is where you shape the cable directly. When a cable is selected, the S1 toolbar appears at the top of the Scene view, giving one-click access to starting a wiring mode, switching tangent modes, LOD and more. In the scene you can move and rotate control points, add a point with the ⊕ button at the middle of a segment, and drag the ▼ handle to adjust sag. Hints appear near the cursor while you work, and a short result message appears after each action.

**Inspector**
Shows only a summary of the cable (point count, total length, etc.) and a button that opens the tool window.

### 2.2 Feature Summary Table

| Category | Feature | Description |
| :--- | :--- | :--- |
| **Wiring** | **Wire between 2 points** | Click start and end mesh surfaces to generate cable (no colliders needed) |
| | **Wire along stroke** | Drag across surfaces to wire along the drawn path |
| | **Snap to surface** | Project selected points onto the nearest surface in a chosen direction |
| | **Ignore back faces** | Wiring and snapping ignore faces seen from behind (e.g. the far side of a wall or floor). On by default |
| | **Divide & Subdivide** | Evenly divide between endpoints, or insert points at midpoints of all segments |
| | **Space points evenly** | Reposition points at equal distances along the current curve length |
| | **Loop / Flip** | Connect endpoints into a closed loop; swap start and end directions |
| **Points** | **3 Tangent modes** | Auto (automatically smoothed) / Smooth (symmetric) / Corner (broken) |
| | **Intuitive sag manipulation** | Drag the `▼` handle in the Scene view to adjust segment sag directly |
| | **Surface collision** | Prevent sagging cables from clipping through floors or objects |
| | **Twist correction** | Rotate knots manually along the curve axis (with 15° reset) |
| | **Attachments** | Attach connectors or accessories to follow individual control points (models and offsets are adjusted inside the window) |
| **Cable Type** | **7 Built-in profiles** | Heavy, Flat, Parallel, Bundled, Cluster, Spiral tube, and Twisted profiles |
| | **Duplicate & Edit profile** | Create a standalone copy of a profile without affecting other cables |
| | **Adaptive segmentation** | Subdivide only curved sections while keeping straight sections low-poly |
| | **UV / Lightmap support** | Length-proportional UV tiling and non-overlapping UV1 lightmap shelf packing |
| **Output** | **Procedural LOD generation** | Automatically generate LOD0–2 meshes and configure a `LODGroup` |
| | **Single-chain bones** | Automatically create a bone hierarchy and analytic skin weights for PhysBone |
| | **Mesh / FBX / Prefab export** | Non-destructive baking that places results and preserves the source cable |

---

## 3. Feature Details

### 3.1 Wiring (Wire Tab)

#### Wiring Method
- **Wire between 2 points (`tile-pick`)**:
  - Click any two points on mesh surfaces to generate a cable with natural tangent elevation based on surface normals.
  - **Details**: Adjust `Tangent handle length` to control the curvature rising from the endpoints.
- **Wire along stroke (`tile-draw`)**:
  - Drag across mesh surfaces to capture a stroke. The 3D RDP (Ramer–Douglas–Peucker) algorithm extracts an optimal set of control points.
  - **Details**:
    - `Surface offset (m)`: Clearance distance from surfaces (default: 0.003m = 3mm).
    - `Simplify`: Tolerance threshold for point simplification. Larger values reduce knot count.
    - `Sample spacing (px)`: Mouse sampling interval while dragging.
    - `Generated point type`: Tangent mode for newly created points (Auto / Smooth).
- **Ignore back faces** (a detail setting shared by both methods; default: on): Faces seen from behind are not hit, so surfaces on the far side of a wall or floor are not picked up. When it is off, a `Back faces` chip is shown while the details are collapsed.

#### Knots & Operations
- **Knot list**: Displays knot indices and positions. Clicking an entry frames the Scene view on that point.
- **Quick buttons**:
  - `＋`: Adds a control point after the selected point (or at the end).
  - `⇄ (Flip)`: Reverses the direction of the cable (swaps start and end).
  - `⟺ (Even)`: Redistributes control points evenly along the total curve length.
- **Loop**: Closes the curve by smoothly connecting the end point back to the start.
- **Subdivide & Add (inside Details)**:
  - `Divide start to end`: Re-places points evenly spaced along the straight line between start and end.
  - `Subdivide all segments`: Inserts a control point at the midpoint of every segment.
  - `New point type`: Tangent mode applied to newly added points (Auto / Smooth / Corner).

#### Snap to Surface
- Projects selected control points onto the closest mesh surface in a chosen direction.
- **Direction**: `Down` (Vector3.down) / `Up` (Vector3.up) / `Custom` (user-defined Vector3).
- **Cable local direction**: Rotates the projection vector in the cable's local coordinate space.
- **Max distance (m)**: Points with no detected surface within this distance remain untouched.
- **Target layers**: LayerMask used for raycasting (colliders are not required).
- **Ignore back faces** (inside Details; default: on): Points are not snapped onto faces that face away from the projection direction.

---

### 3.2 Point Adjustment (Points Tab)

#### Selected Point
Clicking a control point (or marquee selecting multiple points) in the Scene view exposes its properties:

- **Position / Space**: Edit coordinates in either World or Local space.
- **Tangent Mode**:
  - **Auto**: Tangents are automatically computed to ensure smooth curves (recommended).
  - **Smooth**: Tangent handles on both sides are aligned collinear and mirrored.
  - **Corner**: Tangent handles on in/out sides can be adjusted independently (ideal for sharp 90° bends).
- **Tangent length**: Individually tune the in and out handle lengths.
- **Rotation (Twist correction)**: Rotate the cross-section around the curve axis to correct twisting in sharp curves. Click the reset button to return to 0.

#### Attachments
Make models such as connectors or cable ties follow the selected point. They track the point's position and orientation automatically. Everything is set up in this card; you don't need the Inspector.

- **+ Attach**: Adds an attachment to the selected point (only when exactly one point is selected). You can also add one with `Add attachment` on the S1 scene toolbar, which opens this tab.
- When no point is selected, the cable's attachments are listed; click one to select its point.
- Settings for each attachment:
  - **Point index**: The point it is attached to. Changing it moves the attachment to that point.
  - **Prefab / FBX**: The model to place.
  - **Position offset / Rotation offset / Scale**: Offset from the point's position and orientation.
  - **Update attachment**: Recreates a missing model and realigns it to the point.
  - **Trash icon**: Removes the attachment.
- Exports (mesh / FBX / prefab) include the attachment models.

#### Sag
- **All (m)**: Adjusts sag simultaneously across all segments with a single slider.
- **Segment details**:
  - Inspect and configure sag drop individually for each segment.
  - **Symmetric handles / Tangent handle length**: Fine-tune the shape and curvature of the sag handles.
  - **Insert sag points (V1 style)**: Converts procedural sag calculation into physical midpoint control points lowered in space.

#### Surface Collision (Drape along surfaces)
- When enabled, cables sagging downward will automatically conform to and rest on floor or obstacle mesh surfaces without penetrating them.
- **Surface layers**: LayerMask specifying which surfaces participate in collision detection.

---

### 3.3 Cable Profile (Cable Type Tab)

#### Cross-section preview
The top of the tab shows a large drawing of the selected profile's cross-section. It redraws as soon as you change a value. The line below shows the vertex count, the diameter and the profile type.

#### Profiles
CableGenerator V2 includes 7 pre-built profile assets (in the `Profile_sample` folder). New cables start with **Heavy**:

1. **Heavy**: Large single-conductor high-voltage cable or industrial hose.
2. **Flat**: Flat flexible cable (FFC) or ribbon cable.
3. **Parallel**: Multiple conductors aligned side-by-side in parallel.
4. **Bundled**: A main core wire surrounded by bundled outer wires.
5. **Cluster**: Multiple individual insulated wires randomly grouped together.
6. **Spiral tube**: Corrugated protective sleeve or flexible spiral hose.
7. **Twisted**: Twisted-pair wiring with repeating helical spirals.

- **Duplicate to edit**: Duplicates the current profile into a new dedicated asset for this cable, allowing custom edits without affecting other cables.
- **+ New**: Creates a brand new default profile of any chosen type.

#### Thickness & Resolution
- **Thickness (`radiusScale`)**: Multiplier for the overall cross-sectional diameter.
- **Segmentation**:
  - **Auto**: Curvature-adaptive subdivision. Straight spans use fewer polygons while sharp bends receive dense subdivisions.
  - **Even**: Subdivides the cable at a fixed resolution along its entire length.
- **Angle tolerance (°) / Segments (`resolution`)**: Thresholds controlling adaptive division sensitivity and cross-sectional density.
- **UV tiling**: Texture repeat count along the length of the cable.

---

### 3.4 Output, LOD & Bones (Output Tab)

#### Level of Detail (LOD)
- **LOD toggle**: Automatically generates 3 mesh levels integrated into a standard Unity `LODGroup`:
  - **LOD0**: Full quality mesh (100%).
  - **LOD1**: Mid distance (approx. 50% polygon count).
  - **LOD2**: Far distance (approx. 30% lightweight mesh).
  - **Cull**: Screen relative transition height below which the mesh is completely culled from rendering.

#### Format & Export
- **Mesh (.asset)**:
  - Saves generated meshes as project `.asset` files and places a baked GameObject in the scene.
- **FBX**:
  - Exports a baked `.fbx` model file to the project folder.
  - **LOD to export**: Select a specific LOD level (LOD0, LOD1, or LOD2) to export.
  - **Add bones**:
    - Automatically builds a single-chain bone hierarchy (`Bone_0` to `Bone_N`) with analytic skin weights for `SkinnedMeshRenderer`.
    - **Bone count**: Specifies the number of bones along the cable.
    - Attach VRChat PhysBone components to `Bone_0` (the root bone) to enable physics simulation.
  - **Place in scene**: Automatically instantiates the exported FBX model into the scene.
- **Prefab (LOD)**:
  - Saves a `.prefab` containing three mesh levels — LOD0 (full), LOD1 (approx. 50%) and LOD2 (approx. 30%) — and a `LODGroup` that switches between them.
  - LODs are included even when the LOD toggle is off. The switch thresholds come from the Mid / Far / Cull values in the LOD section.
  - The three meshes are saved together in `..._LODs.asset` next to the prefab. When moving the prefab to another folder, move this asset with it.
  - Attached models (connectors, etc.) are included in the prefab.
  - **Place in scene**: Places an instance of the saved prefab at the source cable's position.

#### Upload Directly Without Exporting
Generated meshes (including LOD1, LOD2, and the `LODGroup` when LOD is enabled) are serialized directly into the scene file. Therefore, you can upload your VRChat world without performing any export (bake) steps. If you want to modify cable shapes after uploading, simply edit the cables directly in your scene.

- The `.unity` scene file size will increase depending on the number of cables and vertices. If this is a concern, use the export options below to externalize meshes as standalone files.
- If a cable shape changes (for example, due to edits to a shared CableProfile asset), the scene may be marked unsaved when opened. Please save the scene before uploading.
- Even if you remove the `CableGenerator` component, the generated mesh at that moment remains intact on the GameObject.

#### Non-Destructive Baking Workflow
Upon export, the original `CableGenerator` object is deactivated, tagged with `EditorOnly`, and safely preserved in the scene hierarchy.  
If you need to make changes, simply delete the exported object, reactivate the original cable, and continue editing seamlessly.

---

### 3.5 Scene View Operations (S1 Toolbar & Direct Manipulation)

When a cable is selected, the S1 floating toolbar appears at the top of the Scene view.

#### S1 Toolbar Controls
- **Wiring group**: Toggle buttons to start/end `Wire between 2 points` or `Wire along stroke`.
- **Selected knot group**:
  - Switch tangent modes (`Auto` / `Smooth` / `Corner`).
  - `Snap to surface`: Project selected points downward onto the nearest surface.
  - `Add attachment`: Adds an attachment to the selected point and opens the **Points** tab of the window, where you set the model and offsets.
  - `Delete`: Remove selected control points (at least 2 points are always preserved).
- **Global cable toggles**:
  - `Drape along surfaces`: Toggle surface collision on/off.
  - `LOD`: Toggle procedural LOD generation on/off.
- **Open window**: Immediately bring the Cable Generator tool window to the front.

#### Direct Scene Manipulation
- **Selecting knots**:
  - Click a sphere handle to select a knot.
  - `Shift` or `Ctrl` click to select multiple knots.
  - Drag a marquee box across empty space in the Scene view to box-select.
- **Position arrows**: Drag standard translation gizmo handles to move points.
- **Insert knot button (`⊕`)**: Click the `⊕` ghost icon at the center of any segment to insert a new control point at that position.
- **Sag handle (`▼`)**: Drag the `▼` icon downward at the midpoint of a segment to directly adjust the sag depth.

---

## 4. Inquiries & Support

不明点があれば dennokoworks のBOOTHメッセージまで  
(If you have any questions, bug reports, or feature requests regarding this tool, please feel free to contact us via **dennokoworks BOOTH message**.)


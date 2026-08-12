# `item.info.json` Reference

`item.info.json` is the preferred configuration format for `FbxGbxConverter`.

It is a JSON-based configuration file that provides item metadata, mesh and material configuration, placement behavior, lighting, waypoints, and LOD information that cannot reliably be derived from the FBX itself.

Fields are described below using their serialized property names.

---

## `ItemConfig`

The root object of the configuration file.

| Field                   | Type                   | Default          | Description                                                             |
| ----------------------- | ---------------------- | ---------------- | ----------------------------------------------------------------------- |
| `AuthorName`            | `string`               | `""`             | Name of the item author.                                                |
| `Name`                  | `string?`              | `null`           | Optional name of the item.                                              |
| `Description`           | `string?`              | `null`           | Optional description of the item.                                       |
| `Waypoint`              | `Waypoint?`            | `null`           | Optional waypoint configuration.                                        |
| `PivotsPositions`       | `PivotPosition[]?`     | `null`           | Optional list of pivot positions.                                       |
| `PivotRotations`        | `PivotRotation[]?`     | `null`           | Optional list of pivot rotations.                                       |
| `PlacementParams`       | `PlacementParameters?` | `null`           | Optional parameters controlling item placement in the editor.           |
| `Scale`                 | `float`                | `1.0`            | Global scale applied to the item.                                       |
| `MaterialConfiguration` | `MaterialConfig[]`     | `[]`             | Material definitions and their corresponding FBX/GBX material mappings. Required for each used material |
| `MeshConfiguration`     | `MeshConfig[]`         | `[]`             | Configuration for individual nodes in the FBX.                          |
| `Lights`                | `LightConfig[]`        | `[]`             | Light definitions to generate with the item.                            |
| `LodParameters`         | `LodParameters?`       | `null`           | Optional LOD configuration.                                             |

---

## `Waypoint`

Defines waypoint-related behavior for the item.

| Field                 | Type            | Default      | Description                                                               |
| --------------------- | --------------- | ------------ | ------------------------------------------------------------------------- |
| `Type`                | `EWaypointType` | `Checkpoint` | Type of waypoint represented by the item.                                 |
| `NoRespawn`           | `bool`          | `false`      | Prevents the waypoint from being used as a respawn point.                 |
| `DefaultGravitySpawn` | `Vec3?`         | `(0, -1, 0)` | Unknown effect. |
| `TorqueX`             | `float?`        | `0`          | Unknown effect.                                                           |
| `TorqueDuration`      | `int?`          | `0`          | Unknown effect.                                                           |

---

## `PivotPosition`

Defines a pivot position available for the item.

| Field | Type   | Default     | Description            |
| ----- | ------ | ----------- | ---------------------- |
| `Pos` | `Vec3` | `(0, 0, 0)` | Position of the pivot. |

Multiple pivot positions can be specified using `PivotsPositions`.

---

## `PivotRotation`

Defines a pivot rotation available for the item.

| Field | Type   | Default  | Description                                        |
| ----- | ------ | -------- | -------------------------------------------------- |
| `Rot` | `Quat` | Identity | Rotation of the pivot represented as a quaternion. |

Multiple pivot rotations can be specified using `PivotRotations`.

---

## `PlacementParameters`

Controls how the item can be placed and manipulated.

| Field                      | Type    | Default | Description                                                                                               |
| -------------------------- | ------- | ------- | --------------------------------------------------------------------------------------------------------- |
| `GridHorizontalStep`       | `float` | `0`     | Horizontal grid placement step.                                                                           |
| `GridHorizontalOffset`     | `float` | `0`     | Horizontal offset applied to grid placement.                                                              |
| `GridVerticalStep`         | `float` | `0`     | Vertical grid placement step.                                                                             |
| `GridVerticalOffset`       | `float` | `0`     | Vertical offset applied to grid placement.                                                                |
| `LevitationVerticalStep`   | `float` | `0`     | Vertical step used when levitating/moving the item.                                                       |
| `LevitationVerticalOffset` | `float` | `0`     | Vertical offset used for levitation.                                                                      |
| `GhostMode`                | `bool`  | `false` | Enables ghost placement mode.                                                                             |
| `OneAxisRotation`          | `bool`  | `false` | Restricts item rotation to only around the Y axis.                                                                      |
| `ManualPivotSwitch`        | `bool`  | `false` | Allows manually switching between configured pivots.                                                      |
| `NotOnItem`                | `bool`  | `false` | ?                                                  |
| `AutoRotation`             | `bool`  | `false` | Automatic rotation.                                                              |
| `PivotSnapDistance`        | `float` | `-1`    | Maximum distance at which a pivot can snap. A value of `-1` disables/indicates no explicit snap distance. |

---

## `MaterialConfig`

Defines how a material in the FBX is converted. **Required for every material used!**

| Field        | Type          | Default | Description                                                                                 |
| ------------ | ------------- | ------- | ------------------------------------------------------------------------------------------- |
| `Name`       | `string`      | `""`    | Name of the material used in the fbx.                                    |
| `Link`       | `string`      | `""`    | Links the configured material to the corresponding material/resource in the Nadeo material library. |
| `Color`      | `Color?`      | `null`  | Optional color override.                                                |
| `PhysicsId`  | `MaterialId?` | `null`  | Optional physics/surface material override.                                               |
| `GameplayId` | `GameplayId?` | `null`  | Optional gameplay material override.                                                      |

### Material linking

`Name` and `Link` are used to associate configuration entries with materials from the FBX/conversion pipeline.

`item.fbx` includes the material names used in blender. `Name` identifies these materials and `Link` is then used to find the entry in the `NadeoImporterMaterialLib.txt`.

This entry defines default material link, uv channels and gameplay effects.

---

## `LodParameters`

Defines global LOD distance settings for the item.

| Field             | Type      | Default | Description                                                   |
| ----------------- | --------- | ------- | ------------------------------------------------------------- |
| `MaxLodDistances` | `float[]` | `[]`    | Maximum distance value associated with each global LOD level. |

The values represent the configured LOD distance.

For example:

```text
MaxLodDistances = [100, 200, 400]
```

corresponds to the configured LOD distance values for the respective LOD levels.
Implicitly the last LOD level is from the last configured distance (or 0 in case of empty array) to infinity.


> **Note:** A configured value of `100` corresponds to approximately `200` units of actual in-game distance.

Every LOD level in the `MeshConfig` must be associated with a distance range.

---

## `MeshConfig`

Defines conversion behavior for an individual mesh.

| Field           | Type                | Default | Description                                                                                  |
| --------------- | ------------------- | ------- | -------------------------------------------------------------------------------------------- |
| `Name`          | `string`            | —       | Name of the mesh in the FBX. Used to identify the node this configuration applies to.        |
| `MeshFlags`     | `MeshFlags`         | `None`  | Flags controlling how the mesh is converted and represented. Multiple flags can be combined. |
| `TriggerEffect` | `LegacyGameplayId?` | `null`  | Optional gameplay/trigger effect associated with the mesh.                                   |
| `WaypointType`  | `EWaypointType?`    | `null`  | Optional waypoint type assigned to the mesh.                                                 |
| `MovingGroup`   | `string?`           | `null`  | Optional moving group identifier for meshes that belong to a moving object/group.            |
| `Lods`          | `int[]`             | `[]`    | LOD indices pointing to the global MaxLodDistances.                             |

### `Lods`

The `Lods` field specifies the global LOD levels for which the mesh has geometry.

The indices do not have to be contiguous.

For example:

```json
{
  "Name": "Building",
  "Lods": [1, 3, 5]
}
```

means that the mesh provides geometry for LODs `1`, `3`, and `5`.

An empty list means that the mesh has no LOD variance.

>**Note:** Constructed Gbx structures only allow max 4 LOD levels per mesh (one of which is the infinity LOD), so additional LODs will lead to the mesh data being duplicated to achieve the requested LOD levels.

---

## `MeshFlags`

`MeshFlags` controls special behavior of individual nodes.

Flags can be combined using their bitwise values.

| Flag              | Value | Description                                                                      |
| ----------------- | ----: | -------------------------------------------------------------------------------- |
| `None`            |   `0` | No special behavior.                                                             |
| `NonCollidable`   |   `1` | Mesh does not participate in collision.                                          |
| `Invisible`       |   `2` | Mesh is not rendered.                                                            |
| `Moving`          |   `4` | Mesh is treated as part of a moving object.                                      |
| `TriggerEffect`   |   `8` | Mesh acts as a trigger for a gameplay effect.                                    |
| `TriggerWaypoint` |  `16` | Mesh acts as a waypoint trigger.                                                 |
| `Socket`          |  `32` | Mesh is ignored and position is treated as a socket for waypoint respawns.                                                     |
| `NoMerge`      |  `64` | Meshes will receive their own group rather than being merged with other similar type geometry. |
| `Skip`            | `128` | Node is skipped during conversion.                                               |

Example:

```json
{
  "Name": "CollisionOnly",
  "MeshFlags": "Invisible"
}
```

The default NadeoImporter groups all meshes into 1 mesh structure producing 1 lightmap.

You can use `NoMerge` to force a mesh to be treated as a separate mesh structure having its own lightmap and is easily identified using Editor++ in the ingame editor.

```json
{
  "Name": "Main_Mesh",
  "MeshFlags": "None"
},
{
  "Name": "CustomGroup",
  "MeshFlags": "NoMerge"
}
```

In the resulting Gbx structure this will produce 2 mesh structures, one for `Main_Mesh` and one for `CustomGroup`.

```json
{
  "Name": "MovingMesh",
  "MeshFlags": "Moving"
},
{
  "Name": "StaticMesh",
  "MeshFlags": ""
},
{
  "Name": "StaticMesh_2",
  "MeshFlags": ""
}
```

Moving nodes will always get their own group / mesh structure unless you specify a `MovingGroup`. This example will produce 2 mesh structures, one for `MovingMesh` and one for the other 2 static meshes.

>**Note:** Only 1 socket is allowed per item. If multiple meshes are marked as `Socket`, the last one found will be used and the others will be ignored.

---

## `LightConfig`

Defines a light generated as part of the GBX item.

| Field                 | Type        | Default | Description                            |
| --------------------- | ----------- | ------- | -------------------------------------- |
| `Name`                | `string`    | —       | Name identifying the node.            |
| `Type`                | `LightType` | —       | Type of light. (Point=0, Spot=1)                         |
| `Color`               | `Color`     | —       | Light color.                           |
| `Intensity`           | `float`     | —       | Light intensity.                       |
| `Distance`            | `float`     | —       | Maximum/functional light distance.     |
| `NightOnly`           | `bool`      | `false` | Restricts the light to nighttime.      |
| `PointEmissionRadius` | `float`     | `0`     | Radius of point-light emission.        |
| `PointEmissionLength` | `float`     | `0`     | Length of point-light emission.        |
| `SpotInnerAngle`      | `float`     | `40`    | Inner cone angle for spot lights.      |
| `SpotOuterAngle`      | `float`     | `60`    | Outer cone angle for spot lights.      |
| `SpotEmissionSizeX`   | `float`     | `0`     | X dimension of the spot emission area. |
| `SpotEmissionSizeY`   | `float`     | `0`     | Y dimension of the spot emission area. |

Fields specific to point or spot lights only have an effect when the corresponding `Type` is used.

---


## Optional Configuration Sections

The following sections are optional and are omitted from serialized JSON when they are not configured:

* `Waypoint`
* `PivotsPositions`
* `PivotRotations`
* `PlacementParams`
* `LodParameters`

This allows a minimal configuration to contain only the information required for the specific item.

## Minimal Example

```json
{
  "Type": "StaticObject",
  "Collection": "Stadium",
  "AuthorName": "Author",
  "Name": "Example Item",
  "Scale": 1.0,
  "MaterialConfiguration": [
    {
      "Name": "TM_DecoTechnics_asset",
      "Link": "DecoTechnics"
    }
  ],
  "MeshConfiguration": [
    {
      "Name": "MainMesh",
      "MeshFlags": "None"
    }
  ],
  "Lights": []
}
```

## Configuration and FBX

The configuration references objects contained in the FBX by name. Therefore, changes to mesh or material names in Blender can require corresponding changes to `item.info.json`.

The FBX and configuration should be treated as a single input asset:

```text
<item>.fbx
<item>.info.json
```

Both files must remain consistent with each other for conversion to succeed.

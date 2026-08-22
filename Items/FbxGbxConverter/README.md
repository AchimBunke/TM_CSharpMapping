# FBX → GBX Converter

This component converts Blender-exported **FBX assets** into **GBX**.

It is designed after the NadeoImporter and supports multiple input configuration formats.

The converter accepts:

* `<item>.fbx` — the FBX exported from Blender.
* `<item>.info.json` — the preferred configuration format, providing information that cannot be reliably derived from the model itself.
* `<item>.Item.xml` + `<item>.MeshParams.xml` — the legacy configuration format used by the original NadeoImporter.
* `NadeoImporterMaterialLib.txt` — the material library used to create material links.

Legacy configuration files are converted internally into an `item.info.json` representation before the main conversion process. This keeps the conversion pipeline consistent while maintaining compatibility with existing NadeoImporter-based workflows.

**`<item>.info.json` is the preferred format**, as it provides additional configuration options and enables more converter functionality.

## Overview

The converter accepts an FBX together with either the modern `item.info.json` or the legacy NadeoImporter configuration files.

```text
                         Configuration
                              │
              ┌───────────────┴───────────────┐
              │                               │
            .info.json           .Item.xml + .MeshParams.xml
              │                        (legacy format)
              │                               │
              │                       ┌───────▼───────┐
              │                       │ Legacy →      │
              │                       │ info.json     │
              │                       └───────┬───────┘
              │                               │
              └───────────────┬───────────────┘
                              ▼
                          ItemConfig             NadeoImporterMaterialLib.txt
                              │                               │
                              │                               │
Blender                       │                               │
   │                          │                               │
   │ FBX export               │                               │
   ▼                          ▼                               │
┌─────────────┐       ┌────────────────────┐                  │
│    .fbx     │       │     ItemConfig     │                  │
│   meshes    │       │ item data          │                  │
│   geometry  │       │ materials          │                  │
│             │       │ mesh flags         │                  │
└──────┬──────┘       │                    │                  │
       │              └─────────┬──────────┘                  │                    
       └──────────────┬─────────┘ ────────────────────────────┘
                      ▼
              FBX → GBX Converter
                      │
                      ▼
                    .gbx
```

## Conversion

At a high level, conversion consists of:

1. Reading `item.info.json`
2. Reading the referenced FBX
3. Resolving meshes and materials
4. Applying mesh-specific configuration and flags
5. Applying item configuration such as placement parameters
6. Building the corresponding GBX structures
7. Writing the resulting GBX

## Input

### FBX

The FBX is expected to be exported from Blender and contains the actual model data, including meshes and their geometry.

If you want to keep a high degree of controllability over the converted geometry, triangulate and UV unwrap the meshes in Blender manually before exporting to FBX.

### `item.info.json`

>see [Item Configuration](./ItemConfig.md)

The configuration file contains additional information used during conversion, including:

* Item-specific metadata
* Mesh definitions
* Material definitions

The configuration allows the converter to distinguish between information contained in the FBX and information that is specific to the GBX item.



## Compared to NadeoImporter

FbxGbxConverter supports CPLugPrefab Gbx structures which allow a variety of features that are not supported by the NadeoImporter.
- Moving Items
- Gamplay Triggers (Gates)
- Infinite LODs
- Custom Mesh grouping

A cost to using CPLugPrefabs is that they alway require a Surface shape. Each mesh produces to outputs, a visible mesh and a surface for collisions. The NadeoImporter only created a collision surface if parts of the mesh were invisible or non-collidable, otherwise they could use the visual mesh data as collision source reducing item size.


##  List of Features

## Features

| Feature | Status | Info |
|---|---|---|
| Static Items | 🟢 Implemented |
| Checkpoints | 🟢 Implemented |
| Gates | 🟢 Implemented |
| Moving Items and groups | 🟢 Implemented | Also moving configuration
| Moving Items with separate anchor point | 🟢 Implemented | how to set anchor point?
| Moving Items with Triggers | ⚪ To investigate | Is it possible? |
| Moving Items with relative Animations | 🟢 Implemented  | Non-collidable only |
| LOD (any number) | 🟢 Implemented & ⚪ Investigating | Investigating to allow infinite LOD without splitting model |
| Blocks | 🔴 Not Planned Yet | will require lots of additional research |
| Default Placement | 🟢 Implemented | |
| Advanced Placement | 🟢 Implemented | Attachement to Boarders, Blocks & Edges |
| Variant Items | 🟡 Planned | How to define variant sets (each variant = 1 item)
| Animated Textures (moving items) | 🟡 Partially Implemented | Easier way to configure animated textures? |
| All Materials | 🟢 Implemented | Just accept MaterialLib.txt |
| Lights | 🟢 Implemented | Currently only valid if at least 1 static model |
| Mesh Modeler Items | 🟢 Implemented | Conversion to mesh modeler items |
| MediaTracker Triangles | 🟡 Planned | Convert mesh to MediaTracker 3D triangles |
| MediaTracker Triangles Animations | 🔴 Not Planned | Convert mesh to MediaTracker 3D triangles |
| Modify Trigger GameplayMainDir | 🟢 Implemented  | |
| Water | 🟡 Planned | |
| Force Lightmap size | 🟢 Implemented | Evaluate if current version is useful |

<!--
| Feature 1 | 🟢 Implemented | Short description / notes |
| Feature 2 | 🟡 Planned | What needs to be done |
| Feature 3 | 🔴 Not Planned | Reason or additional info |
| Feature 4 | ⚪ Investigating | Current findings / open questions |
||🟠 On Hold — Temporarily paused / blocked by another dependency.
-->
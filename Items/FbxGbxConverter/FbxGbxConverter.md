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
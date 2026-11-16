using GBX.NET.Engines.Plug;
using TM_GenericMapping.Items.FbxGbxConversion.Serialization;
using TM_GenericMapping.Items.MeshCompilation;

namespace TM_GenericMapping.Items.FbxGbxConversion;

/// <param name="TextureAssets">Set if the material config defines Color/Normal/Roughness images (tree material). Null for regular materials.</param>
internal record MaterialDef(CPlugMaterialUserInst MaterialInstance, DMaterial? DMaterial, NormalizedTextureAssets? TextureAssets = null);

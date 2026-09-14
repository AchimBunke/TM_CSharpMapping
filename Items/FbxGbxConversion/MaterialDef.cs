using GBX.NET.Engines.Plug;
using TM_GenericMapping.Items.FbxGbxConversion.Serialization;

namespace TM_GenericMapping.Items.FbxGbxConversion;

internal record MaterialDef(CPlugMaterialUserInst MaterialInstance, DMaterial? DMaterial);

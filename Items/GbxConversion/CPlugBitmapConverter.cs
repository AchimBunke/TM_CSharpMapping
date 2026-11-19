using GBX.NET;
using GBX.NET.Components;
using GBX.NET.Engines.Plug;
using System.Text.Json;
using System.Text.Json.Serialization;
using TM_GenericMapping.Common;
using TM_GenericMapping.Messaging;
using TM_GenericMapping.Templating;
using static GBX.NET.Engines.Plug.CPlugBitmap;
using Color = System.Drawing.Color;

namespace TM_GenericMapping.Items.GbxConversion;

public class BitmapConfig
{
    [JsonPropertyName("ImageFile")]
    public string ImageFile { get; set; } = "";

    [JsonPropertyName("Usage")]
    public EUsage Usage { get; set; } = EUsage.Color;

    [JsonPropertyName("BumpScaleFactor")]
    public float BumpScaleFactor { get; set; } = 1f;

    [JsonPropertyName("BumpScaleMipLevel")]
    public float BumpScaleMipLevel { get; set; } = 0.5f;

    [JsonPropertyName("BorderRGB")]
    public Color BorderRGB { get; set; } = Color.Black;

    [JsonPropertyName("ClearColor")]
    public Color ClearColor { get; set; } = Color.Black;

    [JsonPropertyName("DefaultRenderClearColor")]
    public Color DefaultRenderClearColor { get; set; } = Color.Black;

    [JsonPropertyName("DefaultTexCoordRotate")]
    public float DefaultTexCoordRotate { get; set; } = 0f;

    [JsonPropertyName("DefaultTexCoordScale")]
    public Vec2 DefaultTexCoordScale { get; set; } = new Vec2(1f, 1f);

    [JsonPropertyName("DefaultTexCoordTrans")]
    public Vec2 DefaultTexCoordTrans { get; set; } = new Vec2(0f, 0f);

    [JsonPropertyName("GrassId")]
    public string GrassId { get; set; } = "";

    [JsonPropertyName("GrassIdTexCoordRotate ")]
    public float GrassIdTexCoordRotate { get; set; } = 0;

    [JsonPropertyName("GrassIdTexCoordScale")]
    public Vec2 GrassIdTexCoordSize { get; set; } = new Vec2(1f, 1f);

    [JsonPropertyName("GrassIdTexCoordTrans")]
    public Vec2 GrassIdTexCoordTrans { get; set; } = new Vec2(0f, 0f);

    [JsonPropertyName("HeightInMeters")]
    public float HeightInMeters { get; set; } = 1;

    [JsonPropertyName("MipMapFadeAlphas")]
    public float[] MipMapFadeAlphas { get; set; } = [];

    [JsonPropertyName("MipMapLodBiasDefault")]
    public float MipMapLodBiasDefault { get; set; } = 0;

    [JsonPropertyName("MipMapLowerAlpha")]
    public float MipMapLowerAlpha { get; set; } = 0;

    [JsonPropertyName("PixelUpdate")]
    public int PixelUpdate { get; set; } = 0;

    [JsonPropertyName("RenderSizeDiv")]
    public uint RenderSizeDiv { get; set; } = 1;

    [JsonPropertyName("RenderSizeMul")]
    public uint RenderSizeMul { get; set; } = 1;


    public static string Serialize(BitmapConfig config)
    {
        return JsonSerializer.Serialize(config, new JsonSerializerOptions
        {
            WriteIndented = true
        });
    }
    public static BitmapConfig Deserialize(string json)
    {
        return JsonSerializer.Deserialize<BitmapConfig>(json)!;
    }
}

public class CPlugBitmapConverter
{
    public ToolResult<CPlugBitmap> Convert(BitmapConfig config)
    {
        return CreateBitmapFromConfig(config);
    }

    public ToolResult<CPlugBitmap> ConvertAndSave(BitmapConfig config, string filePath)
    {
        var bitmapResult = Convert(config);
        if (bitmapResult.IsFailure)
            return bitmapResult;
        bitmapResult.Value.Save(filePath);
        return bitmapResult;

    }

    ToolResult<CPlugBitmap> CreateBitmapFromConfig(BitmapConfig config)
    {
        var template = GbxTemplateLibrary.CreateBitmapGbxTemplate().Value;
        var imageFile = NodeRefUtils.CreateFile(config.ImageFile);
        var templateNode = template.Node;

        templateNode.References.Image.Descriptor = imageFile;
        
        templateNode.Usage = config.Usage;
        templateNode.BumpScaleFactor = config.BumpScaleFactor;
        templateNode.BumpScaleMipLevel = config.BumpScaleMipLevel;
        templateNode.BorderRGB = config.BorderRGB.ToRGBInt();
        templateNode.ClearColor = BitConverter.ToUInt32(BitConverter.GetBytes(config.ClearColor.ToArgb()));
        templateNode.DefaultRenderClearColor = BitConverter.ToUInt32(BitConverter.GetBytes(config.DefaultRenderClearColor.ToArgb()));
        templateNode.DefaultTexCoordRotate = config.DefaultTexCoordRotate;
        templateNode.DefaultTexCoordScale = config.DefaultTexCoordScale;
        templateNode.DefaultTexCoordTrans = config.DefaultTexCoordTrans;
        templateNode.GrassId = config.GrassId;
        templateNode.GrassIdTexCoordRotate = config.GrassIdTexCoordRotate;
        templateNode.GrassIdTexCoordSize = config.GrassIdTexCoordSize;
        templateNode.GrassIdTexCoordTrans = config.GrassIdTexCoordTrans;
        templateNode.HeightInMeters = config.HeightInMeters;
        templateNode.MipMapFadeAlphas = config.MipMapFadeAlphas;
        templateNode.MipMapLodBiasDefault = config.MipMapLodBiasDefault;
        templateNode.MipMapLowerAlpha = config.MipMapLowerAlpha;
        templateNode.PixelUpdate = config.PixelUpdate;
        templateNode.RenderSizeDiv = config.RenderSizeDiv;
        templateNode.RenderSizeMul = config.RenderSizeMul;



        return ToolResult.Success(templateNode, nameof(CPlugBitmapConverter));   
    }
}

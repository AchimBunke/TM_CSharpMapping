using GBX.NET;
using GBX.NET.Engines.Game;
using GBX.NET.Inputs;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;

namespace TM_GenericMapping.Common;

/// <summary>
/// Something thats rendered by a IRenderer.
/// </summary>
public abstract class RenderObject : MediaObject
{

    public int Order { get; set; } = 0;
    public bool CanShareBlock { get; set; } = false;

    private List<PostProcessingEffect> _localWorldSpacePostProcessingEffects = new();
    public IReadOnlyList<PostProcessingEffect> LocalWorldSpacePostProcessingEffects => _localWorldSpacePostProcessingEffects;
    private List<PostProcessingEffect> _localNDCPostProcessingEffects = new();
    public IReadOnlyList<PostProcessingEffect> LocalNDCPostProcessingEffects => _localNDCPostProcessingEffects;

    protected RenderObject([NotNull] IRenderer renderer) 
    {
        Renderer = renderer ?? Rendering.DefaultTriangleRenderer ?? throw new ArgumentNullException(nameof(renderer));
    }
    protected RenderObject(RenderObject other) : base(other)
    {
        Order = other.Order;
        Renderer = other.Renderer;
        CanShareBlock = other.CanShareBlock;
        _localWorldSpacePostProcessingEffects = other._localWorldSpacePostProcessingEffects.ToList();
        _localNDCPostProcessingEffects = other._localNDCPostProcessingEffects.ToList();
    }

    public IRenderer Renderer { get; set; }

    public void AddLocalNDCPostProcessingEffect(PostProcessingEffect effect)
    {
        _localNDCPostProcessingEffects.Add(effect);
    }
    public void AddLocalWorldSpacePostProcessingEffect(PostProcessingEffect effect)
    {
        _localWorldSpacePostProcessingEffects.Add(effect);
    }
}


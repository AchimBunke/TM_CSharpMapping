using System.Collections.ObjectModel;
using System.Numerics;
using TM_GenericMapping.Common;

namespace TM_GenericMapping.Common;
public enum Space
{
    World,
    Local
}
public enum OutlineExtendsDirection
{
    Inwards,
    Outwards,
    Bidirectional
}
public interface IFillable
{
    void SetFilled(bool filled);
    bool IsFilled { get; }
    bool CanFill { get; }
}
public interface IOutlineable
{
    bool HasOutline { get; }
    float OutlineWidth { get; }
    void SetOutlineWidth(float width);
    OutlineExtendsDirection OutlineExtends{ get; }
}
public interface ICloneable<T> where T: class
{
    T Clone();
}


public abstract class MediaObject : ICloneable<MediaObject>
{

    protected MediaObject() { }

    protected MediaObject(MediaObject other)
    {
        Name = other.Name;
        localPosition = other.localPosition;
        localScale = other.localScale;
        localRotation = other.localRotation;
        localToWorldTRS = other.localToWorldTRS;
        isLocalToWorldTRSDirty = other.isLocalToWorldTRSDirty;
        foreach(var o in other.SubObjects)
        {
            AddSubObjects(o.Clone());
        }
        foreach(var cmp in other.Components)
        {
            if (cmp is ICloneableComponent cloneable)
                components.Add(cloneable.Clone(this));
            else
                components.Add(cmp);
        }
    }

    public HashSet<MediaObject> SubObjects { get; } = [];
    public MediaObject Parent { get; private set; } = null!;
    /// <summary>
    /// TODO: Change (only use for composite which should implement custom logic for shape creation!!)
    /// </summary>
    /// <param name="parent"></param>
    internal void SetParent(MediaObject parent)
    {
        Parent = parent;
    }
    public string Name { get; set; } = "RenderObject";

    public uint LayerMask { get; set; } = 0; // 0 = no layer
    public void SetLayerMask(params ReadOnlySpan<string> layers)
    {
        LayerMask = 0;
        foreach (var layer in layers)
        {
            LayerMask |= LayerManager.GetLayerMask(layer);
        }
    }
    public void AddLayer(string layer) => LayerMask |= LayerManager.GetLayerMask(layer);
    public void RemoveLayer(string layer) => LayerMask &= ~LayerManager.GetLayerMask(layer);


    private Vector3 localPosition = Vector3.Zero;
    public Vector3 LocalPosition 
    {
        get => localPosition;
        set => SetLocalPosition(value);
    }
    public Vector3 Position
    {
        get => LocalToWorldTRS.Translation;
        set => SetWorldPosition(value);
    }
    void SetLocalPosition(Vector3 position)
    {
        if (position == localPosition)
            return;
        localPosition = position;
        SetLocalToWorldTRSDirty();
    }
    void SetWorldPosition(Vector3 position)
    {
        if (Parent is null)
        {
            SetLocalPosition(position);
            return;
        }
        if (Matrix4x4.Invert(Parent.LocalToWorldTRS, out var inverse))
        {
            SetLocalPosition(Vector3.Transform(position, inverse));
        }
        else
        {
            throw new Exception("Cannot invert LocalToWorld!");
        }
    }
    public void SetPosition(Vector3 position, Space space)
    {
        if (space == Space.Local)
            SetLocalPosition(position);
        else
            SetWorldPosition(position);
    }
    public void SetPosition(ScreenPosition screenPos, Space space)
        => SetPosition(screenPos.ToVector3(), space);

    private Vector3 localScale = Vector3.One;
    public Vector3 LocalScale
    {
        get => localScale;
        set => SetLocalScale(value);
    }
    public Vector3 LossyScale
    {
        get
        {
            if (Matrix4x4.Decompose(localToWorldTRS, out var scale, out _, out _))
            {
                return scale;
            }
            throw new Exception("Cannot extract lossy scale");
        }
    }
    void SetLocalScale(Vector3 scale)
    {
        if (scale == localScale)
            return;
        localScale = scale;
        SetLocalToWorldTRSDirty();
    }

    private Quaternion localRotation = Quaternion.Identity;
    public Quaternion LocalRotation
    {
        get => localRotation;
        set => SetLocalRotation(value);
    }
    public Quaternion Rotation
    {
        get
        {
            if (Matrix4x4.Decompose(localToWorldTRS, out _, out var rotation, out _))
            {
                return rotation;
            }
            throw new Exception("Cannot extract rotation");
        }
        set => SetWorldRotation(value);
    }
    void SetLocalRotation(Quaternion rotation)
    {
        if (rotation == localRotation)
            return;
        localRotation= rotation;
        SetLocalToWorldTRSDirty();
    }
    void SetWorldRotation(Quaternion rotation)
    {
        if (Parent is null)
        {
            SetLocalRotation(rotation);
            return;
        }
        if ((Matrix4x4.Invert(Parent.LocalToWorldTRS, out var inverse) && Matrix4x4.Decompose(inverse, out _, out var invRotation, out _)))
        {
            SetLocalRotation(rotation * invRotation);
        }
        else
        {
            throw new Exception("Cannot invert LocalToWorld!");
        }
    }
    public void SetRotation(Quaternion rotation, Space space)
    {
        if (space == Space.Local)
            SetLocalRotation(rotation);
        else
            SetWorldRotation(rotation);
    }

    public Matrix4x4 GetLocalTRS()
    {
        return Matrix4x4.CreateScale(LocalScale) *
            Matrix4x4.CreateFromQuaternion(LocalRotation) *
            Matrix4x4.CreateTranslation(LocalPosition);
    }

    private Matrix4x4 localToWorldTRS;
    public Matrix4x4 LocalToWorldTRS
    {
        get
        {
            if (isLocalToWorldTRSDirty)
                RecalculateLocalToWorldTRS();
            return localToWorldTRS;
        }
        private set
        {
            localToWorldTRS = value;
            isLocalToWorldTRSDirty = false;
        }
    }

    private bool isLocalToWorldTRSDirty = true;
    private void SetLocalToWorldTRSDirty()
    {
        isLocalToWorldTRSDirty = true;
        foreach(var subObject in SubObjects)
        {
            if (!subObject.isLocalToWorldTRSDirty)
                subObject.SetLocalToWorldTRSDirty();
        }
    }
    private void RecalculateLocalToWorldTRS()
    {
        if (Parent is null)
            LocalToWorldTRS = GetLocalTRS() * Matrix4x4.Identity;
        else
            LocalToWorldTRS =GetLocalTRS() * Parent.LocalToWorldTRS;
    }

    public void Translate(Vector3 vec3, Space space = Space.Local)
    {
        if (vec3.LengthSquared() == 0)
            return;
        if (space == Space.Local)
            LocalPosition += vec3;
        else
            Position += vec3;
    }
    public void Rotate(Quaternion rotation, Space space = Space.Local)
    {
        if (rotation == Quaternion.Identity)
            return;
        if (space == Space.Local)
            LocalRotation *= rotation;
        else
            Rotation *= rotation;
    }

    public void AddSubObjects(params ReadOnlySpan<MediaObject> objs)
    {
        foreach (var obj in objs)
        {
            SubObjects.Add(obj);
            obj.Parent = this;
            obj.SetLocalToWorldTRSDirty();
        }
    }


    //public virtual M Animate<M, T>(bool continuosKeyFrames = false)
    //    where T : MediaObject
    //    where M : MediaObjectAnimator<T>
    //    => new MediaObjectAnimator<T>((T)this) { ContinuosKeyFrames = continuosKeyFrames } as M;

    public virtual MediaObjectAnimator<MediaObject> Animate(bool continuosKeyFrames = false, ulong keyframeGenerationRateMillis = 0)
         => new MediaObjectAnimator<MediaObject>(this) { ContinuosKeyFrames = continuosKeyFrames, KeyframeGenerationRateMillis = keyframeGenerationRateMillis };

    public abstract MediaObject Clone();


    HashSet<IMediaObjectComponent> components = [];
    public IReadOnlySet<IMediaObjectComponent> Components => components;
    public T? GetComponent<T>() where T : IMediaObjectComponent
    {
        return components.OfType<T>().FirstOrDefault();
    }
    public bool TryGetComponent<T>(out T cmp) where T : IMediaObjectComponent
    {
        if (components.OfType<T>().Any())
        {
            cmp = GetComponent<T>()!;
            return true;
        }
        else
        {
            cmp = default!;
            return false;
        }
    }
    public void AddComponent<T>(T component) where T : IMediaObjectComponent
    {
        components.Add(component);
    }
    public void AddComponents(params ReadOnlySpan<IMediaObjectComponent> components)
    {
        foreach(var cmp in components)
            AddComponent(cmp);
    }
}

public class MediaObjectGroup : MediaObject
{
    public MediaObjectGroup()
    {
        Name = "MediaGroup";
    }
    protected MediaObjectGroup(MediaObjectGroup other) : base(other)
    {
    }

    public override MediaObjectGroup Clone()
    {
        return new MediaObjectGroup(this);
    }
}


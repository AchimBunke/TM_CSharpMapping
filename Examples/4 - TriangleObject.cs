using System.Drawing;
using System.Numerics;
using TM_GenericMapping.Common;
using TM_GenericMapping.IO;
using static TM_GenericMapping.Common.Conversion;

namespace TM_GenericMapping.Examples;

public class Example_TriangleObject
{
    // TriangleObject are objects made up from triangles that can be rendered into 2D or 3D Triangle tracks in the MediaTracker.
    // As such they consist of Vertices/Points, Triangles and Colors.
    // Each Point is mapped to a single Color so both collections ust be the same size.
    // Each Triangle has 3 indices that map to a Point

    // This library provides a set of predefined TriangleObjects such as:
    // 2D meshes in (X,Y,0):
    //  - Rectangle, Square, Dot, Triangle
    //  - Arc, Circle, Ellipse
    //  - Line
    // 3D meshes in (X,Y,Z)
    //  - Cuboid, Cube
    //  - Sphere
    //  - SweepShape (3D shape along Spline)
    //
    // You can also use your own meshes by providing the mesh data. They can be constructed in two ways:
    // 1. Provide Vertices + Triangles + Colors
    // 2. Provide only Points:
    //      TriangleObject will use 2D Ear Clipping Triangulation to generate triangles;
    //      TriangleObject3D will use 3D Convex Hull to generate 3D mesh;

    // You can have TriangleObjects share the same MediaTracker track by grouping them together under a common parent TriangleObject (if they have the same type of renderer)

    class MySceneBuilder : SceneBuilder
    {
        protected override void Build()
        {
            // Planing to make a 3DTriangles
            Rendering.DefaultTriangleRenderer = new Triangle3DRenderer();

            // Creating a custom 2D mesh by providing only corner points.
            // Also create an outline (a border around that object)
            var custom2DMesh = new TriangleObject(
                points:
                [
                    V3(0,0,0),
                    V3(1,0,0),
                    V3(1,1,0),
                    V3(0,2,0),
                    V3(-1,1,0),
                    V3(-1,0,0),
                ],
                fillColor: Color.Blue,
                withOutline: true,
                outlineWidth: 0.2f,
                outlineColor: Color.Red);
            custom2DMesh.Position = V3(0, 10, 0);

            scene.Add(custom2DMesh);

            // Create a custom 3D mesh shaped like a pyramid
            var custom3DMesh = new TriangleObject3D(
                points:
                [
                    V3(-1,-1,0),
                    V3(1,-1,0),
                    V3(1,1,0),
                    V3(-1,1,0),
                    V3(0,0,1),
                ],
                fillColor: Color.Green);
            custom3DMesh.Position = V3(10, 10, 0);

            scene.Add(custom3DMesh);


            // Creating an empty container object
            var container = new TriangleObject() { Name = "Container" };

            var child_1 = new Cuboid(size: V3(4, 4, 2));
            // This will color each vertex with a different random color to make it easier to recognize triangles
            child_1.WithTriangleVisualizationColors();
            child_1.Position = V3(0, 0, 5);

            var child_2 = new Sphere(radius: 2, resolution: (12, 12));
            child_2.WithTriangleVisualizationColors();

            // Making an object the child of another object will:
            // - Attach that object to the parent (relative position/rotation/scale). Moving the parent will also move the children
            // - If possible combine the vertex/triangle data into a single MediaTracker track.
            //   This will reduce the amount of tracks in the clip. (Also filesize)
            //   !! BUT: If one of the objects inside a shared track requires a keyframe, this will also include the vertex data of all other objects even though they may not need one.
            //           This is especially problematic when using continuous animations (only one obj requires keyframes every 100ms, but each keyframe contains data of 100 other objs)
            // You can disable track sharing by setting:
            // container.CanShareBlock = false; // Only before adding to scene
            container.AddSubObjects(child_1, child_2);

            container.Position = V3(20, 10, 0);
            scene.Add(container);

            // Wait 2 seconds
            scene.Wait(2);

            // Change position of container
            scene.SetPosition(container, V3(20, 10, 20));
            // This will result in both the Cuboid and Sphere moving from one point to another within 2 seconds.

            // Wait for all keyframes/animations to be created/end
            scene.WaitAnimationEnd();
        }
    }

    public void Program()
    {
        // ===================== Setup Gbx =====================
        GbxExtensions.Setup();

        // ===================== Setup Templates (see 1) =====================
        BlockTemplates blockTemplates = MediaTrackerUtils.CreateBlockTemplates();

        // ===================== Create SceneTimeline and SceneBuilder =====================
        // This is the SceneTimeline; A container which is modified by the SceneScript/SceneBuilder
        SceneTimeline sceneTimeline = new SceneTimeline()
        {
            BlockTemplates = blockTemplates,
        };
        // The SceneScript/SceneBuilder contains your code to add objects, step forward in time, animate ...
        ISceneScript sceneBuilder = new MySceneBuilder();

        // ===================== Render SceneTimeline into Clip =====================
        string clipName = "MyClip.Clip.Gbx";
        string clipSavePath = Path.Combine(WindowsUtils.ClipsDirectoryPath, clipName);

        Clip clip = new Clip()
        {
            SavePath = clipSavePath
        };
        // Initialize Clip
        clip.Create(blockTemplates);

        // Render into clip
        // This will call your builder's code and step along the SceneTimeline 
        sceneTimeline.Animate(clip.MediaClip, SceneAnimationSettings.Default, sceneBuilder);

        // Save Clip
        clip.Save();
    }

  
}

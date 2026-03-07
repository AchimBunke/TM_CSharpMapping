using System.Numerics;
using TM_GenericMapping.Common;
using TM_GenericMapping.IO;
using static TM_GenericMapping.Common.Conversion;

namespace TM_GenericMapping.Examples;

public class Example_Scenes
{
    // Scenes are the main way to create MediaTracker clips and animations.
    // They represent a 3D environment in which you can add objects such as triangle based objects.
    // They can simulate time to allow you to move objects, play animations or add custom behaviors.
    // The scene can render its contents into a clip producing keyframes along the simulated timeline.

    // This rendering is optimized for the MediaTracker to minimize keyframes and reducing the map/clip filesize.
    // The MediaTracker will linearly interpolate the position of an object (e.g. image or triangle) between 2 keyframes.
    // So when simulating the timeline it will NOT create keyframes every frame or tick, instead it will try to only create keyframes whenever necessary.

    // Example:
    // 2 different objects in the scene, each in a different 3DTriangles track, a Cube and a Sphere.
    // In the scene simulation, the Cube moves from point A to B at constant speed.
    // At the same time the Sphere moves the same way but accelerates while moving.
    // For the Cube only 2 Keyframes are necessary to accurately movel this animation in the MediaTracker, one in the beginning and one at the end.
    // For the acceleration of the Sphere, each time it is accelerated, a new keyframe must be added (assuming constant acceleration => every frame/tick needs a keyframe).
    // If the SceneTimeline and animations are used correctly, it will automatically recognize where to insert keyframes.


    // How it works:
    // The SceneTimeline contains the scene and can step forward in time only. 
    // A SceneBuilder is where you manage the SceneTimeline - write code to add objects, step forward in time, animate objects...

    public void Program()
    {
        // ===================== Setup Gbx =====================
        GbxExtensions.Setup();

        // ===================== Setup Templates (see 1) =====================
        BlockTemplates blockTemplates = MediaTrackerUtils.CreateBlockTemplates();

        // ===================== Choose Scene Animation Settings =====================
        // AnimationTickRateMillis:         How fast the scene steps forward in time each update 
        // MinKeyFrameTickRateMillis:       The minimum time delay between 2 created Keyframes (avoids creating too many if animation tick rate is low)
        // FallbackAnimationTimeSeconds:    The time after which a second Keyframe is created if only a single one was created during the timeline
        // UpdateTrackOrder:                Sort tracks by provided Order

        // SceneAnimationSettings sceneAnimSettings = SceneAnimationSettings.Default;
        SceneAnimationSettings sceneAnimSettings = new SceneAnimationSettings()
        {
            AnimationTickRateMillis = 20,
            MinKeyFrameTickRateMillis = 100,
            FallbackAnimationTimeSeconds = 1f,
            UpdateTrackOrder = true,
        };

        // ===================== Create SceneTimeline and SceneBuilder =====================
        // This is the SceneTimeline; A container which is modified by the SceneScript/SceneBuilder
        SceneTimeline sceneTimeline = new SceneTimeline()
        {
            BlockTemplates = blockTemplates,
            RenderData = RenderData.Default, // Necessary for 2D only
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
        sceneTimeline.Animate(clip.MediaClip, sceneAnimSettings, sceneBuilder);

        // Save Clip
        clip.Save();
    }

    /// <summary>
    /// This is where you write your code to define the scene and animations.
    /// </summary>
    class MySceneBuilder : SceneBuilder
    {
        /// <summary>
        /// See <see cref="SceneTimeline.Animate(GBX.NET.Engines.Game.CGameCtnMediaClip, SceneAnimationSettings, ISceneScript)"/>
        /// </summary>
        protected override void Build()
        {
            // If your are using SceneBuilder as base class you can acces the scene as a property.
            SceneTimeline scn = scene;

            // At this point your SceneTimeline is at time = 0:00
            // Every object you add here will (probably) create keyframes at 0:00


            // The currently active DefaultTriangleRenderer defines which type of triangles are created (2D/3D/3D Relative to Player/...)
            Rendering.DefaultTriangleRenderer = new Triangle3DRenderer();
            // Create a TriangleObject
            // The current Renderer is 3D so this will create a 3DTriangles track in the clip
            var cube = new Cube(size: 20);
            // Before you add this object to the scene you can modify it however you like (be careful to not mess up mesh data)
            // Moving cube center to Stadium surface (0,8,0)
            cube.Position = WorldPositionExtensions.StadiumSurfaceOffset;
            // Rotating cube around Y
            cube.Rotation = Quaternion.CreateFromYRotationDegrees(45);
            // Changing Scale
            cube.LocalScale = V3(15);

            // Adding Cube to scene
            scene.Add(cube);
            // After an object is added, you should NOT modify it directly unless you understand how keyframes are generated
            // Instead you can modify it thorugh a scenes methods
            scene.SetPosition(cube, cube.Position + V3(5, 10, 5));

            // To step forward in time the scene provides different methods
            // During this time, animations and custom behaviors will be updated
            scene.AnimationStep(); // Steps on animation update
            scene.Wait(1); // Steps 1 second forward

            scene.WaitAnimationEnd(); // I recommend using this at the end because this will step forward in time and make sure that any running animations will finish

            // at this point the scene contains:
            // - 1 Obj (Cube)
            // - 1 Track (3DTriangles)
            //   - 1 Keyframe at t=0 (adding an object will make the scene try to create a keyframe at the first possible time), no animation of obj = no new keyframes

            // The SceneTimeline will make sure that at least 2 keyframes exist so a second one will be created some time after the first.

        }
    }
}

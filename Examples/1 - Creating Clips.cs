using TM_GenericMapping.Common;
using TM_GenericMapping.IO;

namespace TM_GenericMapping.Examples;

public class Example_CreatingClips
{
    public void Program()
    {
        // ===================== Setup Gbx =====================
        GbxExtensions.Setup();

        // ===================== Setup Templates =====================
        // !! IMPORTANT: You need to create the clips used in the next few lines ingame. !!
        // Open the MediaTracker and create and save clips with 1 Track of 2DTriangles, 3DTriangles, Image, Text, <nothing> respectively
        // Set the paths to your specific save location or use the default one
        MediaTrackerUtils.ImageTemplatePath = Path.Combine(WindowsUtils.ClipsDirectoryPath, @"Templates\ImageTemplate.Clip.Gbx");
        MediaTrackerUtils.TextTemplatePath = Path.Combine(WindowsUtils.ClipsDirectoryPath, @"Templates\TextTemplate.Clip.Gbx");
        MediaTrackerUtils.Triangles2DTemplatePath = Path.Combine(WindowsUtils.ClipsDirectoryPath, @"Templates\Triangle2DTemplate.Clip.Gbx");
        MediaTrackerUtils.Triangles3DTemplatePath = Path.Combine(WindowsUtils.ClipsDirectoryPath, @"Templates\Triangle3DTemplate.Clip.Gbx");
        // Can use whatever has a track
        MediaTrackerUtils.TrackTemplatePath = Path.Combine(WindowsUtils.ClipsDirectoryPath, @"Templates\Triangle2DTemplate.Clip.Gbx");

        // Load these templates. They will be used to create clips later.
        BlockTemplates blockTemplates = MediaTrackerUtils.CreateBlockTemplates();

        // ===================== Create Clip =====================
        string clipName = "MyClip.Clip.Gbx";
        string clipSavePath = Path.Combine(WindowsUtils.ClipsDirectoryPath, clipName);

        Clip clip = new Clip()
        {
            SavePath = clipSavePath
        };
        // Initialize Clip
        clip.Create(blockTemplates);

        // ===================== Create Scene =====================
        // Create a SceneBuilder and SceneTimeline (see other examples)
        SceneBuilder sceneBuilder = new MySceneBuilder();
        SceneTimeline sceneTimeline = new SceneTimeline();

        // ===================== Render Scene into Clip =====================
        sceneTimeline.Animate(clip.MediaClip, SceneAnimationSettings.Default, sceneBuilder);
        // Now the clip contains tracks and can be saved

        // ===================== Save Clip =====================
        // Saves clip to SavePath
        clip.Save();

        // Now you can open the MediaTracker and load this clip

        // ===================== Open Clips =====================
        // You can also open clips from the SavePath.
        // This loads the content of the clip into the Clip.MediaClip property.
        clip.Open();

    }

    class MySceneBuilder : SceneBuilder
    {
        protected override void Build()
        {
            Console.WriteLine("Adding Cube to Scene");
            var cube = new Cube(size: 20);
            scene.Add(cube);
        }
    }
}

using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MatchRogue.Editor
{
    public static class PrototypeSceneBuilder
    {
        [MenuItem("Match Rogue/Rebuild Prototype Scene")]
        public static void RebuildScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            var camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 6.4f;
            camera.transform.position = new Vector3(0f, 0f, -10f);
            camera.backgroundColor = new Color(0.10f, 0.09f, 0.13f);

            var bootstrap = new GameObject("Game Bootstrap");
            bootstrap.AddComponent<GameBootstrap>();

            const string scenePath = "Assets/Scenes/Prototype.unity";
            Directory.CreateDirectory(Path.GetDirectoryName(scenePath));
            EditorSceneManager.SaveScene(scene, scenePath);
            AssetDatabase.Refresh();
        }
    }
}

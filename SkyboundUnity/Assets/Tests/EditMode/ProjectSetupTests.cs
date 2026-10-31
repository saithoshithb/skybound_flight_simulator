using System.IO.Ports;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

public class ProjectSetupTests
{
    [Test]
    public void SerialPortApiIsAvailable()
    {
        // Compiling at all proves the API profile; the call proves it runs. Zero ports is fine.
        Assert.That(SerialPort.GetPortNames(), Is.Not.Null);
        Assert.That(
            PlayerSettings.GetApiCompatibilityLevel(NamedBuildTarget.Standalone),
            Is.EqualTo(ApiCompatibilityLevel.NET_Unity_4_8));
    }

    [Test]
    public void UniversalRenderPipelineIsActive()
    {
        Assert.That(GraphicsSettings.currentRenderPipeline, Is.Not.Null);
        Assert.That(GraphicsSettings.currentRenderPipeline.GetType().Name, Does.Contain("Universal"));
    }

    [Test]
    public void InputSystemIsAvailable()
    {
        Assert.That(typeof(Keyboard), Is.Not.Null);
    }

    [Test]
    public void EveryQualityLevelFollowsTheScreenRefresh()
    {
        var current = QualitySettings.GetQualityLevel();
        for (var i = 0; i < QualitySettings.names.Length; i++)
        {
            QualitySettings.SetQualityLevel(i, false);
            Assert.That(QualitySettings.vSyncCount, Is.EqualTo(1), QualitySettings.names[i]);
        }
        QualitySettings.SetQualityLevel(current, false);
    }

    [Test]
    public void TheOneSceneIsInTheBuild()
    {
        var scenes = EditorBuildSettings.scenes;
        Assert.That(scenes.Length, Is.EqualTo(1));
        Assert.That(scenes[0].path, Is.EqualTo("Assets/Scenes/Flight.unity"));
    }
}

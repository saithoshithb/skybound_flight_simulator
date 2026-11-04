using System.IO;
using NUnit.Framework;
using Skybound;

public class SettingsTests
{
    [Test]
    public void SavingReplacesTheFileWholeAndLeavesNoTempFile()
    {
        var dir = Path.Combine(Path.GetTempPath(), "skybound-settings-test");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "settings.json");
        File.Delete(path);

        SettingsStore.WriteSafely(path, "{\"first\":1}");
        SettingsStore.WriteSafely(path, "{\"second\":2}");

        Assert.That(File.ReadAllText(path), Is.EqualTo("{\"second\":2}"));
        Assert.That(File.Exists(path + ".tmp"), Is.False);
        Directory.Delete(dir, true);
    }

    [Test]
    public void ValuesTheSlidersNeverAllowAreBroughtBackInRange()
    {
        var c = new ControlSettings { sensitivity = 50, deadzone = double.NaN, smoothingMs = -5 };
        c.roll.maxAngleDeg = 0;

        SettingsStore.FitToRanges(c);

        Assert.That(c.roll.maxAngleDeg, Is.EqualTo(10));
        Assert.That(c.sensitivity, Is.EqualTo(2));
        Assert.That(c.deadzone, Is.EqualTo(new ControlSettings().deadzone), "NaN falls back to the default");
        Assert.That(c.smoothingMs, Is.EqualTo(0));
        // The case that went wrong: held exactly at neutral with a 0° max angle gave 0 / 0.
        Assert.That(ControlMapping.ShapeAxis(0, c.roll, c), Is.EqualTo(0));
    }
}

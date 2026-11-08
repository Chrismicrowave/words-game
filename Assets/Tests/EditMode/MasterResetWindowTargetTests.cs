using System;
using System.Reflection;
using NUnit.Framework;

public sealed class MasterResetWindowTargetTests
{
    private static readonly BindingFlags PrivateStatic = BindingFlags.NonPublic | BindingFlags.Static;

    [Test]
    public void WordListLibraryMetadataHasAnIndividualResetPreviewEntry()
    {
        Type windowType = Type.GetType("MasterConfigWindow, Assembly-CSharp-Editor", true);
        Type targetsType = Type.GetType("GameResetTargets, Core", true);
        Array targets = (Array)windowType.GetField("IndividualTargets", PrivateStatic).GetValue(null);
        string[] names = (string[])windowType.GetField("TargetNames", PrivateStatic).GetValue(null);
        string[] descriptions = (string[])windowType.GetField("TargetDescriptions", PrivateStatic).GetValue(null);

        Assert.AreEqual(targets.Length, names.Length);
        Assert.AreEqual(targets.Length, descriptions.Length);

        object libraryMetadataTarget = Enum.Parse(targetsType, "WordListLibraryMetadata");
        int index = Array.IndexOf(targets, libraryMetadataTarget);
        Assert.GreaterOrEqual(index, 0);
        Assert.AreEqual("Word list library metadata", names[index]);

        string description = descriptions[index].ToLowerInvariant();
        StringAssert.Contains("favorites", description);
        StringAssert.Contains("play counts", description);
        StringAssert.Contains("run records", description);
        StringAssert.Contains("revision history", description);
        StringAssert.Contains("word-list content files are not removed", description);

        MethodInfo describeTargets = windowType.GetMethod("DescribeTargets", PrivateStatic);
        string previewLabel = (string)describeTargets.Invoke(null, new[] { libraryMetadataTarget });
        StringAssert.Contains(names[index], previewLabel);

        object allLocal = Enum.Parse(targetsType, "AllLocal");
        int allLocalBits = Convert.ToInt32(allLocal);
        int libraryMetadataBit = Convert.ToInt32(libraryMetadataTarget);
        Assert.AreNotEqual(0, allLocalBits & libraryMetadataBit);
    }
}

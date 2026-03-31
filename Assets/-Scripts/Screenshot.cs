using SFB; // Namespace from the plugin
using UnityEngine;
using System.IO;

public class SaveScreenshot : MonoBehaviour
{
    public void SaveScreenshotWithDialog()
    {
        string timestamp = System.DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        string defaultFileName = $"screenshot_{timestamp}";
        var extension = new[] { new ExtensionFilter("PNG Files", "png") };

        StandaloneFileBrowser.SaveFilePanelAsync("Save Screenshot", "", defaultFileName, extension, (path) =>
        {
            if (!string.IsNullOrEmpty(path))
            {
                StartCoroutine(CaptureScreenshotToPath(path));
            }
        });
    }

    System.Collections.IEnumerator CaptureScreenshotToPath(string path)
    {
        yield return new WaitForEndOfFrame();
        Texture2D screenImage = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
        screenImage.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0);
        screenImage.Apply();
        File.WriteAllBytes(path, screenImage.EncodeToPNG());
        Debug.Log($"Screenshot saved to: {path}");
        Destroy(screenImage);
    }
}

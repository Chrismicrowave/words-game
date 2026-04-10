using UnityEngine;
using UnityEditor;
using UnityEngine.UI;
using UnityEngine.Events;

public class InspectListeners
{
    public static void Execute()
    {
        string[] paths = {
            "--- UI ---/Menus/WordListPanel/MyListTabBtn",
            "--- UI ---/Menus/WordListPanel/DailyTabBtn",
            "--- UI ---/Menus/WordListPanel/MyListPanelBtns/AddBtn",
            "--- UI ---/Menus/WordListPanel/MyListPanelBtns/DelBtn",
            "--- UI ---/Menus/WordListPanel/MyListPanelBtns/SwapBtn",
            "--- UI ---/Menus/WordListPanel/MyListPanelBtns/UpBtn",
            "--- UI ---/Menus/WordListPanel/MyListPanelBtns/DownBtn",
            "--- UI ---/Menus/WordListPanel/DailyPanelBtns/FetchDailyBtn",
            "--- UI ---/Menus/WordListPanel/DailyPanelBtns/SwapBtn (1)",
        };

        foreach (string path in paths)
        {
            GameObject go = GameObject.Find(path);
            if (go == null) { Debug.Log($"[Listeners] NOT FOUND: {path}"); continue; }
            Button btn = go.GetComponent<Button>();
            if (btn == null) { Debug.Log($"[Listeners] No Button: {path}"); continue; }

            int count = btn.onClick.GetPersistentEventCount();
            if (count == 0) {
                Debug.Log($"[Listeners] {go.name}: NO LISTENERS");
            } else {
                for (int i = 0; i < count; i++) {
                    string target = btn.onClick.GetPersistentTarget(i)?.GetType().Name ?? "null";
                    string method = btn.onClick.GetPersistentMethodName(i);
                    Debug.Log($"[Listeners] {go.name}[{i}]: {target}.{method}");
                }
            }
        }
    }
}

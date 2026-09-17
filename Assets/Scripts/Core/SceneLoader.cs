using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class SceneLoader : MonoBehaviour
{
    private static SceneLoader instance;
    private Image fadeImage;
    private const float FadeDuration = 0.4f;

    public static void Load(string sceneName)
    {
        EnsureInstance();
        if (instance != null)
            instance.StartCoroutine(instance.FadeAndLoad(sceneName));
    }

    private static void EnsureInstance()
    {
        if (instance != null) return;
        GameObject go = new GameObject("SceneLoader");
        instance = go.AddComponent<SceneLoader>();
        DontDestroyOnLoad(go);
        instance.BuildCanvas();
    }

    private void BuildCanvas()
    {
        Canvas canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 1000; // on top of everything

        CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);

        GameObject child = new GameObject("FadeImage");
        child.transform.SetParent(transform, false);
        fadeImage = child.AddComponent<Image>();
        fadeImage.color = new Color(0, 0, 0, 0);
        fadeImage.raycastTarget = true; // blocks clicks during fade

        RectTransform rt = fadeImage.rectTransform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    private IEnumerator FadeAndLoad(string scene)
    {
        Time.timeScale = 1f; // in case a menu left it paused
        fadeImage.raycastTarget = true;   // block input DURING the fade only
        yield return StartCoroutine(Fade(0f, 1f));
        AsyncOperation op = SceneManager.LoadSceneAsync(scene);
        while (op != null && !op.isDone) yield return null;
        yield return new WaitForSeconds(0.1f);
        yield return StartCoroutine(Fade(1f, 0f));
        fadeImage.raycastTarget = false;  // THE FIX — stop eating clicks forever
    }

    private IEnumerator Fade(float from, float to)
    {
        float t = 0f;
        Color c = fadeImage.color;
        while (t < FadeDuration)
        {
            t += Time.unscaledDeltaTime;
            c.a = Mathf.Lerp(from, to, t / FadeDuration);
            fadeImage.color = c;
            yield return null;
        }
        c.a = to;
        fadeImage.color = c;
    }
}
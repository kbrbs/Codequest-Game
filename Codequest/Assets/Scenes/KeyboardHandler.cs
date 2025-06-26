using UnityEngine;

public class KeyboardHandler : MonoBehaviour
{
    public RectTransform panelToMove;
    private Vector3 originalPosition;

    void Start()
    {
        originalPosition = panelToMove.localPosition;
    }

    void Update()
    {
#if UNITY_ANDROID || UNITY_IOS
        if (TouchScreenKeyboard.visible)
        {
            // Move the panel up when keyboard is visible
            // panelToMove.localPosition = new Vector3(originalPosition.x, 400f, originalPosition.z);
            panelToMove.localPosition = Vector3.Lerp(panelToMove.localPosition, new Vector3(originalPosition.x, 400f, originalPosition.z), Time.deltaTime * 10f);
        }
        else
        {
            // Reset position when keyboard is hidden
            panelToMove.localPosition = originalPosition;
        }
#endif
    }
}

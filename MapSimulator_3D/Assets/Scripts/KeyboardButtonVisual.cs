using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public class KeyboardButtonVisual : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
{
    public KeyCode keyCode;

    public Image buttonImage;

    public Color normalColor = new Color(0.86f, 0.86f, 0.86f, 1f);
    public Color pressedColor = new Color(0.45f, 0.45f, 0.45f, 1f);

    public float normalScale = 1f;
    public float pressedScale = 0.92f;

    private bool isPointerPressed = false;

    void Reset()
    {
        buttonImage = GetComponent<Image>();
    }

    void Start()
    {
        if (buttonImage == null)
        {
            buttonImage = GetComponent<Image>();
        }

        SetNormal();
    }

    void Update()
    {
        bool isKeyboardPressed = Input.GetKey(keyCode);

        if (isKeyboardPressed || isPointerPressed)
        {
            SetPressed();
        }
        else
        {
            SetNormal();
        }
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        isPointerPressed = true;
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        isPointerPressed = false;
    }

    private void SetPressed()
    {
        if (buttonImage != null)
        {
            buttonImage.color = pressedColor;
        }

        transform.localScale = Vector3.one * pressedScale;
    }

    private void SetNormal()
    {
        if (buttonImage != null)
        {
            buttonImage.color = normalColor;
        }

        transform.localScale = Vector3.one * normalScale;
    }
}
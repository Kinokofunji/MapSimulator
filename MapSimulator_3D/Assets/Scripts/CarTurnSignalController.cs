using System.Collections;
using TMPro;
using UnityEngine;

public class CarTurnSignalController : MonoBehaviour
{
    [Header("Turn Light Objects")]
    public GameObject leftTurnLight;
    public GameObject rightTurnLight;

    [Header("Turn Point Lights")]
    public Light leftPointLight;
    public Light rightPointLight;

    [Header("Settings")]
    public float signalDuration = 2f;
    public float blinkInterval = 0.25f;

    private TMP_Text turnSignalText;
    private Coroutine currentSignalRoutine;

    void Start()
    {
        GameObject textObject = GameObject.Find("TurnSignalText");

        if (textObject != null)
        {
            turnSignalText = textObject.GetComponent<TMP_Text>();
            turnSignalText.text = "";
        }

        if (leftTurnLight == null)
        {
            Transform left = transform.Find("LeftTurnLight");
            if (left != null)
            {
                leftTurnLight = left.gameObject;
            }
        }

        if (rightTurnLight == null)
        {
            Transform right = transform.Find("RightTurnLight");
            if (right != null)
            {
                rightTurnLight = right.gameObject;
            }
        }

        if (leftPointLight == null && leftTurnLight != null)
        {
            leftPointLight = leftTurnLight.GetComponentInChildren<Light>(true);
        }

        if (rightPointLight == null && rightTurnLight != null)
        {
            rightPointLight = rightTurnLight.GetComponentInChildren<Light>(true);
        }

        SetLights(false, false);
    }

    void Update()
    {
        bool isForward = Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow);
        bool isBackward = Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow);

        bool isLeft = Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow);
        bool isRight = Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow);

        bool isMoving = isForward || isBackward;

        if (!isMoving)
        {
            return;
        }

        if (currentSignalRoutine != null)
        {
            return;
        }

        if (isLeft)
        {
            TriggerLeftSignal(isForward);
        }
        else if (isRight)
        {
            TriggerRightSignal(isForward);
        }
    }

    public void TriggerLeftSignal(bool shouldShowText)
    {
        StartSignal(true, shouldShowText);
    }

    public void TriggerRightSignal(bool shouldShowText)
    {
        StartSignal(false, shouldShowText);
    }

    private void StartSignal(bool isLeft, bool shouldShowText)
    {
        if (currentSignalRoutine != null)
        {
            StopCoroutine(currentSignalRoutine);
        }

        currentSignalRoutine = StartCoroutine(SignalRoutine(isLeft, shouldShowText));
    }

    private IEnumerator SignalRoutine(bool isLeft, bool shouldShowText)
    {
        float timer = 0f;

        if (turnSignalText != null)
        {
            if (shouldShowText)
            {
                turnSignalText.text = isLeft ? "LEFT TURN" : "RIGHT TURN";
            }
            else
            {
                turnSignalText.text = "";
            }
        }

        while (timer < signalDuration)
        {
            if (isLeft)
            {
                SetLights(true, false);
            }
            else
            {
                SetLights(false, true);
            }

            yield return new WaitForSeconds(blinkInterval);

            SetLights(false, false);

            yield return new WaitForSeconds(blinkInterval);

            timer += blinkInterval * 2f;
        }

        SetLights(false, false);

        if (turnSignalText != null)
        {
            turnSignalText.text = "";
        }

        currentSignalRoutine = null;
    }

    private void SetLights(bool leftOn, bool rightOn)
    {
        if (leftTurnLight != null)
        {
            leftTurnLight.SetActive(leftOn);
        }

        if (rightTurnLight != null)
        {
            rightTurnLight.SetActive(rightOn);
        }

        if (leftPointLight != null)
        {
            leftPointLight.enabled = leftOn;
        }

        if (rightPointLight != null)
        {
            rightPointLight.enabled = rightOn;
        }
    }
}
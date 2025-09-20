using UnityEngine;
using System.Collections.Generic;

public class KillOverlayAnimator
{
    public static KillOverlayAnimator Instance;

    private Transform quadParent;

    private bool isAnimating = false;
    private bool isShrinking = false;
    private float pauseTime = 0f;
    private float pauseDuration = 2f;

    private Queue<Vector3> shrinkSteps;

    private Vector3 scaleStart = new Vector3(1f, 0f, 1f);
    private Vector3 scaleMid1 = new Vector3(1f, 0.3f, 1f);
    private Vector3 scaleMid2 = new Vector3(1f, 0.5f, 1f);
    private Vector3 scaleFull = new Vector3(1f, 1f, 1f);

    private Quaternion rotStart = Quaternion.Euler(0, 0, 0);
    private Quaternion rotMid1 = Quaternion.Euler(0, 0, 25);
    private Quaternion rotMid2 = Quaternion.Euler(0, 0, 345);

    private float animStepTime = 0.05f; // small step for smooth animation
    private float shrinkStepTime = 0.003f;
    private float animAccumulator = 0f;
    private int animPhase = 0; // 0=first,1=second,2=third

    public KillOverlayAnimator()
    {
        Instance = this;
        Reset();
    }

    private void FindQuadParent()
    {
        var cam = GameObject.Find("Main Camera");
        if (cam != null)
        {
            var target = cam.transform.Find("Hud/KillOverlay/QuadParent");
            if (target != null)
            {
                quadParent = target;
            }
        }
    }

    public void Reset()
    {
        isAnimating = false;
        isShrinking = false;
        pauseTime = 0f;
        animAccumulator = 0f;
        animPhase = 0;

        shrinkSteps = new Queue<Vector3>();

        FindQuadParent();
        if (quadParent != null)
        {
            quadParent.gameObject.SetActive(false);
            quadParent.localScale = scaleStart;
            quadParent.localRotation = rotStart;
        }
    }

    public void StartAnimation(float waitSeconds = 2f)
    {
        FindQuadParent();
        if (quadParent == null) return;

        Reset();
        pauseDuration = waitSeconds;
        isAnimating = true;

        quadParent.gameObject.SetActive(true);

        // Prepare shrink queue exactly like logs
        float y = 1f;
        while (y > 0f)
        {
            y -= 0.02f;
            if (y < 0f) y = 0f;
            shrinkSteps.Enqueue(new Vector3(1f, y, 1f));
        }
    }

    public void Update()
    {
        if (quadParent == null)
        {
            FindQuadParent();
        }
        if (!isAnimating || quadParent == null) return;

        float dt = Time.deltaTime;

        // First animate the pop-in steps
        if (!isShrinking && animPhase < 3)
        {
            animAccumulator += dt;

            if (animAccumulator >= animStepTime)
            {
                animAccumulator -= animStepTime;
                switch (animPhase)
                {
                    case 0:
                        quadParent.localScale = scaleMid1;
                        quadParent.localRotation = rotMid1;
                        break;
                    case 1:
                        quadParent.localScale = scaleMid2;
                        quadParent.localRotation = rotMid2;
                        break;
                    case 2:
                        quadParent.localScale = scaleFull;
                        quadParent.localRotation = rotStart;
                        break;
                }
                animPhase++;
            }
        }
        else if (!isShrinking)
        {
            // Pause at full size before shrinking
            pauseTime += dt;
            if (pauseTime >= pauseDuration)
            {
                isShrinking = true;
            }
        }
        else
        {
            // Shrink animation step by step
            animAccumulator += dt;

            while (animAccumulator >= shrinkStepTime && shrinkSteps.Count > 0)
            {
                animAccumulator -= shrinkStepTime;
                quadParent.localScale = shrinkSteps.Dequeue();

                if (shrinkSteps.Count == 0)
                {
                    quadParent.gameObject.SetActive(false);
                    isAnimating = false;
                }
            }
        }

    }
}

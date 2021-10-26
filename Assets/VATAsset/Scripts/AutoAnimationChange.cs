using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class AutoAnimationChange : MonoBehaviour
{
    private VATController _controller;
    [SerializeField] private float waitTime = 3f;
    void Start()
    {
        _controller =  GetComponent<VATController>();
        if (!_controller) return;

        StartCoroutine(AutoChange());
    }

    IEnumerator AutoChange()
    {
        while (true)
        {
            yield return new WaitForSeconds(waitTime);
            _controller.AnimationID++;
        }
    }

    private void OnDestroy()
    {
        StopCoroutine(AutoChange());
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class VATController : MonoBehaviour
{
    private int _animationID = 0;
    public int AnimationID
    {
        get => _animationID;
        set
        {
            _animationID = value;
            _renderer.material = _animationMaterials[_animationID % _animationMaterials.Count];
        }
    }

    [SerializeField] private List<Material> _animationMaterials = new List<Material>();
    [SerializeField,HideInInspector] private Renderer _renderer;
    

    public void Init(List<Material> materials)
    {
        _renderer = GetComponent<Renderer>();
        
        
        _animationMaterials.Clear();
        _animationMaterials.AddRange(materials);
    }


}

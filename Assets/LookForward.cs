using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class LookForward : MonoBehaviour
{
    public Transform target;

    // Start is called before the first frame update
    void Start()
    {
    }

    // Update is called once per frame
    void Update()
    {
        transform.LookAt(target, Vector3.up);

        transform.rotation = Quaternion.Euler(0, transform.rotation.eulerAngles.y + 90, 0);

    }
}

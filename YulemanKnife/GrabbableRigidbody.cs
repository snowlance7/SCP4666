// LethalThings, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null
// LethalThings.MonoBehaviours.GrabbableRigidbody
using System.Runtime.CompilerServices;
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class GrabbableRigidbody : GrabbableObject
{
    public float gravity = 0f;
    public bool isThrown = false;
    public bool stuck = false;

    internal Rigidbody rb;

    public override void Start()
    {
        rb = GetComponent<Rigidbody>();
        rb.useGravity = false;
        itemProperties.itemSpawnsOnGround = false;
        base.Start();
        //EnablePhysics(enable: true);
    }

    public new void EnablePhysics(bool enable)
    {
        for (int i = 0; i < propColliders.Length; i++)
        {
            if (!(propColliders[i] == null) && !propColliders[i].gameObject.CompareTag("InteractTrigger") && !propColliders[i].gameObject.CompareTag("DoNotSet"))
            {
                propColliders[i].enabled = enable;
            }
        }
        rb.isKinematic = !enable;
    }

    public override void Update()
    {
        if (!isThrown && !stuck) { base.Update(); }
        fallTime = 1f;
        reachedFloorTarget = true;
        bool wasHeld = isHeld;
        isHeld = true;
        base.Update();
        isHeld = wasHeld;
    }

    /*public void FixedUpdate()
    {
        if (base.IsHost)
        {
            if (!rb.isKinematic && !isHeld)
            {
                rb.useGravity = false;
                rb.AddForce(Vector3.down * gravity, ForceMode.Acceleration);
            }
            else
            {
                rb.AddForce(Vector3.zero, ForceMode.VelocityChange);
            }
        }
    }*/

    public override void LateUpdate()
    {
        if (parentObject != null && isHeld)
        {
            base.transform.rotation = parentObject.rotation;
            base.transform.Rotate(itemProperties.rotationOffset);
            base.transform.position = parentObject.position;
            Vector3 positionOffset = itemProperties.positionOffset;
            positionOffset = parentObject.rotation * positionOffset;
            base.transform.position += positionOffset;
        }
        if (radarIcon != null)
        {
            radarIcon.position = base.transform.position;
        }
    }

    public override void EquipItem()
    {
        base.EquipItem();
        //base.transform.SetParent(null, worldPositionStays: true);
    }
}

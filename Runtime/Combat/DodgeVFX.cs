using UnityEngine;

public class DodgeVFX : MonoBehaviour
{
    [Header("References (optional)")]
    [Tooltip("If set, used as primary transform for facing/parenting.")]
    [SerializeField] private Transform locomotion;
    [Tooltip("Fallback transform if locomotion is not set.")]
    [SerializeField] private Transform combat;

    [Header("Dodge VFX")]
    [SerializeField] private GameObject vfxDodge;
    [Tooltip("Anchor for spawning the dodge VFX. If null, uses hitOriginOverride or self.")]
    [SerializeField] private Transform vfxDodgeAnchor;
    [Tooltip("Optional override for anchor if needed by character setup.")]
    [SerializeField] private Transform hitOriginOverride;
    [SerializeField] private Vector3 vfxDodgeOffset = Vector3.zero;
    [SerializeField] private bool vfxDodgeOffsetLocal = false;

    [Header("Dodge Orientation")]
    [SerializeField] private bool vfxOrientDodgeToFacing = true;
    [SerializeField] private bool vfxYawFlipDodgeWithFacingX = true;
    [Tooltip("If set, this transform is used to determine facing; otherwise uses locomotion/combat/self.")]
    [SerializeField] private Transform vfxDodgeFacingSource;
    [SerializeField] private Vector3 vfxEulerOffset = Vector3.zero;
    [SerializeField] private Vector3 vfxEulerDodge = Vector3.zero;

    [Header("Scale & Parenting")]
    [Min(0f)]
    [SerializeField] private float vfxDodgeScaleMultiplier = 1f;
    [SerializeField] private bool vfxDodgeParentToAttacker = false;

    [Header("Opacity (static)")]
    [SerializeField] private bool vfxEnableOpacity = false;
    [Range(0f, 1f)]
    [SerializeField] private float vfxOpacity = 1f;

    public void TriggerDodgeVFX()
    {
        if (!vfxDodge)
        {
            if (Application.isEditor) Debug.LogWarning("[DodgeVFX] TriggerDodgeVFX called but vfxDodge is not assigned.", this);
            return;
        }

        Transform tr = locomotion ? locomotion : (combat ? combat : transform);
        if (!tr)
        {
            if (Application.isEditor) Debug.LogWarning("[DodgeVFX] Missing base transform (locomotion/combat/self).", this);
            return;
        }

        Transform anchor = vfxDodgeAnchor ? vfxDodgeAnchor : (hitOriginOverride ? hitOriginOverride : tr);
        Vector3 pos = anchor.position;
        if (vfxDodgeOffset != Vector3.zero)
            pos += vfxDodgeOffsetLocal ? anchor.TransformDirection(vfxDodgeOffset) : vfxDodgeOffset;

        Quaternion rot = Quaternion.identity;
        if (vfxOrientDodgeToFacing)
        {
            Transform src = vfxDodgeFacingSource ? vfxDodgeFacingSource : tr;
            Vector3 fwd = src ? src.forward : Vector3.forward;
            fwd.y = 0f; if (fwd.sqrMagnitude < 0.0001f) fwd = Vector3.forward;
            rot = Quaternion.LookRotation(fwd, Vector3.up);

            int sign = 1;
            if (src)
            {
                float rx = src.right.x;
                if (Mathf.Abs(rx) > 0.001f) sign = rx >= 0f ? 1 : -1; else sign = GetFacingSignX();
            }
            else
            {
                sign = GetFacingSignX();
            }
            if (vfxYawFlipDodgeWithFacingX && sign < 0)
                rot *= Quaternion.Euler(0f, 180f, 0f);
        }

        rot *= Quaternion.Euler(vfxEulerOffset);
        if (vfxEulerDodge != Vector3.zero)
            rot *= Quaternion.Euler(vfxEulerDodge);

        var go = GameObject.Instantiate(vfxDodge, pos, rot);
        if (Application.isEditor) Debug.Log($"[DodgeVFX] Spawn '{vfxDodge.name}' pos={pos:F2} yaw={rot.eulerAngles.y:F1} flipSign={GetFacingSignX()} parent={(vfxDodgeParentToAttacker ? "yes" : "no")} ", this);
        if (vfxDodgeParentToAttacker && tr)
            go.transform.SetParent(tr, true);

        if (vfxDodgeScaleMultiplier > 0f && Mathf.Abs(vfxDodgeScaleMultiplier - 1f) > 0.0001f)
            go.transform.localScale = go.transform.localScale * vfxDodgeScaleMultiplier;

        if (vfxEnableOpacity)
            ApplyOpacity(go, Mathf.Clamp01(vfxOpacity));
        else if (Application.isEditor)
            Debug.Log("[DodgeVFX] Opacity disabled (vfxEnableOpacity=false).", this);
    }

    private int GetFacingSignX()
    {
        Transform tr = locomotion ? locomotion : (combat ? combat : transform);
        if (!tr) return 1;
        float rx = tr.right.x;
        if (Mathf.Abs(rx) < 0.001f) return 1;
        return rx >= 0f ? 1 : -1;
    }

    private void ApplyOpacity(GameObject go, float alpha)
    {
        if (!go) return;
        var renderers = go.GetComponentsInChildren<Renderer>(true);
        foreach (var r in renderers)
        {
            var sr = r as SpriteRenderer;
            if (sr)
            {
                var c = sr.color; c.a = alpha; sr.color = c; continue;
            }

            if (r.sharedMaterial && r.material)
            {
                if (r.material.HasProperty("_Color"))
                {
                    var c = r.material.color; c.a = alpha; r.material.color = c;
                }
            }
        }

        var particles = go.GetComponentsInChildren<ParticleSystem>(true);
        foreach (var ps in particles)
        {
            var main = ps.main;
            var start = main.startColor;
            var c = start.color;
            c.a = alpha;
            main.startColor = c;
        }
    }
}

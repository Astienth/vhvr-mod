using UnityEngine;
using ValheimVRMod.Utilities;
using ValheimVRMod.VRCore;

namespace ValheimVRMod.Scripts.Block {
    public class ShieldBlock : Block {

        public string itemName;
        private const float maxParryAngle = 45f;

        private float scaling = 1f;
        private Vector3 posRef;
        private Vector3 scaleRef;
        
        public static ShieldBlock instance;

        private void OnDisable() {
            instance = null;
        }
        
        private void Awake() {
            _meshCooldown = gameObject.AddComponent<MeshCooldown>();
            instance = this;
            InitShield();
        }
        
        private void InitShield()
        {
            posRef = _meshCooldown.transform.localPosition;
            scaleRef = _meshCooldown.transform.localScale;
            hand = VHVRConfig.LeftHanded() ? VRPlayer.rightHand.transform : VRPlayer.leftHand.transform;
        }

        public override void setBlocking(Vector3 hitDir) {
            _blocking = Vector3.Dot(hitDir, getForward()) > 0.5f;
        }

        private Vector3 getForward() {
            switch (itemName)
            {
                case "ShieldWood":
                case "ShieldBanded":
                    return StaticObjects.shieldObj().transform.forward;
                case "ShieldKnight":
                    return -StaticObjects.shieldObj().transform.right;
                case "ShieldBronzeBuckler":
                case "ShieldIronBuckler":
                    return -StaticObjects.shieldObj().transform.up;
            }
            return -StaticObjects.shieldObj().transform.forward;
        }
        protected override void ParryCheck(Vector3 posStart, Vector3 posEnd) {

            if (Vector3.Distance(posEnd, posStart) > minDist) {
                
                Vector3 shieldPos = snapshots[snapshots.Count - 1] + Player.m_localPlayer.transform.InverseTransformDirection(-hand.right) / 2;
                if (Vector3.Angle(shieldPos - snapshots[0] , snapshots[snapshots.Count - 1] - snapshots[0]) < maxParryAngle) {
                    blockTimer = blockTimerParry;
                }
                
            } else {
                blockTimer = blockTimerNonParry;
            }
        }

        private void OnRenderObject() {
            if (scaling != 1f)
            {
                transform.localScale = scaleRef * scaling;
                transform.localPosition = CalculatePos();
            }
            else if (transform.localPosition != posRef || transform.localScale != scaleRef)
            {
                transform.localScale = scaleRef;
                transform.localPosition = posRef;
            }
            StaticObjects.shieldObj().transform.rotation = transform.rotation;
            
        }
        public void ScaleShieldSize(float scale)
        {
            scaling = scale;
        }
        private Vector3 CalculatePos()
        {
            return VRPlayer.leftHand.transform.InverseTransformDirection(hand.TransformDirection(posRef) *(scaleRef * scaling).x);
        }
    }
}

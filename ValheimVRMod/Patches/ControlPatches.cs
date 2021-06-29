﻿using System.Collections.Generic;
using ValheimVRMod.VRCore;
using ValheimVRMod.VRCore.UI;
using HarmonyLib;
using System.Reflection;
using ValheimVRMod.Scripts;
using ValheimVRMod.Utilities;
using System.Reflection.Emit;
using UnityEngine;

namespace ValheimVRMod.Patches {
    // These patches are used to inject the VR inputs into the game's control system

    [HarmonyPatch(typeof(ZInput), nameof(ZInput.GetButtonDown))]
    class ZInput_GetButtonDown_Patch {
        static bool Prefix(string name, ref bool __result) {
            // Need to bypass original function for any required ZInputs that begin
            // with "Joy" to ensure the VR Controls still work when
            // Gamepad is disabled.
            if (VRControls.mainControlsActive && !ZInput.IsGamepadEnabled() && isUsedJoyZinput(name)) {
                __result = VRControls.instance.GetButtonDown(name);
                return false;
            }

            return true;
        }

        private static bool isUsedJoyZinput(string name) {
            return name == "JoyMenu" ||
                   name == "JoyPlace" ||
                   name == "JoyPlace" ||
                   name == "JoyRemove";
        }

        static void Postfix(string name, ref bool __result) {
            if (VRControls.mainControlsActive) {
                __result = __result || VRControls.instance.GetButtonDown(name);
            }
        }
    }

    [HarmonyPatch(typeof(ZInput), nameof(ZInput.GetButtonUp))]
    class ZInput_GetButtonUp_Patch {
        static void Postfix(string name, ref bool __result) {
            if (VRControls.mainControlsActive) {
                __result = __result || VRControls.instance.GetButtonUp(name);
            }
        }
    }

    [HarmonyPatch(typeof(ZInput), nameof(ZInput.GetButton))]
    class ZInput_GetButton_Patch {
        static void Postfix(string name, ref bool __result) {
            if (VRControls.mainControlsActive) {
                __result = __result || VRControls.instance.GetButton(name);
            }
        }
    }

    [HarmonyPatch(typeof(ZInput), nameof(ZInput.GetJoyLeftStickX))]
    class ZInput_GetJoyLeftStickX_Patch {
        static void Postfix(ref float __result) {
            if (VRControls.mainControlsActive) {
                __result = __result + VRControls.instance.GetJoyLeftStickX();
            }
        }
    }

    [HarmonyPatch(typeof(ZInput), nameof(ZInput.GetJoyLeftStickY))]
    class ZInput_GetJoyLeftStickY_Patch {
        static void Postfix(ref float __result) {
            if (VRControls.mainControlsActive) {
                __result = __result + VRControls.instance.GetJoyLeftStickY();
            }
        }
    }

    [HarmonyPatch(typeof(ZInput), nameof(ZInput.GetJoyRightStickX))]
    class ZInput_GetJoyRightStickX_Patch {
        static void Postfix(ref float __result) {
            if (VRControls.mainControlsActive) {
                
                if (ZInput_GetJoyRightStickY_Patch.isRunning 
                    && VRControls.instance.GetJoyRightStickX() > -0.5f 
                    && VRControls.instance.GetJoyRightStickX() < 0.5f)
                {
                    return;
                }
                
                __result = __result + VRControls.instance.GetJoyRightStickX();
            }
        }
    }

    [HarmonyPatch(typeof(ZInput), nameof(ZInput.GetJoyRightStickY))]
    class ZInput_GetJoyRightStickY_Patch {
        public static bool isCrouching;
        public static bool isRunning;

        static void Postfix(ref float __result) {
            if (VRControls.mainControlsActive) {
                var joystick = VRControls.instance.GetJoyRightStickY();

                isRunning = joystick < -0.3f;
                isCrouching = joystick > 0.7f;

                __result = __result + joystick;
            }
        }
    }

    // Patch to enable rotation of pieces using VR control actions
    [HarmonyPatch(typeof(Player), "UpdatePlacement")]
    class Player_Update_Placement_PieceRotationPatch {
        static void Postfix(Player __instance, bool takeInput, ref int ___m_placeRotation) {
            if (!VRControls.mainControlsActive || __instance != Player.m_localPlayer || !takeInput ||
                !__instance.InPlaceMode() || Hud.IsPieceSelectionVisible()) {
                return;
            }

            ___m_placeRotation += VRControls.instance.getPieceRotation();
        }
    }

    // If using VR controls, disable the joystick for the purposes
    // of moving the map around since that will be done with
    // simulated mouse cursor click and drag via laser pointer.
    [HarmonyPatch(typeof(Minimap), "UpdateMap")]
    class Minimap_UpdateMap_MapTranslationPatch {
        private static MethodInfo getJoyLeftStickX =
            AccessTools.Method(typeof(ZInput), nameof(ZInput.GetJoyLeftStickX));

        private static MethodInfo getJoyLeftStickY =
            AccessTools.Method(typeof(ZInput), nameof(ZInput.GetJoyLeftStickY));

        private static float getJoyLeftStickXPatched() {
            if (VRControls.mainControlsActive) {
                return 0.0f;
            }

            return ZInput.GetJoyLeftStickX();
        }

        private static float getJoyLeftStickYPatched() {
            if (VRControls.mainControlsActive) {
                return 0.0f;
            }

            return ZInput.GetJoyLeftStickY();
        }

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) {
            var original = new List<CodeInstruction>(instructions);
            var patched = new List<CodeInstruction>();
            foreach (var instruction in original) {
                if (instruction.Calls(getJoyLeftStickX)) {
                    patched.Add(CodeInstruction.Call(typeof(Minimap_UpdateMap_MapTranslationPatch),
                        nameof(getJoyLeftStickXPatched)));
                }
                else if (instruction.Calls(getJoyLeftStickY)) {
                    patched.Add(CodeInstruction.Call(typeof(Minimap_UpdateMap_MapTranslationPatch),
                        nameof(getJoyLeftStickYPatched)));
                }
                else {
                    patched.Add(instruction);
                }
            }

            return patched;
        }
    }

    [HarmonyPatch(typeof(Player), "SetControls")]
    class PlayerSetControlsPatch {

        static bool wasCrouching;
        
        static void Prefix(Player __instance, ref bool attack, ref bool attackHold, ref bool block, ref bool blockHold,
            ref bool secondaryAttack, ref bool crouch, ref bool run) {
            if (!VRControls.mainControlsActive || __instance != Player.m_localPlayer) {
                return;
            }

            if (VHVRConfig.RoomScaleSneakEnabled()) {
                if (!wasCrouching && VRPlayer.isSneaking) {
                    crouch = true;
                    wasCrouching = true;
                }
                else if (wasCrouching && !VRPlayer.isSneaking) {
                    crouch = true;
                    wasCrouching = false;
                }

                if (wasCrouching) {
                    run = false;
                }
                else {
                    run = ZInput_GetJoyRightStickY_Patch.isRunning;
                }
            }
            else {
                run = ZInput_GetJoyRightStickY_Patch.isRunning;
                if (ZInput_GetJoyRightStickY_Patch.isCrouching) {
                    if (!wasCrouching) {
                        crouch = true;
                        wasCrouching = true;
                    }
                }
                else if (wasCrouching) {
                    wasCrouching = false;
                }
            }

            if (EquipScript.getLeft() == EquipType.Bow) {
                if (BowLocalManager.aborting) {
                    block = true;
                    blockHold = true;
                    BowLocalManager.aborting = false;
                }
                else if (BowLocalManager.startedPulling) {
                    attack = true;
                    BowLocalManager.startedPulling = false;
                }
                else {
                    attackHold = BowLocalManager.isPulling;
                }
                return;
            }

            if (EquipScript.getLeft() == EquipType.Shield) {
                blockHold = ShieldManager.isBlocking();
            }

            switch (EquipScript.getRight()) {
                case EquipType.Fishing:
                    if (FishingManager.isThrowing) {
                        attack = true;
                        attackHold = true;
                        FishingManager.isThrowing = false;
                    }
                    
                    blockHold = FishingManager.isPulling;
                    break;

                case EquipType.Spear:
                    if (SpearManager.isThrowing) {
                        secondaryAttack = true;
                        SpearManager.isThrowing = false;
                    }

                    break;
                // no one knows why all spears throw with right click, only spear-chitin throws with left click: 
                case EquipType.SpearChitin:
                    if (SpearManager.isThrowing) {
                        attack = true;
                        SpearManager.isThrowing = false;
                    }

                    break;
            }
        }
    }

    // Used to enable stack splitting in inventory
    [HarmonyPatch(typeof(InventoryGrid), "OnLeftClick")]
    class InventoryGrid_OnLeftClick_Patch {

        static bool getClickModifier()
        {
            return VRControls.laserControlsActive && VRControls.instance.getClickModifier();
        }

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var original = new List<CodeInstruction>(instructions);
            var patched = new List<CodeInstruction>();
            if (!VHVRConfig.UseVrControls())
            {
                return original;
            }
            bool addedInstruction = false;
            for (int i = 0; i < original.Count; i++)
            {
                var instruction = original[i];
                if (!addedInstruction && instruction.opcode == OpCodes.Ldc_I4)
                {
                    int operand = (int)instruction.operand;
                    if (operand == (int)KeyCode.LeftShift)
                    {
                        // Add our custom check
                        patched.Add(CodeInstruction.Call(typeof(InventoryGrid_OnLeftClick_Patch), nameof(getClickModifier)));
                        addedInstruction = true;
                        // Skip over the next instruction too since it will be the keycode comparison
                        i++;
                    }
                }
                else
                {
                    patched.Add(instruction);
                }
            }
            return patched;
        }
    }

    class SnapTurnPatches
    {
        [HarmonyPatch(typeof(Player), nameof(Player.SetMouseLook))]
        class Player_SetMouseLook_Patch
        {

            private static readonly float MINIMUM_SNAP_SENSITIVITY = 1f;
            private static readonly float SMOOTH_SNAP_INCREMENT_TIME_DELTA = 0.01f;
            private static bool snapTriggered = false;
            private static bool isSmoothSnapping = false;

            private static float currentSmoothSnapAmount = 0f;
            private static float currentDt = 0f;
            private static int smoothSnapDirection = 1;

            static void Prefix(Player __instance, ref Vector2 mouseLook)
            {
                if (__instance != Player.m_localPlayer || !VHVRConfig.UseVrControls() || !VHVRConfig.SnapTurnEnabled())
                {
                    return;
                }
                if (snapTriggered && !isSmoothSnapping)
                {
                    if (turnInputApplied(mouseLook.x))
                    {
                        mouseLook.x = 0f;
                        return;
                    }
                    snapTriggered = false;
                }
                if (VHVRConfig.SmoothSnapTurn())
                {
                    handleSmoothSnap(ref mouseLook);
                } else
                {
                    handleImmediateSnap(ref mouseLook);
                }
            }

            private static void handleSmoothSnap(ref Vector2 mouseLook)
            {
                if (!isSmoothSnapping)
                {
                    // On this update, we are not currently smooth snapping.
                    // Check if the turnInput is applied and trigger the snap if it is
                    // otherwise set turn angle to zero.
                    if (turnInputApplied(mouseLook.x))
                    {
                        isSmoothSnapping = true;
                        snapTriggered = true;
                        // reset the current smooth snapped amount/dt
                        currentSmoothSnapAmount = 0f;
                        currentDt = 0f;
                        smoothSnapDirection = (mouseLook.x > 0) ? 1 : -1;
                        // Determine how much the current update should snap by
                        float snapIncrementAmount = calculateSmoothSnapAngle(mouseLook.x);
                        currentSmoothSnapAmount += snapIncrementAmount;
                        if (Mathf.Abs(currentSmoothSnapAmount) >= VHVRConfig.GetSnapTurnAngle())
                        {
                            // Immediately hit the snap target ? Config is probably weirdly set.
                            // Handle this case anyways.
                            isSmoothSnapping = false;
                        }
                        mouseLook.x = snapIncrementAmount;
                    } else
                    {
                        snapTriggered = false;
                        mouseLook.x = 0f;
                    }
                } else
                {
                    // We are in the middle of a smooth snap
                    float snapIncrementAmount = calculateSmoothSnapAngle(mouseLook.x);
                    currentSmoothSnapAmount += snapIncrementAmount;
                    if (Mathf.Abs(currentSmoothSnapAmount) >= VHVRConfig.GetSnapTurnAngle())
                    {
                        // We've exceeded our target, so disable smooth snapping
                        isSmoothSnapping = false;
                    }
                    mouseLook.x = snapIncrementAmount;
                }
            }

            private static void handleImmediateSnap(ref Vector2 mouseLook)
            {
                if (turnInputApplied(mouseLook.x))
                {
                    // The player triggered a turn this update, so incremement
                    // by the full snap angle.
                    snapTriggered = true;
                    mouseLook.x = (mouseLook.x > 0 ? VHVRConfig.GetSnapTurnAngle() : -VHVRConfig.GetSnapTurnAngle());
                    return;
                }
                else
                {
                    snapTriggered = false;
                    mouseLook.x = 0f;
                }
            }

            private static float calculateSmoothSnapAngle(float mouseX)
            {
                float dt = Time.deltaTime;
                currentDt += dt;
                if (currentDt < SMOOTH_SNAP_INCREMENT_TIME_DELTA)
                {
                    return 0f;
                } else
                {
                    // We've hit our deltaT target, so reset it and continue
                    // with calculating the next increment.
                    currentDt = 0f;
                }
                float finalSnapTarget = VHVRConfig.GetSnapTurnAngle() * smoothSnapDirection;
                float smoothSnapIncrement = VHVRConfig.SmoothSnapSpeed() * smoothSnapDirection;
                if (Mathf.Abs(finalSnapTarget) > Mathf.Abs(currentSmoothSnapAmount + smoothSnapIncrement))
                {
                    // We can still increment by the full "smoothSnapIncrement" and 
                    // be below our final target.
                    return smoothSnapIncrement;
                } else
                {
                    // If we increment by the full amount, we'll exceed our target, so
                    // we should only return the difference
                    return (Mathf.Abs(finalSnapTarget) - Mathf.Abs(currentSmoothSnapAmount)) * smoothSnapDirection;
                }
            }

            private static bool turnInputApplied(float angle)
            {
                return Mathf.Abs(angle) > MINIMUM_SNAP_SENSITIVITY;
            }
        }
    }
}
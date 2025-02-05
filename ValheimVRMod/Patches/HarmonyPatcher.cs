﻿using HarmonyLib;

namespace ValheimVRMod.Patches
{
    class HarmonyPatcher
    {
        private static readonly Harmony harmony = new Harmony("com.valheimvrmod.patches");
        public static void DoPatching()
        {
            harmony.PatchAll();
        }
        
        /** Example of how to patch hidden classes if needed
        private static void DoCustomPatching()
        {
            var type = AccessTools.TypeByName("MultipleDisplayUtilities");
            var method = AccessTools.Method("UnityEngine.UI.MultipleDisplayUtilities:GetMousePositionRelativeToMainDisplayResolution");
            System.Reflection.MethodInfo patchMethodPostfix = SymbolExtensions.GetMethodInfo((Vector2 __result) => Postfix(ref __result));
            harmony.Patch(method, postfix: new HarmonyMethod(patchMethodPostfix));
        }

        public static void Postfix(ref Vector2 __result)
        {
        }
        */
    }
}

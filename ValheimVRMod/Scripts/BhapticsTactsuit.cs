﻿using System;
using System.Timers;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using Bhaptics.Tact;
using UnityEngine;

using static ValheimVRMod.Utilities.LogUtils;

namespace ValheimVRMod.Scripts
{

    public class TactsuitVR : MonoBehaviour
    {
        public static bool suitDisabled = true;
        public static  bool systemInitialized = false;
        public static bool threadEnabled = false;
        // Event to start and stop the threads
        private static ManualResetEvent bHaptics_mrse = new ManualResetEvent(true);
        //semaphore allowing one thread at a time
        private static Semaphore _threadAllowed = new Semaphore(0,1);
        //list of allowed thread by effectname
        public static volatile Dictionary<string, bool> ThreadsConditions = new Dictionary<string, bool>();
        public static volatile Dictionary<string, bool> ThreadsStatus = new Dictionary<string, bool>();
        //association effect name => params (intensity, sleep)
        public static Dictionary<string, float[]> ThreadParams = new Dictionary<string, float[]>();
        // dictionary of all feedback patterns found in the bHaptics directory
        public static Dictionary<string, FileInfo> FeedbackMap = new Dictionary<string, FileInfo>();

#pragma warning disable CS0618 // remove warning that the C# library is deprecated
        public static HapticPlayer hapticPlayer;
#pragma warning restore CS0618 

        public static RotationOption defaultRotationOption = new RotationOption(0.0f, 0.0f);
        private static System.Timers.Timer aTimer;

        public void Awake()
        {

            LogInfo("Initializing suit");
            try
            {
#pragma warning disable CS0618 // remove warning that the C# library is deprecated
                hapticPlayer = new HapticPlayer("Valheim_bhaptics", "Valheim_bhaptics");
#pragma warning restore CS0618
                suitDisabled = false;
            }
            catch { LogInfo("Suit initialization failed!"); }
            RegisterAllTactFiles();
            LogInfo("Starting HeartBeat thread...");
            PlaybackHaptics("HeartBeat");
            SetTimer();
        }
        /**
         * Starts Timer needed for thread creation limiter
         */
        private static void SetTimer()
        {
            // Create a timer with a 200ms interval.
            aTimer = new System.Timers.Timer(200);
            // Hook up the Elapsed event for the timer. 
            aTimer.Elapsed += OnTimedEvent;
            aTimer.AutoReset = true;
            aTimer.Enabled = true;
        }
        private static void OnTimedEvent(object source, ElapsedEventArgs e)
        {
            threadEnabled = true;
        }

        /**
         * Registers all tact files in bHaptics folder
         */
        void RegisterAllTactFiles()
        {
            if (suitDisabled) { return; }
            // Get location of the compiled assembly and search through "bHaptics" directory and contained patterns
            string assemblyFile = Assembly.GetExecutingAssembly().Location;
            string myPath = Path.GetDirectoryName(assemblyFile);
            LogInfo("Assembly path: " + myPath);
            string configPath = myPath + "\\bHaptics";
            DirectoryInfo d = new DirectoryInfo(configPath);
            FileInfo[] Files = d.GetFiles("*.tact", SearchOption.AllDirectories);
            for (int i = 0; i < Files.Length; i++)
            {
                string filename = Files[i].Name;
                string fullName = Files[i].FullName;
                string prefix = Path.GetFileNameWithoutExtension(filename);
                if (filename == "." || filename == "..")
                    continue;
                string tactFileStr = File.ReadAllText(fullName);
                try
                {
                    hapticPlayer.RegisterTactFileStr(prefix, tactFileStr);
                    LogInfo("Pattern registered: " + prefix);
                }
                catch (Exception e) { LogInfo(e.ToString()); }

                FeedbackMap.Add(prefix, Files[i]);
            }
            systemInitialized = true;
        }

        public static void PlaybackHaptics(string key, float intensity = 1.0f, float duration = 1.0f)
        {
            if (suitDisabled) { return; }
            if (FeedbackMap.ContainsKey(key))
            {
                ScaleOption scaleOption = new ScaleOption(intensity, duration);
                hapticPlayer.SubmitRegisteredVestRotation(key, key, defaultRotationOption, scaleOption);
            }
            else
            {
                LogInfo("Feedback not registered: " + key);
            }
        }

        public static void PlayBackHit(string key, float xzAngle, float yShift)
        {
            // two parameters can be given to the pattern to move it on the vest:
            // 1. An angle in degrees [0, 360] to turn the pattern to the left
            // 2. A shift [-0.5, 0.5] in y-direction (up and down) to move it up or down
            if (suitDisabled) { return; }
            ScaleOption scaleOption = new ScaleOption(1f, 1f);
            RotationOption rotationOption = new RotationOption(xzAngle, yShift);
            hapticPlayer.SubmitRegisteredVestRotation(key, key, rotationOption, scaleOption);
        }

        /**
         * Specific sword recoil effect using vest and arms tactosy
         */
        public static void SwordRecoil(bool isRightHand, float intensity = 1.0f)
        {
            // Melee feedback pattern
            if (suitDisabled) { return; }
            float duration = 1.0f;
            var scaleOption = new ScaleOption(intensity, duration);
            var rotationFront = new RotationOption(0f, 0f);
            string postfix = "_L";
            if (isRightHand) { postfix = "_R"; }
            string keyArm = "Sword" + postfix;
            string keyVest = "SwordVest" + postfix;
            hapticPlayer.SubmitRegisteredVestRotation(keyArm, keyArm, rotationFront, scaleOption);
            hapticPlayer.SubmitRegisteredVestRotation(keyVest, keyVest, rotationFront, scaleOption);
        }

        /**
         * Checks if creation needs to be controlled by timer
         * Creates Thread condition if not exists
         * Create Thread if not exists
         * creates or update thread params
         * Start or restart thread with params/updated params
         */
        public static void StartThreadHaptic(string EffectName, float intensity = 1.0f, bool timerNeeded = false, float duration = 1.0f)
        {
            int sleep = timerNeeded ? 200 : 1000;
            //checks if timer control needed
            if (timerNeeded && !threadEnabled)
            {
                return;
            }
            //params
            if (!ThreadParams.ContainsKey(EffectName))
            {
                float[] thParams = { intensity, sleep, duration };
                ThreadParams.Add(EffectName, thParams);
            } else
            {
                //update params
                ThreadParams[EffectName][0] = intensity;
                ThreadParams[EffectName][1] = sleep;
                ThreadParams[EffectName][2] = duration;
            }
            //checking if conditions with name exists
            if (ThreadsConditions.ContainsKey(EffectName))
            {
                if (!ThreadsConditions[EffectName])
                {
                    ThreadsConditions[EffectName] = true;
                }
            }
            else
            {
                bool ThreadCondition = true;
                ThreadsConditions.Add(EffectName, ThreadCondition);
            }
            //checking if thread is alive
            if (!ThreadsStatus[EffectName])
            {
                Thread EffectThread = new Thread(() => ThreadHapticFunc(EffectName));
                EffectThread.Start();
            }
            //we still turn threadEnabled to false for other timerNeeded processes
            threadEnabled = false;
        }

        /**
         * Resets the thread condition to tell the corresponding
         * Thread to stop
         */
        public static void StopThreadHaptic(string name)
        {
            ThreadsConditions[name] = false;
        }

        public static void StopHapticFeedback(string effect)
        {
            hapticPlayer.TurnOff(effect);
        }

        public static void StopAllHapticFeedback()
        {
            StopThreads();
            foreach (string key in FeedbackMap.Keys)
            {
                hapticPlayer.TurnOff(key);
            }
        }

        public static void StopThreads()
        {
            bHaptics_mrse.Reset();
        }

        /**
         * Thread function executing haptic effect every sleep value
         * while corresponding name condition is not false
         */
        public static void ThreadHapticFunc(string name)
        {
            try
            {
                _threadAllowed.WaitOne();
                //thread is alive
                ThreadsStatus[name] = true;
                //if false, stops the thread by making it finish
                while (ThreadsConditions[name] && bHaptics_mrse.WaitOne())
                {
                    PlaybackHaptics(name, ThreadParams[name][0], ThreadParams[name][2]);
                    int sleep = (int)ThreadParams[name][1];
                    Thread.Sleep(sleep == 0 ? 1000 : sleep);
                    _threadAllowed.Release();
                }
            } finally
            {
                //thread is dead
                ThreadsStatus[name] = false;
            }
        }
    }
}

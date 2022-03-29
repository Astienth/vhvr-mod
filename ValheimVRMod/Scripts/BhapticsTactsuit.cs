﻿using System;
using System.Timers;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using Bhaptics.Tact;
using UnityEngine;

using static ValheimVRMod.Utilities.LogUtils;
using System.Linq;

namespace ValheimVRMod.Scripts
{

    public class BhapticsTactsuit : MonoBehaviour
    {
        public static bool suitDisabled = true;
        public static bool systemInitialized = false;
        public static bool threadEnabled = false;
        //semaphore allowing one thread at a time
        //private static Semaphore _threadAllowed = new Semaphore(0,1);
        //list of allowed thread by effectname
        public static volatile Dictionary<string, bool> ThreadsConditions = new Dictionary<string, bool>();
        public static volatile Dictionary<string, bool> ThreadsStatus = new Dictionary<string, bool>();
        //association effect name => params (intensity, sleep)
        public static Dictionary<string, float[]> ThreadParams = new Dictionary<string, float[]>();
        //association effect name => effect
        public static Dictionary<string, string[]> ThreadCallbacks = new Dictionary<string, string[]>();
        // dictionary of all feedback patterns found in the bHaptics directory
        public static Dictionary<string, FileInfo> FeedbackMap = new Dictionary<string, FileInfo>();

#pragma warning disable CS0618 // remove warning that the C# library is deprecated
        public static HapticPlayer hapticPlayer;
#pragma warning restore CS0618 

        public static RotationOption defaultRotationOption = new RotationOption(0.0f, 0.0f);
        private static System.Timers.Timer aTimer;

        #region Initializers

        /**
         * If modVrEnabled harmony will use bhaptics patches
         * initializing suitdisabled according to config bhapticsEnabled
         * if disabled, not starting HapticPlayer
         * Every bhaptics patches return if suitdisabled true so it will be ok
         */
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
            catch
            {
                LogInfo("Suit initialization failed!");
                return;
            }
            RegisterAllTactFiles();
            LogInfo("Starting HeartBeat thread...");
            PlaybackHaptics("HeartBeat");
            SetTimer();
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
        #endregion


        #region PlayingHapticsEffects

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

        public static MyHitData getAngleAndShift(Player player, Vector3 hit)
        {
            // bhaptics starts in the front, then rotates to the left. 0° is front, 90° is left, 270° is right.
            // y is "up", z is "forward" in local coordinates
            Vector3 patternOrigin = new Vector3(0f, 0f, 1f);
            Vector3 hitPosition = hit - player.transform.position;
            Quaternion PlayerRotation = player.transform.rotation;
            Vector3 playerDir = PlayerRotation.eulerAngles;
            // get rid of the up/down component to analyze xz-rotation
            Vector3 flattenedHit = new Vector3(hitPosition.x, 0f, hitPosition.z);

            // get angle. .Net < 4.0 does not have a "SignedAngle" function...
            float earlyhitAngle = Vector3.Angle(flattenedHit, patternOrigin);
            // check if cross product points up or down, to make signed angle myself
            Vector3 earlycrossProduct = Vector3.Cross(flattenedHit, patternOrigin);
            if (earlycrossProduct.y > 0f) { earlyhitAngle *= -1f; }
            // relative to player direction
            float myRotation = earlyhitAngle - playerDir.y;
            // switch directions (bhaptics angles are in mathematically negative direction)
            myRotation *= -1f;
            // convert signed angle into [0, 360] rotation
            if (myRotation < 0f) { myRotation = 360f + myRotation; }

            // up/down shift is in y-direction
            float hitShift = hitPosition.y;
            //torso/player range in valheim
            float upperBound = 1.0f;
            float lowerBound = 0.0f;
            if (hitShift > upperBound) { hitShift = 0.5f; }
            else if (hitShift < lowerBound) { hitShift = -0.5f; }
            // ...and then spread/shift it to [-0.5, 0.5]
            else { hitShift = (hitShift - lowerBound) / (upperBound - lowerBound) - 0.5f; }
            // No tuple returns available in .NET < 4.0, so this is the easiest quickfix
            return new MyHitData(myRotation, hitShift);
        }

        public static void PlayBackHit(string key, float xzAngle, float yShift)
        {
            // two parameters can be given to the pattern to move it on the vest:
            // 1. An angle in degrees [0, 360] to turn the pattern to the left
            // 2. A shift [-0.5, 0.5] in y-direction (up and down) to move it up or down
            if (suitDisabled) { return; }
            if (FeedbackMap.ContainsKey(key))
            {
                ScaleOption scaleOption = new ScaleOption(1f, 1f);
                RotationOption rotationOption = new RotationOption(xzAngle, yShift);
                hapticPlayer.SubmitRegisteredVestRotation(key, key, rotationOption, scaleOption);
            }
            else
            {
                LogInfo("Feedback not registered: " + key);
            }
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
        public static void StartThreadHaptic(
            string EffectName,
            float intensity = 1.0f,
            bool timerNeeded = false,
            int sleep = 1000,
            float duration = 1.0f,
            int delayedStart = 0
            )
        {
            if (timerNeeded)
            {
                sleep = 200;
            }
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
            }
            else
            {
                //update params
                ThreadParams[EffectName][0] = intensity;
                ThreadParams[EffectName][1] = sleep;
                ThreadParams[EffectName][2] = duration;
            }
            //set thread condition true cause we are in start function
            setThreadsConditions(EffectName, true);
            //checking if thread is created and alive
            if (!ThreadsStatus.ContainsKey(EffectName) || !ThreadsStatus[EffectName])
            {
                Thread EffectThread = new Thread(() => ThreadHapticFunc(EffectName, delayedStart));
                EffectThread.Start();
            }
            //we still turn threadEnabled to false for other timerNeeded processes
            threadEnabled = false;
        }

        /**
         * Resets the thread condition to tell the corresponding
         * Thread to stop
         */
        public static void StopThreadHaptic(string name, string[] callback = null)
        {
            if (ThreadsStatus.ContainsKey(name) && ThreadsStatus[name])
            {
                StopHapticFeedback(name);
                if (callback != null)
                {
                    setThreadCallbacks(name, callback);
                }
                setThreadsConditions(name, false);
            }
        }

        /**
         * Stop a thread but with delay
         */
        public static void StopThreadHapticDelayed(string name, int delay)
        {
            if (ThreadsStatus.ContainsKey(name) && ThreadsStatus[name])
            {
                setThreadsStatus(name, true);
                Thread EffectThread = new Thread(() =>
                {
                    Thread.Sleep(delay);
                    StopHapticFeedback(name);
                    setThreadsConditions(name, false);
                    setThreadsStatus(name, false);

                });
                EffectThread.Start();
            }
        }

        public static void StopHapticFeedback(string effect)
        {
            lock (hapticPlayer)
            {
                hapticPlayer.TurnOff(effect);
            }
        }

        public static void StopAllHapticFeedback(string[] exceptions = null)
        {
            lock (ThreadsConditions)
            {
                foreach (string name in ThreadsConditions.Keys)
                {
                    setThreadsConditions(name, false);
                }
            }
            lock (FeedbackMap)
            {
                foreach (string key in FeedbackMap.Keys)
                {
                    if (exceptions == null || !exceptions.Contains(key))
                    {
                        StopHapticFeedback(key);
                    }
                }
            }
        }

        /**
         * Thread function executing haptic effect every sleep value
         * while corresponding name condition is not false
         */
        public static void ThreadHapticFunc(string name, int delayedStart = 0)
        {
            try
            {
                //thread is alive
                setThreadsStatus(name, true);
                if (delayedStart != 0)
                {
                    Thread.Sleep(delayedStart);
                }
                //if false, stops the thread by making it finish
                while (ThreadsConditions[name])
                {
                    PlaybackHaptics(name, ThreadParams[name][0], ThreadParams[name][2]);
                    int sleep = (int)ThreadParams[name][1];
                    Thread.Sleep(sleep == 0 ? 1000 : sleep);
                }
            }
            finally
            {
                //thread is dead
                setThreadsStatus(name, false);
                //if callback exists
                if (ThreadCallbacks.ContainsKey(name))
                {
                    foreach ( string eff in ThreadCallbacks[name])
                    {
                        PlaybackHaptics(eff);
                    }
                    removeThreadCallbacks(name);
                }
            }
        }
        #endregion

        #region Setters
        public static void setThreadCallbacks(string name, string[] effect)
        {
            lock (ThreadCallbacks)
            {
                if (ThreadCallbacks.ContainsKey(name))
                {
                    ThreadCallbacks[name] = effect;
                }
                else
                {
                    ThreadCallbacks.Add(name, effect);
                }
            }
        }
        public static void setThreadsStatus(string name, bool value)
        {
            lock (ThreadsStatus)
            {
                if (ThreadsStatus.ContainsKey(name))
                {
                    ThreadsStatus[name] = value;
                }
                else
                {
                    ThreadsStatus.Add(name, value);
                }
            }
        }

        public static void setThreadsConditions(string name, bool value)
        {
            lock (ThreadsConditions)
            {
                if (ThreadsConditions.ContainsKey(name))
                {
                    ThreadsConditions[name] = value;
                }
                else
                {
                    ThreadsConditions.Add(name, value);
                }
            }
        }

        public static void removeThreadCallbacks(string name)
        {
            lock (ThreadCallbacks)
            {
                ThreadCallbacks.Remove(name);
            }
        }
        #endregion
    }

    public class MyHitData
    {
        public float angle;
        public float shift;

        public MyHitData(float hitAngle, float hitShift)
        {
            angle = hitAngle;
            shift = hitShift;
        }
    }
}
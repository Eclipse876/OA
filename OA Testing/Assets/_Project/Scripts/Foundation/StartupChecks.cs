using System;
using System.Diagnostics;
using System.Threading;
using UnityEngine;
using UnityEngine.EventSystems;

namespace OA.Foundation
{
    
    public static class StartupChecks
    {
        public static void RunBasicChecks()
        {
            CheckTimeScale();
            CheckMainCamera();
            CheckEventSystem();
        }

        private static void CheckTimeScale()
        {
            if (Time.timescale <= 0f)
            {
                Debug.LogWarning("[StartupChecks] Be Advised! Time.timeScale is 0. The game is paused at startup.");
            }
        }

        private static void CheckMainCamera()
        {
            if (Camera.main == null)
            {
                Debug.LogWarning("[StartupChecks] No camera tagged 'MainCamera' was found.");
            }
        }

        private static void CheckEventSystem()
        {
            EventSystem eventSystem = Object.FindObjectOfType<EventSystem>();

            if (eventSystem == null)
            {
                Debug.LogWarning("[StartupChecks] Be Advised! No 'EventSystem' found. Safe to ignore if there is no UI yet.");
            }
            
        }
    }
}
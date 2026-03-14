using UnityEngine;
using UnityEngine.SceneManagement;
 
namespace OA.Foundation
{
    public sealed class GameBootstrap : MonoBehaviour
    {
        [SerializeField] private bool runOnStart = true;
 
        private void Start()
        {
            if (!runOnStart)
                return;
            
            Boot();
        }
 
        public void Boot()
        {
            Debug.Log("[Bootstrap] Starting game bootstrap...");
 
            StartupChecks.RunBasicChecks();
 
            SceneValidator sceneValidator = FindFirstObjectOfType<SceneValidator>();
 
            if (sceneValidator != null)
            {
                bool isValid = sceneValidator.ValidateScene();

                if (!isValid)
                {
                    Debug.LogError("[Bootstrap] Startup halted. Scene validation failed.");
                    return;
                }
            }
            else
            {
                Debug.LogWarning(
                    $"[Bootstrap] No SceneValidator was found in scene '{SceneManager.GetActiveScene().name}'.");
            }
            
            Debug.Log("[Bootstrap] Startup complete.");
 
        }  
 
    }
 
}
using UnityEngine;
using UnityEngine.SceneManagement;

public class SceneSwitcher : MonoBehaviour
{
    void Update()
    {
        // Press 1 -> Loads Scene 0
        if (Input.GetKeyDown(KeyCode.Alpha1))
        {
            SceneManager.LoadScene(0);
        }
        // Press 2 -> Loads Scene 1
        else if (Input.GetKeyDown(KeyCode.Alpha2))
        {
            SceneManager.LoadScene(1);
        }
        // Press 3 -> Loads Scene 2
        else if (Input.GetKeyDown(KeyCode.Alpha3))
        {
            SceneManager.LoadScene(2);
        }
        // Press 4 -> Loads Scene 3
        else if (Input.GetKeyDown(KeyCode.Alpha4))
        {
            SceneManager.LoadScene(3);
        }
        // Press 5 -> Loads Scene 4
        else if (Input.GetKeyDown(KeyCode.Alpha5))
        {
            SceneManager.LoadScene(4);
        }
    }
}
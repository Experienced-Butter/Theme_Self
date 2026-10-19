using UnityEngine;

public class GameManager : MonoBehaviour
{
    [Header("UI Panels")]
    public GameObject mainMenuPanel;
    public GameObject dialoguePanel;

    void Start()
    {
        // Do NOT setactive panels here, control in inspector
    }

    public void StartGame()
    {
        mainMenuPanel.SetActive(false);
        dialoguePanel.SetActive(true);
    }
}

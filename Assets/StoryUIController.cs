using UnityEngine;
using TMPro;

public class StoryUIController : MonoBehaviour
{
    public TMP_Text tmpSpeaker;
    public TMP_Text tmpContent;

    public void RefreshText(string speaker, string content)
    {
        tmpSpeaker.text = speaker;
        tmpContent.text = content;
    }
}

using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public enum GameState
{
    Menu,       // 主菜单
    StoryDialogue, // 剧情对话阶段
    CardGame,   // 卡牌关卡
    RhythmGame, // 音游关卡
    Pause,      // 暂停
    End         // 全部流程结束
}

public class GameManager : MonoBehaviour
{
    public static GameManager Instance;
    public GameState currentState;

    [Header("剧情顺序列表，按顺序执行")]
    public List<StoryNode> storySequence;
    private int currentIndex = 0;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    void Start()
    {
        currentIndex = 0;
        EnterStoryDialogue();
    }

    void Update()
    {

    }

    // 进入剧情对话
    void EnterStoryDialogue()
    {
        currentState = GameState.StoryDialogue;
        StoryNode node = storySequence[currentIndex];
        UnityEngine.Debug.Log($"【剧情】{node.dialogue}");
    }

    // 玩家看完剧情，点击继续，进入对应关卡
    public void ContinueAfterStory()
    {
        StoryNode node = storySequence[currentIndex];
        if (node.isCardLevel)
        {
            currentState = GameState.CardGame;
            UnityEngine.Debug.Log("进入【卡牌关卡】");
        }
        else
        {
            currentState = GameState.RhythmGame;
            UnityEngine.Debug.Log("进入【音游关卡】");
        }
    }

    // 当前关卡通关后，调用这个函数，进入下一段剧情
    public void LevelComplete()
    {
        currentIndex++;
        if (currentIndex >= storySequence.Count)
        {
            currentState = GameState.End;
            UnityEngine.Debug.Log("✅ 全部剧情与关卡通关，游戏结束");
            return;
        }
        // 下一段剧情
        EnterStoryDialogue();
    }
}

[System.Serializable]
public class StoryNode
{
    [Tooltip("true=卡牌关卡，false=音游关卡")]
    public bool isCardLevel;
    [Tooltip("本段剧情文本")]
    [TextArea] public string dialogue;
}

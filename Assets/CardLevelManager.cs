using UnityEngine;
using System.Collections.Generic;

public class CardLevelManager : MonoBehaviour
{
    public static CardLevelManager Instance;

    [Header("关卡配置")]
    public int currentLevel = 1;
    public List<GameObject> cardPrefabList;
    public Transform cardContainer;

    [Header("关卡状态")]
    public bool levelRunning;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    public void StartLevel()
    {
        levelRunning = true;
        ClearAllCards();
        SpawnCardsForLevel();
    }

    void SpawnCardsForLevel()
    {
        ClearAllCards();
        int cardAmount = 4 + (currentLevel - 1) * 2;

        for (int i = 0; i < cardAmount; i++)
        {
            GameObject prefab = cardPrefabList[Random.Range(0, cardPrefabList.Count)];
            Instantiate(prefab, cardContainer);
        }
    }

    public void ClearAllCards()
    {
        if (cardContainer == null) return;
        for (int i = cardContainer.childCount - 1; i >= 0; i--)
        {
            Destroy(cardContainer.GetChild(i).gameObject);
        }
    }

    public void LevelComplete()
    {
        levelRunning = false;
        currentLevel++;
        Debug.Log("关卡完成，下一关：" + currentLevel);
    }

    public void LevelFailed()
    {
        levelRunning = false;
        Debug.Log("关卡失败");
    }

    public void ResetCurrentLevel()
    {
        ClearAllCards();
        StartLevel();
    }
}

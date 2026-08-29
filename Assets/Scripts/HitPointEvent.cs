using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class HitPointEvent : MonoBehaviour
{
    private bool gameStart;
    private float startTime;
    private float timeVal;
    public GameObject[] points;
    private int hitNum = 0;

    // Start is called before the first frame update
    void Start()
    {
        gameStart = true;
        startTime = Time.time;
    }

    // Update is called once per frame
    void Update()
    {
        if (gameStart)
        {
            if (Time.time - startTime >= 6)
            {
                //游戏结束
                gameStart = false;
                if (hitNum >= 2)
                {
                    //游戏胜利
                    GameManager.Instance.LoadNextScript(2);
                }
                else
                {
                    //游戏失败
                    GameManager.Instance.LoadNextScript();
                }
                gameObject.SetActive(false);
            }
            else
            {
                //游戏进行
                timeVal -= Time.deltaTime;
                if (timeVal < 0)
                {
                    ShowPoints();
                    timeVal = 2;
                }
            }
        }
    }
    private void ShowPoints()
    {
        for (int i = 0; i < points.Length; i++)
        {
            points[i].SetActive(false);
        }
        GameObject point = points[Random.Range(0, points.Length)];
        point.SetActive(true);
        Image[] imags = point.GetComponentsInChildren<Image>();
        for (int i = 0; i < imags.Length; i++)
        {
            imags[i].color = new Color(imags[i].color.r, imags[i].color.g, imags[i].color.b, 0);
        }
        UIManager.Instance.DoShowOrHideUITeen(true, false, 2, imags);
    }
    public void HitPoint(GameObject obj)
    {
        AudioSourceManager.Instance.PlaySound("Hit");
        hitNum++;
        obj.SetActive(false);
    }

}

using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class FreeActivities : MonoBehaviour
{
    private Dictionary<int, System.Action> chooseActions;
    // Start is called before the first frame update
    void Start()
    {
        ShowChoiceUI();
        chooseActions = new Dictionary<int, System.Action>()
        {
            { 1, ChooseTest },
            { 2, ChooseDebug },
            { 3, FinishActivities}
        };
    }

    // Update is called once per frame
    void Update()
    {

    }
    private void ShowChoiceUI()
    {
        UIManager.Instance.ShowChoiceUI(3, new string[] { "Test", "Debug", "结束今天的活动" }, ChooseCharacter);
    }
    private void ChooseTest()
    {
        UIManager.Instance.empChoiceUIGo.SetActive(false);
        GameManager.Instance.LoadNextScript();
    }

    public void ChooseDebug()
    {
        Debug.Log("选择Debug");
    }

    public void FinishActivities()
    {
        UIManager.Instance.empChoiceUIGo.SetActive(false);
        GameManager.Instance.LoadNextScript();
    }
    public void ChooseCharacter(object chooseID)
    {
        chooseActions[(int)chooseID]();
    }
}

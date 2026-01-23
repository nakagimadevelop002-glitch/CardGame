using KanKikuchi.AudioManager;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class TitleManager : MonoBehaviour
{
    [SerializeField] private string _loadScene; //シーン名を記述
    [SerializeField] Text scoreText;

    public void SceneChange()
    {
        SceneManager.LoadScene(_loadScene);
    }
    public void OnAttack(InputValue value)
    {
        SceneChange();
    }
    public void OnJump(InputValue value)
    {
        SceneChange();
    }
    public void OnNext(InputValue value)
    {
        SceneChange();
    }
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        BGMManager.Instance.Play(BGMPath.FANTASY14);
        
        int currentScore = 0;
        int aiScore = 0;


#if UNITY_SWITCH && !UNITY_EDITOR
        int.TryParse(NintendoSaveManager.LoadSlot("PlayerScore"), out currentScore);
        int.TryParse(NintendoSaveManager.LoadSlot("AIScore"), out aiScore);
#else
        currentScore = PlayerPrefs.GetInt("PlayerScore");
        aiScore = PlayerPrefs.GetInt("AIScore");
#endif
        if (currentScore == 0)
        {
            scoreText.text = "";
        }
        else
        {
            scoreText.text = $"成績：Player:{currentScore}-AI:{aiScore}";
        }

    }

    // Update is called once per frame
    void Update()
    {
        
    }
}

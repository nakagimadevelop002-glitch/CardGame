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

        int currentScore = PlayerPrefs.GetInt("PlayerScore");
        int aiScore = PlayerPrefs.GetInt("AIScore");
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

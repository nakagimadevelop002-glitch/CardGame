using UnityEngine;
using KanKikuchi.AudioManager;
public class MainGameBootstrapper : MonoBehaviour
{
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        BGMManager.Instance.Play(BGMPath.BGM1);
        ResearchTCG.Bootstrapper.Init();
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}

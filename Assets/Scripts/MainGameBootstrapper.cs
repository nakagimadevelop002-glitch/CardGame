using UnityEngine;

public class MainGameBootstrapper : MonoBehaviour
{
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        ResearchTCG.Bootstrapper.Init();
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}

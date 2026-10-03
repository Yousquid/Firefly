using UnityEngine;

namespace Firefly
{
    public sealed class FireflyExperience : MonoBehaviour
    {
        [SerializeField] private FlightInput input;
        [SerializeField] private FireflyGlow glow;
        [SerializeField] private FireflySwarm swarm;
        public void Configure(FlightInput controls,FireflyGlow lantern,FireflySwarm fireflies)
        {
            input=controls; glow=lantern; swarm=fireflies;
        }
        private void Awake()
        {
            Application.targetFrameRate=60;
            if (input) input.PauseChanged+=OnPause;
            if (swarm) swarm.ClearingAnswered+=OnAnswer;
        }
        private void OnPause(bool paused) { Time.timeScale=paused?0:1; AudioListener.pause=paused; }
        private void OnAnswer(int clearing) { if (input) input.PlayGlowHaptics(true); }
        private void OnDisable()
        {
            if (input) input.PauseChanged-=OnPause;
            if (swarm) swarm.ClearingAnswered-=OnAnswer;
            Time.timeScale=1; AudioListener.pause=false;
        }
    }
}

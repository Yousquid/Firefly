using UnityEngine;

namespace Firefly
{
    /// <summary>Original synthesized ambience. Replace the layers with field recordings for final art.</summary>
    public sealed class MeadowSoundscape : MonoBehaviour
    {
        [SerializeField] private FireflySwarm swarm;
        [SerializeField] private Transform player;
        private AudioSource ambience, wings, response;
        private AudioClip ambientClip, wingClip, responseClip;

        public void Configure(FireflySwarm fireflies,Transform target) { swarm=fireflies; player=target; }
        private void Awake()
        {
            ambience=Layer("Wind and crickets",transform,0.42f,0);
            ambientClip=CreateAmbience(); ambience.clip=ambientClip; ambience.Play();
            wings=Layer("Small wings",player ? player : transform,0.095f,0.8f);
            wingClip=CreateWings(); wings.clip=wingClip; wings.Play();
            response=Layer("Firefly answer",transform,0.18f,0); response.loop=false;
            responseClip=CreateAnswer(); response.clip=responseClip;
            if (swarm) swarm.ClearingAnswered+=OnAnswer;
        }
        private static AudioSource Layer(string name,Transform parent,float volume,float spatial)
        {
            var go=new GameObject(name); go.transform.SetParent(parent,false);
            var source=go.AddComponent<AudioSource>(); source.loop=true; source.playOnAwake=false;
            source.volume=volume; source.spatialBlend=spatial; source.minDistance=1; source.maxDistance=18;
            return source;
        }
        private void OnAnswer(int clearing) { if (response) response.Play(); }
        private static AudioClip CreateAmbience()
        {
            const int rate=22050, seconds=18; var data=new float[rate*seconds*2];
            var random=new System.Random(8071); float noiseLeft=0,noiseRight=0;
            for (int i=0;i<rate*seconds;i++)
            {
                float t=(float)i/rate;
                noiseLeft=Mathf.Lerp(noiseLeft,(float)random.NextDouble()*2-1,0.013f);
                noiseRight=Mathf.Lerp(noiseRight,(float)random.NextDouble()*2-1,0.013f);
                float breeze=0.55f+0.25f*Mathf.Sin(t*Mathf.PI*2/seconds);
                float cricket=0;
                for (int voice=0;voice<3;voice++)
                {
                    float phase=Mathf.Repeat(t+voice*2.7f,4.5f);
                    float gate=phase<0.75f ? Mathf.Pow(Mathf.Sin(phase/0.75f*Mathf.PI),2)*Mathf.Pow(Mathf.Max(0,Mathf.Sin(t*46+voice)),4) : 0;
                    cricket+=Mathf.Sin(t*(3400+voice*410)*Mathf.PI*2)*gate*0.015f;
                }
                float edge=Mathf.Min(Mathf.Clamp01(t/0.15f),Mathf.Clamp01((seconds-t)/0.15f));
                data[i*2]=(noiseLeft*breeze*0.34f+cricket)*edge;
                data[i*2+1]=(noiseRight*breeze*0.34f+cricket*0.7f)*edge;
            }
            var clip=AudioClip.Create("Procedural meadow evening",rate*seconds,2,rate,false); clip.SetData(data,0); return clip;
        }
        private static AudioClip CreateWings()
        {
            const int rate=22050; var data=new float[rate*2];
            for (int i=0;i<data.Length;i++)
            {
                float t=(float)i/rate; data[i]=(Mathf.Sin(t*85*Mathf.PI*2)+0.35f*Mathf.Sin(t*170*Mathf.PI*2))*0.045f;
            }
            var clip=AudioClip.Create("Wing flutter",data.Length,1,rate,false); clip.SetData(data,0); return clip;
        }
        private static AudioClip CreateAnswer()
        {
            const int rate=22050; var data=new float[(int)(rate*1.6f)];
            for (int i=0;i<data.Length;i++)
            {
                float t=(float)i/rate; float envelope=Mathf.Exp(-t*3.7f)*Mathf.Clamp01(t/0.035f);
                data[i]=(Mathf.Sin(t*660*Mathf.PI*2)+0.4f*Mathf.Sin(t*990*Mathf.PI*2))*envelope*0.1f;
            }
            var clip=AudioClip.Create("Soft answer",data.Length,1,rate,false); clip.SetData(data,0); return clip;
        }
        private void OnDestroy()
        {
            if (swarm) swarm.ClearingAnswered-=OnAnswer;
            if (ambientClip) Destroy(ambientClip); if (wingClip) Destroy(wingClip); if (responseClip) Destroy(responseClip);
        }
    }
}
